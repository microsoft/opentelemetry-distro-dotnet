// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    /// <summary>
    /// Agent365 needs every span and installs an AlwaysOn sampler, which must not replace the
    /// Azure Monitor sampler unless spans are actually exported to Agent365.
    /// </summary>
    [Collection("EnvironmentVariableTests")]
    public class SamplerSelectionTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Fact]
        public void AzureMonitorOnly_UsesRateLimitedSamplerByDefault()
        {
            using var sdk = CreateSdk(ExportTarget.AzureMonitor);

            Assert.Equal("RateLimitedSampler", GetSampler(sdk.TracerProvider).GetType().Name);
        }

        [Fact]
        public void AzureMonitorOnly_UsesFixedRateSamplerWhenTracesPerSecondIsCleared()
        {
            using var sdk = CreateSdk(ExportTarget.AzureMonitor, o =>
            {
                o.AzureMonitor.SamplingRatio = 0.5F;
                o.AzureMonitor.TracesPerSecond = null;
            });

            Assert.Equal("ApplicationInsightsSampler", GetSampler(sdk.TracerProvider).GetType().Name);
        }

        /// <summary>
        /// Repro from https://github.com/microsoft/opentelemetry-distro-dotnet/issues/187.
        /// </summary>
        [Fact]
        public void AzureMonitorOnly_SamplingRatioZeroSamplesNothing()
        {
            var sourceName = $"{nameof(SamplerSelectionTests)}.{Guid.NewGuid():N}";
            using var sdk = CreateSdk(ExportTarget.AzureMonitor, o =>
            {
                o.AzureMonitor.SamplingRatio = 0F;
                o.AzureMonitor.TracesPerSecond = null;
            }, sourceName);

            using var source = new ActivitySource(sourceName);
            int sampled = 0;
            for (int i = 0; i < 100; i++)
            {
                using var activity = source.StartActivity("operation");
                if (activity?.Recorded == true)
                {
                    sampled++;
                }
            }

            Assert.Equal(0, sampled);
        }

        [Theory]
        [InlineData(ExportTarget.Agent365)]
        [InlineData(ExportTarget.Agent365 | ExportTarget.AzureMonitor)]
        public void Agent365Export_UsesAlwaysOnParentBasedSampler(ExportTarget exporters)
        {
            using var sdk = CreateSdk(exporters);

            var sampler = GetSampler(sdk.TracerProvider);
            Assert.IsType<ParentBasedSampler>(sampler);
            Assert.True(SamplesChildOfUnsampledLocalParent(sampler));
        }

        [Theory]
        [InlineData(ExportTarget.Otlp)]
        [InlineData(ExportTarget.Console)]
        public void NoAgent365Export_DoesNotInstallAgent365Sampler(ExportTarget exporters)
        {
            using var sdk = CreateSdk(exporters);

            // OpenTelemetry's default ParentBased(AlwaysOn) is also a ParentBasedSampler, so the
            // Agent365 sampler is identified by its AlwaysOn handling of unsampled parents.
            var sampler = GetSampler(sdk.TracerProvider);
            Assert.IsType<ParentBasedSampler>(sampler);
            Assert.False(SamplesChildOfUnsampledLocalParent(sampler));
        }

        private static OpenTelemetrySdk CreateSdk(ExportTarget exporters, Action<MicrosoftOpenTelemetryOptions>? configure = null, string? sourceName = null)
        {
            return OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = exporters;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                    o.Agent365.TokenResolver = (a, t) => System.Threading.Tasks.Task.FromResult<string?>("token");
                    configure?.Invoke(o);
                });

                if (sourceName != null)
                {
                    otel.WithTracing(t => t.AddSource(sourceName));
                }
            });
        }

        private static Sampler GetSampler(TracerProvider tracerProvider)
        {
            var samplerProperty = tracerProvider.GetType().GetProperty("Sampler", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(samplerProperty);
            var sampler = samplerProperty!.GetValue(tracerProvider) as Sampler;
            Assert.NotNull(sampler);
            return sampler!;
        }

        private static bool SamplesChildOfUnsampledLocalParent(Sampler sampler)
        {
            var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, isRemote: false);
            var result = sampler.ShouldSample(new SamplingParameters(parent, parent.TraceId, "child", ActivityKind.Internal));
            return result.Decision == SamplingDecision.RecordAndSample;
        }
    }
}

#endif
