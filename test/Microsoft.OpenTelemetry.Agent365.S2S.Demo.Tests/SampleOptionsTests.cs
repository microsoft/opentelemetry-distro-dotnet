using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenTelemetry.Agent365.S2S.Demo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
