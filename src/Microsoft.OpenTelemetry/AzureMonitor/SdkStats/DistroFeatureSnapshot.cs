// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Immutable snapshot of the distro feature flags reported on a given
    /// <c>UseMicrosoftOpenTelemetry</c> invocation, plus the customer instrumentation key
    /// extracted from the Azure Monitor connection string.
    /// </summary>
    internal sealed class DistroFeatureSnapshot
    {
        /// <summary>
        /// Literal value reported as <c>cikey</c> when no Azure Monitor connection string
        /// is configured (OTLP-only, Console-only, or Agent365-only deployments). Mirrors
        /// the SDKStats spec convention for Network stats.
        /// </summary>
        internal const string NoCustomerInstrumentationKey = "N/A";

        private DistroFeatureSnapshot(DistroFeature features, string customerInstrumentationKey, string distroVersion)
        {
            this.Features = features;
            this.CustomerInstrumentationKey = customerInstrumentationKey;
            this.DistroVersion = distroVersion;
        }

        /// <summary>Bit map of distro features enabled in the current process.</summary>
        internal DistroFeature Features { get; }

        /// <summary>Customer instrumentation key (without the surrounding connection-string envelope).</summary>
        internal string CustomerInstrumentationKey { get; }

        /// <summary>Version string of the distro package.</summary>
        internal string DistroVersion { get; }

        /// <summary>
        /// Builds a snapshot from a fully-finalized set of distro options. The customer
        /// instrumentation key is extracted from the Azure Monitor connection string when
        /// present; for OTLP-only, Console-only, or Agent365-only deployments where no
        /// connection string is configured, <see cref="CustomerInstrumentationKey"/> is set
        /// to the literal <c>"N/A"</c> (per the SDKStats spec convention for Network stats,
        /// extended here for consistency).
        /// </summary>
        /// <param name="options">The distro options after defaults and auto-detection have been applied.</param>
        /// <param name="effectiveExporters">The exporter selection resolved by the distro.</param>
        /// <param name="customerSdkStatsEnabled">Whether customer-facing SDK stats are enabled.</param>
        /// <param name="a365OnlyMode">Whether the distro entered Agent365-only mode.</param>
        /// <param name="distroVersion">The distro assembly version string.</param>
        internal static DistroFeatureSnapshot? Build(
            MicrosoftOpenTelemetryOptions options,
            ExportTarget effectiveExporters,
            bool customerSdkStatsEnabled,
            bool a365OnlyMode,
            string distroVersion)
        {
            if (options is null)
            {
                return null;
            }

            var ikey = TryExtractInstrumentationKey(options.AzureMonitor.ConnectionString);
            if (string.IsNullOrEmpty(ikey))
            {
                // No customer connection string (OTLP/Console/A365-only deployment).
                // Per the SDKStats spec, report the cikey dimension as the literal "N/A"
                // rather than omitting it, so backend KQL queries don't need to filter
                // out missing rows.
                ikey = NoCustomerInstrumentationKey;
            }

            var features = DistroFeature.Distro;

            // AzureMonitor-scoped feature flags.
            if (options.AzureMonitor.Credential != null)
            {
                features |= DistroFeature.AadHandling;
            }

            if (options.AzureMonitor.EnableLiveMetrics)
            {
                features |= DistroFeature.LiveMetrics;
            }

            if (options.AzureMonitor.EnableStandardMetrics)
            {
                features |= DistroFeature.StandardMetrics;
            }

            if (options.AzureMonitor.EnablePerfCounters)
            {
                features |= DistroFeature.PerfCounters;
            }

            if (!options.AzureMonitor.DisableOfflineStorage)
            {
                features |= DistroFeature.DiskRetry;
            }

            if (options.AzureMonitor.EnableTraceBasedLogsSampler)
            {
                features |= DistroFeature.TraceBasedLogsSampler;
            }

            if (customerSdkStatsEnabled)
            {
                features |= DistroFeature.CustomerSdkStats;
            }

            // Exporter selection.
            if (effectiveExporters.HasFlag(ExportTarget.AzureMonitor))
            {
                features |= DistroFeature.ExporterAzureMonitor;
            }

            if (effectiveExporters.HasFlag(ExportTarget.Otlp))
            {
                features |= DistroFeature.ExporterOtlp;
            }

            if (effectiveExporters.HasFlag(ExportTarget.Agent365))
            {
                features |= DistroFeature.ExporterAgent365;
            }

            if (effectiveExporters.HasFlag(ExportTarget.Console))
            {
                features |= DistroFeature.ExporterConsole;
            }

            // Distro behavior flags.
            features |= DistroFeature.AgentFramework; // UseAgentFramework is unconditional today.

            if (a365OnlyMode)
            {
                features |= DistroFeature.A365OnlyMode;
            }

            return new DistroFeatureSnapshot(features, ikey!, distroVersion);
        }

        /// <summary>
        /// Extracts the <c>InstrumentationKey</c> value from an Application Insights
        /// connection string. Returns <see langword="null"/> when not present.
        /// </summary>
        internal static string? TryExtractInstrumentationKey(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return null;
            }

            // Connection strings are semicolon-separated "Key=Value" pairs and the key matching
            // performed by the exporter is case-insensitive. Mirror that here without taking a
            // dependency on the exporter's internal parser.
            foreach (var rawSegment in connectionString!.Split(';'))
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                var eqIndex = segment.IndexOf('=');
                if (eqIndex <= 0 || eqIndex == segment.Length - 1)
                {
                    continue;
                }

                var key = segment.Substring(0, eqIndex).Trim();
                if (string.Equals(key, "InstrumentationKey", StringComparison.OrdinalIgnoreCase))
                {
                    var value = segment.Substring(eqIndex + 1).Trim();
                    return value.Length == 0 ? null : value;
                }
            }

            return null;
        }
    }
}
