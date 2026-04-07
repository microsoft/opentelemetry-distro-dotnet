# Microsoft Agent Framework (MAF) Demo

This demo shows how the **Microsoft OpenTelemetry Distro** captures telemetry from the [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) with a single line of code.

## What it does

- Creates a MAF `AIAgent` backed by Azure OpenAI with a `GetWeather` tool
- Exposes a `POST /chat` endpoint that invokes the agent
- Exports traces and metrics to **Azure Monitor** (Application Insights) and **Console**
- The distro's `UseAgentFramework()` is enabled by default — no extra configuration needed

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- An [Azure OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) resource with a deployed model (e.g., `gpt-5.4-mini`)
- An [Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview) resource (optional — for Azure Monitor export)

## Configuration

Set the following environment variables:

| Variable | Required | Description |
|----------|----------|-------------|
| `AZURE_OPENAI_ENDPOINT` | Yes | Azure OpenAI endpoint (e.g., `https://your-resource.openai.azure.com/`) |
| `AZURE_OPENAI_API_KEY` | Yes | Azure OpenAI API key |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | No | Model deployment name (defaults to `gpt-5.4-mini`) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | No | Application Insights connection string (enables Azure Monitor export) |

These are pre-configured in `Properties/launchSettings.json` for local development.

## Running the demo

```powershell
# From the repo root
dotnet run --project examples/Microsoft.OpenTelemetry.AgentFramework.Demo
```

The app starts on `http://localhost:5081`.

## Testing

Send a chat request:

```powershell
Invoke-RestMethod -Uri http://localhost:5081/chat `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"message":"What is the weather in Seattle?"}'
```

Expected response:

```json
{
  "reply": { "messages": [...], "finishReason": "stop", ... },
  "traceId": "abc123..."
}
```

The agent will invoke the `GetWeather` tool and return the result.

## Viewing traces

- **Console**: Traces are printed to the terminal where the app is running
- **Azure Monitor**: Traces appear in Application Insights → **Transaction search** (allow 2-5 minutes for ingestion). Search by the `traceId` from the response.

## How the distro simplifies this

Without the distro, you would need ~50 lines of manual OpenTelemetry setup (see the [MAF AgentOpenTelemetry sample](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/AgentOpenTelemetry)). With the distro, it's one line:

```csharp
builder.UseMicrosoftOpenTelemetry(o =>
{
    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Console;
});
```

This automatically:
- Subscribes to MAF activity sources (`Experimental.Microsoft.Agents.AI*`)
- Adds ASP.NET Core, HTTP client, and SQL client instrumentation
- Configures Azure Monitor and Console exporters
- Detects Azure resource metadata (VM, App Service, Container Apps)
