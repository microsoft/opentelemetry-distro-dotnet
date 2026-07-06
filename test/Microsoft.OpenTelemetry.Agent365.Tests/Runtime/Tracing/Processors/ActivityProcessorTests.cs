// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing;

using System.Diagnostics;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

[TestClass]
public sealed class ActivityProcessorTests : ActivityTest
{
    private const string ExternalSourceName = "System.Net.Http";

    /// <summary>
    /// Activities from a non-Agent365Sdk source must pass through untouched even when
    /// Agent365 baggage is set in the ambient context.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotMutate_NonAgent365Activities()
    {
        // Arrange - register the processor for an external source
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

            // Assert - no Agent365 / GenAI tags must be applied
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

    /// <summary>
    /// An activity that originates from the Agent365Sdk source but carries no
    /// <c>gen_ai.operation.name</c> tag (i.e. not one of the four GenAI scopes)
    /// must also pass through untouched.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotMutate_Agent365ActivitiesWithoutGenAiOperationName()
    {
        // Arrange - register the processor for the Agent365 source
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

        // Act - set baggage and start a raw Agent365Sdk span (no gen_ai.operation.name tag)
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .Build())
        {
            var agent365Source = new ActivitySource(SourceName);
            using var activity = agent365Source.StartActivity("custom-non-genai-span");

            // Assert - no GenAI tags must be applied to a non-GenAI Agent365Sdk span
            capturedActivity.Should().NotBeNull();
            capturedActivity!.GetTagItem(TenantIdKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive microsoft.tenant.id");
            capturedActivity.GetTagItem(GenAiAgentIdKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive gen_ai.agent.id");
            capturedActivity.GetTagItem(TelemetrySdkNameKey).Should().BeNull(
                because: "Agent365Sdk spans without gen_ai.operation.name must not receive telemetry.sdk.name");
        }
    }

    /// <summary>
    /// BaggageBuilder.OperationSource sets service.name in baggage; the processor must
    /// propagate it onto eligible GenAI spans via the AttributeKeys allow-list.
    /// </summary>
    [TestMethod]
    public void OnStart_PropagatesServiceName_FromOperationSourceBaggage()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();
        const string serviceName = "ACF";

        // Act - set service.name via OperationSource baggage, then start an eligible GenAI span
        using (new BaggageBuilder()
            .OperationSource(serviceName)
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InvokeAgentScope.Start(
                    new Request(),
                    new InvokeAgentScopeDetails(endpoint: null),
                    new AgentDetails("agent-1"));
            });

            // Assert - processor must coalesce service.name onto the GenAI span
            activity.GetTagItem(ServiceNameKey).Should().Be(serviceName,
                because: "BaggageBuilder.OperationSource() must propagate service.name onto eligible GenAI spans");
        }
    }

    /// <summary>
    /// The four GenAI scope types (invoke_agent, execute_tool, inference, output_messages)
    /// must have baggage-backed tags coalesced onto them by the processor.
    /// </summary>
    [TestMethod]
    public void OnStart_Mutates_GenAiScopeActivities()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - set baggage then start each GenAI scope
        using (new BaggageBuilder()
            .TenantId("tenant-123")
            .AgentId("agent-abc")
            .Build())
        {
            var activities = new[]
            {
                ListenForActivity(() =>
                {
                    using var scope = InvokeAgentScope.Start(
                        new Request(),
                        new InvokeAgentScopeDetails(endpoint: null),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = ExecuteToolScope.Start(
                        new Request(),
                        new ToolCallDetails("tool-name", "{}"),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = InferenceScope.Start(
                        new Request(),
                        new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                        new AgentDetails("agent-1"));
                }),
                ListenForActivity(() =>
                {
                    using var scope = OutputScope.Start(
                        new Request(),
                        new Response(new[] { "output-message" }),
                        new AgentDetails("agent-1"));
                }),
            };

            // Assert - baggage-backed tags must be coalesced onto GenAI spans
            foreach (var activity in activities)
            {
                activity.GetTagItem(TenantIdKey).Should().Be("tenant-123",
                    because: "GenAI spans must receive microsoft.tenant.id from baggage");
                activity.GetTagItem(GenAiAgentIdKey).Should().NotBeNull(
                    because: "GenAI spans must receive gen_ai.agent.id");
                activity.GetTagItem(TelemetrySdkNameKey).Should().Be(TelemetrySdkNameValue,
                    because: "GenAI spans must receive the telemetry.sdk.name tag");
            }
        }
    }

    /// <summary>
    /// A custom baggage key set via <see cref="BaggageBuilder.CustomAttribute"/> must be
    /// coalesced onto GenAI spans, even though it is not part of the curated allowlist.
    /// </summary>
    [TestMethod]
    public void OnStart_CoalescesCustomAttributeKeys_OntoGenAiSpans()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - set a custom attribute with no strongly-typed builder method
        using (new BaggageBuilder()
            .CustomAttribute("custom.app.key", "custom-value")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem("custom.app.key").Should().Be("custom-value",
                because: "custom attribute keys must be propagated onto GenAI spans");
        }
    }

    /// <summary>
    /// A custom attribute must NOT overwrite a value already set directly on the span
    /// (here, an agent attribute applied by the scope before the span starts).
    /// </summary>
    [TestMethod]
    public void OnStart_CustomAttribute_DoesNotOverwrite_SpanSetTag()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - custom attribute uses the same key the scope already sets from AgentDetails
        using (new BaggageBuilder()
            .CustomAttribute(GenAiAgentIdKey, "custom-agent")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("real-agent"));
            });

            // Assert
            activity.GetTagItem(GenAiAgentIdKey).Should().Be("real-agent",
                because: "a custom attribute must not overwrite a value set directly on the span");
        }
    }

    /// <summary>
    /// A custom attribute must NOT overwrite a value supplied through the request/scope,
    /// even when it shares the key with an allowlisted attribute.
    /// </summary>
    [TestMethod]
    public void OnStart_CustomAttribute_DoesNotOverwrite_RequestSuppliedValue()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - custom attribute clashes with the conversation id supplied on the request
        using (new BaggageBuilder()
            .CustomAttribute(GenAiConversationIdKey, "custom-conversation")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(conversationId: "request-conversation"),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem(GenAiConversationIdKey).Should().Be("request-conversation",
                because: "a custom attribute must not overwrite a value supplied via the request");
        }
    }

    /// <summary>
    /// A generic baggage key set via <see cref="BaggageBuilder.SetRange"/> (not marked as a
    /// custom attribute and not on the allowlist) must NOT be coalesced onto spans.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotCoalesce_NonCustomNonAllowlistedKeys()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - set an arbitrary key via SetRange (used for known keys, not custom routing)
        using (new BaggageBuilder()
            .SetRange(new[] { new KeyValuePair<string, object?>("unmarked.key", "unmarked-value") })
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem("unmarked.key").Should().BeNull(
                because: "only custom-attribute or allowlisted keys are coalesced onto spans");
        }
    }

    /// <summary>
    /// When a key is set both directly on the span and in baggage, the value set
    /// directly on the span must take precedence.
    /// </summary>
    [TestMethod]
    public void OnStart_SpanTagTakesPrecedence_OverBaggageOnClash()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - conversation id is set on the span via Request and also in baggage
        using (new BaggageBuilder()
            .ConversationId("baggage-conversation")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(conversationId: "span-conversation"),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem(GenAiConversationIdKey).Should().Be("span-conversation",
                because: "values set directly on the span must win over baggage values");
        }
    }

    /// <summary>
    /// Span-specific baggage (the invoke_agent server attributes) must only be coalesced onto
    /// invoke_agent spans and must not leak onto other GenAI span types.
    /// </summary>
    [TestMethod]
    public void OnStart_RetainsSpanSpecificBaggage_ForInvokeAgentOnly()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - server address/port baggage is span-specific to invoke_agent
        using (new BaggageBuilder()
            .InvokeAgentServer("agent-host", 8443)
            .Build())
        {
            var invokeAgentActivity = ListenForActivity(() =>
            {
                using var scope = InvokeAgentScope.Start(
                    new Request(),
                    new InvokeAgentScopeDetails(endpoint: null),
                    new AgentDetails("agent-1"));
            });

            var inferenceActivity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            invokeAgentActivity.GetTagItem(ServerAddressKey).Should().Be("agent-host",
                because: "invoke_agent spans must receive server.address from baggage");
            invokeAgentActivity.GetTagItem(ServerPortKey).Should().Be("8443",
                because: "invoke_agent spans must receive server.port from baggage");

            inferenceActivity.GetTagItem(ServerAddressKey).Should().BeNull(
                because: "invoke_agent-specific server.address must not leak onto inference spans");
            inferenceActivity.GetTagItem(ServerPortKey).Should().BeNull(
                because: "invoke_agent-specific server.port must not leak onto inference spans");
        }
    }

    /// <summary>
    /// A custom attribute key with surrounding whitespace must still be coalesced onto spans:
    /// the builder normalizes the key so it matches the trimmed key the processor looks up.
    /// </summary>
    [TestMethod]
    public void OnStart_CoalescesCustomAttributeKeys_WhenKeyHasSurroundingWhitespace()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - custom key is supplied with leading/trailing whitespace
        using (new BaggageBuilder()
            .CustomAttribute("  custom.spaced.key  ", "spaced-value")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem("custom.spaced.key").Should().Be("spaced-value",
                because: "custom keys are trimmed so they resolve against the processor's lookup");
        }
    }

    /// <summary>
    /// The reserved internal meta-key must never be registered as a custom attribute nor
    /// emitted as a span tag, even if a caller passes it explicitly.
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotEmitReservedMetaKey_WhenPassedAsCustomAttribute()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - caller maliciously/accidentally passes the reserved meta-key
        using (new BaggageBuilder()
            .CustomAttribute(CustomBaggageKeysKey, "leaked-plumbing")
            .Build())
        {
            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem(CustomBaggageKeysKey).Should().BeNull(
                because: "the reserved internal meta-key must never surface as a span tag");
        }
    }

    /// <summary>
    /// The reserved meta-key must not be coalesced onto spans even when it is present inside
    /// the custom-keys list itself (e.g. ambient baggage set manually/maliciously).
    /// </summary>
    [TestMethod]
    public void OnStart_DoesNotEmitReservedMetaKey_WhenPresentInAmbientCustomKeysList()
    {
        // Arrange
        using var tracerProvider = ConstructTracerProvider();

        // Act - manually set ambient baggage that lists the reserved key among the custom keys
        var previous = Baggage.Current;
        try
        {
            Baggage.Current = Baggage.Current
                .SetBaggage("custom.real.key", "real-value")
                .SetBaggage(CustomBaggageKeysKey, $"custom.real.key,{CustomBaggageKeysKey}");

            var activity = ListenForActivity(() =>
            {
                using var scope = InferenceScope.Start(
                    new Request(),
                    new InferenceCallDetails(InferenceOperationType.Chat, "model-name", "provider-name"),
                    new AgentDetails("agent-1"));
            });

            // Assert
            activity.GetTagItem("custom.real.key").Should().Be("real-value",
                because: "legitimate custom keys must still be coalesced onto spans");
            activity.GetTagItem(CustomBaggageKeysKey).Should().BeNull(
                because: "the reserved meta-key must be skipped even if it appears in the custom-keys list");
        }
        finally
        {
            Baggage.Current = previous;
        }
    }
}
