// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Immutable rule finding produced by A365 validation.
/// </summary>
public sealed class A365ValidationFinding
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A365ValidationFinding"/> class.
    /// </summary>
    /// <param name="ruleId">The rule identifier.</param>
    /// <param name="severity">The finding severity.</param>
    /// <param name="status">The finding status.</param>
    /// <param name="operationName">The operation name, if any.</param>
    /// <param name="attributeName">The attribute name, if any.</param>
    /// <param name="message">The finding message.</param>
    /// <param name="remediation">The remediation guidance.</param>
    /// <param name="suppressionReason">The suppression reason, if any.</param>
    /// <param name="traceId">The trace identifier, if any.</param>
    /// <param name="spanId">The span identifier, if any.</param>
    internal A365ValidationFinding(
        string ruleId,
        A365ValidationSeverity severity,
        A365ValidationFindingStatus status,
        string? operationName,
        string? attributeName,
        string message,
        string remediation,
        string? suppressionReason,
        string? traceId,
        string? spanId)
    {
        RuleId = ruleId;
        Severity = severity;
        Status = status;
        OperationName = operationName;
        AttributeName = attributeName;
        Message = message;
        Remediation = remediation;
        SuppressionReason = suppressionReason;
        TraceId = traceId;
        SpanId = spanId;
    }

    /// <summary>
    /// Gets the rule identifier.
    /// </summary>
    public string RuleId { get; }

    /// <summary>
    /// Gets the finding severity.
    /// </summary>
    public A365ValidationSeverity Severity { get; }

    /// <summary>
    /// Gets the finding status.
    /// </summary>
    public A365ValidationFindingStatus Status { get; }

    /// <summary>
    /// Gets the operation name, if any.
    /// </summary>
    public string? OperationName { get; }

    /// <summary>
    /// Gets the attribute name, if any.
    /// </summary>
    public string? AttributeName { get; }

    /// <summary>
    /// Gets the finding message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the remediation guidance.
    /// </summary>
    public string Remediation { get; }

    /// <summary>
    /// Gets the suppression reason, if any.
    /// </summary>
    public string? SuppressionReason { get; }

    /// <summary>
    /// Gets the trace identifier, if any.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    /// Gets the span identifier, if any.
    /// </summary>
    public string? SpanId { get; }
}
