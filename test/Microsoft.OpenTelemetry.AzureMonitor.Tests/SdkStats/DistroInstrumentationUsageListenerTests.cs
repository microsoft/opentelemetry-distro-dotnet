// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
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
        public void ActivityListener_DoesNotReportDisabledOrNonmatchingSources()
        {
            using var usageListener = new DistroInstrumentationUsageActivityListener(
                DistroInstrumentation.HttpClient);
            DistroSdkStatsUsage.ResetForTesting();

            using var sqlSource =
                new ActivitySource("OpenTelemetry.Instrumentation.SqlClient.Tests");
            using var customSource = new ActivitySource("Customer.CustomSource");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void ActivityListener_TracksSourcePublicationWithoutActivities()
        {
            using var usageListener = new DistroInstrumentationUsageActivityListener(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient);
            DistroSdkStatsUsage.ResetForTesting();

            using var httpSource = new ActivitySource("System.Net.Http.Tests");
            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);

            using var sqlSource =
                new ActivitySource("OpenTelemetry.Instrumentation.SqlClient.Tests");
            using var duplicateHttpSource = new ActivitySource("System.Net.Http.Tests.Duplicate");

            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void ActivityListener_TracksSourcesCreatedBeforeListener()
        {
            DistroSdkStatsUsage.ResetForTesting();
            using var source = new ActivitySource("Microsoft.SemanticKernel.Tests");

            using var usageListener = new DistroInstrumentationUsageActivityListener(
                DistroInstrumentation.SemanticKernel);

            Assert.True(
                DistroSdkStatsUsage.Instrumentations.HasFlag(
                    DistroInstrumentation.SemanticKernel));
        }

        [Fact]
        public void ActivityListener_AgentFrameworkUpdatesOnlyInstrumentationUsage()
        {
            using var usageListener = new DistroInstrumentationUsageActivityListener(
                DistroInstrumentation.AgentFramework);
            DistroSdkStatsUsage.ResetForTesting();

            using var source =
                new ActivitySource("Experimental.Microsoft.Agents.AI.Agent");

            Assert.Equal(
                DistroInstrumentation.AgentFramework,
                DistroSdkStatsUsage.Instrumentations);
            Assert.Equal(DistroFeature.None, DistroSdkStatsUsage.Features);
        }

        [Fact]
        public void ActivityListener_MicrosoftExtensionsAiMarksOpenAIOnly()
        {
            using var usageListener = new DistroInstrumentationUsageActivityListener(
                DistroInstrumentation.OpenAI | DistroInstrumentation.AgentFramework);
            DistroSdkStatsUsage.ResetForTesting();

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
                DistroInstrumentationUsageActivityListener.GetEnabledInstrumentations(options));
        }

        [Fact]
        public void MeterListener_TracksInstrumentPublicationWithoutMeasurements()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient);
            DistroSdkStatsUsage.ResetForTesting();
            using var httpMeter = new Meter("System.Net.Http.Tests");
            using var sqlMeter = new Meter("Microsoft.Data.SqlClient.Tests");

            Assert.Equal(
                DistroInstrumentation.None,
                DistroSdkStatsUsage.Instrumentations);

            _ = httpMeter.CreateCounter<long>("requests");
            Assert.Equal(
                DistroInstrumentation.HttpClient,
                DistroSdkStatsUsage.Instrumentations);

            _ = sqlMeter.CreateObservableGauge("commands", () => 0L);
            Assert.Equal(
                DistroInstrumentation.HttpClient | DistroInstrumentation.SqlClient,
                DistroSdkStatsUsage.Instrumentations);
        }

        [Fact]
        public void MeterListener_TracksInstrumentsCreatedBeforeListener()
        {
            DistroSdkStatsUsage.ResetForTesting();
            using var meter = new Meter("Agent365Sdk");
            _ = meter.CreateCounter<long>("requests");

            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.Agent365);

            Assert.True(
                DistroSdkStatsUsage.Instrumentations.HasFlag(
                    DistroInstrumentation.Agent365));
        }

        [Fact]
        public void MeterListener_DoesNotReportDisabledOrNonmatchingInstruments()
        {
            using var usageListener = new DistroInstrumentationUsageMeterListener(
                DistroInstrumentation.HttpClient);
            DistroSdkStatsUsage.ResetForTesting();
            using var sqlMeter = new Meter("Microsoft.Data.SqlClient.Tests");
            using var customMeter = new Meter("Customer.CustomMeter");

            _ = sqlMeter.CreateCounter<long>("commands");
            _ = customMeter.CreateCounter<long>("operations");

            Assert.Equal(
                DistroInstrumentation.None,
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
                DistroInstrumentationUsageActivityListener.GetInstrumentations(sourceName));
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
                DistroInstrumentationUsageActivityListener.GetInstrumentations(sourceName));
        }
    }
}
