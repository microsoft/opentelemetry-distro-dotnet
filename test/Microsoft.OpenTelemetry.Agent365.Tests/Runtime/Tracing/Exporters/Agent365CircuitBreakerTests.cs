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
