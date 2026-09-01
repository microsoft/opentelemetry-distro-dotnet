# A365 Instrumentation Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a framework-neutral in-process harness that captures A365 GenAI spans during an integration scenario, validates SDK-owned certification requirements, and produces actionable diagnostics with documented suppressions.

**Architecture:** Public immutable validation models and options live under the A365 runtime validation namespace. A temporary process-wide `ActivityListener` captures eligible spans, an internal certification catalog evaluates immutable snapshots, and the report exposes both typed findings and `EnsureValid()` for test-runner-visible failures.

**Tech Stack:** C#, .NET Standard 2.0, .NET 8, `System.Diagnostics.ActivityListener`, OpenTelemetry .NET, MSTest, FluentAssertions, Microsoft Public API Analyzers.

## Global Constraints

- Execute this plan in a separate git worktree created with the `using-git-worktrees` skill.
- Ship the feature in the existing `Microsoft.OpenTelemetry` package; add no package dependency.
- Support `netstandard2.0` and `net8.0`.
- Do not depend on xUnit, MSTest, NUnit, FluentAssertions, DI, an exporter, or a local collector in production code.
- Do not change tracing, exporter, sampling, or instrumentation behavior unless `EvaluateAsync` is explicitly invoked.
- Capture manual scopes, supported auto-instrumentation, and custom `ActivitySource` spans with recognized A365 `gen_ai.operation.name` values.
- Serialize validation sessions process-wide and always detach the listener.
- Default `SpanCompletionTimeout` to 10 seconds and use an internal 250-millisecond post-action quiet period.
- Keep tenant and agent export identity rules non-suppressible.
- Require a nonblank reason for every suppression and retain suppressed findings in the report.
- Add every new public API to `.publicApi\PublicAPI.Unshipped.txt`.
- Follow TDD: write the focused failing test, confirm failure, implement the smallest behavior, then rerun the focused test.

---

## File Structure

Create production files under:

`src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\`

- `A365ValidationProfile.cs` — built-in profile enum.
- `A365ValidationSeverity.cs` — error and warning severity.
- `A365ValidationFindingStatus.cs` — active and suppressed status.
- `A365ValidationRuleIds.cs` — stable public certification rule IDs.
- `A365SpanSnapshot.cs` — immutable captured span identity and attributes.
- `A365ValidationFinding.cs` — immutable rule finding.
- `A365SpanValidationResult.cs` — one span and its findings.
- `A365ValidationReport.cs` — aggregate counts, validity, formatting entry point, and `EnsureValid()`.
- `A365ValidationException.cs` — exception carrying the typed report.
- `A365ValidationOptions.cs` — timeout, filtering, and suppression registration.
- `A365ValidationSuppression.cs` — internal validated suppression representation.
- `A365ValidationRule.cs` — internal rule definition.
- `A365CertificationRuleCatalog.cs` — centralized certification rule matrix.
- `A365ValidationEngine.cs` — rule evaluation and suppression matching.
- `A365ValidationReportFormatter.cs` — deterministic actionable text output.
- `A365ActivityCaptureSession.cs` — temporary listener, quiet-period tracking, and snapshots.
- `A365InstrumentationValidator.cs` — public orchestration API and process-wide lock.

Create tests under:

`test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\`

- `A365ValidationOptionsTests.cs`
- `A365ValidationEngineTests.cs`
- `A365ValidationReportTests.cs`
- `A365ActivityCaptureSessionTests.cs`
- `A365InstrumentationValidatorTests.cs`

Modify:

- `src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt`
- `docs\agent365-getting-started.md`

---

### Task 1: Public Validation Models and Options

**Files:**
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationProfile.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationSeverity.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationFindingStatus.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationRuleIds.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365SpanSnapshot.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationFinding.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365SpanValidationResult.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationOptions.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationSuppression.cs`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationOptionsTests.cs`

**Interfaces:**
- Consumes: `System.Diagnostics.Activity`, `OpenTelemetryConstants` attribute names.
- Produces: `A365ValidationOptions`, `A365SpanSnapshot`, finding/result models, and stable `A365ValidationRuleIds` used by all later tasks.

- [ ] **Step 1: Write failing options and snapshot tests**

Create `A365ValidationOptionsTests.cs` with focused tests:

```csharp
namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Validation;

[TestClass]
public sealed class A365ValidationOptionsTests
{
    [TestMethod]
    public void Defaults_AreCertificationAndTenSeconds()
    {
        var options = new A365ValidationOptions();

        options.Profile.Should().Be(A365ValidationProfile.Certification);
        options.SpanCompletionTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.SpanFilter.Should().BeNull();
    }

    [TestMethod]
    public void Suppress_RequiresReason()
    {
        var options = new A365ValidationOptions();

        Action act = () => options.Suppress(
            A365ValidationRuleIds.AgentNameRequired,
            reason: " ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    [TestMethod]
    public void OperationSuppression_RequiresOperationName()
    {
        var options = new A365ValidationOptions();

        Action act = () => options.Suppress(
            A365ValidationRuleIds.InvokeUserIdRequired,
            operationName: "",
            reason: "Anonymous invocation");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("operationName");
    }

    [TestMethod]
    public void SpanSnapshot_CopiesAttributes()
    {
        var source = new Dictionary<string, object?>
        {
            ["gen_ai.operation.name"] = "chat",
        };

        var snapshot = new A365SpanSnapshot(
            "trace",
            "span",
            "chat model",
            "Custom.Source",
            "chat",
            source);

        source["gen_ai.operation.name"] = "changed";

        snapshot.Attributes["gen_ai.operation.name"].Should().Be("chat");
    }
}
```

- [ ] **Step 2: Run the focused tests and confirm they fail**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~A365ValidationOptionsTests" --no-restore
```

Expected: FAIL because the validation namespace and public types do not exist.

- [ ] **Step 3: Add enums and stable rule IDs**

Use namespace `Microsoft.Agents.A365.Observability.Runtime.Validation`.

```csharp
public enum A365ValidationProfile
{
    Certification = 0,
}

public enum A365ValidationSeverity
{
    Warning = 0,
    Error = 1,
}

public enum A365ValidationFindingStatus
{
    Active = 0,
    Suppressed = 1,
}
```

Define these constants in `A365ValidationRuleIds`:

```csharp
public const string TenantIdRequired = "A365-COMMON-001";
public const string AgentIdentityRequired = "A365-COMMON-002";
public const string AgentNameRequired = "A365-COMMON-003";
public const string AgentDescriptionRequired = "A365-COMMON-004";
public const string AgentUserIdRequired = "A365-COMMON-005";
public const string AgentUserEmailRequired = "A365-COMMON-006";
public const string AgentBlueprintIdRequired = "A365-COMMON-007";
public const string InvokeUserIdRequired = "A365-INVOKE-001";
public const string InvokeUserNameRequired = "A365-INVOKE-002";
public const string InvokeUserEmailRequired = "A365-INVOKE-003";
public const string InferenceModelRequired = "A365-INFERENCE-001";
public const string InferenceProviderRequired = "A365-INFERENCE-002";
public const string ToolNameRequired = "A365-TOOL-001";
public const string GuardrailDecisionRequired = "A365-GUARDRAIL-001";
public const string GuardrailTargetRequired = "A365-GUARDRAIL-002";
public const string NoSpansCaptured = "A365-SESSION-001";
public const string SpanCompletionTimeout = "A365-SESSION-002";
public const string UnusedSuppression = "A365-SESSION-003";
```

Add XML documentation to every public type and member.

- [ ] **Step 4: Add immutable snapshots and finding/result models**

`A365SpanSnapshot` must copy attributes into a new
`ReadOnlyDictionary<string, object?>`:

```csharp
public sealed class A365SpanSnapshot
{
    internal A365SpanSnapshot(
        string traceId,
        string spanId,
        string displayName,
        string sourceName,
        string operationName,
        IDictionary<string, object?> attributes)
    {
        TraceId = traceId;
        SpanId = spanId;
        DisplayName = displayName;
        SourceName = sourceName;
        OperationName = operationName;
        Attributes = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(attributes, StringComparer.Ordinal));
    }

    public string TraceId { get; }
    public string SpanId { get; }
    public string DisplayName { get; }
    public string SourceName { get; }
    public string OperationName { get; }
    public IReadOnlyDictionary<string, object?> Attributes { get; }
}
```

`A365ValidationFinding` must use this internal constructor and expose matching
public getters:

```csharp
internal A365ValidationFinding(
    string ruleId,
    A365ValidationSeverity severity,
    A365ValidationFindingStatus status,
    string? operationName,
    string? attributeName,
    string message,
    string remediation,
    string? suppressionReason,
    string? traceId,
    string? spanId)
```

`A365SpanValidationResult` must have an internal constructor, public `Span` and
`Findings` getters, and copy findings into `ReadOnlyCollection<A365ValidationFinding>`.

- [ ] **Step 5: Add validated suppression registration**

`A365ValidationOptions`:

```csharp
public sealed class A365ValidationOptions
{
    private readonly List<A365ValidationSuppression> suppressions = new();

    public A365ValidationProfile Profile { get; set; } =
        A365ValidationProfile.Certification;

    public TimeSpan SpanCompletionTimeout { get; set; } =
        TimeSpan.FromSeconds(10);

    public Func<A365SpanSnapshot, bool>? SpanFilter { get; set; }

    internal IReadOnlyList<A365ValidationSuppression> Suppressions =>
        suppressions;

    public void Suppress(string ruleId, string reason)
    {
        suppressions.Add(A365ValidationSuppression.Create(
            ruleId, null, null, reason));
    }

    public void Suppress(
        string ruleId,
        string operationName,
        string reason)
    {
        suppressions.Add(A365ValidationSuppression.Create(
            ruleId, RequireOperationName(operationName), null, reason));
    }

    public void Suppress(
        string ruleId,
        string operationName,
        Func<A365SpanSnapshot, bool> predicate,
        string reason)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        suppressions.Add(A365ValidationSuppression.Create(
            ruleId, RequireOperationName(operationName), predicate, reason));
    }

    private static string RequireOperationName(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException(
                "Operation name must not be empty.",
                nameof(operationName));
        }

        return operationName;
    }
}
```

`A365ValidationSuppression.Create` validates nonblank `ruleId` and `reason`,
stores optional operation/predicate, and exposes an internal `bool WasUsed`
setter for the engine.

- [ ] **Step 6: Rerun focused tests**

Run the Step 2 command.

Expected: PASS for all `A365ValidationOptionsTests`.

- [ ] **Step 7: Commit the public model foundation**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationOptionsTests.cs
git commit -m "Add A365 validation models and options" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 2: Certification Rule Catalog and Evaluation

**Files:**
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationRule.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365CertificationRuleCatalog.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationEngine.cs`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationEngineTests.cs`

**Interfaces:**
- Consumes: Task 1 snapshots, suppressions, findings, rule IDs, and `OpenTelemetryConstants`.
- Produces: `A365ValidationEngine.Validate(...)`, used by the public orchestrator in Task 4.

- [ ] **Step 1: Write failing catalog and evaluation tests**

Create helpers that build snapshots from attribute dictionaries, then add:

```csharp
[TestMethod]
public void Validate_ValidChatSpan_HasNoFindings()
{
    var span = CreateSpan("chat", new Dictionary<string, object?>
    {
        [TenantIdKey] = "tenant",
        [GenAiAgentIdKey] = "agent",
        [GenAiAgentNameKey] = "Weather agent",
        [GenAiAgentDescriptionKey] = "Answers weather questions",
        [AgentAUIDKey] = "agent-user",
        [AgentEmailKey] = "agent@example.com",
        [AgentBlueprintIdKey] = "blueprint",
        [GenAiRequestModelKey] = "gpt-4.1",
        [GenAiProviderNameKey] = "openai",
    });

    var results = A365ValidationEngine.Validate(
        new[] { span },
        new A365ValidationOptions());

    results.Single().Findings.Should().BeEmpty();
}

[TestMethod]
public void Validate_MissingExporterIdentity_IsNotSuppressible()
{
    var options = new A365ValidationOptions();
    options.Suppress(
        A365ValidationRuleIds.TenantIdRequired,
        "Local tenant is unavailable");

    Action act = () => A365ValidationEngine.Validate(
        new[] { CreateSpan("chat") },
        options);

    act.Should().Throw<ArgumentException>()
        .WithMessage("*non-suppressible*");
}

[TestMethod]
public void Validate_OperationSuppression_LeavesVisibleSuppressedFinding()
{
    var options = new A365ValidationOptions();
    options.Suppress(
        A365ValidationRuleIds.InvokeUserIdRequired,
        "invoke_agent",
        "Anonymous entry point");

    var result = A365ValidationEngine.Validate(
        new[] { CreateValidCommonSpan("invoke_agent") },
        options).Single();

    result.Findings.Should().ContainSingle(f =>
        f.RuleId == A365ValidationRuleIds.InvokeUserIdRequired &&
        f.Status == A365ValidationFindingStatus.Suppressed &&
        f.SuppressionReason == "Anonymous entry point");
}

[TestMethod]
public void Validate_PredicateSuppression_AppliesOnlyToMatchingSpan()
{
    var options = new A365ValidationOptions();
    options.Suppress(
        A365ValidationRuleIds.ToolNameRequired,
        "execute_tool",
        span => span.DisplayName.Contains("optional", StringComparison.Ordinal),
        "Synthetic optional tool span");

    var results = A365ValidationEngine.Validate(
        new[]
        {
            CreateValidCommonSpan("execute_tool", "execute_tool optional"),
            CreateValidCommonSpan("execute_tool", "execute_tool required"),
        },
        options);

    results[0].Findings.Single().Status.Should()
        .Be(A365ValidationFindingStatus.Suppressed);
    results[1].Findings.Single().Status.Should()
        .Be(A365ValidationFindingStatus.Active);
}
```

Also test whitespace values, agent platform ID as a valid alternative to agent
ID, inference-only rules, tool-only rules, guardrail-only rules, caller rules
only on `invoke_agent`, unknown rule IDs, and predicate exceptions.

Use these test helpers:

```csharp
private static A365SpanSnapshot CreateSpan(
    string operationName,
    IDictionary<string, object?>? attributes = null,
    string displayName = "test span")
{
    var values = attributes == null
        ? new Dictionary<string, object?>()
        : new Dictionary<string, object?>(attributes);
    values[GenAiOperationNameKey] = operationName;

    return new A365SpanSnapshot(
        "00000000000000000000000000000001",
        "0000000000000001",
        displayName,
        "Test.Source",
        operationName,
        values);
}

private static A365SpanSnapshot CreateValidCommonSpan(
    string operationName,
    string displayName = "test span")
{
    return CreateSpan(operationName, new Dictionary<string, object?>
    {
        [TenantIdKey] = "tenant",
        [GenAiAgentIdKey] = "agent",
        [GenAiAgentNameKey] = "Weather agent",
        [GenAiAgentDescriptionKey] = "Answers weather questions",
        [AgentAUIDKey] = "agent-user",
        [AgentEmailKey] = "agent@example.com",
        [AgentBlueprintIdKey] = "blueprint",
    }, displayName);
}
```

- [ ] **Step 2: Run engine tests and confirm failure**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~A365ValidationEngineTests" --no-restore
```

Expected: FAIL because the catalog and engine do not exist.

- [ ] **Step 3: Implement the internal rule type**

```csharp
internal sealed class A365ValidationRule
{
    internal A365ValidationRule(
        string id,
        string? operationName,
        string? attributeName,
        bool suppressible,
        Func<A365SpanSnapshot, string?> validate,
        string remediation)
    {
        Id = id;
        OperationName = operationName;
        AttributeName = attributeName;
        Suppressible = suppressible;
        Validate = validate;
        Remediation = remediation;
    }

    internal string Id { get; }
    internal string? OperationName { get; }
    internal string? AttributeName { get; }
    internal bool Suppressible { get; }
    internal Func<A365SpanSnapshot, string?> Validate { get; }
    internal string Remediation { get; }
}
```

All certification findings are errors. Unused suppressions are report-level
warnings added later.

- [ ] **Step 4: Implement the centralized certification matrix**

Add helpers that distinguish missing and invalid values:

```csharp
private static string? ValidateRequiredString(
    A365SpanSnapshot span,
    string key)
{
    if (!span.Attributes.TryGetValue(key, out var value) ||
        value == null ||
        value is string empty && string.IsNullOrWhiteSpace(empty))
    {
        return $"Missing {key}";
    }

    if (value is not string)
    {
        return $"Invalid {key}: expected a non-empty string but found " +
            value.GetType().Name;
    }

    return null;
}

private static string? ValidateAgentIdentity(A365SpanSnapshot span)
{
    if (ValidateRequiredString(
            span,
            OpenTelemetryConstants.GenAiAgentIdKey) == null ||
        ValidateRequiredString(
            span,
            OpenTelemetryConstants.AgentPlatformIdKey) == null)
    {
        return null;
    }

    return "Missing agent identity: set gen_ai.agent.id or " +
        "microsoft.a365.agent.platform.id";
}
```

For `GuardrailDecisionRequired`, additionally reject string values outside
`allow`, `audit`, `deny`, `modify`, and `warn` using an ordinal-ignore-case
set. Its diagnostic must say
`Invalid microsoft.security.decision.type: expected allow, audit, deny, modify, or warn`.

Populate `A365CertificationRuleCatalog.Rules` with:

| Rule ID | Operation | Attribute/condition | Suppressible |
|---|---|---|---|
| `TenantIdRequired` | all | `microsoft.tenant.id` | No |
| `AgentIdentityRequired` | all | `gen_ai.agent.id` OR `microsoft.a365.agent.platform.id` | No |
| `AgentNameRequired` | all | `gen_ai.agent.name` | Yes |
| `AgentDescriptionRequired` | all | `gen_ai.agent.description` | Yes |
| `AgentUserIdRequired` | all | `microsoft.agent.user.id` | Yes |
| `AgentUserEmailRequired` | all | `microsoft.agent.user.email` | Yes |
| `AgentBlueprintIdRequired` | all | `microsoft.a365.agent.blueprint.id` | Yes |
| `InvokeUserIdRequired` | `invoke_agent` | `user.id` | Yes |
| `InvokeUserNameRequired` | `invoke_agent` | `user.name` | Yes |
| `InvokeUserEmailRequired` | `invoke_agent` | `user.email` | Yes |
| `InferenceModelRequired` | `chat` | `gen_ai.request.model` | Yes |
| `InferenceProviderRequired` | `chat` | `gen_ai.provider.name` | Yes |
| `ToolNameRequired` | `execute_tool` | `gen_ai.tool.name` | Yes |
| `GuardrailDecisionRequired` | `apply_guardrail` | `microsoft.security.decision.type` | Yes |
| `GuardrailTargetRequired` | `apply_guardrail` | `microsoft.security.target.type` | Yes |

Use remediation that names the SDK input responsible for the attribute, for
example:

```csharp
"Set AgentDetails.AgentName or provide gen_ai.agent.name through A365 baggage."
"Set CallerDetails.UserDetails.UserId when starting InvokeAgentScope."
"Set InferenceCallDetails.Model for chat/inference spans."
"Set ToolCallDetails.ToolName for execute_tool spans."
"Set GuardrailDetails.DecisionType when starting ApplyGuardrailScope."
```

Match operation names with `StringComparison.OrdinalIgnoreCase`.

- [ ] **Step 5: Implement validation and suppression precedence**

`A365ValidationEngine.Validate`:

```csharp
internal static IReadOnlyList<A365SpanValidationResult> Validate(
    IReadOnlyList<A365SpanSnapshot> spans,
    A365ValidationOptions options)
{
    ValidateOptions(options);
    var results = new List<A365SpanValidationResult>(spans.Count);

    foreach (var span in spans)
    {
        var findings = new List<A365ValidationFinding>();
        foreach (var rule in A365CertificationRuleCatalog.Rules)
        {
            if (!AppliesToOperation(rule, span))
            {
                continue;
            }

            var message = rule.Validate(span);
            if (message == null)
            {
                continue;
            }

            var suppression = FindSuppression(rule, span, options.Suppressions);
            findings.Add(CreateFinding(rule, span, message, suppression));
            if (suppression != null)
            {
                suppression.WasUsed = true;
            }
        }

        results.Add(new A365SpanValidationResult(span, findings));
    }

    return results.AsReadOnly();
}
```

`ValidateOptions` rejects unsupported profiles, non-positive timeouts, unknown
rule IDs, and suppressions of non-suppressible rules before customer code runs.

`FindSuppression` checks predicate-targeted, operation-targeted, then global
suppressions. Wrap predicate exceptions in `InvalidOperationException` with the
rule ID and span ID; do not treat them as a match.

- [ ] **Step 6: Rerun engine tests**

Run the Step 2 command.

Expected: PASS for all engine tests.

- [ ] **Step 7: Commit the certification engine**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationEngineTests.cs
git commit -m "Add A365 certification validation rules" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 3: Reports, Formatting, and Default Validity Check

**Files:**
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationReport.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationException.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ValidationReportFormatter.cs`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationReportTests.cs`

**Interfaces:**
- Consumes: Task 1 findings/results and Task 2 suppression usage.
- Produces: immutable `A365ValidationReport`, deterministic `ToString()`, and `EnsureValid()`.

- [ ] **Step 1: Write failing report tests**

```csharp
[TestMethod]
public void EnsureValid_InvalidReport_ThrowsActionableException()
{
    var report = CreateReport(
        CreateActiveFinding(
            A365ValidationRuleIds.ToolNameRequired,
            "execute_tool",
            "gen_ai.tool.name",
            "Missing gen_ai.tool.name",
            "Set ToolCallDetails.ToolName."));

    Action act = report.EnsureValid;

    var exception = act.Should().Throw<A365ValidationException>().Which;
    exception.Report.Should().BeSameAs(report);
    exception.Message.Should().Contain("A365 instrumentation validation failed");
    exception.Message.Should().Contain("[A365-TOOL-001]");
    exception.Message.Should().Contain("execute_tool");
    exception.Message.Should().Contain("Fix: Set ToolCallDetails.ToolName.");
}

[TestMethod]
public void SuppressedFinding_DoesNotInvalidateReport()
{
    var report = CreateReport(CreateSuppressedFinding());

    report.IsValid.Should().BeTrue();
    report.SuppressedFindingCount.Should().Be(1);
    report.Invoking(r => r.EnsureValid()).Should().NotThrow();
    report.ToString().Should().Contain("SUPPRESSED:");
}

[TestMethod]
public void ActiveWarning_DoesNotInvalidateReport()
{
    var report = CreateReport(CreateWarningFinding());

    report.IsValid.Should().BeTrue();
    report.WarningCount.Should().Be(1);
}
```

Add tests for deterministic span grouping, singular/plural counts, session-level
findings, and a valid report summary.

Construct test findings through the internal constructors exposed to the test
assembly by the existing `InternalsVisibleTo` declaration:

```csharp
private static A365ValidationReport CreateReport(
    params A365ValidationFinding[] findings)
{
    var span = new A365SpanSnapshot(
        "00000000000000000000000000000001",
        "0000000000000001",
        "execute_tool weather",
        "Test.Source",
        "execute_tool",
        new Dictionary<string, object?>());

    return new A365ValidationReport(
        new[] { new A365SpanValidationResult(span, findings) },
        Array.Empty<A365ValidationFinding>());
}

private static A365ValidationFinding CreateActiveFinding(
    string ruleId,
    string operationName,
    string attributeName,
    string message,
    string remediation)
{
    return new A365ValidationFinding(
        ruleId,
        A365ValidationSeverity.Error,
        A365ValidationFindingStatus.Active,
        operationName,
        attributeName,
        message,
        remediation,
        null,
        "00000000000000000000000000000001",
        "0000000000000001");
}

private static A365ValidationFinding CreateSuppressedFinding()
{
    return new A365ValidationFinding(
        A365ValidationRuleIds.InvokeUserIdRequired,
        A365ValidationSeverity.Error,
        A365ValidationFindingStatus.Suppressed,
        "invoke_agent",
        "user.id",
        "Missing user.id",
        "Set CallerDetails.UserDetails.UserId.",
        "Anonymous entry point",
        "00000000000000000000000000000001",
        "0000000000000001");
}

private static A365ValidationFinding CreateWarningFinding()
{
    return new A365ValidationFinding(
        A365ValidationRuleIds.UnusedSuppression,
        A365ValidationSeverity.Warning,
        A365ValidationFindingStatus.Active,
        null,
        null,
        "Suppression A365-INVOKE-001 did not match any finding.",
        "Remove the stale suppression or correct its targeting.",
        null,
        null,
        null);
}
```

- [ ] **Step 2: Run report tests and confirm failure**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~A365ValidationReportTests" --no-restore
```

Expected: FAIL because report, formatter, and exception do not exist.

- [ ] **Step 3: Implement report counts and validity**

```csharp
public sealed class A365ValidationReport
{
    private readonly IReadOnlyList<A365SpanValidationResult> spans;
    private readonly IReadOnlyList<A365ValidationFinding> sessionFindings;

    internal A365ValidationReport(
        IEnumerable<A365SpanValidationResult> spans,
        IEnumerable<A365ValidationFinding> sessionFindings)
    {
        this.spans = new ReadOnlyCollection<A365SpanValidationResult>(
            spans.ToList());
        this.sessionFindings = new ReadOnlyCollection<A365ValidationFinding>(
            sessionFindings.ToList());
    }

    public IReadOnlyList<A365SpanValidationResult> Spans => spans;
    public IReadOnlyList<A365ValidationFinding> SessionFindings =>
        sessionFindings;

    public int ErrorCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Active &&
        f.Severity == A365ValidationSeverity.Error);

    public int WarningCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Active &&
        f.Severity == A365ValidationSeverity.Warning);

    public int SuppressedFindingCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Suppressed);

    public bool IsValid => ErrorCount == 0;

    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new A365ValidationException(this);
        }
    }

    public override string ToString() =>
        A365ValidationReportFormatter.Format(this);

    private IEnumerable<A365ValidationFinding> AllFindings =>
        sessionFindings.Concat(spans.SelectMany(s => s.Findings));
}
```

Implement `A365ValidationException : Exception` with a public `Report` property
and internal constructor:

```csharp
internal A365ValidationException(A365ValidationReport report)
    : base(report?.ToString())
{
    Report = report ?? throw new ArgumentNullException(nameof(report));
}
```

- [ ] **Step 4: Implement deterministic actionable formatting**

Format:

1. valid/invalid headline with active error, warning, and suppression counts;
2. session findings;
3. spans ordered by trace ID, then span ID;
4. findings ordered by status, severity descending, then rule ID;
5. remediation on a separate `Fix:` line;
6. suppression reason on a separate `SUPPRESSED:` line.

Use `StringBuilder` and `Environment.NewLine`. Do not emit attribute values,
which may contain sensitive content.

- [ ] **Step 5: Rerun report tests**

Run the Step 2 command.

Expected: PASS for all report tests.

- [ ] **Step 6: Commit reports and diagnostics**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationReportTests.cs
git commit -m "Add A365 validation diagnostics" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 4: Activity Capture and Public Evaluator

**Files:**
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365ActivityCaptureSession.cs`
- Create: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\A365InstrumentationValidator.cs`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ActivityCaptureSessionTests.cs`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365InstrumentationValidatorTests.cs`

**Interfaces:**
- Consumes: Task 1 options/snapshots, Task 2 engine, Task 3 report.
- Produces: public `A365InstrumentationValidator.EvaluateAsync(...)`.

- [ ] **Step 1: Write failing capture tests**

Add tests that create activities from a custom source:

```csharp
[TestMethod]
public async Task EvaluateAsync_CapturesRecognizedCustomSourceSpan()
{
    var report = await A365InstrumentationValidator.EvaluateAsync(() =>
    {
        using var source = new ActivitySource("Customer.Agent");
        using var activity = source.StartActivity("chat model");
        activity.Should().NotBeNull();
        SetValidChatAttributes(activity!);
        return Task.CompletedTask;
    });

    report.Spans.Should().ContainSingle();
    report.Spans[0].Span.SourceName.Should().Be("Customer.Agent");
    report.IsValid.Should().BeTrue();
}

[TestMethod]
public async Task EvaluateAsync_IgnoresUnsupportedActivities()
{
    var report = await A365InstrumentationValidator.EvaluateAsync(() =>
    {
        using var source = new ActivitySource("Customer.Agent");
        using var activity = source.StartActivity("http request");
        activity!.SetTag("gen_ai.operation.name", "unsupported");
        return Task.CompletedTask;
    });

    report.IsValid.Should().BeFalse();
    report.SessionFindings.Should().ContainSingle(f =>
        f.RuleId == A365ValidationRuleIds.NoSpansCaptured);
}

[TestMethod]
public async Task EvaluateAsync_RethrowsActionException()
{
    var expected = new InvalidOperationException("application failed");

    Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
        () => Task.FromException(expected));

    var actual = await act.Should().ThrowAsync<InvalidOperationException>();
    actual.Which.Should().BeSameAs(expected);
}
```

Add tests for span filtering, late orphan work during the quiet period,
completion timeout, cancellation, listener cleanup after exceptions, and two
concurrent evaluations running serially.

Use this helper for valid custom chat spans:

```csharp
private static void SetValidChatAttributes(Activity activity)
{
    activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
    activity.SetTag(OpenTelemetryConstants.TenantIdKey, "tenant");
    activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
    activity.SetTag(OpenTelemetryConstants.GenAiAgentNameKey, "Weather agent");
    activity.SetTag(
        OpenTelemetryConstants.GenAiAgentDescriptionKey,
        "Answers weather questions");
    activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, "agent-user");
    activity.SetTag(
        OpenTelemetryConstants.AgentEmailKey,
        "agent@example.com");
    activity.SetTag(
        OpenTelemetryConstants.AgentBlueprintIdKey,
        "blueprint");
    activity.SetTag(OpenTelemetryConstants.GenAiRequestModelKey, "gpt-4.1");
    activity.SetTag(OpenTelemetryConstants.GenAiProviderNameKey, "openai");
}
```

- [ ] **Step 2: Run capture/evaluator tests and confirm failure**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~A365ActivityCaptureSessionTests|FullyQualifiedName~A365InstrumentationValidatorTests" --no-restore
```

Expected: FAIL because capture and evaluator types do not exist.

- [ ] **Step 3: Implement activity eligibility and snapshots**

`A365ActivityCaptureSession` owns:

```csharp
private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);
private readonly ConcurrentDictionary<Activity, byte> active = new();
private readonly ConcurrentQueue<Activity> completed = new();
private readonly ActivityListener listener;
private long eligibleChangeVersion;
```

Configure both sampling delegates:

```csharp
listener.ShouldListenTo = _ => true;
listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
    ActivitySamplingResult.AllDataAndRecorded;
listener.SampleUsingParentId =
    (ref ActivityCreationOptions<string> _) =>
        ActivitySamplingResult.AllDataAndRecorded;
listener.ActivityStarted = OnStarted;
listener.ActivityStopped = OnStopped;
ActivitySource.AddActivityListener(listener);
```

`OnStarted` stores every activity in `active` and increments
`eligibleChangeVersion` when the activity already has a recognized operation.
`OnStopped` removes it, queues it only when the final operation is recognized,
and increments the version for an eligible activity.

Eligibility must reuse `OpenTelemetryConstants.GenAiOperationNames` and read
`gen_ai.operation.name` from the activity tag. Attribute snapshot creation
copies `activity.TagObjects` into an ordinal dictionary and uses lower-case
hex trace/span IDs.

- [ ] **Step 4: Implement bounded quiet-period completion**

Use `Stopwatch` and `Task.Delay` rather than `Task.WaitAsync`, which is not
available on `netstandard2.0`:

```csharp
internal async Task<A365CaptureResult> CompleteAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken)
{
    var stopwatch = Stopwatch.StartNew();

    while (stopwatch.Elapsed < timeout)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = Interlocked.Read(ref eligibleChangeVersion);

        await Task.Delay(QuietPeriod, cancellationToken).ConfigureAwait(false);

        if (version == Interlocked.Read(ref eligibleChangeVersion) &&
            !active.Keys.Any(IsEligible))
        {
            return CreateResult(timedOut: false);
        }
    }

    return CreateResult(timedOut: true);
}
```

`A365CaptureResult` is an internal type containing filtered snapshots and
snapshots of currently eligible timed-out activities. Apply `SpanFilter` after
creating immutable snapshots. If `SpanFilter` throws, propagate an
`InvalidOperationException` that identifies the span.

Define `A365CaptureResult` as an internal sealed class in
`A365ActivityCaptureSession.cs` with `IReadOnlyList<A365SpanSnapshot> Spans`
and `IReadOnlyList<A365SpanSnapshot> TimedOutSpans` getters.

- [ ] **Step 5: Implement process-wide orchestration**

```csharp
public static class A365InstrumentationValidator
{
    private static readonly SemaphoreSlim SessionLock = new(1, 1);

    public static async Task<A365ValidationReport> EvaluateAsync(
        Func<Task> action,
        Action<A365ValidationOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var options = new A365ValidationOptions();
        configure?.Invoke(options);
        A365ValidationEngine.ValidateOptions(options);

        await SessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            A365CaptureResult captured;
            using (var capture =
                new A365ActivityCaptureSession(options.SpanFilter))
            {
                await action().ConfigureAwait(false);
                captured = await capture.CompleteAsync(
                    options.SpanCompletionTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            return BuildReport(captured, options);
        }
        finally
        {
            SessionLock.Release();
        }
    }
}
```

`BuildReport`:

- adds `NoSpansCaptured` as an active error when no snapshots exist;
- adds one `SpanCompletionTimeout` active error per timed-out eligible span;
- validates captured snapshots with `A365ValidationEngine`;
- adds one `UnusedSuppression` warning per suppression whose `WasUsed` is false;
- returns `A365ValidationReport`.

The explicit `using` block must detach the listener before report evaluation
and before the process-wide lock is released. Do not catch customer action
exceptions.

- [ ] **Step 6: Rerun capture/evaluator tests**

Run the Step 2 command.

Expected: PASS for all capture and evaluator tests.

- [ ] **Step 7: Commit the capture harness**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ActivityCaptureSessionTests.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365InstrumentationValidatorTests.cs
git commit -m "Add in-process A365 span validation harness" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 5: Manual Scope and Auto-Instrumentation Integration Coverage

**Files:**
- Modify: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365InstrumentationValidatorTests.cs`
- Reference: `test\Microsoft.OpenTelemetry.Agent365.Tests\Integration\Extensions\AgentFrameworkSpanProcessorTests.cs`
- Reference: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Processors\ActivityTest.cs`

**Interfaces:**
- Consumes: complete public evaluator and existing A365 scopes/processors.
- Produces: end-to-end proof that SDK-created and processor-enriched spans are validated.

- [ ] **Step 1: Add a failing valid manual-scope integration test**

Create a real provider so `ActivityProcessor` enriches baggage-backed values:

```csharp
[TestMethod]
public async Task EvaluateAsync_ValidManualScopes_PassCertification()
{
    using var provider = Sdk.CreateTracerProviderBuilder()
        .AddSource(OpenTelemetryConstants.SourceName)
        .AddProcessor(new ActivityProcessor())
        .Build();

    var report = await A365InstrumentationValidator.EvaluateAsync(() =>
    {
        var agent = CreateCertificationAgentDetails();
        var user = new UserDetails(
            "user-id",
            "user@example.com",
            "User Name");
        var request = new Request(
            "hello",
            sessionId: "session",
            conversationId: "conversation");

        using (InvokeAgentScope.Start(
            request,
            new InvokeAgentScopeDetails(new Uri("https://example.com")),
            agent,
            new CallerDetails(user)))
        {
        }

        using (InferenceScope.Start(
            request,
            new InferenceCallDetails(
                InferenceOperationType.Chat,
                "gpt-4.1",
                "openai"),
            agent,
            user))
        {
        }

        using (ExecuteToolScope.Start(
            request,
            new ToolCallDetails("weather", "{}"),
            agent,
            user))
        {
        }

        using (OutputScope.Start(
            request,
            new Response(new[] { "sunny" }),
            agent,
            user))
        {
        }

        using (ApplyGuardrailScope.Start(
            new GuardrailDetails(
                GuardrailTargetType.LlmInput,
                GuardrailDecisionType.Allow),
            agent,
            request,
            user))
        {
        }

        return Task.CompletedTask;
    });

    report.EnsureValid();
    report.Spans.Should().HaveCount(5);
}
```

`CreateCertificationAgentDetails` supplies agent ID, name, description,
agentic user ID, agentic user email, blueprint ID, and tenant ID.

```csharp
private static AgentDetails CreateCertificationAgentDetails()
{
    return new AgentDetails(
        agentId: "agent",
        agentName: "Weather agent",
        agentDescription: "Answers weather questions",
        agenticUserId: "agent-user",
        agenticUserEmail: "agent@example.com",
        agentBlueprintId: "blueprint",
        tenantId: "tenant");
}
```

- [ ] **Step 2: Run the manual integration test**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~EvaluateAsync_ValidManualScopes_PassCertification" --no-restore
```

Expected: PASS, proving the public evaluator sees the final attributes emitted
by every manual scope.

- [ ] **Step 3: Add a supported auto-instrumentation test**

Follow the credential-free setup in
`AgentFrameworkInvokeAgentPipelineTests`: register `UseAgentFramework()`, the
Agent Framework source, and `ActivityProcessor` (the `UseAgentFramework()` call
already registers `AgentFrameworkSpanProcessor`); then create a synthetic Agent
Framework `invoke_agent` activity inside
`A365InstrumentationValidator.EvaluateAsync`.

The test must assert:

```csharp
report.Spans.Should().ContainSingle(span =>
    string.Equals(
        span.Span.OperationName,
        OpenTelemetryConstants.ChatOperationName,
        StringComparison.OrdinalIgnoreCase));
report.EnsureValid();
```

Wrap the activity creation in:

```csharp
using (new BaggageBuilder()
    .TenantId("tenant")
    .AgentId("agent")
    .AgentName("Weather agent")
    .AgentDescription("Answers weather questions")
    .AgenticUserId("agent-user")
    .AgenticUserEmail("agent@example.com")
    .AgentBlueprintId("blueprint")
    .Build())
{
    var tags = new ActivityTagsCollection
    {
        {
            OpenTelemetryConstants.GenAiOperationNameKey,
            OpenTelemetryConstants.InvokeAgentOperationName
        },
        { OpenTelemetryConstants.UserIdKey, "user-id" },
        { OpenTelemetryConstants.UserNameKey, "User Name" },
        { OpenTelemetryConstants.UserEmailKey, "user@example.com" },
    };

    using var activity = source.StartActivity(
        "invoke_agent WeatherAgent",
        ActivityKind.Internal,
        default(ActivityContext),
        tags);
    activity.Should().NotBeNull();
}
```

- [ ] **Step 4: Add a targeted legitimate-suppression integration test**

Create an `invoke_agent` scope without `UserDetails`, suppress only the three
invoke-user rule IDs for `invoke_agent`, and assert:

```csharp
report.IsValid.Should().BeTrue();
report.SuppressedFindingCount.Should().Be(3);
report.Spans.Single().Findings.Should().OnlyContain(
    finding => finding.Status == A365ValidationFindingStatus.Suppressed);
report.ToString().Should().Contain("Anonymous entry point");
```

- [ ] **Step 5: Run all validation tests**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Runtime.Validation" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit integration coverage**

```powershell
git add test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation src\Microsoft.OpenTelemetry\Agent365
git commit -m "Test A365 instrumentation validation end to end" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 6: Public API Baseline and Consumer Documentation

**Files:**
- Modify: `src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt`
- Modify: `docs\agent365-getting-started.md`
- Test: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationReportTests.cs`

**Interfaces:**
- Consumes: finalized public API from Tasks 1-5.
- Produces: analyzer-approved package surface and customer instructions.

- [ ] **Step 1: Add a failing public API analyzer build**

Run:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --framework net8.0 --no-restore
```

Expected: FAIL with `RS0016` entries for the new public validation API.

- [ ] **Step 2: Update the public API baseline**

Append the analyzer-generated signatures for:

- all three enums and enum values;
- every `A365ValidationRuleIds` constant;
- `A365SpanSnapshot`;
- `A365ValidationFinding`;
- `A365SpanValidationResult`;
- `A365ValidationOptions` and all suppression overloads;
- `A365ValidationReport`;
- `A365ValidationException`; and
- `A365InstrumentationValidator.EvaluateAsync`.

Use the exact signatures printed by the analyzer. Do not hand-edit nullable
annotations away.

- [ ] **Step 3: Add the development-time validation guide**

Add `## Validate A365 instrumentation in an integration test` near the existing
`## Validate locally` section in `docs\agent365-getting-started.md`.

Include this framework-neutral example:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Validation;

A365ValidationReport report =
    await A365InstrumentationValidator.EvaluateAsync(
        async () =>
        {
            await testClient.SendMessageAsync(
                "What is the weather in Seattle?");
        },
        options =>
        {
            options.Suppress(
                A365ValidationRuleIds.InvokeUserIdRequired,
                operationName: "invoke_agent",
                reason: "This entry point intentionally supports anonymous users.");
        });

report.EnsureValid();
```

Document:

- the test and application must execute in the same process;
- no exporter, collector, or test-framework adapter is needed;
- `EnsureValid()` places full diagnostics in test output;
- active errors fail, warnings and suppressed findings do not;
- tenant and agent export identity cannot be suppressed;
- suppressions remain visible and require a reason;
- operation and predicate targeting;
- validation sessions are process-wide and should not overlap unrelated
  telemetry work;
- the 10-second completion timeout; and
- separate-process validation is not supported in the first release.

- [ ] **Step 4: Add a documentation example formatting regression**

Extend `A365ValidationReportTests` to assert the exact key lines shown in the
guide:

```csharp
text.Should().Contain("A365 instrumentation validation failed:");
text.Should().Contain("[A365-TOOL-001] Missing gen_ai.tool.name");
text.Should().Contain("Fix: Set ToolCallDetails.ToolName");
text.Should().Contain("SUPPRESSED: This entry point intentionally supports anonymous users.");
```

- [ ] **Step 5: Run the API build and validation tests**

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --framework net8.0 --no-restore
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Runtime.Validation" --no-restore
```

Expected: both commands PASS with zero errors.

- [ ] **Step 6: Commit API and documentation**

```powershell
git add src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt docs\agent365-getting-started.md test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\A365ValidationReportTests.cs
git commit -m "Document A365 instrumentation validation" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```

---

### Task 7: Cross-Target Verification and Final Review

**Files:**
- Verify: `src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation\*.cs`
- Verify: `test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation\*.cs`
- Verify: `src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt`
- Verify: `docs\agent365-getting-started.md`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: verified implementation ready for review.

- [ ] **Step 1: Run formatting**

```powershell
dotnet format Microsoft.OpenTelemetry.slnx --no-restore --include src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation
```

Expected: command exits successfully and changes only validation files when
formatting is needed.

- [ ] **Step 2: Build every library target**

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-restore
```

Expected: PASS for `netstandard2.0` and `net8.0`.

- [ ] **Step 3: Run the complete Agent365 test project**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --no-restore
```

Expected: PASS for `net8.0` and `net10.0`.

- [ ] **Step 4: Check the final diff**

```powershell
git --no-pager diff --check
git status --short
git --no-pager diff --stat HEAD~4..HEAD
```

Expected: no whitespace errors; only the validation implementation, tests,
public API baseline, and A365 documentation are changed.

- [ ] **Step 5: Review requirements against the design**

Confirm all of the following from tests and diff:

- the listener is installed only during `EvaluateAsync`;
- the listener is detached on success, failure, timeout, and cancellation;
- action exceptions are rethrown unchanged;
- no spans and timed-out spans produce active errors;
- active warnings and suppressed findings do not invalidate the report;
- suppression reasons appear in formatted output;
- tenant and agent identity rules cannot be suppressed;
- manual, auto-instrumented, and custom-source spans are covered;
- no test framework or exporter dependency appears in production code; and
- all public API signatures are tracked.

- [ ] **Step 6: Commit formatting fixes if Step 1 changed files**

Only when `git status --short` shows formatting changes:

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Validation test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Validation
git commit -m "Format A365 validation implementation" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 168f3572-a89e-4ea5-a75e-e5aa701b55dd"
```
