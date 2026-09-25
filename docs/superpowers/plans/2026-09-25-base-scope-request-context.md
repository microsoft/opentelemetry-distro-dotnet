# Base Scope Request Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move shared `Request` telemetry attributes into `OpenTelemetryScope` and pass the request from all five concrete scope implementations.

**Architecture:** Replace the shipped protected base constructor with a new protected signature that accepts `Request?`. The base constructor sets shared request tags before starting the activity; concrete scopes retain only scope-specific request handling. Record the intentional shipped API removal and replacement in the public API baseline.

**Tech Stack:** C#, .NET, System.Diagnostics.Activity, MSTest, FluentAssertions, Microsoft.CodeAnalysis.PublicApiAnalyzers

## Global Constraints

- Do not retain a backward-compatible `OpenTelemetryScope` constructor overload.
- Keep the constructor `protected`; do not use `private protected`.
- Apply shared request tags to all five concrete scopes.
- Set request tags before `activity.Start()`.
- Request values take precedence over baggage values.
- Baggage continues to fill tags omitted from the request.
- Record the shipped constructor removal and replacement signature.

---

### Task 1: Add failing base request-context tests

**Files:**
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ScopeTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ApplyGuardrailScopeTest.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Common/ExportFormatterTests.cs`

**Interfaces:**
- Consumes: `Request`, `BaggageBuilder`, `OpenTelemetryScope`
- Produces: Tests specifying centralized request tags, baggage fallback, request precedence, and guardrail wiring

- [ ] **Step 1: Update test-only subclasses for the intended constructor**

In both `ScopeTests.cs` and `ExportFormatterTests.cs`, change the test scope constructor to accept an optional `Request? request` and pass it before `spanDetails`:

```csharp
public TestScope(
    string operationName,
    string activityName,
    AgentDetails agentDetails,
    Request? request = null,
    SpanDetails? spanDetails = null)
    : base(operationName, activityName, agentDetails, request, spanDetails)
{
}
```

Add `using Microsoft.Agents.A365.Observability.Runtime.Common;` to
`ScopeTests.cs`. Update existing positional `TestScope` calls that pass a
`SpanDetails` as the fourth argument to use the named argument
`spanDetails: ...`.

- [ ] **Step 2: Add a base-scope shared request tag test**

Add this test to `ScopeTests.cs`:

```csharp
[TestMethod]
public void Constructor_SetsSharedRequestTags()
{
    var request = new Request(
        sessionId: "session-123",
        channel: new Channel("service", "https://example.invalid/channel"),
        conversationId: "conversation-123",
        operationSource: "request-source");

    var activity = ListenForActivity(() =>
    {
        using var scope = new TestScope(
            OpenTelemetryConstants.InvokeAgentOperationName,
            "test activity",
            Util.GetAgentDetails(),
            request);
    });

    activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, "session-123");
    activity.ShouldHaveTag(OpenTelemetryConstants.GenAiConversationIdKey, "conversation-123");
    activity.ShouldHaveTag(OpenTelemetryConstants.ServiceNameKey, "request-source");
    activity.ShouldHaveTag(OpenTelemetryConstants.ChannelNameKey, "service");
    activity.ShouldHaveTag(OpenTelemetryConstants.ChannelLinkKey, "https://example.invalid/channel");
}
```

- [ ] **Step 3: Add request precedence and baggage fallback tests**

Add these tests to `ScopeTests.cs`:

```csharp
[TestMethod]
public void Constructor_RequestValuesTakePrecedenceOverBaggage()
{
    using (new BaggageBuilder()
        .OperationSource("baggage-source")
        .SessionId("baggage-session")
        .Build())
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = new TestScope(
                OpenTelemetryConstants.InvokeAgentOperationName,
                "test activity",
                Util.GetAgentDetails(),
                new Request(
                    sessionId: "request-session",
                    operationSource: "request-source"));
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, "request-session");
        activity.ShouldHaveTag(OpenTelemetryConstants.ServiceNameKey, "request-source");
    }
}

[TestMethod]
public void Constructor_BaggageFillsRequestValuesThatAreMissing()
{
    using (new BaggageBuilder()
        .OperationSource("baggage-source")
        .SessionId("baggage-session")
        .Build())
    {
        var activity = ListenForActivity(() =>
        {
            using var scope = new TestScope(
                OpenTelemetryConstants.InvokeAgentOperationName,
                "test activity",
                Util.GetAgentDetails(),
                new Request());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, "baggage-session");
        activity.ShouldHaveTag(OpenTelemetryConstants.ServiceNameKey, "baggage-source");
    }
}
```

- [ ] **Step 4: Add guardrail shared-context coverage**

Add this test to `ApplyGuardrailScopeTest.cs`:

```csharp
[TestMethod]
public void Start_SetsSharedRequestContext()
{
    var request = new Request(
        sessionId: "guardrail-session",
        channel: new Channel("service", "https://example.invalid/guardrail"),
        conversationId: "guardrail-conversation",
        operationSource: "guardrail-source");

    var activity = ListenForActivity(() =>
    {
        using var scope = ApplyGuardrailScope.Start(
            new GuardrailDetails(
                targetType: GuardrailTargetType.LlmInput,
                decisionType: GuardrailDecisionType.Allow),
            Util.GetAgentDetails(),
            request);
    });

    activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, "guardrail-session");
    activity.ShouldHaveTag(OpenTelemetryConstants.GenAiConversationIdKey, "guardrail-conversation");
    activity.ShouldHaveTag(OpenTelemetryConstants.ServiceNameKey, "guardrail-source");
    activity.ShouldHaveTag(OpenTelemetryConstants.ChannelNameKey, "service");
    activity.ShouldHaveTag(OpenTelemetryConstants.ChannelLinkKey, "https://example.invalid/guardrail");
}
```

- [ ] **Step 5: Run the tests to verify the intended constructor is not implemented**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --filter "FullyQualifiedName~ScopeTests|FullyQualifiedName~ApplyGuardrailScopeTest|FullyQualifiedName~ExportFormatterTests" --no-restore
```

Expected: compilation fails because `OpenTelemetryScope` does not yet have the `Request?` constructor parameter.

- [ ] **Step 6: Commit the failing tests**

```powershell
git add test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Scopes\ScopeTests.cs `
        test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Scopes\ApplyGuardrailScopeTest.cs `
        test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Common\ExportFormatterTests.cs
git commit -m "test: define base request context behavior" `
  -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: daca796f-4876-4d40-82f3-abe36153890e"
```

---

### Task 2: Centralize shared request tags in the base scope

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/InvokeAgentScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/InferenceScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OutputScope.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ApplyGuardrailScope.cs`

**Interfaces:**
- Consumes: `Request.SessionId`, `Request.ConversationId`, `Request.OperationSource`, and `Request.Channel`
- Produces: The protected constructor `OpenTelemetryScope(string, string, AgentDetails, Request?, SpanDetails?, UserDetails?)`

- [ ] **Step 1: Replace the base constructor signature**

Change the constructor and XML documentation in `OpenTelemetryScope.cs`:

```csharp
/// <param name="request">Optional request context used for shared span attributes.</param>
protected OpenTelemetryScope(
    string operationName,
    string activityName,
    AgentDetails agentDetails,
    Request? request,
    SpanDetails? spanDetails = null,
    UserDetails? userDetails = null)
```

- [ ] **Step 2: Set shared request tags before activity start**

After the user details block and before stopwatch/activity startup, add:

```csharp
if (request != null)
{
    SetTagMaybe(SessionIdKey, request.SessionId);
    SetTagMaybe(GenAiConversationIdKey, request.ConversationId);
    SetTagMaybe(ServiceNameKey, request.OperationSource);

    if (request.Channel != null)
    {
        SetTagMaybe(ChannelNameKey, request.Channel.Name);
        SetTagMaybe(ChannelLinkKey, request.Channel.Link);
    }
}
```

- [ ] **Step 3: Pass the request from every concrete scope**

Add `request: request` after `agentDetails` in each base constructor call:

```csharp
: base(
    operationName: ...,
    activityName: ...,
    agentDetails: agentDetails,
    request: request,
    spanDetails: ...,
    userDetails: ...)
```

Apply this to `InvokeAgentScope`, `InferenceScope`, `ExecuteToolScope`,
`OutputScope`, and `ApplyGuardrailScope`.

- [ ] **Step 4: Remove duplicated shared request tags**

Remove direct assignments of:

```csharp
SessionIdKey
GenAiConversationIdKey
ServiceNameKey
ChannelNameKey
ChannelLinkKey
```

from the five concrete scopes. Keep all scope-specific request handling,
including `request.Content`, `request.InputContent`, threat diagnostics,
request parameters, and output handling.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --filter "FullyQualifiedName~ScopeTests|FullyQualifiedName~ApplyGuardrailScopeTest|FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~InferenceScopeTest|FullyQualifiedName~InvokeAgentScopeTest|FullyQualifiedName~OutputScopeTest|FullyQualifiedName~ActivityProcessorTests|FullyQualifiedName~ExportFormatterTests" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit the implementation**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\OpenTelemetryScope.cs `
        src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\InvokeAgentScope.cs `
        src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\InferenceScope.cs `
        src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\ExecuteToolScope.cs `
        src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\OutputScope.cs `
        src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\ApplyGuardrailScope.cs
git commit -m "refactor: centralize request context tags" `
  -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: daca796f-4876-4d40-82f3-abe36153890e"
```

---

### Task 3: Record and validate the intentional API break

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`

**Interfaces:**
- Consumes: The old signature from `PublicAPI.Shipped.txt`
- Produces: Public API analyzer entries for the removed and replacement constructors

- [ ] **Step 1: Add the removed shipped signature**

Add:

```text
*REMOVED*Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryScope.OpenTelemetryScope(string! operationName, string! activityName, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.AgentDetails! agentDetails, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.SpanDetails? spanDetails = null, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.UserDetails? userDetails = null) -> void
```

- [ ] **Step 2: Add the replacement constructor signature**

Add:

```text
Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryScope.OpenTelemetryScope(string! operationName, string! activityName, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.AgentDetails! agentDetails, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Request? request, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.SpanDetails? spanDetails = null, Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.UserDetails? userDetails = null) -> void
```

- [ ] **Step 3: Run the affected test project**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --no-restore
```

Expected: all tests pass with no public API analyzer errors.

- [ ] **Step 4: Build the affected package**

Run:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Review the branch diff**

Run:

```powershell
git status --short
git --no-pager diff origin/main...HEAD
```

Expected: only the approved request-context refactor, tests, API baseline, and
design/plan documentation are present in the PR branch.

- [ ] **Step 6: Commit the API baseline**

```powershell
git add src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt
git commit -m "api: update base scope constructor contract" `
  -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" `
  -m "Copilot-Session: daca796f-4876-4d40-82f3-abe36153890e"
```
