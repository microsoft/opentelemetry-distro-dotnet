// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    internal static class ExecuteToolRefinementHelper
    {
        internal static string GetSpanName(
            string toolName, bool loadSkill, string? skillName, string? resourceName, string? executableName = null)
        {
            var name = $"{OpenTelemetryConstants.ExecuteToolOperationName} {toolName}";
            if (loadSkill)
            {
                return string.IsNullOrWhiteSpace(skillName) ? name : $"{name} {skillName}";
            }

            if (!string.IsNullOrWhiteSpace(resourceName))
            {
                return string.IsNullOrWhiteSpace(skillName)
                    ? $"{name} {resourceName}"
                    : $"{name} {skillName} {resourceName}";
            }

            return string.IsNullOrWhiteSpace(executableName) ? name : $"{name} {executableName}";
        }

        internal static IEnumerable<KeyValuePair<string, object>> GetAttributes(
            string? skillName, string? description, string? sourceUri, string? resourceName,
            string? executableName = null, string? executablePath = null, int? exitCode = null)
        {
            if (!string.IsNullOrWhiteSpace(skillName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillNameKey, skillName!);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillDescriptionKey, description!);
            }

            if (!string.IsNullOrWhiteSpace(sourceUri))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillSourceUriKey, sourceUri!);
            }

            if (!string.IsNullOrWhiteSpace(resourceName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.GenAiSkillResourceNameKey, resourceName!);
            }

            if (!string.IsNullOrWhiteSpace(executableName))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExecutableNameKey, executableName!);
            }

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExecutablePathKey, executablePath!);
            }

            if (exitCode.HasValue)
            {
                yield return new KeyValuePair<string, object>(OpenTelemetryConstants.ProcessExitCodeKey, exitCode.Value);
            }
        }
    }
}
