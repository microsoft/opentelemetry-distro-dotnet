// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.E2ETests
{
    public class HttpClientInstrumentationTests
    {
        private readonly ITestOutputHelper _output;

        public HttpClientInstrumentationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(200)]
        [InlineData(500)]
        public async Task HttpClientRequestsAreCaptured(int expectedStatusCode)
        {
            // ARRANGE - find an available port using TcpListener
            var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    ctx.Response.StatusCode = expectedStatusCode;
                    ctx.Response.Close();
                }
                catch { }
            });

            var exportedActivities = new List<Activity>();
            var services = new ServiceCollection();

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
                    new KeyValuePair<string, object>("service.name", "test-http-client"),
                }));

            using var sp = services.BuildServiceProvider();
            var hostedServices = sp.GetServices<IHostedService>();
            foreach (var hs in hostedServices) await hs.StartAsync(CancellationToken.None);

            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // ACT
            using var client = new HttpClient();
            try
            {
                await client.GetAsync($"http://localhost:{port}/test");
            }
            catch { }

            // SHUTDOWN
            tracerProvider.ForceFlush();
            tracerProvider.Shutdown();
            listener.Stop();

            // ASSERT
            var clientActivities = exportedActivities.Where(a => a.Kind == ActivityKind.Client).ToList();

            _output.WriteLine($"Exported {exportedActivities.Count} activities, {clientActivities.Count} client");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} {a.Status}");
            }

            Assert.NotEmpty(clientActivities);
            var activity = clientActivities.First();
            Assert.NotEqual(default, activity.TraceId);
        }

        [Fact]
        public async Task HttpClientErrorsAreCaptured()
        {
            var exportedActivities = new List<Activity>();
            var services = new ServiceCollection();

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

            using var sp = services.BuildServiceProvider();
            var hostedServices = sp.GetServices<IHostedService>();
            foreach (var hs in hostedServices) await hs.StartAsync(CancellationToken.None);

            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // ACT — call a host that won't resolve
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                await client.GetAsync("http://fakehostthatdoesnotexist.invalid/test");
            }
            catch { }

            tracerProvider.ForceFlush();
            tracerProvider.Shutdown();

            // ASSERT
            var clientActivities = exportedActivities.Where(a => a.Kind == ActivityKind.Client).ToList();

            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} status={a.Status}");
            }

            Assert.NotEmpty(clientActivities);
            var activity = clientActivities.First();
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
        }
    }
}
#endif
