# Changelog

## 1.0.0-beta.1

### Features

- Unified Microsoft OpenTelemetry distro combining Azure Monitor, Agent365, and Microsoft Agent Framework observability
- Single-line onboarding via `builder.UseMicrosoftOpenTelemetry()`
- `ExportTarget` flags enum for explicit exporter selection (Azure Monitor, Agent365, OTLP, Console)
- Auto-detection of exporters from connection string (code, env var, IConfiguration) and token resolver
- Azure Monitor distro: ASP.NET Core, HTTP Client, SQL Client instrumentation, resource detection, Azure SDK log forwarding
- Agent365 observability: InvokeAgent, Inference, ExecuteTool, Output scopes, baggage context propagation, Agent365 exporter
- Microsoft Agent Framework: captures `Experimental.Microsoft.Agents.AI` activity sources and metrics
- Agent365 framework extensions: Semantic Kernel, Agent Framework, Azure OpenAI (opt-in via fluent API)
- `IHostApplicationBuilder` and `IOpenTelemetryBuilder` entry points
- Agent365-only mode: infrastructure instrumentation auto-disabled when A365 is the sole exporter (#29)
- Strong-name signing with Microsoft shared library key
- Targets `net8.0` and `netstandard2.0`
