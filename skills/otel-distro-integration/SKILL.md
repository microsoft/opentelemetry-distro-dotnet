---
name: otel-distro-dotnet
description: Integrate Microsoft OpenTelemetry Distro into a .NET AI agent project — unified observability with Agent 365, Azure Monitor, and OTLP export
user-invocable: true
---

You are integrating the Microsoft OpenTelemetry Distro into the user's existing .NET AI agent project. This is the new unified distro that replaces the old fragmented `Microsoft.Agents.A365.Observability.*` packages and raw OpenTelemetry packages with a single `Microsoft.OpenTelemetry` NuGet package.

The user may optionally provide arguments specifying their AI framework, whether they use ASP.NET Core hosting, or other constraints. If not provided, you will discover these by reading their code.

## Phase 1 — Analyze the project

Before writing any code, read the project to answer these questions:

1. **Which AI framework does it use?** Look for: Semantic Kernel (`Microsoft.SemanticKernel`), OpenAI (`Azure.AI.OpenAI`), Microsoft Agent Framework (`Microsoft.Agents.AI`), or none/custom.
2. **Does the project use ASP.NET Core hosting?** Search for `WebApplication.CreateBuilder`, `Host.CreateDefaultBuilder`, or `IHostApplicationBuilder` usage.
3. **Where is the agent entry point?** Find the method that handles incoming messages (activity handler, controller, or middleware).
4. **Where is app startup?** Find `Program.cs` or `Startup.cs` where DI services are registered.
5. **What is the agent's identity?** Look for existing agent_id, tenant_id, blueprint_id values in config, env vars, or code.
6. **Is there an existing .csproj?** Check for project files and existing package references.
7. **Is the project already using old A365 SDK packages?** Check for `Microsoft.Agents.A365.Observability.Extensions.*` or `AddA365Tracing()`. If so, this is a migration.

State your findings to the user in 3-4 sentences, then proceed.

## Phase 2 — Choose integration path

Follow this decision tree exactly:

```
Uses ASP.NET Core hosting (IHostApplicationBuilder)?
├─ YES → HOSTED PATH: UseMicrosoftOpenTelemetry() on builder + DI token cache
│    Framework?
│    ├─ Semantic Kernel    → auto-instrument (enabled by default)
│    ├─ OpenAI             → auto-instrument (enabled by default)
│    ├─ Agent Framework    → auto-instrument (enabled by default)
│    └─ Other/custom       → manual instrumentation (scope classes)
│
└─ NO → CONSOLE/STANDALONE PATH: OpenTelemetrySdk.Create() + UseMicrosoftOpenTelemetry()
     Framework?
     ├─ Supported → auto-instrument (enabled by default)
     └─ Other     → manual instrumentation (scope classes)
```

## Phase 3 — Implement

### INVARIANT RULES — Violating any of these produces a broken integration

1. **Baggage is mandatory.** The exporter partitions spans by `(tenant_id, agent_id)`. Spans missing either value are **silently dropped**. Every code path that creates scopes MUST be inside a `BaggageBuilder` context with both `.TenantId(...)` and `.AgentId(...)`.
2. **Scope nesting order:** `BaggageBuilder.Build()` → `InvokeAgentScope.Start()` → `InferenceScope.Start()` / `ExecuteToolScope.Start()`. Inference and tool scopes are children of the invoke scope.
3. **Four scopes available:** `InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`, `OutputScope`. The first three are required for M365 store publishing.
4. **`UseMicrosoftOpenTelemetry()` is called once at app startup.** It initializes singleton providers. Never call it per-request.
5. **Token resolver:** `Func<string, string, Task<string?>>` — `(agentId, tenantId) => token`. When omitted, the distro auto-registers `IExporterTokenCache<AgenticTokenStruct>` via DI.
6. **ExportTarget flags enum** controls where telemetry is sent. Combine with `|`: `ExportTarget.Agent365 | ExportTarget.Console`.
7. **Auto-instrumentation still requires baggage.** It does NOT set baggage for you.
8. **Do not mix auto and manual instrumentation for the same framework.**
9. **A365-only mode:** When Agent365 is the sole exporter (no AzureMonitor, no OTLP), infrastructure instrumentations (ASP.NET, HTTP, SQL, Azure SDK) are auto-disabled. GenAI instrumentations stay enabled. Override via `o.Instrumentation.Enable*` flags.
10. **`ENABLE_A365_OBSERVABILITY_EXPORTER` is NOT used in .NET.** Control the exporter entirely in code via `ExportTarget.Agent365`.

### Step 3.1 — Install NuGet package

```xml
<PackageReference Include="Microsoft.OpenTelemetry" />
```

```bash
dotnet add package Microsoft.OpenTelemetry
```

If migrating from old SDK, remove these from `.csproj`:
```xml
<!-- Remove all of these -->
<PackageReference Include="Microsoft.Agents.A365.Observability.Runtime" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Hosting" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.SemanticKernel" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.OpenAI" />
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.AgentFramework" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
```

### Step 3.2 — Add observability configuration to app startup

Use this exact namespace. Do not guess namespace paths.

```csharp
using Microsoft.OpenTelemetry;
```

**Hosted path (ASP.NET Core / Worker Services)** — in `Program.cs`:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;

    // Option A (recommended): Let distro auto-manage tokens via DI.
    // IExporterTokenCache<AgenticTokenStruct> is registered automatically.
    // Your agent calls RegisterObservability() at runtime.

    // Option B: Provide your own token resolver
    o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        await MyTokenService.GetTokenAsync(agentId, tenantId);
});
```

**Console / non-hosted path:**

```csharp
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
            await MyTokenService.GetTokenAsync(agentId, tenantId);
    })
);
// ... do work ...
sdk.Dispose();
```

**Adding custom activity sources** (if needed):

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
    })
    .WithTracing(tracing => tracing
        .AddSource("MyCompany.MyAgent.CustomSource"));
```

If migrating, also remove from `Program.cs`:
- `builder.ConfigureOpenTelemetry();`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });`
- `builder.AddA365Tracing(config => { ... });` or `builder.WebHost.AddA365Tracing(...)`
- Old `using` statements for `Microsoft.Agents.A365.Observability.*` (replace with `using Microsoft.OpenTelemetry;`)

### Step 3.3 — Set up token resolver

**Auto DI (recommended for Agent Framework apps)** — don't set `TokenResolver`. The distro auto-registers `IExporterTokenCache<AgenticTokenStruct>` via DI:

```csharp
// Program.cs — no TokenResolver needed
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Agent365;
});
```

In your agent class, inject and call `RegisterObservability()`:

```csharp
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;

public class MyAgent : AgentApplication
{
    private readonly IExporterTokenCache<AgenticTokenStruct> _agentTokenCache;

    public MyAgent(AgentApplicationOptions options,
        IExporterTokenCache<AgenticTokenStruct> agentTokenCache) : base(options)
    {
        _agentTokenCache = agentTokenCache;
    }

    protected async Task MessageActivityAsync(
        ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        _agentTokenCache.RegisterObservability(
            turnContext.Activity.Recipient.AgenticAppId,
            turnContext.Activity.Recipient.TenantId,
            new AgenticTokenStruct(
                userAuthorization: UserAuthorization,
                turnContext: turnContext,
                authHandlerName: "AGENTIC"),
            EnvironmentUtils.GetObservabilityAuthenticationScope());
    }
}
```

**Custom resolver** — for non-agent apps, S2S, or custom auth:
```csharp
o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
{
    return await MyTokenService.GetTokenAsync(agentId, tenantId);
};
```

When you set `TokenResolver` explicitly, the auto DI token cache is **not** registered.

**Note:** Automatic FIC/DefaultAzureCredential fallback is NOT available in .NET. You must provide a `TokenResolver` or use the DI token cache.

### Step 3.4 — Set up baggage

**Turn-level middleware (Bot Framework pipeline):**
```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

adapter.Use(new BaggageTurnMiddleware());
```

**HTTP-level middleware (ASP.NET Core pipeline):**
```csharp
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

app.UseObservabilityRequestContext((httpContext) =>
{
    var tenantId = GetTenantIdFromContext(httpContext);
    var agentId = GetAgentIdFromContext(httpContext);
    return (tenantId, agentId);
});
```

**Manual BaggageBuilder (standalone path):**
```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;

using var baggageScope = new BaggageBuilder()
    .TenantId("<TENANT_ID>")
    .AgentId("<AGENT_ID>")
    .ConversationId("<CONV_ID>")
    .Build();
// Any spans started in this context receive these as attributes
```

**From TurnContext:**
```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Hosting.Extensions;

using var baggageScope = new BaggageBuilder()
    .FromTurnContext(turnContext)
    .Build();
```

### Step 3.5 — Add instrumentation

**Auto-instrumentation** is enabled by default for all supported frameworks. Toggle via `InstrumentationOptions`:

| Framework | Flag | Default |
|---|---|---|
| Semantic Kernel | `o.Instrumentation.EnableSemanticKernelInstrumentation` | true |
| OpenAI / Azure OpenAI | `o.Instrumentation.EnableOpenAIInstrumentation` | true |
| Agent Framework | `o.Instrumentation.EnableAgentFrameworkInstrumentation` | true |
| ASP.NET Core | `o.Instrumentation.EnableAspNetCoreInstrumentation` | true |
| HTTP Client | `o.Instrumentation.EnableHttpClientInstrumentation` | true |
| SQL Client | `o.Instrumentation.EnableSqlClientInstrumentation` | true |
| Azure SDK | `o.Instrumentation.EnableAzureSdkInstrumentation` | true |

No separate extension packages or `WithSemanticKernel()` / `WithOpenAI()` calls needed.

**Manual instrumentation** — wrap existing code with scope `using` statements. Use these exact imports:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
```

Nest scopes in this order inside the baggage context:

1. `using var invokeScope = InvokeAgentScope.Start(request, scopeDetails, agentDetails, callerDetails)` — wraps the entire request handler. Call `invokeScope.RecordInputMessages()`, `invokeScope.RecordOutputMessages()`.
2. `using var inferenceScope = InferenceScope.Start(request, inferenceDetails, agentDetails)` — wraps each LLM call. Call `inferenceScope.RecordOutputMessages()`, `inferenceScope.RecordInputTokens()`, `inferenceScope.RecordOutputTokens()`, `inferenceScope.RecordFinishReasons()`.
3. `using var toolScope = ExecuteToolScope.Start(request, toolDetails, agentDetails)` — wraps each tool call. Call `toolScope.RecordResponse()`.
4. `using var outputScope = OutputScope.Start(request, response, agentDetails, spanDetails: new SpanDetails(parentContext: parentContext))` — for async output after invoke scope ends. Capture parent context via `invokeScope.GetActivityContext()` before disposing.

For `AgentDetails`, populate: `AgentId`, `AgentName`, `TenantId`, `AgentBlueprintId`, `AgenticUserId`, `AgenticUserEmail`.
For `CallerDetails`, populate: `UserDetails` with `UserId`, `UserEmail`.
For `Request`, populate: `Content`, `SessionId`, `ConversationId`, `Channel` with `Name`.
For `InvokeAgentScopeDetails`, populate: `Endpoint` with URI.
For `InferenceCallDetails`, populate: `OperationName` (from `InferenceOperationType`), `Model`, `ProviderName`.
For `ToolCallDetails`, populate: `ToolName`, `ToolType`, `ToolCallId`, `Arguments` (JSON string).

### Step 3.6 — Configure export target

In .NET, the exporter is controlled entirely in code via `ExportTarget` flags — there is no environment variable toggle.

```csharp
// Development
o.Exporters = ExportTarget.Console;

// Production
o.Exporters = ExportTarget.Agent365;

// Both (local validation + remote export)
o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;

// Multi-backend
o.Exporters = ExportTarget.Agent365 | ExportTarget.AzureMonitor | ExportTarget.Otlp;
```

**Warning:** `ExportTarget.Console` is for local development only. Do not include in production — it adds overhead and may log sensitive telemetry to stdout.

## Phase 4 — Verify

After making all changes, run through this checklist mentally and report status to the user:

```
[ ] Microsoft.OpenTelemetry installed (old A365 + OTel packages removed if migrating)
[ ] UseMicrosoftOpenTelemetry() called once at startup
[ ] ExportTarget includes Agent365
[ ] Token resolver configured (DI auto-cache or custom TokenResolver)
[ ] Baggage context established (middleware or manual) with TenantId AND AgentId
[ ] InvokeAgentScope wraps the agent entry point
[ ] InferenceScope wraps every LLM call (or auto-instrumentation enabled)
[ ] ExecuteToolScope wraps every tool call (or auto-instrumentation enabled)
[ ] No per-request calls to UseMicrosoftOpenTelemetry()
[ ] Scope nesting order correct: Baggage → InvokeAgent → Inference/Tool
[ ] Old Program.cs setup removed (ConfigureOpenTelemetry, AddA365Tracing, etc.)
```

Tell the user what to do next:
1. Build and run: `dotnet build && dotnet run`
2. Set `ExportTarget.Console` and run to see console spans
3. If using a custom token resolver stub, implement the actual token acquisition
4. Set `ExportTarget.Agent365` and verify at `admin.cloud.microsoft/#/agents/all`

## Troubleshooting reference

| Symptom | Cause | Fix |
|---|---|---|
| No spans in admin center | Exporter not configured | Set `o.Exporters = ExportTarget.Agent365` |
| "No spans with tenant/agent identity" | Missing baggage | Add `TenantId` AND `AgentId` to BaggageBuilder |
| Export succeeds (HTTP 200) but no data in admin center | Spans accepted but not yet stored, or unsupported operation names | HTTP 200 means accepted, not stored. Verify spans use `invoke_agent`, `execute_tool`, `chat`, or `output_messages` as operation names. Data may take a few minutes to appear. |
| Token resolver returns null | `RegisterObservability()` not called | Call it in the activity handler before export |
| HTTP 401 | Wrong token scope | Verify token has `Agent365.Observability.OtelWrite` |
| HTTP 403 | Missing license, permission, or tenant not enabled | Need M365 E7 / Agent 365 Frontier license; grant `Agent365.Observability.OtelWrite` via `a365 setup admin` or Entra portal (resource `9b975845-388f-4429-889e-eab1ef63949c`, both Delegated + Application). If license and permission are correct, contact the Agent 365 team — your tenant may not be enabled yet. |
| HTTP 429 / 5xx | Transient | .NET does not auto-retry. If persistent, increase `o.Agent365.Exporter.ScheduledDelayMilliseconds` |
| Timeout | Network / slow endpoint | Increase `o.Agent365.Exporter.ExporterTimeoutMilliseconds` |
| Infrastructure spans missing | A365-only mode auto-disables them | Re-enable via `o.Instrumentation.EnableAspNetCoreInstrumentation = true` |
