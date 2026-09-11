# Execute Tool Transfer Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconcile PR [#157](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/157) with the repository's Microsoft Agent365 agent identity model by making execute-tool transfer targets agent-specific.

**Architecture:** `ToolCallDetails` continues to own optional `TransferDetails`, but `TransferDetails` now accepts only optional `TargetAgentDetails : AgentDetails`. `ExecuteToolScope` and `ExecuteToolDataBuilder` read the same model and emit only `microsoft.a365.transfer.mode` plus the five `microsoft.a365.transfer.target.agent.*` attributes when explicitly supplied.

**Tech Stack:** C#, .NET 8/.NET 10, MSTest, FluentAssertions, PublicApiAnalyzer.

## Global Constraints

- Change only execute-tool transfer behavior.
- Do not modify invoke-agent behavior or attributes.
- Keep `gen_ai.agent.*` as the source-agent identity.
- Keep `TransferDetails` attached to `ToolCallDetails`.
- Preserve the shipped `ToolCallDetails` constructors that do not take transfer metadata.
- Preserve the existing six-value `ToolCallDetails` deconstruction shape.
- Remove the unshipped generic transfer-target name/type API.
- Emit only these target attributes from `TargetAgentDetails` when non-null: `microsoft.a365.transfer.target.agent.id`, `microsoft.a365.transfer.target.agent.name`, `microsoft.a365.transfer.target.agent.blueprint.id`, `microsoft.a365.transfer.target.agent.platform.id`, `microsoft.a365.transfer.target.agent.version`.
- Never infer target data.

---

## File Structure

- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs` for transfer contract, serialization, and equality coverage.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs` for Activity emission coverage.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs` for DTO/ETW emission coverage.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/TransferDetails.cs` for the final target-agent API.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs` for the five target-agent constants.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs` and `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs` for identical emission logic.
- Modify `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt` to match the final unshipped public API.
- Modify `docs/agent365-getting-started.md`, `CHANGELOG.md`, and this plan plus the paired design doc for the final agent-specific model.

### Task 1: Write the failing tests first

**Files:**
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs`

**Interfaces:**
- Produces: failing expectations for `TargetAgentDetails`, exact mode serialization, null omission, and obsolete-key removal.
- Preserves: existing ToolCallDetails non-transfer API expectations.

- [ ] Add contract tests asserting `TransferDetails.TargetAgentDetails` holds an `AgentDetails` instance with `AgentId`, `AgentName`, `AgentBlueprintId`, `AgentPlatformId`, and `AgentVersion`.
- [ ] Add tests asserting `TransferMode.ReturnToCaller` serializes to `return_to_caller` and `TransferMode.PassControl` serializes to `pass_control`.
- [ ] Add Activity-path tests asserting all five `microsoft.a365.transfer.target.agent.*` attributes, mode-only omission, partial omission, and absence of the removed generic transfer-target attributes.
- [ ] Add DTO-path tests asserting the same emitted values and omissions.
- [ ] Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "(FullyQualifiedName~ToolCallDetailsTests|FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ExecuteToolDataBuilderTests)" --no-restore
```

Expected RED: compile or test failures against the old generic transfer target API.

### Task 2: Implement the final agent-specific transfer model

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/TransferDetails.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs`
- Modify: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`

**Interfaces:**
- Produces: `TransferDetails(TransferMode mode, AgentDetails? targetAgentDetails = null)` and `TransferDetails.TargetAgentDetails`.
- Produces: the five `microsoft.a365.transfer.target.agent.*` attribute constants.

- [ ] Replace the removed generic transfer-target members with `TargetAgentDetails` on `TransferDetails`.
- [ ] Keep `TransferDetails` immutable and keep exact `ModeValue` serialization.
- [ ] Update equality and hashing to include `TargetAgentDetails`.
- [ ] Update `ExecuteToolScope` and `ExecuteToolDataBuilder` to emit identical transfer values and omit null target fields.
- [ ] Update `PublicAPI.Unshipped.txt` to remove the provisional generic target API and record the final unshipped shape.
- [ ] Re-run the focused net8.0 command above and expect GREEN.

### Task 3: Update documentation and release notes

**Files:**
- Modify: `docs/agent365-getting-started.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-09-10-execute-tool-transfer-details.md`
- Modify: `docs/superpowers/specs/2026-09-10-execute-tool-transfer-details-design.md`

**Interfaces:**
- Produces: final user-facing explanation of source-agent vs target-agent identity.

- [ ] Update the canonical execute-tool example to construct `targetAgentDetails` with `AgentId`, `AgentName`, `AgentBlueprintId`, `AgentPlatformId`, and `AgentVersion` and pass it into `new TransferDetails(...)`.
- [ ] Document that `gen_ai.agent.*` continues to describe the source agent.
- [ ] Document only these execute-tool transfer attributes: `microsoft.a365.transfer.mode`, `microsoft.a365.transfer.target.agent.id`, `microsoft.a365.transfer.target.agent.name`, `microsoft.a365.transfer.target.agent.blueprint.id`, `microsoft.a365.transfer.target.agent.platform.id`, `microsoft.a365.transfer.target.agent.version`.
- [ ] Keep the PR [#157](https://github.com/microsoft/opentelemetry-distro-dotnet/pull/157) link in the changelog/context docs.

### Task 4: Final verification

**Files:**
- Modify: `.superpowers/sdd/agent-target-revision-report.md`

- [ ] Run focused tests on net8.0:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "(FullyQualifiedName~ToolCallDetailsTests|FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ExecuteToolDataBuilderTests)" --no-restore
```

- [ ] Run the full Agent365 test project on net8.0 and net10.0:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --no-restore
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net10.0 --no-restore
```

- [ ] Build with warnings as errors:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --framework net8.0 --no-restore -p:ContinuousIntegrationBuild=true -warnaserror
```

- [ ] Check whitespace and stale references:

```powershell
git --no-pager diff --check
rg "TargetAgentDetails|microsoft\.a365\.transfer\.target\.agent" src test docs CHANGELOG.md
rg "microsoft\.a365\.transfer\.target\.(name|type)" src test docs CHANGELOG.md docs\superpowers
rg "microsoft\.a365\.transfer\.target\.agent\.(id|name|blueprint\.id|platform\.id|version)" src test docs CHANGELOG.md docs\superpowers
```

- [ ] Record RED/GREEN evidence and exact outputs in `.superpowers/sdd/agent-target-revision-report.md`.
- [ ] Commit all changes with the required `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer.
