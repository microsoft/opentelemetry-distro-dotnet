// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using global::OpenTelemetry;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// A filtering activity processor that forwards only gen_ai / agent-related
/// spans to the wrapped inner processor (typically a console exporter processor).
/// This keeps the console output focused on AI/agent spans during local development.
/// </summary>
/// <remarks>
/// A span is forwarded if ANY of:
/// <list type="bullet">
///   <item>Its <see cref="Activity.Source"/> name starts with <c>"Experimental.Microsoft"</c>
///         (covers <c>Experimental.Microsoft.Extensions.AI</c> and <c>Experimental.Microsoft.Agents.AI</c>).</item>
///   <item>Its <see cref="Activity.Source"/> name equals <c>"Agent365Sdk"</c> (A365 observability scopes).</item>
///   <item>It carries any tag whose key starts with <c>"gen_ai."</c>.</item>
/// </list>
/// All other spans (HTTP, ASP.NET, SQL, etc.) are silently dropped from the console.
/// They are still exported to other configured exporters (A365, Azure Monitor, OTLP).
/// </remarks>
internal sealed class GenAiConsoleFilterProcessor : BaseProcessor<Activity>
{
    private readonly BaseProcessor<Activity> _inner;

    public GenAiConsoleFilterProcessor(BaseProcessor<Activity> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public override void OnEnd(Activity data)
    {
        if (IsGenAiSpan(data))
        {
            _inner.OnEnd(data);
        }
    }

    /// <inheritdoc/>
    protected override bool OnForceFlush(int timeoutMilliseconds) => _inner.ForceFlush(timeoutMilliseconds);

    /// <inheritdoc/>
    protected override bool OnShutdown(int timeoutMilliseconds) => _inner.Shutdown(timeoutMilliseconds);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static bool IsGenAiSpan(Activity activity)
    {
        var sourceName = activity.Source.Name;

        // ActivitySource-based checks (fast path)
        if (sourceName.StartsWith("Experimental.Microsoft", StringComparison.Ordinal)
            || sourceName.Equals("Agent365Sdk", StringComparison.Ordinal))
        {
            return true;
        }

        // Attribute-based fallback — any tag starting with "gen_ai."
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key.StartsWith("gen_ai.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
