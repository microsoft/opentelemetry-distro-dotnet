# Standalone Agent365 ETW SDK Design

## Goal

Create a lightweight, standalone Agent365 ETW SDK from the existing repository while preserving the current distro as an umbrella package. The standalone package must support BotDesigner's existing event-building flow without bringing in Azure Monitor, authentication, MSAL, durable storage, or HTTP export dependencies.

The first extraction prioritizes existing contracts and wire compatibility. Redesigning the long logger method signatures is explicitly deferred.

## Current usage

The current `Microsoft.OpenTelemetry` project owns all Agent365 contracts, DTO builders, formatting, ETW emission, OpenTelemetry adapters, and HTTP export code in one assembly.

BotDesigner currently:

1. Creates an Agent365 event using an existing builder such as `InvokeAgentDataBuilder`, `ExecuteToolDataBuilder`, `OutputDataBuilder`, `ExecuteInferenceDataBuilder`, or `ApplyGuardrailDataBuilder`.
2. Mutates the resulting DTO to add routing attributes and status information.
3. Calls `ToDictionary()`.
4. Calls `ExportFormatter.FormatLogData(...)`.
5. Calls `EtwEventSource.Log.LogJson(...)` through a local `IAgent365EtwLogger` wrapper.

BotDesigner does not use `ExportFormatter.FormatMany(...)` or `ExportFormatter.FormatSingle(...)`.

## Project and package structure

The repository will produce three projects, assemblies, and NuGet packages.

### Contracts

Project and package:

`Microsoft.Agents.A365.Observability.Contracts`

This project owns:

- Existing Agent365 tracing contracts, including message and tool contracts.
- Existing event DTOs.
- Existing DTO builders.
- Message serialization utilities required by the builders.
- Schema constants required by the DTOs and builders.

It targets `netstandard2.0` and `net8.0`.

### ETW

Project and package:

`Microsoft.Agents.A365.Observability.Etw`

This project references the Contracts project and owns:

- `EtwEventSource`.
- A new `EtwExportFormatter`.
- The existing `FormatLogData(IDictionary<string, object?>)` behavior moved from `ExportFormatter` into `EtwExportFormatter`.
- ETW-specific tests and documentation.

It targets `netstandard2.0` and `net8.0`.

The provider name, event IDs, event levels, opcodes, messages, JSON property names, timestamp conversion, default span kind, and default status payload remain unchanged.

### Distro

Existing project and package:

`Microsoft.OpenTelemetry`

The distro references both Contracts and ETW. It remains the umbrella package and preserves its existing Agent365, ETW, Azure Monitor, OTLP, console, and instrumentation functionality.

The existing `ExportFormatter` remains in the distro for `FormatMany(...)` and `FormatSingle(...)`. Its `FormatLogData(...)` implementation moves to `EtwExportFormatter`, and distro ETW log call sites use the new class.

To preserve the shipped distro API, `ExportFormatter.FormatLogData(...)` remains as an obsolete forwarding method that delegates to `EtwExportFormatter`. The standalone ETW package does not depend on the distro.

## Dependency graph

```text
Microsoft.Agents.A365.Observability.Contracts
    no dependency on the ETW or distro packages

Microsoft.Agents.A365.Observability.Etw
    -> Microsoft.Agents.A365.Observability.Contracts

Microsoft.OpenTelemetry
    -> Microsoft.Agents.A365.Observability.Contracts
    -> Microsoft.Agents.A365.Observability.Etw
```

NuGet consumers of `Microsoft.OpenTelemetry` continue to add one package reference. NuGet restores and deploys the three assemblies transitively:

```text
Microsoft.OpenTelemetry.dll
Microsoft.Agents.A365.Observability.Etw.dll
Microsoft.Agents.A365.Observability.Contracts.dll
```

BotDesigner references `Microsoft.Agents.A365.Observability.Etw` directly and receives Contracts transitively.

## BotDesigner migration

BotDesigner retains its existing builders, DTO mutation, exclusions, truncation, metrics, feature flags, and ETW error handling.

The required code migration is:

```csharp
private readonly EtwExportFormatter _exportFormatter = new();
```

Existing calls remain structurally equivalent:

```csharp
var jsonContent = _exportFormatter.FormatLogData(eventData.ToDictionary());
EtwEventSource.Log.LogJson(jsonContent);
```

BotDesigner changes its package reference from the broader Agent365 runtime package to `Microsoft.Agents.A365.Observability.Etw`.

## Assembly and API compatibility

Moving public types between assemblies changes their CLR type identity even when namespaces and type names remain unchanged.

Source consumers that rebuild against the new packages retain the same contract and builder usage. The distro uses type forwarding for every shipped public type moved to Contracts or ETW so already-compiled consumers continue resolving those types. Compatibility verification must include loading an application compiled against the previous distro version.

The standalone packages preserve existing namespaces for moved contracts, DTOs, builders, and `EtwEventSource`.

## Error handling

`EtwEventSource` continues using `EventSourceSettings.ThrowOnEventWriteErrors`. Oversized or invalid ETW writes therefore remain observable as exceptions.

The SDK does not swallow ETW write failures. Host applications such as BotDesigner may continue catching failures to protect fire-and-forget audit paths and record host-specific metrics.

`EtwExportFormatter` validates required dictionary keys consistently with the current `FormatLogData(...)` behavior. It does not silently substitute malformed event data beyond the existing default span kind and status behavior.

## Build and packaging

Both new projects are added to `Microsoft.OpenTelemetry.slnx`, so existing solution restore, build, and test commands include them.

The GitHub Actions packaging job must pack all three projects. Its artifact upload wildcard can continue uploading all generated `.nupkg` and symbol packages.

Package publication occurs outside the current GitHub Actions workflows. The publishing system must publish packages in dependency order:

1. Contracts
2. ETW
3. Microsoft.OpenTelemetry

All three packages require coordinated compatible versions. The distro package dependency metadata must reference the released Contracts and ETW versions.

## Testing

### Contracts tests

- Existing builder tests move with the Contracts project.
- Required and optional attributes remain unchanged.
- Structured message and tool payload serialization remains unchanged.
- Status and error mapping remains unchanged.

### ETW tests

- Provider name remains `A365-O11y-EventSource`.
- Event IDs `1000` and `2000`, event levels, opcodes, and payload ordering remain unchanged.
- `EtwExportFormatter` output matches the current `ExportFormatter.FormatLogData(...)` output for every event DTO.
- Default span kind and status behavior remain unchanged.
- Timestamp conversion remains unchanged.
- EventListener integration tests verify emitted payloads.
- Oversized ETW event behavior remains observable.

### Distro tests

- Agent365 HTTP export continues using `ExportFormatter.FormatMany(...)`.
- `EtwLogProcessor` uses `EtwExportFormatter.FormatLogData(...)`.
- `EtwScopeEventProcessor` continues using `ExportFormatter.FormatSingle(...)`.
- Existing umbrella-package setup continues enabling ETW functionality.
- A package-level integration test installs only `Microsoft.OpenTelemetry` and verifies all transitive assemblies are restored.
- Binary compatibility tests verify all type forwards for moved shipped public APIs.

### BotDesigner validation

- Build BotDesigner using only the standalone ETW package for Agent365 contracts and ETW emission.
- Run its existing five event-path tests: invoke agent, execute tool, output messages, inference, and apply guardrail.
- Verify emitted JSON remains wire-compatible with Iguana routing.

## Deferred enhancements

The following are intentionally outside the first extraction:

- Replacing long logger method signatures with request objects.
- Adding a generic `Emit(BaseData)` API.
- Adding fluent builders.
- Redesigning the Agent365 contracts.
- Combining the generated assemblies into a single DLL.
- Moving the projects to a separate repository.
