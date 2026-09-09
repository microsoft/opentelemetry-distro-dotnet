using Microsoft.Extensions.Configuration;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class SampleOptions
{
    internal SampleOptions(
        Uri authority,
        string clientId,
        string clientSecret,
        string tenantId,
        string agentAppInstanceId,
        string agentId,
        string clusterCategory)
    {
        this.Authority = authority;
        this.ClientId = clientId;
        this.ClientSecret = clientSecret;
        this.TenantId = tenantId;
        this.AgentAppInstanceId = agentAppInstanceId;
        this.AgentId = agentId;
        this.ClusterCategory = clusterCategory;
    }

    internal Uri Authority { get; }

    internal string ClientId { get; }

    internal string ClientSecret { get; }

    internal string TenantId { get; }

    internal string AgentAppInstanceId { get; }

    internal string AgentId { get; }

    internal string ClusterCategory { get; }

    internal static SampleOptions Load(IConfiguration configuration)
    {
        var authorityText = Required(
            configuration,
            "Connections:ServiceConnection:Settings:AuthorityEndpoint");

        if (!Uri.TryCreate(authorityText, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps
            || authority.AbsolutePath != "/"
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment))
        {
            throw new InvalidOperationException(
                "Configuration key 'Connections:ServiceConnection:Settings:AuthorityEndpoint' must be an absolute HTTPS authority root without a tenant path, query, or fragment.");
        }

        var clientId = Required(
            configuration,
            "Connections:ServiceConnection:Settings:ClientId");
        var clientSecret = Required(
            configuration,
            "Connections:ServiceConnection:Settings:ClientSecret");
        var tenantId = RequiredGuid(
            configuration,
            "Connections:ServiceConnection:Settings:TenantId");
        var instanceId = RequiredGuid(
            configuration,
            "Agent365:AgentAppInstanceId");

        var configuredAgentId = configuration["Agent365:AgentId"]?.Trim();
        var agentId = string.IsNullOrWhiteSpace(configuredAgentId)
            ? instanceId
            : RejectPlaceholder("Agent365:AgentId", configuredAgentId);

        var clusterCategory = configuration["Agent365:ClusterCategory"]?.Trim();
        if (string.IsNullOrWhiteSpace(clusterCategory))
        {
            clusterCategory = "production";
        }

        return new SampleOptions(
            authority,
            clientId,
            clientSecret,
            tenantId,
            instanceId,
            agentId,
            clusterCategory);
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' is required in appsettings.json.");
        }

        return RejectPlaceholder(key, value);
    }

    private static string RequiredGuid(IConfiguration configuration, string key)
    {
        var value = Required(configuration, key);
        if (!Guid.TryParse(value, out _))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be a GUID.");
        }

        return value;
    }

    private static string RejectPlaceholder(string key, string value)
    {
        if (value.StartsWith('<') && value.EndsWith('>'))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' still contains the example placeholder '{value}'.");
        }

        return value;
    }

    public override string ToString() =>
        $"{nameof(SampleOptions)} {{ {nameof(this.Authority)} = {this.Authority}, {nameof(this.ClientId)} = {this.ClientId}, {nameof(this.ClientSecret)} = [REDACTED], {nameof(this.TenantId)} = {this.TenantId}, {nameof(this.AgentAppInstanceId)} = {this.AgentAppInstanceId}, {nameof(this.AgentId)} = {this.AgentId}, {nameof(this.ClusterCategory)} = {this.ClusterCategory} }}";
}
