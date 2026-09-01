# A365 Instrumentation Validation Design

## Summary

Add a framework-neutral, development-time validation harness to the A365 .NET
SDK. Consumers run one realistic agent scenario in an integration test while
the SDK captures completed A365 GenAI spans in-process and validates them
against SDK-owned certification requirements.

The harness returns a typed report and provides a default `EnsureValid()` check
that throws an actionable SDK exception. Context-dependent rules run only when
applicable. Consumers may explicitly suppress suppressible findings with a
documented reason when a required attribute cannot be provided.

## Goals

- Validate all A365-exportable GenAI spans produced during an explicit local
  evaluation, including manual scopes, auto-instrumentation, and custom
  `ActivitySource` spans.
- Keep certification rules and remediation guidance versioned in the SDK.
- Avoid dependencies on xUnit, MSTest, NUnit, FluentAssertions, DI, exporters,
  or a local collector.
- Show complete, actionable failure diagnostics directly in normal test output.
- Support legitimate exceptions without hiding them from the validation report.
- Require no changes to production observability configuration.
- Support both `netstandard2.0` and `net8.0`.

## Non-goals

- Validating telemetry emitted by a separately launched process.
- Providing a standalone CLI or OTLP validation collector in the first release.
- Validating every recommended OpenTelemetry GenAI attribute.
- Providing a general-purpose customer-defined rule engine.
- Replacing server-side certification or ingestion validation.
- Automatically deciding that a missing context-dependent attribute is
  acceptable when the SDK cannot infer the scenario.

## Consumer experience

The primary API is test-framework-neutral:

```csharp
A365ValidationReport report =
    await A365InstrumentationValidator.EvaluateAsync(
        async () => await testClient.SendMessageAsync("What is the weather?"),
        options =>
        {
            options.Profile = A365ValidationProfile.Certification;
            options.Suppress(
                A365ValidationRuleIds.UserIdRequired,
                operationName: "invoke_agent",
                reason: "This entry point supports anonymous users.");
        });

report.EnsureValid();
```

Consumers add an integration test that starts their application in the same
process and exercises a realistic agent path. The SDK owns activity capture,
certification rules, report generation, suppression matching, and diagnostic
formatting.

Consumers may inspect `report.IsValid` and the typed findings directly instead
of calling `EnsureValid()`.

## Public API

### `A365InstrumentationValidator`

`A365InstrumentationValidator` is the static entry point.

```csharp
public static Task<A365ValidationReport> EvaluateAsync(
    Func<Task> action,
    Action<A365ValidationOptions>? configure = null,
    CancellationToken cancellationToken = default);
```

`EvaluateAsync`:

1. validates options before running customer code;
2. acquires the process-wide validation-session lock;
3. installs a temporary `ActivityListener`;
4. executes the supplied asynchronous action;
5. observes a short quiet period after the action and waits for eligible
   activities that are still running, within the configured completion
   timeout;
6. detaches the listener;
7. creates immutable span snapshots; and
8. evaluates the selected SDK rule profile.

The action overload is intentionally small for the first release. Consumers
can capture values in the delegate closure. Additional generic result overloads
are not required.

### `A365ValidationOptions`

Options include:

- `Profile`, defaulting to `A365ValidationProfile.Certification`;
- `SpanCompletionTimeout`, defaulting to 10 seconds;
- zero or more suppression registrations; and
- an optional span-inclusion predicate for excluding known unrelated A365
  telemetry in a shared test process.

Suppressions have SDK-defined overloads for:

- a rule ID globally;
- a rule ID and operation name; and
- a rule ID, operation name, and span predicate.

Every suppression requires a non-empty reason. Configuration rejects unknown
rule IDs and attempts to suppress a non-suppressible rule.

### Reports and findings

`A365ValidationReport` is immutable and exposes:

- `IsValid`;
- captured span results;
- unsuppressed error count;
- warning count;
- suppressed finding count; and
- session-level findings such as no spans captured or completion timeout.

`A365SpanValidationResult` exposes a stable snapshot of:

- trace ID;
- span ID;
- display name;
- activity source;
- GenAI operation name;
- captured attributes; and
- findings.

`A365ValidationFinding` exposes:

- rule ID;
- severity;
- status (`Active` or `Suppressed`);
- operation and attribute names when applicable;
- diagnostic message;
- remediation;
- suppression reason when suppressed; and
- trace/span identity.

The public enums include the built-in profile, finding severity, and finding
status. `A365ValidationRuleIds` provides constants so customers do not use
unvalidated string literals for suppressions.

### Default validity check

`A365ValidationReport.EnsureValid()` returns normally when `IsValid` is true.
Otherwise it throws `A365ValidationException`.

The exception message contains the complete formatted report so ordinary test
runners display the failed spans, rule IDs, missing attributes, and remediation
without requiring object inspection. The exception also exposes the typed
`Report`.

Example output:

```text
A365 instrumentation validation failed: 2 errors, 1 suppressed finding

execute_tool weather_lookup [trace=..., span=...]
  [A365-TOOL-003] Missing gen_ai.tool.call.id
  Fix: Set ToolCallDetails.ToolCallId when starting ExecuteToolScope.

invoke_agent WeatherAgent [trace=..., span=...]
  [A365-CALLER-001] Missing user.id
  SUPPRESSED: This entry point supports anonymous users.
```

No assertion-library adapters are included in the first release.

## Activity capture

### Listener behavior

The evaluator installs a temporary `System.Diagnostics.ActivityListener`.
Its source predicate listens broadly enough to observe manual A365 scopes,
supported framework auto-instrumentation, and customer activity sources.

The listener requests full activity data during the evaluation so validation
does not depend on the application's production sampling decision. It considers
an activity A365-exportable when its final `gen_ai.operation.name` is one of the
operation names supported by the A365 exporter.

Completed `Activity` references are recorded from the stopped callback, but
attribute snapshots are created only after callback dispatch finishes. This
allows existing OpenTelemetry processors to complete their span enrichment
before validation reads final attributes.

The listener is detached in `finally`, including when the customer action,
capture timeout, or validation logic fails.

### Session boundaries

Capture uses the evaluation time window rather than trace ancestry. Existing
A365 scenarios may continue work on orphan asynchronous spans without a parent
activity, so descendant-only capture would miss telemetry that the exporter
accepts.

Because activity listeners are global to the process, only one validation
session may run at a time. A process-wide asynchronous lock serializes
evaluations. Documentation instructs consumers not to run unrelated
telemetry-producing work concurrently with a validation test. The optional
span-inclusion predicate can further narrow capture when isolation is
impractical.

### Completion

After the customer action completes, the evaluator continues listening until
eligible activities have stopped and no new eligible activity has started or
stopped during an internal 250-millisecond quiet period. This captures
short-lived orphan background work that starts immediately after an HTTP 202
or similar asynchronous handoff. The total post-action wait is bounded by
`SpanCompletionTimeout`, which defaults to 10 seconds.

Activities still running at timeout produce explicit session findings that
identify the operation and span when available. The evaluator never waits
indefinitely.

## Rule model

### Built-in certification catalog

The first release contains one SDK-owned
`A365ValidationProfile.Certification` catalog. Each internal rule defines:

- a stable public rule ID;
- applicable GenAI operation names;
- an attribute requirement;
- an applicability predicate;
- a severity;
- whether it is suppressible;
- a diagnostic message; and
- remediation guidance referencing the relevant SDK API.

The initial matrix is derived from the current A365 certification requirements
and the fields the exporter requires to identify and transmit spans. The
implementation must centralize this matrix rather than duplicating required
attribute lists across validators.

Values that are `null`, empty strings, or whitespace-only strings count as
missing. Rules that require a particular type or allowed value report invalid
values separately from missing values.

### Applicability

Rules run only for relevant operations and scenarios. Examples include:

- execute-tool fields only on `execute_tool`;
- inference fields only on `chat`;
- guardian decision fields only on `apply_guardrail`; and
- caller identity fields only when the certification scenario requires an
  identified caller.

When applicability can be determined from captured span attributes, the SDK
marks an irrelevant rule as not applicable and emits no finding. When the SDK
cannot infer that a certification requirement is irrelevant, the rule remains
active and the consumer must provide the attribute or document an explicit
suppression.

### Structural and suppressible rules

Rules essential to successful A365 export are non-suppressible. This includes
the tenant and agent identity used by the exporter to partition spans.
Session-level failures such as capturing zero eligible spans and invalid
validation configuration are also non-suppressible.

Certification attributes that may be unavailable in a legitimate customer
scenario are suppressible. Suppression does not remove a finding; it changes
its status to `Suppressed`, records the reason, and excludes it from
`IsValid`.

The first release does not require suppression expiration dates, quotas, or
external waiver files.

## Suppression matching

Suppressions are evaluated from most specific to least specific:

1. rule ID, operation name, and span predicate;
2. rule ID and operation name; and
3. global rule ID.

A span predicate receives an immutable public span snapshot rather than the
live `Activity`. Predicate exceptions are surfaced as configuration/evaluation
errors and are not treated as a matching suppression.

Every matched suppression is represented in the report. Unused suppressions
are reported as warnings so stale or misspelled scenario targeting is visible.

## Error handling

- A `null` action throws `ArgumentNullException` before listener installation.
- Invalid profile, timeout, rule ID, suppression reason, or non-suppressible
  suppression throws an options-validation exception before customer code runs.
- An exception from the customer action is rethrown unchanged after listener
  cleanup. Validation does not mask the application failure.
- Cancellation throws `OperationCanceledException` after listener cleanup.
- Capturing zero A365-exportable spans returns an invalid report with setup
  guidance.
- Span completion timeout returns an invalid report with timed-out-span
  findings.
- Listener cleanup occurs on every path.
- Validation infrastructure failures throw an SDK-specific exception rather
  than returning a success-shaped report.

## Compatibility and packaging

- The feature ships in the existing `Microsoft.OpenTelemetry` package.
- Existing tracing, exporter, sampling, and instrumentation configuration is
  unchanged when validation is not explicitly invoked.
- The validation listener exists only for the duration of `EvaluateAsync`.
- No service registration or production configuration is required.
- New public API entries are added to the public API analyzer baseline.
- Implementation uses APIs available to both current target frameworks.

## Testing

### Rule tests

- Validate each certification rule against a valid span.
- Report missing, empty, whitespace-only, wrong-type, and invalid-enum values.
- Verify operation and scenario applicability.
- Verify non-applicable rules emit no findings.
- Verify stable rule IDs and remediation text.

### Suppression tests

- Apply global, operation-level, and predicate suppressions.
- Prefer the most specific matching suppression.
- Preserve suppressed findings and reasons in typed and formatted reports.
- Exclude suppressed findings from the `IsValid` calculation while retaining
  them in the report.
- Reject blank reasons, unknown rule IDs, and non-suppressible rules.
- Report unused suppressions as warnings.
- Surface predicate exceptions.

### Capture tests

- Capture completed manual A365 scopes.
- Capture a supported auto-instrumented GenAI activity.
- Capture an A365-exportable custom `ActivitySource` span.
- Ignore non-GenAI activities and activities with unsupported operation names.
- Preserve attributes added by existing span processors.
- Capture orphan background spans started within the evaluation window.
- Capture eligible work that starts during the post-action quiet period.
- Wait for in-progress spans and report completion timeout.
- Serialize concurrent evaluation sessions.
- Detach the listener after success, action failure, timeout, and validation
  failure.

### Report tests

- Return `IsValid` for a fully valid scenario.
- Return an invalid report when no spans are captured.
- Format grouped, actionable diagnostics with rule IDs, span identity, and
  remediation.
- Include suppression counts and reasons.
- Verify `EnsureValid()` returns for valid reports.
- Verify `EnsureValid()` throws `A365ValidationException` containing the typed
  report and formatted diagnostics.

### Integration tests

- Exercise invoke-agent, inference, execute-tool, output, and guardrail manual
  scope paths through the public evaluator.
- Exercise at least one existing framework auto-instrumentation path.
- Verify a realistic valid scenario passes without suppressions.
- Verify a realistic scenario with an unavailable certification attribute
  passes only when the targeted suppression is configured.

## Documentation

Update `docs/agent365-getting-started.md` with a development-time instrumentation
validation section containing:

- a framework-neutral integration-test example;
- `EvaluateAsync(...).EnsureValid()` usage;
- example failure output;
- global, operation-level, and predicate suppression examples;
- guidance for choosing suppression reasons;
- the same-process requirement;
- the process-wide concurrency limitation;
- completion timeout guidance; and
- a note that separately launched applications require a future
  exporter/collector workflow.

## Future extensions

The capture-independent rule and report model should be reusable by future:

- an opt-in OpenTelemetry validation processor/exporter;
- a local OTLP validation CLI or collector;
- additional SDK-owned recommendation profiles; and
- test-framework-specific assertion adapters.

These extensions are not part of the first release.
