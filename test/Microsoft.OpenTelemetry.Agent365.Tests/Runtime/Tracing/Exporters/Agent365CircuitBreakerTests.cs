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
    public void FailuresWhileClosedDoNotShiftRecoveryWindow()
    {
        // Open the breaker at t=0
        var breaker = OpenBreaker();
        var openTime = _now;

        // Multiple failures while checking state (which might fail) should not shift recovery window
        _now = _now.AddSeconds(10);
        breaker.RecordTransientFailure();
        breaker.RecordTransientFailure();
        breaker.RecordTransientFailure();

        // At t=30, still not ready (exactly at boundary, needs > 30 seconds)
        _now = openTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeFalse();
        breaker.State.Should().Be(Agent365CircuitState.Open);

        // At t=31, recovery window has elapsed (measured from original open time)
        _now = openTime.AddSeconds(31);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
    }

    [TestMethod]
    public void Exactly30SecondsNotEnoughForRecovery()
    {
        var breaker = OpenBreaker();
        var openTime = _now;

        // Exactly 30 seconds later - should not transition
        _now = openTime.AddSeconds(30);
        breaker.TryAcquirePermit().Should().BeFalse();
        breaker.State.Should().Be(Agent365CircuitState.Open);
    }

    [TestMethod]
    public void Exactly31SecondsAllowsHalfOpenTransition()
    {
        var breaker = OpenBreaker();
        var openTime = _now;

        // Exactly 31 seconds later - should allow probe
        _now = openTime.AddSeconds(31);
        breaker.TryAcquirePermit().Should().BeTrue();
        breaker.State.Should().Be(Agent365CircuitState.HalfOpen);
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
