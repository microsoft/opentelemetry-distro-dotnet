# Testing Microsoft.OpenTelemetry Distro — Agent Framework (MAF) Team

## Package

```
Microsoft.OpenTelemetry 1.0.0-alpha.1
```

Available at: `c:\aidistro-repos\local-nuget\` (local feed) or request from distro team.

## What the distro provides

For Agent Framework apps, the distro gives you one-line OpenTelemetry setup with:

- Correct `ActivitySource` registration for `Experimental.Microsoft.Extensions.AI` and `Experimental.Microsoft.Agents.AI`
- `ParentBasedSampler(AlwaysOnSampler)` — ensures gen_ai spans aren't dropped in async processing
- Console exporter, Azure Monitor exporter, and/or Agent365 exporter
- Baggage-to-tags propagation via `ActivityProcessor`
- Semantic Kernel span processor (if using SK)

## Setup

### 1. Add NuGet source

Create `nuget.config` in your project directory:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-distro" value="c:\aidistro-repos\local-nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### 2. Add package to .csproj

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="1.0.0-alpha.1" />

<!-- Keep your existing Agent Framework packages -->
<PackageReference Include="Microsoft.Agents.AI" Version="1.0.0" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0" />
```

You do **not** need to add individual OpenTelemetry packages — the distro includes them.

### 3. Program.cs — Minimal setup

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

// One-line distro setup — includes all OTel instrumentation + correct samplers
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console;
});

var app = builder.Build();
```

If you need to add custom activity sources, use the longer form:

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyApp.CustomSource"));
```

> **Note:** `builder.UseMicrosoftOpenTelemetry()` internally calls `builder.Services.AddOpenTelemetry().UseMicrosoftOpenTelemetry()`. Use the short form unless you need `.WithTracing()`.

### 4. Create your agent with UseOpenTelemetry()

```csharp
using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(
        instructions: "You are a helpful assistant.",
        tools: [AIFunctionFactory.Create(GetWeather)])
    .AsBuilder()
    .UseOpenTelemetry()    // <-- This creates the gen_ai spans
    .Build();
```

### 5. UseMicrosoftOpenTelemetry options

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    // Export targets (combine with |)
    o.Exporters = ExportTarget.Console              // Console output (dev)
                | ExportTarget.AzureMonitor;        // Application Insights

    // Azure Monitor (requires APPLICATIONINSIGHTS_CONNECTION_STRING env var, or set here)
    // o.AzureMonitor.ConnectionString = "InstrumentationKey=...";

    // Agent365 exporter (for A365 platform — requires token resolver)
    // o.Exporters |= ExportTarget.Agent365;
    // o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    //     await MyTokenService.GetTokenAsync(agentId, tenantId);
});
```

If `Exporters` is not set explicitly, exporters are **auto-detected** from configuration:
- `AzureMonitor` — enabled if `APPLICATIONINSIGHTS_CONNECTION_STRING` is found
- `Agent365` — enabled if `TokenResolver` is set

### 6. Set environment variables

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://your-endpoint.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY = "your-key"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o-mini"
```

### 7. Run

```powershell
dotnet run --no-launch-profile
```

> **Important:** Use `--no-launch-profile` if your `launchSettings.json` has empty environment variable overrides — they will blank out your env vars.

## Non-DI setup (Console apps, tests, background jobs)

If you're not using `WebApplicationBuilder` or the Generic Host, use `OpenTelemetrySdk.Create()` directly. This doesn't require the distro package — just the standard OpenTelemetry packages:

```csharp
using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// --- OTel setup (non-DI) ---
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.ConfigureResource(r => r.AddService("my-agent-app"));

    otel.WithTracing(tracing => tracing
        .SetSampler(new ParentBasedSampler(
            rootSampler: new AlwaysOnSampler(),
            localParentNotSampled: new AlwaysOnSampler(),
            remoteParentNotSampled: new AlwaysOnSampler()))
        .AddSource("Experimental.Microsoft.Extensions.AI")
        .AddSource("Experimental.Microsoft.Agents.AI")
        .AddConsoleExporter());

    otel.WithMetrics(metrics => metrics
        .AddMeter("Experimental.Microsoft.Agents.AI")
        .AddMeter("Experimental.Microsoft.Extensions.AI")
        .AddConsoleExporter());
});

// --- Create and use your agent ---
AIAgent agent = new AzureOpenAIClient(
        new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!),
        new ApiKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!))
    .GetChatClient(Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini")
    .AsIChatClient()
    .AsAIAgent(instructions: "You are a helpful assistant.")
    .AsBuilder()
    .UseOpenTelemetry()
    .Build();

var response = await agent.RunAsync("What is 2+2?");
Console.WriteLine(response);
```

**Key points for non-DI:**
- `OpenTelemetrySdk.Create()` manages lifecycle — `using var` ensures flush on exit
- The `ParentBasedSampler(AlwaysOnSampler)` is critical — without it, orphan root spans may be dropped
- ActivitySource names must match exactly: `Experimental.Microsoft.Extensions.AI` and `Experimental.Microsoft.Agents.AI`
- No distro package needed — use `OpenTelemetry`, `OpenTelemetry.Exporter.Console` (or `.OpenTelemetryProtocol`) directly

## Expected spans

Send a request to your `/chat` endpoint (check console output for the actual port):

```powershell
Invoke-RestMethod -Uri "http://localhost:<port>/chat" -Method POST `
    -ContentType "application/json" `
    -Body '{"message":"What is the weather in Seattle?"}'
```

Console output should show:

| Span | Source | Description |
|------|--------|-------------|
| `invoke_agent *` | `Experimental.Microsoft.Agents.AI` | Agent invocation |
| `execute_tool *` | `Experimental.Microsoft.Agents.AI` | Tool call (if tools configured) |
| `chat gpt-*` | `Experimental.Microsoft.Extensions.AI` | LLM call (if `.UseOpenTelemetry()` on IChatClient too) |
| `POST` | `System.Net.Http` | HTTP call to Azure OpenAI |
| `POST /chat` | `Microsoft.AspNetCore.Hosting` | Your endpoint |

## Export targets

| Target | When to use | Requires |
|--------|-------------|----------|
| `ExportTarget.Console` | Local development / debugging | Nothing |
| `ExportTarget.AzureMonitor` | Application Insights | `APPLICATIONINSIGHTS_CONNECTION_STRING` env var |
| `ExportTarget.Agent365` | Agent365 observability platform | Token resolver + agent registration |

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.AgentFramework.Demo/` in the distro repo.

This is a minimal WebAPI app with a `/chat` endpoint, weather tool, and `UseOpenTelemetry()` on the agent — no Bot Framework, no dev tunnel, no Agent365 auth.
