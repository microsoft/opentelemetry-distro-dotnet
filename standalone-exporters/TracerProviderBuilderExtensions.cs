// Copyright (c) Microsoft Corporation. All rights reserved.

using OpenTelemetry.Trace;

namespace A365.OpenTelemetry.Exporter;

/// <summary>Extension methods for registering the A365 exporter with a TracerProviderBuilder.</summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>Add the A365 span exporter to the tracer provider pipeline.</summary>
    public static TracerProviderBuilder AddA365Exporter(
        this TracerProviderBuilder builder,
        Action<A365ExporterOptions>? configure = null)
    {
        var options = new A365ExporterOptions();
        configure?.Invoke(options);

        return builder.AddProcessor(
            new SimpleActivityExportProcessor(new A365SpanExporter(options)));
    }
}
