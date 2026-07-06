// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Extensions.SemanticKernel;
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
        return builder.UseAgent365(o => { }, new InstrumentationOptions());
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
    /// <item>Optional framework integrations: Agent Framework, Azure OpenAI.</item>
    /// </list>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseAgent365(o =>
    ///     {
    ///         o.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///     });
    /// </code>
    /// </remarks>
    internal static IOpenTelemetryBuilder UseAgent365(this IOpenTelemetryBuilder builder, Action<Agent365Options> configure)
    {
        return builder.UseAgent365(configure, new InstrumentationOptions());
    }

    /// <summary>
    /// Configures Agent365 observability with instrumentation options.
    /// </summary>
    internal static IOpenTelemetryBuilder UseAgent365(this IOpenTelemetryBuilder builder, Action<Agent365Options> configure, InstrumentationOptions instrumentationOptions)
    {
        var options = new Agent365Options();
        configure?.Invoke(options);

        // Enable Azure SDK activity sources
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        if (instrumentationOptions.EnableSemanticKernelInstrumentation)
        {
            AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);
        }

        // --- Core tracing: Agent365 scopes + baggage processor + framework span processors ---
        if (instrumentationOptions.EnableTracing)
        {
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
                    remoteParentNotSampled: new global::OpenTelemetry.Trace.AlwaysOnSampler()));

            // Semantic Kernel
            if (instrumentationOptions.EnableSemanticKernelInstrumentation)
            {
                tracing
                    .AddSource(SemanticKernelTelemetryConstants.SemanticKernelSourceWildcard)
                    .AddProcessor(new SemanticKernelSpanProcessor());
            }

            // OpenAI / Azure OpenAI
            if (instrumentationOptions.EnableOpenAIInstrumentation)
            {
                tracing
                    .AddSource("Azure.AI.OpenAI*")
                    .AddSource("OpenAI.*")
                    .AddSource("Experimental.Microsoft.Extensions.AI");
            }

            // Agent365 scopes + baggage processor — registered last because processors
            // are called in order of addition; this ensures OnStart runs after SK and
            // other instrumentation processors.
            if (instrumentationOptions.EnableAgent365Instrumentation)
            {
                tracing
                    .AddSource(OpenTelemetryConstants.SourceName)
                    .AddProcessor<ActivityProcessor>();
            }

            // Agent365 Exporter options (available via DI for built-in or custom shim exporter)
            if (!options.SkipExporter)
            {
                builder.Services.AddSingleton(options.ExporterOptions);

                // Defer the marker check to provider build time so that
                // AddCustomAgent365Exporter can be called AFTER UseMicrosoftOpenTelemetry.
                if (tracing is IDeferredTracerProviderBuilder deferredTracing)
                {
                    deferredTracing.Configure((sp, tracerBuilder) =>
                    {
                        var shimRegistered = sp.GetService<CustomAgent365ExporterMarker>() != null;
                        if (!shimRegistered)
                        {
                            tracerBuilder.AddAgent365Exporter();
                        }
                    });
                }
                else
                {
                    // Fallback: check services at call time (legacy behavior)
                    var shimRegistered = builder.Services.Any(s =>
                        s.ImplementationInstance is CustomAgent365ExporterMarker ||
                        s.ServiceType == typeof(CustomAgent365ExporterMarker) ||
                        s.ImplementationType == typeof(CustomAgent365ExporterMarker));

                    if (!shimRegistered)
                    {
                        tracing.AddAgent365Exporter();
                    }
                }
            }
            });
        }

        return builder;
    }
}
