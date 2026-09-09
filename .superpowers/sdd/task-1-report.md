# Task 1 Report - Standalone Agent365 ETW SDK Contracts extraction

## Commit
- SHA: 813830be793e94a0964f61957cf3258ebfab8884
- Message: `refactor: extract Agent365 contracts package`

## Files changed
```text
M	Directory.Packages.props
A	src/Microsoft.Agents.A365.Observability.Contracts/.publicApi/PublicAPI.Shipped.txt
A	src/Microsoft.Agents.A365.Observability.Contracts/.publicApi/PublicAPI.Unshipped.txt
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/ApplyGuardrailData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/ApplyGuardrailData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/BaseData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/BaseData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ApplyGuardrailDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/ApplyGuardrailDataBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/BaseDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/BaseDataBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteInferenceDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/ExecuteInferenceDataBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/ExecuteToolDataBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/InvokeAgentDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/InvokeAgentDataBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/OutputDataBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/OutputDataBuilder.cs
R071	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/SpanStatusBuilder.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/SpanStatusBuilder.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/ExecuteInferenceData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/ExecuteInferenceData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/ExecuteToolData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/ExecuteToolData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/InvokeAgentData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/InvokeAgentData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/OutputData.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/OutputData.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/SpanKindConstants.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/SpanKindConstants.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/SpanStatus.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/SpanStatus.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/SpanStatusCode.cs	src/Microsoft.Agents.A365.Observability.Contracts/DTOs/SpanStatusCode.cs
A	src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj
A	src/Microsoft.Agents.A365.Observability.Contracts/Properties/AssemblyInfo.cs
R098	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/AgentDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/AgentDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/AgentType.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/AgentType.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/CallerDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/CallerDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GenAiRequestParameters.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GenAiRequestParameters.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GenAiResponseParameters.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GenAiResponseParameters.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GuardrailDecisionType.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GuardrailDecisionType.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GuardrailDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GuardrailDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GuardrailFinding.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GuardrailFinding.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GuardrailRiskSeverity.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GuardrailRiskSeverity.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/GuardrailTargetType.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/GuardrailTargetType.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/InferenceCallDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/InferenceCallDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/InferenceOperationType.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/InferenceOperationType.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/InvokeAgentDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/InvokeAgentDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Messages/MessageEnums.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Messages/MessageEnums.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Messages/MessageParts.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Messages/MessageParts.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Messages/MessageTypes.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Messages/MessageTypes.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Messages/MessageWrappers.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Messages/MessageWrappers.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/OperationSource.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/OperationSource.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Request.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Request.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Response.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Response.cs
R090	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/SpanDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/SpanDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ThreatDiagnosticsSummary.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/ThreatDiagnosticsSummary.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolCallDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/ToolCallDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolType.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/ToolType.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallArguments.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Tools/ExecuteToolCallArguments.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallResult.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/Tools/ExecuteToolCallResult.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/UserDetails.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/UserDetails.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/MessageUtils.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/MessageUtils.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/AutoInstrumentationConstants.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Scopes/AutoInstrumentationConstants.cs
R100	src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs	src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Scopes/OpenTelemetryConstants.cs
M	src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Shipped.txt
M	src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt
A	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Microsoft.Agents.A365.Observability.Contracts.Tests.csproj
A	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Properties/AssemblyInfo.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/BaseDataTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/BaseDataTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/BaseDataBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/BaseDataBuilderTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteInferenceDataBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/ExecuteInferenceDataBuilderTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/InvokeAgentDataBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/InvokeAgentDataBuilderTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/OutputDataBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/OutputDataBuilderTests.cs
R089	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/SpanStatusBuilderTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/Builders/SpanStatusBuilderTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/ExecuteInferenceDataTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/ExecuteInferenceDataTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/ExecuteToolDataTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/ExecuteToolDataTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/InvokeAgentDataTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/InvokeAgentDataTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/OutputDataTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/DTOs/OutputDataTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ExecuteToolJsonModelsTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/Tracing/Contracts/ExecuteToolJsonModelsTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/Tracing/Contracts/ToolCallDetailsTests.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/MessageUtilsTest.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/Tracing/MessageUtilsTest.cs
R100	test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/MessageUtilsToolPayloadTests.cs	test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/Tracing/MessageUtilsToolPayloadTests.cs
```

## Decisions
- Created `src\Microsoft.Agents.A365.Observability.Contracts` as a standalone package targeting `netstandard2.0;net8.0` with package metadata, signing via repository-wide props, SourceLink, and public API analyzer tracking.
- Created `test\Microsoft.Agents.A365.Observability.Contracts.Tests` targeting `net8.0;net10.0` with MSTest and FluentAssertions, then moved the isolated DTO, builder, contract, and message utility tests into it.
- Moved DTOs, builders, tracing contracts, message serialization helpers, and shared tracing constants into the new Contracts assembly without changing namespaces.
- Split all DTO and tracing-contract API baselines from `src\Microsoft.OpenTelemetry\.publicApi` into the new Contracts `.publicApi` files while preserving the `#nullable enable` headers.
- Replaced the previous compile-time `Azure.RequestFailedException` dependency in `SpanStatusBuilder` with full-name/status-property detection so the Contracts package stays free of Azure dependencies while preserving the existing error-type behavior for Azure request failures.
- Removed XML doc references to runtime-only helpers (`BaggageBuilder`, `TraceContextHelper`, `OpenTelemetryScope`) so the standalone Contracts build remains self-contained.

## Exact commands and results
1. `dotnet restore test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj`
   - Result: restore **succeeded** under SDK `10.0.303`, but emitted `Skipping project ... Microsoft.Agents.A365.Observability.Contracts.csproj because it was not found.`
   - Note: this differed from the brief's expected hard failure.
2. `dotnet build test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release`
   - Result: build **succeeded** with `MSB9008` warnings that the referenced Contracts project did not exist yet.
   - Use: treated as the pre-production negative check because restore no longer fails hard on a missing project reference in this SDK.
3. `dotnet test test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release`
   - Result: **failed** initially with `CS0246` because extracted `SpanStatusBuilder` still had a compile-time `Azure` dependency.
4. `dotnet test test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release`
   - Result after fix: **passed**.
   - `net8.0`: Passed `147`, Failed `0`, Skipped `0`.
   - `net10.0`: Passed `147`, Failed `0`, Skipped `0`.
5. `dotnet build src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --configuration Release`
   - Result: **passed** for `net8.0` and `netstandard2.0` with `0 Warning(s)` and `0 Error(s)`.
6. Final fresh verification reruns:
   - `dotnet test test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release`
     - Passed `147/147` on `net8.0` and `147/147` on `net10.0`.
   - `dotnet build src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --configuration Release`
     - Passed on `net8.0` and `netstandard2.0` with `0 warnings`, `0 errors`.
   - `git status --short`
     - Result: clean working tree after commit.

## Self-review
- Correctness: checked project metadata, moved-file coverage, API baseline split, and the Azure-free `SpanStatusBuilder` behavior; no blocking correctness issue found in the Task 1 scope.
- Edge cases: verified the Azure request-failure special-case still has targeted test coverage without introducing a new package dependency.
- Test coverage: confirmed the moved Contracts suite catches the extraction-specific dependency bug first, then passes green across both test TFMs.
- Security: no new untrusted-input or secret-handling paths were introduced; change is assembly extraction and packaging only.
- Pattern match: followed existing repository packaging/signing/API baseline patterns and reused the repository signing keys for internals visibility.
- Blast radius: high for later integration, but intentionally limited here to the standalone Contracts package and its isolated tests; distro rewiring remains for later tasks.

## Concerns
- The brief expected `dotnet restore` to fail before the Contracts production project existed, but SDK `10.0.303` only skipped the missing project and restored successfully; I documented and compensated with a negative build check.
- This commit is task-scoped by plan design. I did **not** run a full `Microsoft.OpenTelemetry` solution build because later tasks own ETW extraction and distro rewiring; until those tasks are complete, the umbrella package is not expected to be fully rewired.
- The self-review was same-session rather than an independent reviewer pass.

---

## Task 1 review follow-up - umbrella solution build fix

### Root cause
- The extracted Contracts assembly existed, but `src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj` did not reference `src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj`, so the umbrella solution could not resolve the moved DTO and tracing contract types at the Task 1 commit.
- Once that project reference was restored, `OpenTelemetryConstants` remained intentionally non-public in the extracted assembly, so the main distro assembly and the Agent365 test assembly also needed internal visibility to preserve the existing Task 1 surface without pulling Task 3 compatibility work forward.

### Fix applied
- Added the minimal `ProjectReference` from `src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj` to the extracted Contracts project.
- Extended `src\Microsoft.Agents.A365.Observability.Contracts\Properties\AssemblyInfo.cs` with `InternalsVisibleTo` entries for `Microsoft.OpenTelemetry` and `Microsoft.OpenTelemetry.Agent365.Tests`.
- Renamed the Contracts signing helper type from `AssemblyInfo` to `ContractsAssemblyInfo` to avoid an internal-type name collision after granting that visibility.

### Brief deviation
- The brief's expected pre-project `dotnet restore` hard failure is not reproducible on SDK `10.0.303`; restore skips the missing project instead of failing. No product-code workaround was needed, and this follow-up kept that documented deviation intact.

### Verification commands and results
1. `dotnet build Microsoft.OpenTelemetry.slnx --configuration Release`
   - Result after follow-up fix: **passed** with `38 Warning(s)` and `0 Error(s)`.
2. `dotnet test test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release`
   - Result: **passed**.
   - `net8.0`: Passed `147`, Failed `0`, Skipped `0`.
   - `net10.0`: Passed `147`, Failed `0`, Skipped `0`.

### Fix commit SHA
- `2ed01a2ebdc5ad9b39dce4a0b38a736c83f76915` — `fix: restore Contracts solution reference`
