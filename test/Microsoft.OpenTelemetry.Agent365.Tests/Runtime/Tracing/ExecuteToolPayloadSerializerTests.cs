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

    [TestMethod]
    public void Serialize_ReplacesNonFiniteFloatingPointValuesWithFallbackStrings()
    {
        IDictionary<string, object> payload = new Dictionary<string, object>
        {
            ["float_nan"] = float.NaN,
            ["float_positive_infinity"] = float.PositiveInfinity,
            ["double_negative_infinity"] = double.NegativeInfinity,
            ["items"] = new object?[] { 12.5d, double.PositiveInfinity, float.NegativeInfinity },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));
        var root = document.RootElement;
        var items = root.GetProperty("items");

        root.GetProperty("float_nan").GetString().Should().Be(float.NaN.ToString());
        root.GetProperty("float_positive_infinity").GetString().Should().Be(float.PositiveInfinity.ToString());
        root.GetProperty("double_negative_infinity").GetString().Should().Be(double.NegativeInfinity.ToString());
        items[0].GetDouble().Should().Be(12.5d);
        items[1].GetString().Should().Be(double.PositiveInfinity.ToString());
        items[2].GetString().Should().Be(float.NegativeInfinity.ToString());
    }

    [TestMethod]
    public void Serialize_PreservesLegacyDictionaryContractForEnumsAndByteArrays()
    {
        IDictionary<string, object> payload = new Dictionary<string, object>
        {
            ["action"] = ToolCallAction.Read,
            ["bytes"] = new byte[] { 0, 1, 2, 3 },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));
        var root = document.RootElement;

        root.GetProperty("action").GetString().Should().Be("read");
        root.GetProperty("bytes").GetString().Should().Be("AAECAw==");
    }

    [TestMethod]
    public void Serialize_PreservesNestedTypedStringKeyDictionariesAsJsonObjects()
    {
        IDictionary<string, object> payload = new Dictionary<string, object>
        {
            ["parameters"] = new Dictionary<string, int>
            {
                ["maxResults"] = 5,
                ["offset"] = 2,
            },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));
        var parameters = document.RootElement.GetProperty("parameters");

        parameters.ValueKind.Should().Be(JsonValueKind.Object);
        parameters.GetProperty("maxResults").GetInt32().Should().Be(5);
        parameters.GetProperty("offset").GetInt32().Should().Be(2);
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
