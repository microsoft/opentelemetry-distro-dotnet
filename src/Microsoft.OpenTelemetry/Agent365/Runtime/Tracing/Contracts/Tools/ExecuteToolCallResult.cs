// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>
    /// Represents the status of a tool call outcome.
    /// </summary>
    public enum ToolCallOutcomeStatus
    {
        /// <summary>
        /// The tool call completed successfully.
        /// </summary>
        Success,

        /// <summary>
        /// The tool call failed.
        /// </summary>
        Failure,
    }

    /// <summary>
    /// Represents a policy decision for a tool call.
    /// </summary>
    public enum ToolPolicyDecision
    {
        /// <summary>
        /// The policy allows the tool call.
        /// </summary>
        Allow,

        /// <summary>
        /// The policy denies the tool call.
        /// </summary>
        Deny,
    }

    /// <summary>
    /// Represents the structured result for an execute tool call.
    /// </summary>
    public sealed class ExecuteToolCallResult : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExecuteToolCallResult"/> class.
        /// </summary>
        public ExecuteToolCallResult()
        {
            SchemaVersion = "1.0";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecuteToolCallResult"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ExecuteToolCallResult(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
            if (!ContainsKey("schema_version"))
            {
                SchemaVersion = "1.0";
            }
        }

        /// <summary>
        /// Gets or sets the schema version.
        /// </summary>
        public string? SchemaVersion
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "schema_version");
            set => ToolCallDictionaryAccessor.SetReference(this, "schema_version", value);
        }

        /// <summary>
        /// Gets or sets the tool call outcome.
        /// </summary>
        public ToolCallResultOutcome? Outcome
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultOutcome>(this, "outcome");
            set => ToolCallDictionaryAccessor.SetReference(this, "outcome", value);
        }

        /// <summary>
        /// Gets or sets the tool call resources.
        /// </summary>
        public IList<ToolCallResultResource>? Resources
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallResultResource>>(this, "resources");
            set => ToolCallDictionaryAccessor.SetReference(this, "resources", value);
        }

        /// <summary>
        /// Gets or sets the tool call data.
        /// </summary>
        public IDictionary<string, object?>? Data
        {
            get => ToolCallDictionaryAccessor.GetReference<IDictionary<string, object?>>(this, "data");
            set => ToolCallDictionaryAccessor.SetReference(this, "data", value);
        }

        /// <summary>
        /// Gets or sets the pagination information.
        /// </summary>
        public ToolCallResultPagination? Pagination
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultPagination>(this, "pagination");
            set => ToolCallDictionaryAccessor.SetReference(this, "pagination", value);
        }
    }

    /// <summary>
    /// Represents a resource included in a tool call result.
    /// </summary>
    public sealed class ToolCallResultResource : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultResource"/> class.
        /// </summary>
        public ToolCallResultResource()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultResource"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultResource(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the resource identifier.
        /// </summary>
        public string? Id
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "id");
            set => ToolCallDictionaryAccessor.SetReference(this, "id", value);
        }

        /// <summary>
        /// Gets or sets the resource URI.
        /// </summary>
        public string? Uri
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "uri");
            set => ToolCallDictionaryAccessor.SetReference(this, "uri", value);
        }

        /// <summary>
        /// Gets or sets the resource name.
        /// </summary>
        public string? Name
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "name");
            set => ToolCallDictionaryAccessor.SetReference(this, "name", value);
        }

        /// <summary>
        /// Gets or sets the resource type.
        /// </summary>
        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }

        /// <summary>
        /// Gets or sets the resource provider.
        /// </summary>
        public string? Provider
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "provider");
            set => ToolCallDictionaryAccessor.SetReference(this, "provider", value);
        }

        /// <summary>
        /// Gets or sets the resource identifiers.
        /// </summary>
        public IList<ToolCallIdentifier>? Identifiers
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallIdentifier>>(this, "identifiers");
            set => ToolCallDictionaryAccessor.SetReference(this, "identifiers", value);
        }

        /// <summary>
        /// Gets or sets the resource container.
        /// </summary>
        public ToolCallContainer? Container
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallContainer>(this, "container");
            set => ToolCallDictionaryAccessor.SetReference(this, "container", value);
        }

        /// <summary>
        /// Gets or sets the tool call outcome.
        /// </summary>
        public ToolCallResultOutcome? Outcome
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultOutcome>(this, "outcome");
            set => ToolCallDictionaryAccessor.SetReference(this, "outcome", value);
        }

        /// <summary>
        /// Gets or sets the sensitivity details.
        /// </summary>
        public ToolCallResultSensitivity? Sensitivity
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultSensitivity>(this, "sensitivity");
            set => ToolCallDictionaryAccessor.SetReference(this, "sensitivity", value);
        }

        /// <summary>
        /// Gets or sets the policy details.
        /// </summary>
        public ToolCallResultPolicy? Policy
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultPolicy>(this, "policy");
            set => ToolCallDictionaryAccessor.SetReference(this, "policy", value);
        }

        /// <summary>
        /// Gets or sets the security details.
        /// </summary>
        public ToolCallResultSecurity? Security
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultSecurity>(this, "security");
            set => ToolCallDictionaryAccessor.SetReference(this, "security", value);
        }

        /// <summary>
        /// Gets or sets the resource data.
        /// </summary>
        public IDictionary<string, object?>? Data
        {
            get => ToolCallDictionaryAccessor.GetReference<IDictionary<string, object?>>(this, "data");
            set => ToolCallDictionaryAccessor.SetReference(this, "data", value);
        }
    }

    /// <summary>
    /// Represents the outcome of a tool call.
    /// </summary>
    public sealed class ToolCallResultOutcome : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultOutcome"/> class.
        /// </summary>
        public ToolCallResultOutcome()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultOutcome"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultOutcome(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the outcome status.
        /// </summary>
        public ToolCallOutcomeStatus? Status
        {
            get => ToolCallDictionaryAccessor.GetEnum<ToolCallOutcomeStatus>(this, "status");
            set => ToolCallDictionaryAccessor.SetEnum(this, "status", value);
        }

        /// <summary>
        /// Gets or sets the tool-specific code.
        /// </summary>
        public string? Code
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "code");
            set => ToolCallDictionaryAccessor.SetReference(this, "code", value);
        }

        /// <summary>
        /// Gets or sets the provider-specific code.
        /// </summary>
        public string? ProviderCode
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "provider_code");
            set => ToolCallDictionaryAccessor.SetReference(this, "provider_code", value);
        }

        /// <summary>
        /// Gets or sets the outcome message.
        /// </summary>
        public string? Message
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "message");
            set => ToolCallDictionaryAccessor.SetReference(this, "message", value);
        }
    }

    /// <summary>
    /// Represents sensitivity metadata for a tool call result.
    /// </summary>
    public sealed class ToolCallResultSensitivity : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultSensitivity"/> class.
        /// </summary>
        public ToolCallResultSensitivity()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultSensitivity"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultSensitivity(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the sensitivity label identifier.
        /// </summary>
        public string? LabelId
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "label_id");
            set => ToolCallDictionaryAccessor.SetReference(this, "label_id", value);
        }
    }

    /// <summary>
    /// Represents policy metadata for a tool call result.
    /// </summary>
    public sealed class ToolCallResultPolicy : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultPolicy"/> class.
        /// </summary>
        public ToolCallResultPolicy()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultPolicy"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultPolicy(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the policy decision.
        /// </summary>
        public ToolPolicyDecision? Decision
        {
            get => ToolCallDictionaryAccessor.GetEnum<ToolPolicyDecision>(this, "decision");
            set => ToolCallDictionaryAccessor.SetEnum(this, "decision", value);
        }

        /// <summary>
        /// Gets or sets the policy identifier.
        /// </summary>
        public string? Id
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "id");
            set => ToolCallDictionaryAccessor.SetReference(this, "id", value);
        }

        /// <summary>
        /// Gets or sets the policy name.
        /// </summary>
        public string? Name
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "name");
            set => ToolCallDictionaryAccessor.SetReference(this, "name", value);
        }
    }

    /// <summary>
    /// Represents security metadata for a tool call result.
    /// </summary>
    public sealed class ToolCallResultSecurity : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultSecurity"/> class.
        /// </summary>
        public ToolCallResultSecurity()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultSecurity"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultSecurity(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether xpia was detected.
        /// </summary>
        public bool? XpiaDetected
        {
            get => ToolCallDictionaryAccessor.GetValue<bool>(this, "xpia_detected");
            set => ToolCallDictionaryAccessor.SetValue(this, "xpia_detected", value);
        }
    }

    /// <summary>
    /// Represents pagination metadata for a tool call result.
    /// </summary>
    public sealed class ToolCallResultPagination : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultPagination"/> class.
        /// </summary>
        public ToolCallResultPagination()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResultPagination"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResultPagination(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether more results are available.
        /// </summary>
        public bool? HasMore
        {
            get => ToolCallDictionaryAccessor.GetValue<bool>(this, "has_more");
            set => ToolCallDictionaryAccessor.SetValue(this, "has_more", value);
        }

        /// <summary>
        /// Gets or sets the cursor for the next page.
        /// </summary>
        public string? NextCursor
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "next_cursor");
            set => ToolCallDictionaryAccessor.SetReference(this, "next_cursor", value);
        }

        /// <summary>
        /// Gets or sets the total result count.
        /// </summary>
        public long? TotalCount
        {
            get => ToolCallDictionaryAccessor.GetValue<long>(this, "total_count");
            set => ToolCallDictionaryAccessor.SetValue(this, "total_count", value);
        }
    }
}
