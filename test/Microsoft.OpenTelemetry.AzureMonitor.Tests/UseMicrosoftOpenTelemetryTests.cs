// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

        [Fact]
        public void Config_ReadsExportersFromAppSettings()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var configData = new Dictionary<string, string?>
                {
                    ["MicrosoftOpenTelemetry:Exporters"] = "AzureMonitor",
                    ["AzureMonitor:ConnectionString"] = TestConnectionString,
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

                // Exporters explicitly set from config — AzureMonitor should be registered
                Assert.True(HasAzureMonitorExporter(services),
                    "Azure Monitor exporter should be registered when Exporters includes AzureMonitor from config.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void Config_ReadsInstrumentationFromAppSettings()
        {
            var configData = new Dictionary<string, string?>
            {
                ["MicrosoftOpenTelemetry:Instrumentation:EnableSqlClientInstrumentation"] = "false",
                ["MicrosoftOpenTelemetry:Instrumentation:EnableOpenAIInstrumentation"] = "false",
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var options = new MicrosoftOpenTelemetryOptions();
            DefaultMicrosoftOpenTelemetryConfigureOptions.BindFromConfiguration(configuration, options);

            Assert.False(options.Instrumentation.EnableSqlClientInstrumentation,
                "EnableSqlClientInstrumentation should be false from config.");
            Assert.False(options.Instrumentation.EnableOpenAIInstrumentation,
                "EnableOpenAIInstrumentation should be false from config.");
            // Defaults should remain for unspecified values
            Assert.True(options.Instrumentation.EnableTracing);
            Assert.True(options.Instrumentation.EnableMetrics);
            Assert.True(options.Instrumentation.EnableLogging);
            Assert.True(options.Instrumentation.EnableAspNetCoreInstrumentation);
        }

        [Fact]
        public void Config_ActionCallbackOverridesAppSettings()
        {
            var configData = new Dictionary<string, string?>
            {
                ["MicrosoftOpenTelemetry:Instrumentation:EnableSqlClientInstrumentation"] = "true",
                ["MicrosoftOpenTelemetry:Instrumentation:EnableTracing"] = "true",
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    // Action callback overrides config values
                    o.Instrumentation.EnableSqlClientInstrumentation = false;
                    o.Exporters = ExportTarget.Console;
                });

            // Verify the Action<> callback takes effect for build-time by checking
            // that IConfigureOptions is registered (runtime resolution test below
            // verifies the PostConfigure ordering)
            Assert.Contains(services, s =>
                s.ServiceType.IsGenericType &&
                s.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) &&
                s.ServiceType.GetGenericArguments().Any(a => a == typeof(MicrosoftOpenTelemetryOptions)));
        }

        [Fact]
        public void Config_RegistersIConfigureOptions()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            // Verify IConfigureOptions<MicrosoftOpenTelemetryOptions> is registered
            Assert.Contains(services, s =>
                s.ServiceType == typeof(IConfigureOptions<MicrosoftOpenTelemetryOptions>));
        }

        [Fact]
        public void Config_RuntimeOptionsResolution()
        {
            var configData = new Dictionary<string, string?>
            {
                ["MicrosoftOpenTelemetry:Instrumentation:EnableSqlClientInstrumentation"] = "false",
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            // Resolve IOptions<MicrosoftOpenTelemetryOptions> at runtime
            var sp = services.BuildServiceProvider();
            var resolvedOptions = sp.GetRequiredService<IOptions<MicrosoftOpenTelemetryOptions>>().Value;

            // IConfiguration binding should have set this to false
            Assert.False(resolvedOptions.Instrumentation.EnableSqlClientInstrumentation,
                "Runtime IOptions should reflect IConfiguration binding.");
            // PostConfigure (Action<> callback) should set Exporters
            Assert.Equal(ExportTarget.Console, resolvedOptions.Exporters);
        }

        [Fact]
        public void Config_ServicesConfigureAffectsRuntimeOptions()
        {
            var services = new ServiceCollection();

            // User calls services.Configure<MicrosoftOpenTelemetryOptions> before UseMicrosoftOpenTelemetry
            services.Configure<MicrosoftOpenTelemetryOptions>(o =>
            {
                o.Instrumentation.EnableHttpClientInstrumentation = false;
            });

            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            var sp = services.BuildServiceProvider();
            var resolvedOptions = sp.GetRequiredService<IOptions<MicrosoftOpenTelemetryOptions>>().Value;

            // services.Configure runs between IConfigureOptions and PostConfigure,
            // so its value should be visible unless the PostConfigure callback overwrites it.
            // Since the Action<> callback doesn't set EnableHttpClientInstrumentation,
            // the services.Configure value should survive.
            Assert.False(resolvedOptions.Instrumentation.EnableHttpClientInstrumentation,
                "services.Configure<MicrosoftOpenTelemetryOptions> should affect runtime IOptions resolution.");
        }

        [Fact]
        public void Config_ExportersFlagsEnum_ParsesCommasSeparated()
        {
            var configData = new Dictionary<string, string?>
            {
                ["MicrosoftOpenTelemetry:Exporters"] = "AzureMonitor, Otlp",
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var options = new MicrosoftOpenTelemetryOptions();
            DefaultMicrosoftOpenTelemetryConfigureOptions.BindFromConfiguration(configuration, options);

            Assert.True(options.ExportersExplicitlySet, "Exporters should be explicitly set from config.");
            Assert.True(options.Exporters.HasFlag(ExportTarget.AzureMonitor));
            Assert.True(options.Exporters.HasFlag(ExportTarget.Otlp));
            Assert.False(options.Exporters.HasFlag(ExportTarget.Agent365));
            Assert.False(options.Exporters.HasFlag(ExportTarget.Console));
        }

        [Fact]
        public void Config_EmptySection_PreservesDefaults()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var options = new MicrosoftOpenTelemetryOptions();
            DefaultMicrosoftOpenTelemetryConfigureOptions.BindFromConfiguration(configuration, options);

            // No MicrosoftOpenTelemetry section → all defaults preserved
            Assert.False(options.ExportersExplicitlySet);
            Assert.Equal(ExportTarget.None, options.Exporters);
            Assert.True(options.Instrumentation.EnableTracing);
            Assert.True(options.Instrumentation.EnableMetrics);
            Assert.True(options.Instrumentation.EnableLogging);
            Assert.True(options.Instrumentation.EnableSqlClientInstrumentation);
        }

        [Fact]
        public void Config_BindsAzureMonitorConnectionStringFromSection()
        {
            var configData = new Dictionary<string, string?>
            {
                ["AzureMonitor:ConnectionString"] = TestConnectionString,
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var options = new MicrosoftOpenTelemetryOptions();
            DefaultMicrosoftOpenTelemetryConfigureOptions.BindFromConfiguration(configuration, options);

            Assert.Equal(TestConnectionString, options.AzureMonitor.ConnectionString);
        }

        [Fact]
        public void Config_AzureMonitorConnectionStringFlowsToExporter()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var configData = new Dictionary<string, string?>
                {
                    ["MicrosoftOpenTelemetry:Exporters"] = "AzureMonitor",
                    ["AzureMonitor:ConnectionString"] = TestConnectionString,
                };
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(configData)
                    .Build();

                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(configuration);

                // Parameterless — everything from config
                services.AddOpenTelemetry()
                    .UseMicrosoftOpenTelemetry(o =>
                    {
                        o.AzureMonitor.DisableOfflineStorage = true;
                        o.AzureMonitor.EnableLiveMetrics = false;
                    });

                // Azure Monitor exporter should be registered with a valid connection string
                Assert.True(HasAzureMonitorExporter(services),
                    "Azure Monitor exporter should be registered when ConnectionString is in AzureMonitor config section.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Fact]
        public void Config_ConnectionStringEnvVarOverridesSection()
        {
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            const string envVarConnectionString = "InstrumentationKey=11111111-1111-1111-1111-111111111111";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, envVarConnectionString);

                var configData = new Dictionary<string, string?>
                {
                    ["AzureMonitor:ConnectionString"] = TestConnectionString,
                };
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(configData)
                    .Build();

                var options = new MicrosoftOpenTelemetryOptions();
                DefaultMicrosoftOpenTelemetryConfigureOptions.BindFromConfiguration(configuration, options);

                // Env var should take precedence over config section
                Assert.Equal(envVarConnectionString, options.AzureMonitor.ConnectionString);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
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
}
#endif
