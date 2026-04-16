# Testing Microsoft.OpenTelemetry Distro — Agent365 Team

## Package

```
Microsoft.OpenTelemetry 1.0.0-alpha.1
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
<PackageReference Include="Microsoft.OpenTelemetry" Version="1.0.0-alpha.1" />
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
using OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing
        .AddSource(
            "A365.AgentFramework",
            "Microsoft.Agents.Builder",
            "Microsoft.Agents.Hosting"));
```

> **Shorter alternative** (if you don't need custom `.AddSource()` calls):
> ```csharp
> builder.UseMicrosoftOpenTelemetry(o =>
> {
>     o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
> });
> ```

### What to REMOVE from Program.cs

- `builder.ConfigureOpenTelemetry();` — replaced by `UseMicrosoftOpenTelemetry()`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });` — token cache is auto-registered via DI
- `builder.AddA365Tracing(config => { ... });` — replaced by `UseMicrosoftOpenTelemetry()`
- `using` statements for `Microsoft.Agents.A365.Observability.*` in Program.cs — replace with `using Microsoft.OpenTelemetry;` and `using OpenTelemetry;`
- `TokenStore` class file — delete it entirely. Replaced by `IExporterTokenCache<AgenticTokenStruct>` (injected via DI)
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

## Known issues

- `launch­Settings.json` may override environment variables with empty strings — use `dotnet run --no-launch-profile` or set values via `dotnet user-secrets` instead
- If `ExportTarget.Agent365` is set, `IExporterTokenCache<AgenticTokenStruct>` must be populated at runtime via `RegisterObservability()` — otherwise the exporter will skip export silently
- Azure Monitor requires `APPLICATIONINSIGHTS_CONNECTION_STRING` to be set if `ExportTarget.AzureMonitor` is included

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.Agent365.Demo/` in the distro repo.
