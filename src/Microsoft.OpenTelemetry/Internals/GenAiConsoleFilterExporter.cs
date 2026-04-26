// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;
using global::OpenTelemetry;
using global::OpenTelemetry.Exporter;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// A filtering activity exporter that wraps <see cref="ConsoleActivityExporter"/>
/// and only forwards gen_ai / agent-related spans. This keeps the console output
/// focused on AI/agent spans during local development.
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
internal sealed class GenAiConsoleFilterExporter : BaseExporter<Activity>
{
    // ParentProvider has a public getter but internal setter. We use reflection
    // to propagate it from our exporter to the inner ConsoleActivityExporter
    // so the console output includes the Resource block.
    private static readonly PropertyInfo? ParentProviderSetter =
        typeof(BaseExporter<Activity>).GetProperty("ParentProvider")?.SetMethod != null
            ? typeof(BaseExporter<Activity>).GetProperty("ParentProvider")
            : null;

    private readonly ConsoleActivityExporter _inner;
    private bool _parentProviderPropagated;

    public GenAiConsoleFilterExporter()
    {
        _inner = new ConsoleActivityExporter(new ConsoleExporterOptions());
    }

    /// <inheritdoc/>
    public override ExportResult Export(in Batch<Activity> batch)
    {
        EnsureParentProviderPropagated();

        // Filter the batch to only gen_ai spans, then export each individually.
        foreach (var activity in batch)
        {
            if (IsGenAiSpan(activity))
            {
                _inner.Export(new Batch<Activity>(new[] { activity }, 1));
            }
        }

        return ExportResult.Success;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Lazily propagates ParentProvider from this exporter to the inner ConsoleActivityExporter.
    /// The SDK sets ParentProvider on our exporter (via SimpleActivityExportProcessor), but the
    /// inner exporter is not registered directly — so we propagate it via reflection to enable
    /// the console output to display the Resource block.
    /// </summary>
    private void EnsureParentProviderPropagated()
    {
        if (_parentProviderPropagated)
            return;

        if (ParentProvider != null && ParentProviderSetter != null)
        {
            try
            {
                ParentProviderSetter.SetValue(_inner, ParentProvider);
            }
            catch
            {
                // Silently fail — Resource block won't display but export still works.
            }

            _parentProviderPropagated = true;
        }
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
