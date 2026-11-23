# Azure Monitor OpenTelemetry ASP.NET Core Demo

An ASP.NET Core application demonstrating `Microsoft.OpenTelemetry` distro usage with Azure Monitor as the primary exporter.

## What it demonstrates

- Azure Monitor exporter for traces, metrics, and logs
- ASP.NET Core, HttpClient, and SQL Client auto-instrumentation
- Resource detection (Azure VM, App Service, Container Apps)
- Connection string configuration via `APPLICATIONINSIGHTS_CONNECTION_STRING`

## Prerequisites

- .NET 11 SDK, plus the .NET 8 and ASP.NET Core 8 runtimes (the demo targets `net8.0` and builds the distro from source)
- Application Insights connection string

## Configuration

Set the connection string via environment variable:

```bash
set APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...
```

Or in `appsettings.json`:

```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=..."
}
```

## Run

```bash
dotnet run
```

Telemetry is sent to Application Insights. View in the Azure portal under your Application Insights resource.
