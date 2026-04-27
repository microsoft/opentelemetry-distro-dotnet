# Agent 365 Observability

> **Migration quick-reference** — before/after code, span attributes, and troubleshooting for migrating from the standalone Agent365 SDK to the `Microsoft.OpenTelemetry` distro. For the full standalone guide (no migration context), see [Agent 365 Getting Started](agent365-getting-started.md).

> **Important:** You need to be part of the [Frontier preview program](https://adoption.microsoft.com/copilot/frontier-program/) to get **early access** to Microsoft Agent 365.

To participate in the Agent 365 ecosystem, add Agent 365 Observability capabilities to your agent. Agent 365 Observability builds on [OpenTelemetry (OTel)](https://opentelemetry.io/docs/specs/otel/protocol/) and provides a unified framework for capturing telemetry consistently and securely across all agent platforms. By implementing this required component, you enable IT admins to monitor your agent's activity in Microsoft admin center and allow security teams to use Defender and Purview for compliance and threat detection.

## Key benefits

- **End-to-end visibility**: Capture comprehensive telemetry for every agent invocation, including sessions, tool calls, and exceptions, giving you full traceability across platforms.
- **Security and compliance enablement**: Feed unified audit logs into Defender and Purview, enabling advanced security scenarios and compliance reporting for your agent.
- **Cross-platform flexibility**: Build on OTel standards and support diverse runtimes and platforms like Copilot Studio, Foundry, and future agent frameworks.
- **Operational efficiency for admins**: Provide centralized observability in Microsoft 365 admin center, reducing troubleshooting time and improving governance with role-based access controls for IT teams managing your agent.

## Supported agents

| Agent type | Details |
|---|---|
| **Microsoft Agent 365-enabled agents** | Use the observability SDK to instrument your agent. |
| **Custom engine agents** | Use the observability SDK to instrument your agent. |
| **Declarative agents** | Observability is supported out of the box. No SDK implementation required. |

## Installation (.NET)

Install the Microsoft OpenTelemetry Distro package. This single package replaces both the Agent365 observability packages and the raw OpenTelemetry packages.

### Package to ADD

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

### Packages to REMOVE (replaced by the distro)

```xml
<!-- Remove all of these -->
<PackageReference Include="Microsoft.Agents.A365.Observability.Extensions.AgentFramework" Version="0.2.151-beta" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />
```

### Packages to KEEP (unchanged)

```xml
<PackageReference Include="Microsoft.Agents.A365.Notifications" Version="0.2.151-beta" />
<PackageReference Include="Microsoft.Agents.A365.Tooling.Extensions.AgentFramework" Version="0.2.151-beta" />
<!-- ... all other non-observability packages stay as-is -->
```

### API differences from A365 SDK

The distro bundles the same observability functionality as the A365 SDK packages, with a few intentional differences:

| A365 SDK | Distro | Notes |
|----------|--------|-------|
| `ChatToolCallExtensions.Trace()` | Not included | The distro does not depend on the `OpenAI` NuGet package. Use `ExecuteToolScope.Start()` directly instead (see [workaround](#chattoolcallextensions-workaround)). |
| `Builder` (fluent API) | `UseMicrosoftOpenTelemetry()` | The distro replaces the A365 `Builder` class with a single entry point. See [Configuration](#configuration-net). |

#### `ChatToolCallExtensions` workaround

If you previously used `chatToolCall.Trace(agentId, tenantId)` from `Microsoft.Agents.A365.Observability.Extensions.OpenAI`, replace it with direct scope creation — all required types are public in the distro:

```csharp
using var scope = ExecuteToolScope.Start(
    new Request(),
    new ToolCallDetails(
        chatToolCall.FunctionName,
        chatToolCall.FunctionArguments?.ToString(),
        chatToolCall.Id,
        null,
        chatToolCall.Kind.ToString()),
    new AgentDetails(agentId: agentId, tenantId: tenantId));

// execute tool...
scope.RecordResponse(result);
```

## Configuration (.NET)

### Before (Agent365 SDK — deprecated)

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

### After (Microsoft OpenTelemetry Distro)

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

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

> Activity sources for Agent Framework, Semantic Kernel, and OpenAI are auto-registered by the distro — you only need `.AddSource()` for your own custom sources.

> ⚠️ **Production warning:** `ExportTarget.Console` is intended for local development only. Do not include it in production deployments — it adds overhead and may log sensitive telemetry to stdout. Use `ExportTarget.Agent365` and/or `ExportTarget.AzureMonitor` in production.

### What to REMOVE from Program.cs

- `builder.ConfigureOpenTelemetry();` — replaced by `UseMicrosoftOpenTelemetry()`
- `builder.Services.AddSingleton(new Agent365ExporterOptions { ... });` — token cache is auto-registered via DI
- `builder.AddA365Tracing(config => { ... });` — replaced by `UseMicrosoftOpenTelemetry()`
- `using` statements for `Microsoft.Agents.A365.Observability.*` in Program.cs — replace with `using Microsoft.OpenTelemetry;` and `using OpenTelemetry;`
- `TokenStore` class file — delete it if present. Use `o.Agent365.Exporter.TokenResolver` directly, or let the distro auto-manage tokens via `IExporterTokenCache<AgenticTokenStruct>` (injected via DI)
- `ConfigureOpenTelemetry()` extension method file (if you have one) — delete it, the distro handles all OTel setup

## Token resolver

When using the Agent 365 exporter, you must provide a token resolver function that returns an authentication token. The distro supports two approaches:

### Auto (DI) — recommended

The distro automatically registers `IExporterTokenCache<AgenticTokenStruct>` via DI. Your `A365OtelWrapper` calls `RegisterObservability()` at runtime. No `TokenResolver` needed in configuration — it's wired internally.

```csharp
// Inject via constructor
private readonly IExporterTokenCache<AgenticTokenStruct>? _agentTokenCache;

// Then in your activity handler:
_agentTokenCache?.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
    userAuthorization: authSystem,
    turnContext: turnContext,
    authHandlerName: authHandlerName
), EnvironmentUtils.GetObservabilityAuthenticationScope());
```

### Custom resolver — advanced

For non-agent apps, service-to-service, or custom auth scenarios, set the token resolver directly:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            return await MyTokenService.GetTokenAsync(agentId, tenantId);
        };
    });
```

If you set `TokenResolver` explicitly, the auto DI token cache is **not** registered — your resolver is used instead.

## Baggage attributes

Use `BaggageBuilder` to set contextual information that flows through all spans in a request. The SDK implements a `SpanProcessor` that copies all nonempty baggage entries to newly started spans without overwriting existing attributes.

```csharp
// These usings still work — namespaces are identical to the Agent365 SDK
using Microsoft.Agents.A365.Observability.Runtime.Common;

new BaggageBuilder()
    .TenantId("tenant-123")
    .AgentId("agent-456")
    .ConversationId("conv-789")
    .Build();

// Any spans started in this context will receive these as attributes
```

## Auto-instrumentation

Auto-instrumentation automatically listens to agentic frameworks' existing telemetry signals and forwards them to Agent 365 observability service.

### Supported platforms (.NET)

| SDK / Framework | Support |
|---|---|
| Semantic Kernel | ✅ |
| OpenAI | ✅ |
| Agent Framework | ✅ |

Auto-instrumentation is handled by the distro's `UseMicrosoftOpenTelemetry()` call which registers the correct `ActivitySource` listeners:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
});
```

The distro automatically subscribes to:

| ActivitySource | Origin |
|---|---|
| `Agent365Sdk` | Agent365 manual scopes (InvokeAgent, ExecuteTool, Inference, Output) |
| `Microsoft.SemanticKernel*` | Semantic Kernel operations (wildcard) |
| `Azure.AI.OpenAI*` | Azure OpenAI calls (wildcard) |
| `OpenAI.*` | OpenAI calls (wildcard) |
| `Experimental.Microsoft.Extensions.AI` | LLM calls via Microsoft.Extensions.AI |
| `Experimental.Microsoft.Agents.AI` | Agent operations (invoke_agent, chat, execute_tool) |
| `Experimental.Microsoft.Agents.AI.Agent` | Agent-level telemetry |
| `Experimental.Microsoft.Agents.AI.ChatClient` | Chat client telemetry |
| `Azure.*` | Azure SDK (via Azure Monitor integration) |

## Manual Instrumentation

Use Agent 365 observability SDK to understand the internal working of the agent. The SDK provides scopes: `InvokeAgentScope`, `ExecuteToolScope`, `InferenceScope`, and `OutputScope`.

> **Important:** For successful store validation, your agent **must** implement `InvokeAgentScope`, `InferenceScope`, and `ExecuteToolScope`. These three scopes are required for publishing.

### Agent invocation (`InvokeAgentScope`)

Use this scope at the start of your agent process to capture properties like the current agent being invoked, agent user data, and more.

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;

var agentDetails = new AgentDetails(
    agentId: "agent-456",
    agentName: "My Agent",
    agentDescription: "An AI agent powered by Azure OpenAI",
    agenticUserId: "auid-123",
    agenticUserEmail: "agent@contoso.com",
    agentBlueprintId: "blueprint-789",
    tenantId: "tenant-123");

var scopeDetails = new InvokeAgentScopeDetails(
    endpoint: new Uri("https://myagent.contoso.com"));

var request = new Request(
    content: "User asks a question",
    sessionId: "session-42",
    conversationId: "conv-xyz",
    channel: new Channel(name: "msteams"));

var callerDetails = new CallerDetails(
    userDetails: new UserDetails(
        userId: "user-123",
        userEmail: "jane.doe@contoso.com",
        userName: "Jane Doe"));

using (var scope = InvokeAgentScope.Start(request, scopeDetails, agentDetails, callerDetails))
{
    // Perform agent invocation logic
    var response = await CallAgentAsync();
    scope.RecordOutputMessages(new[] { response });
}
```

### Tool execution (`ExecuteToolScope`)

Use this scope to add observability tracking to your agent's tool execution.

```csharp
var toolDetails = new ToolCallDetails(
    toolName: "summarize",
    arguments: "{\"text\": \"...\"}",
    toolCallId: "tc-001",
    description: "Summarize provided text",
    toolType: "function",
    endpoint: new Uri("https://tools.contoso.com:8080"));

using (var scope = ExecuteToolScope.Start(request, toolDetails, agentDetails))
{
    var result = await RunToolAsync(toolDetails);
    scope.RecordResponse(result);
}
```

### Inference (`InferenceScope`)

Use this scope to instrument AI model inference calls with observability tracking to capture token usage, model details, and response metadata.

```csharp
var inferenceDetails = new InferenceCallDetails(
    operationName: InferenceOperationType.Chat,
    model: "gpt-4o-mini",
    providerName: "azure-openai");

using (var scope = InferenceScope.Start(request, inferenceDetails, agentDetails))
{
    var completion = await CallLlmAsync();
    scope.RecordOutputMessages(new[] { completion.Text });
    scope.RecordInputTokens(completion.Usage.InputTokens);
    scope.RecordOutputTokens(completion.Usage.OutputTokens);
}
```

### Output (`OutputScope`)

Use this scope for asynchronous scenarios where `InvokeAgentScope`, `ExecuteToolScope`, or `InferenceScope` can't capture output data synchronously. Start `OutputScope` as a child span to record the final output messages after the parent scope finishes.

```csharp
// OutputScope is a child span — capture the parent context while InvokeAgentScope is still active.
// Example: save parentContext before disposing the InvokeAgentScope.
//   var parentContext = invokeScope.GetActivityContext();

var parentContext = savedParentContext; // ActivityContext from InvokeAgentScope.GetActivityContext()

var response = new Response(new[] { "Here is your organized inbox with 15 urgent emails." });

using (OutputScope.Start(
    request,
    response,
    agentDetails,
    spanDetails: new SpanDetails(parentContext: parentContext)))
{
    // Output messages are recorded automatically from the response
}
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
            return await MyTokenService.GetTokenAsync(agentId, tenantId);
        };

        // Optional: custom domain resolver
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
|---|---|---|
| **Auto (DI)** — default | Agent Framework apps with `UserAuthorization` | Distro internally calls `AddAgenticTracingExporter()`, registering `IExporterTokenCache<AgenticTokenStruct>`. Your agent calls `RegisterObservability()`. Token exchange happens via `ExchangeTurnTokenAsync`. |
| **Custom resolver** | Non-agent apps, service-to-service, or custom auth | Set `o.Agent365.Exporter.TokenResolver` directly. You own token acquisition. |

## Validate locally

### Console + Agent365 (validate locally and remotely)

To validate telemetry locally while also sending to Agent365, use both exporters:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Console | ExportTarget.Agent365;
});
```

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run
```

Send a message via Teams. In the console output, look for:

```
Activity.DisplayName:        chat gpt-*
Activity.DisplayName:        invoke_agent *
Activity.DisplayName:        MessageProcessor
```

### Debugging A365 export failures

To investigate export issues, enable verbose logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Agents.A365.Observability": "Debug"
    }
  }
}
```

You can also set override environment variables for testing against custom endpoints:

```powershell
$env:A365_OBSERVABILITY_DOMAIN_OVERRIDE = "https://your-test-endpoint.example.com"
$env:A365_OBSERVABILITY_SCOPE_OVERRIDE = "https://api.powerplatform.com/.default"
```

Key log messages to look for:

```
dbug: Agent365ExporterCore: Obtained token for agent {agentId} tenant {tenantId}.
dbug: Agent365ExporterCore: Sending {spanCount} spans to {requestUri}.
dbug: Agent365ExporterCore: HTTP {statusCode} exporting spans. 'x-ms-correlation-id': '{correlationId}'.
warn: Agent365ExporterCore: No token obtained. Skipping export for this identity.
fail: Agent365ExporterCore: TokenResolver threw for agent {agentId} tenant {tenantId}.
fail: Agent365ExporterCore: Exception exporting spans.
```

### Verify in A365 export

When using `ExportTarget.Console | ExportTarget.Agent365`, look for successful HTTP responses:

```
Received HTTP response headers after *ms - 200
```

## Validate for store publishing

> **Important:** For successful store validation, your agent **must** implement the `InvokeAgentScope`, `InferenceScope`, and `ExecuteToolScope` scopes. These three scopes are required for publishing.

### `InvokeAgentScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "microsoft.a365.caller.agent.blueprint.id": "Optional",
    "microsoft.a365.caller.agent.id": "Optional",
    "microsoft.a365.caller.agent.name": "Optional",
    "microsoft.a365.caller.agent.platform.id": "Optional",
    "microsoft.a365.caller.agent.user.email": "Optional",
    "microsoft.a365.caller.agent.user.id": "Optional",
    "microsoft.a365.caller.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.input.messages": "Required",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "server.address": "Required",
    "server.port": "Required",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `ExecuteToolScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.operation.name": "Required",
    "gen_ai.tool.call.arguments": "Required",
    "gen_ai.tool.call.id": "Required",
    "gen_ai.tool.call.result": "Required",
    "gen_ai.tool.description": "Optional",
    "gen_ai.tool.name": "Required",
    "gen_ai.tool.type": "Required",
    "server.address": "Optional",
    "server.port": "Optional",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `InferenceScope` attributes

```json
{
    "error.type": "Optional",
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.a365.agent.thought.process": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.input.messages": "Required",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "gen_ai.provider.name": "Required",
    "gen_ai.request.model": "Required",
    "gen_ai.response.finish_reasons": "Optional",
    "gen_ai.usage.input_tokens": "Optional",
    "gen_ai.usage.output_tokens": "Optional",
    "server.address": "Optional",
    "server.port": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.session.id": "Optional",
    "microsoft.tenant.id": "Required"
}
```

### `OutputScope` attributes

```json
{
    "microsoft.a365.agent.blueprint.id": "Required",
    "gen_ai.agent.description": "Optional",
    "gen_ai.agent.id": "Required",
    "gen_ai.agent.name": "Required",
    "microsoft.a365.agent.platform.id": "Optional",
    "microsoft.agent.user.email": "Required",
    "microsoft.agent.user.id": "Required",
    "gen_ai.agent.version": "Optional",
    "client.address": "Required",
    "user.id": "Required",
    "user.name": "Optional",
    "user.email": "Required",
    "microsoft.channel.link": "Optional",
    "microsoft.channel.name": "Required",
    "gen_ai.conversation.id": "Required",
    "microsoft.conversation.item.link": "Optional",
    "gen_ai.operation.name": "Required",
    "gen_ai.output.messages": "Required",
    "microsoft.session.id": "Optional",
    "microsoft.session.description": "Optional",
    "microsoft.tenant.id": "Required"
}
```

## Viewing exported logs

To view agent telemetry in Microsoft Purview or Microsoft Defender:

- **Microsoft Purview**: Auditing must be turned on for your organization. See [Turn auditing on or off](https://learn.microsoft.com/en-us/purview/audit-log-enable-disable#turn-on-auditing).
- **Microsoft Defender**: Advanced hunting must be configured to access the `CloudAppEvents` table. See [CloudAppEvents table](https://learn.microsoft.com/en-us/defender-xdr/advanced-hunting-cloudappevents-table).

## Test your agent with observability

After implementing observability, verify telemetry capture:

1. Go to: `https://admin.cloud.microsoft/#/agents/all`
2. Select your agent > Activity
3. You should see sessions and tool calls

## Troubleshooting

### Observability data doesn't appear

**Symptoms:** Agent is running, no telemetry in admin center, can't see agent activity.

**Solutions:**

- Verify observability exporter is enabled via `ExportTarget.Agent365` in `UseMicrosoftOpenTelemetry()`.
- Check token resolver configuration — exporter requires a valid token.
- Enable verbose logging and check for observability-related errors.
- Add `ExportTarget.Console` and check if telemetry is generated locally.

### Missing tenant ID or agent ID — spans skipped

**Symptoms:** Spans are silently dropped and never exported.

**Resolution:** Ensure `BaggageBuilder` is set up with tenant ID and agent ID before creating spans. These values propagate through the OpenTelemetry context and attach to all spans created within the baggage scope.

### Token resolution failure — export skipped or unauthorized

**Symptoms:** Token resolver returns `null` or throws an error.

**Resolution:**

- Verify that a token resolver is provided and returns a valid Bearer token.
- Ensure correct tenant ID and agent ID are used for `BaggageBuilder`.
- For Azure-hosted agents, verify the Managed Identity has the required API permission for the observability scope.

### HTTP 401 Unauthorized

**Resolution:**

- Verify the token audience matches the observability endpoint scope (default: `api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite`).
- Ensure the identity has Agent 365 observability permissions.
- The `Agent365.Observability.OtelWrite` permission is required for .NET `0.3-beta` and newer.

### HTTP 403 Forbidden

**Resolution:**

- Verify the Tenant ID is in the Agent 365 allowed tenant list for trace ingestion.
- Confirm the agent identity has the required role or permission for the target tenant.

### HTTP 429 / 5xx — Transient errors

**Resolution:** These are usually transient. The .NET SDK doesn't retry automatically. Consider reducing export frequency by adjusting `MaxExportBatchSize` or `ScheduledDelayMilliseconds` in exporter options.

### Export timeout

**Resolution:**

- Check network connectivity to the observability endpoint.
- Default HTTP request timeout is 30 seconds.
- Increase `ExporterTimeoutMilliseconds` in exporter options if timeouts occur frequently.

### Export succeeds but telemetry doesn't appear in Defender or Purview

**Resolution:**

- Verify prerequisites for viewing exported logs (Purview auditing, Defender advanced hunting).
- Telemetry can take several minutes to populate after a successful export.

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.Agent365.Demo/` in the distro repo.
