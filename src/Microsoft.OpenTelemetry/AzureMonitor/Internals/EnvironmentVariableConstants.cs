// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.AzureMonitor.Internals;

/// <summary>
/// Environment variable constants used by the distro.
/// </summary>
internal static class EnvironmentVariableConstants
{
    internal const string APPLICATIONINSIGHTS_CONNECTION_STRING = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    internal const string OTEL_TRACES_SAMPLER = "OTEL_TRACES_SAMPLER";
    internal const string OTEL_TRACES_SAMPLER_ARG = "OTEL_TRACES_SAMPLER_ARG";

    /// <summary>
    /// Kill switch shared with the Azure Monitor exporter: when set to <c>"true"</c>
    /// (case-insensitive), the distro skips registering its Feature SDKStats producer.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_STATSBEAT_DISABLED = "APPLICATIONINSIGHTS_STATSBEAT_DISABLED";

    /// <summary>
    /// <summary>
    /// Internal kill switch (spec: <c>disabledAll</c>): when set to <c>"true"</c>
    /// (case-insensitive), the distro turns off all internal SDKStats (Attach / Feature /
    /// Network) completely — it neither bootstraps the SDKStats pin nor registers its own
    /// Feature/Network producers.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_SDKSTATS_DISABLED_ALL = "APPLICATIONINSIGHTS_SDKSTATS_DISABLED_ALL";

    /// <summary>
    /// Overrides the export interval (in seconds) for long-interval internal SDKStats — the
    /// distro's Feature signal (default 24 hours). Maps to the <c>longInterval</c>
    /// configuration in the SDKStats spec. Primarily intended for testing.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_STATS_LONG_EXPORT_INTERVAL = "APPLICATIONINSIGHTS_STATS_LONG_EXPORT_INTERVAL";

    /// <summary>
    /// Customer-facing SDK stats opt-out. Customer SDK Stats are on by default; when this is
    /// set to <c>"true"</c> (case-insensitive), the Azure Monitor exporter disables Customer
    /// SDK Stats and the distro clears the
    /// <see cref="SdkStats.DistroFeature.CustomerSdkStats"/> feature bit.
    /// </summary>
    internal const string APPLICATIONINSIGHTS_SDKSTATS_DISABLED = "APPLICATIONINSIGHTS_SDKSTATS_DISABLED";
}
