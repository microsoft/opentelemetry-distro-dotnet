// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>
    /// Represents the action taken by an execute tool call.
    /// </summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolCallAction
    {
        /// <summary>Creates a resource.</summary>
        Create,

        /// <summary>Reads a resource.</summary>
        Read,

        /// <summary>Updates a resource.</summary>
        Update,

        /// <summary>Deletes a resource.</summary>
        Delete,
    }

    /// <summary>
    /// Represents the structured arguments for an execute tool call.
    /// </summary>
    public sealed class ExecuteToolCallArguments
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the schema version.</summary>
        [JsonPropertyName("schema_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SchemaVersion { get; set; } = "1.0";

        /// <summary>Gets or sets the tool call resources.</summary>
        [JsonPropertyName("resources")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IList<ToolCallResource>? Resources { get; set; }

        /// <summary>Gets or sets the action.</summary>
        [JsonPropertyName("action")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallAction? Action { get; set; }

        /// <summary>Gets or sets the tool parameters.</summary>
        [JsonPropertyName("parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Parameters { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Represents a resource referenced by an execute tool call.
    /// </summary>
    public sealed class ToolCallResource
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

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Represents a resource identifier.
    /// </summary>
    public sealed class ToolCallIdentifier
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the identifier type.</summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }

        /// <summary>Gets or sets the identifier value.</summary>
        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Value { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Represents the resource container for a tool call.
    /// </summary>
    public sealed class ToolCallContainer
    {
        private IDictionary<string, object?> additionalProperties =
            new Dictionary<string, object?>();

        /// <summary>Gets or sets the container identifier.</summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        /// <summary>Gets or sets the container URI.</summary>
        [JsonPropertyName("uri")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Uri { get; set; }

        /// <summary>Gets or sets the container type.</summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }

        /// <summary>Gets or sets provider-specific metadata not defined by the schema.</summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, object?> AdditionalProperties
        {
            get => this.additionalProperties;
            set => this.additionalProperties = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
