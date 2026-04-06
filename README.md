# Microsoft.OpenTelemetry

A unified OpenTelemetry distribution for .NET that provides one-line onboarding for ASP.NET Core, AI agent workloads, and Microsoft Agent Framework apps.

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
});

var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
```

## Features

### Instrumentation (always active)

- **ASP.NET Core** — incoming HTTP requests
- **HTTP Client** — outbound HTTP calls (with Azure SDK dedup filter)
- **SQL Client** — database queries
- **Resource Detection** — Azure App Service, VM, Container Apps
- **Agent365 Scopes** — `InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope`
- **Agent365 Baggage** — per-request context propagation (tenant, agent, session)
- **Microsoft Agent Framework** — captures `Experimental.Microsoft.Agents.AI` activity sources
- **Azure SDK Log Forwarding** — bridges Azure SDK EventSource logs to ILogger
- **Metrics** — `Microsoft.AspNetCore.Hosting`, `System.Net.Http`

### Exporters (customer selects)

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    // Select exporters explicitly
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365 | ExportTarget.Otlp;

    // Azure Monitor
    o.AzureMonitor.ConnectionString = "...";

    // Agent365
    o.Agent365.Exporter.TokenResolver = (agentId, tenantId) => GetTokenAsync(agentId, tenantId);
    o.Agent365.WithSemanticKernel().WithOpenAI();

    // OTLP (Aspire Dashboard, Jaeger, Grafana)
    o.OtlpEndpoint = new Uri("http://localhost:4317");
});
```

Exporters auto-detect when not explicitly set:
- **Azure Monitor** — enabled when `ConnectionString` is set (code, env var `APPLICATIONINSIGHTS_CONNECTION_STRING`, or `IConfiguration`)
- **Agent365** — enabled when `TokenResolver` is set
- **OTLP** — enabled when `OtlpEndpoint` is set

### Agent365 Extensions (opt-in)

```csharp
o.Agent365.WithSemanticKernel();       // Semantic Kernel tracing
o.Agent365.WithAgentFramework();       // Agent Framework span processor
o.Agent365.WithOpenAI();               // Azure OpenAI SDK tracing
```

## Alternative Entry Points

### On `IOpenTelemetryBuilder` (chainable)

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o => { ... });
```

### Individual (composable)

```csharp
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o => o.ConnectionString = "...")
    .UseAgent365(o => o.WithSemanticKernel())
    .UseAgentFramework();
```

## Project Structure

```
src/Microsoft.OpenTelemetry/
├── AzureMonitor/           — Azure Monitor distro (instrumentation + exporter)
├── Agent365/               — Agent365 observability (scopes, baggage, exporter, extensions)
├── AgentFramework/         — Microsoft Agent Framework span capture
├── MicrosoftOpenTelemetryBuilderExtensions.cs  — Unified entry point
├── MicrosoftOpenTelemetryOptions.cs            — Unified options
├── ExportTarget.cs                              — Exporter selection enum
└── Agent365Options.cs / Agent365OpenTelemetryBuilderExtensions.cs

test/
├── Microsoft.OpenTelemetry.AzureMonitor.Tests/  — Azure Monitor tests
└── Microsoft.OpenTelemetry.Agent365.Tests/      — Agent365 tests

examples/
└── Azure.Monitor.OpenTelemetry.AspNetCore.Demo/ — Demo app
```

## Build & Test

```bash
dotnet build src/Microsoft.OpenTelemetry.slnx
dotnet test src/Microsoft.OpenTelemetry.slnx
```

## Packages

| Package | Version |
|---------|---------|
| OpenTelemetry | 1.15.1 |
| Azure.Monitor.OpenTelemetry.Exporter | 1.7.0 |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.1 |
| OpenTelemetry.Instrumentation.Http | 1.15.0 |
| OpenTelemetry.Instrumentation.SqlClient | 1.15.1 |
| Microsoft.SemanticKernel | 1.71.0 |
| Azure.AI.OpenAI | 2.7.0-beta.2 |
