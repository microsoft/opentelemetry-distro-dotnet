// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal sealed class A365ValidationRule
{
    internal A365ValidationRule(
        string id,
        string? operationName,
        string? attributeName,
        bool suppressible,
        Func<A365SpanSnapshot, string?> validate,
        string remediation)
    {
        Id = id;
        OperationName = operationName;
        AttributeName = attributeName;
        Suppressible = suppressible;
        Validate = validate;
        Remediation = remediation;
    }

    internal string Id { get; }

    internal string? OperationName { get; }

    internal string? AttributeName { get; }

    internal bool Suppressible { get; }

    internal Func<A365SpanSnapshot, string?> Validate { get; }

    internal string Remediation { get; }
}
