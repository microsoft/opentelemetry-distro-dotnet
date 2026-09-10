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

The three packages ship as a coordinated version set (see [Package version coupling](#package-version-coupling)).

A **release pack** produces the artifacts that would actually be published, at the repository version (`A365ObservabilityPackageVersion`, currently `1.2.0`):

```powershell
dotnet build Microsoft.OpenTelemetry.slnx --configuration Release
dotnet pack src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --no-build --configuration Release --output .\packages
dotnet pack src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj --no-build --configuration Release --output .\packages
dotnet pack src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-build --configuration Release --output .\packages
```

A **smoke pack** repacks the same build output into a *separate* directory under a unique local prerelease version, so the smoke consumers cannot silently resolve an already-published package from nuget.org. Restore the consumers only from that directory, at that exact version:

```powershell
$smokeVersion = "1.2.0-local.$(Get-Date -Format yyyyMMddHHmmss)"

dotnet pack src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --no-build --configuration Release --output .\smoke-packages -p:Version=$smokeVersion
dotnet pack src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj --no-build --configuration Release --output .\smoke-packages -p:Version=$smokeVersion
dotnet pack src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-build --configuration Release --output .\smoke-packages -p:Version=$smokeVersion

dotnet restore test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --source .\smoke-packages --source https://api.nuget.org/v3/index.json --no-cache -p:SmokePackageVersion=$smokeVersion
dotnet build test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --no-restore --configuration Release -p:SmokePackageVersion=$smokeVersion
dotnet restore test\package-smoke\DistroConsumer\DistroConsumer.csproj --source .\smoke-packages --source https://api.nuget.org/v3/index.json --no-cache -p:SmokePackageVersion=$smokeVersion
dotnet build test\package-smoke\DistroConsumer\DistroConsumer.csproj --no-restore --configuration Release -p:SmokePackageVersion=$smokeVersion
```

The smoke projects pin an exact version range (`[$(SmokePackageVersion)]`), and `SmokePackageVersion` defaults to the repo's `A365ObservabilityPackageVersion`. nuget.org stays in the source list so external transitive dependencies still resolve.

Expected results:

- `StandaloneEtwConsumer` builds with `Microsoft.Agents.A365.Observability.Etw` plus the transitive Contracts assembly, without a `Microsoft.OpenTelemetry.dll` dependency.
- `DistroConsumer` builds with only the `Microsoft.OpenTelemetry` package reference while still resolving `EtwEventSource` through the distro's type forwards.
- Both `project.assets.json` files resolve the Agent365 observability packages at exactly the smoke-packed version.

## Package version coupling

`Microsoft.OpenTelemetry` and `Microsoft.Agents.A365.Observability.Etw` compile against `internal` members of `Microsoft.Agents.A365.Observability.Contracts` (for example `OpenTelemetryConstants`, `AutoInstrumentationConstants`, `MessageUtils`, and `SpanKindConstants`), granted through `InternalsVisibleTo`. `ProjectReference`-based packing emits an inclusive-minimum dependency (`>= <version>`) rather than an exact range, so a consumer could in principle float Contracts ahead of the assembly that was compiled against it.

The repository handles this by treating the three packages as one coordinated version set:

- `A365ObservabilityPackageVersion` in `Directory.Build.props` drives the version of all three packages, so they always pack in lockstep from the same commit.
- Package and validate them together using the release and smoke-pack commands above.

Publish the three packages together and at the same version. On `netstandard2.0`/.NET Framework consumers, forcing these packages out of lockstep can additionally require `bindingRedirect` entries in `app.config`/`web.config`, because .NET Framework binds strong-named assemblies by exact version rather than rolling forward. Making the shared internals public purely to express an exact dependency range would expand the supported public API surface, so this coupling is documented and tracked as a follow-up instead.
