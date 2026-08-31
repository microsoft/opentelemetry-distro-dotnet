# Execute Tool JSON Schema Design

## Summary

Add concrete, customer-friendly .NET models for the JSON stored in the
`gen_ai.tool.call.arguments` and `gen_ai.tool.call.result` attributes.

The models apply only to:

- `ExecuteToolScope`;
- the execute-tool ETW logging path; and
- the shared `ExecuteToolDataBuilder` used by ETW.

Existing string and dictionary APIs remain supported without behavior changes.
The new models are optional typed conveniences over an extensible dictionary
representation.

## Goals

- Represent the documented execute-tool arguments and result JSON structures
  with concrete public classes.
- Preserve arbitrary provider-specific fields at every level.
- Let callers replace a standard field with a custom value when the standard
  shape is not applicable.
- Keep telemetry recording non-throwing for customer-provided payload values.
- Produce equivalent JSON attribute strings through the tracing and ETW paths.
- Preserve all shipped string and dictionary APIs.

## Non-goals

- Changing invoke-agent, inference, output, or guardrail payloads.
- Changing Agent Framework automatic instrumentation.
- Enforcing that callers populate every field listed as required by the schema.
- Performing JSON Schema validation.
- Removing or changing the current legacy wrapping behavior for strings.

## Public object model

### Dictionary-backed classes

Each schema class is a sealed subclass of
`Dictionary<string, object?>`. The inherited dictionary is the single source of
truth for serialization.

This applies individually to every concrete type in this design, including
`ToolCallIdentifier` and `ToolCallContainer`; identifiers and containers can
therefore carry arbitrary provider-specific key/value pairs in addition to
their standard properties.

Each class provides:

- a parameterless constructor;
- a constructor that copies an `IDictionary<string, object?>`; and
- typed convenience properties for the documented standard fields.

A typed property reads and writes the corresponding JSON key in the inherited
dictionary. Indexer assignments and property assignments therefore have normal
last-write-wins behavior:

```csharp
var policy = new ToolCallResultPolicy
{
    Decision = ToolPolicyDecision.Allow,
};

policy["decision"] = "provider_conditional_allow";
```

After the indexer assignment, the serialized `decision` value is
`"provider_conditional_allow"`. The typed `Decision` getter returns `null`
because the current value is not a recognized `ToolPolicyDecision`.

All typed getters are tolerant. They return `null` when a key is absent or its
current value is incompatible with the typed property. They do not throw.
Setting a nullable property to `null` removes the key.

Custom dictionary keys are preserved exactly as supplied. Only the standard
property setters choose the schema's snake_case key names.

### Arguments types

#### `ExecuteToolCallArguments`

Standard properties:

- `string? SchemaVersion` maps to `schema_version`;
- `IList<ToolCallResource>? Resources` maps to `resources`;
- `ToolCallAction? Action` maps to `action`; and
- `IDictionary<string, object?>? Parameters` maps to `parameters`.

The parameterless constructor initializes `schema_version` to `"1.0"`.
Callers may overwrite or remove it through the property or dictionary API.

#### `ToolCallResource`

Standard properties:

- `string? Id`;
- `string? Uri`;
- `string? Name`;
- `string? Type`;
- `string? Provider`;
- `IList<ToolCallIdentifier>? Identifiers`; and
- `ToolCallContainer? Container`.

#### `ToolCallIdentifier`

Standard properties:

- `string? Type`; and
- `string? Value`.

`ToolCallIdentifier` also inherits `Dictionary<string, object?>`, so callers can
add, replace, or remove identifier fields through the dictionary indexer.

#### `ToolCallContainer`

Standard properties:

- `string? Id`;
- `string? Uri`; and
- `string? Type`.

`ToolCallContainer` also inherits `Dictionary<string, object?>`, so callers can
represent provider-specific container shapes through arbitrary key/value pairs.

### Result types

#### `ExecuteToolCallResult`

Standard properties:

- `ToolCallResultOutcome? Outcome`;
- `IList<ToolCallResultResource>? Resources`;
- `IDictionary<string, object?>? Data`; and
- `ToolCallResultPagination? Pagination`.

#### `ToolCallResultResource`

Standard properties:

- `string? Id`;
- `string? Uri`;
- `string? Name`;
- `string? Type`;
- `string? Provider`;
- `IList<ToolCallIdentifier>? Identifiers`;
- `ToolCallContainer? Container`;
- `ToolCallResultOutcome? Outcome`;
- `ToolCallResultSensitivity? Sensitivity`;
- `ToolCallResultPolicy? Policy`;
- `ToolCallResultSecurity? Security`; and
- `IDictionary<string, object?>? Data`.

Argument and result resources remain separate public types so result-only
metadata cannot appear accidentally through the typed argument API. They do
not introduce a shared public base type beyond `Dictionary<string, object?>`.

#### `ToolCallResultOutcome`

Standard properties:

- `ToolCallOutcomeStatus? Status`;
- `string? Code`;
- `string? ProviderCode`; and
- `string? Message`.

#### `ToolCallResultSensitivity`

Standard property:

- `string? LabelId`.

#### `ToolCallResultPolicy`

Standard properties:

- `ToolPolicyDecision? Decision`;
- `string? Id`; and
- `string? Name`.

#### `ToolCallResultSecurity`

Standard property:

- `bool? XpiaDetected`.

#### `ToolCallResultPagination`

Standard properties:

- `bool? HasMore`;
- `string? NextCursor`; and
- `long? TotalCount`.

### Allowed-value enums

The typed convenience properties use:

- `ToolCallAction`: `Create`, `Read`, `Update`, `Delete`;
- `ToolCallOutcomeStatus`: `Success`, `Failure`; and
- `ToolPolicyDecision`: `Allow`, `Deny`.

Property setters store the lowercase schema value as a string. Getters parse
recognized strings case-insensitively. Callers can use the dictionary indexer
to provide future or provider-specific values without waiting for an SDK
release.

### Extensibility by type

Every listed type has its own inherited dictionary:

| Type | Custom key/value pairs |
| --- | --- |
| `ExecuteToolCallArguments` | Yes |
| `ToolCallResource` | Yes |
| `ToolCallIdentifier` | Yes |
| `ToolCallContainer` | Yes |
| `ExecuteToolCallResult` | Yes |
| `ToolCallResultResource` | Yes |
| `ToolCallResultOutcome` | Yes |
| `ToolCallResultSensitivity` | Yes |
| `ToolCallResultPolicy` | Yes |
| `ToolCallResultSecurity` | Yes |
| `ToolCallResultPagination` | Yes |

## API integration

### `ToolCallDetails`

Add a constructor accepting `ExecuteToolCallArguments` and a
`ToolCallArguments` property.

Arguments use this precedence when recorded:

1. `ToolCallArguments`;
2. the existing `ArgumentsObject`; and
3. the existing `Arguments` string.

The existing constructors, properties, deconstruction shape, equality behavior,
and legacy serialization behavior remain available. Equality and hashing
include the typed arguments using the same reference-based treatment currently
used for `ArgumentsObject`.

### `ExecuteToolScope`

The constructor serializes `ToolCallDetails.ToolCallArguments` through the new
execute-tool payload serializer before considering legacy argument values.

Add:

```csharp
public void RecordResponse(ExecuteToolCallResult result)
```

The existing string and dictionary `RecordResponse` overloads remain unchanged.

### ETW logger and DTO builder

Add typed-result support to:

- `A365EtwLogger<T>.LogToolCall`;
- `A365EtwLoggerExtensions.LogToolCall<T>(this IA365EtwLogger<T>, ...)`; and
- `ExecuteToolDataBuilder.Build`.

To avoid changing the shipped `IA365EtwLogger<T>` contract while still giving
interface-typed callers a typed path, keep the typed overload on
`A365EtwLogger<T>` and add an extension method on `IA365EtwLogger<T>`. The
typed result remains a required second parameter rather than replacing the
existing optional string parameter position:

```csharp
LogToolCall(
    ToolCallDetails toolCallDetails,
    ExecuteToolCallResult result,
    AgentDetails agentDetails,
    string conversationId,
    ...);
```

The typed builder overload follows the same ordering. The concrete overload and
the extension method both preserve the existing optional timing, tracing,
channel, caller, extra-attribute, and error parameters after the required
parameters.

The concrete typed ETW overload passes the result to the builder without
converting it at the logger boundary. The interface extension method serializes
with the same helper used by `ExecuteToolScope` and forwards the JSON string to
the existing interface member.

## Serialization

Add an internal `ExecuteToolPayloadSerializer` dedicated to the two execute-tool
attributes. It does not change `MessageUtils` or message serialization.

The serializer first creates a JSON-safe graph from the dictionary-backed
payload:

1. JSON primitives and `null` pass through.
2. `IDictionary<string, object?>` values are copied recursively.
3. Non-string enumerable values are copied element by element.
4. Other objects are serialized normally with `System.Text.Json`.
5. If an individual dictionary value or collection element cannot be
   serialized, that value is replaced with `value.ToString()`.
6. If `ToString()` fails or returns `null`, the value's full type name is used.
7. A repeated reference in the active traversal path is treated as an
   unserializable value and replaced using the same fallback.
8. A value deeper than the serializer's supported traversal depth is replaced
   using the same fallback, preventing excessively deep customer data from
   causing final JSON serialization to fail.

Fallback occurs at the dictionary-entry or collection-element boundary. If an
arbitrary POCO fails as a unit, that POCO value is replaced; the serializer
does not reflect over and partially rewrite the POCO's individual properties.

The final graph is serialized as compact JSON. Dictionary insertion order is
preserved by the runtime serializer, but consumers must not depend on property
ordering.

This path is non-throwing for customer payload content. It does not reject
missing fields, custom field names, standard-field overrides, cycles, or
unsupported object values.

## Data flow

### Span arguments

1. The customer constructs `ExecuteToolCallArguments`.
2. The model is supplied through `ToolCallDetails`.
3. `ExecuteToolScope` selects the typed model by precedence.
4. `ExecuteToolPayloadSerializer` returns a JSON string.
5. The string is stored in `gen_ai.tool.call.arguments`.

### Span result

1. The customer constructs `ExecuteToolCallResult`.
2. The customer calls the typed `RecordResponse` overload.
3. `ExecuteToolPayloadSerializer` returns a JSON string.
4. The string is stored in `gen_ai.tool.call.result`.

### ETW result

1. The customer passes `ExecuteToolCallResult` to the typed concrete overload
   or the public `IA365EtwLogger<T>` extension method.
2. The concrete overload forwards it to `ExecuteToolDataBuilder`, while the
   interface extension serializes it with `ExecuteToolPayloadSerializer` and
   calls the existing string overload.
3. The resulting JSON string is stored in the execute-tool attributes.
4. The existing ETW pipeline emits the containing telemetry DTO.

## Compatibility

- No existing public member is removed or changed.
- The typed ETW and builder overloads place the result second, so existing calls
  that pass `null` in the legacy response-content position remain unambiguous.
- `IA365EtwLogger<T>` remains source- and binary-compatible for external
  implementers because typed usage is added as a public extension method rather
  than a new abstract interface member.
- Legacy string arguments still pass through when valid JSON and are otherwise
  wrapped as `{"arguments": ...}`.
- Legacy string results still pass through when valid JSON and are otherwise
  wrapped as `{"result": ...}`.
- Existing dictionary arguments and results continue using their current
  overloads.
- The new typed arguments take precedence only when a caller selects the new
  `ToolCallDetails` constructor.
- The public API analyzer baselines are updated for all new public types and
  overloads.

## Testing

### Model tests

- Serialize the complete arguments example and assert every standard key and
  value.
- Serialize the complete result example and assert every nested key and value.
- Verify the default `schema_version` and its overwrite/removal behavior.
- Verify sparse and empty models serialize successfully.
- Verify custom keys at every object level.
- Verify standard property and indexer assignments are last-write-wins.
- Verify typed getters return `null` after incompatible custom overrides.
- Verify nullable property assignment removes a key.
- Verify enum setters emit lowercase values and getters recognize them.

### Serializer tests

- Preserve primitives, nested dictionaries, and collections.
- Preserve serializable arbitrary objects.
- Replace one unserializable dictionary value without changing sibling values.
- Replace one unserializable collection element without changing sibling
  elements.
- Replace cyclic references without throwing.
- Use the type name when `ToString()` fails or returns `null`.

### Tracing tests

- Record typed arguments on an execute-tool activity.
- Verify typed arguments take precedence over legacy forms.
- Record a typed result with `ExecuteToolScope.RecordResponse`.
- Parse both attribute strings as JSON and assert their structures.
- Retain the existing legacy string and dictionary tests.

### ETW and builder tests

- Build execute-tool DTO data with typed arguments and a typed result.
- Verify the attribute values are JSON strings containing the expected objects.
- Emit a typed ETW tool call and parse the event payload and nested attribute
  strings.
- Assert typed span and ETW serialization produce equivalent JSON structures.
- Retain existing ETW string-result behavior tests.

## Documentation

Update the execute-tool section of `docs/agent365-getting-started.md` with:

- a complete typed arguments example;
- a typed result example;
- an example of custom fields and standard-field override through the indexer;
- a note that telemetry attributes contain JSON-serialized strings; and
- a compatibility note for existing string and dictionary overloads.
