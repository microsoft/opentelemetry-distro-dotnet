// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.Agent365;
using Microsoft.OpenTelemetry.Agent365.Common;
using Microsoft.OpenTelemetry.Agent365.Extensions.OpenAI;
using Microsoft.OpenTelemetry.Agent365.Extensions.SemanticKernel;
using Microsoft.OpenTelemetry.Agent365.Tracing.Exporters;
using Microsoft.OpenTelemetry.Agent365.Tracing.Processors;
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;
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
            tracing
                .AddSource(OpenTelemetryConstants.SourceName)
                .AddSource(SemanticKernelTelemetryConstants.SemanticKernelSourceWildcard)
                .AddSource("Azure.AI.OpenAI*")
                .AddProcessor<ActivityProcessor>()
                .AddProcessor(new SemanticKernelSpanProcessor())
                .AddProcessor(new OpenAISpanProcessor());

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
                    // No inline TokenResolver — use distro's ObservabilityTokenStore.
                    // Tokens are populated by agent middleware (e.g. A365OtelWrapper)
                    // using ObservabilityTokenStore.SetToken() on each turn.
                    var exporterOptions = new Agent365ExporterOptions
                    {
                        TokenResolver = ObservabilityTokenStore.GetTokenAsync
                    };
                    builder.Services.AddSingleton(exporterOptions);
                }
                tracing.AddAgent365Exporter();
            }
        });

        // Register SK function invocation filter
        builder.Services.AddSingleton<Microsoft.SemanticKernel.IFunctionInvocationFilter, FunctionInvocationFilter>();

        return builder;
    }
}
