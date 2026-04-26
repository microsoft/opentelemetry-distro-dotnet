// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// ────────────────────────────────────────────────────────────────────────────
// Console Demo — OpenTelemetrySdk.Create() API
//
// Demonstrates using the Microsoft OpenTelemetry distro with the recommended
// non-hosted approach: OpenTelemetrySdk.Create(). This provides a unified,
// multi-signal setup under a single disposable boundary — no DI container
// or host builder needed.
//
// Collects traces, metrics, and logs; exports to Console, OTLP, and
// Azure Monitor (when APPLICATIONINSIGHTS_CONNECTION_STRING is set).
// ────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;

// ── Custom telemetry sources ──
var activitySource = new ActivitySource("Demo.Console");
var meter = new Meter("Demo.Console");
var requestCounter = meter.CreateCounter<long>("demo.requests", description: "Number of demo requests processed");
var latencyHistogram = meter.CreateHistogram<double>("demo.request.duration", "ms", "Request processing latency");

// ── Create the SDK with the distro ──
// OpenTelemetrySdk.Create() is the recommended approach for non-hosted apps.
// It manages TracerProvider, MeterProvider, and LoggerFactory under one disposable.
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        // Export to Console, OTLP, and Azure Monitor.
        // Azure Monitor requires APPLICATIONINSIGHTS_CONNECTION_STRING env var.
        o.Exporters = ExportTarget.Console | ExportTarget.Otlp | ExportTarget.AzureMonitor;
        o.AzureMonitor.ConnectionString = "InstrumentationKey=66f50587-a821-4a96-8511-fa96e8f28fd7;IngestionEndpoint=https://westus2-1.in.applicationinsights.azure.com/;LiveEndpoint=https://westus2.livediagnostics.monitor.azure.com/;ApplicationId=b9fa6c07-f06f-414c-b222-f3207aab66cd";
    })
    .WithTracing(tracing => tracing.AddSource("Demo.Console"))
    .WithMetrics(metrics => metrics.AddMeter("Demo.Console"));
});

// Get a logger from the SDK's built-in LoggerFactory
var logger = sdk.GetLoggerFactory().CreateLogger("Demo.Console");

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Microsoft.OpenTelemetry Console Demo");
Console.WriteLine("  (OpenTelemetrySdk.Create — non-hosted)");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();

// ── Generate telemetry ──
for (int i = 1; i <= 3; i++)
{
    Console.WriteLine($"── Request {i} ──");

    // Trace: create a span
    using (var activity = activitySource.StartActivity($"ProcessRequest-{i}", ActivityKind.Server))
    {
        activity?.SetTag("request.id", i);
        activity?.SetTag("request.type", "demo");

        logger.LogInformation("Processing request {RequestId}", i);

        // Child span: simulate a database call
        var sw = Stopwatch.StartNew();
        using (var dbActivity = activitySource.StartActivity("DatabaseQuery", ActivityKind.Client))
        {
            dbActivity?.SetTag("db.system", "sqlite");
            dbActivity?.SetTag("db.statement", $"SELECT * FROM items WHERE id = {i}");
            await Task.Delay(50 + Random.Shared.Next(100));
        }

        // Child span: simulate an HTTP call
        using (var httpActivity = activitySource.StartActivity("ExternalApiCall", ActivityKind.Client))
        {
            httpActivity?.SetTag("http.method", "GET");
            httpActivity?.SetTag("http.url", $"https://api.example.com/items/{i}");
            httpActivity?.SetTag("http.status_code", 200);
            await Task.Delay(30 + Random.Shared.Next(70));
        }

        sw.Stop();

        // Metrics: record counters and histograms
        requestCounter.Add(1, new KeyValuePair<string, object?>("request.type", "demo"));
        latencyHistogram.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("request.type", "demo"));

        logger.LogInformation("Request {RequestId} completed in {Duration:F1}ms", i, sw.Elapsed.TotalMilliseconds);

        if (i == 2)
        {
            // Simulate an error on request 2
            activity?.SetStatus(ActivityStatusCode.Error, "Simulated error");
            logger.LogError("Simulated error on request {RequestId}", i);
        }
    }

    Console.WriteLine();
}

// ── Summary ──
// ForceFlush ensures Azure Monitor's batch exporter sends all pending telemetry
// before the process exits. Dispose alone may not wait long enough.
sdk.TracerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.LoggerProvider?.ForceFlush(timeoutMilliseconds: 10000);
sdk.MeterProvider?.ForceFlush(timeoutMilliseconds: 10000);

sdk.Dispose();
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Demo complete. Telemetry flushed.");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Exporters configured:");
Console.WriteLine("  • Console  — visible above");
Console.WriteLine("  • OTLP     — set OTEL_EXPORTER_OTLP_ENDPOINT (default: http://localhost:4317)");
Console.WriteLine("  • Azure Monitor — set APPLICATIONINSIGHTS_CONNECTION_STRING");
Console.WriteLine();
