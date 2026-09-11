// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

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
        /// <param name="targetAgentDetails">Optional target agent identity. Only <see cref="AgentDetails.AgentId"/>, <see cref="AgentDetails.AgentName"/>, <see cref="AgentDetails.AgentBlueprintId"/>, <see cref="AgentDetails.AgentPlatformId"/>, and <see cref="AgentDetails.AgentVersion"/> are emitted for this transfer model. Other <see cref="AgentDetails"/> properties are not emitted.</param>
        public TransferDetails(
            TransferMode mode,
            AgentDetails? targetAgentDetails = null)
        {
            if (!Enum.IsDefined(typeof(TransferMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "The transfer mode must be a defined enum value.");
            }

            Mode = mode;
            TargetAgentDetails = targetAgentDetails;
        }

        /// <summary>
        /// Gets how control passes to the target.
        /// </summary>
        public TransferMode Mode { get; }

        /// <summary>
        /// Gets the optional target agent identity whose supported fields are emitted for transfers.
        /// </summary>
        public AgentDetails? TargetAgentDetails { get; }

        internal string ModeValue => Mode switch
        {
            TransferMode.ReturnToCaller => "return_to_caller",
            TransferMode.PassControl => "pass_control",
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };

        /// <inheritdoc/>
        public bool Equals(TransferDetails? other) =>
            other is not null &&
            Mode == other.Mode &&
            EqualityComparer<AgentDetails?>.Default.Equals(TargetAgentDetails, other.TargetAgentDetails);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as TransferDetails);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + Mode.GetHashCode();
                hash = (hash * 31) + (TargetAgentDetails?.GetHashCode() ?? 0);
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
}
