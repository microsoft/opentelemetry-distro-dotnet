// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Controls which activity sources are exported to the Agent365 backend.
/// By default, only GenAI and agent-related spans are exported. Infrastructure
/// spans (HTTP, SQL, Azure SDK) are excluded.
/// </summary>
public class Agent365SpanFilterOptions
{
    /// <summary>
    /// Additional activity source name prefixes to include alongside the built-in
    /// GenAI sources. Use this to export custom agent or application spans.
    /// </summary>
    /// <example>
    /// <code>
    /// o.Agent365.SpanFilter.IncludeSources.Add("MyApp.CustomAgent");
    /// </code>
    /// </example>
    public IList<string> IncludeSources { get; } = new List<string>();
}
