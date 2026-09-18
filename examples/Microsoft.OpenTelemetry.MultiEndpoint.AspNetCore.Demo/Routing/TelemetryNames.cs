// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace MultiEndpointDemo.Routing;

public static class TelemetryNames
{
    public const string ActivitySource = "Contoso.Orders";
    public const string Meter = "Contoso.Orders";

    /// <summary>Opaque customer identifier. Never the connection string or the routing values.</summary>
    public const string CustomerIdTag = "contoso.customer_id";

    public const string InstrumentationKey = "microsoft.instrumentation_key";
    public const string IngestionEndpoint = "microsoft.ingestion_endpoint";
    public const string CloudRole = "microsoft.multi_endpoint_cloud_role";

    public static readonly string[] RoutingKeys = [InstrumentationKey, IngestionEndpoint, CloudRole];
}
