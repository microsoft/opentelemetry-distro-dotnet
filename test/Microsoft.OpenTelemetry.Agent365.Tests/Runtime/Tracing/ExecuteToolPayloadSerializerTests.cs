// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing;

using System.Collections;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

[TestClass]
public sealed class ExecuteToolPayloadSerializerTests
{
    [TestMethod]
    public void Serialize_ProducesExpectedArgumentsJson()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?>
            {
                ["format"] = "text",
                ["includeMetadata"] = true,
            },
            Resources = new List<ToolCallResource>
            {
                new()
                {
                    Id = "canonical-resource-id",
                    Uri = "https://example.com/resource",
                    Name = "resource-name",
                    Type = "document",
                    Provider = "provider-name",
                    Identifiers = new List<ToolCallIdentifier>
                    {
                        new()
                        {
                            Type = "provider.identifier_type",
                            Value = "provider-specific-id",
                        },
                    },
                    Container = new ToolCallContainer
                    {
                        Id = "container-id",
                        Uri = "https://example.com/container",
                        Type = "site",
                    },
                },
            },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(arguments));
        var root = document.RootElement;

        root.GetProperty("schema_version").GetString().Should().Be("1.0");
        root.GetProperty("action").GetString().Should().Be("read");
        root.GetProperty("parameters").GetProperty("includeMetadata").GetBoolean().Should().BeTrue();
        root.GetProperty("resources")[0].GetProperty("identifiers")[0]
            .GetProperty("value").GetString().Should().Be("provider-specific-id");
    }

    [TestMethod]
    public void Serialize_ProducesExpectedResultJson()
    {
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
                Code = null,
                ProviderCode = null,
                Message = null,
            },
            Resources = new List<ToolCallResultResource>
            {
                new()
                {
                    Id = "canonical-resource-id",
                    Uri = "https://example.com/resource",
                    Name = "resource-name",
                    Type = "document",
                    Provider = "provider-name",
                    Identifiers = new List<ToolCallIdentifier>
                    {
                        new()
                        {
                            Type = "provider.identifier_type",
                            Value = "provider-specific-id",
                        },
                    },
                    Container = new ToolCallContainer
                    {
                        Id = "container-id",
                        Uri = "https://example.com/container",
                        Type = "site",
                    },
                    Outcome = new ToolCallResultOutcome
                    {
                        Status = ToolCallOutcomeStatus.Success,
                    },
                    Sensitivity = new ToolCallResultSensitivity
                    {
                        LabelId = "label-id",
                    },
                    Policy = new ToolCallResultPolicy
                    {
                        Decision = ToolPolicyDecision.Allow,
                        Id = "policy-id",
                        Name = "policy-name",
                    },
                    Security = new ToolCallResultSecurity
                    {
                        XpiaDetected = false,
                    },
                    Data = new Dictionary<string, object?>(),
                },
            },
            Data = new Dictionary<string, object?>(),
            Pagination = new ToolCallResultPagination
            {
                HasMore = false,
                NextCursor = null,
                TotalCount = 1,
            },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(result));
        var resource = document.RootElement.GetProperty("resources")[0];

        document.RootElement.GetProperty("outcome")
            .GetProperty("status").GetString().Should().Be("success");
        resource.GetProperty("sensitivity")
            .GetProperty("label_id").GetString().Should().Be("label-id");
        resource.GetProperty("policy")
            .GetProperty("decision").GetString().Should().Be("allow");
        resource.GetProperty("security")
            .GetProperty("xpia_detected").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("pagination")
            .GetProperty("total_count").GetInt64().Should().Be(1);
    }

    [TestMethod]
    public void Serialize_ReplacesOnlyFailingDictionaryValue()
    {
        var payload = new ExecuteToolCallResult
        {
            ["good"] = 42,
            ["bad"] = new ThrowingEnumerable(),
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));

        document.RootElement.GetProperty("good").GetInt32().Should().Be(42);
        document.RootElement.GetProperty("bad").GetString().Should().Be(nameof(ThrowingEnumerable));
    }

    [TestMethod]
    public void Serialize_ReplacesCycleWithoutThrowing()
    {
        var payload = new ExecuteToolCallResult();
        payload["self"] = payload;

        var action = () => ExecuteToolPayloadSerializer.Serialize(payload);

        action.Should().NotThrow();
        using var document = JsonDocument.Parse(action());
        document.RootElement.GetProperty("self").GetString()
            .Should().Contain(nameof(ExecuteToolCallResult));
    }

    [TestMethod]
    public void Serialize_ReplacesOnlyFailingCollectionElement()
    {
        var cyclic = new SelfReferencingValue();
        cyclic.Self = cyclic;
        var payload = new ExecuteToolCallResult
        {
            ["items"] = new object?[] { "first", cyclic, "last" },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));
        var items = document.RootElement.GetProperty("items");

        items[0].GetString().Should().Be("first");
        items[1].GetString().Should().Contain(nameof(SelfReferencingValue));
        items[2].GetString().Should().Be("last");
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("test");
        public override string ToString() => nameof(ThrowingEnumerable);
    }

    private sealed class SelfReferencingValue
    {
        public SelfReferencingValue? Self { get; set; }
        public override string ToString() => nameof(SelfReferencingValue);
    }
}
