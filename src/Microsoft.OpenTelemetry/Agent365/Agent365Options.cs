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
    /// Gets the span filter that controls which activity sources are exported
    /// to the Agent365 backend. By default, only GenAI and agent spans are exported.
    /// </summary>
    public Agent365SpanFilterOptions SpanFilter { get; } = new();

    /// <summary>
    /// When true, skips exporter registration. Instrumentation is still added.
    /// Used internally by <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/>.
    /// </summary>
    internal bool SkipExporter { get; set; }
}
