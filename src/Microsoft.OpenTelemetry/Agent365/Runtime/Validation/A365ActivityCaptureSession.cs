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

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = Interlocked.Read(ref eligibleChangeVersion);

            await Task.Delay(QuietPeriod, cancellationToken).ConfigureAwait(false);

            if (version == Interlocked.Read(ref eligibleChangeVersion) &&
                !active.Keys.Any(IsEligible))
            {
                timedOut = false;
                break;
            }
        }

        return CreateResult(timedOut);
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
        active.TryRemove(activity, out _);

        if (IsEligible(activity))
        {
            completed.Enqueue(activity);
            Interlocked.Increment(ref eligibleChangeVersion);
        }
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
            bool include;
            try
            {
                include = spanFilter == null || spanFilter(snapshot);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Span filter failed for span '{snapshot.SpanId}'.",
                    ex);
            }

            if (include)
            {
                filtered.Add(snapshot);
            }
        }

        var timedOutSpans = new List<A365SpanSnapshot>();
        if (timedOut)
        {
            foreach (var activity in active.Keys)
            {
                if (IsEligible(activity))
                {
                    timedOutSpans.Add(CreateSnapshot(activity));
                }
            }
        }

        return new A365CaptureResult(filtered, timedOutSpans);
    }
}

/// <summary>
/// Filtered snapshots produced by a completed <see cref="A365ActivityCaptureSession"/>.
/// </summary>
internal sealed class A365CaptureResult
{
    internal A365CaptureResult(
        IReadOnlyList<A365SpanSnapshot> spans,
        IReadOnlyList<A365SpanSnapshot> timedOutSpans)
    {
        Spans = spans;
        TimedOutSpans = timedOutSpans;
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
}
