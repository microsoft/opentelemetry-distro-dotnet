// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// ────────────────────────────────────────────────────────────────────────────
// Fabric Demo — OTLP → OTel Collector → Azure Data Explorer / Fabric
//
// Demonstrates using the Microsoft OpenTelemetry distro with the OTLP
// exporter to send traces, metrics, and logs to Microsoft Fabric (Real-Time
// Intelligence) or Azure Data Explorer via an OpenTelemetry Collector.
//
// The app exports via OTLP to a local or remote OTel Collector configured
// with the Azure Data Explorer exporter. See the companion collector-config.yaml.
//
// Set OTEL_EXPORTER_OTLP_ENDPOINT to point to your collector (default: http://localhost:4317).
// ────────────────────────────────────────────────────────────────────────────

using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    // Export via OTLP to the OTel Collector.
    // The collector forwards to Azure Data Explorer / Fabric.
    o.Exporters = ExportTarget.Otlp;
});

var app = builder.Build();

app.MapGet("/", () => "Hello from Microsoft.OpenTelemetry → Fabric!");
app.MapGet("/weather", () => new { Temp = 72, City = "Seattle", Unit = "F" });

app.Run();
