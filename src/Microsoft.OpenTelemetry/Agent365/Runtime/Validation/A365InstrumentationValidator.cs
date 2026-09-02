// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Public orchestration entry point for in-process A365 instrumentation
/// validation. Attaches a temporary process-wide <see cref="System.Diagnostics.ActivityListener"/>
/// while the supplied action runs, then evaluates the captured spans against
/// the A365 certification rule catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>Warning:</b> the temporary listener samples every
/// <see cref="System.Diagnostics.ActivitySource"/> in the process as
/// <c>AllDataAndRecorded</c>. For the duration of
/// <see cref="EvaluateAsync"/> this forces full recording process-wide, so
/// activities that would otherwise have been sampled out are created,
/// recorded, and delivered to whatever processors and exporters are already
/// registered. Run validation only against test pipelines: do not run it in
/// a process configured with production exporters or production endpoints.
/// </para>
/// <para>
/// Capture is intentionally not constrained by <c>TracerProvider</c> source
/// registration, which the validator cannot reliably introspect. Every
/// recognized A365 GenAI span in the process is validated, including spans
/// from custom activity sources. Use
/// <see cref="A365ValidationOptions.SpanFilter"/> to opt unrelated recognized
/// telemetry out of a validation session.
/// </para>
/// </remarks>
public static class A365InstrumentationValidator
{
    private static readonly SemaphoreSlim SessionLock = new(1, 1);

    /// <summary>
    /// Runs <paramref name="action"/> while capturing recognized A365 GenAI
    /// spans, then returns a validation report for the captured spans.
    /// Validation sessions are serialized process-wide; only one evaluation
    /// runs at a time.
    /// </summary>
    /// <param name="action">The action under test.</param>
    /// <param name="configure">An optional callback used to configure validation options.</param>
    /// <param name="cancellationToken">A token used to cancel the wait for span completion.</param>
    /// <returns>The validation report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="A365ValidationExecutionException">
    /// A caller-supplied <see cref="A365ValidationOptions.SpanFilter"/> or
    /// suppression predicate threw. The original exception is preserved as
    /// the inner exception. An exception thrown by <paramref name="action"/>
    /// itself is never wrapped; it propagates unchanged.
    /// </exception>
    /// <remarks>
    /// While this method runs, all activity sources in the process are
    /// sampled as recorded. See the remarks on
    /// <see cref="A365InstrumentationValidator"/>.
    /// </remarks>
    public static async Task<A365ValidationReport> EvaluateAsync(
        Func<Task> action,
        Action<A365ValidationOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var options = new A365ValidationOptions();
        configure?.Invoke(options);
        A365ValidationEngine.ValidateOptions(options);

        await SessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            A365CaptureResult captured;
            using (var capture = new A365ActivityCaptureSession(options.SpanFilter))
            {
                await action().ConfigureAwait(false);
                captured = await capture.CompleteAsync(
                    options.SpanCompletionTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            return BuildReport(captured, options);
        }
        finally
        {
            SessionLock.Release();
        }
    }

    /// <summary>
    /// Builds the validation report from a completed capture. Internal (rather
    /// than private) so tests can deterministically exercise the session
    /// finding logic - including the churn/timeout branch below - without
    /// depending on wall-clock timing.
    /// </summary>
    internal static A365ValidationReport BuildReport(
        A365CaptureResult captured,
        A365ValidationOptions options)
    {
        var sessionFindings = new List<A365ValidationFinding>();

        // A recognized span that timed out while still active was captured
        // (it just never completed): reporting it via the SpanCompletionTimeout
        // finding below already conveys that. Only claim that no span was
        // captured at all when there are neither completed spans nor timed-out
        // eligible spans to report.
        if (captured.Spans.Count == 0 && captured.TimedOutSpans.Count == 0)
        {
            sessionFindings.Add(new A365ValidationFinding(
                A365ValidationRuleIds.NoSpansCaptured,
                A365ValidationSeverity.Error,
                A365ValidationFindingStatus.Active,
                operationName: null,
                attributeName: null,
                message: "No recognized A365 spans were captured during the validation session.",
                remediation: "Create a manual scope, use supported auto-instrumentation, or emit a custom ActivitySource span with a recognized gen_ai.operation.name.",
                suppressionReason: null,
                traceId: null,
                spanId: null));
        }

        foreach (var timedOutSpan in captured.TimedOutSpans)
        {
            sessionFindings.Add(new A365ValidationFinding(
                A365ValidationRuleIds.SpanCompletionTimeout,
                A365ValidationSeverity.Error,
                A365ValidationFindingStatus.Active,
                timedOutSpan.OperationName,
                attributeName: null,
                message: $"Span '{timedOutSpan.DisplayName}' did not complete within {options.SpanCompletionTimeout}.",
                remediation: "Ensure the span is stopped before the validated action returns, or increase A365ValidationOptions.SpanCompletionTimeout.",
                suppressionReason: null,
                timedOutSpan.TraceId,
                timedOutSpan.SpanId));
        }

        if (captured.TimedOut && captured.TimedOutSpans.Count == 0)
        {
            // The completion deadline was reached without ever observing a
            // full quiet period, but no eligible activity happened to be
            // active at the exact instant the deadline elapsed. Without this,
            // BuildReport would silently return a clean report even though
            // completion never actually settled. Preserve an explicit,
            // non-span-specific timeout finding instead.
            //
            // The message deliberately does not name a cause: reaching the
            // deadline without a quiet period can happen for reasons besides
            // continuous eligible activity churn -- e.g. a SpanCompletionTimeout
            // configured shorter than the 250ms quiet period itself, or a
            // residual window (the time left before the deadline) that is
            // too short to qualify as a full quiet period even though no
            // churn occurred during it. Claiming "continuous ... activity"
            // would be misleading in those cases.
            sessionFindings.Add(new A365ValidationFinding(
                A365ValidationRuleIds.SpanCompletionTimeout,
                A365ValidationSeverity.Error,
                A365ValidationFindingStatus.Active,
                operationName: null,
                attributeName: null,
                message: $"The validation session did not reach a full 250ms quiet period before the {options.SpanCompletionTimeout} SpanCompletionTimeout elapsed.",
                remediation: "Increase A365ValidationOptions.SpanCompletionTimeout to allow a full 250ms quiet period to be observed, or reduce concurrent/overlapping span activity during validation.",
                suppressionReason: null,
                traceId: null,
                spanId: null));
        }

        var spanResults = A365ValidationEngine.Validate(captured.Spans, options);

        foreach (var suppression in options.Suppressions)
        {
            if (suppression.WasUsed)
            {
                continue;
            }

            sessionFindings.Add(new A365ValidationFinding(
                A365ValidationRuleIds.UnusedSuppression,
                A365ValidationSeverity.Warning,
                A365ValidationFindingStatus.Active,
                suppression.OperationName,
                attributeName: null,
                message: $"Suppression {suppression.RuleId} did not match any finding.",
                remediation: "Remove the stale suppression or correct its targeting.",
                suppressionReason: null,
                traceId: null,
                spanId: null));
        }

        return new A365ValidationReport(spanResults, sessionFindings);
    }
}
