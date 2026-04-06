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
    internal const string ASPNETCORE_DISABLE_URL_QUERY_REDACTION = "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";
    internal const string HTTPCLIENT_DISABLE_URL_QUERY_REDACTION = "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION";
}
