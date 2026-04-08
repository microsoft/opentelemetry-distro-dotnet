# MAF Telemetry Unification Plan

## Problem

Today, developers using Microsoft Agent Framework (MAF) with the Microsoft OpenTelemetry Distro need **two calls** to get end-to-end telemetry:

```csharp
// 1. Set up the distro pipeline (collection + export)
builder.UseMicrosoftOpenTelemetry();

// 2. Instrument the agent (emission)
agent = agentBuilder.UseOpenTelemetry().Build();
```

This is friction — especially for the Azure Monitor scenario where we want zero-config observability. Ideally, a single `UseMicrosoftOpenTelemetry()` call should be sufficient.

## Current Architecture

```
┌─────────────────────────┐     ┌──────────────────────────────┐
│  MAF (emission side)    │     │  Distro (collection side)    │
│                         │     │                              │
│  UseOpenTelemetry()     │     │  UseMicrosoftOpenTelemetry() │
│  ┌───────────────────┐  │     │  ┌────────────────────────┐  │
│  │ OpenTelemetryAgent│──┼──►──┼──│ UseAgentFramework()    │  │
│  │ emits spans on    │  │     │  │ listens to sources:    │  │
│  │ ActivitySources   │  │     │  │ - Experimental.MS...AI │  │
│  └───────────────────┘  │     │  │ - ...AI.Agent          │  │
│                         │     │  │ - ...AI.ChatClient     │  │
│  No distro dependency   │     │  │ + AgentFrameworkSpan-   │  │
│                         │     │  │   Processor             │  │
│                         │     │  └────────────────────────┘  │
│                         │     │  No MAF dependency           │
└─────────────────────────┘     └──────────────────────────────┘
```

The two sides are decoupled — MAF emits, the distro collects. But the developer must wire both.

## Proposed Solution: AppContext Switch

Use an ambient `AppContext` switch (the same pattern used by Azure SDK and Semantic Kernel) to let the distro signal MAF to auto-instrument agents.

### Distro Side (this repo)

In `UseAgentFramework()`, set the switch:

```csharp
AppContext.SetSwitch("Microsoft.Agents.AI.EnableOpenTelemetry", true);
```

This is a one-line addition. No MAF package reference needed.

### MAF Side (microsoft/agent-framework)

In `AIAgentBuilder.Build()`, check the switch and auto-wrap:

```csharp
public AIAgent Build()
{
    var agent = BuildCore();

    // Auto-instrument when a distro (or user) sets the ambient switch
    if (AppContext.TryGetSwitch("Microsoft.Agents.AI.EnableOpenTelemetry", out bool enabled) && enabled)
    {
        agent = new OpenTelemetryAgent(agent);
    }

    return agent;
}
```

This is ~5 lines. No distro package reference needed.

### Result

Developer writes one line:

```csharp
builder.UseMicrosoftOpenTelemetry();
```

MAF agents are automatically instrumented. The `UseOpenTelemetry()` API remains available for customers who want telemetry without a distro (e.g., sending to Jaeger, Zipkin, or other OTel backends directly).

## Precedent

This pattern is already proven in the .NET ecosystem:

| Library | Switch | Set By | Read By |
|---------|--------|--------|---------|
| Azure SDK | `Azure.Experimental.EnableActivitySource` | Distros / users | Azure SDK clients |
| Semantic Kernel | `Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive` | Distros / users | SK pipeline |
| **MAF (proposed)** | `Microsoft.Agents.AI.EnableOpenTelemetry` | Distros / users | MAF agent builder |

## Sensitive Data Capture

MAF already supports the `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` environment variable to control whether prompt/completion content is captured. The distro already sets the Semantic Kernel switch for sensitive data:

```csharp
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);
```

We should consider whether the distro should also set the GenAI content capture env var, or leave it to the developer.

## Activity Sources

### Currently Captured

| Source | Origin |
|--------|--------|
| `Experimental.Microsoft.Agents.AI` | Agent operations (invoke_agent, chat, execute_tool) |
| `Experimental.Microsoft.Agents.AI.Agent` | Agent-level telemetry |
| `Experimental.Microsoft.Agents.AI.ChatClient` | Chat client telemetry |

### Not Yet Captured (needs addition)

| Source | Origin |
|--------|--------|
| `Microsoft.Agents.AI.Workflows` | Workflow spans (session, invoke, message, executor, edge_group) |

The workflow source should be added to `AgentFrameworkConstants` and registered in `UseAgentFramework()`.

## Implementation Steps

### Phase 1: Distro (this repo)

1. Add `AppContext.SetSwitch("Microsoft.Agents.AI.EnableOpenTelemetry", true)` in `UseAgentFramework()`
2. Add `Microsoft.Agents.AI.Workflows` activity source to `AgentFrameworkConstants` and `UseAgentFramework()`
3. Update demo to remove `UseOpenTelemetry()` call (once MAF side is ready)

### Phase 2: MAF (microsoft/agent-framework)

1. Add switch check in `AIAgentBuilder.Build()` to auto-wrap with `OpenTelemetryAgent`
2. Document the switch in MAF's telemetry docs
3. Ensure `OpenTelemetryAgent` handles double-wrapping gracefully (no-op if already wrapped)

### Phase 3: Validation

1. Verify the demo works with single `UseMicrosoftOpenTelemetry()` call
2. Verify `UseOpenTelemetry()` still works standalone (no regression)
3. Verify workflow spans are captured
