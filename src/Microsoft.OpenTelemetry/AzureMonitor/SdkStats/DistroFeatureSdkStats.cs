// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Owns the distro's Feature and Instrumentation SDKStats meter and observable gauge. The Azure Monitor
    /// exporter's Statsbeat <c>MeterProvider</c> subscribes to the meter name advertised by
    /// this class (see <c>StatsbeatConstants.DistroFeatureSdkStatsMeterName</c> in the
    /// exporter). That provider collects on the shared 15-minute reader. Unchanged masks
    /// throttle to one emission per <see cref="EmissionInterval"/> (24 hr), while newly
    /// observed usage emits on the next collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class is a process-wide singleton because the underlying <see cref="Meter"/> is a
    /// process-wide resource and the exporter's MeterProvider subscribes by name. Subsequent
    /// <see cref="Initialize"/> calls atomically swap the snapshot reference so the most
    /// recent <c>UseMicrosoftOpenTelemetry</c> configuration wins; this keeps the disposable
    /// surface of the class minimal and matches the lifecycle of the singleton MeterProvider.
    /// </para>
    /// <para>
    /// Per the SDKStats specification, the gauge returns no measurements when the
    /// current snapshot features combined with runtime-observed features and the current
    /// instrumentation bit mask are all empty. The
    /// <see cref="Func{TResult}"/> of <see cref="IEnumerable{T}"/> overload is used (not the
    /// single-<c>Measurement</c> overload) so an empty result actually skips emission
    /// instead of publishing a phantom zero-valued data point with no tags.
    /// </para>
    /// </remarks>
    internal sealed class DistroFeatureSdkStats : IDisposable
    {
        /// <summary>
        /// Meter name owned by the distro for Feature SDKStats. Must match the constant
        /// subscribed by the Azure Monitor exporter's Statsbeat <see cref="Meter"/> provider.
        /// </summary>
        internal const string MeterName = "MicrosoftOpenTelemetryFeatureSdkStatsMeter";

        /// <summary>Metric name per the SDKStats spec.</summary>
        internal const string MetricName = "Feature";

        /// <summary>Meter version reported alongside <see cref="MeterName"/>.</summary>
        internal const string MeterVersion = "1.0";

        /// <summary>
        /// Default throttle interval for Feature SDKStats (24h); overridable via
        /// <see cref="EnvironmentVariableConstants.APPLICATIONINSIGHTS_STATS_LONG_EXPORT_INTERVAL"/>.
        /// </summary>
        internal static readonly TimeSpan EmissionInterval = TimeSpan.FromHours(24);

        private static DistroFeatureSdkStats? s_instance;
        private static readonly object s_lock = new();

        private static readonly IEnumerable<Measurement<long>> EmptyMeasurements = Array.Empty<Measurement<long>>();

        private readonly Meter _meter;

        private readonly TimeSpan _emissionInterval;
        private readonly object _emissionLock = new();

        private DistroFeatureSnapshot _snapshot;

        private long _lastFeatureEmissionTicks;
        private long _lastInstrumentationEmissionTicks;
        private DistroFeature _lastEmittedFeatures;
        private DistroInstrumentation _lastEmittedInstrumentations;
        private DistroFeatureSnapshot? _lastFeatureSnapshot;
        private DistroFeatureSnapshot? _lastInstrumentationSnapshot;

        private DistroFeatureSdkStats(DistroFeatureSnapshot snapshot)
        {
            // Snapshot is assigned before the meter is created and before the instance is
            // published to s_instance, so any reader that observes the instance via the
            // public Instance property is guaranteed to see a fully-initialized object
            // (no narrow window where _snapshot is null).
            _snapshot = snapshot;
            _emissionInterval = ResolveEmissionInterval();
            _meter = new Meter(MeterName, MeterVersion);
            _meter.CreateObservableGauge<long>(MetricName, this.Observe);
        }

        // Resolve the throttle interval (seconds) from the env var, else the 24h default.
        private static TimeSpan ResolveEmissionInterval()
        {
            string? value;
            try
            {
                value = Environment.GetEnvironmentVariable(
                    EnvironmentVariableConstants.APPLICATIONINSIGHTS_STATS_LONG_EXPORT_INTERVAL);
            }
            catch (Exception)
            {
                return EmissionInterval;
            }

            if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return EmissionInterval;
        }

        /// <summary>The active singleton, if <see cref="Initialize"/> has been called.</summary>
        internal static DistroFeatureSdkStats? Instance => s_instance;

        /// <summary>
        /// Registers (or updates) the distro Feature SDKStats producer with the supplied
        /// snapshot. Safe to call repeatedly; the most recent snapshot wins.
        /// </summary>
        /// <param name="snapshot">Bit map + cikey + distro version describing the configuration.</param>
        /// <remarks>
        /// The Statsbeat <c>MeterProvider</c> that ships our <c>Feature</c> measurement is
        /// brought up either by the customer's own <c>AzureMonitorMetricExporter</c> (when
        /// Azure Monitor is selected) or by the distro's process-wide
        /// <c>SdkStatsPin</c> (eagerly created in
        /// <c>MicrosoftOpenTelemetryBuilderExtensions.TryEnsureSdkStatsPin</c>) when
        /// it is not. Either way, the pin's lifetime is managed outside this class.
        /// </remarks>
        internal static DistroFeatureSdkStats Initialize(DistroFeatureSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (s_lock)
            {
                if (s_instance is null)
                {
                    s_instance = new DistroFeatureSdkStats(snapshot);
                }
                else
                {
                    lock (s_instance._emissionLock)
                    {
                        Volatile.Write(ref s_instance._snapshot, snapshot);
                    }
                }

                return s_instance;
            }
        }

        /// <summary>
        /// Releases the singleton instance and disposes the underlying meter. Test-only;
        /// production code keeps the singleton alive for the lifetime of the process.
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (s_lock)
            {
                s_instance?.Dispose();
                s_instance = null;
                DistroSdkStatsUsage.ResetForTesting();
            }
        }

        public void Dispose()
        {
            _meter.Dispose();
        }

        private IEnumerable<Measurement<long>> Observe()
        {
            bool emissionNeeded;
            lock (_emissionLock)
            {
                try
                {
                    var snapshot = Volatile.Read(ref _snapshot);
                    var features = snapshot.Features | DistroSdkStatsUsage.Features;
                    var instrumentations = DistroSdkStatsUsage.Instrumentations;
                    long nowTicks = DateTime.UtcNow.Ticks;
                    emissionNeeded =
                        ShouldEmit(
                            (ulong)features,
                            (ulong)_lastEmittedFeatures,
                            _lastFeatureEmissionTicks,
                            snapshot,
                            _lastFeatureSnapshot,
                            nowTicks)
                        || ShouldEmit(
                            (ulong)instrumentations,
                            (ulong)_lastEmittedInstrumentations,
                            _lastInstrumentationEmissionTicks,
                            snapshot,
                            _lastInstrumentationSnapshot,
                            nowTicks);
                }
                catch (Exception ex)
                {
                    AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                    return EmptyMeasurements;
                }
            }

            if (!emissionNeeded)
            {
                return EmptyMeasurements;
            }

            string resourceProvider;
            string attachMode;
            string operatingSystem;
            try
            {
                // The first resource-provider lookup may perform IMDS discovery. Keep that
                // potentially blocking work outside the emission lock, then revalidate below.
                resourceProvider = ResourceProviderHelper.GetResourceProvider();
                attachMode = ResourceProviderHelper.GetAttachMode();
                operatingSystem = ResourceProviderHelper.GetOperatingSystem();
            }
            catch (Exception ex)
            {
                AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                return EmptyMeasurements;
            }

            lock (_emissionLock)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                var features = snapshot.Features | DistroSdkStatsUsage.Features;
                var instrumentations = DistroSdkStatsUsage.Instrumentations;
                long nowTicks = DateTime.UtcNow.Ticks;
                bool emitFeatures = ShouldEmit(
                    (ulong)features,
                    (ulong)_lastEmittedFeatures,
                    _lastFeatureEmissionTicks,
                    snapshot,
                    _lastFeatureSnapshot,
                    nowTicks);
                bool emitInstrumentations = ShouldEmit(
                    (ulong)instrumentations,
                    (ulong)_lastEmittedInstrumentations,
                    _lastInstrumentationEmissionTicks,
                    snapshot,
                    _lastInstrumentationSnapshot,
                    nowTicks);

                if (!emitFeatures && !emitInstrumentations)
                {
                    return EmptyMeasurements;
                }

                try
                {
                    var measurements = new List<Measurement<long>>(2);
                    if (emitInstrumentations)
                    {
                        measurements.Add(CreateMeasurement(
                            (long)instrumentations,
                            type: 1,
                            snapshot,
                            resourceProvider,
                            attachMode,
                            operatingSystem));
                    }

                    if (emitFeatures)
                    {
                        measurements.Add(CreateMeasurement(
                            (long)features,
                            type: 0,
                            snapshot,
                            resourceProvider,
                            attachMode,
                            operatingSystem));
                    }

                    if (emitInstrumentations)
                    {
                        _lastEmittedInstrumentations = instrumentations;
                        _lastInstrumentationEmissionTicks = nowTicks;
                        _lastInstrumentationSnapshot = snapshot;
                    }

                    if (emitFeatures)
                    {
                        _lastEmittedFeatures = features;
                        _lastFeatureEmissionTicks = nowTicks;
                        _lastFeatureSnapshot = snapshot;
                    }

                    return measurements;
                }
                catch (Exception ex)
                {
                    AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                    return EmptyMeasurements;
                }
            }
        }

        private bool ShouldEmit(
            ulong currentMask,
            ulong lastEmittedMask,
            long lastEmissionTicks,
            DistroFeatureSnapshot currentSnapshot,
            DistroFeatureSnapshot? lastEmissionSnapshot,
            long nowTicks)
        {
            if (currentMask == 0)
            {
                return false;
            }

            if (currentMask != lastEmittedMask
                || !ReferenceEquals(currentSnapshot, lastEmissionSnapshot))
            {
                return true;
            }

            long elapsedTicks = nowTicks - lastEmissionTicks;
            return lastEmissionTicks == 0
                || elapsedTicks < 0
                || elapsedTicks >= _emissionInterval.Ticks;
        }

        private static Measurement<long> CreateMeasurement(
            long mask,
            int type,
            DistroFeatureSnapshot snapshot,
            string resourceProvider,
            string attachMode,
            string operatingSystem) =>
            new Measurement<long>(
                mask,
                new KeyValuePair<string, object?>("rp", resourceProvider),
                new KeyValuePair<string, object?>("attach", attachMode),
                new KeyValuePair<string, object?>("cikey", snapshot.CustomerInstrumentationKey),
                new KeyValuePair<string, object?>("feature", mask),
                new KeyValuePair<string, object?>("type", type),
                new KeyValuePair<string, object?>("os", operatingSystem),
                new KeyValuePair<string, object?>("language", "dotnet"),
                new KeyValuePair<string, object?>("version", SdkVersion.GetSdkStatsVersion(snapshot.DistroVersion)));
    }
}
