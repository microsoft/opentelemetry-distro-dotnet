using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Validation;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365ActivityCaptureSessionTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(400);

    [TestMethod]
    public async Task CompleteAsync_CapturesEligibleActivity()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(nameof(CompleteAsync_CapturesEligibleActivity));

        using (var activity = source.StartActivity("chat model"))
        {
            activity.Should().NotBeNull();
            activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        }

        var result = await session.CompleteAsync(ShortTimeout, CancellationToken.None);

        result.Spans.Should().ContainSingle();
        result.Spans[0].OperationName.Should().Be("chat");
        result.Spans[0].DisplayName.Should().Be("chat model");
        result.TimedOutSpans.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompleteAsync_IgnoresIneligibleActivity()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(nameof(CompleteAsync_IgnoresIneligibleActivity));

        using (var activity = source.StartActivity("http request"))
        {
            activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "unsupported");
        }

        var result = await session.CompleteAsync(ShortTimeout, CancellationToken.None);

        result.Spans.Should().BeEmpty();
        result.TimedOutSpans.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompleteAsync_SnapshotAttributes_AreImmutableAfterCapture()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(CompleteAsync_SnapshotAttributes_AreImmutableAfterCapture));

        var activity = source.StartActivity("chat model");
        activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        activity.SetTag("custom.tag", "original");
        activity.Dispose();

        var result = await session.CompleteAsync(ShortTimeout, CancellationToken.None);
        var snapshot = result.Spans.Single();

        // Mutate the underlying Activity after the snapshot was captured; the
        // snapshot must not observe the change because it copies attributes
        // eagerly at capture time.
        activity.SetTag("custom.tag", "mutated-after-capture");

        snapshot.Attributes["custom.tag"].Should().Be("original");
    }

    [TestMethod]
    public async Task CompleteAsync_AppliesSpanFilter()
    {
        using var session = new A365ActivityCaptureSession(
            span => span.DisplayName == "keep me");
        using var source = new ActivitySource(nameof(CompleteAsync_AppliesSpanFilter));

        using (var keep = source.StartActivity("keep me"))
        {
            keep!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        }

        using (var drop = source.StartActivity("drop me"))
        {
            drop!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        }

        var result = await session.CompleteAsync(ShortTimeout, CancellationToken.None);

        result.Spans.Should().ContainSingle(s => s.DisplayName == "keep me");
    }

    [TestMethod]
    public async Task CompleteAsync_SpanFilterThrows_WrapsInvalidOperationException()
    {
        using var session = new A365ActivityCaptureSession(
            _ => throw new InvalidOperationException("filter boom"));
        using var source = new ActivitySource(
            nameof(CompleteAsync_SpanFilterThrows_WrapsInvalidOperationException));

        using (var activity = source.StartActivity("chat model"))
        {
            activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        }

        Func<Task> act = () => session.CompleteAsync(ShortTimeout, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("filter boom");
    }

    [TestMethod]
    public async Task CompleteAsync_LateOrphanActivityDuringQuietPeriod_IsStillCaptured()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(CompleteAsync_LateOrphanActivityDuringQuietPeriod_IsStillCaptured));

        using (var first = source.StartActivity("first"))
        {
            first!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(150).ConfigureAwait(false);
            using var second = source.StartActivity("second");
            second!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
        });

        var result = await session.CompleteAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        result.Spans.Should().HaveCount(2);
        result.Spans.Select(s => s.DisplayName).Should().Contain(new[] { "first", "second" });
    }

    [TestMethod]
    public async Task CompleteAsync_TimesOut_ReportsEligibleActiveSpan()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(CompleteAsync_TimesOut_ReportsEligibleActiveSpan));

        var activity = source.StartActivity("stuck chat");
        activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        try
        {
            var result = await session.CompleteAsync(
                TimeSpan.FromMilliseconds(300),
                CancellationToken.None);

            result.Spans.Should().BeEmpty();
            result.TimedOutSpans.Should().ContainSingle(s => s.DisplayName == "stuck chat");
        }
        finally
        {
            activity.Dispose();
        }
    }

    [TestMethod]
    public async Task CompleteAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => session.CompleteAsync(TimeSpan.FromSeconds(5), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public void Dispose_DetachesListenerFromActivitySources()
    {
        var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(nameof(Dispose_DetachesListenerFromActivitySources));

        source.HasListeners().Should().BeTrue();

        session.Dispose();

        source.HasListeners().Should().BeFalse();
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var session = new A365ActivityCaptureSession(null);

        session.Dispose();
        Action act = session.Dispose;

        act.Should().NotThrow();
    }

    [TestMethod]
    public async Task OnStopped_PausedBetweenEnqueueAndActiveRemoval_ActivityRemainsObservable()
    {
        // Regression test for a race where OnStopped removed a stopping
        // activity from `active` before it was queued to `completed`. If a
        // completion check ran in that gap, the activity was visible in
        // neither collection and the completed span was silently lost. This
        // test forces that exact interleaving deterministically (via a
        // synchronization hook instead of sleep-based probing) so it fails
        // reliably against the old ordering and passes against the fix.
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(OnStopped_PausedBetweenEnqueueAndActiveRemoval_ActivityRemainsObservable));

        using var reachedTransition = new ManualResetEventSlim(false);
        using var releaseTransition = new ManualResetEventSlim(false);

        session.OnStoppedTransitionHookForTests = _ =>
        {
            reachedTransition.Set();
            releaseTransition.Wait(TimeSpan.FromSeconds(5));
        };

        var activity = source.StartActivity("chat model");
        activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        var stopTask = Task.Run(() => activity.Dispose());

        reachedTransition.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "OnStopped should reach the transition point deterministically");

        // At this exact instant, OnStopped is paused between queuing the
        // completed activity and removing it from the active set.
        session.IsObservableForTests(activity).Should().BeTrue(
            "an eligible activity must remain visible in the active set or " +
            "the completed queue at every point during OnStopped, so a " +
            "concurrently running completion check can never observe it in " +
            "neither collection");

        releaseTransition.Set();
        await stopTask;
    }

    [TestMethod]
    public async Task CompleteAsync_LongLivedFilteredOutActiveActivity_CompletesQuicklyWithoutTimeout()
    {
        // A foreign span that is eligible by operation name but excluded by
        // the configured span filter must not extend the quiet-period wait,
        // nor be reported as a completion timeout, even though it stays
        // active for the entire (much longer) configured timeout.
        using var session = new A365ActivityCaptureSession(
            span => span.DisplayName != "foreign long-lived");
        using var source = new ActivitySource(
            nameof(CompleteAsync_LongLivedFilteredOutActiveActivity_CompletesQuicklyWithoutTimeout));

        var foreign = source.StartActivity("foreign long-lived");
        foreign!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await session.CompleteAsync(
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            stopwatch.Stop();

            result.Spans.Should().BeEmpty();
            result.TimedOutSpans.Should().BeEmpty();
            result.TimedOut.Should().BeFalse();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
                "a filtered-out active span must not block the quiet period from being reached");
        }
        finally
        {
            foreign.Dispose();
        }
    }

    [TestMethod]
    public async Task CompleteAsync_TimesOut_DoesNotSleepPastConfiguredTimeout()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(CompleteAsync_TimesOut_DoesNotSleepPastConfiguredTimeout));

        var activity = source.StartActivity("stuck chat");
        activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        try
        {
            var timeout = TimeSpan.FromMilliseconds(300);
            var stopwatch = Stopwatch.StartNew();
            await session.CompleteAsync(timeout, CancellationToken.None);
            stopwatch.Stop();

            // Previously, the loop always slept the full 250ms quiet period
            // regardless of remaining budget, so it could overshoot the
            // deadline by nearly a full quiet period on the final iteration.
            stopwatch.Elapsed.Should().BeLessThan(
                timeout + TimeSpan.FromMilliseconds(150),
                "CompleteAsync must not intentionally sleep past the remaining timeout");
        }
        finally
        {
            activity.Dispose();
        }
    }

    [TestMethod]
    public async Task CompleteAsync_ContinuousChurn_PreservesTimedOutState()
    {
        using var session = new A365ActivityCaptureSession(null);
        using var source = new ActivitySource(
            nameof(CompleteAsync_ContinuousChurn_PreservesTimedOutState));
        using var churnStop = new CancellationTokenSource();

        var churnTask = Task.Run(async () =>
        {
            while (!churnStop.IsCancellationRequested)
            {
                using var churn = source.StartActivity("churn");
                churn?.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
                await Task.Delay(20, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        });

        try
        {
            var result = await session.CompleteAsync(
                TimeSpan.FromMilliseconds(400),
                CancellationToken.None);

            // Continuous churn keeps resetting the quiet-period window for
            // the full deadline, so completion must still be reported as
            // timed out (never silently downgraded to a clean/quiet result)
            // even though individual churned spans are extremely short-lived
            // and may or may not happen to be active at the exact deadline.
            // The "no active span at the exact deadline" edge case is covered
            // deterministically by
            // BuildReport_ChurnTimeoutWithNoActiveSpanAtDeadline_AddsGenericTimeoutFinding.
            result.TimedOut.Should().BeTrue();
            result.Spans.Should().NotBeEmpty("churned spans should still be captured as completed");
        }
        finally
        {
            churnStop.Cancel();
            await churnTask;
        }
    }
}
