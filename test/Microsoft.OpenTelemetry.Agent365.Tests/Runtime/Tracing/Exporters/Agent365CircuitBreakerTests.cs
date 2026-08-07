// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public class Agent365CircuitBreakerTests
{
    private DateTimeOffset _now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void OpensAfterFiveTransientFailures()
    {
        var breaker = new Agent365CircuitBreaker(() => _now);

        for (var i = 0; i < 4; i++)
        {
            breaker.RecordTransientFailure();
            breaker.TryAcquirePermit().Should().BeTrue();
        }

        breaker.RecordTransientFailure();

        breaker.State.Should().Be(Agent365CircuitState.Open);
        breaker.TryAcquirePermit().Should().BeFalse();
    }

    [TestMethod]
    public void AllowsOneHalfOpenProbeAfterRecoveryTimeout()
    {
        var breaker = OpenBreaker();
        _now = _now.AddSeconds(31);

        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
        breaker.TryAcquirePermit().Should().BeFalse();
    }

    [TestMethod]
    public void SuccessfulProbeClosesCircuit()
    {
        var breaker = OpenBreaker();
        _now = _now.AddSeconds(31);
        breaker.TryAcquirePermit().Should().BeTrue();

        breaker.RecordSuccess();

        breaker.State.Should().Be(Agent365CircuitState.Closed);
        breaker.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public void FailedProbeReopensCircuit()
    {
        var breaker = OpenBreaker();
        _now = _now.AddSeconds(31);
        breaker.TryAcquirePermit().Should().BeTrue();

        breaker.RecordTransientFailure();

        breaker.State.Should().Be(Agent365CircuitState.Open);
        breaker.TryAcquirePermit().Should().BeFalse();
    }

    [TestMethod]
    public void FailuresWhileOpenDoNotShiftRecoveryWindow()
    {
        // Open the breaker at t=0
        var breaker = OpenBreaker();
        var openTime = _now;

        // Multiple failures while breaker is open should not shift recovery window
        _now = _now.AddSeconds(10);
        breaker.RecordTransientFailure();
        breaker.RecordTransientFailure();
        breaker.RecordTransientFailure();

        // At t=30, recovery window has elapsed (measured from original open time)
        _now = openTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
    }

    [TestMethod]
    public void FailedHalfOpenProbeStartsNewRecoveryWindow()
    {
        var breaker = OpenBreaker();
        var firstOpenTime = _now;

        // First recovery window: at t=30, transition to half-open
        _now = firstOpenTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);

        var probeFailureTime = _now;

        // Probe fails - starts new 30-second window from failure time
        breaker.RecordTransientFailure();
        breaker.State.Should().Be(Agent365CircuitState.Open);

        // At 29 seconds after failure - still not ready
        _now = probeFailureTime.AddSeconds(29);
        breaker.TryAcquirePermit().Should().BeFalse();
        breaker.State.Should().Be(Agent365CircuitState.Open);

        // At 30 seconds after failure - next probe allowed
        _now = probeFailureTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
    }

    [TestMethod]
    public void Exactly30SecondsAllowsHalfOpenTransition()
    {
        var breaker = OpenBreaker();
        var openTime = _now;

        // Exactly 30 seconds later - should allow probe
        _now = openTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
    }

    [TestMethod]
    public void ReleasedHalfOpenPermitAllowsAnotherProbe()
    {
        var breaker = OpenBreaker();
        _now = _now.AddSeconds(31);

        // First probe acquired
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);

        // Second probe blocked while first is in flight
        breaker.TryAcquirePermit().Should().BeFalse();

        // Release without recording success or failure
        breaker.ReleasePermit();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);

        // Another probe now allowed
        breaker.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public void ReleasePermitDoesNothingWhenClosed()
    {
        var breaker = new Agent365CircuitBreaker(() => _now);
        breaker.TryAcquirePermit().Should().BeTrue();

        // No exception, state unchanged
        breaker.ReleasePermit();
        breaker.State.Should().Be(Agent365CircuitState.Closed);
        breaker.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public void ReleasePermitDoesNothingWhenOpen()
    {
        var breaker = OpenBreaker();
        breaker.State.Should().Be(Agent365CircuitState.Open);

        // No exception, state unchanged
        breaker.ReleasePermit();
        breaker.State.Should().Be(Agent365CircuitState.Open);
    }

    [TestMethod]
    public void ClosedPermitDoesNotOwnHalfOpenProbe()
    {
        var breaker = new Agent365CircuitBreaker(() => _now);

        breaker.TryAcquirePermit(out var acquiredHalfOpenProbe).Should().BeTrue();
        acquiredHalfOpenProbe.Should().BeFalse();
    }

    [TestMethod]
    public void HalfOpenProbeReportsOwnershipAndBlocksSecondOwner()
    {
        var breaker = OpenBreaker();
        _now = _now.AddSeconds(31);

        breaker.TryAcquirePermit(out var acquiredHalfOpenProbe).Should().BeTrue();
        acquiredHalfOpenProbe.Should().BeTrue();

        // A second concurrent attempt is refused and owns no probe.
        breaker.TryAcquirePermit(out var secondOwner).Should().BeFalse();
        secondOwner.Should().BeFalse();
    }

    [TestMethod]
    public void ClosedPermitReleaseCannotFreeAnotherInvocationsHalfOpenProbe()
    {
        var breaker = new Agent365CircuitBreaker(() => _now);

        // Invocation B acquires a Closed-state permit; a Closed permit owns no probe.
        breaker.TryAcquirePermit(out var bOwnsProbe).Should().BeTrue();
        bOwnsProbe.Should().BeFalse();

        // While B is still running, the circuit opens and later recovers to half-open.
        for (var i = 0; i < Agent365CircuitBreaker.FailureThreshold; i++)
        {
            breaker.RecordTransientFailure();
        }

        _now = _now.AddSeconds(31);

        // Invocation H claims the single half-open probe.
        breaker.TryAcquirePermit(out var hOwnsProbe).Should().BeTrue();
        hOwnsProbe.Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);

        // B finishes. Its ownership-gated finally must NOT release, because B never owned a probe.
        if (bOwnsProbe)
        {
            breaker.ReleasePermit();
        }

        // H's probe is still in flight: no other invocation may acquire one.
        breaker.TryAcquirePermit(out var thirdOwnsProbe).Should().BeFalse();
        thirdOwnsProbe.Should().BeFalse();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);

        // Sanity: once H releases its own probe, a fresh probe becomes available again.
        breaker.ReleasePermit();
        breaker.TryAcquirePermit(out var afterRelease).Should().BeTrue();
        afterRelease.Should().BeTrue();
    }

    private Agent365CircuitBreaker OpenBreaker()
    {
        var breaker = new Agent365CircuitBreaker(() => _now);
        for (var i = 0; i < 5; i++)
        {
            breaker.RecordTransientFailure();
        }

        return breaker;
    }
}
