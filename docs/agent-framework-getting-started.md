# Agent Framework Observability (.NET)

> **Standalone guide** — covers everything you need to add observability to [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agents using the `Microsoft.OpenTelemetry` distro, with telemetry sent to Azure Monitor and/or OTLP-compatible backends.

The Microsoft OpenTelemetry distro provides built-in support for capturing telemetry from the Microsoft Agent Framework (MAF). With a single line of configuration, the distro automatically subscribes to MAF's activity sources, captures traces and metrics from agent invocations, tool executions, and LLM inference calls, and exports them to Azure Monitor (Application Insights), OTLP endpoints, or the console.

## Key benefits

- **Zero manual wiring**: The distro auto-registers MAF activity source listeners — no need to manually call `AddSource()` for each MAF source.
- **End-to-end agent tracing**: Capture `invoke_agent`, `chat`, and `execute_tool` spans with full parent-child relationships.
- **Multi-destination export**: Send telemetry to Azure Monitor, OTLP (Aspire Dashboard, Jaeger, Grafana), or console — simultaneously.
- **GenAI semantic conventions**: Spans follow the [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/) for consistent observability across agent platforms.

## Auto-captured spans and metrics

Once observability is enabled, the distro automatically captures:

### Spans

| Span | Description |
|---|---|
| `invoke_agent <agent_name>` | Top-level span for each agent invocation. Contains all other spans as children. |
| `chat <model_name>` | Span for LLM calls. Includes prompt and response as attributes when sensitive data is enabled. |
| `execute_tool <function_name>` | Span for tool/function executions. Includes arguments and results when sensitive data is enabled. |

### Metrics

| Metric | Type | Description |
|---|---|---|
| `gen_ai.client.operation.duration` | Histogram | Duration of each LLM operation (seconds). |
| `gen_ai.client.token.usage` | Histogram | Token usage per LLM call. |
| `agent_framework.function.invocation.duration` | Histogram | Duration of tool/function invocations (seconds). |

### Activity sources

The distro listens to these MAF activity sources by default:

| ActivitySource | Description |
|---|---|
| `Experimental.Microsoft.Agents.AI` | Default source — used when no custom `sourceName` is specified in `.UseOpenTelemetry()`. |
| `Experimental.Microsoft.Agents.AI.Agent` | Agent-level operations. |
| `Experimental.Microsoft.Agents.AI.ChatClient` | Chat client operations. |

> **Important:** If you specify a custom `sourceName` in `.UseOpenTelemetry()` or `.WithOpenTelemetry()` (e.g., `sourceName: "MyAgentApp"`), you **must** also register that source with the distro using `.WithTracing()` and `.WithMetrics()`. See [Using a custom ActivitySource name](#using-a-custom-activitysource-name) for details.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agent
- An [Azure OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) or OpenAI resource for the backing LLM
- An [Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview) resource (for Azure Monitor export) and/or an OTLP-compatible backend

## Installation

Install the Microsoft OpenTelemetry Distro NuGet package:

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

> **Note:** Add a `nuget.config` in your project root if the package is not available on the public NuGet feed. Check the [README](../README.md) for feed configuration.

## Configuration (hosted apps)

### Send to Azure Monitor

In `Program.cs`, call `UseMicrosoftOpenTelemetry()` to enable observability. The distro automatically captures MAF telemetry — no extra configuration needed:

```csharp
using Microsoft.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

var app = builder.Build();
```

Set the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable, or configure it explicitly:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
});
```

### Send to OTLP (Aspire Dashboard, Jaeger, Grafana)

To export telemetry to an OTLP-compatible backend such as the [Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview):

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp;
});
```

Set the OTLP endpoint via environment variable:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
```

### Send to Azure Monitor + OTLP + Console

Combine multiple export targets:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Otlp | ExportTarget.Console;
    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
});
```

> ⚠️ **Production warning:** `ExportTarget.Console` is intended for local development only. Do not include it in production deployments — it adds overhead and may log sensitive telemetry to stdout.

## Configuration (non-hosted apps)

For console applications or non-hosted scenarios, use `OpenTelemetrySdk.Create()` with the distro's `UseMicrosoftOpenTelemetry()` extension. The distro automatically registers all MAF activity sources — no manual `AddSource()` calls needed:

> **Important:** Do not dispose the SDK until your application is shutting down. Disposing it early stops all telemetry collection and export — you will lose data.

### Non-hosted with Azure Monitor

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
        o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    });
});

// Build and run your agent — all MAF spans and metrics are captured automatically.
// Dispose the SDK only at application shutdown.
```

### Non-hosted with OTLP (Aspire Dashboard)

To send telemetry to the Aspire Dashboard or any OTLP-compatible backend:

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Otlp;
    });
});

// Build and run your agent.
// Dispose the SDK only at application shutdown.
```

Set the OTLP endpoint via environment variable:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
```

Combine Azure Monitor and OTLP:

```csharp
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Otlp | ExportTarget.Console;
    });
});
```

## Instrumenting the agent

Regardless of which builder pattern you use, instrument your MAF agent by calling `.UseOpenTelemetry()` on the chat client and `.WithOpenTelemetry()` on the agent:

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

const string SourceName = "MyAgentApp";

var instrumentedChatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

var agent = new ChatClientAgent(
    instrumentedChatClient,
    name: "MyAgent",
    instructions: "You are a helpful assistant.",
    tools: [AIFunctionFactory.Create(GetWeather)])
    .WithOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true);
```

> **Important:** When you enable observability on both the chat client and the agent, you may see duplicated information — prompts and responses are captured in both spans. Depending on your needs, consider enabling observability on only one or the other to avoid duplication.

> ⚠️ **Warning:** Only enable `EnableSensitiveData` in development or testing environments. Sensitive data includes prompts, responses, function call arguments, and results.

> **Tip:** If you don't specify a `sourceName`, the default `Experimental.Microsoft.Agents.AI` is used, and the distro captures these spans automatically. If you use a custom `sourceName`, you must register it with the distro — see [Using a custom ActivitySource name](#using-a-custom-activitysource-name) below.

## Using a custom ActivitySource name

When you specify a custom `sourceName` in `.UseOpenTelemetry()` or `.WithOpenTelemetry()`, the Agent Framework emits spans under that custom source instead of the default `Experimental.Microsoft.Agents.AI*` sources. The distro only subscribes to the default sources automatically, so you **must** register your custom source name with the distro to collect those spans.

This follows the standard OpenTelemetry pattern — the source name used for emission must match the source name registered for collection. See the [Agent Framework observability docs](https://learn.microsoft.com/en-us/agent-framework/agents/observability?pivots=programming-language-csharp) for details.

### Hosted apps

```csharp
using Microsoft.OpenTelemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

const string SourceName = "MyAgentApp";

var builder = WebApplication.CreateBuilder(args);

// 1. Configure the distro
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});

// 2. Register the custom source name for collection
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(SourceName))
    .WithMetrics(metrics => metrics.AddMeter(SourceName));

var app = builder.Build();

// 3. Instrument the agent with the same custom source name
var instrumentedChatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

var agent = new ChatClientAgent(instrumentedChatClient, name: "MyAgent", instructions: "...")
    .WithOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true);
```

### Non-hosted apps

```csharp
using Microsoft.OpenTelemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry;

const string SourceName = "MyAgentApp";

// 1. Configure the distro and register the custom source name
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.AzureMonitor;
    });

    otel.WithTracing(tracing => tracing.AddSource(SourceName));
    otel.WithMetrics(metrics => metrics.AddMeter(SourceName));
});

// 2. Instrument the agent with the same custom source name
var instrumentedChatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

var agent = new ChatClientAgent(instrumentedChatClient, name: "MyAgent", instructions: "...")
    .WithOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true);
```

> **Key point:** The `sourceName` passed to `.UseOpenTelemetry()` / `.WithOpenTelemetry()` must exactly match the source name passed to `.AddSource()` and `.AddMeter()`. If they don't match, spans will be silently dropped.

## Aspire Dashboard (local development)

The [Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview) is a quick way to visualize traces and metrics during development without needing Azure Monitor.

### Setting up Aspire Dashboard with Docker

```powershell
docker run --rm -it -d `
    -p 18888:18888 `
    -p 4317:18889 `
    --name aspire-dashboard `
    mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

This starts the dashboard with:
- **Web UI**: [http://localhost:18888](http://localhost:18888)
- **OTLP endpoint**: `http://localhost:4317` for receiving telemetry

### Configuring your application

**With the distro (hosted apps):**

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.Otlp | ExportTarget.Console;
});
```

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
dotnet run
```

**With `OpenTelemetrySdk.Create()` (non-hosted apps):**

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;

var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Otlp | ExportTarget.Console;
    });
});
```

Set the endpoint:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
```

Navigate to [http://localhost:18888](http://localhost:18888) to explore traces, logs, and metrics. Follow the [Aspire Dashboard exploration guide](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/explore) for details.

## Distro vs. manual setup

The distro simplifies Agent Framework observability significantly. Here is a comparison:

**Without the distro** (manual setup with `OpenTelemetrySdk.Create()`):

```csharp
var sdk = OpenTelemetrySdk.Create(otel =>
{
    otel.ConfigureResource(r => r.AddService("my-agent"));

    otel.WithTracing(tracing => tracing
        .AddSource("MyAgentApp")
        .AddSource("Experimental.Microsoft.Agents.AI")
        .AddSource("Experimental.Microsoft.Agents.AI.Agent")
        .AddSource("Experimental.Microsoft.Agents.AI.ChatClient")
        .AddHttpClientInstrumentation()
        .AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString));

    otel.WithMetrics(metrics => metrics
        .AddMeter("MyAgentApp")
        .AddMeter("Experimental.Microsoft.Agents.AI")
        .AddMeter("Experimental.Microsoft.Agents.AI.Agent")
        .AddMeter("Experimental.Microsoft.Agents.AI.ChatClient")
        .AddHttpClientInstrumentation()
        .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString));

    otel.WithLogging(logging => logging
        .AddAzureMonitorLogExporter(o => o.ConnectionString = connectionString));
});
```

**With the distro** (one line):

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
});
```

The distro automatically:
- Subscribes to MAF activity sources (`Experimental.Microsoft.Agents.AI*`)
- Adds ASP.NET Core, HTTP client, SQL client, and Azure SDK instrumentation
- Configures Azure Monitor exporters for traces, metrics, and logs
- Detects Azure resource metadata (VM, App Service, Container Apps)
- Adds a span processor that enriches MAF spans with additional metadata

## Controlling MAF instrumentation

To disable Agent Framework instrumentation while keeping other instrumentation active:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor;
    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
});
```

Other related instrumentation options:

| Property | Description | Default |
|---|---|---|
| `EnableAgentFrameworkInstrumentation` | Agent Framework operation traces and metrics. | `true` |
| `EnableOpenAIInstrumentation` | OpenAI / Azure OpenAI call traces. | `true` |
| `EnableSemanticKernelInstrumentation` | Semantic Kernel operation traces. | `true` |

## Troubleshooting

### No MAF spans appearing

**Symptoms:** Agent runs successfully, but no `invoke_agent`, `chat`, or `execute_tool` spans appear.

**Solutions:**

- Verify that `.UseOpenTelemetry()` is called on the chat client builder and/or `.WithOpenTelemetry()` is called on the agent.
- If using the distro (`UseMicrosoftOpenTelemetry()`), the default MAF sources (`Experimental.Microsoft.Agents.AI*`) are registered automatically. Check that `EnableAgentFrameworkInstrumentation` is not set to `false`.
- **If using a custom `sourceName`** (e.g., `UseOpenTelemetry(sourceName: "MyAgentApp")`), you must also register that source with the distro via `.WithTracing(t => t.AddSource("MyAgentApp"))` and `.WithMetrics(m => m.AddMeter("MyAgentApp"))`. The distro does not automatically detect custom source names. See [Using a custom ActivitySource name](#using-a-custom-activitysource-name).
- If using `OpenTelemetrySdk.Create()` **without** the distro, ensure you call `.AddSource("Experimental.Microsoft.Agents.AI")` (or your custom source name) in the tracer provider configuration.

### Duplicate spans for prompts and responses

**Symptoms:** The same prompt and response appear in both `chat` and `invoke_agent` spans.

**Resolution:** Enable observability on either the chat client or the agent, not both:

```csharp
// Option A: Instrument only the agent
var agent = new ChatClientAgent(chatClient, ...)
    .WithOpenTelemetry(sourceName: SourceName);

// Option B: Instrument only the chat client
var instrumentedClient = chatClient
    .AsBuilder()
    .UseOpenTelemetry(sourceName: SourceName)
    .Build();
var agent = new ChatClientAgent(instrumentedClient, ...);
```

### Missing telemetry in Azure Monitor

**Solutions:**

- Verify `APPLICATIONINSIGHTS_CONNECTION_STRING` is set correctly.
- Allow 2–5 minutes for telemetry ingestion.
- Search by `traceId` in Application Insights → **Transaction search**.
- Add `ExportTarget.Console` temporarily to verify telemetry is being produced locally.

## Reference implementation

See: `examples/Microsoft.OpenTelemetry.AgentFramework.Demo/` in the distro repo.

For the full manual setup approach (without the distro), see the [Agent Framework AgentOpenTelemetry sample](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/AgentOpenTelemetry).
