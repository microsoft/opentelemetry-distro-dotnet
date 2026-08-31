// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>
    /// Represents the action taken by an execute tool call.
    /// </summary>
    public enum ToolCallAction
    {
        /// <summary>
        /// Creates a resource.
        /// </summary>
        Create,

        /// <summary>
        /// Reads a resource.
        /// </summary>
        Read,

        /// <summary>
        /// Updates a resource.
        /// </summary>
        Update,

        /// <summary>
        /// Deletes a resource.
        /// </summary>
        Delete,
    }

    /// <summary>
    /// Represents the structured arguments for an execute tool call.
    /// </summary>
    public sealed class ExecuteToolCallArguments : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExecuteToolCallArguments"/> class.
        /// </summary>
        public ExecuteToolCallArguments()
        {
            SchemaVersion = "1.0";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecuteToolCallArguments"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ExecuteToolCallArguments(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
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
        /// Gets or sets the tool call resources.
        /// </summary>
        public IList<ToolCallResource>? Resources
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallResource>>(this, "resources");
            set => ToolCallDictionaryAccessor.SetReference(this, "resources", value);
        }

        /// <summary>
        /// Gets or sets the action.
        /// </summary>
        public ToolCallAction? Action
        {
            get => ToolCallDictionaryAccessor.GetEnum<ToolCallAction>(this, "action");
            set => ToolCallDictionaryAccessor.SetEnum(this, "action", value);
        }

        /// <summary>
        /// Gets or sets the tool parameters.
        /// </summary>
        public IDictionary<string, object?>? Parameters
        {
            get => ToolCallDictionaryAccessor.GetReference<IDictionary<string, object?>>(this, "parameters");
            set => ToolCallDictionaryAccessor.SetReference(this, "parameters", value);
        }
    }

    /// <summary>
    /// Represents a resource referenced by an execute tool call.
    /// </summary>
    public sealed class ToolCallResource : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResource"/> class.
        /// </summary>
        public ToolCallResource()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallResource"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallResource(IDictionary<string, object?> values)
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
    }

    /// <summary>
    /// Represents a resource identifier.
    /// </summary>
    public sealed class ToolCallIdentifier : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallIdentifier"/> class.
        /// </summary>
        public ToolCallIdentifier()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallIdentifier"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallIdentifier(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the identifier type.
        /// </summary>
        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }

        /// <summary>
        /// Gets or sets the identifier value.
        /// </summary>
        public string? Value
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "value");
            set => ToolCallDictionaryAccessor.SetReference(this, "value", value);
        }
    }

    /// <summary>
    /// Represents the resource container for a tool call.
    /// </summary>
    public sealed class ToolCallContainer : Dictionary<string, object?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallContainer"/> class.
        /// </summary>
        public ToolCallContainer()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallContainer"/> class from existing values.
        /// </summary>
        /// <param name="values">The existing values to copy into the dictionary-backed model.</param>
        public ToolCallContainer(IDictionary<string, object?> values)
            : base(values ?? throw new System.ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets or sets the container identifier.
        /// </summary>
        public string? Id
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "id");
            set => ToolCallDictionaryAccessor.SetReference(this, "id", value);
        }

        /// <summary>
        /// Gets or sets the container URI.
        /// </summary>
        public string? Uri
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "uri");
            set => ToolCallDictionaryAccessor.SetReference(this, "uri", value);
        }

        /// <summary>
        /// Gets or sets the container type.
        /// </summary>
        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }
    }
}
