# Agent365 S2S Observability Sample

This .NET 8 console sample uses the Microsoft OpenTelemetry distro and Agent365
manual scope APIs to export a single S2S trace containing:

1. InvokeAgent
2. Inference that selects a tool
3. ExecuteTool
4. Inference that produces the final answer

It does not use Agents Framework, an LLM, or a real tool endpoint.

## Prerequisites

- .NET 8 SDK
- Blueprint application client ID and secret
- Microsoft Entra tenant ID
- Agent app instance ID
- An onboarded Agent365 agent ID
- `Agent365.Observability.OtelWrite` application permission with admin consent

## Configure

Copy `appsettings.example.json` to `appsettings.json` and replace every
placeholder. `appsettings.json` is gitignored.

## Run

```powershell
dotnet run --project .\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

The console exporter prints all four spans. The Agent365 exporter prints an
HTTP success or failure status and the `x-ms-correlation-id` when returned.
Tokens and secrets are never printed.

## Authentication flow

The sample uses MSAL.NET to:

1. Acquire an Azure AD token-exchange token for the blueprint app with the
   agent app instance ID as the FMI path.
2. Use that token as the agent app instance client assertion.
3. Acquire an application token for the Agent365 observability `/.default`
   scope.
