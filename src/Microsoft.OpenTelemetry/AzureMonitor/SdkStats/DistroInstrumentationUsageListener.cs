// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Observes activity source and metric instrument publication during a bounded startup window.
    /// </summary>
    internal sealed class DistroInstrumentationUsageListener : IDisposable
    {
        internal static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromMinutes(10);

        private const DistroInstrumentation TracingInstrumentations =
            DistroInstrumentation.AzureSdk
            | DistroInstrumentation.AspNetCore
            | DistroInstrumentation.HttpClient
            | DistroInstrumentation.SqlClient
            | DistroInstrumentation.OpenAI
            | DistroInstrumentation.SemanticKernel
            | DistroInstrumentation.AgentFramework
            | DistroInstrumentation.Agent365;

        private const DistroInstrumentation MetricsInstrumentations =
            DistroInstrumentation.AspNetCore
            | DistroInstrumentation.HttpClient
            | DistroInstrumentation.AgentFramework;

        private readonly ActivityListener? _activityListener;
        private readonly MeterListener? _meterListener;
        private readonly Timer _observationTimer;

        // Publication callbacks may overlap. A stale write can only restore enabled candidate bits,
        // causing duplicate work or deferring early shutdown; usage marking is idempotent and the
        // observation timer remains the authoritative cleanup boundary.
        private DistroInstrumentation _remainingInstrumentations;
        private bool _started;
        private bool _disposed;

        internal DistroInstrumentationUsageListener(
            DistroInstrumentation enabledInstrumentations,
            bool observeActivitySources,
            bool observeMetricInstruments,
            TimeSpan? observationWindow = null)
        {
            var effectiveObservationWindow = observationWindow ?? DefaultObservationWindow;
            if (effectiveObservationWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observationWindow),
                    "The instrumentation observation window must be greater than zero.");
            }

            _remainingInstrumentations = enabledInstrumentations;

            if (observeActivitySources)
            {
                _activityListener = new ActivityListener
                {
                    ShouldListenTo = ObserveActivitySource,
                };
            }

            if (observeMetricInstruments)
            {
                _meterListener = new MeterListener
                {
                    InstrumentPublished = ObserveMetricInstrument,
                };
            }

            _observationTimer = new Timer(
                static state => ((DistroInstrumentationUsageListener)state!).Dispose(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            if (_activityListener != null)
            {
                ActivitySource.AddActivityListener(_activityListener);
            }

            _meterListener?.Start();

            _observationTimer.Change(
                effectiveObservationWindow,
                Timeout.InfiniteTimeSpan);
            _started = true;
            if (_remainingInstrumentations == DistroInstrumentation.None)
            {
                ScheduleStop();
            }
        }

        internal bool IsListening => !_disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _observationTimer.Dispose();
            _activityListener?.Dispose();
            _meterListener?.Dispose();
        }

        internal static DistroInstrumentation GetEnabledInstrumentations(InstrumentationOptions options)
        {
            var enabled = DistroInstrumentation.None;
            if (options.EnableAzureSdkInstrumentation)
            {
                enabled |= DistroInstrumentation.AzureSdk;
            }

            if (options.EnableAspNetCoreInstrumentation)
            {
                enabled |= DistroInstrumentation.AspNetCore;
            }

            if (options.EnableHttpClientInstrumentation)
            {
                enabled |= DistroInstrumentation.HttpClient;
            }

            if (options.EnableSqlClientInstrumentation)
            {
                enabled |= DistroInstrumentation.SqlClient;
            }

            if (options.EnableOpenAIInstrumentation)
            {
                enabled |= DistroInstrumentation.OpenAI;
            }

            if (options.EnableSemanticKernelInstrumentation)
            {
                enabled |= DistroInstrumentation.SemanticKernel;
            }

            if (options.EnableAgentFrameworkInstrumentation)
            {
                enabled |= DistroInstrumentation.AgentFramework;
            }

            if (options.EnableAgent365Instrumentation)
            {
                enabled |= DistroInstrumentation.Agent365;
            }

            var supported = DistroInstrumentation.None;
            if (options.EnableTracing)
            {
                supported |= TracingInstrumentations;
            }

            if (options.EnableMetrics)
            {
                supported |= MetricsInstrumentations;
            }

            return enabled & supported;
        }

        internal static DistroInstrumentation GetInstrumentations(string sourceName) =>
            GetInstrumentations(sourceName, candidates: null);

        private static DistroInstrumentation GetInstrumentations(
            string sourceName,
            DistroInstrumentation? candidates)
        {
            var instrumentations = DistroInstrumentation.None;

            if (IsCandidate(candidates, DistroInstrumentation.AzureSdk)
                && sourceName.StartsWith("Azure.", StringComparison.Ordinal)
                && !sourceName.StartsWith("Azure.Monitor.OpenTelemetry", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AzureSdk;
            }

            if (IsCandidate(candidates, DistroInstrumentation.AspNetCore)
                && sourceName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AspNetCore;
            }

            if (IsCandidate(candidates, DistroInstrumentation.HttpClient)
                && sourceName.StartsWith("System.Net.Http", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.HttpClient;
            }

            if (IsCandidate(candidates, DistroInstrumentation.SqlClient)
                && (sourceName.StartsWith("OpenTelemetry.Instrumentation.SqlClient", StringComparison.Ordinal)
                    || sourceName.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
                    || sourceName.StartsWith("System.Data.SqlClient", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.SqlClient;
            }

            if (IsCandidate(candidates, DistroInstrumentation.OpenAI)
                && (sourceName.StartsWith("Azure.AI.OpenAI", StringComparison.Ordinal)
                    || sourceName.StartsWith("OpenAI.", StringComparison.Ordinal)
                    || string.Equals(sourceName, "Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal)))
            {
                instrumentations |= DistroInstrumentation.OpenAI;
            }

            if (IsCandidate(candidates, DistroInstrumentation.SemanticKernel)
                && sourceName.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SemanticKernel;
            }

            if (IsCandidate(candidates, DistroInstrumentation.AgentFramework)
                && sourceName.StartsWith("Experimental.Microsoft.Agents.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AgentFramework;
            }

            if (IsCandidate(candidates, DistroInstrumentation.Agent365)
                && string.Equals(sourceName, "Agent365Sdk", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.Agent365;
            }

            return instrumentations;
        }

        private static bool IsCandidate(
            DistroInstrumentation? candidates,
            DistroInstrumentation instrumentation) =>
            candidates == null
            || (candidates.Value & instrumentation) != DistroInstrumentation.None;

        private bool ObserveActivitySource(ActivitySource source)
        {
            ObservePublication(source.Name);

            // This listener observes source publication only and never receives activities.
            return false;
        }

        private void ObserveMetricInstrument(Instrument instrument, MeterListener listener)
        {
            ObservePublication(instrument.Meter.Name);
        }

        private void ObservePublication(string name)
        {
            if (_disposed)
            {
                return;
            }

            var remaining = _remainingInstrumentations;
            if (remaining == DistroInstrumentation.None)
            {
                return;
            }

            var newlyObserved = GetInstrumentations(name, remaining);
            if (newlyObserved == DistroInstrumentation.None)
            {
                return;
            }

            DistroSdkStatsUsage.MarkInstrumentationInUse(newlyObserved);

            var updatedRemaining = remaining & ~newlyObserved;
            _remainingInstrumentations = updatedRemaining;
            if (_started && updatedRemaining == DistroInstrumentation.None)
            {
                ScheduleStop();
            }
        }

        private void ScheduleStop()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _observationTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Dispose won the race with a publication callback.
            }
        }
    }
}
