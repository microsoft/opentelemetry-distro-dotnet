// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Microsoft.OpenTelemetry.Agent365.Tracing.Processors
{
    using Microsoft.OpenTelemetry.Agent365.Common;
    using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;
    using global::OpenTelemetry;
    using System.Diagnostics;

    /// <summary>
    /// Processes activity telemetry data by adding contextual baggage information.
    /// </summary>
    internal sealed class ActivityProcessor : BaseProcessor<Activity>
    {
        private static readonly string[] AttributeKeys = new[]
        {
            OpenTelemetryConstants.GenAiAgentIdKey,
            OpenTelemetryConstants.GenAiAgentNameKey,
            OpenTelemetryConstants.GenAiAgentDescriptionKey,
            OpenTelemetryConstants.GenAiAgentVersionKey,
            OpenTelemetryConstants.AgentEmailKey,
            OpenTelemetryConstants.AgentBlueprintIdKey,
            OpenTelemetryConstants.AgentAUIDKey,
            OpenTelemetryConstants.AgentPlatformIdKey,
            OpenTelemetryConstants.TenantIdKey,
            OpenTelemetryConstants.GenAiConversationIdKey,
            OpenTelemetryConstants.GenAiConversationItemLinkKey,
            OpenTelemetryConstants.GenAiInputMessagesKey,
            OpenTelemetryConstants.GenAiOutputMessagesKey,
            OpenTelemetryConstants.GenAiToolCallResultKey,
            OpenTelemetryConstants.GenAiToolNameKey,
            OpenTelemetryConstants.GenAiToolCallIdKey,
            OpenTelemetryConstants.GenAiToolDescriptionKey,
            OpenTelemetryConstants.GenAiToolArgumentsKey,
            OpenTelemetryConstants.GenAiToolTypeKey,
            OpenTelemetryConstants.GenAiProviderNameKey,
            OpenTelemetryConstants.SessionIdKey,
            OpenTelemetryConstants.SessionDescriptionKey,
            OpenTelemetryConstants.ChannelNameKey,
            OpenTelemetryConstants.ChannelLinkKey,
            OpenTelemetryConstants.UserIdKey,
            OpenTelemetryConstants.UserNameKey,
            OpenTelemetryConstants.UserEmailKey,
            OpenTelemetryConstants.CallerClientIpKey,
        };

        private static readonly string[] InvokeAgentAttributeKeys = new[]
        {
            OpenTelemetryConstants.ServerAddressKey,
            OpenTelemetryConstants.ServerPortKey,
        };

        /// <summary>
        /// Called when an activity starts, adds tags for attributes listed in AttributeKeys.
        /// </summary>
        /// <param name="activity">The activity that is starting.</param>
        public override void OnStart(Activity activity)
        {
            // Set telemetry SDK attributes
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkNameKey, OpenTelemetryConstants.TelemetrySdkNameValue);
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkLanguageKey, OpenTelemetryConstants.TelemetrySdkLanguageValue);
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkVersionKey, OpenTelemetryConstants.TelemetrySdkVersionValue);

            foreach (var key in AttributeKeys)
            {
                var baggageValue = Baggage.Current.GetBaggage(key);
                if (key == OpenTelemetryConstants.GenAiAgentIdKey && !string.IsNullOrEmpty(baggageValue))
                {
                    // Force overwrite gen_ai.agent.id from baggage — the baggage value is the
                    // A365 platform identity set by agent middleware. Without this, the AI framework's
                    // internal agent ID would be used, causing export to an unregistered identity.
                    activity.SetTag(key, baggageValue);
                }
                else
                {
                    activity.CoalesceTag(key, baggageValue);
                }
            }

            if (activity.OperationName == InvokeAgentScope.OperationName ||
                (activity.DisplayName != null && activity.DisplayName.StartsWith(InvokeAgentScope.OperationName)))
            {
                foreach (var key in InvokeAgentAttributeKeys)
                {
                    activity.CoalesceTag(key, Baggage.Current.GetBaggage(key));
                }
            }

            base.OnStart(activity);
        }
    }
}
