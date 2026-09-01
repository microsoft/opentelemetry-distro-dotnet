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
