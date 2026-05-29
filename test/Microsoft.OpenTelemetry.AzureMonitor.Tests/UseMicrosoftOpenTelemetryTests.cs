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
                Assert.Contains(services, s =>
                    s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                    s.ServiceType.Name.Contains("TracerProviderBuilder"));
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
            Assert.Contains(services, s =>
                s.ServiceType.IsGenericType &&
                s.ServiceType.GetGenericArguments().Any(a => a.Name == "AzureMonitorOptions"));
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
                    o.Agent365.TokenResolver = (a, t) =>
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
                    o.Agent365.TokenResolver = (a, t) =>
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
            Assert.Contains(services, s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder"));

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
                    });

            Assert.Contains(services, s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder"));
        }

        [Fact]
        public void AgentFramework_AlwaysEnabled()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o => { });

            // AgentFramework is always enabled — sources and processor are registered
            Assert.Contains(services, s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder") ||
                s.ServiceType.Name.Contains("TracerProviderBuilder"));
        }
    }

    [Collection("EnvironmentVariableTests")]
    public class InstrumentationSuppressTests
    {
        [Fact]
        public void SuppressDefault_AllDefaults_AllDisabled()
        {
            var options = new InstrumentationOptions();
            options.SuppressDefaultInfraInstrumentation();

            Assert.False(options.EnableAspNetCoreInstrumentation);
            Assert.False(options.EnableHttpClientInstrumentation);
            Assert.False(options.EnableSqlClientInstrumentation);
            Assert.False(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void SuppressDefault_UserSetOneTrue_OnlyThatStaysEnabled()
        {
            var options = new InstrumentationOptions();
            options.EnableHttpClientInstrumentation = true; // explicit

            options.SuppressDefaultInfraInstrumentation();

            Assert.False(options.EnableAspNetCoreInstrumentation);
            Assert.True(options.EnableHttpClientInstrumentation); // user set
            Assert.False(options.EnableSqlClientInstrumentation);
            Assert.False(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void SuppressDefault_UserSetOneFalse_StaysFalse()
        {
            var options = new InstrumentationOptions();
            options.EnableSqlClientInstrumentation = false; // explicit false

            options.SuppressDefaultInfraInstrumentation();

            Assert.False(options.EnableAspNetCoreInstrumentation);
            Assert.False(options.EnableHttpClientInstrumentation);
            Assert.False(options.EnableSqlClientInstrumentation); // user's explicit false preserved
            Assert.False(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void SuppressDefault_UserSetAllTrue_NoneDisabled()
        {
            var options = new InstrumentationOptions();
            options.EnableAspNetCoreInstrumentation = true;
            options.EnableHttpClientInstrumentation = true;
            options.EnableSqlClientInstrumentation = true;
            options.EnableAzureSdkInstrumentation = true;

            options.SuppressDefaultInfraInstrumentation();

            Assert.True(options.EnableAspNetCoreInstrumentation);
            Assert.True(options.EnableHttpClientInstrumentation);
            Assert.True(options.EnableSqlClientInstrumentation);
            Assert.True(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void SuppressDefault_MixedOverride_CorrectResult()
        {
            var options = new InstrumentationOptions();
            options.EnableAspNetCoreInstrumentation = true;  // explicit true
            options.EnableHttpClientInstrumentation = false; // explicit false
            // SQL and Azure SDK left at default

            options.SuppressDefaultInfraInstrumentation();

            Assert.True(options.EnableAspNetCoreInstrumentation);  // user true
            Assert.False(options.EnableHttpClientInstrumentation); // user false
            Assert.False(options.EnableSqlClientInstrumentation);  // suppressed
            Assert.False(options.EnableAzureSdkInstrumentation);   // suppressed
        }

        [Fact]
        public void Defaults_AllTrue_BeforeSuppress()
        {
            var options = new InstrumentationOptions();

            Assert.True(options.EnableAspNetCoreInstrumentation);
            Assert.True(options.EnableHttpClientInstrumentation);
            Assert.True(options.EnableSqlClientInstrumentation);
            Assert.True(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void GenAiInstrumentation_NotAffectedBySuppress()
        {
            var options = new InstrumentationOptions();
            options.SuppressDefaultInfraInstrumentation();

            Assert.True(options.EnableOpenAIInstrumentation);
            Assert.True(options.EnableSemanticKernelInstrumentation);
            Assert.True(options.EnableAgentFrameworkInstrumentation);
            Assert.True(options.EnableAgent365Instrumentation);
        }

        [Fact]
        public void SuppressDefault_CalledTwice_Idempotent()
        {
            var options = new InstrumentationOptions();
            options.SuppressDefaultInfraInstrumentation();
            options.SuppressDefaultInfraInstrumentation(); // second call

            Assert.False(options.EnableAspNetCoreInstrumentation);
            Assert.False(options.EnableHttpClientInstrumentation);
            Assert.False(options.EnableSqlClientInstrumentation);
            Assert.False(options.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void SuppressDefault_SignalFlags_Unaffected()
        {
            var options = new InstrumentationOptions();
            options.SuppressDefaultInfraInstrumentation();

            Assert.True(options.EnableTracing);
            Assert.True(options.EnableMetrics);
            Assert.True(options.EnableLogging);
        }
    }

    [Collection("EnvironmentVariableTests")]
    public class A365OnlyModeTests
    {
        [Fact]
        public void Agent365Only_InfraDisabledByDefault()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.False(captured!.EnableAspNetCoreInstrumentation);
            Assert.False(captured.EnableHttpClientInstrumentation);
            Assert.False(captured.EnableSqlClientInstrumentation);
            Assert.False(captured.EnableAzureSdkInstrumentation);
            // gen_ai instrumentation still enabled
            Assert.True(captured.EnableOpenAIInstrumentation);
            Assert.True(captured.EnableAgentFrameworkInstrumentation);
        }

        [Fact]
        public void Agent365Only_UserOverride_Respected()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;
                    o.Instrumentation.EnableHttpClientInstrumentation = true;
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.False(captured!.EnableAspNetCoreInstrumentation); // suppressed
            Assert.True(captured.EnableHttpClientInstrumentation);   // user override
            Assert.False(captured.EnableSqlClientInstrumentation);   // suppressed
            Assert.False(captured.EnableAzureSdkInstrumentation);    // suppressed
        }

        [Fact]
        public void Agent365Only_UserOverrideMultiple_AllRespected()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Instrumentation.EnableAspNetCoreInstrumentation = true;
                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                    // SQL and Azure SDK left at default
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);  // user true
            Assert.False(captured.EnableHttpClientInstrumentation);   // user false
            Assert.False(captured.EnableSqlClientInstrumentation);    // suppressed
            Assert.False(captured.EnableAzureSdkInstrumentation);     // suppressed
        }

        [Fact]
        public void Agent365PlusAzureMonitor_InfraEnabled()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365 | ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);
            Assert.True(captured.EnableHttpClientInstrumentation);
            Assert.True(captured.EnableSqlClientInstrumentation);
            Assert.True(captured.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void Agent365PlusOtlp_InfraEnabled()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365 | ExportTarget.Otlp;
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);
            Assert.True(captured.EnableHttpClientInstrumentation);
            Assert.True(captured.EnableSqlClientInstrumentation);
            Assert.True(captured.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void Agent365Only_NoConsole_StillSuppresses()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365; // no Console
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.False(captured!.EnableAspNetCoreInstrumentation);
            Assert.False(captured.EnableHttpClientInstrumentation);
            Assert.False(captured.EnableSqlClientInstrumentation);
            Assert.False(captured.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void ConsoleOnly_NoA365_NoSuppression()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);
            Assert.True(captured.EnableHttpClientInstrumentation);
            Assert.True(captured.EnableSqlClientInstrumentation);
            Assert.True(captured.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void AzureMonitorOnly_NoSuppression()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);
            Assert.True(captured.EnableHttpClientInstrumentation);
            Assert.True(captured.EnableSqlClientInstrumentation);
            Assert.True(captured.EnableAzureSdkInstrumentation);
        }

        [Fact]
        public void NoExporters_NoSuppression()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);
                var services = new ServiceCollection();
                InstrumentationOptions? captured = null;

                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        captured = o.Instrumentation;
                    });

                Assert.NotNull(captured);
                Assert.True(captured!.EnableAspNetCoreInstrumentation);
                Assert.True(captured.EnableHttpClientInstrumentation);
                Assert.True(captured.EnableSqlClientInstrumentation);
                Assert.True(captured.EnableAzureSdkInstrumentation);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void DuplicateCallThrows()
        {
            var services = new ServiceCollection();
            var builder = services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            Assert.Throws<NotSupportedException>(() =>
                builder.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                }));
        }

        [Fact]
        public void Agent365Only_GenAiFlags_AllEnabled()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableOpenAIInstrumentation);
            Assert.True(captured.EnableSemanticKernelInstrumentation);
            Assert.True(captured.EnableAgentFrameworkInstrumentation);
            Assert.True(captured.EnableAgent365Instrumentation);
        }

        [Fact]
        public void Agent365PlusAllExporters_InfraEnabled()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365 | ExportTarget.AzureMonitor | ExportTarget.Otlp | ExportTarget.Console;
                    o.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.True(captured!.EnableAspNetCoreInstrumentation);
            Assert.True(captured.EnableHttpClientInstrumentation);
            Assert.True(captured.EnableSqlClientInstrumentation);
            Assert.True(captured.EnableAzureSdkInstrumentation);
        }
    }

    [Collection("EnvironmentVariableTests")]
    public class UseCustomExporterTests
    {
        [Fact]
        public void CustomExporterMarker_SkipsBuiltInExporter_RegistersOptionsInDI()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.ContextualTokenResolver = _ =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            using var sp = services.BuildServiceProvider();

            // Agent365ExporterOptions IS registered in DI (shim can resolve it)
            var options = sp.GetService<Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterOptions>();
            Assert.NotNull(options);

            // The built-in exporter registers via IDeferredTracerProviderBuilder.Configure which
            // adds IConfigureTracerProviderBuilder registrations. With the marker, there should be
            // fewer such registrations than without the marker.
            var configureCount = services.Count(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder"));

            // Compare with no-marker baseline to verify the exporter configure callback was skipped
            var servicesNoMarker = new ServiceCollection();
            servicesNoMarker.AddLogging();
            servicesNoMarker.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.ContextualTokenResolver = _ =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            var configureCountNoMarker = servicesNoMarker.Count(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder"));

            Assert.True(configureCountNoMarker > configureCount,
                $"Without marker: {configureCountNoMarker} configure callbacks, with marker: {configureCount}. " +
                "The built-in exporter should add at least one extra configure callback when no marker is present.");
        }

        [Fact]
        public void NoMarker_RegistersBuiltInExporter_InProcessorChain()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.TokenResolver = (a, t) =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            // Without a marker, AddAgent365Exporter() is called which registers a deferred
            // configure callback. Verify it's present (more callbacks than with marker).
            var configureCount = services.Count(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder"));

            var servicesWithMarker = new ServiceCollection();
            servicesWithMarker.AddLogging();
            servicesWithMarker.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
            servicesWithMarker.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.TokenResolver = (a, t) =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                });

            var configureCountWithMarker = servicesWithMarker.Count(s =>
                s.ServiceType.Name.Contains("IConfigureTracerProviderBuilder"));

            Assert.True(configureCount > configureCountWithMarker,
                $"Without marker: {configureCount} configure callbacks, with marker: {configureCountWithMarker}. " +
                "The built-in exporter should register an additional configure callback.");
        }


        [Fact]
        public void CustomExporterMarker_ExporterOptionsResolvable_WithTokenResolver()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.TokenResolver = (agentId, tenantId) =>
                        System.Threading.Tasks.Task.FromResult<string?>("resolved-token");
                });

            using var sp = services.BuildServiceProvider();
            var options = sp.GetService<Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterOptions>();

            Assert.NotNull(options);
            Assert.NotNull(options!.TokenResolver);
        }

        [Fact]
        public void CustomExporterMarker_ExporterOptionsResolvable_WithContextualTokenResolver()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.ContextualTokenResolver = ctx =>
                        System.Threading.Tasks.Task.FromResult<string?>("contextual-token");
                });

            using var sp = services.BuildServiceProvider();
            var options = sp.GetService<Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters.Agent365ExporterOptions>();

            Assert.NotNull(options);
            Assert.NotNull(options!.ContextualTokenResolver);
        }

        [Fact]
        public void CustomExporterMarker_WithSkipExporter_NoOptionsInDI()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var services = new ServiceCollection();
                services.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.Exporters = ExportTarget.Console; // Agent365 NOT in target
                        o.Agent365.TokenResolver = (a, t) =>
                            System.Threading.Tasks.Task.FromResult<string?>("token");
                    });

                // Agent365ExporterOptions NOT registered (SkipExporter takes precedence)
                Assert.DoesNotContain(services, s => s.ServiceType.Name == "Agent365ExporterOptions");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void CustomExporterMarker_A365OnlyMode_InfraStillSuppressed()
        {
            var services = new ServiceCollection();
            InstrumentationOptions? captured = null;

            services.AddSingleton(Microsoft.OpenTelemetry.CustomAgent365ExporterMarker.Instance);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.ContextualTokenResolver = _ =>
                        System.Threading.Tasks.Task.FromResult<string?>("token");
                    captured = o.Instrumentation;
                });

            Assert.NotNull(captured);
            Assert.False(captured!.EnableAspNetCoreInstrumentation);
            Assert.False(captured.EnableHttpClientInstrumentation);
            Assert.False(captured.EnableSqlClientInstrumentation);
            Assert.False(captured.EnableAzureSdkInstrumentation);
        }
    }
}
#endif
