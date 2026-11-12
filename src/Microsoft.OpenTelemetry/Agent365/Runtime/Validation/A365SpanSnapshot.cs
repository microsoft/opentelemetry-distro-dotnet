// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Immutable snapshot of a captured span and its attributes.
/// </summary>
public sealed class A365SpanSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A365SpanSnapshot"/> class,
    /// deriving the effective export routing identity from
    /// <paramref name="attributes"/>. Used when a snapshot is built from
    /// attributes alone -- there is no <see cref="System.Diagnostics.Activity"/>
    /// to read baggage from -- so routing identity resolves exactly as it does
    /// for a span that carries its identity in tags.
    /// </summary>
    /// <param name="traceId">The trace identifier.</param>
    /// <param name="spanId">The span identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="sourceName">The source name.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="attributes">The span attributes.</param>
    internal A365SpanSnapshot(
        string traceId,
        string spanId,
        string displayName,
        string sourceName,
        string operationName,
        IDictionary<string, object?> attributes)
        : this(
            traceId,
            spanId,
            displayName,
            sourceName,
            operationName,
            attributes,
            ResolveRoutingValue(attributes, OpenTelemetryConstants.TenantIdKey),
            ResolveRoutingValue(attributes, OpenTelemetryConstants.GenAiAgentIdKey) ??
                ResolveRoutingValue(attributes, OpenTelemetryConstants.AgentPlatformIdKey))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="A365SpanSnapshot"/> class
    /// with an explicitly resolved export routing identity.
    /// </summary>
    /// <param name="traceId">The trace identifier.</param>
    /// <param name="spanId">The span identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="sourceName">The source name.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="attributes">The span attributes (activity tags only).</param>
    /// <param name="routingTenantId">
    /// The tenant identifier the A365 exporter would route this span with,
    /// resolved from the activity's tag or baggage.
    /// </param>
    /// <param name="routingAgentId">
    /// The agent identifier the A365 exporter would route this span with,
    /// resolved from the activity's <c>gen_ai.agent.id</c> tag or baggage and
    /// falling back to its agent platform identifier.
    /// </param>
    internal A365SpanSnapshot(
        string traceId,
        string spanId,
        string displayName,
        string sourceName,
        string operationName,
        IDictionary<string, object?> attributes,
        string? routingTenantId,
        string? routingAgentId)
    {
        RoutingTenantId = routingTenantId;
        RoutingAgentId = routingAgentId;
        TraceId = traceId;
        SpanId = spanId;
        DisplayName = displayName;
        SourceName = sourceName;
        OperationName = operationName;
        Attributes = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(attributes, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the trace identifier.
    /// </summary>
    public string TraceId { get; }

    /// <summary>
    /// Gets the span identifier.
    /// </summary>
    public string SpanId { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the source name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the operation name, resolved from the span's
    /// <c>gen_ai.operation.name</c> tag and falling back to the activity's
    /// baggage, because that is exactly how the A365 exporter classifies a
    /// span as GenAI telemetry. Unlike the entries in <see cref="Attributes"/>,
    /// this value may therefore originate from baggage.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the captured span attributes.
    /// </summary>
    /// <remarks>
    /// These are the attributes the A365 exporter would actually serialize:
    /// the activity's tags only, with the same duplicate-key behavior as the
    /// exporter's OTLP attribute mapping (the last tag written for a key
    /// wins). <see cref="System.Diagnostics.Activity"/> baggage is
    /// deliberately not merged in, because baggage is not serialized into OTLP
    /// span attributes: a value present only in baggage never reaches the
    /// Agent 365 service, so surfacing it here would misreport the exported
    /// payload.
    /// </remarks>
    public IReadOnlyDictionary<string, object?> Attributes { get; }

    /// <summary>
    /// Gets the tenant identifier the A365 exporter would route this span
    /// with, resolved from the activity's <c>microsoft.tenant.id</c> tag and
    /// falling back to its baggage, or <see langword="null"/> when neither
    /// supplies one. Routing identity is captured separately from
    /// <see cref="Attributes"/> because the exporter resolves it through
    /// <c>GetAttributeOrBaggage</c> before (and independently of) attribute
    /// serialization.
    /// </summary>
    internal string? RoutingTenantId { get; }

    /// <summary>
    /// Gets the agent identifier the A365 exporter would route this span with,
    /// resolved from the activity's <c>gen_ai.agent.id</c> tag or baggage and
    /// falling back to its <c>microsoft.a365.agent.platform.id</c> tag or
    /// baggage, or <see langword="null"/> when none supplies one.
    /// </summary>
    internal string? RoutingAgentId { get; }

    /// <summary>
    /// Resolves a routing value from an attribute dictionary using the same
    /// non-null-wins, stringifying semantics that
    /// <c>ActivityExtensions.GetAttributeOrBaggage</c> applies to a tag.
    /// </summary>
    private static string? ResolveRoutingValue(
        IDictionary<string, object?> attributes,
        string key)
    {
        if (attributes == null ||
            !attributes.TryGetValue(key, out var value) ||
            value == null)
        {
            return null;
        }

        var text = value as string ?? value.ToString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
