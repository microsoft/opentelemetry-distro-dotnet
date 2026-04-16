// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Extensions.SemanticKernel;
using Microsoft.Agents.A365.Observability.Hosting;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Extension methods for configuring Agent365 observability on <see cref="IOpenTelemetryBuilder"/>.
/// </summary>
internal static class Agent365OpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Configures Agent365 observability for AI agent tracing, including
    /// scopes (InvokeAgent, Inference, ExecuteTool, Output), baggage propagation,
    /// and the Agent365 exporter.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    internal static IOpenTelemetryBuilder UseAgent365(this IOpenTelemetryBuilder builder)
    {
        return builder.UseAgent365(o => { });
    }

    /// <summary>
    /// Configures Agent365 observability for AI agent tracing with options.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <param name="configure">Callback to configure <see cref="Agent365Options"/>.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>This method configures:</para>
    /// <list type="bullet">
    /// <item>Agent365 activity source for tracing scopes (InvokeAgent, Inference, ExecuteTool, Output).</item>
    /// <item>Baggage-to-tags activity processor for context propagation.</item>
    /// <item>Agent365 exporter when <c>TokenResolver</c> is configured.</item>
    /// <item>Optional framework integrations: Semantic Kernel, Agent Framework, Azure OpenAI.</item>
    /// </list>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseAgent365(o =>
    ///     {
    ///         o.Exporter.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///         o.WithSemanticKernel()
    ///          .WithAgentFramework()
    ///          .WithOpenAI();
    ///     });
    /// </code>
    /// </remarks>
    internal static IOpenTelemetryBuilder UseAgent365(this IOpenTelemetryBuilder builder, Action<Agent365Options> configure)
    {
        var options = new Agent365Options();
        configure?.Invoke(options);

        // Enable Azure SDK activity sources
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
        AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

        // --- Core tracing: Agent365 scopes + baggage processor + framework span processors ---
        builder.WithTracing(tracing =>
        {
            // Match the Agent365 SDK sampler: ParentBasedSampler with AlwaysOnSampler
            // for all cases. The Bot Framework returns HTTP 202 immediately and processes
            // LLM calls on async continuations with no parent Activity. Without this,
            // parent-based samplers drop those orphan root spans — including gen_ai.*
            // chat and invoke_agent spans.
            tracing
                .SetSampler(new global::OpenTelemetry.Trace.ParentBasedSampler(
                    rootSampler: new global::OpenTelemetry.Trace.AlwaysOnSampler(),
                    localParentNotSampled: new global::OpenTelemetry.Trace.AlwaysOnSampler(),
                    remoteParentNotSampled: new global::OpenTelemetry.Trace.AlwaysOnSampler()))
                .AddSource(OpenTelemetryConstants.SourceName)
                .AddSource(SemanticKernelTelemetryConstants.SemanticKernelSourceWildcard)
                .AddSource("Azure.AI.OpenAI*")
                .AddSource("Experimental.Microsoft.Extensions.AI")
                .AddSource("Experimental.Microsoft.Agents.AI")
                .AddProcessor<ActivityProcessor>()
                .AddProcessor(new SemanticKernelSpanProcessor());

            // Agent365 Exporter (enabled when not skipped)
            if (!options.SkipExporter)
            {
                if (options.Exporter.TokenResolver != null)
                {
                    // Inline TokenResolver provided — register options directly in DI
                    builder.Services.AddSingleton(options.Exporter);
                }
                else
                {
                    // No inline TokenResolver — register agentic token cache and options in DI.
                    // Token cache is populated by agent middleware (e.g. via RegisterObservability).
                    builder.Services.AddAgenticTracingExporter();
                }
                tracing.AddAgent365Exporter();
            }
        });

        // Register SK function invocation filter
        builder.Services.AddSingleton<Microsoft.SemanticKernel.IFunctionInvocationFilter, FunctionInvocationFilter>();

        return builder;
    }
}
