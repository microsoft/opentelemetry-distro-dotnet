// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Diagnostics;

namespace A365.OpenTelemetry.Exporter;

/// <summary>Fluent builder that sets A365 baggage and tags on the current Activity.</summary>
public sealed class BaggageBuilder : IDisposable
{
    /// <summary>Baggage key for the tenant ID.</summary>
    public const string BaggageTenantId = "tenant_id";

    /// <summary>Baggage key for the agent ID.</summary>
    public const string BaggageAgentId = "agent_id";

    /// <summary>Baggage key for the conversation ID.</summary>
    public const string BaggageConversationId = "conversation_id";

    /// <summary>Span attribute key for the tenant ID.</summary>
    public const string AttrTenantId = "a365.tenant_id";

    /// <summary>Span attribute key for the agent ID.</summary>
    public const string AttrAgentId = "a365.agent_id";

    /// <summary>Span attribute key for the conversation ID.</summary>
    public const string AttrConversationId = "a365.conversation_id";

    private string? _tenantId;
    private string? _agentId;
    private string? _conversationId;
    private Activity? _activity;
    private string? _previousTenantBaggage;
    private string? _previousAgentBaggage;
    private string? _previousConversationBaggage;
    private bool _built;

    /// <summary>Set the tenant ID.</summary>
    public BaggageBuilder TenantId(string value)
    {
        _tenantId = value;
        return this;
    }

    /// <summary>Set the agent ID.</summary>
    public BaggageBuilder AgentId(string value)
    {
        _agentId = value;
        return this;
    }

    /// <summary>Set the conversation ID (optional).</summary>
    public BaggageBuilder ConversationId(string value)
    {
        _conversationId = value;
        return this;
    }

    /// <summary>Apply baggage and tags to the current Activity. Returns this instance for disposal.</summary>
    public BaggageBuilder Build()
    {
        _activity = Activity.Current;
        if (_activity is null)
        {
            _built = true;
            return this;
        }

        // Save previous baggage values for restoration on Dispose.
        _previousTenantBaggage = _activity.GetBaggageItem(BaggageTenantId);
        _previousAgentBaggage = _activity.GetBaggageItem(BaggageAgentId);
        _previousConversationBaggage = _activity.GetBaggageItem(BaggageConversationId);

        if (_tenantId is not null)
        {
            _activity.SetBaggage(BaggageTenantId, _tenantId);
            _activity.SetTag(AttrTenantId, _tenantId);
        }

        if (_agentId is not null)
        {
            _activity.SetBaggage(BaggageAgentId, _agentId);
            _activity.SetTag(AttrAgentId, _agentId);
        }

        if (_conversationId is not null)
        {
            _activity.SetBaggage(BaggageConversationId, _conversationId);
            _activity.SetTag(AttrConversationId, _conversationId);
        }

        _built = true;
        return this;
    }

    /// <summary>Restore previous baggage values.</summary>
    public void Dispose()
    {
        if (!_built || _activity is null)
        {
            return;
        }

        // Restore previous baggage. SetBaggage with null removes the entry.
        _activity.SetBaggage(BaggageTenantId, _previousTenantBaggage);
        _activity.SetBaggage(BaggageAgentId, _previousAgentBaggage);
        _activity.SetBaggage(BaggageConversationId, _previousConversationBaggage);
    }

    /// <summary>Set A365 routing attributes directly on an Activity without baggage.</summary>
    public static void SetA365Attributes(Activity activity, string tenantId, string agentId)
    {
        activity.SetTag(AttrTenantId, tenantId);
        activity.SetTag(AttrAgentId, agentId);
        activity.SetTag(BaggageTenantId, tenantId);
        activity.SetTag(BaggageAgentId, agentId);
    }
}
