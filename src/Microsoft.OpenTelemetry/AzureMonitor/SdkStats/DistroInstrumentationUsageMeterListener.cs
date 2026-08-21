// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Metrics;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Marks enabled instrumentations when their metric instruments are published.
    /// </summary>
    internal sealed class DistroInstrumentationUsageMeterListener : IDisposable
    {
        private readonly DistroInstrumentation _enabledInstrumentations;
        private readonly MeterListener _listener;

        internal DistroInstrumentationUsageMeterListener(
            DistroInstrumentation enabledInstrumentations)
        {
            _enabledInstrumentations = enabledInstrumentations;
            _listener = new MeterListener
            {
                InstrumentPublished = EnableKnownInstrumentation,
            };

            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void EnableKnownInstrumentation(Instrument instrument, MeterListener listener)
        {
            var instrumentation =
                DistroInstrumentationUsageActivityListener.GetInstrumentations(instrument.Meter.Name)
                & _enabledInstrumentations;
            if (instrumentation != DistroInstrumentation.None)
            {
                DistroSdkStatsUsage.MarkInstrumentationInUse(instrumentation);

                if ((instrumentation & DistroInstrumentation.AgentFramework) != 0)
                {
                    DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.AgentFramework);
                }
            }
        }
    }
}
