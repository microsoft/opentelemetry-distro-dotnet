// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;

namespace A365.OpenTelemetry.Exporter;

/// <summary>OpenTelemetry exporter that sends spans to the Agent 365 Observability Service.</summary>
public sealed class A365SpanExporter : BaseExporter<Activity>
{
    private const string TenantIdKey = "tenant_id";
    private const string AgentIdKey = "agent_id";
    private const string A365TenantIdKey = "a365.tenant_id";
    private const string A365AgentIdKey = "a365.agent_id";

    private readonly A365ExporterOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<A365SpanExporter> _logger;
    private readonly bool _ownsHttpClient;

    /// <summary>Create a new A365 span exporter.</summary>
    public A365SpanExporter(A365ExporterOptions options)
        : this(options, httpClient: null, logger: null)
    {
    }

    /// <summary>Create a new A365 span exporter with an optional HttpClient and logger.</summary>
    public A365SpanExporter(
        A365ExporterOptions options,
        HttpClient? httpClient,
        ILogger<A365SpanExporter>? logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = _options.Timeout;
        _logger = logger ?? NullLogger<A365SpanExporter>.Instance;
    }

    /// <inheritdoc />
    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            return ExportAsync(batch).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A365SpanExporter: unhandled error during export");
            return ExportResult.Failure;
        }
    }

    private async Task<ExportResult> ExportAsync(Batch<Activity> batch)
    {
        // Collect all activities from the batch.
        var activities = new List<Activity>();
        foreach (var activity in batch)
        {
            activities.Add(activity);
        }

        if (activities.Count == 0)
        {
            return ExportResult.Success;
        }

        // Group by (tenantId, agentId) resolved from tags or baggage.
        var groups = new Dictionary<(string TenantId, string AgentId), List<Activity>>();

        foreach (var activity in activities)
        {
            var tenantId = ResolveValue(activity, TenantIdKey, A365TenantIdKey);
            var agentId = ResolveValue(activity, AgentIdKey, A365AgentIdKey);

            if (tenantId is null || agentId is null)
            {
                _logger.LogDebug(
                    "A365SpanExporter: skipping span {SpanName} - missing tenant_id or agent_id",
                    activity.DisplayName);
                continue;
            }

            var key = (tenantId, agentId);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Activity>();
                groups[key] = list;
            }

            list.Add(activity);
        }

        if (groups.Count == 0)
        {
            _logger.LogDebug("A365SpanExporter: no spans with tenant_id and agent_id, nothing to export");
            return ExportResult.Success;
        }

        var overallSuccess = true;

        foreach (var ((tenantId, agentId), spanGroup) in groups)
        {
            try
            {
                var success = await ExportGroupAsync(tenantId, agentId, spanGroup).ConfigureAwait(false);
                if (!success)
                {
                    overallSuccess = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A365SpanExporter: failed to export {SpanCount} spans for tenant={TenantId} agent={AgentId}",
                    spanGroup.Count,
                    tenantId,
                    agentId);
                overallSuccess = false;
            }
        }

        return overallSuccess ? ExportResult.Success : ExportResult.Failure;
    }

    private async Task<bool> ExportGroupAsync(string tenantId, string agentId, List<Activity> spans)
    {
        var endpoint = _options.Endpoint.TrimEnd('/');
        var url = $"{endpoint}/observabilityService/tenants/{tenantId}/otlp/agents/{agentId}/traces";

        var token = await _options.TokenResolver(agentId, tenantId).ConfigureAwait(false);

        var json = OtlpJsonSerializer.Serialize(spans);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        _logger.LogDebug(
            "A365SpanExporter: exporting {SpanCount} spans to {Url}",
            spans.Count,
            url);

        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "A365SpanExporter: export returned HTTP {StatusCode} for tenant={TenantId} agent={AgentId}",
                (int)response.StatusCode,
                tenantId,
                agentId);
            return false;
        }

        _logger.LogDebug(
            "A365SpanExporter: successfully exported {SpanCount} spans for tenant={TenantId} agent={AgentId}",
            spans.Count,
            tenantId,
            agentId);

        return true;
    }

    private static string? ResolveValue(Activity activity, string tagKey, string a365TagKey)
    {
        // Prefer direct tag lookup (standard key first, then a365-prefixed).
        var value = activity.GetTagItem(tagKey) as string;
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        value = activity.GetTagItem(a365TagKey) as string;
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Fall back to baggage.
        value = activity.GetBaggageItem(tagKey);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        value = activity.GetBaggageItem(a365TagKey);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        base.Dispose(disposing);
    }
}
