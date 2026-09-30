// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing.Contracts;

[TestClass]
public sealed class ExecuteToolJsonModelsTests
{
    [TestMethod]
    public void Arguments_DefaultsSchemaVersionAndStoresStandardProperties()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?> { ["format"] = "text" },
            Resources = new List<ToolCallResource>(),
        };

        arguments.SchemaVersion.Should().Be("1.0");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.Parameters!["format"].Should().Be("text");
    }

    [TestMethod]
    public void Result_DefaultsSchemaVersion()
    {
        new ExecuteToolCallResult().SchemaVersion.Should().Be("1.0");
    }

    [TestMethod]
    public void Arguments_AdditionalPropertiesSerializeAsNestedMetadata()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            AdditionalProperties =
            {
                ["provider_option"] = true,
                ["action"] = "provider-specific-action",
            },
        };

        using var document = JsonDocument.Parse(MessageUtils.SerializeToolPayload(arguments)!);
        var root = document.RootElement;

        root.GetProperty("schema_version").GetString().Should().Be("1.0");
        root.GetProperty("action").GetString().Should().Be("read");
        root.GetProperty("metadata").GetProperty("provider_option").GetBoolean().Should().BeTrue();
        root.GetProperty("metadata").GetProperty("action").GetString()
            .Should().Be("provider-specific-action");
        root.TryGetProperty("provider_option", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Result_AdditionalPropertiesSerializeAsNestedMetadata()
    {
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
                AdditionalProperties =
                {
                    ["provider_outcome"] = "accepted",
                },
            },
            AdditionalProperties =
            {
                ["provider_result"] = 42,
            },
        };

        using var document = JsonDocument.Parse(MessageUtils.SerializeToolPayload(result)!);
        var root = document.RootElement;

        root.GetProperty("metadata").GetProperty("provider_result").GetInt32().Should().Be(42);
        root.GetProperty("outcome").GetProperty("metadata").GetProperty("provider_outcome").GetString()
            .Should().Be("accepted");
        root.TryGetProperty("provider_result", out _).Should().BeFalse();
        root.GetProperty("outcome").TryGetProperty("provider_outcome", out _).Should().BeFalse();
    }

    [TestMethod]
    public void EveryNestedModelSerializesAdditionalPropertiesAsMetadata()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Resources =
            [
                new ToolCallResource
                {
                    AdditionalProperties = { ["resource_metadata"] = true },
                    Identifiers =
                    [
                        new ToolCallIdentifier
                        {
                            AdditionalProperties = { ["identifier_metadata"] = true },
                        },
                    ],
                    Container = new ToolCallContainer
                    {
                        AdditionalProperties = { ["container_metadata"] = true },
                    },
                },
            ],
            AdditionalProperties =
            {
                ["arguments_metadata"] = true,
            },
        };

        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                AdditionalProperties = { ["outcome_metadata"] = true },
            },
            Resources =
            [
                new ToolCallResultResource
                {
                    AdditionalProperties = { ["result_resource_metadata"] = true },
                    Sensitivity = new ToolCallResultSensitivity
                    {
                        AdditionalProperties = { ["sensitivity_metadata"] = true },
                    },
                    Policy = new ToolCallResultPolicy
                    {
                        AdditionalProperties = { ["policy_metadata"] = true },
                    },
                    Security = new ToolCallResultSecurity
                    {
                        AdditionalProperties = { ["security_metadata"] = true },
                    },
                },
            ],
            Pagination = new ToolCallResultPagination
            {
                AdditionalProperties = { ["pagination_metadata"] = true },
            },
            AdditionalProperties = { ["result_metadata"] = true },
        };

        using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var argumentsRoot = argumentsDocument.RootElement;
        argumentsRoot.GetProperty("metadata").GetProperty("arguments_metadata").GetBoolean().Should().BeTrue();
        var argumentResource = argumentsRoot.GetProperty("resources")[0];
        argumentResource.GetProperty("metadata").GetProperty("resource_metadata").GetBoolean().Should().BeTrue();
        argumentResource.GetProperty("identifiers")[0].GetProperty("metadata")
            .GetProperty("identifier_metadata").GetBoolean().Should().BeTrue();
        argumentResource.GetProperty("container").GetProperty("metadata")
            .GetProperty("container_metadata").GetBoolean().Should().BeTrue();

        using var resultDocument = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var resultRoot = resultDocument.RootElement;
        resultRoot.GetProperty("metadata").GetProperty("result_metadata").GetBoolean().Should().BeTrue();
        resultRoot.GetProperty("outcome").GetProperty("metadata")
            .GetProperty("outcome_metadata").GetBoolean().Should().BeTrue();
        var resultResource = resultRoot.GetProperty("resources")[0];
        resultResource.GetProperty("metadata").GetProperty("result_resource_metadata").GetBoolean().Should().BeTrue();
        resultResource.GetProperty("sensitivity").GetProperty("metadata")
            .GetProperty("sensitivity_metadata").GetBoolean().Should().BeTrue();
        resultResource.GetProperty("policy").GetProperty("metadata")
            .GetProperty("policy_metadata").GetBoolean().Should().BeTrue();
        resultResource.GetProperty("security").GetProperty("metadata")
            .GetProperty("security_metadata").GetBoolean().Should().BeTrue();
        resultRoot.GetProperty("pagination").GetProperty("metadata")
            .GetProperty("pagination_metadata").GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void DefaultJsonSerializer_UsesSchemaNamesAndStringEnums()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            AdditionalProperties =
            {
                ["provider_option"] = true,
            },
        };

        var json = JsonSerializer.Serialize(arguments);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("action").GetString().Should().Be("read");
        document.RootElement.GetProperty("metadata").GetProperty("provider_option").GetBoolean().Should().BeTrue();
        document.RootElement.TryGetProperty("provider_option", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("SchemaVersion", out _).Should().BeFalse();
    }

    [TestMethod]
    public void DefaultJsonSerializer_DeserializesSchemaNamesAndStringEnums()
    {
        var arguments = JsonSerializer.Deserialize<ExecuteToolCallArguments>(
            """{"schema_version":"2.0","action":"read","metadata":{"provider_option":true},"unknown":"ignored"}""");

        arguments.Should().NotBeNull();
        arguments!.SchemaVersion.Should().Be("2.0");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.AdditionalProperties.Should().ContainKey("provider_option");
        arguments.AdditionalProperties["provider_option"].Should().BeOfType<JsonElement>()
            .Which.GetBoolean().Should().BeTrue();
        arguments.AdditionalProperties.Should().NotContainKey("unknown");
        arguments.AdditionalProperties.Should().NotContainKey("schema_version");
        arguments.AdditionalProperties.Should().NotContainKey("action");
    }

    [TestMethod]
    public void EmptyAdditionalPropertiesEmitEmptyMetadata()
    {
        using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(new ExecuteToolCallArguments()));
        using var resultDocument = JsonDocument.Parse(JsonSerializer.Serialize(new ExecuteToolCallResult()));

        argumentsDocument.RootElement.GetProperty("metadata").GetRawText().Should().Be("{}");
        resultDocument.RootElement.GetProperty("metadata").GetRawText().Should().Be("{}");
    }

    [TestMethod]
    public void DirectSerializerAndMessageUtilsUseSameTypedWireContract()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            AdditionalProperties = { ["provider_option"] = true },
        };

        using var directDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        using var sdkDocument = JsonDocument.Parse(MessageUtils.SerializeToolPayload(arguments)!);

        JsonElement.DeepEquals(directDocument.RootElement, sdkDocument.RootElement).Should().BeTrue(
            $"direct JSON was {directDocument.RootElement} and SDK JSON was {sdkDocument.RootElement}");
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsUndefinedEnumValues()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = (ToolCallAction)999,
        };

        var act = () => JsonSerializer.Serialize(arguments);

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void MetadataKeysMatchingDeclaredPropertiesRemainNested()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            AdditionalProperties =
            {
                ["action"] = "provider-specific-action",
                ["schema_version"] = "9.9",
            },
        };

        var outcome = new ToolCallResultOutcome
        {
            Status = ToolCallOutcomeStatus.Success,
            AdditionalProperties =
            {
                ["status"] = "provider-specific-status",
            },
        };

        var policy = new ToolCallResultPolicy
        {
            Decision = ToolPolicyDecision.Allow,
            AdditionalProperties =
            {
                ["decision"] = "provider-specific-decision",
            },
        };

        using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        argumentsDocument.RootElement.GetProperty("action").GetString().Should().Be("read");
        argumentsDocument.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
        argumentsDocument.RootElement.GetProperty("metadata").GetProperty("action").GetString()
            .Should().Be("provider-specific-action");
        argumentsDocument.RootElement.GetProperty("metadata").GetProperty("schema_version").GetString()
            .Should().Be("9.9");

        using var outcomeDocument = JsonDocument.Parse(JsonSerializer.Serialize(outcome));
        outcomeDocument.RootElement.GetProperty("status").GetString().Should().Be("success");
        outcomeDocument.RootElement.GetProperty("metadata").GetProperty("status").GetString()
            .Should().Be("provider-specific-status");

        using var policyDocument = JsonDocument.Parse(JsonSerializer.Serialize(policy));
        policyDocument.RootElement.GetProperty("decision").GetString().Should().Be("allow");
        policyDocument.RootElement.GetProperty("metadata").GetProperty("decision").GetString()
            .Should().Be("provider-specific-decision");
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericAction()
    {
        var act = () => JsonSerializer.Deserialize<ExecuteToolCallArguments>(
            """{"action":1}""");

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericOutcomeStatus()
    {
        var act = () => JsonSerializer.Deserialize<ToolCallResultOutcome>(
            """{"status":0}""");

        act.Should().Throw<JsonException>();
    }

    [TestMethod]
    public void DefaultJsonSerializer_RejectsNumericPolicyDecision()
    {
        var act = () => JsonSerializer.Deserialize<ToolCallResultPolicy>(
            """{"decision":0}""");

        act.Should().Throw<JsonException>();
    }
}
