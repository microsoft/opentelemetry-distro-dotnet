// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Exception thrown when A365 validation fails.
/// </summary>
public sealed class A365ValidationException : Exception
{
    internal A365ValidationException(A365ValidationReport report)
        : base(report?.ToString())
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <summary>
    /// Gets the typed validation report.
    /// </summary>
    public A365ValidationReport Report { get; }
}
