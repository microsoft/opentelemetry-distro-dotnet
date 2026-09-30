// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    internal sealed class ExecuteToolJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert == typeof(ExecuteToolCallArguments) ||
            typeToConvert == typeof(ExecuteToolCallResult) ||
            typeToConvert == typeof(ToolCallResource) ||
            typeToConvert == typeof(ToolCallResultResource) ||
            typeToConvert == typeof(ToolCallIdentifier) ||
            typeToConvert == typeof(ToolCallContainer) ||
            typeToConvert == typeof(ToolCallResultOutcome) ||
            typeToConvert == typeof(ToolCallResultSensitivity) ||
            typeToConvert == typeof(ToolCallResultPolicy) ||
            typeToConvert == typeof(ToolCallResultSecurity) ||
            typeToConvert == typeof(ToolCallResultPagination);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            if (!CanConvert(typeToConvert))
            {
                throw new NotSupportedException(
                    $"Type '{typeToConvert}' is not an execute tool schema model.");
            }

            return (JsonConverter)Activator.CreateInstance(
                typeof(ExecuteToolJsonConverter<>).MakeGenericType(typeToConvert))!;
        }
    }

    internal sealed class ExecuteToolJsonConverter<TModel> : JsonConverter<TModel>
        where TModel : class, new()
    {
        private const string MetadataPropertyName = "metadata";
        private static readonly PropertyInfo AdditionalPropertiesProperty =
            typeof(TModel).GetProperty(nameof(ExecuteToolCallArguments.AdditionalProperties))!;
        private static readonly ExecuteToolJsonProperty[] DeclaredProperties = CreateDeclaredProperties();

        public override TModel Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"Expected a JSON object for '{typeToConvert.Name}'.");
            }

            var model = new TModel();
            foreach (var jsonProperty in document.RootElement.EnumerateObject())
            {
                if (jsonProperty.NameEquals(MetadataPropertyName))
                {
                    AdditionalPropertiesProperty.SetValue(
                        model,
                        DeserializeMetadata(jsonProperty.Value, options));
                    continue;
                }

                var declaredProperty = FindDeclaredProperty(jsonProperty.Name);
                if (declaredProperty != null)
                {
                    declaredProperty.Property.SetValue(
                        model,
                        JsonSerializer.Deserialize(
                            jsonProperty.Value.GetRawText(),
                            declaredProperty.Property.PropertyType,
                            options));
                }
            }

            return model;
        }

        public override void Write(
            Utf8JsonWriter writer,
            TModel value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            foreach (var declaredProperty in DeclaredProperties)
            {
                var propertyValue = declaredProperty.Property.GetValue(value);
                if (propertyValue == null)
                {
                    continue;
                }

                writer.WritePropertyName(declaredProperty.JsonName);
                JsonSerializer.Serialize(
                    writer,
                    propertyValue,
                    declaredProperty.Property.PropertyType,
                    options);
            }

            var metadata =
                (IDictionary<string, object?>)AdditionalPropertiesProperty.GetValue(value)!;
            if (metadata.Count > 0)
            {
                writer.WritePropertyName(MetadataPropertyName);
                JsonSerializer.Serialize(writer, metadata, options);
            }

            writer.WriteEndObject();
        }

        private static IDictionary<string, object?> DeserializeMetadata(
            JsonElement metadata,
            JsonSerializerOptions options)
        {
            if (metadata.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    $"The '{MetadataPropertyName}' property must be a JSON object.");
            }

            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                metadata.GetRawText(),
                options)!;
        }

        private static ExecuteToolJsonProperty? FindDeclaredProperty(string jsonName)
        {
            foreach (var declaredProperty in DeclaredProperties)
            {
                if (string.Equals(declaredProperty.JsonName, jsonName, StringComparison.Ordinal))
                {
                    return declaredProperty;
                }
            }

            return null;
        }

        private static ExecuteToolJsonProperty[] CreateDeclaredProperties()
        {
            var modelProperties = typeof(TModel).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var declaredProperties = new List<ExecuteToolJsonProperty>(modelProperties.Length);

            foreach (var property in modelProperties)
            {
                if (property == AdditionalPropertiesProperty ||
                    property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                {
                    continue;
                }

                var jsonName =
                    property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                    property.Name;
                declaredProperties.Add(new ExecuteToolJsonProperty(jsonName, property));
            }

            return declaredProperties.ToArray();
        }
    }

    internal sealed class ExecuteToolJsonProperty
    {
        internal ExecuteToolJsonProperty(string jsonName, PropertyInfo property)
        {
            this.JsonName = jsonName;
            this.Property = property;
        }

        internal string JsonName { get; }

        internal PropertyInfo Property { get; }
    }
}
