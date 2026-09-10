# Execute Tool Transfer Details Design

## Goal

Add typed support for the agent-to-agent transfer attributes introduced by
OpenTelemetry semantic-conventions-genai PR 447. The change is limited to
`execute_tool` telemetry. It does not change `InvokeAgentScope` or invoke-agent
attributes.

## Semantic model

On an `execute_tool` span:

- `gen_ai.agent.*` continues to identify the source agent executing the tool.
- `gen_ai.transfer.mode` describes how control passes to the target.
- `gen_ai.transfer.target.name` identifies the target when available.
- `gen_ai.transfer.target.type` identifies whether the target is an agent,
  human, workflow, or a future custom value.

Transfer attributes are emitted only when the caller explicitly supplies
transfer details. The SDK must not infer them from tool names, span hierarchy,
timing, endpoints, or other application-specific conventions.

## Public API

Add an immutable `TransferDetails` contract and associate it with
`ToolCallDetails`. This keeps all facts describing a tool call together and
allows both the Activity-based scope and DTO builder APIs to consume the same
model.

`TransferDetails` contains:

- A required transfer mode represented by a typed enum with the standard
  `return_to_caller` and `pass_control` values.
- An optional target name.
- An optional target type represented by a typed enum covering the standard
  `agent`, `human`, and `workflow` values.

`ToolCallDetails` exposes an optional `TransferDetails` property. Existing
constructors and call sites remain source- and binary-compatible. New overloads
or optional constructor parameters must follow the repository's existing API
compatibility patterns without changing `ExecuteToolScope.Start`.

## Telemetry emission

Add constants for:

- `gen_ai.transfer.mode`
- `gen_ai.transfer.target.name`
- `gen_ai.transfer.target.type`

Wire the values through both execute-tool emission paths:

1. `ExecuteToolScope` sets Activity tags from `ToolCallDetails.TransferDetails`.
2. `ExecuteToolDataBuilder` adds the same attributes to ETW/export DTO data.

The two paths must produce identical attribute names and serialized enum values.
Absent transfer details produce no transfer attributes. Target name and type are
emitted only when present.

## Compatibility and validation

The change must not:

- Modify invoke-agent contracts, spans, or attributes.
- Replace or reinterpret `gen_ai.agent.*`.
- Emit transfer attributes for ordinary tool executions.
- Infer transfer semantics.
- Break existing `ToolCallDetails` constructors, deconstruction, equality, or
  hash-code behavior.

Equality and hash-code behavior must include transfer details when supplied.
Deconstruction should remain compatible with existing consumers; transfer
details can be exposed separately rather than changing the existing deconstruct
signature.

## Testing

Add focused tests covering:

- `return_to_caller` and `pass_control` serialization.
- Agent, human, and workflow target types.
- Optional target name and target type.
- No transfer attributes when details are absent.
- Activity-based `ExecuteToolScope` emission.
- DTO-based `ExecuteToolDataBuilder` emission.
- Existing constructor and deconstruction compatibility.
- Equality and hash-code behavior with transfer details.

Update integration assertions only where needed to verify the exported
attribute names; do not broaden invoke-agent coverage.

## Documentation

Update the execute-tool documentation and example to show an agent transfer and
explain that `agentDetails` is the source agent while `TransferDetails` is the
transfer target. Add a changelog entry referencing alignment with semantic
conventions PR 447.
