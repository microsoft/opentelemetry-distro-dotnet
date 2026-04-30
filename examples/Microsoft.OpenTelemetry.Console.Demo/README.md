# Microsoft.OpenTelemetry Console Demo

A minimal console application demonstrating `Microsoft.OpenTelemetry` distro usage without ASP.NET Core hosting, using `OpenTelemetrySdk.Create()`.

## What it demonstrates

- Non-hosted entry point via `OpenTelemetrySdk.Create()` + `UseMicrosoftOpenTelemetry()`
- Console + OTLP + Azure Monitor exporters
- Manual span creation with `ActivitySource`
- `ForceFlush` for short-lived processes

## Prerequisites

- .NET 8.0+
- (Optional) Azure OpenAI API key for LLM spans
- (Optional) Application Insights connection string for Azure Monitor

## Run

```bash
dotnet run --project Microsoft.OpenTelemetry.Console.Demo.csproj
```

Traces are printed to the console. No web server is started.
