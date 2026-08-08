// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal sealed class Agent365TransmissionGate
    {
        internal static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(1);

        private readonly object _lock = new();
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Random _random;

        private enum GateState { Closed, Backoff, Probe }

        private GateState _state;
        private int _consecutiveErrors;
        private DateTimeOffset _nextProbeTime;
        private bool _probeInFlight;

        internal Agent365TransmissionGate(Func<DateTimeOffset>? utcNow = null, Random? random = null)
        {
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _random = random ?? Random.Shared;
        }

        internal int ConsecutiveErrors
        {
            get { lock (_lock) { return _consecutiveErrors; } }
        }

        internal TimeSpan CurrentDelay
        {
            get { lock (_lock) { return _nextProbeTime - _utcNow(); } }
        }

        internal bool TryAcquire(out bool ownsProbe)
        {
            lock (_lock)
            {
                TransitionIfReady();

                ownsProbe = false;

                if (_state == GateState.Closed)
                {
                    return true;
                }

                if (_state == GateState.Probe && !_probeInFlight)
                {
                    _probeInFlight = true;
                    ownsProbe = true;
                    return true;
                }

                return false;
            }
        }

        internal void RecordSuccess()
        {
            lock (_lock)
            {
                _state = GateState.Closed;
                _consecutiveErrors = 0;
                _probeInFlight = false;
            }
        }

        internal void RecordRetryableFailure(TimeSpan? retryAfter)
        {
            lock (_lock)
            {
                _consecutiveErrors++;
                _probeInFlight = false;

                TimeSpan delay;
                if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                {
                    delay = retryAfter.Value > MaximumDelay ? MaximumDelay : retryAfter.Value;
                }
                else
                {
                    delay = CalculateFallback();
                }

                var now = _utcNow();
                // Guard against overflow
                var maxSafe = DateTimeOffset.MaxValue - now;
                _nextProbeTime = now + (delay > maxSafe ? maxSafe : delay);
                _state = GateState.Backoff;
            }
        }

        internal void ReleaseProbe()
        {
            lock (_lock)
            {
                if (_state == GateState.Probe)
                {
                    _probeInFlight = false;
                }
            }
        }

        internal static bool IsRetryable(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return statusCode == HttpStatusCode.Unauthorized
                || statusCode == HttpStatusCode.RequestTimeout
                || code == 429
                || code >= 500 && code <= 599;
        }

        private void TransitionIfReady()
        {
            if (_state == GateState.Backoff && _utcNow() >= _nextProbeTime)
            {
                _state = GateState.Probe;
                _probeInFlight = false;
            }
        }

        private TimeSpan CalculateFallback()
        {
            var slot = (Math.Pow(2, _consecutiveErrors) - 1) / 2;
            var upperSeconds = Math.Max(
                MinimumDelay.TotalSeconds,
                Math.Min(slot * MinimumDelay.TotalSeconds, MaximumDelay.TotalSeconds));
            var seconds = _random.Next(1, Math.Max(2, (int)Math.Ceiling(upperSeconds)));
            return TimeSpan.FromSeconds(
                Math.Max(MinimumDelay.TotalSeconds, Math.Min(seconds, MaximumDelay.TotalSeconds)));
        }
    }
}
