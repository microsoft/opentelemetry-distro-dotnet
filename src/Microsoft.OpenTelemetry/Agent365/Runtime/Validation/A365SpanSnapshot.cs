// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Immutable snapshot of a captured span and its attributes.
/// </summary>
public sealed class A365SpanSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A365SpanSnapshot"/> class.
    /// </summary>
    /// <param name="traceId">The trace identifier.</param>
    /// <param name="spanId">The span identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="sourceName">The source name.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="attributes">The span attributes.</param>
    internal A365SpanSnapshot(
        string traceId,
        string spanId,
        string displayName,
        string sourceName,
        string operationName,
        IDictionary<string, object?> attributes)
    {
        TraceId = traceId;
        SpanId = spanId;
        DisplayName = displayName;
        SourceName = sourceName;
        OperationName = operationName;
        Attributes = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(attributes, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the trace identifier.
    /// </summary>
    public string TraceId { get; }

    /// <summary>
    /// Gets the span identifier.
    /// </summary>
    public string SpanId { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the source name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the operation name.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the captured span attributes.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; }
}
