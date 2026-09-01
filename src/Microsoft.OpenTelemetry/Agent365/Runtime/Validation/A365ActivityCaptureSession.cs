// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Temporarily attaches a process-wide <see cref="ActivityListener"/> to
/// capture recognized A365 GenAI spans and waits for a quiet period before
/// producing immutable snapshots.
/// </summary>
internal sealed class A365ActivityCaptureSession : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentDictionary<Activity, byte> active = new();
    private readonly ConcurrentQueue<Activity> completed = new();
    private readonly Func<A365SpanSnapshot, bool>? spanFilter;
    private readonly ActivityListener listener;
    private long eligibleChangeVersion;
    private bool disposed;

    internal A365ActivityCaptureSession(Func<A365SpanSnapshot, bool>? spanFilter)
    {
        this.spanFilter = spanFilter;

        listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = OnStarted,
            ActivityStopped = OnStopped,
        };

        ActivitySource.AddActivityListener(listener);
    }

    /// <summary>
    /// Gets or sets a test-only synchronization hook. When set, it is invoked
    /// synchronously inside <see cref="OnStopped"/> after a stopping activity
    /// has been made durably observable (queued to the completed set when
    /// eligible) but before it is removed from the active set. Production
    /// code never sets this; it exists solely so regression tests can
    /// deterministically observe the transition point instead of relying on
    /// wall-clock timing.
    /// </summary>
    internal Action<Activity>? OnStoppedTransitionHookForTests { get; set; }

    /// <summary>
    /// Waits for a 250-millisecond quiet period, bounded by <paramref name="timeout"/>,
    /// during which no new eligible activity starts or stops. Returns filtered
    /// snapshots for completed eligible spans and snapshots for any span that
    /// was still active and eligible when the timeout elapsed.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for span completion.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>The captured result.</returns>
    internal async Task<A365CaptureResult> CompleteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var timedOut = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var version = Interlocked.Read(ref eligibleChangeVersion);

            // Never sleep past the remaining timeout budget: cap the delay to
            // whatever time is left instead of always waiting the full quiet
            // period, so CompleteAsync returns promptly once the deadline is
            // reached instead of intentionally oversleeping by up to QuietPeriod.
            var delay = remaining < QuietPeriod ? remaining : QuietPeriod;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            if (version == Interlocked.Read(ref eligibleChangeVersion) &&
                !active.Keys.Any(IsEligibleForWait))
            {
                timedOut = false;
                break;
            }
        }

        return CreateResult(timedOut);
    }

    /// <summary>
    /// Test-only helper: reports whether <paramref name="activity"/> is
    /// currently observable through either the active set or the completed
    /// queue. Used to assert that an eligible stopping activity is never
    /// visible in neither collection at the same time.
    /// </summary>
    internal bool IsObservableForTests(Activity activity)
    {
        return active.ContainsKey(activity) || completed.Contains(activity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        listener.Dispose();
    }

    private static bool IsEligible(Activity activity)
    {
        var operationName =
            activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string;

        return !string.IsNullOrEmpty(operationName) &&
            OpenTelemetryConstants.GenAiOperationNames.Contains(operationName!);
    }

    /// <summary>
    /// Determines whether <paramref name="activity"/> is both recognized (by
    /// operation name) and, when a <see cref="spanFilter"/> is configured,
    /// passes it. A span that is eligible by name but excluded by the span
    /// filter must not extend the quiet-period wait, nor be reported as a
    /// completion timeout, since the caller has declared it out of scope.
    /// </summary>
    private bool IsEligibleForWait(Activity activity)
    {
        return IsEligible(activity) && PassesFilter(CreateSnapshot(activity));
    }

    private bool PassesFilter(A365SpanSnapshot snapshot)
    {
        try
        {
            return spanFilter == null || spanFilter(snapshot);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Span filter failed for span '{snapshot.SpanId}'.",
                ex);
        }
    }

    private static A365SpanSnapshot CreateSnapshot(Activity activity)
    {
        var operationName =
            activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string ??
            string.Empty;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            attributes[tag.Key] = tag.Value;
        }

        return new A365SpanSnapshot(
            activity.TraceId.ToHexString().ToLowerInvariant(),
            activity.SpanId.ToHexString().ToLowerInvariant(),
            activity.DisplayName,
            activity.Source.Name,
            operationName,
            attributes);
    }

    private void OnStarted(Activity activity)
    {
        active[activity] = 0;

        if (IsEligible(activity))
        {
            Interlocked.Increment(ref eligibleChangeVersion);
        }
    }

    private void OnStopped(Activity activity)
    {
        // Ordering matters: an eligible stopping activity must remain visible
        // to CompleteAsync's completion logic (via `active` or `completed`) at
        // every instant. Queue it as completed (and publish the version bump)
        // BEFORE removing it from `active`, so there is never a window where
        // it exists in neither collection and could be silently dropped by a
        // concurrently-running completion check.
        if (IsEligible(activity))
        {
            completed.Enqueue(activity);
            Interlocked.Increment(ref eligibleChangeVersion);
        }

        OnStoppedTransitionHookForTests?.Invoke(activity);

        active.TryRemove(activity, out _);
    }

    private A365CaptureResult CreateResult(bool timedOut)
    {
        var snapshots = new List<A365SpanSnapshot>();
        while (completed.TryDequeue(out var activity))
        {
            snapshots.Add(CreateSnapshot(activity));
        }

        var filtered = new List<A365SpanSnapshot>();
        foreach (var snapshot in snapshots)
        {
            if (PassesFilter(snapshot))
            {
                filtered.Add(snapshot);
            }
        }

        var timedOutSpans = new List<A365SpanSnapshot>();
        if (timedOut)
        {
            foreach (var activity in active.Keys)
            {
                if (IsEligibleForWait(activity))
                {
                    timedOutSpans.Add(CreateSnapshot(activity));
                }
            }
        }

        return new A365CaptureResult(filtered, timedOutSpans, timedOut);
    }
}

/// <summary>
/// Filtered snapshots produced by a completed <see cref="A365ActivityCaptureSession"/>.
/// </summary>
internal sealed class A365CaptureResult
{
    internal A365CaptureResult(
        IReadOnlyList<A365SpanSnapshot> spans,
        IReadOnlyList<A365SpanSnapshot> timedOutSpans,
        bool timedOut)
    {
        Spans = spans;
        TimedOutSpans = timedOutSpans;
        TimedOut = timedOut;
    }

    /// <summary>
    /// Gets the spans that completed and passed the configured span filter.
    /// </summary>
    internal IReadOnlyList<A365SpanSnapshot> Spans { get; }

    /// <summary>
    /// Gets the eligible spans that were still active when the completion
    /// timeout elapsed.
    /// </summary>
    internal IReadOnlyList<A365SpanSnapshot> TimedOutSpans { get; }

    /// <summary>
    /// Gets a value indicating whether the completion deadline was reached
    /// without observing a quiet period. This can be <see langword="true"/>
    /// even when <see cref="TimedOutSpans"/> is empty, e.g. when continuous
    /// eligible activity churn kept resetting the quiet-period window but no
    /// activity happened to be active at the exact instant the deadline was
    /// reached.
    /// </summary>
    internal bool TimedOut { get; }
}
