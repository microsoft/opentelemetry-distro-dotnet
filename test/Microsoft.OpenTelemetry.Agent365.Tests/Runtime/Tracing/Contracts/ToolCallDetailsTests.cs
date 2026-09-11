// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Contracts;

[TestClass]
public sealed class ToolCallDetailsTests
{
    [TestMethod]
    public void Constructor_WithTransferDetails_ExposesTypedValues()
    {
        var transfer = new TransferDetails(
            TransferMode.ReturnToCaller,
            targetName: "weather-agent",
            targetType: TransferTargetType.Agent);

        var details = new ToolCallDetails(
            "handoff",
            transfer);

        details.TransferDetails.Should().BeSameAs(transfer);
        transfer.Mode.Should().Be(TransferMode.ReturnToCaller);
        transfer.TargetName.Should().Be("weather-agent");
        transfer.TargetType.Should().Be(TransferTargetType.Agent);
        transfer.ModeValue.Should().Be("return_to_caller");
        transfer.TargetTypeValue.Should().Be("agent");
    }

    [TestMethod]
    public void Constructor_WithoutTransferDetails_RemainsCompatible()
    {
        var details = new ToolCallDetails(
            "tool",
            "{}",
            "call-1",
            "description",
            "function",
            new Uri("https://example.com"));

        details.TransferDetails.Should().BeNull();

        var (name, arguments, callId, description, type, endpoint) = details;
        name.Should().Be("tool");
        arguments.Should().Be("{}");
        callId.Should().Be("call-1");
        description.Should().Be("description");
        type.Should().Be("function");
        endpoint.Should().Be(new Uri("https://example.com"));
    }

    [TestMethod]
    public void Equals_WithEquivalentTransferDetails_IsTrueAndHasMatchingHashCode()
    {
        var left = new ToolCallDetails(
            "handoff",
            new TransferDetails(
                TransferMode.PassControl,
                "support-agent",
                TransferTargetType.Agent));
        var right = new ToolCallDetails(
            "handoff",
            new TransferDetails(
                TransferMode.PassControl,
                "support-agent",
                TransferTargetType.Agent));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [TestMethod]
    public void TransferDetails_MapsAllStandardValues()
    {
        new TransferDetails(TransferMode.PassControl).ModeValue
            .Should().Be("pass_control");
        new TransferDetails(TransferMode.ReturnToCaller).ModeValue
            .Should().Be("return_to_caller");
        new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Agent)
            .TargetTypeValue.Should().Be("agent");
        new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Human)
            .TargetTypeValue.Should().Be("human");
        new TransferDetails(TransferMode.PassControl, targetType: TransferTargetType.Workflow)
            .TargetTypeValue.Should().Be("workflow");
    }

    [TestMethod]
    public void TransferDetails_WithUndefinedMode_ThrowsAtConstructionTime()
    {
        Action act = () => new TransferDetails((TransferMode)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("mode");
    }

    [TestMethod]
    public void TransferDetails_WithUndefinedTargetType_ThrowsAtConstructionTime()
    {
        Action act = () => new TransferDetails(
            TransferMode.PassControl,
            targetType: (TransferTargetType)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("targetType");
    }

    [TestMethod]
    public void Equals_WithSameTypedArgumentsReference_IsTrueAndHasMatchingHashCode()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
        };

        var left = new ToolCallDetails("tool-name", arguments);
        var right = new ToolCallDetails("tool-name", arguments);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [TestMethod]
    public void Equals_WithDifferentTypedArgumentsReference_IsFalse()
    {
        var left = new ToolCallDetails(
            "tool-name",
            new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Read,
            });

        var right = new ToolCallDetails(
            "tool-name",
            new ExecuteToolCallArguments
            {
                Action = ToolCallAction.Read,
            });

        left.Should().NotBe(right);
    }
}
