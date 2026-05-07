// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Diagnostics;
using System.Text.Json;

namespace A365.OpenTelemetry.Exporter;

/// <summary>Serializes a collection of <see cref="Activity"/> instances to OTLP JSON (ExportTraceServiceRequest).</summary>
internal static class OtlpJsonSerializer
{
    /// <summary>Serialize activities into an OTLP ExportTraceServiceRequest JSON string.</summary>
    internal static string Serialize(IEnumerable<Activity> activities)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteStartArray("resourceSpans");

        // Group by source (ActivitySource) to produce one ResourceSpans per source.
        var bySource = activities.GroupBy(a => a.Source.Name);

        foreach (var sourceGroup in bySource)
        {
            writer.WriteStartObject();

            // resource
            WriteResource(writer, sourceGroup.Key, sourceGroup.First());

            // scopeSpans - one scope per ActivitySource
            writer.WriteStartArray("scopeSpans");
            writer.WriteStartObject();

            WriteScope(writer, sourceGroup.Key, sourceGroup.First().Source.Version);

            writer.WriteStartArray("spans");
            foreach (var activity in sourceGroup)
            {
                WriteSpan(writer, activity);
            }
            writer.WriteEndArray(); // spans

            writer.WriteEndObject(); // scopeSpans[0]
            writer.WriteEndArray();  // scopeSpans

            writer.WriteEndObject(); // resourceSpans[i]
        }

        writer.WriteEndArray();  // resourceSpans
        writer.WriteEndObject(); // root

        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteResource(Utf8JsonWriter writer, string serviceName, Activity sample)
    {
        writer.WriteStartObject("resource");
        writer.WriteStartArray("attributes");

        WriteStringAttribute(writer, "service.name", serviceName);

        // Include resource-level tags from the first activity if available.
        if (sample.GetTagItem("service.version") is string version)
        {
            WriteStringAttribute(writer, "service.version", version);
        }

        writer.WriteEndArray(); // attributes
        writer.WriteEndObject(); // resource
    }

    private static void WriteScope(Utf8JsonWriter writer, string name, string? version)
    {
        writer.WriteStartObject("scope");
        writer.WriteString("name", name);
        if (version is not null)
        {
            writer.WriteString("version", version);
        }
        writer.WriteEndObject();
    }

    private static void WriteSpan(Utf8JsonWriter writer, Activity activity)
    {
        writer.WriteStartObject();

        writer.WriteString("traceId", activity.TraceId.ToHexString());
        writer.WriteString("spanId", activity.SpanId.ToHexString());

        if (activity.ParentSpanId != default)
        {
            writer.WriteString("parentSpanId", activity.ParentSpanId.ToHexString());
        }

        writer.WriteString("name", activity.DisplayName);
        writer.WriteNumber("kind", MapSpanKind(activity.Kind));

        // Timestamps as string nanoseconds (OTLP convention for JSON encoding).
        writer.WriteString("startTimeUnixNano", ToNanoString(activity.StartTimeUtc));
        var endTimeUtc = activity.StartTimeUtc + activity.Duration;
        writer.WriteString("endTimeUnixNano", ToNanoString(endTimeUtc));

        // Attributes
        WriteAttributes(writer, activity);

        // Status
        WriteStatus(writer, activity);

        // Events
        WriteEvents(writer, activity);

        // Links
        WriteLinks(writer, activity);

        writer.WriteEndObject();
    }

    private static void WriteAttributes(Utf8JsonWriter writer, Activity activity)
    {
        writer.WriteStartArray("attributes");

        foreach (var tag in activity.TagObjects)
        {
            if (tag.Value is null)
            {
                continue;
            }

            WriteAttribute(writer, tag.Key, tag.Value);
        }

        writer.WriteEndArray();
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string key, object value)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteStartObject("value");

        switch (value)
        {
            case string s:
                writer.WriteString("stringValue", s);
                break;
            case bool b:
                writer.WriteBoolean("boolValue", b);
                break;
            case int i:
                writer.WriteString("intValue", i.ToString());
                break;
            case long l:
                writer.WriteString("intValue", l.ToString());
                break;
            case double d:
                writer.WriteNumber("doubleValue", d);
                break;
            case float f:
                writer.WriteNumber("doubleValue", f);
                break;
            case string[] arr:
                writer.WriteStartObject("arrayValue");
                writer.WriteStartArray("values");
                foreach (var item in arr)
                {
                    writer.WriteStartObject();
                    writer.WriteString("stringValue", item);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            default:
                writer.WriteString("stringValue", value.ToString());
                break;
        }

        writer.WriteEndObject(); // value
        writer.WriteEndObject(); // attribute
    }

    private static void WriteStringAttribute(Utf8JsonWriter writer, string key, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteStartObject("value");
        writer.WriteString("stringValue", value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteStatus(Utf8JsonWriter writer, Activity activity)
    {
        writer.WriteStartObject("status");

        var statusCode = activity.Status switch
        {
            ActivityStatusCode.Unset => 0,
            ActivityStatusCode.Ok => 1,
            ActivityStatusCode.Error => 2,
            _ => 0
        };

        writer.WriteNumber("code", statusCode);

        if (activity.StatusDescription is not null)
        {
            writer.WriteString("message", activity.StatusDescription);
        }

        writer.WriteEndObject();
    }

    private static void WriteEvents(Utf8JsonWriter writer, Activity activity)
    {
        writer.WriteStartArray("events");

        foreach (var evt in activity.Events)
        {
            writer.WriteStartObject();

            writer.WriteString("timeUnixNano", ToNanoString(evt.Timestamp.UtcDateTime));
            writer.WriteString("name", evt.Name);

            writer.WriteStartArray("attributes");
            foreach (var tag in evt.Tags)
            {
                if (tag.Value is not null)
                {
                    WriteAttribute(writer, tag.Key, tag.Value);
                }
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLinks(Utf8JsonWriter writer, Activity activity)
    {
        writer.WriteStartArray("links");

        foreach (var link in activity.Links)
        {
            writer.WriteStartObject();

            writer.WriteString("traceId", link.Context.TraceId.ToHexString());
            writer.WriteString("spanId", link.Context.SpanId.ToHexString());

            writer.WriteStartArray("attributes");
            if (link.Tags is not null)
            {
                foreach (var tag in link.Tags)
                {
                    if (tag.Value is not null)
                    {
                        WriteAttribute(writer, tag.Key, tag.Value);
                    }
                }
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static int MapSpanKind(ActivityKind kind) => kind switch
    {
        ActivityKind.Internal => 1,
        ActivityKind.Server => 2,
        ActivityKind.Client => 3,
        ActivityKind.Producer => 4,
        ActivityKind.Consumer => 5,
        _ => 0
    };

    private static string ToNanoString(DateTime utc)
    {
        // Ticks since Unix epoch, multiplied by 100 to get nanoseconds.
        const long TicksPerNanosecond = 100;
        var unixTicks = utc.Ticks - DateTime.UnixEpoch.Ticks;
        var nanos = (ulong)unixTicks * TicksPerNanosecond;
        return nanos.ToString();
    }
}
