// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Hosting.Extensions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Moq;

namespace Microsoft.Agents.A365.Observability.Hosting.Tests.Extensions;

[TestClass]
public class TurnContextExtensionsTests
{
    [TestMethod]
    public void GetCallerBaggagePairs_AadObjectIdSet_ReturnsAadObjectId()
    {
        // Arrange – Teams-like channel where AadObjectId, AgenticUserId, and From.Id are all set
        var turnContext = CreateTurnContext(
            fromId: "from-id",
            aadObjectId: "aad-object-id",
            agenticUserId: "agentic-user-id");

        // Act
        var pairs = turnContext.GetCallerBaggagePairs().ToDictionary(p => p.Key, p => p.Value);

        // Assert – AadObjectId takes precedence
        pairs[OpenTelemetryConstants.UserIdKey].Should().Be("aad-object-id");
    }

    [TestMethod]
    public void GetCallerBaggagePairs_NoAadObjectId_FallsBackToAgenticUserId()
    {
        // Arrange – A2A scenario where AadObjectId is null but AgenticUserId is set
        var turnContext = CreateTurnContext(
            fromId: "from-id",
            aadObjectId: null,
            agenticUserId: "agentic-user-id");

        // Act
        var pairs = turnContext.GetCallerBaggagePairs().ToDictionary(p => p.Key, p => p.Value);

        // Assert – falls back to AgenticUserId
        pairs[OpenTelemetryConstants.UserIdKey].Should().Be("agentic-user-id");
    }

    [TestMethod]
    public void GetCallerBaggagePairs_NoAadObjectId_FallsBackToGuidAgenticUserId()
    {
        // Arrange – A2A scenario where AgenticUserId is a GUID
        var turnContext = CreateTurnContext(
            fromId: "29:1sH5NArUwkWAX",
            aadObjectId: null,
            agenticUserId: "bef730f4-d6f5-4ffb-b759-26ffa449ed7e");

        // Act
        var pairs = turnContext.GetCallerBaggagePairs().ToDictionary(p => p.Key, p => p.Value);

        // Assert – falls back to AgenticUserId (GUID format)
        pairs[OpenTelemetryConstants.UserIdKey].Should().Be("bef730f4-d6f5-4ffb-b759-26ffa449ed7e");
    }

    [TestMethod]
    public void GetCallerBaggagePairs_NoAadObjectIdNoAgenticUserId_FallsBackToFromId()
    {
        // Arrange – non-Teams channel where only From.Id is available
        var turnContext = CreateTurnContext(
            fromId: "from-id",
            aadObjectId: null,
            agenticUserId: null);

        // Act
        var pairs = turnContext.GetCallerBaggagePairs().ToDictionary(p => p.Key, p => p.Value);

        // Assert – falls back to From.Id
        pairs[OpenTelemetryConstants.UserIdKey].Should().Be("from-id");
    }

    private static ITurnContext CreateTurnContext(
        string? fromId = "caller-id",
        string? aadObjectId = "caller-aad",
        string? agenticUserId = null,
        string? fromName = "Caller")
    {
        var from = new ChannelAccount
        {
            Id = fromId,
            Name = fromName,
            AadObjectId = aadObjectId,
        };

        // Set AgenticUserId
        if (agenticUserId != null)
        {
            from.AgenticUserId = agenticUserId;
        }

        var mockActivity = new Mock<IActivity>();
        mockActivity.Setup(a => a.Type).Returns("message");
        mockActivity.Setup(a => a.From).Returns(from);

        var mockTurnContext = new Mock<ITurnContext>();
        mockTurnContext.Setup(tc => tc.Activity).Returns(mockActivity.Object);

        return mockTurnContext.Object;
    }
}
