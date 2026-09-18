# Multi-endpoint routing to Azure Monitor (.NET)

> **Preview.** This feature is off by default and is enabled with an `AppContext` switch. The
> behaviour described here may change before it is generally available.

Multi-endpoint routing lets one application send telemetry to several Application Insights
components. A service handling work for several customers can send each customer's telemetry to that
customer's component.

Your application supplies the destination for every telemetry item:

- **Traces and logs** - in an OpenTelemetry processor, obtain the connection string for the item's
  customer, then attach its instrumentation key and ingestion endpoint as routing attributes.
- **Metrics** - obtain the same values at the measurement call and pass them as dimensions to
  `Counter.Add`, `Histogram.Record`, or another measurement API.

How you identify the customer, retrieve the connection string, and cache parsed values is
application-owned. Routing does not require a startup catalog, a particular cache, or a particular
context mechanism.

If your application sends all telemetry to one component, use the standard configuration in
[Azure Monitor Getting Started](azure-monitor-getting-started.md).

## 1. Requirements

- A `Microsoft.OpenTelemetry` release that includes multi-endpoint routing. It is **not** in
  `Microsoft.OpenTelemetry` 1.1.0; that package predates the feature and ignores the switch.
- **Leave `AzureMonitor.Credential` unset.** Configuring a credential with routing throws
  `NotSupportedException` at startup.
- **Set `o.Exporters` explicitly.** The distro only selects Azure Monitor automatically when it finds
  a connection string, and a routing-only application usually has none.

## 2. Set up routing

The switch is read once and applies to the whole process. Set it before OpenTelemetry is registered
and before the host is built.

```csharp
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

AppContext.SetSwitch("Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Required for a routing-only application with no host connection string.
        o.Exporters = ExportTarget.AzureMonitor;
        // Leave o.AzureMonitor.Credential unset.

        // Optional, but recommended: these are disabled or ignored under routing anyway,
        // and setting them explicitly avoids collecting telemetry that is then discarded.
        o.AzureMonitor.EnableLiveMetrics = false;
        o.AzureMonitor.EnableStandardMetrics = false;
        o.AzureMonitor.EnablePerfCounters = false;
        o.AzureMonitor.TracesPerSecond = null;
        o.AzureMonitor.SamplingRatio = 1.0F;
    })
    .WithTracing(tracing => tracing
        .AddSource("Contoso.Application")
        .AddProcessor(new RoutingActivityProcessor(customerRouting.ForActivity)))
    .WithLogging(logging => logging
        .AddProcessor(new RoutingLogProcessor(customerRouting.ForLogRecord)))
    .WithMetrics(metrics => metrics
        .AddMeter("Contoso.Application"));

var app = builder.Build();
```

`customerRouting` stands for your own customer-resolution logic; section 3 defines what it must
return. Leave `AzureMonitor.ConnectionString` unset if the application has no component of its own.
Call `UseMicrosoftOpenTelemetry` only once per service collection - a second call throws.

Alternatively, set the switch in the executable project's file so it applies before any application
code runs:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Azure.Monitor.OpenTelemetry.EnableMultiEndpointRouting"
                                  Value="true" />
</ItemGroup>
```

## 3. Resolve the customer's connection string

Everything below depends on one application-owned lookup. The examples take it as a function per
signal, so that each call receives the item being processed and can resolve the customer from it:

| Input | Returns | Your responsibility |
|---|---|---|
| An `Activity` | That activity's customer connection string, or `null` | Identify and authorize the customer for that activity |
| A `LogRecord` | That record's customer connection string, or `null` | Identify and authorize the customer for that record |
| Customer context at a measurement call | That customer's connection string, or `null` | Resolve the customer before recording the measurement |

```csharp
public interface ICustomerRouting
{
    string? ForActivity(Activity activity);

    string? ForLogRecord(LogRecord record);
}
```

These are not SDK types. Implement them with your existing customer context and configuration - a
cache, a dictionary, a secret store, or a dynamic lookup. This guide does not prescribe storage,
caching, or how customer context is carried.

Two rules matter:

- **Never substitute another customer's connection string.** Return `null` when the customer cannot
  be determined or is not authorized.
- **Processor callbacks are synchronous.** Avoid blocking remote lookups inside them.

> **Trust requirement.** Obtain the connection string for an authorized customer through trusted
> application logic. Routing values are validated for shape, but nothing verifies that the caller is
> entitled to that destination, so an attacker-supplied `https` endpoint would be accepted. Never
> derive these values from a request header, propagated baggage, or a caller-supplied payload.

### The routing attributes

| Attribute | Required | Value |
|---|---|---|
| `microsoft.instrumentation_key` | Yes | The customer's instrumentation key. |
| `microsoft.ingestion_endpoint` | Yes | The customer's ingestion endpoint. |
| `microsoft.multi_endpoint_cloud_role` | No | Sets `ai.cloud.role`. Missing or blank becomes `unknown_service`. |

Values must be strings. The endpoint must be an absolute `https` URI without credentials, query
string, or fragment.

**An item with missing or invalid routing attributes is dropped.** This applies whether or not a
connection string is configured for the application itself - routed telemetry never falls back to the
application's own component.

### Extracting the two values

The processors need the key and endpoint rather than the raw connection string. If your application
already stores parsed values, use them directly.

```csharp
public sealed record RoutingDestination(string InstrumentationKey, string IngestionEndpoint)
{
    public static RoutingDestination? FromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var values = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);

        return values.TryGetValue("InstrumentationKey", out var key)
            && values.TryGetValue("IngestionEndpoint", out var endpoint)
            ? new RoutingDestination(key, endpoint)
            : null;
    }
}
```

Parsing on every item is wasteful. Caching the parsed pair is a reasonable optimisation, and yours to
make.

## 4. Traces and logs: attach the attributes in a processor

A processor covers telemetry from your own code and from instrumentation you do not own - ASP.NET
Core, HttpClient, SQL Client, Azure SDK, and the GenAI, Agent Framework, Semantic Kernel, and
Agent365 sources the distro registers.

A processor does **not** discover the customer by itself. Your function must resolve the correct
customer for the item when the processor runs.

Both processors below own the routing attribute names: they replace any existing values, and remove
them when resolution returns nothing, so a stale or caller-supplied value cannot select a
destination. Unrelated attributes are preserved.

### Traces

```csharp
using System.Diagnostics;
using OpenTelemetry;

public sealed class RoutingActivityProcessor(Func<Activity, string?> resolveConnectionString)
    : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        var destination = RoutingDestination.FromConnectionString(resolveConnectionString(activity));

        // SetTag replaces an existing value, and a null value removes the tag.
        activity.SetTag("microsoft.instrumentation_key", destination?.InstrumentationKey);
        activity.SetTag("microsoft.ingestion_endpoint", destination?.IngestionEndpoint);
    }
}
```

To use a per-item cloud role, resolve it through the same trusted logic and set
`microsoft.multi_endpoint_cloud_role` alongside the other two.

### Logs

Routing reads `LogRecord.Attributes` and takes the **first** occurrence of each key, so appending
would lose to a pre-existing value. Rebuild the list instead:

```csharp
using OpenTelemetry;
using OpenTelemetry.Logs;

public sealed class RoutingLogProcessor(Func<LogRecord, string?> resolveConnectionString)
    : BaseProcessor<LogRecord>
{
    private static readonly string[] RoutingKeys =
    [
        "microsoft.instrumentation_key",
        "microsoft.ingestion_endpoint",
        "microsoft.multi_endpoint_cloud_role",
    ];

    public override void OnEnd(LogRecord record)
    {
        var attributes = (record.Attributes ?? [])
            .Where(attribute => !RoutingKeys.Contains(attribute.Key))
            .ToList();

        var destination = RoutingDestination.FromConnectionString(resolveConnectionString(record));

        if (destination is not null)
        {
            attributes.Add(new("microsoft.instrumentation_key", destination.InstrumentationKey));
            attributes.Add(new("microsoft.ingestion_endpoint", destination.IngestionEndpoint));
        }

        // With no destination the record keeps no routing attributes and is dropped.
        record.Attributes = attributes;
    }
}
```

Logging scopes are not consulted, so putting routing values only in a scope has no effect.

### Make sure the customer is resolvable at the callback

`OnEnd` can run after your application code has unwound. The ASP.NET Core server activity in
particular **starts before** authentication and any customer resolution in middleware, and ends after
the pipeline has unwound - so a request-local value may already be cleared, and capturing ambient
state in `OnStart` does not help.

Because the resolver receives the item, prefer resolving from something durable on the item itself,
such as a tag or a custom property your code attaches while the customer is known. For queued or
background work, carry the customer identity with the work item and make it available before the work
runs. Telemetry with no assigned customer must not inherit a previous customer's destination.

## 5. Metrics: attach the values at measurement time

A metric's dimensions are part of its aggregation key, so the destination must be supplied **when the
measurement is recorded**. A trace or log processor does not enrich metrics, and the values cannot be
recovered once measurements for different customers have been aggregated together.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class CustomerMetrics
{
    private static readonly Meter Meter = new("Contoso.Application");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("contoso.requests");

    public static void RecordRequest(string? customerConnectionString)
    {
        var destination = RoutingDestination.FromConnectionString(customerConnectionString);

        if (destination is null)
        {
            // Do not record this measurement under another customer's route.
            return;
        }

        var tags = new TagList
        {
            { "microsoft.instrumentation_key", destination.InstrumentationKey },
            { "microsoft.ingestion_endpoint", destination.IngestionEndpoint },
        };

        Requests.Add(1, tags);
    }
}
```

Register the meter with `.AddMeter("Contoso.Application")`, as shown in section 2. The same pattern
applies to histograms and other instruments.

### Metrics from instrumentation libraries

On .NET 8 and later, the HTTP request-duration instruments have dedicated enrichment hooks:

| Instrument | Enrichment point |
|---|---|
| `http.server.request.duration` | `IHttpMetricsTagsFeature.Tags` on the request |
| `http.client.request.duration` | `HttpMetricsEnrichmentContext.AddCallback` on the outgoing request |

Attach the same routing dimensions there. These hooks do not cover every instrument in those meters;
instruments without routing dimensions are dropped. Filter the ones you do not need rather than
assuming the whole meter is enriched:

```csharp
metrics.AddView("http.server.active_requests", MetricStreamConfiguration.Drop);
```

Two limits worth knowing:

- A view that restricts `TagKeys` must keep the routing dimensions, and the cloud-role dimension if
  you use it. Views can drop dimensions but cannot add them back.
- Destination dimensions increase cardinality. Past the OpenTelemetry limit (2000 series per metric
  by default) measurements collapse into a single overflow point tagged `otel.metric.overflow=true`.
  It carries no destination, cannot be attributed to one customer, and must not be given a fallback.

## 6. Verify isolation with two customers

Isolation is the property worth testing. Use two authorized test customers with different components,
give each a fixed marker, and emit a trace, a log, and a metric for each.

```csharp
activity?.SetTag("routing.test_marker", "customer-a-test");
```

Query each component after ingestion:

```kusto
union requests, dependencies, traces, customMetrics
| where timestamp > ago(30m)
| extend marker = tostring(customDimensions["routing.test_marker"])
| summarize count() by marker
```

| Component | Must contain | Must not contain |
|---|---|---|
| Customer A | `customer-a-test` | `customer-b-test` |
| Customer B | `customer-b-test` | `customer-a-test` |

Repeat with concurrent work for both customers to exercise context isolation. Also exercise
unresolved context: confirm the processors remove existing routing attributes, that the item is not
sent to a host connection string, and that the metric call skips recording.

Counts alone do not establish isolation, and a successful export does not prove an item is queryable
yet. The marker is for verification - avoid unbounded unique dimensions in production.

## 7. What changes while routing is enabled

| Setting | Default | Under routing |
|---|---|---|
| `AzureMonitor.Credential` | unset | **Must stay unset.** Throws `NotSupportedException` at startup |
| `o.Exporters` | auto-detected | **Must be set explicitly** when there is no connection string |
| `AzureMonitor.ConnectionString` | from environment | Optional, and never a fallback for items without a valid route |
| `AzureMonitor.TracesPerSecond` | `5.0` | Ignored. Rate limiting is per process, so one destination would consume another's allowance |
| `AzureMonitor.SamplingRatio` | `1.0` | Becomes the effective sampler. The default samples everything |
| `AzureMonitor.EnableLiveMetrics` | `true` | Disabled. Live Metrics cannot serve routed destinations |
| `AzureMonitor.EnableStandardMetrics` | `true` | Not collected |
| `AzureMonitor.EnablePerfCounters` | `true` | Not collected. A process-wide counter has no single destination |

Sampling also affects logs when `EnableTraceBasedLogsSampler` is on, so review those together.

**Buffering and offline storage.** Routed telemetry is buffered in up to 64 partitions keyed by
**normalized ingestion endpoint**, not by customer, sharing a 100 MiB budget. Customers whose
components share an endpoint share a partition, so this is not a 64-customer limit. Beyond 64
endpoints telemetry still transmits but without an offline fallback, and a full budget evicts the
oldest stored telemetry, which may belong to another endpoint. Partitioning is not per-customer
capacity isolation.

**Additional export targets.** Azure Monitor consumes the routing attributes during conversion, but
that does not sanitize the underlying `Activity` and `LogRecord` for other exporters. Check what
Console, OTLP, or Agent365 emit before combining them, and do not strip the attributes in a processor
that runs before Azure Monitor reads them.

**Cloud role and resources.** `microsoft.multi_endpoint_cloud_role` sets `ai.cloud.role` per item.
The provider resource is shared, so it does not become a separate resource per customer.

## 8. Troubleshooting

Routing decisions are reported through the Azure Monitor event source, not through application logs.
Capture them with `dotnet-trace`, which writes a `.nettrace` file you can open in PerfView or Visual
Studio:

```bash
dotnet-trace collect --process-id <PID> --providers OpenTelemetry-AzureMonitor-Exporter::Verbose
```

| Symptom | Check | Events |
|---|---|---|
| Nothing reaches Azure Monitor | `o.Exporters` set; switch set before registration | `MultiEndpointRoutingEnabled` confirms routing is active |
| Startup throws | A credential is configured | `NotSupportedException` at startup |
| Traces missing | Resolver returned a value for that activity; values valid; sampling | `RoutedTelemetryRejected` (Verbose) names the reason; `RoutedExportSummary` (Informational) counts routed and dropped items |
| Metrics missing | Dimensions supplied at measurement time; meter registered; views keep the dimensions; cardinality overflow | `RoutedMetricRejected` (Verbose), `RoutedInstrumentDropped` (Informational) |
| Logs missing | Processor registered; attributes rather than scopes; resolver returned a value | **Dropped log records emit no per-record event.** Compare expected counts against `RoutedGroupOutcome`, and assert on `LogRecord.Attributes` in a unit test |
| Live Metrics or standard metrics absent | Disabled automatically | `LiveMetricsDisabledForMultiEndpointRouting`, `StandardMetricsDisabledForMultiEndpointRouting` |
| Buffered telemetry lost | Partition cap or shared budget | `MultiEndpointPartitionCapReached`, `RoutedTelemetryEvicted` |

`RoutedGroupOutcome` (Informational) reports item count and status code per destination, which is the
quickest way to confirm a specific destination is receiving data.

## Sample

[Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo](../examples/Microsoft.OpenTelemetry.MultiEndpoint.Console.Demo)
routes traces, logs, and metrics to two Application Insights components. It attaches the attributes
at the call site rather than in a processor, because it emits all of its own telemetry.
