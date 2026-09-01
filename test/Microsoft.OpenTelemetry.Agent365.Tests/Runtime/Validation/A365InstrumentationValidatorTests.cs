using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Validation;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365InstrumentationValidatorTests
{
    [TestMethod]
    public async Task EvaluateAsync_CapturesRecognizedCustomSourceSpan()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();
            SetValidChatAttributes(activity!);
            return Task.CompletedTask;
        });

        report.Spans.Should().ContainSingle();
        report.Spans[0].Span.SourceName.Should().Be("Customer.Agent");
        report.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task EvaluateAsync_IgnoresUnsupportedActivities()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent");
            using var activity = source.StartActivity("http request");
            activity!.SetTag("gen_ai.operation.name", "unsupported");
            return Task.CompletedTask;
        });

        report.IsValid.Should().BeFalse();
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.NoSpansCaptured);
    }

    [TestMethod]
    public async Task EvaluateAsync_RethrowsActionException()
    {
        var expected = new InvalidOperationException("application failed");

        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () => Task.FromException(expected));

        var actual = await act.Should().ThrowAsync<InvalidOperationException>();
        actual.Which.Should().BeSameAs(expected);
    }

    [TestMethod]
    public async Task EvaluateAsync_ActionThrows_DetachesListenerAndReleasesLockForNextEvaluation()
    {
        Func<Task> failing = () => A365InstrumentationValidator.EvaluateAsync(
            () => Task.FromException(new InvalidOperationException("boom")));

        await failing.Should().ThrowAsync<InvalidOperationException>();

        using var probe = new ActivitySource(
            nameof(EvaluateAsync_ActionThrows_DetachesListenerAndReleasesLockForNextEvaluation));
        probe.HasListeners().Should().BeFalse();

        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.Recovery");
            using var activity = source.StartActivity("chat model");
            SetValidChatAttributes(activity!);
            return Task.CompletedTask;
        });

        report.IsValid.Should().BeTrue();
        report.Spans.Should().ContainSingle();
    }

    [TestMethod]
    public async Task EvaluateAsync_NullAction_ThrowsArgumentNullException()
    {
        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(null!);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("action");
    }

    [TestMethod]
    public async Task EvaluateAsync_ActiveSpanNeverCompletes_AddsTimeoutFinding()
    {
        Activity? stuck = null;

        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource(
                    nameof(EvaluateAsync_ActiveSpanNeverCompletes_AddsTimeoutFinding));
                stuck = source.StartActivity("stuck chat");
                stuck!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
                return Task.CompletedTask;
            },
            options => options.SpanCompletionTimeout = TimeSpan.FromMilliseconds(300));

        try
        {
            report.IsValid.Should().BeFalse();
            report.SessionFindings.Should().ContainSingle(f =>
                f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
                f.Severity == A365ValidationSeverity.Error);
        }
        finally
        {
            stuck?.Dispose();
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_UnusedSuppression_AddsWarningFinding()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource("Customer.Agent.Unused");
                using var activity = source.StartActivity("chat model");
                SetValidChatAttributes(activity!);
                return Task.CompletedTask;
            },
            options => options.Suppress(
                A365ValidationRuleIds.ToolNameRequired,
                "execute_tool",
                "Not exercised by this scenario"));

        report.IsValid.Should().BeTrue();
        report.WarningCount.Should().Be(1);
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.UnusedSuppression &&
            f.Severity == A365ValidationSeverity.Warning &&
            f.Message.Contains(A365ValidationRuleIds.ToolNameRequired));
    }

    [TestMethod]
    public async Task EvaluateAsync_SpanFilterThrows_PropagatesInvalidOperationException()
    {
        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource("Customer.Agent.Filtered");
                using var activity = source.StartActivity("chat model");
                SetValidChatAttributes(activity!);
                return Task.CompletedTask;
            },
            options => options.SpanFilter = _ => throw new InvalidOperationException("filter boom"));

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("filter boom");

        using var probe = new ActivitySource(
            nameof(EvaluateAsync_SpanFilterThrows_PropagatesInvalidOperationException) + ".Probe");
        probe.HasListeners().Should().BeFalse();
    }

    [TestMethod]
    public async Task EvaluateAsync_AlreadyCancelledToken_ThrowsWithoutRunningAction()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var actionRan = false;

        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        actionRan.Should().BeFalse();
    }

    [TestMethod]
    public async Task EvaluateAsync_ConcurrentEvaluations_RunSerially()
    {
        var concurrentCount = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        async Task TrackedAction()
        {
            lock (gate)
            {
                concurrentCount++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCount);
            }

            await Task.Delay(150).ConfigureAwait(false);

            lock (gate)
            {
                concurrentCount--;
            }
        }

        var first = A365InstrumentationValidator.EvaluateAsync(TrackedAction);
        var second = A365InstrumentationValidator.EvaluateAsync(TrackedAction);

        await Task.WhenAll(first, second);

        maxObservedConcurrency.Should().Be(1);
    }

    [TestMethod]
    public async Task EvaluateAsync_LongLivedFilteredOutActiveSpan_DoesNotReportTimeout()
    {
        Activity? foreign = null;

        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource(
                    nameof(EvaluateAsync_LongLivedFilteredOutActiveSpan_DoesNotReportTimeout));
                foreign = source.StartActivity("foreign long-lived");
                foreign!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
                return Task.CompletedTask;
            },
            options =>
            {
                options.SpanCompletionTimeout = TimeSpan.FromSeconds(2);
                options.SpanFilter = span => span.DisplayName != "foreign long-lived";
            });

        try
        {
            report.SessionFindings.Should().NotContain(f =>
                f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout);
        }
        finally
        {
            foreign?.Dispose();
        }
    }

    [TestMethod]
    public void BuildReport_ChurnTimeoutWithNoActiveSpanAtDeadline_AddsGenericTimeoutFinding()
    {
        // Deterministic coverage for the case where CompleteAsync reaches its
        // deadline because of continuous eligible activity churn, but no
        // activity happens to be active at the exact final instant. The
        // capture result still reports TimedOut = true with an empty
        // TimedOutSpans list; BuildReport must not silently treat that as a
        // clean report.
        var options = new A365ValidationOptions();
        var captured = new A365CaptureResult(
            spans: Array.Empty<A365SpanSnapshot>(),
            timedOutSpans: Array.Empty<A365SpanSnapshot>(),
            timedOut: true);

        var report = A365InstrumentationValidator.BuildReport(captured, options);

        report.IsValid.Should().BeFalse();
        report.SessionFindings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
            f.Severity == A365ValidationSeverity.Error &&
            f.Status == A365ValidationFindingStatus.Active &&
            f.SpanId == null);
    }

    [TestMethod]
    public void BuildReport_TimedOutSpanWithNoCompletedSpans_DoesNotAddNoSpansCapturedFinding()
    {
        // A recognized span that timed out while still active was in fact
        // captured -- it just never completed. BuildReport must report that
        // via SpanCompletionTimeout only, and must not additionally claim
        // (falsely) that no span was captured at all.
        var options = new A365ValidationOptions();
        var timedOutSpan = new A365SpanSnapshot(
            "0123456789abcdef0123456789abcdef",
            "0123456789abcdef",
            "stuck chat",
            "Customer.Agent",
            "chat",
            new Dictionary<string, object?>());
        var captured = new A365CaptureResult(
            spans: Array.Empty<A365SpanSnapshot>(),
            timedOutSpans: new[] { timedOutSpan },
            timedOut: true);

        var report = A365InstrumentationValidator.BuildReport(captured, options);

        report.SessionFindings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.NoSpansCaptured);
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
            f.SpanId == timedOutSpan.SpanId);
    }

    private static void SetValidChatAttributes(Activity activity)
    {
        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        activity.SetTag(OpenTelemetryConstants.TenantIdKey, "tenant");
        activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
        activity.SetTag(OpenTelemetryConstants.GenAiAgentNameKey, "Weather agent");
        activity.SetTag(
            OpenTelemetryConstants.GenAiAgentDescriptionKey,
            "Answers weather questions");
        activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, "agent-user");
        activity.SetTag(
            OpenTelemetryConstants.AgentEmailKey,
            "agent@example.com");
        activity.SetTag(
            OpenTelemetryConstants.AgentBlueprintIdKey,
            "blueprint");
        activity.SetTag(OpenTelemetryConstants.GenAiRequestModelKey, "gpt-4.1");
        activity.SetTag(OpenTelemetryConstants.GenAiProviderNameKey, "openai");
    }
}
