// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Represents the severity of a validation finding.
/// </summary>
public enum A365ValidationSeverity
{
    /// <summary>
    /// Indicates a warning.
    /// </summary>
    Warning = 0,

    /// <summary>
    /// Indicates an error.
    /// </summary>
    Error = 1,
}
