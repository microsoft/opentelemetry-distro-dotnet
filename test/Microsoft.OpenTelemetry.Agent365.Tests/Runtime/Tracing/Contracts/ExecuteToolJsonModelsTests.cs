// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing.Contracts;

[TestClass]
public sealed class ExecuteToolJsonModelsTests
{
    [TestMethod]
    public void Arguments_DefaultsSchemaVersionAndStoresStandardProperties()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?> { ["format"] = "text" },
            Resources = new List<ToolCallResource>(),
        };

        arguments["schema_version"].Should().Be("1.0");
        arguments["action"].Should().Be("read");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.Parameters!["format"].Should().Be("text");
    }

    [TestMethod]
    public void StandardAndIndexerWritesUseLastWriteWins()
    {
        var policy = new ToolCallResultPolicy
        {
            Decision = ToolPolicyDecision.Allow,
        };

        policy["decision"] = "provider_conditional_allow";

        policy["decision"].Should().Be("provider_conditional_allow");
        policy.Decision.Should().BeNull();

        policy.Decision = ToolPolicyDecision.Deny;

        policy["decision"].Should().Be("deny");
        policy.Decision.Should().Be(ToolPolicyDecision.Deny);
    }

    [TestMethod]
    public void NullPropertyRemovesStandardKey()
    {
        var identifier = new ToolCallIdentifier
        {
            Type = "microsoft.graph.drive_item_id",
            Value = "01ABCDEF",
        };

        identifier.Type = null;

        identifier.Should().NotContainKey("type");
        identifier.Should().ContainKey("value");
    }

    [TestMethod]
    public void IdentifierAndContainerAcceptCustomProperties()
    {
        var identifier = new ToolCallIdentifier
        {
            ["provider_scope"] = "tenant",
        };
        var container = new ToolCallContainer
        {
            ["provider_path"] = "/sites/Engineering",
        };

        identifier["provider_scope"].Should().Be("tenant");
        container["provider_path"].Should().Be("/sites/Engineering");
    }

    [TestMethod]
    public void CopyConstructorPreservesCustomValues()
    {
        var result = new ExecuteToolCallResult(
            new Dictionary<string, object?>
            {
                ["provider_result"] = 42,
            });

        result["provider_result"].Should().Be(42);
    }
}
