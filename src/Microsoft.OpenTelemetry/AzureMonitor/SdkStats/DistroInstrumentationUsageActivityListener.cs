// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Marks enabled instrumentations when their activity sources are created.
    /// </summary>
    internal sealed class DistroInstrumentationUsageActivityListener : IDisposable
    {
        private readonly DistroInstrumentation _enabledInstrumentations;
        private readonly ActivityListener _listener;

        internal DistroInstrumentationUsageActivityListener(
            DistroInstrumentation enabledInstrumentations)
        {
            _enabledInstrumentations = enabledInstrumentations;
            _listener = new ActivityListener
            {
                ShouldListenTo = ObserveActivitySource,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private bool ObserveActivitySource(ActivitySource source)
        {
            var used = GetInstrumentations(source.Name) & _enabledInstrumentations;
            if (used == DistroInstrumentation.None)
            {
                return false;
            }

            DistroSdkStatsUsage.MarkInstrumentationInUse(used);

            if ((used & DistroInstrumentation.AgentFramework) != 0)
            {
                DistroSdkStatsUsage.MarkFeatureInUse(DistroFeature.AgentFramework);
            }

            // This listener observes source publication only and never receives activities.
            return false;
        }

        internal static DistroInstrumentation GetEnabledInstrumentations(InstrumentationOptions options)
        {
            var enabled = DistroInstrumentation.None;
            if (options.EnableAzureSdkInstrumentation)
            {
                enabled |= DistroInstrumentation.AzureSdk;
            }

            if (options.EnableAspNetCoreInstrumentation)
            {
                enabled |= DistroInstrumentation.AspNetCore;
            }

            if (options.EnableHttpClientInstrumentation)
            {
                enabled |= DistroInstrumentation.HttpClient;
            }

            if (options.EnableSqlClientInstrumentation)
            {
                enabled |= DistroInstrumentation.SqlClient;
            }

            if (options.EnableOpenAIInstrumentation)
            {
                enabled |= DistroInstrumentation.OpenAI;
            }

            if (options.EnableSemanticKernelInstrumentation)
            {
                enabled |= DistroInstrumentation.SemanticKernel;
            }

            if (options.EnableAgentFrameworkInstrumentation)
            {
                enabled |= DistroInstrumentation.AgentFramework;
            }

            if (options.EnableAgent365Instrumentation)
            {
                enabled |= DistroInstrumentation.Agent365;
            }

            return enabled;
        }

        internal static DistroInstrumentation GetInstrumentations(string sourceName)
        {
            var instrumentations = DistroInstrumentation.None;

            if (sourceName.StartsWith("Azure.", StringComparison.Ordinal)
                && !sourceName.StartsWith("Azure.Monitor.OpenTelemetry", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AzureSdk;
            }

            if (sourceName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AspNetCore;
            }

            if (sourceName.StartsWith("System.Net.Http", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.HttpClient;
            }

            if (sourceName.StartsWith("OpenTelemetry.Instrumentation.SqlClient", StringComparison.Ordinal)
                || sourceName.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
                || sourceName.StartsWith("System.Data.SqlClient", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SqlClient;
            }

            if (sourceName.StartsWith("Azure.AI.OpenAI", StringComparison.Ordinal)
                || sourceName.StartsWith("OpenAI.", StringComparison.Ordinal)
                || string.Equals(sourceName, "Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.OpenAI;
            }

            if (sourceName.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.SemanticKernel;
            }

            if (sourceName.StartsWith("Experimental.Microsoft.Agents.AI", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.AgentFramework;
            }

            if (string.Equals(sourceName, "Agent365Sdk", StringComparison.Ordinal))
            {
                instrumentations |= DistroInstrumentation.Agent365;
            }

            return instrumentations;
        }
    }
}
