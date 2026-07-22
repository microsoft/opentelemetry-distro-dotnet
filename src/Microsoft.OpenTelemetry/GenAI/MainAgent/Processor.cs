// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Microsoft.OpenTelemetry.GenAI.MainAgent
{
    /// <summary>
    /// Span and log-record processors that propagate <c>microsoft.gen_ai.main_agent.*</c>
    /// attributes from the top-level (user-facing) GenAI agent so that all downstream
    /// telemetry is attributed to the main agent rather than internal sub-agents in a
    /// multi-agent system.
    /// </summary>
    internal static class GenAIMainAgentPropagation
    {
        // Each row: (target attribute on current activity,
        //            primary source attribute on parent activity,
        //            fallback source attribute on parent activity)
        internal static readonly (string Target, string Primary, string Fallback)[] PropagationTable =
        {
            (OpenTelemetryConstants.GenAiMainAgentNameKey,
                OpenTelemetryConstants.GenAiMainAgentNameKey,
                OpenTelemetryConstants.GenAiAgentNameKey),
            (OpenTelemetryConstants.GenAiMainAgentIdKey,
                OpenTelemetryConstants.GenAiMainAgentIdKey,
                OpenTelemetryConstants.GenAiAgentIdKey),
            (OpenTelemetryConstants.GenAiMainAgentVersionKey,
                OpenTelemetryConstants.GenAiMainAgentVersionKey,
                OpenTelemetryConstants.GenAiAgentVersionKey),
            (OpenTelemetryConstants.GenAiMainAgentConversationIdKey,
                OpenTelemetryConstants.GenAiMainAgentConversationIdKey,
                OpenTelemetryConstants.GenAiConversationIdKey),
        };

        // Used at OnEnd to copy the current activity's own gen_ai.* attributes onto the
        // microsoft.gen_ai.main_agent.* attributes when the activity is the top-level
        // invoke_agent span and no main_agent.* attribute has been propagated yet.
        internal static readonly (string Target, string Fallback)[] SelfCopyTable =
        {
            (OpenTelemetryConstants.GenAiMainAgentNameKey, OpenTelemetryConstants.GenAiAgentNameKey),
            (OpenTelemetryConstants.GenAiMainAgentIdKey, OpenTelemetryConstants.GenAiAgentIdKey),
            (OpenTelemetryConstants.GenAiMainAgentVersionKey, OpenTelemetryConstants.GenAiAgentVersionKey),
            (OpenTelemetryConstants.GenAiMainAgentConversationIdKey, OpenTelemetryConstants.GenAiConversationIdKey),
        };

        // Project-scope attributes, so all telemetry is attributed to the same project.
        internal static readonly string[] ProjectIdKeys = OpenTelemetryConstants.GenAiProjectIdKeys;
    }

    /// <summary>
    /// Propagates <c>microsoft.gen_ai.main_agent.*</c> attributes onto activities.
    /// <para>
    /// On <see cref="OnStart"/>: copies main-agent attributes from the parent activity
    /// (or falls back to the parent's <c>gen_ai.agent.*</c> / <c>gen_ai.conversation.id</c>
    /// attributes) onto the new activity.
    /// </para>
    /// <para>
    /// On <see cref="OnEnd"/>: when the activity is itself an <c>invoke_agent</c>
    /// operation and has not already been enriched, copies its own <c>gen_ai.agent.*</c>
    /// / <c>gen_ai.conversation.id</c> attributes onto
    /// <c>microsoft.gen_ai.main_agent.*</c> so the top-level agent identifies itself as
    /// the main agent. For other activities that still lack
    /// <c>microsoft.gen_ai.main_agent.*</c> attributes, a fallback read from the (now
    /// potentially enriched) parent is attempted.
    /// </para>
    /// </summary>
    internal class GenAIMainAgentSpanProcessor : BaseProcessor<Activity>
    {
        /// <inheritdoc/>
        public override void OnStart(Activity activity)
        {
            if (activity == null)
            {
                return;
            }

            var parent = activity.Parent;
            if (parent == null)
            {
                return;
            }

            foreach (var (target, primary, fallback) in GenAIMainAgentPropagation.PropagationTable)
            {
                var value = parent.GetTagItem(primary) ?? parent.GetTagItem(fallback);
                if (value != null)
                {
                    activity.SetTag(target, value);
                }
            }

            foreach (var key in GenAIMainAgentPropagation.ProjectIdKeys)
            {
                var value = parent.GetTagItem(key);
                if (value != null)
                {
                    activity.SetTag(key, value);
                }
            }
        }

        /// <inheritdoc/>
        public override void OnEnd(Activity activity)
        {
            if (activity == null)
            {
                return;
            }

            var parent = activity.Parent;

            var hasMainAgentAttribute = false;
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Key.StartsWith(OpenTelemetryConstants.GenAiMainAgentAttributePrefix, StringComparison.Ordinal))
                {
                    hasMainAgentAttribute = true;
                    break;
                }
            }

            // Main-agent enrichment — skipped when the activity is already enriched.
            if (!hasMainAgentAttribute)
            {
                // Self-promotion: top-level invoke_agent activities copy their own
                // gen_ai.agent.* → microsoft.gen_ai.main_agent.*
                //
                // "Top-level" means this invoke_agent is not nested under another
                // invoke_agent. We guard against a nested invoke_agent (whose parent
                // was never enriched — e.g. parent tags stamped after this OnEnd, or
                // parent produced by an untracked pipeline) incorrectly claiming to
                // be the main agent by requiring the immediate parent to either be
                // absent or not itself an invoke_agent.
                if (activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) is string opName &&
                    string.Equals(opName, OpenTelemetryConstants.InvokeAgentOperationName, StringComparison.Ordinal) &&
                    !IsParentInvokeAgent(parent))
                {
                    foreach (var (target, source) in GenAIMainAgentPropagation.SelfCopyTable)
                    {
                        var value = activity.GetTagItem(source);
                        if (value != null)
                        {
                            activity.SetTag(target, value);
                        }
                    }
                }

                // Fallback propagation: re-read from the parent activity whose attributes
                // may have been set after this child was created (timing issue).
                if (parent != null)
                {
                    foreach (var (target, primary, fallback) in GenAIMainAgentPropagation.PropagationTable)
                    {
                        var value = parent.GetTagItem(primary) ?? parent.GetTagItem(fallback);
                        if (value != null)
                        {
                            activity.SetTag(target, value);
                        }
                    }
                }
            }

            // Project-id fallback: handles parents stamped after child start.
            if (parent != null)
            {
                foreach (var key in GenAIMainAgentPropagation.ProjectIdKeys)
                {
                    if (activity.GetTagItem(key) == null)
                    {
                        var value = parent.GetTagItem(key);
                        if (value != null)
                        {
                            activity.SetTag(key, value);
                        }
                    }
                }
            }
        }

        // Returns true when the immediate parent activity is itself a
        // gen_ai invoke_agent span — signalling that the current activity is a
        // nested invocation and therefore not the main (top-level) agent.
        private static bool IsParentInvokeAgent(Activity? parent)
        {
            return parent?.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) is string parentOp &&
                string.Equals(parentOp, OpenTelemetryConstants.InvokeAgentOperationName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Copies any <c>microsoft.gen_ai.main_agent.*</c> attributes (and the Foundry
    /// project-id attributes) from the current activity onto every emitted log record.
    /// </summary>
    internal class GenAIMainAgentLogRecordProcessor : BaseProcessor<LogRecord>
    {
        /// <inheritdoc/>
        public override void OnEnd(LogRecord data)
        {
            if (data == null)
            {
                return;
            }

            var activity = Activity.Current;
            if (activity == null)
            {
                return;
            }

            List<KeyValuePair<string, object?>>? mainAgentAttributes = null;
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Key.StartsWith(OpenTelemetryConstants.GenAiMainAgentAttributePrefix, StringComparison.Ordinal) ||
                    Array.IndexOf(GenAIMainAgentPropagation.ProjectIdKeys, tag.Key) >= 0)
                {
                    mainAgentAttributes ??= new List<KeyValuePair<string, object?>>();
                    mainAgentAttributes.Add(tag);
                }
            }

            if (mainAgentAttributes == null)
            {
                return;
            }

            // Merge without duplicating keys the log record already carries.
            // Attributes on the log record are treated as authoritative — any
            // main-agent / project-id key explicitly set by the caller wins over
            // the value read from the ambient Activity. Appending duplicates
            // would produce two entries with the same key, which downstream
            // exporters and log processors handle inconsistently.
            HashSet<string>? existingKeys = null;
            if (data.Attributes != null)
            {
                existingKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kvp in data.Attributes)
                {
                    existingKeys.Add(kvp.Key);
                }
            }

            var merged = new List<KeyValuePair<string, object?>>();
            if (data.Attributes != null)
            {
                merged.AddRange(data.Attributes);
            }

            foreach (var kvp in mainAgentAttributes)
            {
                if (existingKeys != null && existingKeys.Contains(kvp.Key))
                {
                    continue;
                }
                merged.Add(kvp);
            }

            data.Attributes = merged;
        }
    }
}
