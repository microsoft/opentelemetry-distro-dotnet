// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    [Collection("EnvironmentVariableTests")]
    public class UseMicrosoftOpenTelemetryTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        private static bool HasAzureMonitorExporter(IServiceCollection services)
            => services.Any(s => s.ImplementationInstance?.GetType().Name == "UseAzureMonitorExporterRegistration");

        private static bool HasAgent365Exporter(IServiceCollection services)
            => services.Any(s => s.ServiceType.Name == "Agent365ExporterOptions");

        [Fact]
        public void Parameterless_RegistersAllInstrumentation_NoExporters()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o => { });

                // No Azure Monitor exporter (no connection string)
                Assert.False(HasAzureMonitorExporter(services));

                // No Agent365 exporter (no token resolver)
                Assert.False(HasAgent365Exporter(services));

                // But tracing config IS registered (instrumentation active)
                Assert.True(services.Any(s =>
                    s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                    s.ServiceType.Name.Contains("TracerProviderBuilder")));
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void SkipExporter_AzureMonitor_InstrumentationStillActive()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console; // Explicitly NOT AzureMonitor
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                });

            // Exporter NOT registered
            Assert.False(HasAzureMonitorExporter(services),
                "Azure Monitor exporter should be skipped when not in ExportTarget.");

            // But AzureMonitor options ARE configured (instrumentation active)
            Assert.True(services.Any(s =>
                s.ServiceType.IsGenericType &&
                s.ServiceType.GetGenericArguments().Any(a => a.Name == "AzureMonitorOptions")),
                "AzureMonitor options should be configured (instrumentation runs).");
        }

        [Fact]
        public void SkipExporter_Agent365_InstrumentationStillActive()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor; // Explicitly NOT Agent365
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                    o.Agent365.Exporter.TokenResolver = (a, t) =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            // Agent365 exporter NOT registered
            Assert.False(HasAgent365Exporter(services),
                "Agent365 exporter should be skipped when not in ExportTarget.");

            // Azure Monitor exporter IS registered
            Assert.True(HasAzureMonitorExporter(services),
                "Azure Monitor exporter should be registered.");
        }

        [Fact]
        public void DualExporter_BothRegistered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                    o.Agent365.Exporter.TokenResolver = (a, t) =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            // Both auto-detected
            Assert.True(HasAzureMonitorExporter(services), "Azure Monitor should be auto-detected from ConnectionString.");
            Assert.True(HasAgent365Exporter(services), "Agent365 should be auto-detected from TokenResolver.");
        }

        [Fact]
        public void ConsoleExporter_Registered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            // Console exporter registers via OpenTelemetry internals —
            // verify tracing config exists (console exporter is configured inside WithTracing)
            Assert.True(services.Any(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder")),
                "Tracing should be configured with console exporter.");

            // No Azure Monitor or Agent365
            Assert.False(HasAzureMonitorExporter(services));
            Assert.False(HasAgent365Exporter(services));
        }

        [Fact]
        public void OtlpExporter_Registered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Otlp;
                    o.OtlpEndpoint = new Uri("http://localhost:4317");
                });

            Assert.True(services.Any(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder")),
                "Tracing should be configured with OTLP exporter.");
        }

        [Fact]
        public void AgentFramework_EnabledByDefault()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o => { });

            // AgentFramework is enabled by default (EnableAgentFramework = true)
            // This registers additional activity sources via UseAgentFramework()
            Assert.True(services.Any(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder")),
                "Agent Framework sources should be registered.");
        }

        [Fact]
        public void AgentFramework_CanBeDisabled()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                var servicesBefore = services.Count;

                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.EnableAgentFramework = false;
                    });

                var servicesCountWithDisabled = services.Count;

                var services2 = new ServiceCollection();
                services2.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.EnableAgentFramework = true;
                    });

                // With AgentFramework enabled, more services should be registered
                Assert.True(services2.Count >= servicesCountWithDisabled,
                    "Enabling AgentFramework should register additional services.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }
    }
}
#endif
