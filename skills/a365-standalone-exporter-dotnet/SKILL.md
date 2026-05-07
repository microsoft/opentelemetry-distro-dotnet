---
name: a365-standalone-exporter-dotnet
description: Add the A365 standalone SpanExporter to a .NET project that already has OpenTelemetry configured — no full distro required, just plug-in export to Agent 365
---

You are adding the `A365.OpenTelemetry.Exporter` standalone SpanExporter NuGet package to the user's existing .NET project. This is the **lightweight** path for teams that already have OpenTelemetry tracing set up and want to add Agent 365 as an additional export destination — without adopting the full `Microsoft.OpenTelemetry` distro.

## When to use this vs the full distro

| Use standalone exporter (`A365.OpenTelemetry.Exporter`) | Use full distro (`Microsoft.OpenTelemetry`) |
|---|---|
| Already have a TracerProvider configured | Starting from scratch or want turnkey setup |
| Only need A365 export — keep your existing backends | Want auto-instrumentation for Semantic Kernel/AI |
| Minimal dependency footprint (OTel SDK + Http + Json) | Want scope classes (InvokeAgentScope, etc.) |
| Want full control over Activity creation | Want ASP.NET Core hosting integration |

## Phase 1 — Analyze the project

Before writing any code, answer:

1. **Does the project already have a TracerProvider?** Look for `Sdk.CreateTracerProviderBuilder()`, `services.AddOpenTelemetry()`, or `TracerProviderBuilder`.
2. **What exporters/processors are configured?** Look for `AddOtlpExporter`, `AddConsoleExporter`, `AddProcessor`.
3. **Where are Activities created?** Find `ActivitySource`, `source.StartActivity()`.
4. **What auth mechanism is available?** Azure Identity, MSAL, or custom.
5. **Where does the agent get its identity?** Find tenant_id, agent_id in config/env.

State findings in 2-3 sentences, then proceed.

## Phase 2 — Implement

### INVARIANT RULES

1. **Routing attributes are mandatory.** The exporter groups Activities by `(tenant_id, agent_id)` read from tags or baggage. Activities missing both are **silently skipped**.
2. **Token resolver is async.** Signature: `Func<string, string, Task<string?>>` — `(agentId, tenantId) => Task<token>`. Return `null` to skip.
3. **`gen_ai.operation.name` must be set on every Activity.** Only activities with one of `invoke_agent`, `execute_tool`, `chat`, `output_messages` are processed by A365.
4. **All attribute values should be strings for A365 processing.** Token counts must be `"42"` not `42`; ports must be `"443"` not `443`.
5. **Do NOT add `?api-version=1`** — handled internally.
6. **Add as an additional processor** — do not replace existing exporters.
7. **BaggageBuilder implements IDisposable** — use with `using` to restore previous baggage on scope exit.
8. **Target framework: net8.0** — package requires .NET 8.0+.

### Step 2.1 — Install

```bash
dotnet add package A365.OpenTelemetry.Exporter
```

Dependencies pulled automatically:
- `OpenTelemetry` >= 1.7.0
- `Microsoft.Extensions.Logging.Abstractions` >= 8.0.1
- `System.Text.Json` >= 8.0.4

For token resolution, also add:
```bash
# For Azure Identity (recommended):
dotnet add package Azure.Identity

# For MSAL:
dotnet add package Microsoft.Identity.Client
```

### Step 2.2 — Register the exporter with TracerProvider

**Option A — Extension method (simplest):**

```csharp
using A365.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Trace;

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyAgent")
    .AddA365Exporter(opts =>
    {
        opts.TokenResolver = myTokenResolver;
        // opts.Endpoint defaults to "https://agent365.svc.cloud.microsoft"
        // opts.Timeout defaults to 30 seconds
    })
    // keep your existing exporters:
    .AddConsoleExporter()
    .Build();
```

**Option B — With ASP.NET Core DI:**

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("MyAgent")
        .AddA365Exporter(opts =>
        {
            opts.TokenResolver = myTokenResolver;
        })
        // ... other exporters
    );
```

**Option C — Manual (if you need a custom HttpClient or logger):**

```csharp
var options = new A365ExporterOptions
{
    TokenResolver = myTokenResolver,
};

var exporter = new A365SpanExporter(options, httpClient, logger);
builder.AddProcessor(new SimpleActivityExportProcessor(exporter));
```

### Step 2.3 — Set up token resolver

Pick one:

**DefaultAzureCredential (recommended for Azure-hosted workloads):**

```csharp
using Azure.Identity;

var credential = new DefaultAzureCredential();

opts.TokenResolver = async (agentId, tenantId) =>
{
    var token = await credential.GetTokenAsync(
        new Azure.Core.TokenRequestContext(
            new[] { "9b975845-388f-4429-889e-eab1ef63949c/.default" }));
    return token.Token;
};
```

**MSAL Confidential Client (S2S / client credentials):**

```csharp
using Microsoft.Identity.Client;

var app = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority($"https://login.microsoftonline.com/{aadTenantId}")
    .Build();

opts.TokenResolver = async (agentId, tenantId) =>
{
    var result = await app.AcquireTokenForClient(
        new[] { "9b975845-388f-4429-889e-eab1ef63949c/.default" })
        .ExecuteAsync();
    return result.AccessToken;
};
```

**Custom resolver:**

```csharp
opts.TokenResolver = async (agentId, tenantId) =>
{
    // Return bearer token or null to skip this group
    return await myVault.GetTokenAsync(agentId, tenantId);
};
```

The token must have scope `9b975845-388f-4429-889e-eab1ef63949c/.default` (resource: Agent 365 Observability) with app role `Agent365.Observability.OtelWrite`.

### Step 2.4 — Set A365 routing attributes on Activities

The exporter reads `tenant_id` / `agent_id` (or `a365.tenant_id` / `a365.agent_id`) from Activity tags, falling back to Activity baggage. Two approaches:

**Option A — BaggageBuilder (recommended for request-scoped context):**

```csharp
using A365.OpenTelemetry.Exporter;

using var baggage = new BaggageBuilder()
    .TenantId(tenantId)
    .AgentId(agentId)
    .ConversationId(conversationId)  // optional
    .Build();

using var activity = source.StartActivity("invoke_agent");
activity?.SetTag("gen_ai.operation.name", "invoke_agent");
// ... agent logic ...
```

`BaggageBuilder.Build()` sets both baggage AND tags (`a365.tenant_id`, `a365.agent_id`, `a365.conversation_id`) on the current Activity. On `Dispose()`, previous baggage values are restored.

**Option B — Direct attributes (for one-off Activities):**

```csharp
using var activity = source.StartActivity("invoke_agent");
BaggageBuilder.SetA365Attributes(activity!, tenantId, agentId);
activity?.SetTag("gen_ai.operation.name", "invoke_agent");
```

`SetA365Attributes` sets both `a365.tenant_id`/`a365.agent_id` and `tenant_id`/`agent_id` tags.

### Step 2.5 — Set required span attributes for A365 ingestion

Beyond routing, A365 requires specific attributes per operation type. Set these on every Activity:

**All Activities:**
```csharp
activity?.SetTag("gen_ai.operation.name", "invoke_agent"); // or chat/execute_tool/output_messages
activity?.SetTag("gen_ai.agent.id", agentId);
activity?.SetTag("gen_ai.agent.name", "My Agent");
activity?.SetTag("microsoft.a365.agent.blueprint.id", agentId);
activity?.SetTag("gen_ai.conversation.id", conversationId);
activity?.SetTag("microsoft.channel.name", "web"); // or msteams, outlook
activity?.SetTag("user.id", userAadObjectId);
activity?.SetTag("client.address", callerIp);
activity?.SetTag("server.address", "myagent.example.com");
activity?.SetTag("server.port", "443"); // STRING, not int
```

**`invoke_agent` Activities** (additionally):
```csharp
activity?.SetTag("gen_ai.input.messages", """[{"role":"user","content":"..."}]""");
activity?.SetTag("gen_ai.output.messages", """[{"role":"assistant","content":"..."}]""");
```

**`chat` Activities** (additionally):
```csharp
activity?.SetTag("gen_ai.request.model", "gpt-4o");
activity?.SetTag("gen_ai.provider.name", "openai");
activity?.SetTag("gen_ai.usage.input_tokens", "150");  // STRING
activity?.SetTag("gen_ai.usage.output_tokens", "42");  // STRING
```

**`execute_tool` Activities** (additionally):
```csharp
activity?.SetTag("gen_ai.tool.name", "search_products");
activity?.SetTag("gen_ai.tool.type", "function");
activity?.SetTag("gen_ai.tool.call.id", "call_abc123");
activity?.SetTag("gen_ai.tool.call.arguments", """{"query":"top products"}""");
activity?.SetTag("gen_ai.tool.call.result", """{"results":[...]}""");
```

### Step 2.6 — Complete integration example

```csharp
using System.Diagnostics;
using A365.OpenTelemetry.Exporter;
using Azure.Identity;
using OpenTelemetry;
using OpenTelemetry.Trace;

// --- Setup (once at startup) ---
var source = new ActivitySource("MyAgent", "1.0.0");
var credential = new DefaultAzureCredential();

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyAgent")
    .AddConsoleExporter()  // existing
    .AddA365Exporter(opts =>
    {
        opts.TokenResolver = async (agentId, tenantId) =>
        {
            var token = await credential.GetTokenAsync(
                new Azure.Core.TokenRequestContext(
                    new[] { "9b975845-388f-4429-889e-eab1ef63949c/.default" }));
            return token.Token;
        };
    })
    .Build();

// --- Per-request (in the request handler) ---
var tenantId = "<customer-tenant-guid>";
var agentId = "<your-agent-aad-app-object-id>";
var conversationId = "conv-001";

using var baggage = new BaggageBuilder()
    .TenantId(tenantId)
    .AgentId(agentId)
    .ConversationId(conversationId)
    .Build();

using var activity = source.StartActivity("invoke_agent");
activity?.SetTag("gen_ai.operation.name", "invoke_agent");
activity?.SetTag("gen_ai.agent.id", agentId);
activity?.SetTag("gen_ai.agent.name", "My Agent");
activity?.SetTag("microsoft.a365.agent.blueprint.id", agentId);
activity?.SetTag("gen_ai.conversation.id", conversationId);
activity?.SetTag("microsoft.channel.name", "web");
activity?.SetTag("user.id", "<aad-user-objectid>");
activity?.SetTag("client.address", "10.1.2.80");
activity?.SetTag("server.address", "myagent.example.com");
activity?.SetTag("server.port", "443");
activity?.SetTag("gen_ai.input.messages", """[{"role":"user","content":"hello"}]""");
// ... agent logic ...
activity?.SetTag("gen_ai.output.messages", """[{"role":"assistant","content":"hi"}]""");
```

### Step 2.7 — Nested Activities (parent-child)

```csharp
using var parentActivity = source.StartActivity("invoke_agent");
parentActivity?.SetTag("gen_ai.operation.name", "invoke_agent");
BaggageBuilder.SetA365Attributes(parentActivity!, tenantId, agentId);
// ... set other parent attributes ...

using var childActivity = source.StartActivity("chat");
childActivity?.SetTag("gen_ai.operation.name", "chat");
BaggageBuilder.SetA365Attributes(childActivity!, tenantId, agentId);
childActivity?.SetTag("gen_ai.request.model", "gpt-4o");
// ... LLM call ...
```

Child Activities automatically inherit the parent's trace context when created within the parent's `using` scope.

## Phase 3 — Verify

Checklist:

```
[ ] A365.OpenTelemetry.Exporter NuGet package installed
[ ] .AddA365Exporter() called on TracerProviderBuilder (not replacing existing exporters)
[ ] Token resolver configured and returning valid tokens
[ ] BaggageBuilder or SetA365Attributes sets tenant_id AND agent_id on every Activity
[ ] gen_ai.operation.name set on every Activity (invoke_agent/execute_tool/chat/output_messages)
[ ] All required attributes set per operation type
[ ] Numeric values encoded as strings (token counts, port)
[ ] Activity?.SetTag used (null-safe) since StartActivity can return null
[ ] using keyword on BaggageBuilder and Activity for proper disposal
[ ] ActivitySource name matches .AddSource("...") registration
```

Tell the user:
1. Run the agent and check console exporter output for Activities
2. Verify in Defender advanced hunting after ~5 minutes (see KQL below)
3. If 200 OK but no data, check: M365 E7 license assigned, tenant consent granted

**Verification KQL:**
```kusto
let agentIdToFind = "YOUR-AGENT-ID";
CloudAppEvents
| where Timestamp > ago(1d)
| where ActionType in ("InvokeAgent", "InferenceCall", "ExecuteToolBySDK")
| extend resData = parse_json(tostring(RawEventData))
| where resData.AgentId == agentIdToFind or resData.TargetAgentId == agentIdToFind
| project Timestamp, ActionType, resData
| order by Timestamp desc
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No spans exported (DEBUG: "skipping span — missing tenant_id or agent_id") | BaggageBuilder not used or SetA365Attributes not called | Wrap with `using var baggage = new BaggageBuilder()...Build()` or call `SetA365Attributes()` |
| StartActivity returns null | ActivitySource not registered or no listener | Ensure `.AddSource("MyAgent")` matches your `new ActivitySource("MyAgent")` |
| Token resolver throws | Credential not configured or secret expired | Check `DefaultAzureCredential` logs; verify Azure.Identity installed |
| HTTP 401 | Wrong token audience | Scope must be `9b975845-388f-4429-889e-eab1ef63949c/.default` |
| HTTP 403 | Agent ID mismatch or missing permission | URL agent_id must match token's `appid`/`azp` claim; grant `Agent365.Observability.OtelWrite` |
| 200 OK but no data in Defender | Silent drop — no M365 E7 license, or wrong `gen_ai.operation.name` | Ensure at least 1 user has M365 E7 license; use valid operation names |
| Timeout during export | 30s default too short for cold-start auth | Increase `opts.Timeout = TimeSpan.FromSeconds(60)` or pre-warm token cache |
| Token counts show as zero | Sent as int instead of string | Use `activity?.SetTag("gen_ai.usage.input_tokens", "150")` — string value |
| Run tree broken / flat spans | Activities not nested or different TraceId | Ensure child Activities are created within parent's `using` scope |
| BaggageBuilder not applying to child Activities | Build() called after StartActivity | Call `Build()` BEFORE starting Activities |
