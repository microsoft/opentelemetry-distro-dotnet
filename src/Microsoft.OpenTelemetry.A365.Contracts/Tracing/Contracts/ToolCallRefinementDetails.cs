// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Explicit semantic-convention metadata for skill and command tool executions.
    /// </summary>
    /// <remarks>Supply metadata from the tool API or runtime state, including for failed attempts.
    /// Do not infer a refinement from a tool's name or opaque arguments.</remarks>
    public sealed class ToolCallRefinementDetails : IEquatable<ToolCallRefinementDetails>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallRefinementDetails"/> class.
        /// </summary>
        /// <param name="kind">The specialized operation performed by the tool.</param>
        /// <param name="skillName">The skill name, when available.</param>
        /// <param name="description">The skill description. May contain sensitive information.</param>
        /// <param name="sourceUri">The skill's source URI, not a temporary materialization path.
        /// Callers must remove sensitive components such as credentials and access tokens before supplying it.</param>
        /// <param name="resourceName">The skill-relative resource name, such as <c>scripts/check.py</c>.</param>
        /// <param name="executableName">The executable name, only when the tool directly executes a single process.</param>
        /// <param name="executablePath">The executable path, only when the tool directly executes a single process.</param>
        /// <param name="exitCode">The reported exit code of a single command or process, when available.</param>
        public ToolCallRefinementDetails(
            ToolCallRefinementKind kind,
            string? skillName = null,
            string? description = null,
            string? sourceUri = null,
            string? resourceName = null,
            string? executableName = null,
            string? executablePath = null,
            int? exitCode = null)
        {
            if (!Enum.IsDefined(typeof(ToolCallRefinementKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "The refinement kind must be a defined enum value.");
            }

            if (kind != ToolCallRefinementKind.ExecuteCommand &&
                (executableName != null || executablePath != null || exitCode.HasValue))
            {
                throw new ArgumentException("Process metadata can only be supplied for command execution.", nameof(kind));
            }

            Kind = kind;
            SkillName = skillName;
            Description = description;
            SourceUri = sourceUri;
            ResourceName = resourceName;
            ExecutableName = executableName;
            ExecutablePath = executablePath;
            ExitCode = exitCode;
        }

        /// <summary>Gets the specialized operation performed by the tool.</summary>
        public ToolCallRefinementKind Kind { get; }

        /// <summary>Gets the skill name, when available.</summary>
        public string? SkillName { get; }

        /// <summary>Gets the skill description, when available.</summary>
        public string? Description { get; }

        /// <summary>Gets the caller-scrubbed skill source URI, when available.</summary>
        public string? SourceUri { get; }

        /// <summary>Gets the skill-relative resource name, when available.</summary>
        public string? ResourceName { get; }

        /// <summary>Gets the executable name for a single process, when available.</summary>
        public string? ExecutableName { get; }

        /// <summary>Gets the executable path for a single process, when available.</summary>
        public string? ExecutablePath { get; }

        /// <summary>Gets the reported command or process exit code, when available.</summary>
        public int? ExitCode { get; }

        /// <summary>
        /// Resolves the execute-tool span name using the refinement's semantic-convention fallback rules.
        /// </summary>
        /// <param name="toolName">The tool name exposed by the runtime.</param>
        /// <returns>The refined execute-tool span name.</returns>
        public string GetSpanName(string toolName)
        {
            var name = $"{OpenTelemetryConstants.ExecuteToolOperationName} {toolName}";
            if (Kind == ToolCallRefinementKind.LoadSkill)
            {
                return string.IsNullOrWhiteSpace(SkillName) ? name : $"{name} {SkillName}";
            }

            if (!string.IsNullOrWhiteSpace(ResourceName))
            {
                return string.IsNullOrWhiteSpace(SkillName)
                    ? $"{name} {ResourceName}"
                    : $"{name} {SkillName} {ResourceName}";
            }

            return Kind == ToolCallRefinementKind.ExecuteCommand && !string.IsNullOrWhiteSpace(ExecutableName)
                ? $"{name} {ExecutableName}"
                : name;
        }

        /// <summary>Gets the available semantic-convention attributes, omitting empty strings.</summary>
        /// <returns>The explicitly supplied skill and process attributes.</returns>
        public IEnumerable<KeyValuePair<string, object>> GetAttributes()
        {
            if (!string.IsNullOrWhiteSpace(SkillName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillNameKey, SkillName!);
            }

            if (!string.IsNullOrWhiteSpace(Description))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillDescriptionKey, Description!);
            }

            if (!string.IsNullOrWhiteSpace(SourceUri))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillSourceUriKey, SourceUri!);
            }

            if (!string.IsNullOrWhiteSpace(ResourceName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillResourceNameKey, ResourceName!);
            }

            if (!string.IsNullOrWhiteSpace(ExecutableName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExecutableNameKey, ExecutableName!);
            }

            if (!string.IsNullOrWhiteSpace(ExecutablePath))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExecutablePathKey, ExecutablePath!);
            }

            if (ExitCode.HasValue)
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExitCodeKey, ExitCode.Value);
            }
        }

        /// <inheritdoc/>
        public bool Equals(ToolCallRefinementDetails? other) =>
            other is not null &&
            (Kind, SkillName, Description, SourceUri, ResourceName, ExecutableName, ExecutablePath, ExitCode)
                .Equals((other.Kind, other.SkillName, other.Description, other.SourceUri, other.ResourceName,
                    other.ExecutableName, other.ExecutablePath, other.ExitCode));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ToolCallRefinementDetails);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            (Kind, SkillName, Description, SourceUri, ResourceName, ExecutableName, ExecutablePath, ExitCode).GetHashCode();
    }

    /// <summary>Identifies a semantic-convention refinement of an execute-tool operation.</summary>
    public enum ToolCallRefinementKind
    {
        /// <summary>A dedicated tool loads a skill into the agent's context.</summary>
        LoadSkill,

        /// <summary>A dedicated tool reads a skill resource.</summary>
        ReadSkillResource,

        /// <summary>A tool executes commands, optionally including a skill's script.</summary>
        ExecuteCommand,
    }
}
