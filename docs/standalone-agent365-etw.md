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

After packing the three local packages into `.\packages`, validate both consumer shapes against NuGet artifacts instead of project references:

```powershell
dotnet restore test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json
dotnet build test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --no-restore --configuration Release
dotnet restore test\package-smoke\DistroConsumer\DistroConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json
dotnet build test\package-smoke\DistroConsumer\DistroConsumer.csproj --no-restore --configuration Release
```

Expected results:

- `StandaloneEtwConsumer` builds with `Microsoft.Agents.A365.Observability.Etw` plus the transitive Contracts assembly, without a `Microsoft.OpenTelemetry.dll` dependency.
- `DistroConsumer` builds with only the `Microsoft.OpenTelemetry` package reference while still resolving `EtwEventSource` through the distro's type forwards.
