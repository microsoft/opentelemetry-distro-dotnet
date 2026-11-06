// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Scopes;

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

[TestClass]
public sealed class InferenceScopeTest : ActivityTest
{
    [TestMethod]
    public void Start_SetsExpectedTags()
    {
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai",
            123,
            456,
            new[] { "stop", "length" },
            responseModel: "gpt-4o-2024-11-20",
            cacheReadInputTokens: 23,
            cacheWriteInputTokens: 12,
            endpoint: new Uri("https://models.example:8443/openai/deployments/customer-name"));

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiRequestModelKey, details.Model);
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiResponseModelKey, details.ResponseModel!);
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiProviderNameKey, details.ProviderName);
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageInputTokensKey).Should().Be(details.InputTokens!.Value);
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageOutputTokensKey).Should().Be(details.OutputTokens!.Value);
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageCacheReadInputTokensKey).Should().Be(details.CacheReadInputTokens!.Value);
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageCacheWriteInputTokensKey).Should().Be(details.CacheWriteInputTokens!.Value);
        activity.ShouldHaveTag(OpenTelemetryConstants.ServerAddressKey, "models.example");
        activity.GetTagItem(OpenTelemetryConstants.ServerPortKey).Should().Be(8443);
        activity.GetTagItem(OpenTelemetryConstants.GenAiResponseFinishReasonsKey).Should().BeEquivalentTo(details.FinishReasons!);
    }

    [DataTestMethod]
    [DataRow(InferenceOperationType.Chat, "chat")]
    [DataRow(InferenceOperationType.TextCompletion, "text_completion")]
    [DataRow(InferenceOperationType.GenerateContent, "generate_content")]
    [DataRow((InferenceOperationType)999, "chat")]
    public void Start_UsesCanonicalOperationNameForSpanAndMetric(InferenceOperationType operationName, string expected)
    {
        var details = new InferenceCallDetails(operationName, "gpt-4o", "openai");
        var metricOperations = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == SourceName && instrument.Name == GenAiClientOperationDurationMetricName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == GenAiOperationNameKey)
                {
                    metricOperations.Add(tag.Value as string);
                }
            }
        });
        listener.Start();

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiOperationNameKey, expected);
        activity.DisplayName.Should().Be($"{expected} gpt-4o");
        metricOperations.Should().Equal(expected);
    }

    [TestMethod]
    public void Start_WithUnsupportedOperation_DoesNotThrowOrLeakActivity()
    {
        var details = new InferenceCallDetails((InferenceOperationType)999, "gpt-4o", "openai");
        using var parent = new Activity("inference-parent").Start();
        Activity? activity = null;
        Action start = () => activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails());
            Activity.Current.Should().NotBeSameAs(parent);
        });

        try
        {
            start.Should().NotThrow();
            Activity.Current.Should().BeSameAs(parent);
            activity!.GetTagItem(GenAiOperationNameKey).Should().Be("chat");
        }
        finally
        {
            if (Activity.Current != parent)
            {
                Activity.Current?.Stop();
            }
        }
    }

    [DataTestMethod]
    [DataRow("/relative/private", UriKind.Relative)]
    [DataRow("file:///private/deployment", UriKind.Absolute)]
    public void Start_WithInvalidEndpoint_OmitsEndpointTags(string endpoint, UriKind kind)
    {
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai",
            endpoint: new Uri(endpoint, kind));
        var parent = Activity.Current;

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails());
        });

        Activity.Current.Should().BeSameAs(parent);
        activity.GetTagItem(ServerAddressKey).Should().BeNull();
        activity.GetTagItem(ServerPortKey).Should().BeNull();
        activity.GetTagItem(GenAiRequestModelKey).Should().Be("gpt-4o");
    }

    [TestMethod]
    public void RecordInputTokens_SetsTag()
    {
        var inputTokens = 789;
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordInputTokens(inputTokens);
        });
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageInputTokensKey).Should().Be(inputTokens);
    }

    [TestMethod]
    public void RecordOutputTokens_SetsTag()
    {
        var outputTokens = 321;
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordOutputTokens(outputTokens);
        });
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageOutputTokensKey).Should().Be(outputTokens);
    }

    [TestMethod]
    public void RecordModelObservationResponseDetails_SetsTags()
    {
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordResponseModel("gpt-4o-2024-11-20");
            scope.RecordCacheReadInputTokens(17);
            scope.RecordCacheWriteInputTokens(9);
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiResponseModelKey, "gpt-4o-2024-11-20");
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageCacheReadInputTokensKey).Should().Be(17);
        activity.GetTagItem(OpenTelemetryConstants.GenAiUsageCacheWriteInputTokensKey).Should().Be(9);
    }

    [TestMethod]
    public void RecordFinishReasons_SetsTag()
    {
        var finishReasons = new[] { "tool_calls", "stop" };
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordFinishReasons(finishReasons);
        });
        activity.GetTagItem(OpenTelemetryConstants.GenAiResponseFinishReasonsKey).Should().BeEquivalentTo(finishReasons);
    }

    [TestMethod]
    public void RecordInputMessages_SetsTag()
    {
        var messages = new[] { "Hello", "How are you?" };
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordInputMessages(messages);
        });

        var tagValue = activity.Tags.First(t => t.Key == "gen_ai.input.messages").Value;
        tagValue.Should().StartWith("[");
        tagValue.Should().Contain("\"role\":\"user\"");
        tagValue.Should().Contain("Hello");
        tagValue.Should().Contain("How are you?");
    }

    [TestMethod]
    public void RecordOutputMessages_SetsTag()
    {
        var messages = new[] { "Hi there!", "I'm fine." };
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordOutputMessages(messages);
        });

        var tagValue = activity.Tags.First(t => t.Key == "gen_ai.output.messages").Value;
        tagValue.Should().StartWith("[");
        tagValue.Should().Contain("\"role\":\"assistant\"");
        tagValue.Should().Contain("Hi there!");
        tagValue.Should().Contain("I\\u0027m fine.");
    }

    [TestMethod]
    public void SetStartTime_SetsActivityStartTime()
    {
        var customStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails());
            scope.SetStartTime(customStartTime);
        });

        // Activity start time should be close to the custom start time
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void Start_SetsConversationId_WhenProvided()
    {
        var conversationId = "conv-inf-123";
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                new Request(conversationId: conversationId),
                details,
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiConversationIdKey, conversationId);
    }

    [TestMethod]
    public void Start_SetsSessionId_WhenProvidedOnRequest()
    {
        var sessionId = "session-inf-123";
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                new Request(sessionId: sessionId),
                details,
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.SessionIdKey, sessionId);
    }

    [TestMethod]
    public void Start_SetsOperationSource_WhenProvidedOnRequest()
    {
        var operationSource = OperationSource.SDK.ToString();
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                new Request(operationSource: operationSource),
                details,
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.ServiceNameKey, operationSource);
    }

    [TestMethod]
    public void Start_SetsChannel_Tags()
    {
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");
        var metadata = new Channel(name: "ChannelZ", link: "https://channel/link/z");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                new Request(channel: metadata),
                details,
                Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelNameKey, metadata.Name!);
        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelLinkKey, metadata.Link!);
    }

    [TestMethod]
    public void RecordThoughtProcess_SetsTag()
    {
        var thoughtProcess = "First, I analyzed the user's request. Then, I determined the best approach would be to provide a structured response. Finally, I composed the answer.";
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(Util.GetDefaultRequest(), details, Util.GetAgentDetails())!;
            scope.RecordThoughtProcess(thoughtProcess);
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiAgentThoughtProcessKey, thoughtProcess);
    }

    [TestMethod]
    public void Start_SetsCallerDetails_WhenProvided()
    {
        // Arrange
        var userDetails = new UserDetails(
            userId: "caller-inf-123",
            userName: "Inference Caller",
            userEmail: "caller-inf@example.com",
            userClientIP: System.Net.IPAddress.Parse("10.0.0.1"));
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                Util.GetDefaultRequest(),
                details,
                Util.GetAgentDetails(),
                userDetails: userDetails);
        });

        // Assert
        activity.ShouldHaveTag(OpenTelemetryConstants.UserIdKey, userDetails.UserId!);
        activity.ShouldHaveTag(OpenTelemetryConstants.UserNameKey, userDetails.UserName!);
        activity.ShouldHaveTag(OpenTelemetryConstants.UserEmailKey, userDetails.UserEmail!);
        activity.ShouldHaveTag(OpenTelemetryConstants.CallerClientIpKey, userDetails.UserClientIP!.ToString());
    }

    [TestMethod]
    public void Start_WithCustomStartTime_SetsActivityStartTime()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                Util.GetDefaultRequest(),
                details,
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime));
        });

        // Assert
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void Start_WithCustomStartAndEndTime_SetsActivityTimes()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);
        var customEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 25, TimeSpan.Zero); // 5 seconds later
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                Util.GetDefaultRequest(),
                details,
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime, endTime: customEndTime));
        });

        // Assert - Start time should be set to custom time
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }

    [TestMethod]
    public void SetEndTime_OverridesEndTime()
    {
        // Arrange
        var customStartTime = new DateTimeOffset(2023, 11, 14, 22, 13, 40, TimeSpan.Zero);
        var initialEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 45, TimeSpan.Zero);
        var laterEndTime = new DateTimeOffset(2023, 11, 14, 22, 13, 48, TimeSpan.Zero);
        var details = new InferenceCallDetails(
            InferenceOperationType.Chat,
            "gpt-4o",
            "openai");

        // Act
        var activity = ListenForActivity(() =>
        {
            using var scope = InferenceScope.Start(
                Util.GetDefaultRequest(),
                details,
                Util.GetAgentDetails(),
                spanDetails: new SpanDetails(startTime: customStartTime, endTime: initialEndTime));
            scope.SetEndTime(laterEndTime);
        });

        // Assert - The start time should be set
        var startTime = new DateTimeOffset(activity.StartTimeUtc);
        startTime.Should().BeCloseTo(customStartTime, TimeSpan.FromMilliseconds(100));
    }
}
