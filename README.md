# Microsoft.OpenTelemetry

A unified OpenTelemetry distribution for .NET. One-line onboarding for ASP.NET Core apps, [Microsoft Agent Framework](https://github.com/microsoft/agent-framework), and [Agent365](https://learn.microsoft.com/en-us/microsoft-agent-365/) — Microsoft's managed observability backend for AI agents.

**Targets:** `net8.0`, `net10.0`

## Install

```bash
dotnet add package Microsoft.OpenTelemetry
```

## Quick Start

Send Agent Framework traces to Agent365 in one call:

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Agent365 auto-enables when the Microsoft.Agents.A365.Observability.Hosting
        // token cache is registered by your app, OR when you set a TokenResolver:
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId)
            => tokenProvider.GetTokenAsync(agentId, tenantId);
    });

var app = builder.Build();
app.MapPost("/api/messages", () => Results.Ok());
app.Run();
```

> A shorthand `builder.UseMicrosoftOpenTelemetry(...)` extension on `WebApplicationBuilder` is also available and is equivalent to the form above.

Not using Agent Framework / Agent365? Jump to [Azure Monitor](#3-azure-monitor-aspnet-core--worker--console) or the [Options reference](#options-reference).

## Signals & destinations

| Signal | Azure Monitor | Agent365 | OTLP |
|---|:---:|:---:|:---:|
| Traces | ✅ | ✅ | ✅ |
| Metrics | ✅ | — | ✅ |
| Logs | ✅ | — | ✅ |

Exporters auto-detect when `Exporters` isn't set:
- **Azure Monitor** — enabled when `ConnectionString` is set (code, env var `APPLICATIONINSIGHTS_CONNECTION_STRING`, or `IConfiguration`).
- **Agent365** — enabled when `TokenResolver` is set (or when the DI token cache is registered by `Microsoft.Agents.A365.Observability.Hosting`).
- **OTLP** — enabled when `OtlpEndpoint` is set.

## Instrumentation (always active)

- ASP.NET Core incoming HTTP requests
- HTTP client outbound calls (with Azure SDK dedup filter)
- SQL client queries
- Resource detection (Azure App Service, VM, Container Apps)
- Agent365 scopes (`InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope`) and baggage propagation
- Microsoft Agent Framework — `Experimental.Microsoft.Agents.AI` activity source
- Azure SDK EventSource → `ILogger` log forwarding
- Metrics — `Microsoft.AspNetCore.Hosting`, `System.Net.Http`

## Onboarding by scenario

Pick the section that matches your workload. All three can be combined in the same app.

---

### 1. Agent365

Send agent telemetry (invoke agent, inference, tool execution, output) to the [Agent365](https://learn.microsoft.com/en-us/microsoft-agent-365/) observability backend.

**Prerequisites**
- An Agent365 tenant and agent identity — see the [Agent365 developer docs](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/).
- Either:
  - **Auto-managed tokens (recommended for Agent Framework apps):** the `Microsoft.Agents.A365.Observability.Hosting` package registers a token cache via DI — no code needed on your side.
  - **Custom token resolver:** an async function that returns a bearer token for `(agentId, tenantId)`.
- NuGet packages: `Microsoft.OpenTelemetry`, `Microsoft.Agents.Builder`.

**Setup**

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Custom token resolver (skip if using auto-managed tokens)
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId)
            => tokenProvider.GetTokenAsync(agentId, tenantId);
    });
```

**What you get**
- **Scopes**: `InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope`
- **Baggage**: per-request tenant / agent / session context propagation
- **Exporter**: authenticated export to Agent365 endpoint

**Composable alternative**

```csharp
builder.Services.AddOpenTelemetry()
    .UseAgent365(o =>
    {
        o.Exporter.TokenResolver = (agentId, tenantId) => tokenProvider.GetTokenAsync(agentId, tenantId);
    });
```

See [examples/Microsoft.OpenTelemetry.Agent365.Demo](examples/Microsoft.OpenTelemetry.Agent365.Demo).

---

### 2. Microsoft Agent Framework

Capture activity from the `Experimental.Microsoft.Agents.AI` activity source and export to your backend of choice.

**Prerequisites**
- An app that uses `Microsoft.Agents.AI` (Agent Framework).
- An export destination (Azure Monitor connection string, OTLP collector, or Agent365 identity).
- NuGet package: `Microsoft.OpenTelemetry`.

**Setup — Agent Framework → Azure Monitor**

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
        // Agent Framework activity source is already captured — no extra flag needed.
    });
```

**Setup — Agent Framework → OTLP (Aspire Dashboard, Jaeger, Grafana Tempo)**

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.OtlpEndpoint = new Uri("http://localhost:4317");
    });
```

**Setup — Agent Framework → Agent365**

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId)
            => tokenProvider.GetTokenAsync(agentId, tenantId);
    });
```

**Composable alternative**

```csharp
builder.Services.AddOpenTelemetry()
    .UseAgentFramework();
```

See [examples/Microsoft.OpenTelemetry.AgentFramework.Demo](examples/Microsoft.OpenTelemetry.AgentFramework.Demo).

---

### 3. Azure Monitor (ASP.NET Core / Worker / Console)

Send traces, metrics, and logs to Application Insights / Azure Monitor.

**Prerequisites**
- An Application Insights resource — copy its **Connection String** from the Azure portal.
- NuGet package: `Microsoft.OpenTelemetry`.

**Setup**

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.AzureMonitor.ConnectionString =
            builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    });
```

**Configuration sources**

| Source | Key |
|---|---|
| Environment variable | `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| `appsettings.json` | `"APPLICATIONINSIGHTS_CONNECTION_STRING": "..."` |
| Code | `o.AzureMonitor.ConnectionString = "..."` |

**Composable alternative**

```csharp
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o => o.ConnectionString = "InstrumentationKey=...");
```

See [examples/Azure.Monitor.OpenTelemetry.AspNetCore.Demo](examples/Azure.Monitor.OpenTelemetry.AspNetCore.Demo).

---

### Combining destinations

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Agent365 | ExportTarget.Otlp;

        o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId)
            => tokenProvider.GetTokenAsync(agentId, tenantId);
        o.OtlpEndpoint = new Uri("http://localhost:4317");
    });
```

## Options reference

Full surface of `MicrosoftOpenTelemetryOptions`. Everything is opt-in; values shown are defaults.

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // --- Export targets (pick one or combine with |) ---
        o.Exporters = ExportTarget.Console         // Console output (dev)
                    | ExportTarget.Agent365        // Agent365 observability platform
                    | ExportTarget.AzureMonitor    // Application Insights
                    | ExportTarget.Otlp;           // OTLP (Aspire, Jaeger, Grafana)

        // --- Agent365 exporter settings ---

        // Option A: Auto-managed tokens via DI (recommended for Agent Framework apps).
        // The Microsoft.Agents.A365.Observability.Hosting package registers
        // IExporterTokenCache<AgenticTokenStruct>. Tokens are exchanged
        // per request via ExchangeTurnTokenAsync — no TokenResolver needed.

        // Option B: Custom token resolver (non-agent apps, S2S, custom auth)
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            return await MyTokenService.GetTokenAsync(agentId, tenantId);
        };

        // Optional: custom domain resolver (default: agent365.svc.cloud.microsoft)
        o.Agent365.Exporter.DomainResolver = tenantId => "agent365.svc.cloud.microsoft";

        // Optional: use S2S endpoint path
        o.Agent365.Exporter.UseS2SEndpoint = false;

        // Optional: batch export tuning
        o.Agent365.Exporter.MaxQueueSize = 2048;
        o.Agent365.Exporter.MaxExportBatchSize = 512;
        o.Agent365.Exporter.ScheduledDelayMilliseconds = 5000;
        o.Agent365.Exporter.ExporterTimeoutMilliseconds = 30000;

        // --- Azure Monitor settings ---
        o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
        o.AzureMonitor.SamplingRatio = 1.0f;
        o.AzureMonitor.EnableLiveMetrics = true;

        // --- OTLP settings ---
        o.OtlpEndpoint = new Uri("http://localhost:4317");
    });
```

### Token resolver: auto vs custom

| Approach | When to use | How it works |
|---|---|---|
| **Auto (DI) — default** | Agent Framework apps that reference `Microsoft.Agents.A365.Observability.Hosting` | `IExporterTokenCache<AgenticTokenStruct>` is registered automatically. Per-request token exchange happens via `ExchangeTurnTokenAsync`. |
| **Custom resolver** | Non-agent apps, service-to-service, or custom auth | Set `o.Agent365.Exporter.TokenResolver` directly. You own token acquisition. |

> If `TokenResolver` is set explicitly, the auto DI token cache is **not** registered — your resolver wins.

## Verify it works

Send a request through your agent (e.g., a Teams message). In the console output you should see activity spans from the instrumented sources:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

And a successful Agent365 export:

```
Received HTTP response headers after *ms - 200
```

## Internal logging

The distro's internal components (exporters, span processors) use `ILoggerFactory` from DI when available.
In ASP.NET Core and hosted apps, this means internal diagnostics flow through the app's configured logging pipeline automatically.

**Non-DI / console apps:** If your app does not register `ILoggerFactory` in DI, internal diagnostics are silently discarded (`NullLoggerFactory`). To see internal log output, add `Microsoft.Extensions.Logging.Console` and wire it up:

```bash
dotnet add package Microsoft.Extensions.Logging.Console
```

```csharp
builder.Services.AddLogging(logging => logging.AddConsole());
```

## Documentation

- [Agent 365 Observability Guide](docs/agent365-observability.md) — Comprehensive guide for adding Agent365 observability using the distro
- [Migrating from Agent365 SDK](docs/testing-agent365.md) — Migration guide from the standalone Agent365 SDK to the distro
- [Agent365 Observability Reference](docs/observability-agent365.md) — Quick-reference with before/after code, span attributes, and troubleshooting

## Examples

- [Azure.Monitor.OpenTelemetry.AspNetCore.Demo](examples/Azure.Monitor.OpenTelemetry.AspNetCore.Demo) — ASP.NET Core → Azure Monitor
- [Microsoft.OpenTelemetry.Agent365.Demo](examples/Microsoft.OpenTelemetry.Agent365.Demo) — Agent Framework app → Agent365
- [Microsoft.OpenTelemetry.AgentFramework.Demo](examples/Microsoft.OpenTelemetry.AgentFramework.Demo) — Agent Framework → OTLP / Azure Monitor

## Build & test

```bash
dotnet build Microsoft.OpenTelemetry.slnx
dotnet test Microsoft.OpenTelemetry.slnx
```

## Microsoft Open Source Code of Conduct

This project has adopted the
[Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more
information see the
[Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/)
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com)
with any additional questions or comments.

## Data Collection

As this SDK is designed to enable applications to perform data collection which is sent to the Microsoft collection endpoints the following is required to identify our privacy statement.

The software may collect information about you and your use of the software and send it to Microsoft. Microsoft may use this information to provide services and improve our products and services. You may turn off the telemetry as described in the repository. There are also some features in the software that may enable you and Microsoft to collect data from users of your applications. If you use these features, you must comply with applicable law, including providing appropriate notices to users of your applications together with a copy of Microsoft’s privacy statement. Our privacy statement is located at https://go.microsoft.com/fwlink/?LinkID=824704. You can learn more about data collection and use in the help documentation and our privacy statement. Your use of the software operates as your consent to these practices.

### Internal Telemetry

Internal telemetry can be disabled by setting the environment variable `APPLICATIONINSIGHTS_STATSBEAT_DISABLED` to `true`.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow [Microsoft’s Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general). Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party’s policies.

## Reporting Security Issues

See [SECURITY.md](SECURITY.md).
