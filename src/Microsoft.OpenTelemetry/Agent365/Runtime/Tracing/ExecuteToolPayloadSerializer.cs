// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing
{
    internal static class ExecuteToolPayloadSerializer
    {
        private const int MaximumDepth = 64;

        public static string Serialize<TValue>(IDictionary<string, TValue>? payload)
        {
            if (payload == null)
            {
                return "{}";
            }

            var active = new HashSet<object>(ReferenceComparer.Instance);
            var normalized = NormalizeDictionary(payload, active, 0);
            return JsonSerializer.Serialize(normalized, MessageUtils.SerializerOptions);
        }

        private static Dictionary<string, object?> NormalizeDictionary<TValue>(
            IDictionary<string, TValue> source,
            HashSet<object> active,
            int depth)
        {
            if (depth >= MaximumDepth || !active.Add(source))
            {
                return new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(source),
                };
            }

            try
            {
                var result = new Dictionary<string, object?>();
                foreach (var pair in source)
                {
                    result[pair.Key] = NormalizeValue(pair.Value, active, depth + 1);
                }

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(source),
                };
            }
            finally
            {
                active.Remove(source);
            }
        }

        private static object? NormalizeValue(object? value, HashSet<object> active, int depth)
        {
            if (value == null || value is string || value is bool ||
                value is byte || value is sbyte || value is short ||
                value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is decimal)
            {
                return value;
            }

            if (value is float single)
            {
                return !float.IsNaN(single) && !float.IsInfinity(single) ? value : GetFallback(single);
            }

            if (value is double number)
            {
                return !double.IsNaN(number) && !double.IsInfinity(number) ? value : GetFallback(number);
            }

            if (depth >= MaximumDepth || active.Contains(value))
            {
                return GetFallback(value);
            }

            if (value is IDictionary<string, object?> dictionary)
            {
                return NormalizeDictionary(dictionary, active, depth);
            }

            if (value is IDictionary<string, object> nonNullableDictionary)
            {
                return NormalizeDictionary(nonNullableDictionary, active, depth);
            }

            if (TryNormalizeStringKeyDictionary(value, active, depth, out var normalizedDictionary))
            {
                return normalizedDictionary;
            }

            if (value is byte[] || value.GetType().IsEnum)
            {
                return NormalizeSerializableValue(value);
            }

            if (value is IEnumerable enumerable)
            {
                if (!active.Add(value))
                {
                    return GetFallback(value);
                }

                try
                {
                    var items = new List<object?>();
                    foreach (var item in enumerable)
                    {
                        items.Add(NormalizeValue(item, active, depth + 1));
                    }

                    return items;
                }
                catch (Exception)
                {
                    return GetFallback(value);
                }
                finally
                {
                    active.Remove(value);
                }
            }

            return NormalizeSerializableValue(value);
        }

        private static bool TryNormalizeStringKeyDictionary(
            object value,
            HashSet<object> active,
            int depth,
            out Dictionary<string, object?>? normalized)
        {
            if (!ImplementsStringKeyDictionary(value.GetType()))
            {
                normalized = null;
                return false;
            }

            if (!active.Add(value))
            {
                normalized = new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(value),
                };
                return true;
            }

            try
            {
                var result = new Dictionary<string, object?>();
                foreach (var item in (IEnumerable)value)
                {
                    if (!TryGetDictionaryEntry(item, out var key, out var itemValue))
                    {
                        normalized = new Dictionary<string, object?>
                        {
                            ["value"] = GetFallback(value),
                        };
                        return true;
                    }

                    result[key] = NormalizeValue(itemValue, active, depth + 1);
                }

                normalized = result;
                return true;
            }
            catch (Exception)
            {
                normalized = new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(value),
                };
                return true;
            }
            finally
            {
                active.Remove(value);
            }
        }

        private static bool ImplementsStringKeyDictionary(Type type)
        {
            foreach (var candidate in type.GetInterfaces())
            {
                if (candidate.IsGenericType &&
                    (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                        candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
                {
                    var arguments = candidate.GetGenericArguments();
                    if (arguments[0] == typeof(string))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetDictionaryEntry(object? item, out string key, out object? value)
        {
            if (item is DictionaryEntry entry && entry.Key is string dictionaryEntryKey)
            {
                key = dictionaryEntryKey;
                value = entry.Value;
                return true;
            }

            if (item == null)
            {
                key = string.Empty;
                value = null;
                return false;
            }

            var itemType = item.GetType();
            var keyProperty = itemType.GetProperty("Key");
            var valueProperty = itemType.GetProperty("Value");
            if (keyProperty?.GetValue(item) is string propertyKey && valueProperty != null)
            {
                key = propertyKey;
                value = valueProperty.GetValue(item);
                return true;
            }

            key = string.Empty;
            value = null;
            return false;
        }

        private static object? NormalizeSerializableValue(object value)
        {
            try
            {
                using var document = JsonDocument.Parse(
                    JsonSerializer.Serialize(value, value.GetType(), MessageUtils.SerializerOptions));
                return document.RootElement.Clone();
            }
            catch (Exception)
            {
                return GetFallback(value);
            }
        }

        private static string GetFallback(object value)
        {
            try
            {
                return value.ToString() ?? value.GetType().FullName ?? value.GetType().Name;
            }
            catch (Exception)
            {
                return value.GetType().FullName ?? value.GetType().Name;
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
