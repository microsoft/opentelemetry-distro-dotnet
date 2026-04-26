// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    [Collection("EnvironmentVariableTests")]
    public class NonHostedExporterTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Fact]
        public void AzureMonitor_ExporterHostedService_RemovedFromDI()
        {
            // When Azure Monitor is enabled, the ExporterRegistrationHostedService
            // should be removed from IHostedService registrations to prevent
            // double-initialization in hosted apps.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            // The Azure Monitor ExporterRegistrationHostedService should have been
            // removed from IHostedService registrations
            var hostedServiceDescriptors = services
                .Where(s => s.ServiceType == typeof(IHostedService))
                .ToList();

            // Should NOT contain any Azure Monitor exporter hosted service
            foreach (var descriptor in hostedServiceDescriptors)
            {
                if (descriptor.ImplementationFactory != null)
                {
                    var declaringType = descriptor.ImplementationFactory.Method.DeclaringType?.FullName ?? string.Empty;
                    Assert.DoesNotContain("ExporterRegistration", declaringType);
                }
            }
        }

        [Fact]
        public void AzureMonitor_OtherHostedServices_NotRemoved()
        {
            // Other IHostedService registrations (like A365OnlyModeStartupLogger)
            // should NOT be removed when Azure Monitor exporter service is removed.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor | ExportTarget.Console;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            // A365OnlyModeStartupLogger should still be registered
            // (it's not Azure Monitor related)
            var hostedServiceDescriptors = services
                .Where(s => s.ServiceType == typeof(IHostedService))
                .ToList();

            // Should have at least one hosted service remaining (A365OnlyModeStartupLogger, etc.)
            Assert.NotEmpty(hostedServiceDescriptors);
        }

        [Fact]
        public void NoAzureMonitor_HostedServices_Untouched()
        {
            // When Azure Monitor is NOT enabled, no hosted services should be removed.
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = System.Environment.GetEnvironmentVariable(envVar);
            try
            {
                System.Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.Exporters = ExportTarget.Console;
                    });

                // No Azure Monitor → no removal → all hosted services intact
                var hostedServiceDescriptors = services
                    .Where(s => s.ServiceType == typeof(IHostedService))
                    .ToList();

                // A365OnlyModeStartupLogger should be registered
                Assert.NotEmpty(hostedServiceDescriptors);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void AzureMonitor_MeterProviderCallback_Registered()
        {
            // When Azure Monitor is enabled and the hosted service is removed,
            // a ConfigureOpenTelemetryMeterProvider callback should be registered
            // to initialize the exporter during MeterProvider build.
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            // Verify MeterProvider configuration callbacks are registered
            // (ConfigureOpenTelemetryMeterProvider adds IConfigureMeterProviderBuilder)
            Assert.Contains(services, s =>
                s.ServiceType.Name.Contains("IConfigureMeterProviderBuilder") ||
                s.ServiceType.Name.Contains("MeterProviderBuilder"));
        }
    }
}
#endif
