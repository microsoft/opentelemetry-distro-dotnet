// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    internal static class ToolCallDictionaryAccessor
    {
        internal static T? GetReference<T>(IDictionary<string, object?> values, string key)
            where T : class
        {
            return values.TryGetValue(key, out var value) ? value as T : null;
        }

        internal static T? GetValue<T>(IDictionary<string, object?> values, string key)
            where T : struct
        {
            return values.TryGetValue(key, out var value) && value is T typed
                ? typed
                : null;
        }

        internal static TEnum? GetEnum<TEnum>(IDictionary<string, object?> values, string key)
            where TEnum : struct
        {
            if (!values.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is TEnum typed)
            {
                return typed;
            }

            return value is string text &&
                Enum.TryParse<TEnum>(text, true, out var parsed) &&
                Enum.IsDefined(typeof(TEnum), parsed)
                    ? parsed
                    : null;
        }

        internal static void SetReference<T>(IDictionary<string, object?> values, string key, T? value)
            where T : class
        {
            if (value == null)
            {
                values.Remove(key);
                return;
            }

            values[key] = value;
        }

        internal static void SetValue<T>(IDictionary<string, object?> values, string key, T? value)
            where T : struct
        {
            if (!value.HasValue)
            {
                values.Remove(key);
                return;
            }

            values[key] = value.Value;
        }

        internal static void SetEnum<TEnum>(IDictionary<string, object?> values, string key, TEnum? value)
            where TEnum : struct
        {
            if (!value.HasValue)
            {
                values.Remove(key);
                return;
            }

            values[key] = value.Value.ToString()!.ToLower(CultureInfo.InvariantCulture);
        }
    }
}
