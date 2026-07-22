// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using global::OpenTelemetry;
using global::OpenTelemetry.Logs;
using global::OpenTelemetry.Trace;
using Microsoft.OpenTelemetry.GenAI.MainAgent;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Extension methods for registering the GenAI main-agent attribute propagation
/// processors on an <see cref="IOpenTelemetryBuilder"/>.
/// </summary>
internal static class GenAIMainAgentOpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="GenAIMainAgentSpanProcessor"/> and
    /// <see cref="GenAIMainAgentLogRecordProcessor"/> so that
    /// <c>microsoft.gen_ai.main_agent.*</c> attributes are propagated onto every
    /// span/log emitted downstream of the top-level (user-facing) GenAI agent.
    /// </summary>
    /// <remarks>
    /// These processors must be added BEFORE any batch export processor so their
    /// <c>OnStart</c>/<c>OnEnd</c> callbacks run first and downstream exporters observe
    /// the enriched attributes. This method should be invoked early in the pipeline
    /// configuration (before <c>UseAzureMonitor</c>, OTLP or Console exporters are
    /// added).
    /// </remarks>
    internal static IOpenTelemetryBuilder UseGenAIMainAgent(
        this IOpenTelemetryBuilder builder,
        InstrumentationOptions instrumentationOptions)
    {
        if (instrumentationOptions.EnableTracing)
        {
            builder.WithTracing(tracing => tracing.AddProcessor(new GenAIMainAgentSpanProcessor()));
        }

        if (instrumentationOptions.EnableLogging)
        {
            builder.WithLogging(logging => logging.AddProcessor(new GenAIMainAgentLogRecordProcessor()));
        }

        return builder;
    }
}
