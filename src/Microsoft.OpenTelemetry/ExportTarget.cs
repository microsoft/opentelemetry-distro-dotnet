// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Specifies which exporters to enable in the Microsoft OpenTelemetry distro.
/// Multiple exporters can be combined using bitwise OR.
/// </summary>
[Flags]
public enum ExportTarget
{
    /// <summary>No exporters enabled. Instrumentation is still active.</summary>
    None = 0,

    /// <summary>Export telemetry to Azure Monitor (Application Insights).</summary>
    AzureMonitor = 1,

    /// <summary>Export telemetry to Agent365 observability service.</summary>
    Agent365 = 2,

    /// <summary>Export telemetry via OTLP (OpenTelemetry Protocol).</summary>
    Otlp = 4,

    /// <summary>Export telemetry to console (development only).</summary>
    Console = 8,
}
