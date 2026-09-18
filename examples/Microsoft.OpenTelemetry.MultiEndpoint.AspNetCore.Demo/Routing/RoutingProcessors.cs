// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// Stamps the destination on every activity, including those created by instrumentation libraries.
/// </summary>
public sealed class RoutingActivityProcessor(ICustomerRouting routing) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        var destination = routing.ForActivity(activity);

        // SetTag replaces an existing value, and a null value removes the tag, so an activity with no
        // resolved customer cannot keep a stale destination and be routed somewhere unintended.
        activity.SetTag(TelemetryNames.InstrumentationKey, destination?.InstrumentationKey);
        activity.SetTag(TelemetryNames.IngestionEndpoint, destination?.IngestionEndpoint);
        activity.SetTag(TelemetryNames.CloudRole, destination?.CloudRole);
    }
}

/// <summary>
/// Stamps the destination on every log record, so ordinary ILogger calls need no changes.
/// </summary>
public sealed class RoutingLogProcessor(ICustomerRouting routing) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        // Routing reads the first occurrence of each key, so appending would lose to a pre-existing
        // value. Rebuild the list without the routing keys, then add the trusted ones.
        var attributes = (record.Attributes ?? [])
            .Where(attribute => !TelemetryNames.RoutingKeys.Contains(attribute.Key))
            .ToList();

        var destination = routing.ForLogRecord(record);

        if (destination is not null)
        {
            attributes.Add(new(TelemetryNames.InstrumentationKey, destination.InstrumentationKey));
            attributes.Add(new(TelemetryNames.IngestionEndpoint, destination.IngestionEndpoint));

            if (destination.CloudRole is not null)
            {
                attributes.Add(new(TelemetryNames.CloudRole, destination.CloudRole));
            }
        }

        record.Attributes = attributes;
    }
}
