// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Scopes;

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

[TestClass]
public sealed class ScopeTests : ActivityTest
{
    private class TestScope : OpenTelemetryScope
    {
        public TestScope(
            string operationName,
            string activityName,
            AgentDetails agentDetails,
            Request? request = null,
            SpanDetails? spanDetails = null)
            : base(operationName, activityName, agentDetails, request, spanDetails)
        {
        }
    }

    [TestMethod]
    public void NestedScope_PropagatesAgentId()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act
        var activity = ListenForActivity(() =>
        {
            using var invokeAgentScope = InvokeAgentScope.Start(new Request(), ScopeDetails, Util.GetAgentDetails());
            using var toolScope = ExecuteToolScope.Start(new Request(), new ToolCallDetails("TestTool", "Input: 42"), Util.GetAgentDetails());
        });

        // Assert
        activity.Should().NotBeNull();
        activity.Kind.Should().Be(ActivityKind.Internal);
        activity.TagObjects.Should().ContainKey(GenAiOperationNameKey)
            .WhoseValue.Should().Be(OpenTelemetryConstants.ExecuteToolOperationName);
        activity.TagObjects.Should().ContainKey(GenAiAgentIdKey)
            .WhoseValue.Should().BeOfType<string>()
            .Which.Should().Be(AgentId);
    }

    [TestMethod]
    public void Id_ReturnsActivityId()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);
        
        using var scope = new TestScope(
            "TestOperation",
            "TestActivity",
            Util.GetAgentDetails(),
            spanDetails: new SpanDetails(ActivityKind.Internal));
        
        // Act
        var expectedId = scope.Id;

        // Assert
        expectedId.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Constructor_SetsSharedRequestTags()
    {
        var request = new Request(
            sessionId: "session-123",
            channel: new Channel("service", "https://example.invalid/channel"),
            conversationId: "conversation-123",
            operationSource: "request-source");

        var activity = ListenForActivity(() =>
        {
            using var scope = new TestScope(
                InvokeAgentOperationName,
                "test activity",
                Util.GetAgentDetails(),
                request);
        });

        activity.ShouldHaveTag(SessionIdKey, "session-123");
        activity.ShouldHaveTag(GenAiConversationIdKey, "conversation-123");
        activity.ShouldHaveTag(ServiceNameKey, "request-source");
        activity.ShouldHaveTag(ChannelNameKey, "service");
        activity.ShouldHaveTag(ChannelLinkKey, "https://example.invalid/channel");
    }

    [TestMethod]
    public void Constructor_RequestValuesTakePrecedenceOverBaggage()
    {
        using var tracerProvider = ConstructTracerProvider();
        using (new BaggageBuilder()
            .OperationSource("baggage-source")
            .SessionId("baggage-session")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = new TestScope(
                    InvokeAgentOperationName,
                    "test activity",
                    Util.GetAgentDetails(),
                    new Request(
                        sessionId: "request-session",
                        operationSource: "request-source"));
            });

            activity.ShouldHaveTag(SessionIdKey, "request-session");
            activity.ShouldHaveTag(ServiceNameKey, "request-source");
        }
    }

    [TestMethod]
    public void Constructor_BaggageFillsRequestValuesThatAreMissing()
    {
        using var tracerProvider = ConstructTracerProvider();
        using (new BaggageBuilder()
            .OperationSource("baggage-source")
            .SessionId("baggage-session")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = new TestScope(
                    InvokeAgentOperationName,
                    "test activity",
                    Util.GetAgentDetails(),
                    new Request());
            });

            activity.ShouldHaveTag(SessionIdKey, "baggage-session");
            activity.ShouldHaveTag(ServiceNameKey, "baggage-source");
        }
    }

    [TestMethod]
    public void SetParentContext_SetsActivityParentId()
    {
        // Arrange
        var manualParentActivity = CreateActivity();
        var parentContext = manualParentActivity.Context;
        var parentSpanId = manualParentActivity.SpanId.ToString() ?? string.Empty;

        // Act
        var activity = ListenForActivity(() =>
        {
            using var toolScope = ExecuteToolScope.Start(new Request(), new ToolCallDetails("TestTool", "Input: 42"), Util.GetAgentDetails(), spanDetails: new SpanDetails(parentContext: parentContext));
        });
        
        // Assert
        activity.Should().NotBeNull();
        activity!.ParentSpanId.ToString().Should().Be(parentSpanId);
    }
}
