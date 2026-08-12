// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public class Agent365TransmissionGateTests
{
    private DateTimeOffset _now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private Agent365TransmissionGate CreateGate(Random? random = null) =>
        new Agent365TransmissionGate(() => _now, random);

    private void Advance(TimeSpan span) => _now += span;

    private Agent365TransmissionGate OpenAndAdvance()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(null);
        // Advance past the maximum possible delay
        Advance(TimeSpan.FromHours(2));
        return gate;
    }

    [TestMethod]
    public void FirstRetryableFailureOpensSharedBackoff()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(null);
        gate.TryAcquire(out _).Should().BeFalse();
    }

    [TestMethod]
    public void HonorsPositiveRetryAfter()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(TimeSpan.FromMinutes(3));
        Advance(TimeSpan.FromMinutes(2));
        gate.TryAcquire(out _).Should().BeFalse();
        Advance(TimeSpan.FromMinutes(1));
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeTrue();
    }

    [TestMethod]
    public void FallbackBackoffIsJitteredBetweenTenSecondsAndOneHour()
    {
        var gate = CreateGate(random: new DeterministicRandom(0));
        gate.RecordRetryableFailure(null);
        gate.CurrentDelay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        gate.CurrentDelay.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [TestMethod]
    public void OnlyOneProbeIsAllowedAfterBackoff()
    {
        var gate = OpenAndAdvance();
        gate.TryAcquire(out var firstProbe).Should().BeTrue();
        firstProbe.Should().BeTrue();
        gate.TryAcquire(out _).Should().BeFalse();
    }

    [TestMethod]
    public void SuccessResetsConsecutiveErrors()
    {
        var gate = OpenAndAdvance();
        gate.TryAcquire(out _).Should().BeTrue();
        gate.RecordSuccess();
        gate.ConsecutiveErrors.Should().Be(0);
        gate.TryAcquire(out _).Should().BeTrue();
    }

    [TestMethod]
    public void ClosedGatePermitsAcquireWithoutProbeOwnership()
    {
        var gate = CreateGate();
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeFalse();
    }

    [TestMethod]
    public void IsRetryableAcceptsUnauthorized()
    {
        Agent365TransmissionGate.IsRetryable(HttpStatusCode.Unauthorized).Should().BeTrue();
    }

    [TestMethod]
    public void IsRetryableAcceptsRequestTimeout()
    {
        Agent365TransmissionGate.IsRetryable(HttpStatusCode.RequestTimeout).Should().BeTrue();
    }

    [TestMethod]
    public void IsRetryableAccepts429()
    {
        Agent365TransmissionGate.IsRetryable((HttpStatusCode)429).Should().BeTrue();
    }

    [TestMethod]
    public void IsRetryableAcceptsAll5xx()
    {
        for (var code = 500; code <= 599; code++)
        {
            Agent365TransmissionGate.IsRetryable((HttpStatusCode)code).Should().BeTrue($"status {code} should be retryable");
        }
    }

    [TestMethod]
    public void IsRetryableExcludes403Forbidden()
    {
        Agent365TransmissionGate.IsRetryable(HttpStatusCode.Forbidden).Should().BeFalse();
    }

    [TestMethod]
    public void IsRetryableExcludes200()
    {
        Agent365TransmissionGate.IsRetryable(HttpStatusCode.OK).Should().BeFalse();
    }

    [TestMethod]
    public void IsRetryableExcludes404()
    {
        Agent365TransmissionGate.IsRetryable(HttpStatusCode.NotFound).Should().BeFalse();
    }

    [TestMethod]
    public void RetryAfterLargerThanMaximumIsClamped()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(TimeSpan.FromDays(365));
        gate.CurrentDelay.Should().BeLessThanOrEqualTo(Agent365TransmissionGate.MaximumDelay);
    }

    [TestMethod]
    public void NegativeRetryAfterFallsBackToJittered()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(TimeSpan.FromSeconds(-5));
        gate.CurrentDelay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void ReleaseProbeAllowsAnotherProbeAfterBackoff()
    {
        var gate = OpenAndAdvance();
        gate.TryAcquire(out _).Should().BeTrue();
        gate.ReleaseProbe();
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeTrue();
    }

    [TestMethod]
    public void SecondFailureIncrementsConsecutiveErrors()
    {
        var gate = CreateGate();
        gate.RecordRetryableFailure(null);
        gate.RecordRetryableFailure(null);
        gate.ConsecutiveErrors.Should().Be(2);
    }

    [TestMethod]
    public void SuccessAfterProbeAllowsSubsequentAcquires()
    {
        var gate = OpenAndAdvance();
        gate.TryAcquire(out _).Should().BeTrue();
        gate.RecordSuccess();
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeFalse(); // Closed state, no probe ownership
    }

    [TestMethod]
    public async Task SlowFailureCalculationDoesNotBlockClosedGateAcquire()
    {
        using var clockEntered = new ManualResetEventSlim();
        using var releaseClock = new ManualResetEventSlim();
        var gate = new Agent365TransmissionGate(
            () =>
            {
                clockEntered.Set();
                releaseClock.Wait();
                return _now;
            });

        var failure = Task.Run(() => gate.RecordRetryableFailure(TimeSpan.FromSeconds(30)));
        try
        {
            clockEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var acquire = Task.Run(() => gate.TryAcquire(out _));
            var completed = await Task.WhenAny(acquire, Task.Delay(TimeSpan.FromSeconds(1)));

            completed.Should().Be(
                acquire,
                "calculating a failure transition must not serialize unrelated gate callers");
            (await acquire).Should().BeTrue("the previously published state is still closed");
        }
        finally
        {
            releaseClock.Set();
            await failure;
        }
    }

    [TestMethod]
    public async Task StaleFailureRecalculatesDeadlineAfterClosedStateIsRepublished()
    {
        using var firstClockEntered = new ManualResetEventSlim();
        using var releaseFirstClock = new ManualResetEventSlim();
        var firstClockCall = 0;
        var originalNow = _now;
        var gate = new Agent365TransmissionGate(
            () =>
            {
                if (Interlocked.Increment(ref firstClockCall) == 1)
                {
                    firstClockEntered.Set();
                    releaseFirstClock.Wait();
                    return originalNow;
                }

                return _now;
            });

        var staleFailure = Task.Run(
            () => gate.RecordRetryableFailure(TimeSpan.FromSeconds(30)));
        try
        {
            firstClockEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            gate.RecordRetryableFailure(TimeSpan.FromMinutes(5));
            gate.RecordSuccess();
            Advance(TimeSpan.FromMinutes(1));

            releaseFirstClock.Set();
            await staleFailure;

            gate.TryAcquire(out _).Should().BeFalse(
                "a stale transition must retry against the newly published closed state and current time");
        }
        finally
        {
            releaseFirstClock.Set();
            await staleFailure;
        }
    }
}

/// <summary>
/// A deterministic <see cref="Random"/> subclass for testing. Returns a fixed value from Next().
/// </summary>
internal sealed class DeterministicRandom : Random
{
    private readonly int _value;

    internal DeterministicRandom(int value) => _value = value;

    public override int Next(int minValue, int maxValue)
    {
        if (maxValue <= minValue) return minValue;
        return minValue;
    }
}
