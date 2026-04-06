// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    /// <summary>
    /// Tests that evaluate initialization of the Microsoft OpenTelemetry distro.
    /// </summary>
    public class InitializationTests
    {
        private const string TestConnectionString = "InstrumentationKey=unitTest";

        [Fact]
        public async Task VerifyCannotCallUseAzureMonitorTwice()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .UseAzureMonitor()
                .UseAzureMonitor();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            await Assert.ThrowsAsync<NotSupportedException>(async () => await StartHostedServicesAsync(serviceProvider));
        }

        [Fact]
        public async Task VerifyCannotCallUseAzureMonitorExporterTwice()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .UseAzureMonitorExporter()
                .UseAzureMonitorExporter();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            await Assert.ThrowsAsync<NotSupportedException>(async () => await StartHostedServicesAsync(serviceProvider));
        }

        [Fact]
        public async Task VerifyCannotCallUseAzureMonitorExporterAndUseAzureMonitor()
        {
            var serviceCollection = new ServiceCollection();

            var otelBuilder = serviceCollection.AddOpenTelemetry();
            otelBuilder.UseAzureMonitorExporter();
            otelBuilder.UseAzureMonitor();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            await Assert.ThrowsAsync<NotSupportedException>(async () => await StartHostedServicesAsync(serviceProvider));
        }

        [Fact]
        public void VerifyCanCallAddAzureMonitorTraceExporterTwice()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .WithTracing(b => b
                    .AddAzureMonitorTraceExporter(x => x.ConnectionString = TestConnectionString)
                    .AddAzureMonitorTraceExporter(x => x.ConnectionString = TestConnectionString));

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var tracerProvider = serviceProvider.GetRequiredService<TracerProvider>();
            Assert.NotNull(tracerProvider);
        }

        [Fact]
        public void VerifyCanCallAddAzureMonitorMetricExporterTwice()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .WithMetrics(b => b
                    .AddAzureMonitorMetricExporter(x => x.ConnectionString = TestConnectionString)
                    .AddAzureMonitorMetricExporter(x => x.ConnectionString = TestConnectionString));

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();
            Assert.NotNull(meterProvider);
        }

        [Fact]
        public void VerifyCanCallAddAzureMonitorLogExporterTwice()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .WithLogging(b => b
                    .AddAzureMonitorLogExporter(x => x.ConnectionString = TestConnectionString)
                    .AddAzureMonitorLogExporter(x => x.ConnectionString = TestConnectionString));

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var loggerProvider = serviceProvider.GetService<LoggerProvider>();
            Assert.NotNull(loggerProvider);
        }

        [Fact]
        public async Task VerifyUseAzureMonitor_ProvidersAreCreated()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .UseAzureMonitor(options =>
                {
                    options.ConnectionString = TestConnectionString;
                    options.DisableOfflineStorage = true;
                    options.EnableLiveMetrics = false;
                });

            using var serviceProvider = serviceCollection.BuildServiceProvider();
            await StartHostedServicesAsync(serviceProvider);

            var tracerProvider = serviceProvider.GetRequiredService<TracerProvider>();
            Assert.NotNull(tracerProvider);

            var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();
            Assert.NotNull(meterProvider);

            var loggerProvider = serviceProvider.GetService<LoggerProvider>();
            Assert.NotNull(loggerProvider);
        }

        [Fact]
        public async Task VerifyUseAzureMonitorExporter_ProvidersAreCreated()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddOpenTelemetry()
                .UseAzureMonitorExporter(options =>
                {
                    options.ConnectionString = TestConnectionString;
                    options.EnableLiveMetrics = false;
                });

            using var serviceProvider = serviceCollection.BuildServiceProvider();
            await StartHostedServicesAsync(serviceProvider);

            var tracerProvider = serviceProvider.GetRequiredService<TracerProvider>();
            Assert.NotNull(tracerProvider);

            var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();
            Assert.NotNull(meterProvider);

            var loggerProvider = serviceProvider.GetService<LoggerProvider>();
            Assert.NotNull(loggerProvider);
        }

        private static async Task StartHostedServicesAsync(ServiceProvider serviceProvider)
        {
            var hostedServices = serviceProvider.GetServices<IHostedService>();
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
        }
    }
}
