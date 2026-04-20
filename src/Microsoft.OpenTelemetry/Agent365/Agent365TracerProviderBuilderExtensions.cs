// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// Local shim: AddAgent365Exporter is internal in the Runtime package (0.2.151-beta).
// Remove this file once the Agent365 team makes it public or adds InternalsVisibleTo.

using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using System;
using System.Net.Http;

namespace Microsoft.OpenTelemetry.Agent365;

/// <summary>
/// Local extension methods to wire up the Agent365 exporter on TracerProviderBuilder.
/// Mirrors the internal ObservabilityTracerProviderBuilderExtensions in the Runtime package.
/// </summary>
internal static class Agent365TracerProviderBuilderExtensions
{
    private static readonly Lazy<ILoggerFactory> FallbackConsoleLoggerFactory =
        new Lazy<ILoggerFactory>(() => LoggerFactory.Create(b => b.AddConsole()));

    /// <summary>
    /// Adds the Agent365 Exporter to the OpenTelemetry TracerProviderBuilder using deferred initialization.
    /// </summary>
    internal static TracerProviderBuilder AddAgent365Exporter(this TracerProviderBuilder builder)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        var deferredBuilder = builder as IDeferredTracerProviderBuilder
            ?? throw new InvalidOperationException(
                "The provided TracerProviderBuilder does not implement IDeferredTracerProviderBuilder.");

        return deferredBuilder.Configure((sp, b) => ConfigureInternal(sp, b));
    }

    private static TracerProviderBuilder ConfigureInternal(IServiceProvider serviceProvider, TracerProviderBuilder builder)
    {
        var exporterOptions = serviceProvider.GetRequiredService<Agent365ExporterOptions>();
        var httpClient = serviceProvider.GetService<HttpClient>();

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? FallbackConsoleLoggerFactory.Value;
        var logger = serviceProvider.GetService<ILogger<Agent365Exporter>>() ?? loggerFactory.CreateLogger<Agent365Exporter>();
        var coreLogger = serviceProvider.GetService<ILogger<Agent365ExporterCore>>() ?? loggerFactory.CreateLogger<Agent365ExporterCore>();
        var formatterLogger = serviceProvider.GetService<ILogger<ExportFormatter>>() ?? loggerFactory.CreateLogger<ExportFormatter>();

        var exportFormatter = new ExportFormatter(formatterLogger);
        var exporterCore = new Agent365ExporterCore(exportFormatter, coreLogger);

        builder.AddProcessor(new BatchActivityExportProcessorAsync(
            new Agent365ExporterAsync(core: exporterCore, logger: logger, options: exporterOptions, resource: null, httpClient: httpClient),
            maxQueueSize: exporterOptions.MaxQueueSize,
            scheduledDelayMilliseconds: exporterOptions.ScheduledDelayMilliseconds,
            maxExportBatchSize: exporterOptions.MaxExportBatchSize));

        return builder;
    }
}
