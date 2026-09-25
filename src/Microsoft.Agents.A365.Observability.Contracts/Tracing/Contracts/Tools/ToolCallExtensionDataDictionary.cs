// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    internal sealed class ToolCallExtensionDataDictionary<TModel> : IDictionary<string, object?>
    {
        private static readonly HashSet<string> DeclaredJsonPropertyNames = CreateDeclaredJsonPropertyNames();
        private readonly IDictionary<string, object?> inner;

        private ToolCallExtensionDataDictionary(IDictionary<string, object?> inner)
        {
            this.inner = inner;
        }

        public object? this[string key]
        {
            get => this.inner[key];
            set => this.inner[key] = value;
        }

        public ICollection<string> Keys => this.inner.Keys;

        public ICollection<object?> Values => this.inner.Values;

        public int Count => this.inner.Count;

        public bool IsReadOnly => this.inner.IsReadOnly;

        internal static IDictionary<string, object?> Create() =>
            new ToolCallExtensionDataDictionary<TModel>(new Dictionary<string, object?>());

        internal static IDictionary<string, object?> Wrap(IDictionary<string, object?> value) =>
            value is ToolCallExtensionDataDictionary<TModel>
                ? value
                : new ToolCallExtensionDataDictionary<TModel>(
                    value ?? throw new ArgumentNullException(nameof(value)));

        public void Add(string key, object? value) => this.inner.Add(key, value);

        public void Add(KeyValuePair<string, object?> item) => this.inner.Add(item);

        public void Clear() => this.inner.Clear();

        public bool Contains(KeyValuePair<string, object?> item) => this.inner.Contains(item);

        public bool ContainsKey(string key) => this.inner.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
            this.inner.CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            Validate();
            return this.inner.GetEnumerator();
        }

        public bool Remove(string key) => this.inner.Remove(key);

        public bool Remove(KeyValuePair<string, object?> item) => this.inner.Remove(item);

        public bool TryGetValue(string key, out object? value) => this.inner.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static HashSet<string> CreateDeclaredJsonPropertyNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in typeof(TModel).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetCustomAttribute<JsonExtensionDataAttribute>() != null)
                {
                    continue;
                }

                names.Add(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name);
            }

            return names;
        }

        private void Validate()
        {
            foreach (var propertyName in this.inner.Keys)
            {
                if (DeclaredJsonPropertyNames.Contains(propertyName))
                {
                    throw new JsonException(
                        $"Extension data property '{propertyName}' conflicts with a declared JSON property.");
                }
            }
        }
    }
}
