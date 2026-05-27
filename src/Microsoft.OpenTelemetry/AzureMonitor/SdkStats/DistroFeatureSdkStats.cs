// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.OpenTelemetry.AzureMonitor.Internals;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Owns the distro's Feature SDKStats meter and observable gauge. The Azure Monitor
    /// exporter's Statsbeat <c>MeterProvider</c> subscribes to the meter name advertised by
    /// this class (see <c>StatsbeatConstants.DistroFeatureSdkStatsMeterName</c> in the
    /// exporter), so measurements are exported on the existing 24-hour Statsbeat cadence
    /// without any further wiring on the distro side.
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
    /// Per the SDKStats specification, the gauge returns an empty measurement when the
    /// configured features bit mask is <see cref="DistroFeature.None"/>.
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

        private static DistroFeatureSdkStats? s_instance;
        private static readonly object s_lock = new();

        private readonly Meter _meter;
        private DistroFeatureSnapshot? _snapshot;
        private IDisposable? _statsbeatExporterPin;

        private DistroFeatureSdkStats()
        {
            _meter = new Meter(MeterName, MeterVersion);
            _meter.CreateObservableGauge(MetricName, this.Observe);
        }

        /// <summary>The active singleton, if <see cref="Initialize"/> has been called.</summary>
        internal static DistroFeatureSdkStats? Instance => s_instance;

        /// <summary>
        /// Registers (or updates) the distro Feature SDKStats producer with the supplied
        /// snapshot. Safe to call repeatedly; the most recent snapshot wins.
        /// </summary>
        /// <param name="snapshot">Bit map + cikey + distro version describing the configuration.</param>
        /// <param name="statsbeatExporterPin">
        /// Optional <see cref="IDisposable"/> kept alive for the lifetime of this singleton.
        /// Used by the distro to pin an inert <c>AzureMonitorMetricExporter</c> whose
        /// construction side-effect creates the Statsbeat <c>MeterProvider</c> that ships
        /// our <c>Feature</c> measurement when the customer hasn't selected the Azure
        /// Monitor exporter. Pass <see langword="null"/> when the customer's own Azure
        /// Monitor exporter already triggers Statsbeat for the process.
        /// </param>
        internal static DistroFeatureSdkStats Initialize(
            DistroFeatureSnapshot snapshot,
            IDisposable? statsbeatExporterPin = null)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (s_instance is null)
            {
                lock (s_lock)
                {
                    s_instance ??= new DistroFeatureSdkStats();
                }
            }

            Volatile.Write(ref s_instance!._snapshot, snapshot);

            // The first pin wins for the process lifetime. If a second call supplies a pin
            // while one is already held, dispose the redundant one immediately — the cached
            // transmitter behind it lives in TransmitterFactory and will continue to serve
            // the already-attached Statsbeat MeterProvider.
            if (statsbeatExporterPin != null)
            {
                if (Interlocked.CompareExchange(ref s_instance._statsbeatExporterPin, statsbeatExporterPin, null) != null)
                {
                    try { statsbeatExporterPin.Dispose(); } catch { /* best effort */ }
                }
            }

            return s_instance;
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
            }
        }

        public void Dispose()
        {
            _meter.Dispose();
            try { _statsbeatExporterPin?.Dispose(); } catch { /* best effort */ }
        }

        private Measurement<long> Observe()
        {
            try
            {
                var snapshot = Volatile.Read(ref _snapshot);
                if (snapshot is null || snapshot.Features == DistroFeature.None)
                {
                    // SDKStats spec: "Don't send feature/instrumentation SDKStats when the
                    // feature/instrumentation list is empty."
                    return new Measurement<long>();
                }

                return new Measurement<long>(
                    (long)snapshot.Features,
                    new KeyValuePair<string, object?>("rp", ResourceProviderHelper.GetResourceProvider()),
                    new KeyValuePair<string, object?>("attach", ResourceProviderHelper.GetAttachMode()),
                    new KeyValuePair<string, object?>("cikey", snapshot.CustomerInstrumentationKey),
                    new KeyValuePair<string, object?>("feature", (long)snapshot.Features),
                    new KeyValuePair<string, object?>("type", 0),
                    new KeyValuePair<string, object?>("os", ResourceProviderHelper.GetOperatingSystem()),
                    new KeyValuePair<string, object?>("language", "dotnet"),
                    new KeyValuePair<string, object?>("version", snapshot.DistroVersion));
            }
            catch (Exception ex)
            {
                AzureMonitorAspNetCoreEventSource.Log.DistroFeatureSdkStatsCallbackFailed(ex);
                return new Measurement<long>();
            }
        }
    }
}
