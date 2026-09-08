# Agent365 S2S Sample Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a self-contained .NET 8 console sample that acquires an Agent365 S2S token from app configuration and exports manual InvokeAgent, Inference, and ExecuteTool spans.

**Architecture:** A non-hosted `OpenTelemetrySdk` configures Console and Agent365 exporters against the repository's local distro project. A dedicated MSAL-based `S2STokenProvider` performs the blueprint-app to agent-instance to observability-token exchange, while `SampleScenario` emits deterministic manual A365 spans without Agents Framework, an LLM, or an external tool.

**Tech Stack:** .NET 8, Microsoft OpenTelemetry distro, Microsoft Agent365 observability scope APIs, OpenTelemetry .NET, MSAL.NET (`Microsoft.Identity.Client`), Microsoft.Extensions.Configuration, MSTest, FluentAssertions.

## Global Constraints

- The sample lives at `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo`.
- The sample is a one-shot console application; do not add ASP.NET or Agents Framework.
- Inference and tool execution are simulated; do not call an LLM or external tool.
- Use the Agent365 S2S endpoint and app-only token flow; there is no agentic user.
- Real credentials belong only in gitignored `appsettings.json`; tracked configuration contains placeholders.
- Never log access tokens, client assertions, client secrets, or complete authentication responses.
- The sample must force-flush traces before exiting.
- Follow TDD: run every specified test before and after its implementation.

---

## File Structure

### New sample files

- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj` — console project, local distro reference, MSAL/configuration dependencies, local config copying.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/SampleOptions.cs` — reads and validates the exact app configuration schema.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/TokenExchangeResult.cs` — token plus expiration value object.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/ITokenExchangeClient.cs` — testable boundary around MSAL network operations.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/MsalTokenExchangeClient.cs` — concrete two-request MSAL implementation.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/S2STokenProvider.cs` — tenant validation, per-agent cache, concurrency, and exporter resolver.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/SampleScenario.cs` — deterministic InvokeAgent/Inference/ExecuteTool/Inference trace.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/Program.cs` — loads configuration, wires telemetry, executes the scenario, and flushes.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/appsettings.example.json` — safe configuration template.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/.gitignore` — excludes real configuration and local telemetry storage.
- `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/README.md` — setup, run, expected logs, and troubleshooting.

### New test files

- `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj` — focused net8.0 test project.
- `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/TestData.cs` — shared valid sample options for tests.
- `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/SampleOptionsTests.cs` — configuration validation.
- `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/S2STokenProviderTests.cs` — flow, failures, caching, and concurrency.
- `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/SampleScenarioTests.cs` — span sequence and parentage.

### Existing files to modify

- `Directory.Packages.props` — centrally pin MSAL and configuration packages.
- `Microsoft.OpenTelemetry.slnx` — include the sample and its tests.
- `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Exporters/Agent365ExporterCore.cs` — log successful HTTP status and correlation ID.
- `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Exporters/Agent365ExporterTests.cs` — verify the success diagnostic.
- `README.md` — link the new example.

---

### Task 1: Scaffold the sample and validate configuration

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Microsoft.OpenTelemetry.slnx`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/SampleOptions.cs`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/appsettings.example.json`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/.gitignore`
- Create: `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj`
- Create: `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/TestData.cs`
- Create: `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/SampleOptionsTests.cs`

**Interfaces:**
- Produces: `SampleOptions.Load(IConfiguration) -> SampleOptions`
- Produces: `SampleOptions.Authority`, `ClientId`, `ClientSecret`, `TenantId`, `AgentAppInstanceId`, `AgentId`, and `ClusterCategory`

- [ ] **Step 1: Add the failing configuration tests**

Create `SampleOptionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class SampleOptionsTests
{
    [TestMethod]
    public void Load_ValidConfiguration_BindsValues()
    {
        var configuration = BuildConfiguration();

        var options = SampleOptions.Load(configuration);

        options.Authority.Should().Be(new Uri("https://login.microsoftonline.com"));
        options.ClientId.Should().Be("11111111-1111-1111-1111-111111111111");
        options.ClientSecret.Should().Be("sample-secret");
        options.TenantId.Should().Be("22222222-2222-2222-2222-222222222222");
        options.AgentAppInstanceId.Should().Be("33333333-3333-3333-3333-333333333333");
        options.AgentId.Should().Be("44444444-4444-4444-4444-444444444444");
        options.ClusterCategory.Should().Be("production");
    }

    [TestMethod]
    public void Load_MissingRequiredValue_ThrowsWithConfigurationKey()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Connections:ServiceConnection:Settings:ClientSecret"] = string.Empty,
        });

        var action = () => SampleOptions.Load(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connections:ServiceConnection:Settings:ClientSecret*");
    }

    [TestMethod]
    public void Load_UnchangedPlaceholder_ThrowsWithConfigurationKey()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Agent365:AgentAppInstanceId"] = "<agent-app-instance-id>",
        });

        var action = () => SampleOptions.Load(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Agent365:AgentAppInstanceId*");
    }

    [TestMethod]
    public void Load_MissingAgentId_DefaultsToInstanceId()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Agent365:AgentId"] = string.Empty,
        });

        var options = SampleOptions.Load(configuration);

        options.AgentId.Should().Be(options.AgentAppInstanceId);
    }

    private static IConfiguration BuildConfiguration(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Connections:ServiceConnection:Settings:AuthorityEndpoint"] = "https://login.microsoftonline.com",
            ["Connections:ServiceConnection:Settings:ClientId"] = "11111111-1111-1111-1111-111111111111",
            ["Connections:ServiceConnection:Settings:ClientSecret"] = "sample-secret",
            ["Connections:ServiceConnection:Settings:TenantId"] = "22222222-2222-2222-2222-222222222222",
            ["Agent365:AgentAppInstanceId"] = "33333333-3333-3333-3333-333333333333",
            ["Agent365:AgentId"] = "44444444-4444-4444-4444-444444444444",
            ["Agent365:ClusterCategory"] = "production",
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
```

- [ ] **Step 2: Add project/package entries and run the tests to verify RED**

Add these central package versions to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.Identity.Client" Version="4.84.2" />
<PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.2" />
<PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.2" />
```

Create the sample project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Microsoft.OpenTelemetry.Agent365.S2S.Demo</RootNamespace>
    <AssemblyName>Microsoft.OpenTelemetry.Agent365.S2S.Demo</AssemblyName>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj" />
    <PackageReference Include="Microsoft.Identity.Client" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
    <InternalsVisibleTo Include="Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests" />
  </ItemGroup>
</Project>
```

Create the test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj" />
  </ItemGroup>
</Project>
```

Add both projects to `Microsoft.OpenTelemetry.slnx`.

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~SampleOptionsTests
```

Expected: build failure because `SampleOptions` does not exist.

- [ ] **Step 3: Implement `SampleOptions`**

Create `SampleOptions.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed record SampleOptions(
    Uri Authority,
    string ClientId,
    string ClientSecret,
    string TenantId,
    string AgentAppInstanceId,
    string AgentId,
    string ClusterCategory)
{
    internal static SampleOptions Load(IConfiguration configuration)
    {
        var authorityText = Required(
            configuration,
            "Connections:ServiceConnection:Settings:AuthorityEndpoint");
        if (!Uri.TryCreate(authorityText, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Configuration key 'Connections:ServiceConnection:Settings:AuthorityEndpoint' must be an absolute HTTPS URI.");
        }

        var clientId = Required(
            configuration,
            "Connections:ServiceConnection:Settings:ClientId");
        var clientSecret = Required(
            configuration,
            "Connections:ServiceConnection:Settings:ClientSecret");
        var tenantId = RequiredGuid(
            configuration,
            "Connections:ServiceConnection:Settings:TenantId");
        var instanceId = RequiredGuid(
            configuration,
            "Agent365:AgentAppInstanceId");

        var configuredAgentId = configuration["Agent365:AgentId"]?.Trim();
        var agentId = string.IsNullOrWhiteSpace(configuredAgentId)
            ? instanceId
            : RejectPlaceholder("Agent365:AgentId", configuredAgentId);

        var clusterCategory = configuration["Agent365:ClusterCategory"]?.Trim();
        if (string.IsNullOrWhiteSpace(clusterCategory))
        {
            clusterCategory = "production";
        }

        return new SampleOptions(
            authority,
            clientId,
            clientSecret,
            tenantId,
            instanceId,
            agentId,
            clusterCategory);
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' is required in appsettings.json.");
        }

        return RejectPlaceholder(key, value);
    }

    private static string RequiredGuid(IConfiguration configuration, string key)
    {
        var value = Required(configuration, key);
        if (!Guid.TryParse(value, out _))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be a GUID.");
        }

        return value;
    }

    private static string RejectPlaceholder(string key, string value)
    {
        if (value.StartsWith('<') && value.EndsWith('>'))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' still contains the example placeholder '{value}'.");
        }

        return value;
    }
}
```

Create `appsettings.example.json`:

```json
{
  "Connections": {
    "ServiceConnection": {
      "Settings": {
        "AuthorityEndpoint": "https://login.microsoftonline.com",
        "ClientId": "<blueprint-app-client-id>",
        "ClientSecret": "<blueprint-app-client-secret>",
        "TenantId": "<tenant-id>"
      }
    }
  },
  "Agent365": {
    "AgentAppInstanceId": "<agent-app-instance-id>",
    "AgentId": "<onboarded-agent-id>",
    "ClusterCategory": "production"
  }
}
```

Create `.gitignore`:

```gitignore
appsettings.json
.otel/
```

Create `TestData.cs`:

```csharp
namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

internal static class TestData
{
    internal static SampleOptions CreateOptions() =>
        new(
            new Uri("https://login.microsoftonline.com"),
            "11111111-1111-1111-1111-111111111111",
            "sample-secret",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444",
            "production");
}
```

- [ ] **Step 4: Run the configuration tests to verify GREEN**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~SampleOptionsTests
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props Microsoft.OpenTelemetry.slnx examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests
git commit -m "test: scaffold Agent365 S2S sample configuration" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: b204ed95-85a0-406f-a7ef-261b2462a367"
```

---

### Task 2: Implement the S2S token provider

**Files:**
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/TokenExchangeResult.cs`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/ITokenExchangeClient.cs`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/MsalTokenExchangeClient.cs`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/S2STokenProvider.cs`
- Create: `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/S2STokenProviderTests.cs`

**Interfaces:**
- Consumes: `SampleOptions`
- Produces: `ITokenExchangeClient.AcquireBlueprintTokenAsync(SampleOptions, CancellationToken)`
- Produces: `ITokenExchangeClient.AcquireObservabilityTokenAsync(SampleOptions, string, CancellationToken)`
- Produces: `S2STokenProvider.ResolveAsync(string, string, CancellationToken) -> Task<string?>`

- [ ] **Step 1: Write failing token-provider tests**

Create `S2STokenProviderTests.cs`:

```csharp
using FluentAssertions;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class S2STokenProviderTests
{
[TestMethod]
public async Task ResolveAsync_ValidRequest_PerformsBothExchanges()
{
    var client = new FakeTokenExchangeClient();
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, TimeProvider.System);

    var token = await provider.ResolveAsync(
        options.AgentId,
        options.TenantId);

    token.Should().Be("observability-token-1");
    client.BlueprintCalls.Should().Be(1);
    client.ObservabilityCalls.Should().Be(1);
    client.LastClientAssertion.Should().Be("blueprint-token");
}

[TestMethod]
public async Task ResolveAsync_DifferentTenant_ThrowsBeforeTokenAcquisition()
{
    var client = new FakeTokenExchangeClient();
    var provider = new S2STokenProvider(
        TestData.CreateOptions(),
        client,
        TimeProvider.System);

    var action = () => provider.ResolveAsync(
        "44444444-4444-4444-4444-444444444444",
        "55555555-5555-5555-5555-555555555555");

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*does not match configured tenant*");
    client.BlueprintCalls.Should().Be(0);
}

[TestMethod]
public async Task ResolveAsync_UnexpiredToken_ReturnsCachedToken()
{
    var client = new FakeTokenExchangeClient();
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, TimeProvider.System);

    var first = await provider.ResolveAsync(options.AgentId, options.TenantId);
    var second = await provider.ResolveAsync(options.AgentId, options.TenantId);

    second.Should().Be(first);
    client.BlueprintCalls.Should().Be(1);
    client.ObservabilityCalls.Should().Be(1);
}

[TestMethod]
public async Task ResolveAsync_ConcurrentRequests_PerformOneExchange()
{
    var client = new FakeTokenExchangeClient(delay: TimeSpan.FromMilliseconds(50));
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, TimeProvider.System);

    var tokens = await Task.WhenAll(
        Enumerable.Range(0, 8)
            .Select(_ => provider.ResolveAsync(options.AgentId, options.TenantId)));

    tokens.Should().OnlyContain(token => token == "observability-token-1");
    client.BlueprintCalls.Should().Be(1);
    client.ObservabilityCalls.Should().Be(1);
}

[TestMethod]
public async Task ResolveAsync_TokenWithinRefreshBuffer_AcquiresNewToken()
{
    var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    var timeProvider = new TestTimeProvider(now);
    var client = new FakeTokenExchangeClient
    {
        ObservabilityExpiresOn = now.AddMinutes(2),
    };
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, timeProvider);

    var first = await provider.ResolveAsync(options.AgentId, options.TenantId);
    timeProvider.Advance(TimeSpan.FromSeconds(61));
    client.ObservabilityExpiresOn = now.AddHours(1);
    var second = await provider.ResolveAsync(options.AgentId, options.TenantId);

    first.Should().Be("observability-token-1");
    second.Should().Be("observability-token-2");
    client.ObservabilityCalls.Should().Be(2);
}

[TestMethod]
public async Task ResolveAsync_BlueprintFailure_PreservesStageMessage()
{
    var client = new FakeTokenExchangeClient
    {
        BlueprintException = new InvalidOperationException(
            "The blueprint app token exchange failed (invalid_client)."),
    };
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, TimeProvider.System);

    var action = () => provider.ResolveAsync(options.AgentId, options.TenantId);

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*blueprint app token exchange failed*");
}

[TestMethod]
public async Task ResolveAsync_ObservabilityFailure_PreservesStageMessage()
{
    var client = new FakeTokenExchangeClient
    {
        ObservabilityException = new InvalidOperationException(
            "The Agent365 observability token exchange failed (invalid_scope)."),
    };
    var options = TestData.CreateOptions();
    var provider = new S2STokenProvider(options, client, TimeProvider.System);

    var action = () => provider.ResolveAsync(options.AgentId, options.TenantId);

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*Agent365 observability token exchange failed*");
}

private sealed class FakeTokenExchangeClient : ITokenExchangeClient
{
    private readonly TimeSpan _delay;
    private int _blueprintCalls;
    private int _observabilityCalls;

    internal FakeTokenExchangeClient(TimeSpan? delay = null)
    {
        _delay = delay ?? TimeSpan.Zero;
    }

    internal int BlueprintCalls => _blueprintCalls;

    internal int ObservabilityCalls => _observabilityCalls;

    internal string? LastClientAssertion { get; private set; }

    internal DateTimeOffset ObservabilityExpiresOn { get; set; } =
        DateTimeOffset.UtcNow.AddHours(1);

    internal Exception? BlueprintException { get; init; }

    internal Exception? ObservabilityException { get; init; }

    public async Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _blueprintCalls);
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken);
        }

        if (BlueprintException is not null)
        {
            throw BlueprintException;
        }

        return new TokenExchangeResult(
            "blueprint-token",
            DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _observabilityCalls);
        LastClientAssertion = clientAssertion;
        if (ObservabilityException is not null)
        {
            throw ObservabilityException;
        }

        return Task.FromResult(
            new TokenExchangeResult(
                $"observability-token-{call}",
                ObservabilityExpiresOn));
    }
}

private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    internal void Advance(TimeSpan duration) => _utcNow += duration;
}
}
```

- [ ] **Step 2: Run the token tests to verify RED**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~S2STokenProviderTests
```

Expected: build failure because token-provider types do not exist.

- [ ] **Step 3: Add the token exchange contracts**

Create `TokenExchangeResult.cs`:

```csharp
namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed record TokenExchangeResult(
    string AccessToken,
    DateTimeOffset ExpiresOn);
```

Create `ITokenExchangeClient.cs`:

```csharp
namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal interface ITokenExchangeClient
{
    Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken);

    Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement the MSAL client**

Create `MsalTokenExchangeClient.cs`:

```csharp
using Microsoft.Identity.Client;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class MsalTokenExchangeClient : ITokenExchangeClient
{
    private static readonly string[] TokenExchangeScopes =
        ["api://AzureAdTokenExchange/.default"];

    private static readonly string[] ObservabilityScopes =
        ["api://9b975845-388f-4429-889e-eab1ef63949c/.default"];

    public async Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithClientSecret(options.ClientSecret)
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(TokenExchangeScopes)
                .WithFmiPath(options.AgentAppInstanceId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw new InvalidOperationException(
                $"The blueprint app token exchange failed ({exception.ErrorCode}): {exception.Message}",
                exception);
        }
    }

    public async Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.AgentAppInstanceId)
                .WithClientAssertion(_ => Task.FromResult(clientAssertion))
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(ObservabilityScopes)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw new InvalidOperationException(
                $"The Agent365 observability token exchange failed ({exception.ErrorCode}): {exception.Message}",
                exception);
        }
    }

    private static string BuildAuthority(SampleOptions options) =>
        $"{options.Authority.AbsoluteUri.TrimEnd('/')}/{options.TenantId}";
}
```

- [ ] **Step 5: Implement caching and concurrency in `S2STokenProvider`**

Create `S2STokenProvider.cs`:

```csharp
using System.Collections.Concurrent;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class S2STokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(1);

    private readonly SampleOptions _options;
    private readonly ITokenExchangeClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    internal S2STokenProvider(
        SampleOptions options,
        ITokenExchangeClient client,
        TimeProvider timeProvider)
    {
        _options = options;
        _client = client;
        _timeProvider = timeProvider;
    }

    internal async Task<string?> ResolveAsync(
        string agentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(tenantId, _options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The exporter requested tenant '{tenantId}', which does not match configured tenant '{_options.TenantId}'.");
        }

        var cacheEntry = _cache.GetOrAdd(
            $"{tenantId}:{agentId}",
            static _ => new CacheEntry());

        var cachedToken = cacheEntry.Token;
        if (IsUsable(cachedToken))
        {
            return cachedToken!.AccessToken;
        }

        await cacheEntry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedToken = cacheEntry.Token;
            if (IsUsable(cachedToken))
            {
                return cachedToken!.AccessToken;
            }

            var blueprintToken = await _client
                .AcquireBlueprintTokenAsync(_options, cancellationToken)
                .ConfigureAwait(false);
            var observabilityToken = await _client
                .AcquireObservabilityTokenAsync(
                    _options,
                    blueprintToken.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);

            cacheEntry.Token = observabilityToken;
            return observabilityToken.AccessToken;
        }
        finally
        {
            cacheEntry.Gate.Release();
        }
    }

    private bool IsUsable(TokenExchangeResult? token) =>
        token is not null
        && token.ExpiresOn > _timeProvider.GetUtcNow().Add(RefreshBuffer);

    private sealed class CacheEntry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        internal TokenExchangeResult? Token { get; set; }
    }
}
```

- [ ] **Step 6: Run the token tests to verify GREEN**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~S2STokenProviderTests
```

Expected: all token-provider tests pass.

- [ ] **Step 7: Commit**

```powershell
git add examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests
git commit -m "feat: add Agent365 S2S token provider" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: b204ed95-85a0-406f-a7ef-261b2462a367"
```

---

### Task 3: Add the manual Agent365 span scenario

**Files:**
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/SampleScenario.cs`
- Create: `test/Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests/SampleScenarioTests.cs`

**Interfaces:**
- Consumes: `SampleOptions`
- Produces: `SampleScenario.RunAsync(SampleOptions, Func<TimeSpan, CancellationToken, Task>?, CancellationToken) -> Task`

- [ ] **Step 1: Write the failing span-sequence test**

Create `SampleScenarioTests.cs`:

```csharp
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class SampleScenarioTests
{
    [TestMethod]
    public async Task RunAsync_EmitsExpectedAgent365SpanSequence()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryConstants.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await SampleScenario.RunAsync(
            TestData.CreateOptions(),
            static (_, _) => Task.CompletedTask);

        stopped.Should().HaveCount(4);
        stopped.Select(activity => activity.GetTagItem("gen_ai.operation.name"))
            .Should().ContainInOrder("Chat", "execute_tool", "Chat", "invoke_agent");

        var invoke = stopped.Single(
            activity => Equals(activity.GetTagItem("gen_ai.operation.name"), "invoke_agent"));
        stopped.Where(activity => activity != invoke)
            .Should().OnlyContain(activity => activity.ParentSpanId == invoke.SpanId);
    }
}
```

- [ ] **Step 2: Run the scenario test to verify RED**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~SampleScenarioTests
```

Expected: build failure because `SampleScenario` does not exist.

- [ ] **Step 3: Implement `SampleScenario`**

Create `SampleScenario.cs`:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal static class SampleScenario
{
    internal static async Task RunAsync(
        SampleOptions options,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= static (duration, token) => Task.Delay(duration, token);

        const string question = "What is the weather in Seattle?";
        const string finalAnswer = "It is currently 62°F and partly cloudy in Seattle.";
        const string sessionId = "session-s2s-123";
        const string conversationId = "conversation-s2s-789";

        var agent = new AgentDetails(
            agentId: options.AgentId,
            agentName: "Weather Agent",
            agentDescription: "Answers weather-related questions",
            agentBlueprintId: options.ClientId,
            tenantId: options.TenantId,
            providerName: "openai",
            agentVersion: "1.0.0");

        var request = new Request(
            content: question,
            sessionId: sessionId,
            channel: new Channel("service"),
            conversationId: conversationId,
            operationSource: "SDK");

        using var baggage = new BaggageBuilder()
            .TenantId(options.TenantId)
            .AgentId(options.AgentId)
            .AgentName(agent.AgentName)
            .AgentDescription(agent.AgentDescription)
            .AgentBlueprintId(options.ClientId)
            .ChannelName("service")
            .SessionId(sessionId)
            .ConversationId(conversationId)
            .Build();

        using var invoke = InvokeAgentScope.Start(
            request,
            new InvokeAgentScopeDetails(
                new Uri("https://weather-agent.contoso.com")),
            agent);

        using (var inference = InferenceScope.Start(
            request,
            new InferenceCallDetails(
                InferenceOperationType.Chat,
                "gpt-4o",
                "openai"),
            agent))
        {
            inference.RecordInputMessages(
                ["You are a helpful weather assistant.", question]);
            await delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
            inference.RecordInputTokens(45);
            inference.RecordOutputTokens(12);
            inference.RecordFinishReasons(["tool_call"]);
            inference.RecordOutputMessages(
                ["I will look up the weather for Seattle."]);
        }

        using (var tool = ExecuteToolScope.Start(
            request,
            new ToolCallDetails(
                "get_weather",
                new Dictionary<string, object>
                {
                    ["city"] = "Seattle",
                    ["units"] = "fahrenheit",
                },
                toolCallId: "call-abc123",
                description: "Fetches current weather for a city",
                toolType: "function",
                endpoint: new Uri("https://weather-api.contoso.com")),
            agent))
        {
            await delay(TimeSpan.FromMilliseconds(20), cancellationToken)
                .ConfigureAwait(false);
            tool.RecordResponse(new Dictionary<string, object>
            {
                ["temperature"] = 62,
                ["condition"] = "Partly cloudy",
            });
        }

        using (var inference = InferenceScope.Start(
            request,
            new InferenceCallDetails(
                InferenceOperationType.Chat,
                "gpt-4o",
                "openai",
                inputTokens: 80,
                outputTokens: 25,
                finishReasons: ["stop"]),
            agent))
        {
            await delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
            inference.RecordOutputMessages([finalAnswer]);
        }

        invoke.RecordResponse(finalAnswer);
    }
}
```

- [ ] **Step 4: Run the scenario test to verify GREEN**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj --filter FullyQualifiedName~SampleScenarioTests
```

Expected: the single scenario test passes and captures four spans.

- [ ] **Step 5: Commit**

```powershell
git add examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests
git commit -m "feat: emit manual Agent365 S2S spans" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: b204ed95-85a0-406f-a7ef-261b2462a367"
```

---

### Task 4: Surface export results and wire the executable

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Exporters/Agent365ExporterCore.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Exporters/Agent365ExporterTests.cs`
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/Program.cs`

**Interfaces:**
- Consumes: `SampleOptions.Load`, `MsalTokenExchangeClient`, `S2STokenProvider.ResolveAsync`, `SampleScenario.RunAsync`
- Produces: a runnable one-shot sample with deterministic exit codes

- [ ] **Step 1: Write the failing exporter success-log test**

Add to `Agent365ExporterTests.cs`:

```csharp
[TestMethod]
public void Export_Http200_LogsStatusAndCorrelationId()
{
    var handler = new TestHttpMessageHandler(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("x-ms-correlation-id", "success-correlation-id");
        return response;
    });
    var options = new Agent365ExporterOptions
    {
        TokenResolver = (_, _) => Task.FromResult<string?>("test-token"),
        UseS2SEndpoint = true,
        DomainResolver = _ => "test.example.com",
    };
    var logger = new InMemoryLogger();
    var core = new Agent365ExporterCore(
        new ExportFormatter(NullLogger<ExportFormatter>.Instance),
        logger);
    var exporter = new Agent365Exporter(
        core,
        NullLogger<Agent365Exporter>.Instance,
        options,
        ResourceBuilder.CreateEmpty().AddService("test").Build(),
        new HttpClient(handler));
    using var activity = CreateActivity("tenant-200", "agent-200");
    var batch = CreateBatch(activity);

    var result = exporter.Export(in batch);

    result.Should().Be(ExportResult.Success);
    logger.LogMessages.Should().ContainSingle(message =>
        message.Contains("HTTP 200")
        && message.Contains("success-correlation-id"));
}
```

- [ ] **Step 2: Run the success-log test to verify RED**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~Export_Http200_LogsStatusAndCorrelationId
```

Expected: failure because no successful-response message is logged.

- [ ] **Step 3: Log successful status and correlation ID**

In `Agent365ExporterCore`, read the correlation header before the status branch and add:

```csharp
var correlationId = response.Headers.Contains(CorrelationIdHeaderKey)
    ? response.Headers.GetValues(CorrelationIdHeaderKey).FirstOrDefault()
    : null;

if (response.IsSuccessStatusCode)
{
    DistroNetworkSdkStats.Instance?.TrackResponse(
        requestHost,
        (int)response.StatusCode,
        stopwatch.Elapsed.TotalMilliseconds);
    _logger?.LogInformation(
        "Agent365ExporterCore: HTTP {StatusCode} success for chunk {ChunkIndex} of {ChunkCount}. Correlation ID: {CorrelationId}.",
        (int)response.StatusCode,
        chunkIndex,
        chunkCount,
        correlationId ?? "N/A");
    return new Agent365SendOutcome(Agent365SendDisposition.Delivered, null);
}
```

Remove the duplicate correlation-header extraction from the non-success branch.

- [ ] **Step 4: Run the exporter test to verify GREEN**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~Export_Http200_LogsStatusAndCorrelationId
```

Expected: 1 test passes.

- [ ] **Step 5: Implement `Program.cs`**

Create `Program.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            var configuration = LoadConfiguration();
            var options = SampleOptions.Load(configuration);
            var tokenProvider = new S2STokenProvider(
                options,
                new MsalTokenExchangeClient(),
                TimeProvider.System);

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.Services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSimpleConsole(console =>
                    {
                        console.SingleLine = true;
                        console.TimestampFormat = "HH:mm:ss ";
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                });

                otel.UseMicrosoftOpenTelemetry(distro =>
                {
                    distro.Exporters = ExportTarget.Console | ExportTarget.Agent365;
                    distro.Agent365.TokenResolver = (agentId, tenantId) =>
                        tokenProvider.ResolveAsync(agentId, tenantId);
                    distro.Agent365.UseS2SEndpoint = true;
                    distro.Agent365.ClusterCategory = options.ClusterCategory;
                });
            });

            Console.WriteLine("Generating InvokeAgent, Inference, ExecuteTool, and Inference spans.");
            await SampleScenario.RunAsync(options).ConfigureAwait(false);

            if (sdk.TracerProvider?.ForceFlush(timeoutMilliseconds: 30_000) != true)
            {
                Console.Error.WriteLine("Trace flush did not complete within 30 seconds.");
                return 1;
            }

            Console.WriteLine("Telemetry flushed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IConfigurationRoot LoadConfiguration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Configuration file '{path}' was not found. Copy appsettings.example.json to appsettings.json and fill in the values.");
        }

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}
```

- [ ] **Step 6: Build the sample**

Run:

```powershell
dotnet build .\examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 7: Run without local configuration**

Ensure `examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\appsettings.json` does not exist, then run:

```powershell
dotnet run --project .\examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

Expected: exit code 1 and a message instructing the user to copy `appsettings.example.json`; no stack trace and no secret values.

- [ ] **Step 8: Commit**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Exporters\Agent365ExporterCore.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Exporters\Agent365ExporterTests.cs examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\Program.cs
git commit -m "feat: run and diagnose Agent365 S2S export" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: b204ed95-85a0-406f-a7ef-261b2462a367"
```

---

### Task 5: Document, configure locally, and validate end to end

**Files:**
- Create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/README.md`
- Modify: `README.md`
- Local-only create: `examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/appsettings.json`

**Interfaces:**
- Consumes: the completed sample executable and existing Agent365 demo configuration
- Produces: documented setup and verified live S2S export

- [ ] **Step 1: Write the sample README**

Document:

```markdown
# Agent365 S2S Observability Sample

This .NET 8 console sample uses the Microsoft OpenTelemetry distro and Agent365
manual scope APIs to export a single S2S trace containing:

1. InvokeAgent
2. Inference that selects a tool
3. ExecuteTool
4. Inference that produces the final answer

It does not use Agents Framework, an LLM, or a real tool endpoint.

## Prerequisites

- .NET 8 SDK
- Blueprint application client ID and secret
- Microsoft Entra tenant ID
- Agent app instance ID
- An onboarded Agent365 agent ID
- `Agent365.Observability.OtelWrite` application permission with admin consent

## Configure

Copy `appsettings.example.json` to `appsettings.json` and replace every
placeholder. `appsettings.json` is gitignored.

## Run

```powershell
dotnet run --project .\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

The console exporter prints all four spans. The Agent365 exporter prints an
HTTP success or failure status and the `x-ms-correlation-id` when returned.
Tokens and secrets are never printed.

## Authentication flow

The sample uses MSAL.NET to:

1. Acquire an Azure AD token-exchange token for the blueprint app with the
   agent app instance ID as the FMI path.
2. Use that token as the agent app instance client assertion.
3. Acquire an application token for the Agent365 observability `/.default`
   scope.
```

Add a link under the root README examples section:

```markdown
- [Agent365 S2S Observability Sample](examples/Microsoft.OpenTelemetry.Agent365.S2S.Demo/README.md)
```

- [ ] **Step 2: Run all targeted tests**

Run:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Agent365ExporterTests"
```

Expected: all tests pass.

- [ ] **Step 3: Build the solution**

Run:

```powershell
dotnet build .\Microsoft.OpenTelemetry.slnx
```

Expected: build succeeds with no errors or warnings introduced by this change.

- [ ] **Step 4: Create the local real configuration safely**

Create `examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\appsettings.json` from the user's existing Agent365 demo configuration. Map:

- `Connections:ServiceConnection:Settings:AuthorityEndpoint`
- `Connections:ServiceConnection:Settings:ClientId`
- `Connections:ServiceConnection:Settings:ClientSecret`
- the tenant ID from the existing app configuration
- the agent app instance ID
- the onboarded agent ID

Before running, verify:

```powershell
git check-ignore -v .\examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\appsettings.json
```

Expected: the sample's `.gitignore` rule is reported.

- [ ] **Step 5: Run the live sample**

Run:

```powershell
dotnet run --project .\examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\Microsoft.OpenTelemetry.Agent365.S2S.Demo.csproj
```

Expected:

- Console output contains one InvokeAgent span, two Inference spans, and one ExecuteTool span.
- Token acquisition completes without printing token material.
- Agent365 export logs an HTTP success status and correlation ID.
- Process exits with code 0 after `Telemetry flushed.`

- [ ] **Step 6: Verify no secrets are staged**

Run:

```powershell
git status --short
git diff --cached --name-only
git grep -n "ClientSecret" -- ':!**/appsettings.example.json' ':!**/*.md'
```

Expected: `appsettings.json` is absent from status and staged files; no real secret value appears in tracked source.

- [ ] **Step 7: Commit documentation**

```powershell
git add README.md examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\README.md examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\appsettings.example.json examples\Microsoft.OpenTelemetry.Agent365.S2S.Demo\.gitignore
git commit -m "docs: add Agent365 S2S sample guidance" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: b204ed95-85a0-406f-a7ef-261b2462a367"
```

---

## Final Verification

- [ ] Run the complete sample test project:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests\Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests.csproj
```

- [ ] Run the affected exporter tests:

```powershell
dotnet test .\test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Agent365ExporterTests"
```

- [ ] Build the entire solution:

```powershell
dotnet build .\Microsoft.OpenTelemetry.slnx
```

- [ ] Run the sample with the gitignored real configuration and verify a successful Agent365 HTTP response.

- [ ] Confirm the worktree contains no staged or tracked credentials:

```powershell
git status --short
git diff --cached --check
```
