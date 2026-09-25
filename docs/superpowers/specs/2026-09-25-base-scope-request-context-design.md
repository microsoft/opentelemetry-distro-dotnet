# Base Scope Request Context Design

## Goal

Centralize shared `Request` telemetry attributes in `OpenTelemetryScope` rather
than setting them independently in each concrete Agent365 tracing scope.

## Constructor contract

Replace the existing protected `OpenTelemetryScope` constructor with a
constructor that also accepts `Request? request`. Do not retain a
backward-compatible overload, and do not change the constructor to
`private protected`.

This intentionally changes the protected base-class contract. Agent365 users
are expected to create spans through the concrete scope implementations rather
than subclassing `OpenTelemetryScope`.

The new parameter order is:

```csharp
protected OpenTelemetryScope(
    string operationName,
    string activityName,
    AgentDetails agentDetails,
    Request? request,
    SpanDetails? spanDetails = null,
    UserDetails? userDetails = null)
```

Update the unshipped public API baseline to replace the old constructor
signature with this signature.

## Centralized request attributes

When `request` is non-null, the base constructor sets:

| Request value | OpenTelemetry attribute |
|---|---|
| `Request.SessionId` | `session.id` |
| `Request.ConversationId` | `gen_ai.conversation.id` |
| `Request.OperationSource` | `service.name` |
| `Request.Channel.Name` | `microsoft.channel.name` |
| `Request.Channel.Link` | `microsoft.channel.link` |

Set these tags before `activity.Start()`. `ActivityProcessor.OnStart()` uses
`CoalesceTag` for baggage-backed values, so an explicit request value remains
authoritative while baggage fills only missing attributes.

Null request values do not create tags. Existing baggage propagation remains
available when request values are absent.

## Concrete scopes

Pass the existing request into the base constructor from all five concrete
scope implementations:

- `InvokeAgentScope`
- `InferenceScope`
- `ExecuteToolScope`
- `OutputScope`
- `ApplyGuardrailScope`

Remove duplicated assignments for session ID, conversation ID, operation
source, channel name, and channel link from those scopes. Keep request handling
that is specific to a scope, including input-message recording, guardrail
content, request parameters, and threat diagnostics.

This adds session ID and operation source propagation to guardrail spans when
those values are present on the request.

## Validation

Tests must verify:

- Every concrete scope receives the shared request tags from the base class.
- Null request fields do not create attributes.
- Request values take precedence over values supplied through
  `BaggageBuilder`.
- Baggage values still populate attributes when the request omits them.
- Scope-specific request behavior remains unchanged.
- The public API baseline contains only the new constructor signature.

Run the focused scope and processor tests, then build the affected projects.
