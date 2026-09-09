using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class SampleScenarioTests
{
    private const string EnableOpenTelemetrySwitch = "Azure.Experimental.EnableActivitySource";
    private const string SourceName = "Agent365Sdk";
    private const string GenAiOperationNameKey = "gen_ai.operation.name";
    private const string InvokeAgentOperationName = "invoke_agent";
    private const string ExecuteToolOperationName = "execute_tool";
    private const string TenantIdKey = "microsoft.tenant.id";
    private const string GenAiAgentIdKey = "gen_ai.agent.id";
    private const string GenAiAgentNameKey = "gen_ai.agent.name";
    private const string GenAiAgentDescriptionKey = "gen_ai.agent.description";
    private const string AgentAuidKey = "microsoft.agent.user.id";
    private const string AgentEmailKey = "microsoft.agent.user.email";
    private const string AgentBlueprintIdKey = "microsoft.a365.agent.blueprint.id";
    private const string ChannelNameKey = "microsoft.channel.name";
    private const string SessionIdKey = "microsoft.session.id";
    private const string GenAiConversationIdKey = "gen_ai.conversation.id";
    private const string ServiceNameKey = "service.name";
    private const string TelemetrySdkNameKey = "telemetry.sdk.name";
    private const string TelemetrySdkNameValue = "microsoft-opentelemetry";
    private const string ServerAddressKey = "server.address";
    private const string GenAiOutputMessagesKey = "gen_ai.output.messages";
    private const string GenAiRequestModelKey = "gen_ai.request.model";
    private const string GenAiProviderNameKey = "gen_ai.provider.name";
    private const string GenAiUsageInputTokensKey = "gen_ai.usage.input_tokens";
    private const string GenAiUsageOutputTokensKey = "gen_ai.usage.output_tokens";
    private const string GenAiResponseFinishReasonsKey = "gen_ai.response.finish_reasons";
    private const string GenAiToolNameKey = "gen_ai.tool.name";
    private const string GenAiToolTypeKey = "gen_ai.tool.type";
    private const string GenAiToolCallIdKey = "gen_ai.tool.call.id";
    private const string GenAiToolArgumentsKey = "gen_ai.tool.call.arguments";
    private const string GenAiToolCallResultKey = "gen_ai.tool.call.result";
    private const string PreservedAmbientKey = "contoso.preserved";

    private const string SessionId = "session-s2s-123";
    private const string ConversationId = "conversation-s2s-789";
    private const string ChannelName = "service";
    private const string ServiceName = "Microsoft.OpenTelemetry.Agent365.S2S.Demo";
    private const string AgentName = "Weather Agent";
    private const string AgentDescription = "Answers current Seattle weather questions.";
    private const string FinalAnswer = "It is currently 62°F and partly cloudy in Seattle.";
    private static readonly TimeSpan FirstInferenceDuration = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ToolDuration = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan FinalInferenceDuration = TimeSpan.FromMilliseconds(30);
    private static readonly DateTimeOffset FixedScenarioStartTime =
        new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [TestMethod]
    public async Task RunAsync_AcceptsInjectedTimeProvider_AndKeepsRelativeSpanTimeline()
    {
        AppContext.SetSwitch(EnableOpenTelemetrySwitch, true);

        var stopped = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var delays = new List<TimeSpan>();
        var options = TestData.CreateOptions();
        var timeProvider = new TestTimeProvider(FixedScenarioStartTime);

        await SampleScenario.RunAsync(
            options,
            (duration, cancellationToken) =>
            {
                delays.Add(duration);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            timeProvider);

        delays.Should().Equal(FirstInferenceDuration, ToolDuration, FinalInferenceDuration);
        stopped.Should().HaveCount(4);

        var invoke = stopped.Single(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                InvokeAgentOperationName));
        var inferenceSpans = stopped.Where(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                nameof(InferenceOperationType.Chat))).ToArray();
        var tool = stopped.Single(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                ExecuteToolOperationName));

        invoke.StartTimeUtc.Should().Be(FixedScenarioStartTime.UtcDateTime);
        invoke.Duration.Should().Be(
            FirstInferenceDuration + ToolDuration + FinalInferenceDuration);

        inferenceSpans[0].StartTimeUtc.Should().Be(FixedScenarioStartTime.UtcDateTime);
        inferenceSpans[0].Duration.Should().Be(FirstInferenceDuration);

        tool.StartTimeUtc.Should().Be(inferenceSpans[0].StartTimeUtc + inferenceSpans[0].Duration);
        tool.Duration.Should().Be(ToolDuration);

        inferenceSpans[1].StartTimeUtc.Should().Be(tool.StartTimeUtc + tool.Duration);
        inferenceSpans[1].Duration.Should().Be(FinalInferenceDuration);

        invoke.StartTimeUtc.Should().BeBefore(tool.StartTimeUtc);
        tool.StartTimeUtc.Should().BeBefore(inferenceSpans[1].StartTimeUtc);
        invoke.StartTimeUtc.Add(invoke.Duration).Should().Be(
            inferenceSpans[1].StartTimeUtc + inferenceSpans[1].Duration);
    }

    [TestMethod]
    public async Task RunAsync_EmitsExpectedAgent365SpanSequence()
    {
        AppContext.SetSwitch(EnableOpenTelemetrySwitch, true);

        var stopped = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var delays = new List<TimeSpan>();
        var options = TestData.CreateOptions();
        var timeProvider = new TestTimeProvider(FixedScenarioStartTime);

        await SampleScenario.RunAsync(
            options,
            (duration, cancellationToken) =>
            {
                delays.Add(duration);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            timeProvider);

        delays.Should().Equal(FirstInferenceDuration, ToolDuration, FinalInferenceDuration);

        stopped.Should().HaveCount(4);
        stopped.Select(static activity => activity.GetTagItem(GenAiOperationNameKey))
            .Should()
            .ContainInOrder(
                nameof(InferenceOperationType.Chat),
                ExecuteToolOperationName,
                nameof(InferenceOperationType.Chat),
                InvokeAgentOperationName);

        var invoke = stopped.Single(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                InvokeAgentOperationName));
        var inferenceSpans = stopped.Where(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                nameof(InferenceOperationType.Chat))).ToArray();
        var tool = stopped.Single(
            activity => Equals(
                activity.GetTagItem(GenAiOperationNameKey),
                ExecuteToolOperationName));

        stopped.Where(activity => activity != invoke)
            .Should()
            .OnlyContain(activity =>
                activity.ParentSpanId == invoke.SpanId
                && activity.TraceId == invoke.TraceId);

        foreach (var activity in stopped)
        {
            AssertCommonCertificationTags(activity, options);
        }

        invoke.GetTagItem(ServerAddressKey).Should().Be("weather-agent.contoso.com");
        (invoke.GetTagItem(GenAiOutputMessagesKey) as string)
            .Should()
            .Contain("partly cloudy in Seattle.")
            .And.Contain("62\\u00B0F");

        inferenceSpans[0].GetTagItem(GenAiRequestModelKey).Should().Be("gpt-4o-mini");
        inferenceSpans[0].GetTagItem(GenAiProviderNameKey).Should().Be("openai");
        inferenceSpans[0].GetTagItem(GenAiUsageInputTokensKey).Should().Be(24);
        inferenceSpans[0].GetTagItem(GenAiUsageOutputTokensKey).Should().Be(12);
        inferenceSpans[0].GetTagItem(GenAiResponseFinishReasonsKey)
            .Should()
            .BeEquivalentTo(new[] { "tool_call" });

        tool.GetTagItem(GenAiToolNameKey).Should().Be("get_weather");
        tool.GetTagItem(GenAiToolTypeKey).Should().Be("function");
        tool.GetTagItem(GenAiToolCallIdKey).Should().Be("call-weather-001");
        tool.GetTagItem(ServerAddressKey).Should().Be("weather-api.contoso.com");
        (tool.GetTagItem(GenAiToolArgumentsKey) as string)
            .Should()
            .Contain("\"city\":\"Seattle\"");
        (tool.GetTagItem(GenAiToolCallResultKey) as string)
            .Should()
            .Contain("\"temperature\":62");

        inferenceSpans[1].GetTagItem(GenAiRequestModelKey).Should().Be("gpt-4o-mini");
        inferenceSpans[1].GetTagItem(GenAiProviderNameKey).Should().Be("openai");
        inferenceSpans[1].GetTagItem(GenAiUsageInputTokensKey).Should().Be(36);
        inferenceSpans[1].GetTagItem(GenAiUsageOutputTokensKey).Should().Be(18);
        inferenceSpans[1].GetTagItem(GenAiResponseFinishReasonsKey)
            .Should()
            .BeEquivalentTo(new[] { "stop" });
        (inferenceSpans[1].GetTagItem(GenAiOutputMessagesKey) as string)
            .Should()
            .Contain("partly cloudy in Seattle.")
            .And.Contain("62\\u00B0F");
    }

    [TestMethod]
    public async Task RunAsync_OmitsAmbientAgenticUserTags_AndRestoresCallerBaggage()
    {
        AppContext.SetSwitch(EnableOpenTelemetrySwitch, true);

        var stopped = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var options = TestData.CreateOptions();
        var previous = Baggage.Current;

        try
        {
            Baggage.Current = Baggage.Current
                .SetBaggage(AgentAuidKey, "ambient-agent-user-id")
                .SetBaggage(AgentEmailKey, "ambient-user@contoso.com")
                .SetBaggage(PreservedAmbientKey, "preserved-value");

            await SampleScenario.RunAsync(
                options,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                new TestTimeProvider(FixedScenarioStartTime));

            stopped.Should().HaveCount(4);

            foreach (var activity in stopped)
            {
                AssertCommonCertificationTags(activity, options);
            }

            Baggage.Current.GetBaggage(AgentAuidKey).Should().Be("ambient-agent-user-id");
            Baggage.Current.GetBaggage(AgentEmailKey).Should().Be("ambient-user@contoso.com");
            Baggage.Current.GetBaggage(PreservedAmbientKey).Should().Be("preserved-value");
        }
        finally
        {
            Baggage.Current = previous;
        }
    }

    private static void AssertCommonCertificationTags(Activity activity, SampleOptions options)
    {
        activity.GetTagItem(TenantIdKey).Should().Be(options.TenantId);
        activity.GetTagItem(GenAiAgentIdKey).Should().Be(options.AgentId);
        activity.GetTagItem(GenAiAgentNameKey).Should().Be(AgentName);
        activity.GetTagItem(GenAiAgentDescriptionKey).Should().Be(AgentDescription);
        activity.GetTagItem(AgentAuidKey).Should().BeNull();
        activity.GetTagItem(AgentEmailKey).Should().BeNull();
        activity.GetTagItem(AgentBlueprintIdKey).Should().Be(options.ClientId);
        activity.GetTagItem(ChannelNameKey).Should().Be(ChannelName);
        activity.GetTagItem(SessionIdKey).Should().Be(SessionId);
        activity.GetTagItem(GenAiConversationIdKey).Should().Be(ConversationId);
        activity.GetTagItem(ServiceNameKey).Should().Be(ServiceName);
        activity.GetTagItem(TelemetrySdkNameKey)
            .Should()
            .Be(TelemetrySdkNameValue);
    }
}
