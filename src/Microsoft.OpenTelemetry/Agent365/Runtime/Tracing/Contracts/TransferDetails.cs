// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Describes an explicit transfer performed by an execute-tool operation.
    /// </summary>
    public sealed class TransferDetails : IEquatable<TransferDetails>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransferDetails"/> class.
        /// </summary>
        /// <param name="mode">How control passes to the target.</param>
        /// <param name="targetName">Optional human-readable target name.</param>
        /// <param name="targetType">Optional target classification.</param>
        public TransferDetails(
            TransferMode mode,
            string? targetName = null,
            TransferTargetType? targetType = null)
        {
            Mode = mode;
            TargetName = targetName;
            TargetType = targetType;
        }

        /// <summary>
        /// Gets how control passes to the target.
        /// </summary>
        public TransferMode Mode { get; }

        /// <summary>
        /// Gets the optional human-readable target name.
        /// </summary>
        public string? TargetName { get; }

        /// <summary>
        /// Gets the optional target classification.
        /// </summary>
        public TransferTargetType? TargetType { get; }

        internal string ModeValue => Mode switch
        {
            TransferMode.ReturnToCaller => "return_to_caller",
            TransferMode.PassControl => "pass_control",
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };

        internal string? TargetTypeValue => TargetType switch
        {
            TransferTargetType.Agent => "agent",
            TransferTargetType.Human => "human",
            TransferTargetType.Workflow => "workflow",
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(TargetType)),
        };

        /// <inheritdoc/>
        public bool Equals(TransferDetails? other) =>
            other is not null &&
            Mode == other.Mode &&
            string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
            TargetType == other.TargetType;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as TransferDetails);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + Mode.GetHashCode();
                hash = (hash * 31) + (TargetName == null ? 0 : StringComparer.Ordinal.GetHashCode(TargetName));
                hash = (hash * 31) + TargetType.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Describes how control passes during a transfer.
    /// </summary>
    public enum TransferMode
    {
        /// <summary>
        /// The source agent waits for the target and resumes afterward.
        /// </summary>
        ReturnToCaller,

        /// <summary>
        /// The target assumes control of the remaining work.
        /// </summary>
        PassControl,
    }

    /// <summary>
    /// Describes the type of transfer target.
    /// </summary>
    public enum TransferTargetType
    {
        /// <summary>
        /// Another agent.
        /// </summary>
        Agent,

        /// <summary>
        /// A human participant.
        /// </summary>
        Human,

        /// <summary>
        /// A workflow.
        /// </summary>
        Workflow,
    }
}
