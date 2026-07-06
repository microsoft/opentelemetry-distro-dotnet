// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using global::OpenTelemetry;
using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.Agents.A365.Observability.Runtime.Common
{
    /// <summary>
    /// Per request baggage builder
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see href="https://learn.microsoft.com/microsoft-agent-365/developer/observability?tabs=dotnet#baggage-attributes">Learn more about baggage attributes</see>
    /// </para>
    /// <para>
    /// <b>Certification Requirements:</b> To ensure the agent passes certification, the following properties must be set using their respective methods:
    /// <list type="bullet">
    ///   <item><see cref="TenantId"/></item>
    ///   <item><see cref="ConversationId"/></item>
    ///   <item><see cref="ChannelName"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Scope-Specific Recommendations:</b> The following properties are typically set at individual scopes and should be applied as appropriate:
    /// <list type="bullet">
    ///   <item><see cref="AgentId"/></item>
    ///   <item><see cref="AgentName"/></item>
    ///   <item><see cref="AgentDescription"/></item>
    ///   <item><see cref="AgenticUserId"/></item>
    ///   <item><see cref="AgenticUserEmail"/></item>
    ///   <item><see cref="AgentBlueprintId"/></item>
    ///   <item><see cref="UserId"/></item>
    ///   <item><see cref="UserEmail"/></item>
    ///   <item><see cref="UserName"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// <see href="https://go.microsoft.com/fwlink/?linkid=2344479">Learn more about certification requirements</see>
    /// </para>
    /// </remarks>
    public class BaggageBuilder
    {
        private sealed class Scope : IDisposable
        {
            private readonly Baggage _previous;
            private bool _disposed;
            public Scope(Baggage prev) => _previous = prev;
            public void Dispose()
            {
                if (_disposed) return;
                Baggage.Current = _previous;
                _disposed = true;
            }
        }

        private readonly Dictionary<string, string?> _pairs = new Dictionary<string, string?>();

        private readonly HashSet<string> _customKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Sets the tenant ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property must be set for the agent to pass certification requirements.
        /// </remarks>
        public BaggageBuilder TenantId(string? v)
        { 
            Set(OpenTelemetryConstants.TenantIdKey, v); 
            return this;
        }

        /// <summary>
        /// Sets the agent ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgentId(string? v)
        { 
            Set(OpenTelemetryConstants.GenAiAgentIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agent name baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgentName(string? v)
        {
            Set(OpenTelemetryConstants.GenAiAgentNameKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agent description baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgentDescription(string? v)
        {
            Set(OpenTelemetryConstants.GenAiAgentDescriptionKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agent version baggage value.
        /// </summary>
        public BaggageBuilder AgentVersion(string? v)
        {
            Set(OpenTelemetryConstants.GenAiAgentVersionKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agentic user ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgenticUserId(string? v)
        { 
            Set(OpenTelemetryConstants.AgentAUIDKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agentic user email baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgenticUserEmail(string? v)
        { 
            Set(OpenTelemetryConstants.AgentEmailKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agent blueprint ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder AgentBlueprintId(string? v)
        { 
            Set(OpenTelemetryConstants.AgentBlueprintIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the agent platform ID baggage value.
        /// </summary>
        public BaggageBuilder AgentPlatformId(string? v)
        {
            Set(OpenTelemetryConstants.AgentPlatformIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the user ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder UserId(string? v)
        { 
            Set(OpenTelemetryConstants.UserIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the user email baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder UserEmail(string? v)
        {
            Set(OpenTelemetryConstants.UserEmailKey, v);
            return this;
        }

        /// <summary>
        /// Sets the user name baggage value.
        /// </summary>
        /// <remarks>
        /// This property should be set to pass certification, but is typically set at individual scopes.
        /// </remarks>
        public BaggageBuilder UserName(string? v)
        {
            Set(OpenTelemetryConstants.UserNameKey, v);
            return this;
        }

        /// <summary>
        /// Sets the user client IP baggage value.
        /// </summary>
        public BaggageBuilder UserClientIp(IPAddress v)
        {
            Set(OpenTelemetryConstants.CallerClientIpKey, v.ToString());
            return this;
        }

        /// <summary>
        /// Sets the invoke agent server address and port baggage values.
        /// </summary>
        /// <param name="address">The server address (hostname) of the target agent service.</param>
        /// <param name="port">Optional server port. Only recorded when different from 443.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        public BaggageBuilder InvokeAgentServer(string? address, int? port = null)
        {
            Set(OpenTelemetryConstants.ServerAddressKey, address);
            if (port.HasValue && port.Value != 443)
            {
                Set(OpenTelemetryConstants.ServerPortKey, port.Value.ToString());
            }
            return this;
        }

        /// <summary>
        /// Sets the conversation ID baggage value.
        /// </summary>
        /// <remarks>
        /// This property must be set using this method in order for the agent to pass certification requirements.
        /// </remarks>
        public BaggageBuilder ConversationId(string? v)
        {
            Set(OpenTelemetryConstants.GenAiConversationIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the conversation item link baggage value.
        /// </summary>
        public BaggageBuilder ConversationItemLink(string? v)
        {
            Set(OpenTelemetryConstants.GenAiConversationItemLinkKey, v);
            return this;
        }

        /// <summary>
        /// Sets the channel name baggage value.
        /// </summary>
        /// <remarks>
        /// This property must be set for the agent to pass certification requirements.
        /// </remarks>
        public BaggageBuilder ChannelName(string? v)
        {
            Set(OpenTelemetryConstants.ChannelNameKey, v);
            return this;
        }

        /// <summary>
        /// Sets the channel link baggage value.
        /// </summary>
        public BaggageBuilder ChannelLink(string? v)
        {
            Set(OpenTelemetryConstants.ChannelLinkKey, v);
            return this;
        }

        /// <summary>
        /// Sets the session ID baggage value.
        /// </summary>
        public BaggageBuilder SessionId(string? v)
        {
            Set(OpenTelemetryConstants.SessionIdKey, v);
            return this;
        }

        /// <summary>
        /// Sets the session description baggage value.
        /// </summary>
        public BaggageBuilder SessionDescription(string? v)
        {
            Set(OpenTelemetryConstants.SessionDescriptionKey, v);
            return this;
        }

        /// <summary>
        /// Sets the operation source baggage value. 
        /// To be used with server spans to identify the source of the operation e.g(ACF, ATG)
        /// </summary>
        /// <remarks>
        /// This property must be set for the agent to pass certification requirements.
        /// </remarks>        
        /// <param name="source">The operation source identifying where the operation originated.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        public BaggageBuilder OperationSource(string source)
        {
            Set(OpenTelemetryConstants.ServiceNameKey, source);
            return this;
        }

        /// <summary>
        /// Applies the collected baggage to the current context.
        /// </summary>
        public IDisposable Build()
        {
            var previous = Baggage.Current;
            // Iterate through all key/value pairs and set them in _pairs
            foreach (var kvp in _pairs)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    Baggage.Current = Baggage.Current.SetBaggage(kvp.Key, kvp.Value);
                }
            }

            // Record custom keys (set via CustomAttribute) in a reserved baggage entry so the
            // ActivityProcessor knows to coalesce them onto every GenAI span. Merge with any
            // custom keys already present in the ambient baggage to support nested scopes.
            if (_customKeys.Count > 0)
            {
                var merged = new HashSet<string>(_customKeys, StringComparer.Ordinal);
                var existing = Baggage.Current.GetBaggage(OpenTelemetryConstants.CustomBaggageKeysKey);
                if (!string.IsNullOrEmpty(existing))
                {
                    foreach (var key in existing!.Split(','))
                    {
                        var trimmedKey = key.Trim();
                        if (trimmedKey.Length > 0)
                        {
                            merged.Add(trimmedKey);
                        }
                    }
                }

                Baggage.Current = Baggage.Current.SetBaggage(
                    OpenTelemetryConstants.CustomBaggageKeysKey,
                    string.Join(",", merged));
            }

            return new Scope(previous);
        }

        /// <summary>
        /// Adds a baggage key/value if the value is not null or whitespace.
        /// </summary>
        public void Set(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) _pairs[k] = v; }

        /// <summary>
        /// Sets multiple baggage values from an enumerable of key-value pairs.
        /// </summary>
        public BaggageBuilder SetRange(IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            if (pairs == null) return this;
            foreach (var kvp in pairs)
            {
                Set(kvp.Key, kvp.Value?.ToString());
            }
            return this;
        }

        /// <summary>
        /// Adds a custom baggage key/value pair that is coalesced onto every GenAI span
        /// (in addition to being propagated in baggage), unlike the strongly-typed members
        /// which follow the SDK's curated per-span attribute routing.
        /// </summary>
        /// <remarks>
        /// The value is only recorded when both the key and value are non-null and non-whitespace.
        /// Custom keys are propagated across process boundaries via W3C baggage, so they must not
        /// contain secrets or PII. A value set directly on a span always takes precedence over a
        /// custom baggage value when keys clash.
        /// </remarks>
        /// <param name="key">The custom attribute key.</param>
        /// <param name="value">The custom attribute value.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        public BaggageBuilder CustomAttribute(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return this;
            }

            // Normalize the key so it matches how the ActivityProcessor looks it up (the processor
            // trims each entry it reads from the reserved baggage list). Without this, a key with
            // leading/trailing whitespace would be stored untrimmed yet looked up trimmed, so its
            // value would never be coalesced onto spans.
            var trimmedKey = key.Trim();

            // Commas delimit keys in the reserved baggage list. A key containing a comma would be
            // split into multiple bogus lookups by the processor, so reject it outright.
            if (trimmedKey.IndexOf(',') >= 0)
            {
                return this;
            }

            // The reserved meta-key tracks the set of custom keys; it must never be registered as a
            // custom attribute or the internal key list could be emitted as a span tag.
            if (string.Equals(trimmedKey, OpenTelemetryConstants.CustomBaggageKeysKey, StringComparison.Ordinal))
            {
                return this;
            }

            Set(trimmedKey, value);
            _customKeys.Add(trimmedKey);
            return this;
        }

        /// <summary>
        /// Adds multiple custom baggage key/value pairs that are coalesced onto every GenAI span.
        /// See <see cref="CustomAttribute(string, string?)"/> for details.
        /// </summary>
        /// <param name="pairs">The custom attribute key/value pairs.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        public BaggageBuilder CustomAttributes(IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            if (pairs == null) return this;
            foreach (var kvp in pairs)
            {
                CustomAttribute(kvp.Key, kvp.Value?.ToString());
            }
            return this;
        }
    }
}