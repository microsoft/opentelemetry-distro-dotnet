// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>Explicit metadata for a tool that executes commands, optionally including a skill's script.</summary>
    public sealed class ExecuteCommandRefinementDetails : IExecuteToolRefinementDetails, IEquatable<ExecuteCommandRefinementDetails>
    {
        /// <summary>Initializes a new instance of the <see cref="ExecuteCommandRefinementDetails"/> class.</summary>
        /// <param name="executableName">The executable name, only when the tool directly executes a single process.</param>
        /// <param name="executablePath">The executable path, only when the tool directly executes a single process.</param>
        /// <param name="exitCode">The reported exit code of a single command or process, when available.</param>
        /// <param name="skillName">The skill name, when the command executes a skill's script.</param>
        /// <param name="description">The skill description. May contain sensitive information.</param>
        /// <param name="sourceUri">The skill's source URI, not a temporary materialization path.
        /// Callers must remove sensitive components such as credentials and access tokens before supplying it.</param>
        /// <param name="resourceName">The skill-relative script name, such as <c>scripts/check.py</c>.</param>
        public ExecuteCommandRefinementDetails(
            string? executableName = null,
            string? executablePath = null,
            int? exitCode = null,
            string? skillName = null,
            string? description = null,
            string? sourceUri = null,
            string? resourceName = null)
        {
            ExecutableName = executableName;
            ExecutablePath = executablePath;
            ExitCode = exitCode;
            SkillName = skillName;
            Description = description;
            SourceUri = sourceUri;
            ResourceName = resourceName;
        }

        /// <summary>Gets the executable name for a single process, when available.</summary>
        public string? ExecutableName { get; }

        /// <summary>Gets the executable path for a single process, when available.</summary>
        public string? ExecutablePath { get; }

        /// <summary>Gets the reported command or process exit code, when available.</summary>
        public int? ExitCode { get; }

        /// <summary>Gets the skill name, when available.</summary>
        public string? SkillName { get; }

        /// <summary>Gets the skill description, when available.</summary>
        public string? Description { get; }

        /// <summary>Gets the caller-scrubbed skill source URI, when available.</summary>
        public string? SourceUri { get; }

        /// <summary>Gets the skill-relative script name, when available.</summary>
        public string? ResourceName { get; }

        /// <inheritdoc/>
        public string GetSpanName(string toolName) =>
            ExecuteToolRefinementHelper.GetSpanName(toolName, false, SkillName, ResourceName, ExecutableName);

        /// <inheritdoc/>
        public IEnumerable<KeyValuePair<string, object>> GetAttributes() =>
            ExecuteToolRefinementHelper.GetAttributes(SkillName, Description, SourceUri, ResourceName, ExecutableName, ExecutablePath, ExitCode);

        /// <inheritdoc/>
        public bool Equals(ExecuteCommandRefinementDetails? other) =>
            other is not null &&
            (SkillName, Description, SourceUri, ResourceName, ExecutableName, ExecutablePath, ExitCode)
                .Equals((other.SkillName, other.Description, other.SourceUri, other.ResourceName,
                    other.ExecutableName, other.ExecutablePath, other.ExitCode));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ExecuteCommandRefinementDetails);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            (SkillName, Description, SourceUri, ResourceName, ExecutableName, ExecutablePath, ExitCode).GetHashCode();
    }
}
