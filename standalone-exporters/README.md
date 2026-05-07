# A365.OpenTelemetry.Exporter

A standalone .NET OpenTelemetry SpanExporter for Agent 365 (A365) Observability.
This package is for teams that already have an OpenTelemetry TracerProvider configured
and want to add A365 as an additional export destination. Spans are grouped by
`tenant_id` and `agent_id` and POSTed to the A365 OTLP ingestion endpoint.

## Quick Start

Install the package:

```
dotnet add package A365.OpenTelemetry.Exporter
```

Register the exporter alongside your existing TracerProvider setup:

```csharp
using A365.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Trace;

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyAgent")
    .AddA365Exporter(opts =>
    {
        opts.Endpoint = "https://agent365.svc.cloud.microsoft";
        opts.TokenResolver = async (agentId, tenantId) =>
        {
            // Return a bearer token (see examples below).
            return await GetTokenAsync();
        };
    })
    // chain other exporters as usual
    .Build();
```

Set the required A365 routing attributes on your spans:

```csharp
using var baggage = new BaggageBuilder()
    .TenantId("00000000-0000-0000-0000-000000000001")
    .AgentId("00000000-0000-0000-0000-000000000002")
    .ConversationId("conv-123")  // optional
    .Build();

using var activity = source.StartActivity("invoke_agent");
// ... your agent logic ...
```

Or set attributes directly on individual spans:

```csharp
using var activity = source.StartActivity("invoke_agent");
BaggageBuilder.SetA365Attributes(activity!, tenantId, agentId);
```

## Token Resolver Examples

**DefaultAzureCredential (Azure.Identity):**

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

**MSAL Confidential Client:**

```csharp
using Microsoft.Identity.Client;

var app = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .Build();

opts.TokenResolver = async (agentId, tenantId) =>
{
    var result = await app.AcquireTokenForClient(
        new[] { "9b975845-388f-4429-889e-eab1ef63949c/.default" })
        .ExecuteAsync();
    return result.AccessToken;
};
```

## Required Span Attributes

The A365 backend expects the following semantic conventions on ingested spans:

| Attribute | Values | Description |
|-----------|--------|-------------|
| `gen_ai.operation.name` | `invoke_agent`, `execute_tool`, `chat`, `output_messages` | Operation type for the span |
| `tenant_id` or `a365.tenant_id` | GUID | Azure AD tenant that owns the agent |
| `agent_id` or `a365.agent_id` | GUID | Agent 365 agent identifier |

Spans that are missing both `tenant_id` and `agent_id` are silently skipped by the exporter.

## How It Works

1. The exporter collects spans from the OpenTelemetry batch.
2. Spans are grouped by `(tenant_id, agent_id)` read from tags or baggage.
3. Each group is serialized to OTLP JSON (ExportTraceServiceRequest format).
4. The payload is POSTed to `{endpoint}/observabilityService/tenants/{tenantId}/otlp/agents/{agentId}/traces`.
5. A bearer token is obtained per group via the configured `TokenResolver`.
