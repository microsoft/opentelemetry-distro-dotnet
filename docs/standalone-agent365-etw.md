# Standalone Agent365 ETW package

Use `Microsoft.Agents.A365.Observability.Etw` when you only need the standalone ETW formatter/event source surface. Use `Microsoft.OpenTelemetry` when you want the full distro with exporters, hosting integration, and type-forwarded Agent365 ETW APIs.

## Package selection

### Standalone ETW consumers

```xml
<PackageReference Include="Microsoft.Agents.A365.Observability.Etw" Version="<latest>" />
```

The ETW package brings `Microsoft.Agents.A365.Observability.Contracts` transitively, so standalone consumers do not need a separate Contracts package reference.

### Distro consumers

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="<latest>" />
```

The distro package forwards `EtwEventSource` and the Agent365 DTO/contracts types, so distro consumers do not need a separate ETW package reference.

## Manual ETW payload formatting

```csharp
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;

private readonly EtwExportFormatter _exportFormatter = new();

var eventData = InvokeAgentDataBuilder.Build(
    invokeAgentScopeDetails,
    agentDetails,
    conversationId,
    request,
    callerDetails,
    inputMessages,
    outputMessages,
    startTime,
    endTime,
    spanId,
    parentSpanId,
    extraAttributes: extraAttributes,
    traceId: traceId);

var jsonContent = _exportFormatter.FormatLogData(eventData.ToDictionary());
EtwEventSource.Log.LogJson(jsonContent);
```

## BotDesigner migration

BotDesigner only needs the standalone ETW package.

1. In `/src/Infrastructure/Common/Microsoft.CCI.Common/Microsoft.CCI.Common.csproj`, replace `Microsoft.Agents.A365.Observability.Runtime` with `Microsoft.Agents.A365.Observability.Etw`:

   ```xml
   <PackageReference Include="Microsoft.Agents.A365.Observability.Etw" Version="<latest>" />
   ```

2. In `/src/Infrastructure/Common/Microsoft.CCI.Common/Logging/A365Observability/A365ObservabilityEtwLogger.cs`, replace `ExportFormatter` with `EtwExportFormatter` so the formatter field becomes:

   ```csharp
   private readonly EtwExportFormatter _exportFormatter = new();
   ```

3. Keep using `InvokeAgentDataBuilder` and the other DTO builders through the ETW package's transitive Contracts dependency; no project references are required.
4. Build the BotDesigner `Microsoft.CCI.Common` project.
5. Run `A365ObservabilityEtwLoggerTests` and `A365ObservabilityRequiredFieldsTests`.
6. Verify invoke-agent, execute-tool, output-messages, inference, and apply-guardrail JSON payload snapshots remain unchanged.

## Local smoke validation

The three packages ship as a coordinated version set (see [Package version coupling](#package-version-coupling)). Pack them with a unique local prerelease version so the smoke consumers cannot silently resolve an already-published package from nuget.org, then restore with that exact version:

```powershell
$version = "1.2.0-local.$(Get-Date -Format yyyyMMddHHmmss)"

dotnet build Microsoft.OpenTelemetry.slnx --configuration Release -p:Version=$version
dotnet pack src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --no-build --configuration Release --output .\packages -p:Version=$version
dotnet pack src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj --no-build --configuration Release --output .\packages -p:Version=$version
dotnet pack src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-build --configuration Release --output .\packages -p:Version=$version

dotnet restore test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json --no-cache -p:SmokePackageVersion=$version
dotnet build test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --no-restore --configuration Release -p:SmokePackageVersion=$version
dotnet restore test\package-smoke\DistroConsumer\DistroConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json --no-cache -p:SmokePackageVersion=$version
dotnet build test\package-smoke\DistroConsumer\DistroConsumer.csproj --no-restore --configuration Release -p:SmokePackageVersion=$version
```

The smoke projects pin an exact version range (`[$(SmokePackageVersion)]`), and `SmokePackageVersion` defaults to the repo's `A365ObservabilityPackageVersion`. nuget.org stays in the source list so external transitive dependencies still resolve.

Expected results:

- `StandaloneEtwConsumer` builds with `Microsoft.Agents.A365.Observability.Etw` plus the transitive Contracts assembly, without a `Microsoft.OpenTelemetry.dll` dependency.
- `DistroConsumer` builds with only the `Microsoft.OpenTelemetry` package reference while still resolving `EtwEventSource` through the distro's type forwards.
- Both `project.assets.json` files resolve the Agent365 observability packages at exactly the locally packed version.

## Package version coupling

`Microsoft.OpenTelemetry` and `Microsoft.Agents.A365.Observability.Etw` compile against `internal` members of `Microsoft.Agents.A365.Observability.Contracts` (for example `OpenTelemetryConstants`, `AutoInstrumentationConstants`, `MessageUtils`, and `SpanKindConstants`), granted through `InternalsVisibleTo`. `ProjectReference`-based packing emits an inclusive-minimum dependency (`>= <version>`) rather than an exact range, so a consumer could in principle float Contracts ahead of the assembly that was compiled against it.

The repository handles this by treating the three packages as one coordinated version set:

- `A365ObservabilityPackageVersion` in `Directory.Build.props` drives the version of all three packages, so they always pack in lockstep from the same commit.
- CI packs and validates them together with a single version override.

Publish the three packages together and at the same version. Making the shared internals public purely to express an exact dependency range would expand the supported public API surface, so this coupling is documented and tracked as a follow-up instead.
