// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Logs;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Unified extension methods for configuring the Microsoft OpenTelemetry distro.
/// </summary>
public static class MicrosoftOpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro on an <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Callback to configure <see cref="MicrosoftOpenTelemetryOptions"/>.</param>
    /// <returns>The supplied <typeparamref name="TBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>This is the recommended entry point for ASP.NET Core and Worker Service apps.</para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.UseMicrosoftOpenTelemetry(o =>
    /// {
    ///     o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    ///     o.Agent365.Exporter.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///     o.OtlpEndpoint = new Uri("http://localhost:4317");
    /// });
    /// </code>
    /// </remarks>
    public static TBuilder UseMicrosoftOpenTelemetry<TBuilder>(
        this TBuilder builder,
        Action<MicrosoftOpenTelemetryOptions> configure) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenTelemetry().UseMicrosoftOpenTelemetry(configure);
        return builder;
    }

    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro with default settings.
    /// Exporters are auto-detected from environment variables and IConfiguration.
    /// </summary>
    public static TBuilder UseMicrosoftOpenTelemetry<TBuilder>(
        this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        return builder.UseMicrosoftOpenTelemetry(o => { });
    }
    /// <summary>
    /// Configures the Microsoft OpenTelemetry distro with Azure Monitor, Agent365,
    /// Microsoft Agent Framework, and shared exporters (OTLP, Console) in a single call.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder.</param>
    /// <param name="configure">Callback to configure <see cref="MicrosoftOpenTelemetryOptions"/>.</param>
    /// <returns>The supplied <see cref="IOpenTelemetryBuilder"/> for chaining calls.</returns>
    /// <remarks>
    /// <para>This is the recommended single entry point for the Microsoft OpenTelemetry distro.</para>
    /// <para>Usage:</para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .UseMicrosoftOpenTelemetry(o =>
    ///     {
    ///         // Select exporters
    ///         o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365;
    ///
    ///         // Azure Monitor
    ///         o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
    ///
    ///         // Agent365
    ///         o.Agent365.Exporter.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    ///     });
    /// </code>
    /// </remarks>
    public static IOpenTelemetryBuilder UseMicrosoftOpenTelemetry(
        this IOpenTelemetryBuilder builder,
        Action<MicrosoftOpenTelemetryOptions> configure)
    {
        var options = new MicrosoftOpenTelemetryOptions();
        configure(options);

        // Auto-detect exporters from configuration if not explicitly set
        var exporters = options.Exporters;
        if (!options.ExportersExplicitlySet)
        {
            exporters = ExportTarget.None;

            // Check code-provided, IConfiguration, and raw env var for connection string
            if (!string.IsNullOrWhiteSpace(options.AzureMonitor.ConnectionString)
                || HasAzureMonitorConnectionString(builder.Services))
                exporters |= ExportTarget.AzureMonitor;
            if (options.Agent365.Exporter.TokenResolver != null)
                exporters |= ExportTarget.Agent365;
            
        }

        // --- Azure Monitor (always: instrumentation; exporter gated by Exporters flag) ---
        builder.UseAzureMonitor(o =>
        {
            o.SkipExporter = !exporters.HasFlag(ExportTarget.AzureMonitor);
            o.ConnectionString = options.AzureMonitor.ConnectionString;
            o.Credential = options.AzureMonitor.Credential;
            o.DisableOfflineStorage = options.AzureMonitor.DisableOfflineStorage;
            o.StorageDirectory = options.AzureMonitor.StorageDirectory;
            o.EnableLiveMetrics = options.AzureMonitor.EnableLiveMetrics;
            o.EnableStandardMetrics = options.AzureMonitor.EnableStandardMetrics;
            o.EnablePerfCounters = options.AzureMonitor.EnablePerfCounters;
            o.EnableTraceBasedLogsSampler = options.AzureMonitor.EnableTraceBasedLogsSampler;
            o.SamplingRatio = options.AzureMonitor.SamplingRatio;
            o.TracesPerSecond = options.AzureMonitor.TracesPerSecond;
        }, options.Instrumentation);

        // --- Agent365 (always: scopes + baggage + span processors; exporter gated by Exporters flag) ---
        builder.UseAgent365(o =>
        {
            o.SkipExporter = !exporters.HasFlag(ExportTarget.Agent365);
            o.Exporter.TokenResolver = options.Agent365.Exporter.TokenResolver;
            o.Exporter.DomainResolver = options.Agent365.Exporter.DomainResolver;
            o.Exporter.UseS2SEndpoint = options.Agent365.Exporter.UseS2SEndpoint;
            o.Exporter.MaxQueueSize = options.Agent365.Exporter.MaxQueueSize;
            o.Exporter.ScheduledDelayMilliseconds = options.Agent365.Exporter.ScheduledDelayMilliseconds;
            o.Exporter.ExporterTimeoutMilliseconds = options.Agent365.Exporter.ExporterTimeoutMilliseconds;
            o.Exporter.MaxExportBatchSize = options.Agent365.Exporter.MaxExportBatchSize;
        }, options.Instrumentation);

        // --- Microsoft Agent Framework (always: captures MAF spans + processor) ---
        builder.UseAgentFramework(options.Instrumentation);

        // --- OTLP exporter ---
        if (exporters.HasFlag(ExportTarget.Otlp))
        {
            if (options.Instrumentation.EnableTracing)
            {
                builder.WithTracing(tracing =>
                {
                    tracing.AddOtlpExporter();
                });
            }

            if (options.Instrumentation.EnableMetrics)
            {
                builder.WithMetrics(metrics =>
                {
                    metrics.AddOtlpExporter();
                });
            }

            if (options.Instrumentation.EnableLogging)
            {
                builder.WithLogging(logging =>
                {
                    logging.AddOtlpExporter();
                });
            }
        }

        // --- Console exporter ---
        if (exporters.HasFlag(ExportTarget.Console))
        {
            if (options.Instrumentation.EnableTracing)
            {
                builder.WithTracing(tracing => tracing.AddConsoleExporter());
            }

            if (options.Instrumentation.EnableMetrics)
            {
                builder.WithMetrics(metrics => metrics.AddConsoleExporter());
            }

            if (options.Instrumentation.EnableLogging)
            {
                builder.WithLogging(logging => logging.AddConsoleExporter());
            }
        }

        return builder;
    }

    /// <summary>
    /// Checks if an Azure Monitor connection string is available from IConfiguration
    /// (appsettings.json, env var provider, Key Vault, etc.) or raw environment variable.
    /// Does NOT call BuildServiceProvider().
    /// </summary>
    private static bool HasAzureMonitorConnectionString(IServiceCollection services)
    {
        // Try to find IConfiguration from service descriptors (registered as singleton instance
        // by HostApplicationBuilder, WebApplicationBuilder, and Host.CreateDefaultBuilder)
        IConfiguration? config = null;
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IConfiguration)
                && services[i].ImplementationInstance is IConfiguration c)
            {
                config = c;
                break;
            }
        }

        if (config != null)
        {
            // Check "AzureMonitor" config section (appsettings.json)
            var sectionConnStr = config.GetSection("AzureMonitor")?["ConnectionString"];
            if (!string.IsNullOrWhiteSpace(sectionConnStr))
                return true;

            // Check APPLICATIONINSIGHTS_CONNECTION_STRING via IConfiguration
            // (covers env var provider, in-memory collection, and other IConfiguration sources)
            var configConnStr = config["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            if (!string.IsNullOrWhiteSpace(configConnStr))
                return true;
        }

        // Fallback: check raw environment variable (for non-host DI apps without IConfiguration)
        var envConnStr = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        return !string.IsNullOrWhiteSpace(envConnStr);
    }
}
