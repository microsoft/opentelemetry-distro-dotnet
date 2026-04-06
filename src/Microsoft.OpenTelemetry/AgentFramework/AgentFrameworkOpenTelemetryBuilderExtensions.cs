// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.AgentFramework;
using Microsoft.Extensions.DependencyInjection;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Extension methods for configuring Microsoft Agent Framework observability on <see cref="IOpenTelemetryBuilder"/>.
/// </summary>
public static class AgentFrameworkOpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Configures the distro to capture telemetry from Microsoft Agent Framework.
    /// Listens to the default <c>Experimental.Microsoft.Agents.AI</c> activity sources
    /// that Agent Framework emits when <c>.UseOpenTelemetry()</c> or <c>.WithOpenTelemetry()</c> is called.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>
    /// Microsoft Agent Framework emits spans following the
    /// <see href="https://opentelemetry.io/docs/specs/semconv/gen-ai/">OpenTelemetry GenAI Semantic Conventions</see>.
    /// </para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseAzureMonitor(o => o.ConnectionString = "...")
    ///     .UseAgentFramework();
    /// </code>
    /// </remarks>
    public static IOpenTelemetryBuilder UseAgentFramework(this IOpenTelemetryBuilder builder)
    {
        builder.WithTracing(tracing =>
        {
            // Default Microsoft Agent Framework activity sources
            tracing
                .AddSource(AgentFrameworkConstants.DefaultSource)
                .AddSource(AgentFrameworkConstants.AgentSource)
                .AddSource(AgentFrameworkConstants.ChatClientSource);
        });

        // Also capture metrics from the same sources
        builder.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(AgentFrameworkConstants.DefaultSource)
                .AddMeter(AgentFrameworkConstants.AgentSource)
                .AddMeter(AgentFrameworkConstants.ChatClientSource);
        });

        return builder;
    }
}
