// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Marker class registered as a singleton to indicate that a custom (shim) Agent365 exporter
/// has been registered. When detected, the built-in Agent365 exporter is skipped.
/// </summary>
internal sealed class CustomAgent365ExporterMarker
{
    public static readonly CustomAgent365ExporterMarker Instance = new();

    private CustomAgent365ExporterMarker()
    {
    }
}
