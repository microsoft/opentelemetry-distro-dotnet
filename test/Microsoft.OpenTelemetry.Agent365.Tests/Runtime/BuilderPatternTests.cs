using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using global::OpenTelemetry.Trace;

namespace Microsoft.Agents.A365.Observability.Tests;

/// <summary>
/// Test to verify that the new builder pattern works as expected in the issue example.
/// </summary>
[TestClass]
public sealed class BuilderPatternTests
{
    [TestMethod]
    public void AddTracing_WithLambdaConfiguration_ShouldWork()
    {
        HostApplicationBuilder builder = new HostApplicationBuilder();

        // Use the new lambda configuration approach
        var result = builder.AddA365Tracing();

        // Should return the configured service collection directly (no Build() needed)
        result.Should().NotBeNull();
        result.Should().BeSameAs(builder);
        result.Services.Should().BeAssignableTo<IServiceCollection>();
    }

    [TestMethod]
    public void AddTracing_WithNullLambda_ShouldWork()
    {
        HostApplicationBuilder builder = new HostApplicationBuilder();

        var result = builder.AddA365Tracing(null);

        result.Should().NotBeNull();
        result.Should().BeSameAs(builder);
    }

    [TestMethod]
    public void AddTracing_WithEmptyLambda_ShouldWork()
    {
        HostApplicationBuilder builder = new HostApplicationBuilder();

        // Pass empty lambda - should work like no configuration
        var result = builder.AddA365Tracing(_ => { });

        result.Should().NotBeNull();
        result.Should().BeSameAs(builder);
    }

    [TestMethod]
    public void AddTracing_WithOfflineStorageDisabled_BuildsAndDisposesProviderCleanly()
    {
        var builder = new HostApplicationBuilder();
        builder.Configuration["EnableAgent365Exporter"] = "true";
        builder.Services.AddSingleton(new Agent365ExporterOptions
        {
            TokenResolver = (_, _) => Task.FromResult<string?>("unit-test-token"),
            DisableOfflineStorage = true,
        });

        builder.AddA365Tracing();

        using var host = builder.Build();

        // Resolving the TracerProvider forces the deferred exporter configuration to run, wiring durable
        // delivery. With offline storage disabled it must build without creating an on-disk store or
        // starting a replay loop, and dispose cleanly when the host is disposed.
        var tracerProvider = host.Services.GetService<TracerProvider>();

        tracerProvider.Should().NotBeNull();
    }
}