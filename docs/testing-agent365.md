# Testing Microsoft.OpenTelemetry Distro — Agent365 Team

## Package

```
Microsoft.OpenTelemetry (latest version)
```

Available at: `c:\aidistro-repos\local-nuget\` (local feed) or request from distro team.

## What changes

The distro replaces the Agent365 observability packages **and** the raw OpenTelemetry packages with a single package. Namespaces are identical to the Agent365 SDK — no `using` changes needed in your agent code.

### Packages to REMOVE from .csproj

```xml
<!-- Remove all of these -->
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.AgentFramework" Version="0.2.151-beta" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />
```

### Package to ADD

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

### Packages to KEEP (unchanged)

```xml
<PackageReference Include="Microsoft.Agents.A365.Notifications" Version="0.2.151-beta" />
<PackageReference Include="Microsoft.Agents.A365.Tooling.Extensions.AgentFramework" Version="0.2.151-beta" />
<!-- ... all other non-observability packages stay as-is -->
```

## Program.cs changes

### BEFORE (Agent365 SDK)

```csharp
using Microsoft.Agents.A365.Observability;
using Microsoft.Agents.A365.Observability.Extensions.AgentFramework;
using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureOpenTelemetry();

builder.Services.AddSingleton(new Agent365ExporterOptions
{
    TokenResolver = (agentId, tenantId) => Task.FromResult(TokenStore.GetToken(agentId, tenantId))
});

builder.AddA365Tracing(config =>
{
    config.WithAgentFramework();
});
```

### AFTER (Distro)

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;

    // Option A (recommended): Let the distro auto-manage tokens via DI.
    // IExporterTokenCache<AgenticTokenStruct> is registered automatically.
    // Your A365OtelWrapper calls RegisterObservability() at runtime.

    // Option B: Provide your own token resolver (matches old Agent365ExporterOptions pattern)
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
    {
        return await MyTokenService.GetTokenAsync(agentId, tenantId);
    };
});
```

If you need to add your own application-specific activity sources, use the longer form:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyAgent.CustomSource"));
```

> Activity sources for Agent Framework, Semantic Kernel, and OpenAI are auto-registered — you only need `.AddSource()` for your own custom sources.

> ⚠️ **Production warning:** `ExportTarget.Console` is intended for local development only. Do not include it in production deployments — it adds overhead and may log sensitive telemetry to stdout. Use `ExportTarget.Agent365` and/or `ExportTarget.AzureMonitor` in production.

### What to REMOVE from Program.cs

- `builder.ConfigureOpenTelemetry();` — replaced by `UseMicrosoftOpenTelemetry()`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });` — token cache is auto-registered via DI
- `builder.AddA365Tracing(config => { ... });` — replaced by `UseMicrosoftOpenTelemetry()`
- `using` statements for `Microsoft.Agents.A365.Observability.*` in Program.cs — replace with `using Microsoft.OpenTelemetry;` and `using OpenTelemetry;`
- `TokenStore` class file — delete it if present. Use `o.Agent365.Exporter.TokenResolver` directly, or let the distro auto-manage tokens via `IExporterTokenCache<AgenticTokenStruct>` (injected via DI)
- `ConfigureOpenTelemetry()` extension method file (if you have one) — delete it, the distro handles all OTel setup

### What to REMOVE from A365OtelWrapper.cs

The `TokenStore.SetToken(...)` / `TokenStore.GetToken(...)` calls are replaced. Instead, inject `IExporterTokenCache<AgenticTokenStruct>` via the constructor:

```csharp
// BEFORE: TokenStore.SetToken(agentId, tenantId, token);
// AFTER:  (inject via constructor)
private readonly IExporterTokenCache<AgenticTokenStruct>? _agentTokenCache;

// Then in your method:
_agentTokenCache?.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
    userAuthorization: authSystem,
    turnContext: turnContext,
    authHandlerName: authHandlerName
), EnvironmentUtils.GetObservabilityAuthenticationScope());
```

### What to ADD

- `using Microsoft.OpenTelemetry;`
- The `UseMicrosoftOpenTelemetry()` block shown above

## Agent code (MyAgent.cs, A365OtelWrapper.cs)

**No changes needed** to `using` statements in agent code. The distro uses the same `Microsoft.Agents.A365.Observability.*` namespaces as the Agent365 SDK:

```csharp
// These usings still work — namespaces are identical
using Microsoft.Agents.A365.Observability.Hosting.Caching;  // IExporterTokenCache, AgenticTokenStruct
using Microsoft.Agents.A365.Observability.Runtime.Common;    // BaggageBuilder, EnvironmentUtils
```

### Token registration pattern

The `A365OtelWrapper.RegisterObservability` call stays the same:

```csharp
agentTokenCache.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
    userAuthorization: authSystem,
    turnContext: turnContext,
    authHandlerName: authHandlerName
), EnvironmentUtils.GetObservabilityAuthenticationScope());
```

The only difference: `IExporterTokenCache<AgenticTokenStruct>` comes from DI automatically (no need to manually create `Agent365ExporterOptions` or `TokenStore`).

## NuGet source setup

Add to your `nuget.config` (or create one in the project directory):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-distro" value="c:\aidistro-repos\local-nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

## Run and verify

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run
```

## UseMicrosoftOpenTelemetry options reference

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // --- Export targets (pick one or combine with |) ---
        o.Exporters = ExportTarget.Console          // Console output (dev)
                    | ExportTarget.Agent365          // Agent365 observability platform
                    | ExportTarget.AzureMonitor;     // Application Insights

        // --- Agent365 exporter settings ---

        // Option A: Let the distro auto-manage tokens via DI (recommended)
        // IExporterTokenCache<AgenticTokenStruct> is registered automatically.
        // Your A365OtelWrapper calls RegisterObservability() at runtime.
        // No TokenResolver needed here — it's wired internally.

        // Option B: Provide your own token resolver (advanced/custom)
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            // Return a valid bearer token for the given agent/tenant
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
        // o.AzureMonitor.ConnectionString = "InstrumentationKey=...";
        // o.AzureMonitor.SamplingRatio = 1.0f;
        // o.AzureMonitor.EnableLiveMetrics = true;
    });
```

### Token resolver: auto vs custom

| Approach | When to use | How it works |
|----------|-------------|--------------|
| **Auto (DI)** — default | Agent Framework apps with `UserAuthorization` | Distro registers `IExporterTokenCache<AgenticTokenStruct>`. Your `A365OtelWrapper` calls `RegisterObservability()`. Token exchange happens via `ExchangeTurnTokenAsync`. |
| **Custom resolver** | Non-agent apps, service-to-service, or custom auth | Set `o.Agent365.Exporter.TokenResolver` directly. You own token acquisition. |

If you set `TokenResolver` explicitly, the auto DI token cache is **not** registered — your resolver is used instead.

Send a message via Teams. In the console output, look for:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

And A365 export:
```
Received HTTP response headers after *ms - 200
```

## Auto-instrumentation migration

The old Agent365 SDK required explicit per-framework instrumentation calls (`WithSemanticKernel()`, `WithOpenAI()`, `WithAgentFramework()`). The distro replaces these with **auto-detection** — all frameworks are instrumented by default via `InstrumentationOptions` flags.

### What replaces what

| Old (Agent365 SDK) | New (Distro) | Default |
|---|---|---|
| `config.WithAgentFramework()` | `InstrumentationOptions.EnableAgentFrameworkInstrumentation` | `true` (auto) |
| `config.WithSemanticKernel()` | `InstrumentationOptions.EnableSemanticKernelInstrumentation` | `true` (auto) |
| `config.WithOpenAI()` | `InstrumentationOptions.EnableOpenAIInstrumentation` | `true` (auto) |
| Manual `AddAspNetCoreInstrumentation()` | `InstrumentationOptions.EnableAspNetCoreInstrumentation` | `true` (auto) |
| Manual `AddHttpClientInstrumentation()` | `InstrumentationOptions.EnableHttpClientInstrumentation` | `true` (auto) |
| Manual `AddSqlClientInstrumentation()` | `InstrumentationOptions.EnableSqlClientInstrumentation` | `true` (auto) |

### Disabling a specific framework

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
        o.Instrumentation.EnableSemanticKernelInstrumentation = false; // disable SK if not used
    });
```

### ActivitySources registered by the distro

| Source | Framework | Notes |
|---|---|---|
| `Agent365Sdk` | Agent365 scopes | InvokeAgent, ExecuteTool, Inference, Output |
| `Microsoft.SemanticKernel*` | Semantic Kernel | Wildcard match |
| `Azure.AI.OpenAI*` | Azure OpenAI | Wildcard match |
| `OpenAI.*` | OpenAI | Wildcard match |
| `Experimental.Microsoft.Extensions.AI` | Microsoft.Extensions.AI | LLM calls via IChatClient |
| `Experimental.Microsoft.Agents.AI` | Microsoft Agent Framework | Agent operations |
| `Experimental.Microsoft.Agents.AI.Agent` | Microsoft Agent Framework | Agent-level telemetry |
| `Experimental.Microsoft.Agents.AI.ChatClient` | Microsoft Agent Framework | Chat client telemetry |
| `Azure.*` | Azure SDK | Via Azure Monitor integration |

## Console exporter for local validation

For local development without Agent365 connectivity, use `ExportTarget.Console` only:

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console;
});

var app = builder.Build();
// ... configure endpoints ...
app.Run();
```

Run with:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-launch-profile
```

You should see spans printed to the console like:

```
Activity.TraceId:            abc123...
Activity.SpanId:             def456...
Activity.DisplayName:        invoke_agent MyAgent
Activity.Kind:               Client
Activity.Tags:
    gen_ai.agent.id: agent-456
    microsoft.tenant.id: tenant-123
    gen_ai.operation.name: invoke_agent
```

## Span comparison: before vs. after migration

### Before (Agent365 SDK)

| Span | Source |
|---|---|
| `AddA365Tracing` custom spans | `Agent365.Observability` |
| SK/OpenAI spans (if `.WithSemanticKernel()` / `.WithOpenAI()` called) | Various |
| No ASP.NET Core / HTTP spans unless manually added | — |

### After (Distro)

| Span | Source | Description |
|---|---|---|
| `invoke_agent *` | `Agent365Sdk` | Agent invocation (parent span) |
| `execute_tool *` | `Agent365Sdk` | Tool/function calls |
| `chat *` / `text_completion *` | `Agent365Sdk` | LLM inference calls (name from `InferenceOperationType`) |
| `output_messages` | `Agent365Sdk` | Final output (async scenarios) |
| `chat gpt-*` | `Experimental.Microsoft.Extensions.AI` | LLM call via IChatClient |
| `POST /chat` | `Microsoft.AspNetCore.Hosting` | ASP.NET Core endpoint (auto) |
| `POST` | `System.Net.Http` | HTTP calls to Azure OpenAI (auto) |
| SK function spans | `Microsoft.SemanticKernel*` | Semantic Kernel operations (auto) |

**Key difference:** The distro auto-registers ASP.NET Core, HTTP, and SQL instrumentation — the old SDK required manual setup for these.

## New features and breaking changes

### New features

- **Unified single-package setup** — one `Microsoft.OpenTelemetry` package replaces 6+ packages
- **Auto-instrumentation** — all frameworks instrumented by default (no explicit `.With*()` calls)
- **DI-based token cache** — `IExporterTokenCache<AgenticTokenStruct>` auto-registered; no manual `TokenStore`
- **ParentBasedSampler(AlwaysOnSampler)** — ensures gen_ai spans aren't dropped in async processing (Bot Framework returns HTTP 202 immediately; LLM calls happen asynchronously with no parent Activity)
- **SemanticKernelSpanProcessor** — auto-normalizes SK spans to Agent365 schema
- **AgentFrameworkSpanProcessor** — auto-processes MAF spans
- **Azure Monitor integration** — `ExportTarget.AzureMonitor` for Application Insights with auto-detection

### Breaking changes

- `ConfigureOpenTelemetry()` is removed — use `UseMicrosoftOpenTelemetry()`
- `AddA365Tracing()` is removed — use `UseMicrosoftOpenTelemetry()`
- `Agent365ExporterOptions` is no longer manually registered — configure via `o.Agent365.Exporter.*`
- `TokenStore` class is removed — use `o.Agent365.Exporter.TokenResolver` directly, or `IExporterTokenCache<AgenticTokenStruct>` via DI for auto-managed tokens

### New DI services auto-registered

| Service | Purpose |
|---|---|
| `IExporterTokenCache<AgenticTokenStruct>` → `AgenticTokenCache` | Agentic token handling |
| `IExporterTokenCache<string>` → `ServiceTokenCache` | Service-to-service scenarios |
| `Agent365ExporterOptions` | Exporter configuration (singleton) |
| `ActivityProcessor` | Baggage-to-tags propagation |
| `SemanticKernelSpanProcessor` | SK span normalization |
| `AgentFrameworkSpanProcessor` | MAF span processing |

## Environment variable mapping

| Old (Agent365 SDK / Manual OTel) | New (Distro) | Notes |
|---|---|---|
| ~~`EnableAgent365Exporter` / `ENABLE_A365_OBSERVABILITY_EXPORTER`~~ | `ExportTarget.Agent365` in code | No env var equivalent in .NET distro (Python/Node.js only). Set via `o.Exporters` flag or auto-detected when `o.Agent365.Exporter.TokenResolver` is set |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Not needed for Agent365 | Distro manages endpoint internally |
| — | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Auto-detected for Azure Monitor |
| — | `A365_OBSERVABILITY_SCOPE_OVERRIDE` | Override auth scope (defaults to production) |
| — | `A365_OBSERVABILITY_DOMAIN_OVERRIDE` | Override Agent365 endpoint domain |
| — | `OTEL_TRACES_SAMPLER` | Sampler type: `microsoft.rate_limited`, `microsoft.fixed_percentage` |
| — | `OTEL_TRACES_SAMPLER_ARG` | Sampler argument (traces/second or ratio) |
| — | `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` | Disable URL query redaction in ASP.NET Core spans |
| — | `OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION` | Disable URL query redaction in HTTP client spans |
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | Same | Used to detect development mode |

## Logging migration

The distro includes OTel log signal capture via `OpenTelemetryLoggerProvider`. All `ILogger` output is automatically exported to the configured exporters.

### Default behavior

- Logging is **enabled by default** (`InstrumentationOptions.EnableLogging = true`)
- Azure SDK `EventSource` logs are forwarded to `ILogger` via `AzureEventSourceLogForwarder`

### Disabling OTel log export

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Instrumentation.EnableLogging = false; // suppresses OpenTelemetryLoggerProvider
    });
```

When disabled, the distro adds a provider-scoped filter to suppress logs from reaching `OpenTelemetryLoggerProvider`:

```csharp
// Internally applied:
logging.AddFilter<OpenTelemetryLoggerProvider>(null, LogLevel.None);
```

### What you DON'T need to do

- No manual `AddOpenTelemetry()` on `ILoggingBuilder` — the distro handles it
- No manual `AzureEventSourceLogForwarder` registration — auto-registered if Azure Monitor is enabled

## Middleware and instrumentation

### Auto-registered by the distro

The following are configured automatically when you call `UseMicrosoftOpenTelemetry()` — no migration needed:

| Component | What it does |
|---|---|
| ASP.NET Core instrumentation | Captures HTTP request/response spans for all endpoints |
| HTTP client instrumentation | Captures outbound HTTP call spans |
| SQL client instrumentation | Captures SQL query spans |
| `ActivityProcessor` | Copies baggage entries (tenant ID, agent ID, etc.) to span tags |
| `SemanticKernelSpanProcessor` | Normalizes Semantic Kernel spans to Agent365 schema |
| `AgentFrameworkSpanProcessor` | Processes Microsoft Agent Framework spans |

### Unchanged from Agent365 SDK

`ObservabilityBaggageMiddleware` uses the same API and namespace as the old Agent365 SDK — no changes needed. If you already call `app.UseObservabilityRequestContext(resolver)`, it continues to work as-is.

```csharp
// Same API as before — no migration required
app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    var agentId = httpContext.Request.Headers["X-Agent-Id"].FirstOrDefault();
    return (tenantId, agentId);
});
```

## Known issues

- `launch­Settings.json` may override environment variables with empty strings — use `dotnet run --no-launch-profile` or set values via `dotnet user-secrets` instead
- If `ExportTarget.Agent365` is set, `IExporterTokenCache<AgenticTokenStruct>` must be populated at runtime via `RegisterObservability()` — otherwise the exporter will skip export silently
- Azure Monitor requires `APPLICATIONINSIGHTS_CONNECTION_STRING` to be set if `ExportTarget.AzureMonitor` is included

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.Agent365.Demo/` in the distro repo.
