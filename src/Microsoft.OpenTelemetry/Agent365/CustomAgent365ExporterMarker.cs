// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Marker class registered as a singleton to indicate that a custom Agent365 exporter
/// has been registered. When detected, the built-in Agent365 exporter is skipped,
/// but all A365 instrumentation (sampler, activity sources, processors) and
/// <see cref="Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterOptions"/>
/// remain available in DI.
/// </summary>
/// <remarks>
/// <para>
/// Register <see cref="Instance"/> as a singleton in the DI container before building the host
/// to signal that a custom exporter will handle A365 span export:
/// </para>
/// <code>
/// services.AddSingleton(CustomAgent365ExporterMarker.Instance);
/// </code>
/// </remarks>
public sealed class CustomAgent365ExporterMarker
{
    /// <summary>
    /// The singleton instance to register in DI.
    /// </summary>
    public static readonly CustomAgent365ExporterMarker Instance = new();

    private CustomAgent365ExporterMarker()
    {
    }
}
