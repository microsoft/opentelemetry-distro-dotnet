// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Options for configuring Agent365 observability.
/// </summary>
public class Agent365Options
{
    /// <summary>
    /// Gets the Agent365 exporter settings (token resolver, domain, S2S, batch settings).
    /// </summary>
    public Agent365ExporterOptions Exporter { get; } = new();

    /// <summary>
    /// When true, skips exporter registration. Instrumentation is still added.
    /// Used internally by <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/>.
    /// </summary>
    internal bool SkipExporter { get; set; }
}
