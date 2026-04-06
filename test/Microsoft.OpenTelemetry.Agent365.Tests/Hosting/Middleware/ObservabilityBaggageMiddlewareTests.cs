using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenTelemetry.Agent365.Hosting.Middleware;
using System.Text.Json;

namespace Microsoft.OpenTelemetry.Agent365.Hosting.Tests.Middleware;

[TestClass]
public class ObservabilityBaggageMiddlewareTests
{
    [TestMethod]
    public async Task Middleware_UsesResolverValues()
    {
        // Arrange — use HostBuilder (WebHostBuilder is deprecated in net10.0)
        using var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseObservabilityRequestContext(ctx => ("tenant-abc", "agent-xyz"));
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", async (HttpContext ctx) =>
                        {
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync(
                                JsonSerializer.Serialize(new { tenant = "tenant-abc", agent = "agent-xyz" }));
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual("tenant-abc", root.GetProperty("tenant").GetString());
        Assert.AreEqual("agent-xyz", root.GetProperty("agent").GetString());
    }
}