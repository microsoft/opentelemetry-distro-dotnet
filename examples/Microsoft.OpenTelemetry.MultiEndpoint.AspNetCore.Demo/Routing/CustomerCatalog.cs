// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace MultiEndpointDemo.Routing;

/// <summary>
/// The server-owned mapping from customer to destination. This is the trust boundary: a destination
/// is only ever chosen from here, never from anything the caller supplied.
/// </summary>
public sealed class CustomerCatalog
{
    private readonly Dictionary<string, string> _customerIdByApiKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoutingDestination> _destinationByCustomerId = new(StringComparer.OrdinalIgnoreCase);

    public CustomerCatalog(IOptions<DemoOptions> options, ILogger<CustomerCatalog> logger)
    {
        foreach (var customer in options.Value.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Id) || string.IsNullOrWhiteSpace(customer.ApiKey))
            {
                continue;
            }

            var destination = RoutingDestination.FromConnectionString(
                customer.ConnectionString,
                customer.CloudRole ?? customer.Id);

            if (destination is null)
            {
                // Loud at startup beats telemetry vanishing at runtime.
                throw new InvalidOperationException(
                    $"Customer '{customer.Id}' has no connection string with an explicit IngestionEndpoint.");
            }

            _customerIdByApiKey[customer.ApiKey] = customer.Id;
            _destinationByCustomerId[customer.Id] = destination;
        }

        logger.LogInformation("Loaded {CustomerCount} routed customers.", _destinationByCustomerId.Count);
    }

    public int Count => _destinationByCustomerId.Count;

    public IEnumerable<string> CustomerIds => _destinationByCustomerId.Keys;

    /// <summary>
    /// Stands in for authentication. A real service would authenticate the caller and map the
    /// resulting principal to a customer; only the lookup that follows would stay the same.
    /// </summary>
    public string? AuthorizeCustomer(string? apiKey) =>
        !string.IsNullOrEmpty(apiKey) && _customerIdByApiKey.TryGetValue(apiKey, out var customerId)
            ? customerId
            : null;

    public RoutingDestination? Resolve(string customerId) =>
        _destinationByCustomerId.TryGetValue(customerId, out var destination) ? destination : null;
}
