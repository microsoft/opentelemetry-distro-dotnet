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
}
