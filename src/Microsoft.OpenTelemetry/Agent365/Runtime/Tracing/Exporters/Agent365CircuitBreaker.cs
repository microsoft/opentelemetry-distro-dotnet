// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal enum Agent365CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    internal sealed class Agent365CircuitBreaker
    {
        internal const int FailureThreshold = 5;
        internal static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);

        private readonly object _gate = new();
        private readonly Func<DateTimeOffset> _utcNow;
        private Agent365CircuitState _state;
        private int _consecutiveFailures;
        private DateTimeOffset? _lastFailureTime;
        private bool _probeInFlight;

        internal Agent365CircuitBreaker(Func<DateTimeOffset>? utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        internal Agent365CircuitState State
        {
            get
            {
                lock (_gate)
                {
                    TransitionToHalfOpenIfReady();
                    return _state;
                }
            }
        }

        internal bool TryAcquirePermit() => TryAcquirePermit(out _);

        /// <summary>
        /// Attempts to acquire a permit to send. Reports via <paramref name="acquiredHalfOpenProbe"/>
        /// whether this call took ownership of the single half-open probe, so the caller can release
        /// exactly that probe (and never another invocation's) when it finishes.
        /// </summary>
        /// <param name="acquiredHalfOpenProbe">
        /// <c>true</c> only when the circuit was HalfOpen and this call claimed the sole probe;
        /// <c>false</c> for a Closed-state permit (which owns no probe) or when no permit was granted.
        /// </param>
        /// <returns><c>true</c> when sending is permitted; otherwise <c>false</c>.</returns>
        internal bool TryAcquirePermit(out bool acquiredHalfOpenProbe)
        {
            lock (_gate)
            {
                TransitionToHalfOpenIfReady();

                acquiredHalfOpenProbe = false;

                if (_state == Agent365CircuitState.Closed)
                {
                    return true;
                }

                if (_state == Agent365CircuitState.HalfOpen && !_probeInFlight)
                {
                    _probeInFlight = true;
                    acquiredHalfOpenProbe = true;
                    return true;
                }

                return false;
            }
        }

        internal void RecordSuccess()
        {
            lock (_gate)
            {
                _state = Agent365CircuitState.Closed;
                _consecutiveFailures = 0;
                _lastFailureTime = null;
                _probeInFlight = false;
            }
        }

        /// <summary>
        /// Releases an acquired half-open probe without recording success or failure and without
        /// changing the circuit state, failure count, or recovery timestamp.
        /// Does nothing when the circuit is Closed or Open.
        /// </summary>
        internal void ReleasePermit()
        {
            lock (_gate)
            {
                if (_state == Agent365CircuitState.HalfOpen)
                {
                    _probeInFlight = false;
                }
            }
        }

        internal void RecordTransientFailure()
        {
            lock (_gate)
            {
                _probeInFlight = false;

                if (_state == Agent365CircuitState.HalfOpen)
                {
                    // Reopening circuit from half-open probe failure
                    _lastFailureTime = _utcNow();
                    _state = Agent365CircuitState.Open;
                }
                else if (_state == Agent365CircuitState.Closed)
                {
                    // Track failures while closed
                    _consecutiveFailures++;

                    // Only transition and record time when threshold reached
                    if (_consecutiveFailures >= FailureThreshold)
                    {
                        _lastFailureTime = _utcNow();
                        _state = Agent365CircuitState.Open;
                    }
                }
                // If already Open, do nothing - don't update _lastFailureTime
            }
        }

        private void TransitionToHalfOpenIfReady()
        {
            if (_state == Agent365CircuitState.Open
                && _lastFailureTime.HasValue
                && _utcNow() - _lastFailureTime.Value >= RecoveryTimeout)
            {
                _state = Agent365CircuitState.HalfOpen;
                _probeInFlight = false;
            }
        }
    }
}
