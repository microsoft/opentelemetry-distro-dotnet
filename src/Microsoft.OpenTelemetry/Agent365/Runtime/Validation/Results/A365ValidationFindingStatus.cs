// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Represents the status of a validation finding.
/// </summary>
public enum A365ValidationFindingStatus
{
    /// <summary>
    /// The finding is active.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The finding is suppressed.
    /// </summary>
    Suppressed = 1,
}
