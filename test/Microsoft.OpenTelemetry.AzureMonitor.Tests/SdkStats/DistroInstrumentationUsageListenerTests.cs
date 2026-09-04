// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.SdkStats
{
    [Collection("EnvironmentVariableTests")]
    public class DistroInstrumentationUsageListenerTests
    {
        public DistroInstrumentationUsageListenerTests()
        {
            DistroFeatureSdkStats.ResetForTesting();
        }

        [Fact]
        public void DefaultObservationWindow_IsTenMinutes()
        {
            Assert.Equal(
                TimeSpan.FromMinutes(10),
                DistroInstrumentationUsageListener.DefaultObservationWindow);
        }

        [Fact]
        public void Listener_DoesNotReportDisabledOrNonmatchingPublications()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.Agent365,
                observeActivitySources: true,
                observeMetricInstruments: true);
            using var disabledSource =
                new ActivitySource("Experimental.Microsoft.Agents.AI.Disabled");
            using var customSource = new ActivitySource("Customer.CustomSource");
            using var disabledMeter =
                new Meter("Experimental.Microsoft.Agents.AI.Disabled");
            using var customMeter = new Meter("Customer.CustomMeter");

            _ = disabledMeter.CreateCounter<long>("disabled");
            _ = customMeter.CreateCounter<long>("custom");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(usageListener.IsListening);
        }

        [Fact]
        public void Listener_StopsEarlyAfterAllCandidatesAreFoundAcrossSignals()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365,
                observeActivitySources: true,
                observeMetricInstruments: true);

            using var agentFrameworkSource =
                new ActivitySource("Experimental.Microsoft.Agents.AI.Tests");
            using var duplicateAgentFrameworkSource =
                new ActivitySource("Experimental.Microsoft.Agents.AI.Tests.Duplicate");

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(usageListener.IsListening);

            using var duplicateAgentFrameworkMeter =
                new Meter("Experimental.Microsoft.Agents.AI.Tests.Duplicate");
            _ = duplicateAgentFrameworkMeter.CreateCounter<long>("requests");
            Assert.True(usageListener.IsListening);

            using var agent365Meter = new Meter("Agent365Sdk");
            _ = agent365Meter.CreateCounter<long>("requests");

            Assert.Equal(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(
                SpinWait.SpinUntil(
                    () => !usageListener.IsListening,
                    TimeSpan.FromSeconds(5)));

            DistroSdkStatsUsage.ResetForTesting();
            using var publicationAfterStop = new ActivitySource("Agent365Sdk");
            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public async Task Listener_HandlesConcurrentDuplicatePublications()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365,
                observeActivitySources: true,
                observeMetricInstruments: true);
            var publications = new Task[32];

            for (var i = 0; i < publications.Length; i++)
            {
                var publicationIndex = i;
                publications[i] = Task.Run(() =>
                {
                    if (publicationIndex % 2 == 0)
                    {
                        using var source = new ActivitySource(
                            $"Experimental.Microsoft.Agents.AI.Concurrent.{publicationIndex}");
                    }
                    else
                    {
                        using var meter = new Meter("Agent365Sdk");
                        _ = meter.CreateCounter<long>($"requests-{publicationIndex}");
                    }
                });
            }

            await Task.WhenAll(publications);

            Assert.Equal(
                DistroInstrumentation.AgentFramework | DistroInstrumentation.Agent365,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public async Task Listener_DisposeIsIdempotent()
        {
            var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.Agent365,
                observeActivitySources: true,
                observeMetricInstruments: true);

            await Task.WhenAll(
                Task.Run(usageListener.Dispose),
                Task.Run(usageListener.Dispose));

            Assert.False(usageListener.IsListening);
            using var source = new ActivitySource("Agent365Sdk");
            using var meter = new Meter("Agent365Sdk");
            _ = meter.CreateCounter<long>("requests-after-dispose");
            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void Listener_StopsAtObservationWindow()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.Agent365,
                observeActivitySources: true,
                observeMetricInstruments: true,
                observationWindow: TimeSpan.FromMilliseconds(50));

            Assert.True(
                SpinWait.SpinUntil(
                    () => !usageListener.IsListening,
                    TimeSpan.FromSeconds(5)));

            DistroSdkStatsUsage.ResetForTesting();
            using var source = new ActivitySource("Agent365Sdk");
            using var meter = new Meter("Agent365Sdk");
            _ = meter.CreateCounter<long>("requests");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void Listener_TracksActivitySourcesCreatedBeforeListener()
        {
            using var source =
                new ActivitySource("Experimental.Microsoft.Agents.AI.PreExisting");
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.AgentFramework,
                observeActivitySources: true,
                observeMetricInstruments: false);

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(
                SpinWait.SpinUntil(
                    () => !usageListener.IsListening,
                    TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void Listener_TracksMetricInstrumentsCreatedBeforeListener()
        {
            using var meter = new Meter("Agent365Sdk");
            _ = meter.CreateCounter<long>("requests");

            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.Agent365,
                observeActivitySources: false,
                observeMetricInstruments: true);

            Assert.Equal(
                DistroInstrumentation.Agent365,
                DistroSdkStatsUsage.Instrumentations);
            Assert.True(
                SpinWait.SpinUntil(
                    () => !usageListener.IsListening,
                    TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void Listener_AgentFrameworkUpdatesOnlyInstrumentationUsage()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.AgentFramework,
                observeActivitySources: true,
                observeMetricInstruments: false);
            using var source =
                new ActivitySource("Experimental.Microsoft.Agents.AI.Agent");

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void Listener_MicrosoftExtensionsAiMarksOpenAIOnly()
        {
            using var usageListener = new DistroInstrumentationUsageListener(
                DistroInstrumentation.OpenAI | DistroInstrumentation.AgentFramework,
                observeActivitySources: true,
                observeMetricInstruments: false);
            using var source =
                new ActivitySource("Experimental.Microsoft.Extensions.AI");

            Assert.Equal(
                DistroInstrumentation.OpenAI,
                DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void GetEnabledInstrumentations_UsesOnlyEnabledOptions()
        {
            var options = new InstrumentationOptions
            {
                EnableAzureSdkInstrumentation = false,
                EnableAspNetCoreInstrumentation = true,
                EnableHttpClientInstrumentation = false,
                EnableSqlClientInstrumentation = true,
                EnableOpenAIInstrumentation = false,
                EnableSemanticKernelInstrumentation = true,
                EnableAgentFrameworkInstrumentation = false,
                EnableAgent365Instrumentation = true,
            };

            Assert.Equal(
                DistroInstrumentation.AspNetCore
                    | DistroInstrumentation.SqlClient
                    | DistroInstrumentation.SemanticKernel
                    | DistroInstrumentation.Agent365,
                DistroInstrumentationUsageListener.GetEnabledInstrumentations(options));
        }

        [Fact]
        public void GetEnabledInstrumentations_MetricsOnlyUsesMetricsSupportedOptions()
        {
            var options = new InstrumentationOptions
            {
                EnableTracing = false,
                EnableMetrics = true,
                EnableAzureSdkInstrumentation = true,
                EnableAspNetCoreInstrumentation = true,
                EnableHttpClientInstrumentation = true,
                EnableSqlClientInstrumentation = true,
                EnableOpenAIInstrumentation = true,
                EnableSemanticKernelInstrumentation = true,
                EnableAgentFrameworkInstrumentation = true,
                EnableAgent365Instrumentation = true,
            };

            Assert.Equal(
                DistroInstrumentation.AspNetCore
                    | DistroInstrumentation.HttpClient
                    | DistroInstrumentation.AgentFramework,
                DistroInstrumentationUsageListener.GetEnabledInstrumentations(options));
        }

        [Fact]
        public void GetEnabledInstrumentations_TracingOnlyUsesAllEnabledOptions()
        {
            var options = new InstrumentationOptions
            {
                EnableTracing = true,
                EnableMetrics = false,
            };

            Assert.Equal(
                DistroInstrumentation.AzureSdk
                    | DistroInstrumentation.AspNetCore
                    | DistroInstrumentation.HttpClient
                    | DistroInstrumentation.SqlClient
                    | DistroInstrumentation.OpenAI
                    | DistroInstrumentation.SemanticKernel
                    | DistroInstrumentation.AgentFramework
                    | DistroInstrumentation.Agent365,
                DistroInstrumentationUsageListener.GetEnabledInstrumentations(options));
        }

        [Fact]
        public void Builder_MetricsOnlyTracksOnlyMetricsSupportedInstrumentations()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(options =>
                {
                    options.Exporters = ExportTarget.Console;
                    options.Instrumentation.EnableTracing = false;
                    options.Instrumentation.EnableMetrics = true;
                    options.Instrumentation.EnableAzureSdkInstrumentation = false;
                    options.Instrumentation.EnableAspNetCoreInstrumentation = false;
                    options.Instrumentation.EnableHttpClientInstrumentation = false;
                    options.Instrumentation.EnableSqlClientInstrumentation = true;
                    options.Instrumentation.EnableOpenAIInstrumentation = false;
                    options.Instrumentation.EnableSemanticKernelInstrumentation = false;
                    options.Instrumentation.EnableAgentFrameworkInstrumentation = true;
                    options.Instrumentation.EnableAgent365Instrumentation = false;
                });

            using var serviceProvider = services.BuildServiceProvider();
            _ = serviceProvider.GetRequiredService<MeterProvider>();
            DistroSdkStatsUsage.ResetForTesting();

            using var sqlMeter = new Meter("Microsoft.Data.SqlClient.Tests.MetricsOnly");
            _ = sqlMeter.CreateCounter<long>("commands");
            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);

            using var agentFrameworkMeter =
                new Meter("Experimental.Microsoft.Agents.AI.Tests.MetricsOnly");
            _ = agentFrameworkMeter.CreateCounter<long>("operations");
            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Theory]
        [InlineData("Azure.Core.Http", 1UL << 0)]
        [InlineData("Microsoft.AspNetCore.Hosting", 1UL << 1)]
        [InlineData("System.Net.Http", 1UL << 2)]
        [InlineData("OpenTelemetry.Instrumentation.SqlClient", 1UL << 3)]
        [InlineData("Microsoft.Data.SqlClient", 1UL << 3)]
        [InlineData("System.Data.SqlClient", 1UL << 3)]
        [InlineData("Azure.AI.OpenAI", (1UL << 0) | (1UL << 4))]
        [InlineData("OpenAI.Chat", 1UL << 4)]
        [InlineData("Experimental.Microsoft.Extensions.AI", 1UL << 4)]
        [InlineData("Microsoft.SemanticKernel", 1UL << 5)]
        [InlineData("Experimental.Microsoft.Agents.AI", 1UL << 6)]
        [InlineData("Agent365Sdk", 1UL << 7)]
        public void GetInstrumentations_MapsKnownSourcesAndMeters(
            string sourceName,
            ulong expected)
        {
            Assert.Equal(
                (DistroInstrumentation)expected,
                DistroInstrumentationUsageListener.GetInstrumentations(sourceName));
        }

        [Theory]
        [InlineData("Azure.Monitor.OpenTelemetry")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.CustomerSdkStats")]
        [InlineData("Experimental.Microsoft.Extensions.AI.Agent")]
        [InlineData("Microsoft.Extensions.AI")]
        [InlineData("Experimental.Microsoft.Agents")]
        [InlineData("Customer.System.Net.Http")]
        [InlineData("Agent365Sdk.Extensions")]
        public void GetInstrumentations_IgnoresNonmatchingAndExporterOwnedNames(
            string sourceName)
        {
            Assert.Equal(
                DistroInstrumentation.None,
                DistroInstrumentationUsageListener.GetInstrumentations(sourceName));
        }
    }
}
