// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal sealed class A365ValidationRule
{
    internal A365ValidationRule(
        string id,
        IReadOnlyCollection<string>? operationNames,
        string? attributeName,
        bool suppressible,
        Func<A365SpanSnapshot, string?> validate,
        string remediation)
    {
        Id = id;
        OperationNames = operationNames;
        AttributeName = attributeName;
        Suppressible = suppressible;
        Validate = validate;
        Remediation = remediation;
    }

    internal string Id { get; }

    internal IReadOnlyCollection<string>? OperationNames { get; }

    internal string? AttributeName { get; }

    internal bool Suppressible { get; }

    internal Func<A365SpanSnapshot, string?> Validate { get; }

    internal string Remediation { get; }
}
