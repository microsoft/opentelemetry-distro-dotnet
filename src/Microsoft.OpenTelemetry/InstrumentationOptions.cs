// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Controls which telemetry signals and auto-instrumentations are active.
/// All options default to <c>true</c> (opt-out model).
/// </summary>
public class InstrumentationOptions
{
    // ── Signal pipelines ──

    /// <summary>
    /// Gets or sets a value indicating whether the distributed tracing pipeline is enabled.
    /// When <c>false</c>, no <c>TracerProvider</c> is configured and all library-level
    /// tracing options are ignored. Default: <c>true</c>.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the metrics pipeline is enabled.
    /// When <c>false</c>, no <c>MeterProvider</c> is configured. Default: <c>true</c>.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OpenTelemetry log export is enabled.
    /// When <c>false</c>, a provider-scoped <see cref="Microsoft.Extensions.Logging.LogLevel.None"/>
    /// filter is added to <c>OpenTelemetryLoggerProvider</c>, suppressing all log records
    /// from reaching any OpenTelemetry exporter (Azure Monitor, OTLP, Console).
    /// Other logging providers (console logger, file logger, etc.) are not affected.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    // ── Auto-instrumentation libraries ──

    /// <summary>
    /// Gets or sets a value indicating whether ASP.NET Core incoming request
    /// instrumentation is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="System.Net.Http.HttpClient"/>
    /// outgoing request instrumentation is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableHttpClientInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SQL Client database call
    /// instrumentation is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableSqlClientInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Azure SDK client library
    /// instrumentation is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableAzureSdkInstrumentation { get; set; } = true;

    // ── GenAI / Agent instrumentation libraries ──

    /// <summary>
    /// Gets or sets a value indicating whether OpenAI and Azure OpenAI SDK
    /// instrumentation is enabled (<c>Azure.AI.OpenAI*</c>, <c>OpenAI.*</c>,
    /// <c>Experimental.Microsoft.Extensions.AI</c>). Default: <c>true</c>.
    /// </summary>
    public bool EnableOpenAIInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Semantic Kernel orchestrator
    /// instrumentation is enabled (<c>Microsoft.SemanticKernel*</c>). Default: <c>true</c>.
    /// </summary>
    public bool EnableSemanticKernelInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Agent Framework
    /// instrumentation is enabled (<c>Experimental.Microsoft.Agents.AI*</c>). Default: <c>true</c>.
    /// </summary>
    public bool EnableAgentFrameworkInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Agent365 SDK scope and baggage
    /// instrumentation is enabled (<c>Agent365Sdk</c> source). Default: <c>true</c>.
    /// </summary>
    public bool EnableAgent365Instrumentation { get; set; } = true;
}
