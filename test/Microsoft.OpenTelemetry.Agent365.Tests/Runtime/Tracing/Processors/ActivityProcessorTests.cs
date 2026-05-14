// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing;

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

[TestClass]
public sealed class ActivityProcessorTests
{
    private const string ExternalSourceName = "System.Net.Http";

    [TestInitialize]
    public void EnableTelemetry()
    {
        AppContext.SetSwitch(EnableOpenTelemetrySwitch, true);
    }

    [TestMethod]
    public void OnStart_DoesNotMutate_NonAgent365Activities()
    {
        // Arrange - build a provider that listens to an external source but uses the Agent365 processor
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(ExternalSourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ExternalSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => capturedActivity = a,
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        // Act - set Agent365 baggage then start a span from an unrelated source
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .SessionId("session-xyz")
            .Build())
        {
            var externalSource = new ActivitySource(ExternalSourceName);
            using var activity = externalSource.StartActivity("HTTP GET /api/data");

            // Assert - the processor must NOT have applied any Agent365 / GenAI tags
            capturedActivity.Should().NotBeNull();
            capturedActivity!.GetTagItem(TenantIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive microsoft.tenant.id");
            capturedActivity.GetTagItem(GenAiAgentIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive gen_ai.agent.id");
            capturedActivity.GetTagItem(GenAiAgentNameKey).Should().BeNull(
                because: "non-Agent365 spans must not receive gen_ai.agent.name");
            capturedActivity.GetTagItem(SessionIdKey).Should().BeNull(
                because: "non-Agent365 spans must not receive microsoft.session.id");
            capturedActivity.GetTagItem(TelemetrySdkNameKey).Should().BeNull(
                because: "non-Agent365 spans must not receive telemetry.sdk.name");
        }
    }

    [TestMethod]
    public void OnStart_Mutates_Agent365Activities()
    {
        // Arrange
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => capturedActivity = a,
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        // Act - set baggage then start a span from the Agent365 source
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .Build())
        {
            var agent365Source = new ActivitySource(SourceName);
            using var activity = agent365Source.StartActivity("invoke_agent");

            // Assert - the processor SHOULD have applied the Agent365 tags
            capturedActivity.Should().NotBeNull();
            capturedActivity!.GetTagItem(TenantIdKey).Should().Be("tenant-123",
                because: "Agent365 spans must receive microsoft.tenant.id from baggage");
            capturedActivity.GetTagItem(GenAiAgentIdKey).Should().Be("agent-abc",
                because: "Agent365 spans must receive gen_ai.agent.id from baggage");
            capturedActivity.GetTagItem(TelemetrySdkNameKey).Should().Be(TelemetrySdkNameValue,
                because: "Agent365 spans must receive the telemetry.sdk.name tag");
        }
    }
}
