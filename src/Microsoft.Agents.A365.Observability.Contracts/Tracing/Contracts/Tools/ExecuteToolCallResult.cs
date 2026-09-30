// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>Represents the status of a tool call outcome.</summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolCallOutcomeStatus
    {
        /// <summary>The tool call completed successfully.</summary>
        Success,

        /// <summary>The tool call failed.</summary>
        Failure,
    }

    /// <summary>Represents a policy decision for a tool call.</summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolPolicyDecision
    {
        /// <summary>The policy allows the tool call.</summary>
        Allow,

        /// <summary>The policy denies the tool call.</summary>
        Deny,
    }

    /// <summary>Represents the structured result for an execute tool call.</summary>
    public sealed class ExecuteToolCallResult
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the schema version.</summary>
        [JsonPropertyName("schema_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SchemaVersion { get; set; } = "1.0";

        /// <summary>Gets or sets the tool call outcome.</summary>
        [JsonPropertyName("outcome")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultOutcome? Outcome { get; set; }

        /// <summary>Gets or sets the tool call resources.</summary>
        [JsonPropertyName("resources")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<ToolCallResultResource>? Resources { get; set; }

        /// <summary>Gets or sets the tool call data.</summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Data { get; set; }

        /// <summary>Gets or sets the pagination information.</summary>
        [JsonPropertyName("pagination")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultPagination? Pagination { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents a resource included in a tool call result.</summary>
    public sealed class ToolCallResultResource
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the resource identifier.</summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        /// <summary>Gets or sets the resource URI.</summary>
        [JsonPropertyName("uri")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Uri { get; set; }

        /// <summary>Gets or sets the resource name.</summary>
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        /// <summary>Gets or sets the resource type.</summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }

        /// <summary>Gets or sets the resource provider.</summary>
        [JsonPropertyName("provider")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Provider { get; set; }

        /// <summary>Gets or sets the resource identifiers.</summary>
        [JsonPropertyName("identifiers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<ToolCallIdentifier>? Identifiers { get; set; }

        /// <summary>Gets or sets the resource container.</summary>
        [JsonPropertyName("container")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallContainer? Container { get; set; }

        /// <summary>Gets or sets the tool call outcome.</summary>
        [JsonPropertyName("outcome")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultOutcome? Outcome { get; set; }

        /// <summary>Gets or sets the sensitivity details.</summary>
        [JsonPropertyName("sensitivity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultSensitivity? Sensitivity { get; set; }

        /// <summary>Gets or sets the policy details.</summary>
        [JsonPropertyName("policy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultPolicy? Policy { get; set; }

        /// <summary>Gets or sets the security details.</summary>
        [JsonPropertyName("security")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallResultSecurity? Security { get; set; }

        /// <summary>Gets or sets the resource data.</summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Data { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents the outcome of a tool call.</summary>
    public sealed class ToolCallResultOutcome
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the outcome status.</summary>
        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallOutcomeStatus? Status { get; set; }

        /// <summary>Gets or sets the tool-specific code.</summary>
        [JsonPropertyName("code")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Code { get; set; }

        /// <summary>Gets or sets the provider-specific code.</summary>
        [JsonPropertyName("provider_code")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProviderCode { get; set; }

        /// <summary>Gets or sets the outcome message.</summary>
        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents sensitivity metadata for a tool call result.</summary>
    public sealed class ToolCallResultSensitivity
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the sensitivity label identifier.</summary>
        [JsonPropertyName("label_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LabelId { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents policy metadata for a tool call result.</summary>
    public sealed class ToolCallResultPolicy
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the policy decision.</summary>
        [JsonPropertyName("decision")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolPolicyDecision? Decision { get; set; }

        /// <summary>Gets or sets the policy identifier.</summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        /// <summary>Gets or sets the policy name.</summary>
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents security metadata for a tool call result.</summary>
    public sealed class ToolCallResultSecurity
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets a value indicating whether xpia was detected.</summary>
        [JsonPropertyName("xpia_detected")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? XpiaDetected { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Represents pagination metadata for a tool call result.</summary>
    public sealed class ToolCallResultPagination
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets a value indicating whether more results are available.</summary>
        [JsonPropertyName("has_more")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HasMore { get; set; }

        /// <summary>Gets or sets the cursor for the next page.</summary>
        [JsonPropertyName("next_cursor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NextCursor { get; set; }

        /// <summary>Gets or sets the total result count.</summary>
        [JsonPropertyName("total_count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TotalCount { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
