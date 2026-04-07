// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Options for the unified <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/> entry point.
/// </summary>
public class MicrosoftOpenTelemetryOptions
{
    private ExportTarget? _exporters;

    /// <summary>
    /// Gets or sets which exporters to enable.
    /// When not explicitly set, exporters are auto-detected from configuration:
    /// <list type="bullet">
    /// <item><see cref="ExportTarget.AzureMonitor"/> — when <see cref="AzureMonitor"/>.<see cref="AzureMonitorOptions.ConnectionString"/> is set.</item>
    /// <item><see cref="ExportTarget.Agent365"/> — when <see cref="Agent365"/>.<see cref="Agent365Options.Exporter"/>.<c>TokenResolver</c> is set.</item>
    /// <item><see cref="ExportTarget.Otlp"/> — when <see cref="OtlpEndpoint"/> is set.</item>
    /// </list>
    /// Set explicitly to override auto-detection:
    /// <c>o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365;</c>
    /// </summary>
    public ExportTarget Exporters
    {
        get => _exporters ?? ExportTarget.None;
        set => _exporters = value;
    }

    /// <summary>
    /// Returns true if <see cref="Exporters"/> was explicitly set by the user.
    /// </summary>
    internal bool ExportersExplicitlySet => _exporters.HasValue;

    /// <summary>
    /// Gets the Azure Monitor configuration.
    /// Setting <see cref="AzureMonitorOptions.ConnectionString"/> auto-enables the Azure Monitor exporter.
    /// </summary>
    public AzureMonitorOptions AzureMonitor { get; } = new();

    /// <summary>
    /// Gets the Agent365 configuration.
    /// Only exporter is gated by <see cref="Exporters"/>; instrumentation (scopes, baggage) is always active.
    /// </summary>
    public Agent365Options Agent365 { get; } = new();

    /// <summary>
    /// Gets or sets whether to enable Microsoft Agent Framework span capture. Default is true.
    /// Listens to <c>Experimental.Microsoft.Agents.AI</c> activity sources automatically.
    /// </summary>
    public bool EnableAgentFramework { get; set; } = true;
}
