// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection("EnvironmentVariableTests")]
    public class DistroFeatureSdkStatsTests
    {
        private const string ValidConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

        public DistroFeatureSdkStatsTests()
        {
            DistroFeatureSdkStats.ResetForTesting();
        }

        [Fact]
        public void Observe_ReturnsMeasurementWithExpectedTags()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-test")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            var measurements = CollectObservableMeasurements();

            var match = Assert.Single(measurements, m => m.tags.TryGetValue("version", out var v) && (string?)v == "mot9.9.9-test");

            // The numeric value equals the feature mask.
            Assert.Equal((long)snapshot.Features, match.value);

            Assert.Equal("dotnet", match.tags["language"]);
            Assert.Equal(0, match.tags["type"]);
            Assert.Equal(snapshot.CustomerInstrumentationKey, match.tags["cikey"]);
            Assert.Equal((long)snapshot.Features, match.tags["feature"]);
            Assert.True(match.tags.ContainsKey("rp"));
            Assert.True(match.tags.ContainsKey("attach"));
            Assert.True(match.tags.ContainsKey("os"));
        }

        [Fact]
        public void Observe_WhenFeaturesAreNone_EmitsNoMeasurement()
        {
            // Exercises the spec-mandated short-circuit: when the snapshot's feature mask is
            // DistroFeature.None, the observable gauge MUST return zero measurements (not a
            // default Measurement<long>(), which would still publish a phantom zero data point
            // with no tags). Use the internal test factory to construct a None-masked snapshot
            // directly — DistroFeatureSnapshot.Build always sets at least Distro|AgentFramework
            // so it cannot produce a None snapshot through the normal code path.
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.None,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-none");

            DistroFeatureSdkStats.Initialize(snapshot);

            var measurements = CollectObservableMeasurements();

            Assert.Empty(measurements);

            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient);
            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var instrumentation = Assert.Single(CollectObservableMeasurements());
            Assert.Equal(1, instrumentation.tags["type"]);
            Assert.Equal((long)DistroInstrumentation.HttpClient, instrumentation.value);
        }

        [Fact]
        public void Observe_InstrumentationIsAbsentUntilObservedAndThenUsesTypeOne()
        {
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.None,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-instrumentation");
            DistroFeatureSdkStats.Initialize(snapshot);

            Assert.Empty(CollectObservableMeasurements());

            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient);

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var measurement = Assert.Single(CollectObservableMeasurements());
            Assert.Equal((long)DistroInstrumentation.HttpClient, measurement.value);
            Assert.Equal((long)DistroInstrumentation.HttpClient, measurement.tags["feature"]);
            Assert.Equal(1, measurement.tags["type"]);
        }

        [Fact]
        public void Observe_EmitsFeatureAndInstrumentationMasksIndependently()
        {
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-types");
            DistroFeatureSdkStats.Initialize(snapshot);
            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.SqlClient);

            var measurements = CollectObservableMeasurements();

            var feature = Assert.Single(measurements, measurement => (int)measurement.tags["type"]! == 0);
            Assert.Equal((long)DistroFeature.Distro, feature.value);
            Assert.Equal((long)DistroFeature.Distro, feature.tags["feature"]);

            var instrumentation = Assert.Single(measurements, measurement => (int)measurement.tags["type"]! == 1);
            Assert.Equal((long)DistroInstrumentation.SqlClient, instrumentation.value);
            Assert.Equal((long)DistroInstrumentation.SqlClient, instrumentation.tags["feature"]);
        }

        [Fact]
        public void UsageRegistry_UpdatesConcurrentlyAndNeverClearsObservedBits()
        {
            Parallel.Invoke(
                () => DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.LiveMetrics),
                () => DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.AgentFramework),
                () => DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient),
                () => DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.SqlClient));

            DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.LiveMetrics);
            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient);

            Assert.Equal(
                DistroFeature.LiveMetrics | DistroFeature.AgentFramework,
                DistroSdkStatsUsage.Features);
            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void Observe_NewMaskWaitsForSharedLongInterval()
        {
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-dynamic");
            DistroFeatureSdkStats.Initialize(snapshot);

            var initial = Assert.Single(CollectObservableMeasurements());
            Assert.Equal((long)DistroFeature.Distro, initial.value);

            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient);

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var update = CollectObservableMeasurements();
            Assert.Equal(2, update.Count);
            var instrumentation = Assert.Single(
                update,
                measurement => (int)measurement.tags["type"]! == 1);
            Assert.Equal((long)DistroInstrumentation.HttpClient, instrumentation.value);
        }

        [Fact]
        public void Initialize_ReplacesSnapshotAtNextScheduledCollection()
        {
            var initialSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "old-cikey",
                distroVersion: "1.0.0");
            var instance = DistroFeatureSdkStats.Initialize(initialSnapshot);
            Assert.Single(CollectObservableMeasurements());

            var updatedSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro | DistroFeature.LiveMetrics,
                customerInstrumentationKey: "new-cikey",
                distroVersion: "2.0.0");

            Assert.Same(instance, DistroFeatureSdkStats.Initialize(updatedSnapshot));

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var measurement = Assert.Single(CollectObservableMeasurements());
            Assert.Equal(
                (long)(DistroFeature.Distro | DistroFeature.LiveMetrics),
                measurement.value);
            Assert.Equal("new-cikey", measurement.tags["cikey"]);
            Assert.Equal("mot2.0.0", measurement.tags["version"]);
        }

        [Fact]
        public void Initialize_StrictSubsetClearsOldConfigurationBitsButKeepsRuntimeFeatures()
        {
            var initialSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro | DistroFeature.LiveMetrics,
                customerInstrumentationKey: "N/A",
                distroVersion: "1.0.0");
            DistroFeatureSdkStats.Initialize(initialSnapshot);
            DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.AgentFramework);

            var initial = Assert.Single(CollectObservableMeasurements());
            Assert.Equal(
                (long)(DistroFeature.Distro | DistroFeature.LiveMetrics | DistroFeature.AgentFramework),
                initial.value);

            var strictSubsetSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "N/A",
                distroVersion: "1.0.0");
            DistroFeatureSdkStats.Initialize(strictSubsetSnapshot);

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var updated = Assert.Single(CollectObservableMeasurements());
            Assert.Equal(
                (long)(DistroFeature.Distro | DistroFeature.AgentFramework),
                updated.value);
            Assert.False(
                ((DistroFeature)updated.value).HasFlag(DistroFeature.LiveMetrics));
            Assert.True(
                DistroSdkStatsUsage.Features.HasFlag(DistroFeature.AgentFramework));
        }

        [Fact]
        public void Initialize_TagOnlyChangeWaitsAndThenReemitsBothTypesWithNewTags()
        {
            var initialSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "old-cikey",
                distroVersion: "1.0.0");
            DistroFeatureSdkStats.Initialize(initialSnapshot);
            DistroSdkStatsUsage.MarkInstrumentationInUse(DistroInstrumentation.HttpClient);

            Assert.Equal(2, CollectObservableMeasurements().Count);
            Assert.Empty(CollectObservableMeasurements());

            var updatedSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "new-cikey",
                distroVersion: "2.0.0");
            DistroFeatureSdkStats.Initialize(updatedSnapshot);

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var updated = CollectObservableMeasurements();
            Assert.Equal(2, updated.Count);
            Assert.Contains(updated, measurement =>
                (int)measurement.tags["type"]! == 0
                && measurement.value == (long)DistroFeature.Distro);
            Assert.Contains(updated, measurement =>
                (int)measurement.tags["type"]! == 1
                && measurement.value == (long)DistroInstrumentation.HttpClient);
            Assert.All(updated, measurement =>
            {
                Assert.Equal("new-cikey", measurement.tags["cikey"]);
                Assert.Equal("mot2.0.0", measurement.tags["version"]);
            });
        }

        [Fact]
        public async Task Initialize_SynchronizesSnapshotAndUsageWithObserve()
        {
            var initialSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "old-cikey",
                distroVersion: "1.0.0");
            var instance = DistroFeatureSdkStats.Initialize(initialSnapshot);
            Assert.Single(CollectObservableMeasurements());

            var updatedSnapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro | DistroFeature.LiveMetrics,
                customerInstrumentationKey: "new-cikey",
                distroVersion: "2.0.0");
            var emissionLock = typeof(DistroFeatureSdkStats)
                .GetField("_emissionLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(instance)!;
            using var started = new ManualResetEventSlim();

            Task<DistroFeatureSdkStats>? update = null;
            Monitor.Enter(emissionLock);
            try
            {
                update = Task.Run(() =>
                {
                    started.Set();
                    return DistroFeatureSdkStats.Initialize(updatedSnapshot);
                });
                Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                Assert.False(update.IsCompleted);
            }
            finally
            {
                Monitor.Exit(emissionLock);
            }

            Assert.Same(instance, await update!);

            Assert.Empty(CollectObservableMeasurements());

            MakeNextCollectionEligible();
            var measurement = Assert.Single(CollectObservableMeasurements());
            Assert.Equal(
                (long)(DistroFeature.Distro | DistroFeature.LiveMetrics),
                measurement.value);
            Assert.Equal("new-cikey", measurement.tags["cikey"]);
            Assert.Equal("mot2.0.0", measurement.tags["version"]);
        }

        [Fact]
        public void Observe_WithoutAzureMonitorConnectionString_UsesNAForCikey()
        {
            // Deployments without Azure Monitor (OTLP-only, Console-only, A365-only) still
            // report Feature SDKStats; the spec convention is to populate the cikey dimension
            // with the literal "N/A" so backend KQL doesn't need to filter out missing rows.
            var options = new MicrosoftOpenTelemetryOptions();
            // No ConnectionString set.

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                connectionString: null,
                ExportTarget.Otlp,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-otlp-only");

            Assert.NotNull(snapshot);
            Assert.Equal(DistroFeatureSnapshot.NoCustomerInstrumentationKey, snapshot!.CustomerInstrumentationKey);
            Assert.Equal("N/A", snapshot.CustomerInstrumentationKey);

            DistroFeatureSdkStats.Initialize(snapshot);
            var measurements = CollectObservableMeasurements();

            var match = Assert.Single(measurements, m => m.tags.TryGetValue("version", out var v) && (string?)v == "mot9.9.9-otlp-only");
            Assert.Equal("N/A", match.tags["cikey"]);
            Assert.Equal((long)snapshot.Features, match.value);
        }

        [Fact]
        public void Observe_ThrottlesToSingleEmission_AcrossRapidCollections()
        {
            // The exporter collects this gauge on the shared 15-minute reader. Verify the
            // throttle holds it to one emission per 24 hr window so Feature stats don't ship
            // every 15 min.
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-throttle")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            const int simulatedCollections = 5;
            int emissions = 0;
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroFeatureSdkStats.MeterName
                        && instrument.Name == DistroFeatureSdkStats.MetricName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>((_, _, _, _) => emissions++);
            listener.Start();

            for (int i = 0; i < simulatedCollections; i++)
            {
                listener.RecordObservableInstruments();
            }

            // 5 collections at the 15-min cadence, but the throttle allows only one until the
            // 24 hr window elapses.
            Assert.Equal(1, emissions);
        }

        [Fact]
        public void Observe_ConcurrentCallbacksEmitOnlyOncePerUnchangedMask()
        {
            var snapshot = DistroFeatureSnapshot.CreateForTesting(
                DistroFeature.Distro,
                customerInstrumentationKey: "N/A",
                distroVersion: "9.9.9-concurrent");
            DistroFeatureSdkStats.Initialize(snapshot);

            int emissions = 0;
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroFeatureSdkStats.MeterName
                        && instrument.Name == DistroFeatureSdkStats.MetricName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>(
                (_, _, _, _) => Interlocked.Increment(ref emissions));
            listener.Start();

            Parallel.For(0, 16, _ => listener.RecordObservableInstruments());

            Assert.Equal(1, emissions);
        }

        [Fact]
        public void Observe_EmitsAgain_WhenClockJumpsBackwards()
        {
            // Simulate the last emission being recorded ~48 hr in the future, i.e. the wall
            // clock has since jumped backwards (NTP/VM sync). The backwards-jump guard must
            // allow an emission now instead of suppressing until wall-clock time catches up.
            var options = new MicrosoftOpenTelemetryOptions();
            options.AzureMonitor.ConnectionString = ValidConnectionString;

            var snapshot = DistroFeatureSnapshot.Build(
                options,
                ValidConnectionString,
                ExportTarget.AzureMonitor,
                customerSdkStatsEnabled: false,
                a365OnlyMode: false,
                distroVersion: "9.9.9-clockback")!;

            DistroFeatureSdkStats.Initialize(snapshot);

            Assert.Single(CollectObservableMeasurements());
            Assert.Empty(CollectObservableMeasurements());

            var instance = DistroFeatureSdkStats.Instance!;
            long futureTicks = DateTime.UtcNow.Ticks + TimeSpan.FromHours(48).Ticks;
            typeof(DistroFeatureSdkStats)
                .GetField("_lastCollectionTicks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(instance, futureTicks);

            var measurements = CollectObservableMeasurements();

            Assert.Single(measurements);
        }

        [Fact]
        public void Observe_LongExportIntervalOverride_ShortensThrottleWindow()
        {
            // A 1s override makes an emission 2s ago eligible again (the 24h default would suppress it).
            const string longIntervalEnvVar = "APPLICATIONINSIGHTS_STATS_LONG_EXPORT_INTERVAL";
            var previous = Environment.GetEnvironmentVariable(longIntervalEnvVar);
            Environment.SetEnvironmentVariable(longIntervalEnvVar, "1");
            try
            {
                DistroFeatureSdkStats.ResetForTesting();

                var options = new MicrosoftOpenTelemetryOptions();
                options.AzureMonitor.ConnectionString = ValidConnectionString;
                var snapshot = DistroFeatureSnapshot.Build(
                    options,
                    ValidConnectionString,
                    ExportTarget.AzureMonitor,
                    customerSdkStatsEnabled: false,
                    a365OnlyMode: false,
                    distroVersion: "9.9.9-longoverride")!;

                DistroFeatureSdkStats.Initialize(snapshot);

                Assert.Single(CollectObservableMeasurements());

                // Backdate the last emission past the 1s window.
                var instance = DistroFeatureSdkStats.Instance!;
                long twoSecondsAgo = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(2).Ticks;
                typeof(DistroFeatureSdkStats)
                    .GetField("_lastCollectionTicks", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(instance, twoSecondsAgo);

                Assert.Single(CollectObservableMeasurements());
            }
            finally
            {
                Environment.SetEnvironmentVariable(longIntervalEnvVar, previous);
                DistroFeatureSdkStats.ResetForTesting();
            }
        }

        private static List<(long value, Dictionary<string, object?> tags)> CollectObservableMeasurements()
        {
            var results = new List<(long value, Dictionary<string, object?> tags)>();

            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == DistroFeatureSdkStats.MeterName
                        && instrument.Name == DistroFeatureSdkStats.MetricName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var dict = new Dictionary<string, object?>(tags.Length);
                for (int i = 0; i < tags.Length; i++)
                {
                    dict[tags[i].Key] = tags[i].Value;
                }
                results.Add((value, dict));
            });
            listener.Start();
            listener.RecordObservableInstruments();
            return results;
        }

        private static void MakeNextCollectionEligible()
        {
            var instance = DistroFeatureSdkStats.Instance!;
            long previousIntervalTicks =
                DateTime.UtcNow.Ticks - DistroFeatureSdkStats.EmissionInterval.Ticks - 1;
            typeof(DistroFeatureSdkStats)
                .GetField("_lastCollectionTicks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(instance, previousIntervalTicks);
        }
    }
}
