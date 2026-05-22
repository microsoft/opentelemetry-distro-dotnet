// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using OpenTelemetry;
using Xunit;
using Assert = Xunit.Assert;

namespace Microsoft.OpenTelemetry.Agent365.Tests
{
    [Collection("EnvironmentVariableTests")]
    public class TokenCacheDiTests
    {
        [Fact]
        public void CustomTokenResolver_ExporterOptions_RegisteredDirectly()
        {
            // When a custom TokenResolver is provided, Agent365ExporterOptions should be
            // registered as a singleton instance (no AgenticTokenCache dependency).
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.TokenResolver = (agentId, tenantId) =>
                        Task.FromResult<string?>("custom-token");
                });

            // Agent365ExporterOptions should be registered directly (instance, not factory)
            var exporterOptionsDescriptors = services
                .Where(s => s.ServiceType == typeof(Agent365ExporterOptions))
                .ToList();

            Assert.Single(exporterOptionsDescriptors);
            Assert.NotNull(exporterOptionsDescriptors[0].ImplementationInstance);
        }

        [Fact]
        public void CustomTokenResolver_AgenticTokenCache_NotRegistered()
        {
            // Fix for issue #103: With a custom TokenResolver, AgenticTokenCache
            // should NOT be auto-registered (it depends on Microsoft.Agents.Builder).
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                    o.Agent365.TokenResolver = (agentId, tenantId) =>
                        Task.FromResult<string?>("custom-token");
                });

            Assert.DoesNotContain(services, s =>
                s.ServiceType == typeof(IExporterTokenCache<AgenticTokenStruct>));
        }

        [Fact]
        public void NoTokenResolver_DoesNotThrow_AgenticTokenCacheNotRegistered()
        {
            // Without a TokenResolver, the SDK must not crash the host app.
            // Agent365ExporterOptions is registered (exporter logs error and becomes no-op),
            // but AgenticTokenCache is NOT auto-registered (no Microsoft.Agents.Builder dependency).
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Agent365;
                });

            // Agent365ExporterOptions is registered (exporter handles missing resolver gracefully)
            Assert.Contains(services, s =>
                s.ServiceType == typeof(Agent365ExporterOptions));

            // No AgenticTokenCache registered
            Assert.DoesNotContain(services, s =>
                s.ServiceType == typeof(IExporterTokenCache<AgenticTokenStruct>));
        }

    }
}
