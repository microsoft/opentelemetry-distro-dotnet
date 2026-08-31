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

        public static string Serialize(IDictionary<string, object?> payload)
        {
            if (payload == null)
            {
                return "{}";
            }

            var active = new HashSet<object>(ReferenceComparer.Instance);
            var normalized = NormalizeDictionary(payload, active, 0);
            return JsonSerializer.Serialize(normalized);
        }

        private static Dictionary<string, object?> NormalizeDictionary(
            IDictionary<string, object?> source,
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

        internal static IDictionary<string, object?> ToNullableDictionary(IDictionary<string, object> payload)
        {
            var converted = new Dictionary<string, object?>(payload.Count);
            foreach (var pair in payload)
            {
                converted[pair.Key] = pair.Value;
            }

            return converted;
        }

        private static object? NormalizeValue(object? value, HashSet<object> active, int depth)
        {
            if (value == null || value is string || value is bool ||
                value is byte || value is sbyte || value is short ||
                value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float ||
                value is double || value is decimal)
            {
                return value;
            }

            if (depth >= MaximumDepth || active.Contains(value))
            {
                return GetFallback(value);
            }

            if (value is IDictionary<string, object?> dictionary)
            {
                return NormalizeDictionary(dictionary, active, depth);
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

            try
            {
                using var document = JsonDocument.Parse(
                    JsonSerializer.Serialize(value, value.GetType()));
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
