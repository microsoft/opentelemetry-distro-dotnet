# Microsoft.OpenTelemetry.A365.Etw

> **Important:** This package is intended for Microsoft internal Agent365 ETW integration scenarios and will not be published to NuGet.org. It is not intended for general third-party consumption. Application developers should use `Microsoft.OpenTelemetry` instead.

This package provides Agent365 observability ETW event emission and payload formatting for Microsoft internal integrations. It can be consumed as a standalone package in those scenarios and automatically resolves `Microsoft.OpenTelemetry.A365.Contracts` as a transitive dependency. Do not install the Contracts package directly.

## Guidance for application developers

Add [Microsoft.OpenTelemetry](https://www.nuget.org/packages/Microsoft.OpenTelemetry) to your application:

```bash
dotnet add package Microsoft.OpenTelemetry
```

See the [Microsoft.OpenTelemetry documentation](https://github.com/microsoft/opentelemetry-distro-dotnet#readme) for configuration and usage.

## Guardrail findings in direct ETW logs

Internal integrations can supply findings directly to `IA365EtwLogger<T>.LogApplyGuardrail`;
no `Activity` or tracing provider is required:

```csharp
etwLogger.LogApplyGuardrail(
    guardrailDetails,
    agentDetails,
    conversationId,
    parentSpanId,
    findings: new[]
    {
        new GuardrailFinding(
            riskCategory: "prompt_injection",
            riskSeverity: "high",
            policyDecisionType: "deny",
            policyId: "policy-1",
            riskScore: 0.9,
            riskMetadata: new[] { "position:input[0].content" }),
    });
```

`GuardrailFinding` is in the
`Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts` namespace.
Each finding appears in the log payload's `Events` array with `name`
(`microsoft.security.finding`), `timeUnixNano`, and `attributes`. Required attributes
are `microsoft.security.risk.category` and `microsoft.security.risk.severity`.
Supplied policy details, risk score, and risk metadata are included with the same
attribute names and value types as `ApplyGuardrailScope.RecordFinding`.
Risk metadata must not contain sensitive user content or PII.

Findings are part of the existing ETW `LogJson` event (ID 2000), not separate ETW
events. Null or empty collections omit `Events` and preserve the existing payload
shape. Null entries in a findings collection throw `ArgumentException`.
Consumers must rebuild against the updated method signature, and custom
`IA365EtwLogger<T>` implementations must add the optional `findings` parameter.
