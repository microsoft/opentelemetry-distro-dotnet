// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        // Guard against duplicate calls
        if (builder.Services.Any(s => s.ImplementationInstance is UseMicrosoftOpenTelemetryRegistration))
        {
            throw new NotSupportedException(
                "Multiple calls to UseMicrosoftOpenTelemetry on the same IServiceCollection are not supported.");
        }

        builder.Services.AddSingleton(UseMicrosoftOpenTelemetryRegistration.Instance);

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

        // Determine which signals have at least one exporter destination.
        // Agent365 only exports traces — metrics and logs sent to it would go nowhere.
        var hasTracingExporter = exporters != ExportTarget.None; // all exporters support traces
        var hasMetricsExporter = exporters.HasFlag(ExportTarget.AzureMonitor)
                              || exporters.HasFlag(ExportTarget.Otlp)
                              || exporters.HasFlag(ExportTarget.Console);
        var hasLoggingExporter = hasMetricsExporter; // same set supports logs

        // Effective signal flags: user intent AND exporter availability
        var effectiveTracing = options.Instrumentation.EnableTracing && hasTracingExporter;
        var effectiveMetrics = options.Instrumentation.EnableMetrics && hasMetricsExporter;
        var effectiveLogging = options.Instrumentation.EnableLogging && hasLoggingExporter;

        // Build an effective InstrumentationOptions that subsystems will use
        var effectiveInstrumentation = new InstrumentationOptions
        {
            EnableTracing = effectiveTracing,
            EnableMetrics = effectiveMetrics,
            EnableLogging = effectiveLogging,
            EnableAspNetCoreInstrumentation = options.Instrumentation.EnableAspNetCoreInstrumentation,
            EnableHttpClientInstrumentation = options.Instrumentation.EnableHttpClientInstrumentation,
            EnableSqlClientInstrumentation = options.Instrumentation.EnableSqlClientInstrumentation,
            EnableAzureSdkInstrumentation = options.Instrumentation.EnableAzureSdkInstrumentation,
            EnableOpenAIInstrumentation = options.Instrumentation.EnableOpenAIInstrumentation,
            EnableSemanticKernelInstrumentation = options.Instrumentation.EnableSemanticKernelInstrumentation,
            EnableAgentFrameworkInstrumentation = options.Instrumentation.EnableAgentFrameworkInstrumentation,
            EnableAgent365Instrumentation = options.Instrumentation.EnableAgent365Instrumentation,
        };

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
        }, effectiveInstrumentation);

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
        }, effectiveInstrumentation);

        // --- Microsoft Agent Framework (always: captures MAF spans + processor) ---
        builder.UseAgentFramework(effectiveInstrumentation);

        // --- OTLP exporter ---
        if (exporters.HasFlag(ExportTarget.Otlp))
        {
            if (effectiveInstrumentation.EnableTracing)
            {
                builder.WithTracing(tracing =>
                {
                    tracing.AddOtlpExporter();
                });
            }

            if (effectiveInstrumentation.EnableMetrics)
            {
                builder.WithMetrics(metrics =>
                {
                    metrics.AddOtlpExporter();
                });
            }

            if (effectiveInstrumentation.EnableLogging)
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
            if (effectiveInstrumentation.EnableTracing)
            {
                builder.WithTracing(tracing => tracing.AddConsoleExporter());
            }

            if (effectiveInstrumentation.EnableMetrics)
            {
                builder.WithMetrics(metrics => metrics.AddConsoleExporter());
            }

            if (effectiveInstrumentation.EnableLogging)
            {
                builder.WithLogging(logging => logging.AddConsoleExporter());
            }
        }

        // --- Logging kill switch ---
        // When effective logging is disabled, suppress all log records from reaching
        // OpenTelemetryLoggerProvider (affects Azure Monitor, OTLP, Console, etc.).
        // This is a provider-scoped ILogger filter — other loggers are not affected.
        if (!effectiveInstrumentation.EnableLogging)
        {
            builder.Services.AddLogging(logging =>
            {
                logging.AddFilter<OpenTelemetryLoggerProvider>(null, LogLevel.None);
            });
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
