// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>Explicit metadata for a tool that loads a skill or reads a skill resource.</summary>
    public sealed class SkillRefinementDetails : IExecuteToolRefinementDetails, IEquatable<SkillRefinementDetails>
    {
        /// <summary>Initializes a new instance of the <see cref="SkillRefinementDetails"/> class.</summary>
        /// <param name="kind">The skill operation performed by the tool.</param>
        /// <param name="skillName">The skill name, when available.</param>
        /// <param name="description">The skill description. May contain sensitive information.</param>
        /// <param name="sourceUri">The skill's source URI, not a temporary materialization path.
        /// Callers must remove sensitive components such as credentials and access tokens before supplying it.</param>
        /// <param name="resourceName">The skill-relative resource name, such as <c>references/policy.md</c>.</param>
        public SkillRefinementDetails(
            SkillRefinementKind kind,
            string? skillName = null,
            string? description = null,
            string? sourceUri = null,
            string? resourceName = null)
        {
            if (!Enum.IsDefined(typeof(SkillRefinementKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "The refinement kind must be a defined enum value.");
            }

            Kind = kind;
            SkillName = skillName;
            Description = description;
            SourceUri = sourceUri;
            ResourceName = resourceName;
        }

        /// <summary>Gets the skill operation performed by the tool.</summary>
        public SkillRefinementKind Kind { get; }

        /// <summary>Gets the skill name, when available.</summary>
        public string? SkillName { get; }

        /// <summary>Gets the skill description, when available.</summary>
        public string? Description { get; }

        /// <summary>Gets the caller-scrubbed skill source URI, when available.</summary>
        public string? SourceUri { get; }

        /// <summary>Gets the skill-relative resource name, when available.</summary>
        public string? ResourceName { get; }

        /// <inheritdoc/>
        public string GetSpanName(string toolName) =>
            ExecuteToolRefinementHelper.GetSpanName(toolName, Kind == SkillRefinementKind.LoadSkill, SkillName, ResourceName);

        /// <inheritdoc/>
        public IEnumerable<KeyValuePair<string, object>> GetAttributes() =>
            ExecuteToolRefinementHelper.GetAttributes(SkillName, Description, SourceUri, ResourceName);

        /// <inheritdoc/>
        public bool Equals(SkillRefinementDetails? other) =>
            other is not null &&
            (Kind, SkillName, Description, SourceUri, ResourceName)
                .Equals((other.Kind, other.SkillName, other.Description, other.SourceUri, other.ResourceName));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as SkillRefinementDetails);

        /// <inheritdoc/>
        public override int GetHashCode() => (Kind, SkillName, Description, SourceUri, ResourceName).GetHashCode();
    }

    /// <summary>Identifies a skill-loading or skill-resource-reading refinement of an execute-tool operation.</summary>
    public enum SkillRefinementKind
    {
        /// <summary>A dedicated tool loads a skill into the agent's context.</summary>
        LoadSkill,

        /// <summary>A dedicated tool reads a skill resource.</summary>
        ReadSkillResource,
    }
}
