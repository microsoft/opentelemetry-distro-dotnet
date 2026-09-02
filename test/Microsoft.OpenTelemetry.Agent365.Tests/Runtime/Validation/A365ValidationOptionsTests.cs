using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Validation;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365ValidationOptionsTests
{
    [TestMethod]
    public void Defaults_AreCertificationAndTenSeconds()
    {
        var options = new A365ValidationOptions();

        options.Profile.Should().Be(A365ValidationProfile.Certification);
        options.SpanCompletionTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.SpanFilter.Should().BeNull();
    }

    [TestMethod]
    public void Suppress_RequiresReason()
    {
        var options = new A365ValidationOptions();

        Action act = () => options.Suppress(
            A365ValidationRuleIds.AgentNameRequired,
            reason: " ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    [TestMethod]
    public void OperationSuppression_RequiresOperationName()
    {
        var options = new A365ValidationOptions();

        Action act = () => options.Suppress(
            A365ValidationRuleIds.InvokeUserIdRequired,
            operationName: "",
            reason: "Anonymous invocation");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("operationName");
    }

    [TestMethod]
    public void SpanSnapshot_CopiesAttributes()
    {
        var source = new Dictionary<string, object?>
        {
            ["gen_ai.operation.name"] = "chat",
        };

        var snapshot = new A365SpanSnapshot(
            "trace",
            "span",
            "chat model",
            "Custom.Source",
            "chat",
            source);

        source["gen_ai.operation.name"] = "changed";

        snapshot.Attributes["gen_ai.operation.name"].Should().Be("chat");
    }

    [TestMethod]
    public void SpanSnapshot_DerivesRoutingIdentityFromAttributes()
    {
        var snapshot = new A365SpanSnapshot(
            "trace",
            "span",
            "chat model",
            "Custom.Source",
            "chat",
            new Dictionary<string, object?>
            {
                ["microsoft.tenant.id"] = "tenant",
                ["gen_ai.agent.id"] = "agent",
                ["microsoft.a365.agent.platform.id"] = "platform-agent",
            });

        snapshot.RoutingTenantId.Should().Be("tenant");
        snapshot.RoutingAgentId.Should().Be("agent");
    }

    [TestMethod]
    public void SpanSnapshot_FallsBackToAgentPlatformIdForRoutingIdentity()
    {
        var snapshot = new A365SpanSnapshot(
            "trace",
            "span",
            "chat model",
            "Custom.Source",
            "chat",
            new Dictionary<string, object?>
            {
                ["microsoft.a365.agent.platform.id"] = "platform-agent",
            });

        snapshot.RoutingTenantId.Should().BeNull();
        snapshot.RoutingAgentId.Should().Be("platform-agent");
    }

    [TestMethod]
    public void SpanSnapshot_MissingRoutingAttributes_LeavesRoutingIdentityNull()
    {
        var snapshot = new A365SpanSnapshot(
            "trace",
            "span",
            "chat model",
            "Custom.Source",
            "chat",
            new Dictionary<string, object?>
            {
                ["gen_ai.agent.id"] = null,
            });

        snapshot.RoutingTenantId.Should().BeNull();
        snapshot.RoutingAgentId.Should().BeNull();
    }
}
