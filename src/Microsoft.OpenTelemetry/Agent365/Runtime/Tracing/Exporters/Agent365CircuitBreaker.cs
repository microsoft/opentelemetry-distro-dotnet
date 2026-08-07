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

        internal bool TryAcquirePermit()
        {
            lock (_gate)
            {
                TransitionToHalfOpenIfReady();

                if (_state == Agent365CircuitState.Closed)
                {
                    return true;
                }

                if (_state == Agent365CircuitState.HalfOpen && !_probeInFlight)
                {
                    _probeInFlight = true;
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

        internal void RecordTransientFailure()
        {
            lock (_gate)
            {
                _consecutiveFailures++;
                _lastFailureTime = _utcNow();
                _probeInFlight = false;

                if (_state == Agent365CircuitState.HalfOpen
                    || _consecutiveFailures >= FailureThreshold)
                {
                    _state = Agent365CircuitState.Open;
                }
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
