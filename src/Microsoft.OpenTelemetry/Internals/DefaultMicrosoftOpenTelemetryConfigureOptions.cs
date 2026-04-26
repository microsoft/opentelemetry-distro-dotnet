// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// <see cref="IConfigureOptions{MicrosoftOpenTelemetryOptions}"/> implementation that reads
/// distro-level options from the "MicrosoftOpenTelemetry" section in <see cref="IConfiguration"/>.
/// </summary>
internal class DefaultMicrosoftOpenTelemetryConfigureOptions : IConfigureOptions<MicrosoftOpenTelemetryOptions>
{
    internal const string SectionName = "MicrosoftOpenTelemetry";
    private const string AzureMonitorSectionName = "AzureMonitor";
    private const string ConnectionStringEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    private readonly IConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMicrosoftOpenTelemetryConfigureOptions"/> class.
    /// </summary>
    /// <param name="configuration"><see cref="IConfiguration"/> from which distro configuration can be retrieved.</param>
    public DefaultMicrosoftOpenTelemetryConfigureOptions(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(MicrosoftOpenTelemetryOptions options)
    {
        if (_configuration == null)
        {
            return;
        }

        try
        {
            // --- MicrosoftOpenTelemetry section (Exporters, Instrumentation) ---
            var section = _configuration.GetSection(SectionName);
            if (section.Exists())
            {
                // Bind Instrumentation sub-properties (all bool properties with public setters).
                var instrumentationSection = section.GetSection(nameof(MicrosoftOpenTelemetryOptions.Instrumentation));
                if (instrumentationSection.Exists())
                {
                    instrumentationSection.Bind(options.Instrumentation);
                }

                // Bind Exporters ([Flags] enum — IConfiguration handles comma-separated values like "AzureMonitor, Otlp").
                var exportersValue = section[nameof(MicrosoftOpenTelemetryOptions.Exporters)];
                if (!string.IsNullOrWhiteSpace(exportersValue)
                    && Enum.TryParse<ExportTarget>(exportersValue, ignoreCase: true, out var exporters))
                {
                    options.Exporters = exporters;
                }
            }

            // --- AzureMonitor section (top-level, binds ConnectionString and other properties) ---
            // This populates options.AzureMonitor so the bridge in UseMicrosoftOpenTelemetry
            // can copy values to the actual AzureMonitorOptions exporter configuration.
            var azureMonitorSection = _configuration.GetSection(AzureMonitorSectionName);
            if (azureMonitorSection.Exists())
            {
                azureMonitorSection.Bind(options.AzureMonitor);
            }

            // APPLICATIONINSIGHTS_CONNECTION_STRING via IConfiguration
            // (covers env var provider, in-memory collection, Key Vault, etc.)
            var connectionStringFromConfig = _configuration[ConnectionStringEnvVar];
            if (!string.IsNullOrEmpty(connectionStringFromConfig))
            {
                options.AzureMonitor.ConnectionString = connectionStringFromConfig;
            }

            // Raw environment variable takes highest precedence.
            var connectionStringFromEnvVar = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
            if (!string.IsNullOrEmpty(connectionStringFromEnvVar))
            {
                options.AzureMonitor.ConnectionString = connectionStringFromEnvVar;
            }
        }
        catch (Exception)
        {
            // Configuration binding errors should not crash the application.
            // The distro will fall back to code defaults.
        }
    }

    /// <summary>
    /// Eagerly binds configuration values to the supplied <paramref name="options"/> instance.
    /// Used during service registration to read build-time decisions from IConfiguration
    /// before DI options infrastructure is available.
    /// </summary>
    internal static void BindFromConfiguration(IConfiguration? configuration, MicrosoftOpenTelemetryOptions options)
    {
        if (configuration == null)
        {
            return;
        }

        var configureOptions = new DefaultMicrosoftOpenTelemetryConfigureOptions(configuration);
        configureOptions.Configure(options);
    }
}
