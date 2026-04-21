using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;

namespace Microsoft.Agents.A365.Observability.Hosting.Tests.Middleware;

[TestClass]
public class ObservabilityBaggageMiddlewareTests
{
    [TestMethod]
    public async Task Middleware_UsesResolverValues()
    {
        // Arrange
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseObservabilityRequestContext(ctx => ("tenant-abc", "agent-xyz"));
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/", async (HttpContext ctx) =>
                    {
                        await ctx.Response.WriteAsync("tenant-abc|agent-xyz");
                    });
                });
            });

        using var server = new TestServer(builder);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.AreEqual("tenant-abc|agent-xyz", body);
    }
}