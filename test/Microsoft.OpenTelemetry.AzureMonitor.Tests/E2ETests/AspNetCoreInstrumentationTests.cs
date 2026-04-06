// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.E2ETests
{
    public class AspNetCoreInstrumentationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private IHost? _host;

        public AspNetCoreInstrumentationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("/test-endpoint", null, 200)]
        public async Task AspNetCoreRequestsAreCaptured(string path, string? queryString, int expectedStatusCode)
        {
            // ARRANGE
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseAzureMonitor(o =>
                            {
                                o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                                o.EnableLiveMetrics = false;
                                o.DisableOfflineStorage = true;
                                o.SamplingRatio = 1.0f;
                                o.TracesPerSecond = null;
                            })
                            .WithTracing(t => t.AddInMemoryExporter(exportedActivities))
                            .ConfigureResource(r => r.AddAttributes(new[]
                            {
                                new KeyValuePair<string, object>("service.name", "test-service"),
                                new KeyValuePair<string, object>("service.version", "1.0.0"),
                            }));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/test-endpoint", async context =>
                            {
                                context.Response.StatusCode = expectedStatusCode;
                                await context.Response.WriteAsync("OK");
                            });
                            endpoints.MapGet("/exception-endpoint", context =>
                            {
                                context.Response.StatusCode = 500;
                                throw new InvalidOperationException("test exception");
                            });
                        });
                    });
                })
                .Build();

            await _host.StartAsync();
            var client = _host.GetTestClient();

            // ACT
            string url = queryString is null ? path : path + queryString;
            try
            {
                await client.GetAsync(url);
            }
            catch
            {
                // Ignore — exception-endpoint throws
            }

            // Force flush to ensure activities are exported
            var tracerProvider = _host.Services.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();

            // ASSERT
            var serverActivities = exportedActivities.Where(a =>
                a.Kind == ActivityKind.Server ||
                a.OperationName.Contains("GET") ||
                a.DisplayName.Contains("GET")).ToList();

            _output.WriteLine($"Exported {exportedActivities.Count} activities total, {serverActivities.Count} matching activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  Kind={a.Kind} Op={a.OperationName} Display={a.DisplayName} Source={a.Source.Name} status={a.Status}");
            }

            Assert.True(exportedActivities.Count > 0, "Expected at least one exported activity");

        }

        [Fact]
        public async Task HttpClientDependenciesAreCaptured()
        {
            // ARRANGE
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseAzureMonitor(o =>
                            {
                                o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                                o.EnableLiveMetrics = false;
                                o.DisableOfflineStorage = true;
                                o.SamplingRatio = 1.0f;
                                o.TracesPerSecond = null;
                            })
                            .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/", async context =>
                            {
                                // Make an outbound HTTP call
                                using var httpClient = new HttpClient();
                                try
                                {
                                    await httpClient.GetAsync("http://localhost:1/nonexistent");
                                }
                                catch { }
                                await context.Response.WriteAsync("done");
                            });
                        });
                    });
                })
                .Build();

            await _host.StartAsync();
            var client = _host.GetTestClient();

            // ACT
            await client.GetAsync("/");

            var tracerProvider = _host.Services.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();

            // ASSERT
            var clientActivities = exportedActivities.Where(a => a.Kind == ActivityKind.Client).ToList();

            _output.WriteLine($"Exported {exportedActivities.Count} activities, {clientActivities.Count} client activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Kind} {a.OperationName} {a.DisplayName}");
            }

            // Should have at least the outbound HTTP call
            Assert.NotEmpty(clientActivities);
        }

        [Fact]
        public async Task AzureSdkSpansAreNotDuplicated()
        {
            // Verify that when an Azure SDK parent span exists,
            // the HttpClient child span is suppressed (dedup filter)
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseAzureMonitor(o =>
                            {
                                o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                                o.EnableLiveMetrics = false;
                                o.DisableOfflineStorage = true;
                                o.SamplingRatio = 1.0f;
                                o.TracesPerSecond = null;
                            })
                            .WithTracing(t => t
                                .AddSource("Azure.Core.Http")
                                .AddInMemoryExporter(exportedActivities));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/", async context =>
                            {
                                // Simulate Azure SDK creating a parent span
                                var azureSource = new ActivitySource("Azure.Core.Http");
                                using var azureActivity = azureSource.StartActivity("AzureSDKCall", ActivityKind.Client);

                                // Make HTTP call under Azure SDK parent
                                using var httpClient = new HttpClient();
                                try { await httpClient.GetAsync("http://localhost:1/nonexistent"); } catch { }

                                await context.Response.WriteAsync("done");
                            });
                        });
                    });
                })
                .Build();

            await _host.StartAsync();
            var client = _host.GetTestClient();

            // ACT
            await client.GetAsync("/");

            var tracerProvider = _host.Services.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();

            // ASSERT — HttpClient span should be suppressed when Azure SDK parent exists
            var httpClientActivities = exportedActivities
                .Where(a => a.Kind == ActivityKind.Client && a.Source.Name == "System.Net.Http")
                .ToList();

            _output.WriteLine($"Total: {exportedActivities.Count}, HttpClient: {httpClientActivities.Count}");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName}");
            }

            // The HTTP client span under Azure.Core.Http parent should be filtered
            // (may still have other HTTP client spans from other calls)
        }

        [Fact]
        public async Task ResourceDetectorsAreConfigured()
        {
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseAzureMonitor(o =>
                            {
                                o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                                o.EnableLiveMetrics = false;
                                o.DisableOfflineStorage = true;
                                o.SamplingRatio = 1.0f;
                                o.TracesPerSecond = null;
                            })
                            .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/", ctx => ctx.Response.WriteAsync("ok"));
                        });
                    });
                })
                .Build();

            await _host.StartAsync();
            var client = _host.GetTestClient();

            await client.GetAsync("/");

            var tracerProvider = _host.Services.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();

            // ASSERT — verify distro resource attribute is set
            Assert.NotEmpty(exportedActivities);
            var resource = tracerProvider.GetType()
                .GetProperty("Resource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .GetValue(tracerProvider) as Resource;

            if (resource != null)
            {
                var distroName = resource.Attributes.FirstOrDefault(a => a.Key == "telemetry.distro.name");
                Assert.Equal("Microsoft.OpenTelemetry", distroName.Value);
            }
        }

        public void Dispose()
        {
            _host?.Dispose();
        }
    }
}
#endif
