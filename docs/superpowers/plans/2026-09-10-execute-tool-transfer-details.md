# Execute Tool Transfer Details Design

## Goal

Update PR [#157](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/157) so execute-tool transfers align with the repository's Microsoft Agent365 agent identity model. The change stays scoped to execute-tool telemetry and leaves invoke-agent behavior and attributes unchanged.

## Semantic model

On an `execute_tool` span:

- `gen_ai.agent.*` continues to identify the source agent passed to `ExecuteToolScope.Start(...)` or `ExecuteToolDataBuilder.Build(...)`.
- `TransferDetails` remains part of `ToolCallDetails` and carries explicit transfer metadata.
- `TransferDetails.Mode` serializes exactly as:
  - `TransferMode.ReturnToCaller` -> `return_to_caller`
  - `TransferMode.PassControl` -> `pass_control`
- `TransferDetails.TargetAgentDetails` is the only transfer target model. The earlier generic target name/type idea is removed because the API is still unshipped and this PR only supports agent targets.

The SDK never infers transfer attributes from tool names, endpoints, results, activity hierarchy, or any other source.

## Public API

`ToolCallDetails` keeps the existing transfer-aware constructor overloads and existing six-value deconstruction. `TransferDetails` becomes:

```csharp
public sealed class TransferDetails
{
    public TransferDetails(
        TransferMode mode,
        AgentDetails? targetAgentDetails = null);

    public TransferMode Mode { get; }

    public AgentDetails? TargetAgentDetails { get; }
}
```

This reuses the shipped `AgentDetails` contract for target identity and avoids introducing a second, partially overlapping identity model.

## Execute-tool target attributes

When `TransferDetails` is present, always emit `microsoft.a365.transfer.mode`.

When `TransferDetails.TargetAgentDetails` is present, emit only the non-null target agent fields below:

- `microsoft.a365.transfer.target.agent.id` <- `AgentDetails.AgentId`
- `microsoft.a365.transfer.target.agent.name` <- `AgentDetails.AgentName`
- `microsoft.a365.transfer.target.agent.blueprint.id` <- `AgentDetails.AgentBlueprintId`
- `microsoft.a365.transfer.target.agent.platform.id` <- `AgentDetails.AgentPlatformId`
- `microsoft.a365.transfer.target.agent.version` <- `AgentDetails.AgentVersion`

Do not emit:

- the removed generic transfer-target name/type attributes
- any `gen_ai.transfer.*` attributes

## Emission paths

`ExecuteToolScope` and `ExecuteToolDataBuilder` must serialize identical transfer values from the same `ToolCallDetails.TransferDetails` instance:

1. write the same `microsoft.a365.transfer.mode` string for both mode values;
2. omit all target agent attributes when `TargetAgentDetails` is null;
3. omit individual agent attributes when the corresponding `AgentDetails` property is null;
4. never substitute source-agent values into transfer-target fields.

## Canonical example

```csharp
var targetAgentDetails = new AgentDetails(
    agentId: "weather-agent-id",
    agentName: "Weather Agent",
    agentBlueprintId: "weather-blueprint",
    agentPlatformId: "weather-platform",
    agentVersion: "2026.09.10");

var toolCallDetails = new ToolCallDetails(
    toolName: "handoff",
    transferDetails: new TransferDetails(
        mode: TransferMode.ReturnToCaller,
        targetAgentDetails: targetAgentDetails),
    arguments: "{\"city\":\"Seattle\",\"units\":\"metric\"}",
    toolCallId: "tc-001",
    description: "Delegate weather lookup to the weather specialist agent",
    toolType: "function",
    endpoint: new Uri("https://weather-agent.contoso.com"));

using var scope = ExecuteToolScope.Start(
    request: request,
    details: toolCallDetails,
    agentDetails: agentDetails);
```

## Testing and validation

Tests must cover:

- both serialized mode values;
- all five target agent attributes in Activity and DTO paths;
- mode-only omission;
- partial target agent omission;
- equality and hash code behavior for `TransferDetails`/`ToolCallDetails`;
- absence of removed generic transfer-target API references.

Validation must include focused net8.0 tests for `ToolCallDetailsTests`, `ExecuteToolScopeTest`, and `ExecuteToolDataBuilderTests`; full Agent365 test runs on net8.0 and net10.0; a warnings-as-errors build of `Microsoft.OpenTelemetry`; `git diff --check`; and repository searches proving the obsolete generic target API is gone.