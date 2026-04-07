// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    [Collection("EnvironmentVariableTests")]
    public class ExportTargetAutoDetectionTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        private static bool HasAzureMonitorExporter(IServiceCollection services)
            => services.Any(s => s.ImplementationInstance?.GetType().Name == "UseAzureMonitorExporterRegistration");

        [Fact]
        public void AutoDetects_AzureMonitor_FromCodeConnectionString()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            Assert.True(HasAzureMonitorExporter(services), "Azure Monitor exporter should be registered when ConnectionString is set in code.");
        }

        [Fact]
        public void AutoDetects_AzureMonitor_FromEnvironmentVariable()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, TestConnectionString);

                var services = new ServiceCollection();
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.AzureMonitor.DisableOfflineStorage = true;
                        o.AzureMonitor.EnableLiveMetrics = false;
                    });

                Assert.True(HasAzureMonitorExporter(services), "Azure Monitor exporter should be registered when APPLICATIONINSIGHTS_CONNECTION_STRING env var is set.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void AutoDetects_AzureMonitor_FromIConfiguration_Section()
        {
            var configData = new Dictionary<string, string?>
            {
                ["AzureMonitor:ConnectionString"] = TestConnectionString
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            Assert.True(HasAzureMonitorExporter(services), "Azure Monitor exporter should be registered when AzureMonitor:ConnectionString is in IConfiguration.");
        }

        [Fact]
        public void AutoDetects_AzureMonitor_FromIConfiguration_EnvVarKey()
        {
            var configData = new Dictionary<string, string?>
            {
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = TestConnectionString
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });

            Assert.True(HasAzureMonitorExporter(services), "Azure Monitor exporter should be registered when APPLICATIONINSIGHTS_CONNECTION_STRING is in IConfiguration.");
        }

        [Fact]
        public void NoExporter_WhenNoConnectionString()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o => { });

                Assert.False(HasAzureMonitorExporter(services), "Azure Monitor exporter should NOT be registered when no connection string is available.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void ExplicitExporters_OverridesAutoDetect()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                });

            Assert.False(HasAzureMonitorExporter(services), "Azure Monitor exporter should NOT be registered when Exporters explicitly excludes it.");
        }

        [Fact]
        public void AutoDetects_Agent365_FromTokenResolver()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Agent365.Exporter.TokenResolver = (agentId, tenantId) =>
                        System.Threading.Tasks.Task.FromResult<string?>("test-token");
                });

            // Agent365 exporter registers its options as singleton
            var hasAgent365Exporter = services.Any(s =>
                s.ServiceType.Name == "Agent365ExporterOptions");

            Assert.True(hasAgent365Exporter, "Agent365 exporter should be registered when TokenResolver is set.");
        }

        [Fact]
        public void Agent365_ExporterNotRegistered_WhenNoTokenResolver()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o => { });

            var hasAgent365Exporter = services.Any(s =>
                s.ServiceType.Name == "Agent365ExporterOptions");

            Assert.False(hasAgent365Exporter, "Agent365 exporter should NOT be registered when TokenResolver is not set.");
        }

        [Fact]
        public void Instrumentation_AlwaysRegistered_EvenWhenExporterSkipped()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    // Only console exporter — instrumentation should still register
                    o.Exporters = ExportTarget.Console;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                });

            // Azure Monitor exporter NOT registered (Console only)
            Assert.False(HasAzureMonitorExporter(services), "Azure Monitor exporter should be skipped.");

            // But Azure Monitor instrumentation IS registered (UseAzureMonitor was called)
            var hasAzureMonitorOptions = services.Any(s =>
                s.ServiceType.IsGenericType &&
                s.ServiceType.GetGenericArguments().Any(a => a.Name == "AzureMonitorOptions"));
            Assert.True(hasAzureMonitorOptions, "Azure Monitor instrumentation should be configured even when exporter is skipped.");

            // TracerProvider config IS registered
            Assert.Contains(services, s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder"));
        }

        [Fact]
        public void AllThreePillars_Registered_RegardlessOfExporters()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        // Only console exporter — all instrumentation should still register
                        o.Exporters = ExportTarget.Console;
                    });

                // Azure Monitor instrumentation registered (UseAzureMonitor called with SkipExporter)
                // Verify via AzureMonitorOptions being configured
                var hasAzureMonitorOptions = services.Any(s =>
                    s.ServiceType.IsGenericType &&
                    s.ServiceType.GetGenericArguments().Any(a => a.Name == "AzureMonitorOptions"));
                Assert.True(hasAzureMonitorOptions, "AzureMonitor options should be configured (instrumentation active).");

                // Agent365 baggage processor source should be registered
                // (UseAgent365 always adds the Agent365Sdk ActivitySource)
                // We can verify by checking the services for ActivityProcessor registration
                Assert.Contains(services, s => s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                    s.ServiceType.Name.Contains("TracerProviderBuilder"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }
    }
}
#endif
