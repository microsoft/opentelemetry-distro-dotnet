// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;

namespace Microsoft.OpenTelemetry.AzureMonitor.Internals;

/// <summary>
/// Exception extension methods.
/// </summary>
internal static class ExceptionExtensions
{
    /// <summary>
    /// Flattens an exception into a single exception by combining aggregate inner exceptions.
    /// </summary>
    internal static Exception FlattenException(this Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            return aggregateException.Flatten();
        }

        return exception;
    }

    /// <summary>
    /// Converts an exception to a culture-invariant string representation.
    /// </summary>
    internal static string ToInvariantString(this Exception exception)
    {
        return exception.ToString();
    }
}
