# Execute Tool Transfer Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add typed execute-tool transfer details and emit provisional `microsoft.a365.transfer.*` attributes aligned with the semantics proposed in OpenTelemetry semantic-conventions-genai PR 447.

**Architecture:** `ToolCallDetails` owns an optional immutable `TransferDetails` value so the Activity and DTO emission paths consume one model. A shared internal conversion on `TransferDetails` maps typed enum values to the required snake-case attribute values; both emitters use the same constants and conversion without changing `ExecuteToolScope.Start`.

**Tech Stack:** C#, .NET 8/.NET 10, `System.Diagnostics.Activity`, MSTest, FluentAssertions.

## Global Constraints

- Change only `execute_tool` telemetry; do not modify invoke-agent contracts, spans, or attributes.
- Keep `gen_ai.agent.*` as the source agent executing the tool.
- Use only `microsoft.a365.transfer.mode`, `microsoft.a365.transfer.target.name`, and `microsoft.a365.transfer.target.type` for the new attributes.
- Do not emit the unmerged `gen_ai.transfer.*` names.
- Emit transfer attributes only from explicit `TransferDetails`; never infer them.
- Preserve existing `ToolCallDetails` constructor binaries, source compatibility, and six-value deconstruction.
- Do not change `ExecuteToolScope.Start`.

---

## File Structure

- Create `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/TransferDetails.cs`: immutable transfer contract, enums, value conversion, equality, and hashing.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolCallDetails.cs`: add the optional transfer property and binary-compatible constructor overloads.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs`: define the three provisional A365 attribute keys.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`: emit Activity tags.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs`: emit DTO/ETW attributes.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs`: verify contract behavior and compatibility.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs`: verify Activity attributes.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs`: verify DTO attributes.
- Modify `docs/agent365-getting-started.md`: document transfer usage and attribute names.
- Modify `CHANGELOG.md`: record the SDK capability.

### Task 1: Add the typed transfer contract to `ToolCallDetails`

**Files:**
- Create: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/TransferDetails.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolCallDetails.cs`
- Test: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs`

**Interfaces:**
- Produces: `TransferMode`, `TransferTargetType`, and `TransferDetails`.
- Produces: `ToolCallDetails.TransferDetails`.
- Preserves: all existing `ToolCallDetails` constructor signatures and `Deconstruct(out string, out string?, out string?, out string?, out string?, out Uri?)`.

- [ ] **Step 1: Write failing contract and compatibility tests**

Add these tests to `ToolCallDetailsTests.cs`:

```csharp
[TestMethod]
public void Constructor_WithTransferDetails_ExposesTypedValues()
{
    var transfer = new TransferDetails(
        TransferMode.ReturnToCaller,
        targetName: "weather-agent",
        targetType: TransferTargetType.Agent);

    var details = new ToolCallDetails(
        "handoff",
        transfer);

    details.TransferDetails.Should().BeSameAs(transfer);
    transfer.Mode.Should().Be(TransferMode.ReturnToCaller);
    transfer.TargetName.Should().Be("weather-agent");
    transfer.TargetType.Should().Be(TransferTargetType.Agent);
    transfer.ModeValue.Should().Be("return_to_caller");
    transfer.TargetTypeValue.Should().Be("agent");
}

[TestMethod]
public void Constructor_WithoutTransferDetails_RemainsCompatible()
{
    var details = new ToolCallDetails(
        "tool",
        "{}",
        "call-1",
        "description",
        "function",
        new Uri("https://example.com"));

    details.TransferDetails.Should().BeNull();

    var (name, arguments, callId, description, type, endpoint) = details;
    name.Should().Be("tool");
    arguments.Should().Be("{}");
    callId.Should().Be("call-1");
    description.Should().Be("description");
    type.Should().Be("function");
    endpoint.Should().Be(new Uri("https://example.com"));
}

[TestMethod]
public void Equals_WithEquivalentTransferDetails_IsTrueAndHasMatchingHashCode()
{
    var left = new ToolCallDetails(
        "handoff",
        new TransferDetails(
            TransferMode.PassControl,
            "support-agent",
            TransferTargetType.Agent));
    var right = new ToolCallDetails(
        "handoff",
        new TransferDetails(
            TransferMode.PassControl,
            "support-agent",
            TransferTargetType.Agent));

    left.Should().Be(right);
    left.GetHashCode().Should().Be(right.GetHashCode());
}

[TestMethod]
public void TransferDetails_MapsAllStandardValues()
{
    new TransferDetails(TransferMode.PassControl).ModeValue
        .Should().Be("pass_control");
    new TransferDetails(TransferMode.ReturnToCaller).ModeValue
        .Should().Be("return_to_caller");
    new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Agent)
        .TargetTypeValue.Should().Be("agent");
    new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Human)
        .TargetTypeValue.Should().Be("human");
    new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Workflow)
        .TargetTypeValue.Should().Be("workflow");
}
```

- [ ] **Step 2: Run the contract tests and verify failure**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ToolCallDetailsTests" --no-restore
```

Expected: compilation fails because `TransferDetails`, `TransferMode`, `TransferTargetType`, the new constructor overloads, and `ToolCallDetails.TransferDetails` do not exist.

- [ ] **Step 3: Create the immutable transfer contract**

Create `TransferDetails.cs`:

```csharp
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Describes an explicit transfer performed by an execute-tool operation.
    /// </summary>
    public sealed class TransferDetails : IEquatable<TransferDetails>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransferDetails"/> class.
        /// </summary>
        /// <param name="mode">How control passes to the target.</param>
        /// <param name="targetName">Optional human-readable target name.</param>
        /// <param name="targetType">Optional target classification.</param>
        public TransferDetails(
            TransferMode mode,
            string? targetName = null,
            TransferTargetType? targetType = null)
        {
            Mode = mode;
            TargetName = targetName;
            TargetType = targetType;
        }

        /// <summary>
        /// Gets how control passes to the target.
        /// </summary>
        public TransferMode Mode { get; }

        /// <summary>
        /// Gets the optional human-readable target name.
        /// </summary>
        public string? TargetName { get; }

        /// <summary>
        /// Gets the optional target classification.
        /// </summary>
        public TransferTargetType? TargetType { get; }

        internal string ModeValue => Mode switch
        {
            TransferMode.ReturnToCaller => "return_to_caller",
            TransferMode.PassControl => "pass_control",
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };

        internal string? TargetTypeValue => TargetType switch
        {
            TransferTargetType.Agent => "agent",
            TransferTargetType.Human => "human",
            TransferTargetType.Workflow => "workflow",
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(TargetType)),
        };

        /// <inheritdoc/>
        public bool Equals(TransferDetails? other) =>
            other is not null &&
            Mode == other.Mode &&
            string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
            TargetType == other.TargetType;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as TransferDetails);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + Mode.GetHashCode();
                hash = (hash * 31) + (TargetName == null ? 0 : StringComparer.Ordinal.GetHashCode(TargetName));
                hash = (hash * 31) + TargetType.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Describes how control passes during a transfer.
    /// </summary>
    public enum TransferMode
    {
        /// <summary>
        /// The source agent waits for the target and resumes afterward.
        /// </summary>
        ReturnToCaller,

        /// <summary>
        /// The target assumes control of the remaining work.
        /// </summary>
        PassControl,
    }

    /// <summary>
    /// Describes the type of transfer target.
    /// </summary>
    public enum TransferTargetType
    {
        /// <summary>
        /// Another agent.
        /// </summary>
        Agent,

        /// <summary>
        /// A human participant.
        /// </summary>
        Human,

        /// <summary>
        /// A workflow.
        /// </summary>
        Workflow,
    }
}
```

- [ ] **Step 4: Add binary-compatible `ToolCallDetails` overloads**

Keep each current constructor unchanged. Add one corresponding overload per
argument representation with a required `TransferDetails transferDetails`
second parameter. This keeps existing constructor signatures intact and avoids
adding an optional parameter to a compiled public signature:

```csharp
public ToolCallDetails(
    string toolName,
    TransferDetails transferDetails,
    string? arguments = null,
    string? toolCallId = null,
    string? description = null,
    string? toolType = null,
    Uri? endpoint = null)
{
    ToolName = toolName;
    Arguments = arguments;
    ToolCallId = toolCallId;
    Description = description;
    ToolType = toolType;
    Endpoint = endpoint;
    TransferDetails = transferDetails ?? throw new ArgumentNullException(nameof(transferDetails));
}
```

Add these overloads for structured arguments:

```csharp
public ToolCallDetails(
    string toolName,
    TransferDetails transferDetails,
    IDictionary<string, object> argumentsObject,
    string? toolCallId = null,
    string? description = null,
    string? toolType = null,
    Uri? endpoint = null)
{
    ToolName = toolName;
    TransferDetails = transferDetails ?? throw new ArgumentNullException(nameof(transferDetails));
    ArgumentsObject = argumentsObject ?? throw new ArgumentNullException(nameof(argumentsObject));
    ToolCallId = toolCallId;
    Description = description;
    ToolType = toolType;
    Endpoint = endpoint;
}

public ToolCallDetails(
    string toolName,
    TransferDetails transferDetails,
    ExecuteToolCallArguments toolCallArguments,
    string? toolCallId = null,
    string? description = null,
    string? toolType = null,
    Uri? endpoint = null)
{
    ToolName = toolName;
    TransferDetails = transferDetails ?? throw new ArgumentNullException(nameof(transferDetails));
    ToolCallArguments = toolCallArguments ?? throw new ArgumentNullException(nameof(toolCallArguments));
    ToolCallId = toolCallId;
    Description = description;
    ToolType = toolType;
    Endpoint = endpoint;
}
```

Add XML documentation matching the existing constructors and describing
`transferDetails` as explicit transfer metadata. Add:

```csharp
public TransferDetails? TransferDetails { get; }
```

Include `TransferDetails` in `Equals` and `GetHashCode`:

```csharp
EqualityComparer<TransferDetails?>.Default.Equals(
    TransferDetails,
    other.TransferDetails)
```

```csharp
hash = (hash * 31) + (TransferDetails?.GetHashCode() ?? 0);
```

Do not change the existing six-output `Deconstruct` method.

- [ ] **Step 5: Run the contract tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ToolCallDetailsTests" --no-restore
```

Expected: all `ToolCallDetailsTests` pass.

- [ ] **Step 6: Commit the contract**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Contracts\TransferDetails.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Contracts\ToolCallDetails.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Contracts\ToolCallDetailsTests.cs
git commit -m "feat: add execute tool transfer details" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 17199429-dbdf-4cca-85ba-9ae35ec1a845"
```

### Task 2: Emit transfer attributes through both execute-tool paths

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs`
- Test: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs`
- Test: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs`

**Interfaces:**
- Consumes: `ToolCallDetails.TransferDetails`, `TransferDetails.ModeValue`, and `TransferDetails.TargetTypeValue`.
- Produces: identical transfer attributes from Activity and DTO emission.

- [ ] **Step 1: Write failing Activity emission tests**

Add to `ExecuteToolScopeTest.cs`:

```csharp
[TestMethod]
public void Start_WithTransferDetails_SetsExplicitTransferAttributes()
{
    var activity = ListenForActivity(() =>
    {
        using var scope = ExecuteToolScope.Start(
            Util.GetDefaultRequest(),
            new ToolCallDetails(
                "handoff",
                new TransferDetails(
                    TransferMode.ReturnToCaller,
                    "weather-agent",
                    TransferTargetType.Agent)),
            Util.GetAgentDetails());
    });

    activity.ShouldHaveTag(OpenTelemetryConstants.TransferModeKey, "return_to_caller");
    activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetNameKey, "weather-agent");
    activity.ShouldHaveTag(OpenTelemetryConstants.TransferTargetTypeKey, "agent");
}

[TestMethod]
public void Start_WithoutTransferDetails_OmitsTransferAttributes()
{
    var activity = ListenForActivity(() =>
    {
        using var scope = ExecuteToolScope.Start(
            Util.GetDefaultRequest(),
            new ToolCallDetails("ordinary-tool", (string?)null),
            Util.GetAgentDetails());
    });

    activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferModeKey);
    activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetNameKey);
    activity.Tags.Should().NotContainKey(OpenTelemetryConstants.TransferTargetTypeKey);
}
```

- [ ] **Step 2: Write failing DTO emission tests**

Add to `ExecuteToolDataBuilderTests.cs`:

```csharp
[TestMethod]
public void Build_WithTransferDetails_IncludesExplicitTransferAttributes()
{
    var tool = new ToolCallDetails(
        "handoff",
        new TransferDetails(
            TransferMode.PassControl,
            "support-workflow",
            TransferTargetType.Workflow));

    var data = ExecuteToolDataBuilder.Build(
        tool,
        new AgentDetails("source-agent"),
        "conversation-1");

    data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("pass_control");
    data.Attributes[OpenTelemetryConstants.TransferTargetNameKey].Should().Be("support-workflow");
    data.Attributes[OpenTelemetryConstants.TransferTargetTypeKey].Should().Be("workflow");
}

[TestMethod]
public void Build_WithModeOnly_OmitsOptionalTargetAttributes()
{
    var tool = new ToolCallDetails(
        "handoff",
        new TransferDetails(TransferMode.ReturnToCaller));

    var data = ExecuteToolDataBuilder.Build(
        tool,
        new AgentDetails("source-agent"),
        "conversation-1");

    data.Attributes[OpenTelemetryConstants.TransferModeKey].Should().Be("return_to_caller");
    data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetNameKey);
    data.Attributes.Should().NotContainKey(OpenTelemetryConstants.TransferTargetTypeKey);
}
```

- [ ] **Step 3: Run the emission tests and verify failure**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ExecuteToolDataBuilderTests" --no-restore
```

Expected: compilation fails because the transfer constants and emission logic do not exist.

- [ ] **Step 4: Add the provisional attribute constants**

Add to the tool-call key region in `OpenTelemetryConstants.cs`:

```csharp
public const string TransferModeKey = "microsoft.a365.transfer.mode";
public const string TransferTargetNameKey = "microsoft.a365.transfer.target.name";
public const string TransferTargetTypeKey = "microsoft.a365.transfer.target.type";
```

- [ ] **Step 5: Emit the Activity tags**

In `ExecuteToolScope` after the existing tool attributes:

```csharp
var transferDetails = details.TransferDetails;
if (transferDetails != null)
{
    SetTagMaybe(OpenTelemetryConstants.TransferModeKey, transferDetails.ModeValue);
    SetTagMaybe(OpenTelemetryConstants.TransferTargetNameKey, transferDetails.TargetName);
    SetTagMaybe(OpenTelemetryConstants.TransferTargetTypeKey, transferDetails.TargetTypeValue);
}
```

Do not derive transfer details from `ToolName`, `ToolType`, endpoint, parent span, or `agentDetails`.

- [ ] **Step 6: Emit the DTO attributes**

At the end of `ExecuteToolDataBuilder.AddToolDetails`:

```csharp
var transferDetails = toolCallDetails.TransferDetails;
if (transferDetails != null)
{
    AddIfNotNull(attributes, OpenTelemetryConstants.TransferModeKey, transferDetails.ModeValue);
    AddIfNotNull(attributes, OpenTelemetryConstants.TransferTargetNameKey, transferDetails.TargetName);
    AddIfNotNull(attributes, OpenTelemetryConstants.TransferTargetTypeKey, transferDetails.TargetTypeValue);
}
```

- [ ] **Step 7: Run the focused emission tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ExecuteToolDataBuilderTests" --no-restore
```

Expected: all selected tests pass and no assertion references `gen_ai.transfer.*`.

- [ ] **Step 8: Commit the emission paths**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\OpenTelemetryConstants.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\ExecuteToolScope.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\DTOs\Builders\ExecuteToolDataBuilder.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Scopes\ExecuteToolScopeTest.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\DTOs\Builders\ExecuteToolDataBuilderTests.cs
git commit -m "feat: emit execute tool transfer attributes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 17199429-dbdf-4cca-85ba-9ae35ec1a845"
```

### Task 3: Document, validate, and finalize the feature

**Files:**
- Modify: `docs/agent365-getting-started.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: the Task 1 public contract and Task 2 attribute names.
- Produces: user-facing usage guidance and release documentation.

- [ ] **Step 1: Update the execute-tool example**

Extend the execute-tool example in `docs/agent365-getting-started.md` with:

```csharp
var toolCallDetails = new ToolCallDetails(
    toolName: "handoff",
    transferDetails: new TransferDetails(
        mode: TransferMode.ReturnToCaller,
        targetName: "weather-agent",
        targetType: TransferTargetType.Agent));

using var scope = ExecuteToolScope.Start(
    request: request,
    details: toolCallDetails,
    agentDetails: sourceAgentDetails);
```

Immediately explain:

```markdown
`agentDetails` identifies the source agent executing the tool. `TransferDetails`
describes an explicitly exposed transfer and target. The SDK does not infer
transfer semantics for ordinary tool calls.
```

Add the following optional attributes to the `ExecuteToolScope` attribute table:

```json
"microsoft.a365.transfer.mode": "Optional",
"microsoft.a365.transfer.target.name": "Optional",
"microsoft.a365.transfer.target.type": "Optional"
```

- [ ] **Step 2: Add the changelog entry**

Under `## Unreleased` in `CHANGELOG.md`, add:

```markdown
- Add typed execute-tool transfer details and emit provisional `microsoft.a365.transfer.*` attributes for explicitly reported agent, human, and workflow transfers, aligned with the semantics proposed in OpenTelemetry semantic-conventions-genai PR 447.
```

- [ ] **Step 3: Verify no unapproved attribute names were introduced**

Run:

```powershell
rg "gen_ai\.transfer" src test docs\agent365-getting-started.md CHANGELOG.md
```

Expected: no matches.

Run:

```powershell
rg "microsoft\.a365\.transfer\.(mode|target\.name|target\.type)" src test docs\agent365-getting-started.md CHANGELOG.md
```

Expected: matches in constants, tests, documentation, and changelog.

- [ ] **Step 4: Run the complete Agent365 test project**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --no-restore
```

Expected: all tests pass with zero failures.

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net10.0 --no-restore
```

Expected: all tests pass with zero failures.

- [ ] **Step 5: Validate formatting and the final diff**

Run:

```powershell
dotnet format Microsoft.OpenTelemetry.slnx --verify-no-changes --no-restore
git --no-pager diff --check
git status --short
```

Expected: formatting and whitespace checks pass; only intended files are modified.

- [ ] **Step 6: Commit documentation and validation**

```powershell
git add docs\agent365-getting-started.md CHANGELOG.md
git commit -m "docs: describe execute tool transfers" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 17199429-dbdf-4cca-85ba-9ae35ec1a845"
```
