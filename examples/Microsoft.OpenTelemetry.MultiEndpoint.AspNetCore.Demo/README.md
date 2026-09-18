# Multi-Endpoint Routing: ASP.NET Core Demo

A hosted ASP.NET Core application that routes each customer's telemetry to that customer's
Application Insights component, using one Azure Monitor exporter.

Traces, logs, and metrics all carry the destination:

| Signal | How the destination is attached |
|---|---|
| Traces | `RoutingActivityProcessor` stamps every activity, including ones from instrumentation libraries |
| Logs | `RoutingLogProcessor` stamps every record, so ordinary `ILogger` calls need no changes |
| Application metrics | `OrderMetrics` passes the dimensions to `Counter.Add` and `Histogram.Record` |
| `http.server.request.duration` | `CustomerContextMiddleware` uses `IHttpMetricsTagsFeature` |
| `http.client.request.duration` | `RoutingMetricsHandler` uses `HttpMetricsEnrichmentContext` |

For the full feature guide, see [Multi-Endpoint Routing](../../docs/multi-endpoint-routing.md).

## How a request is routed

1. `CustomerContextMiddleware` authorizes the caller and stamps an opaque `contoso.customer_id` on
   the request activity. It never stamps the connection string or the routing values themselves.
2. `CustomerRouting` resolves that identifier against `CustomerCatalog`, the server-owned map loaded
   from configuration. Child activities such as the outgoing HTTP call carry no identifier of their
   own, so it walks up `Activity.Parent` to the request activity.
3. The processors attach `microsoft.instrumentation_key` and `microsoft.ingestion_endpoint`, and the
   exporter sends each item to the endpoint it names.

When no customer can be authorized, the resolver returns `null`, the processors remove any routing
attributes, and the telemetry is dropped. It is never sent to another customer's component.

## Configure

Each customer needs a connection string containing an explicit `IngestionEndpoint`. Supply them
outside source control - user secrets, or environment variables:

```powershell
$env:MultiEndpointDemo__Customers__0__ConnectionString = "<contoso connection string>"
$env:MultiEndpointDemo__Customers__1__ConnectionString = "<fabrikam connection string>"
```

Add more customers by adding entries to the `MultiEndpointDemo:Customers` array in
`appsettings.json` and supplying the matching connection strings.

A customer whose connection string is missing or has no explicit `IngestionEndpoint` fails at
startup, rather than having every telemetry item silently dropped at runtime.

## Run

```powershell
dotnet run --project .\examples\Microsoft.OpenTelemetry.MultiEndpoint.AspNetCore.Demo
```

Send a request as each customer:

```powershell
curl -H "X-Api-Key: contoso-key"  http://localhost:5000/orders
curl -H "X-Api-Key: fabrikam-key" http://localhost:5000/orders
curl http://localhost:5000/health
```

`/orders` produces a request span, a nested client span, a log, and two application metrics, all
routed to the caller's component. `/health` has no caller, so its telemetry is dropped - that is the
expected result, not a failure.

## Verify isolation

Query each component. Each must contain only its own customer's telemetry:

```kusto
union withsource=Signal requests, dependencies, traces, customMetrics
| where timestamp > ago(30m)
| summarize count() by Signal, cloud_RoleName
```

`cloud_RoleName` comes from the optional `microsoft.multi_endpoint_cloud_role` attribute, so each
component shows the role configured for that customer and no other.

## Before copying this pattern

The API key header stands in for authentication so the sample stays self-contained. Replace
`CustomerCatalog.AuthorizeCustomer` with your real authentication and authorization; only the lookup
that follows it should stay the same.

What must not change: the destination is chosen from a server-owned store after the caller is
authorized. Never take an instrumentation key or ingestion endpoint from a request header,
propagated baggage, or a request body. Those values are validated for shape but not for ownership,
so a caller-supplied endpoint would receive your telemetry.

## What routing changes

Live Metrics, standard metrics, and performance counters are unavailable while routing is enabled,
and Microsoft Entra ID authentication cannot be used. Rate-limited sampling is ignored in favour of
fixed-rate `SamplingRatio`, because a per-process rate limit would be shared across every customer.
See the [feature guide](../../docs/multi-endpoint-routing.md) for the full list.
