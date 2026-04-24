// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// A composite processor that wraps an inner processor (typically the Agent365
/// <see cref="BatchActivityExportProcessor"/>) and only forwards activities
/// whose source matches the GenAI allow-list or user-specified
/// <see cref="Agent365SpanFilterOptions.IncludeSources"/>.
/// </summary>
/// <remarks>
/// This processor does not mutate the <see cref="Activity"/> — it simply
/// skips calling <c>OnEnd</c> on the inner processor for non-matching
/// activities. Other exporters in the processor chain are unaffected.
/// </remarks>
internal sealed class Agent365SpanFilterProcessor : BaseProcessor<Activity>
{
    /// <summary>
    /// Built-in GenAI activity source prefixes always allowed through to Agent365.
    /// </summary>
    private static readonly string[] GenAIPrefixes =
    {
        "Agent365Sdk",
        "Microsoft.SemanticKernel",
        "Azure.AI.OpenAI",
        "OpenAI.",
        "Experimental.Microsoft.Extensions.AI",
        "Experimental.Microsoft.Agents.AI",
    };

    private readonly BaseProcessor<Activity> _inner;
    private readonly string[] _allowedPrefixes;
    private readonly ConcurrentDictionary<string, bool> _cache = new();

    public Agent365SpanFilterProcessor(BaseProcessor<Activity> inner, Agent365SpanFilterOptions filter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Merge built-in + user prefixes into a single array at construction time.
        // This avoids per-span IList interface dispatch and double iteration.
        var prefixes = new string[GenAIPrefixes.Length + filter.IncludeSources.Count];
        GenAIPrefixes.CopyTo(prefixes, 0);
        filter.IncludeSources.CopyTo(prefixes, GenAIPrefixes.Length);
        _allowedPrefixes = prefixes;
    }

    public override void OnStart(Activity activity)
    {
        // Forward OnStart unconditionally — filtering is done at OnEnd
        _inner.OnStart(activity);
    }

    public override void OnEnd(Activity activity)
    {
        if (ShouldExport(activity))
        {
            _inner.OnEnd(activity);
        }
        // else: silently drop — inner exporter never sees it
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        return _inner.ForceFlush(timeoutMilliseconds);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _inner.Shutdown(timeoutMilliseconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool ShouldExport(Activity activity)
    {
        var sourceName = activity.Source.Name;

        if (_cache.TryGetValue(sourceName, out var cached))
        {
            return cached;
        }

        var result = MatchesPrefixes(sourceName);
        _cache.TryAdd(sourceName, result);
        return result;
    }

    private bool MatchesPrefixes(string sourceName)
    {
        var prefixes = _allowedPrefixes;

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (sourceName.StartsWith(prefixes[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
