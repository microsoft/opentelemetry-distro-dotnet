// Copyright (c) Microsoft Corporation. All rights reserved.

namespace A365.OpenTelemetry.Exporter;

/// <summary>Options for configuring the A365 span exporter.</summary>
public sealed class A365ExporterOptions
{
    /// <summary>Async delegate that returns a bearer token for the given (agentId, tenantId) pair.</summary>
    public Func<string, string, Task<string?>> TokenResolver { get; set; } =
        (_, _) => Task.FromResult<string?>(null);

    /// <summary>Base URL of the Agent 365 Observability Service.</summary>
    public string Endpoint { get; set; } = "https://agent365.svc.cloud.microsoft";

    /// <summary>HTTP request timeout per export batch.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
