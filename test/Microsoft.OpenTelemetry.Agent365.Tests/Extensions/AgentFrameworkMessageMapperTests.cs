// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.A365.Observability.Extensions.AgentFramework.Utils;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Extensions
{
    [TestClass]
    public class AgentFrameworkMessageMapperTests
    {
        [TestMethod]
        public void MapInputMessages_MapsUserAndAssistantMessages()
        {
            var activity = new Activity("test");
            var inputMessages = @"[
                {""role"": ""user"", ""parts"": [{""type"": ""text"", ""content"": ""Hello""}]},
                {""role"": ""assistant"", ""parts"": [{""type"": ""text"", ""content"": ""Hi there!""}]}
            ]";

            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, inputMessages);

            var result = AgentFrameworkMessageMapper.MapInputMessages(activity);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("\"version\":\"0.1.0\""), "Should contain version");
            Assert.IsTrue(result.Contains("\"role\":\"user\""), "Should contain user role");
            Assert.IsTrue(result.Contains("Hello"), "User message should be preserved");
            Assert.IsTrue(result.Contains("Hi there!"), "Assistant message should be preserved");
            Assert.IsTrue(result.Contains("\"type\":\"text\""), "Should contain text part type");
        }

        [TestMethod]
        public void MapInputMessages_IncludesSystemMessages()
        {
            var activity = new Activity("test");
            var inputMessages = @"[
                {""role"": ""system"", ""parts"": [{""type"": ""text"", ""content"": ""You are a helpful assistant.""}]},
                {""role"": ""user"", ""parts"": [{""type"": ""text"", ""content"": ""Hello""}]}
            ]";

            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, inputMessages);

            var result = AgentFrameworkMessageMapper.MapInputMessages(activity);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("\"version\":\"0.1.0\""), "Should contain version");
            Assert.IsTrue(result.Contains("\"role\":\"system\""), "System message should be included");
            Assert.IsTrue(result.Contains("You are a helpful assistant."), "System content should be preserved");
        }

        [TestMethod]
        public void MapOutputMessages_HandlesFinishReason()
        {
            var activity = new Activity("test");
            var outputMessages = @"[
                {
                    ""role"": ""assistant"",
                    ""parts"": [{""type"": ""text"", ""content"": ""Here is your answer.""}],
                    ""finish_reason"": ""stop""
                }
            ]";

            activity.SetTag(OpenTelemetryConstants.GenAiOutputMessagesKey, outputMessages);

            var result = AgentFrameworkMessageMapper.MapOutputMessages(activity);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("\"version\":\"0.1.0\""), "Should contain version");
            Assert.IsTrue(result.Contains("Here is your answer"), "Output content should be preserved");
        }

        [TestMethod]
        public void MapInputMessages_HandlesEmptyPartsArray()
        {
            var activity = new Activity("test");
            var inputMessages = @"[
                {""role"": ""user"", ""parts"": []},
                {""role"": ""assistant"", ""parts"": [{""type"": ""text"", ""content"": ""Valid message""}]}
            ]";

            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, inputMessages);

            var result = AgentFrameworkMessageMapper.MapInputMessages(activity);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Valid message"), "Valid message should be preserved");
        }

        [TestMethod]
        public void MapInputMessages_ReturnsNullOnInvalidJson()
        {
            var activity = new Activity("test");
            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, "not a valid json");

            var result = AgentFrameworkMessageMapper.MapInputMessages(activity);
            Assert.IsNull(result, "Should return null on invalid JSON");
        }

        [TestMethod]
        public void MapInputMessages_ReturnsNullOnMissingTag()
        {
            var activity = new Activity("test");

            Assert.IsNull(AgentFrameworkMessageMapper.MapInputMessages(activity));
            Assert.IsNull(AgentFrameworkMessageMapper.MapOutputMessages(activity));
        }

        [TestMethod]
        public void MapInputMessages_ReturnsNullOnEmptyArray()
        {
            var activity = new Activity("test");
            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, "[]");

            Assert.IsNull(AgentFrameworkMessageMapper.MapInputMessages(activity));
        }

        [TestMethod]
        public void MapInputMessages_HandlesToolCallParts()
        {
            var activity = new Activity("test");
            var inputMessages = @"[
                {""role"": ""assistant"", ""parts"": [
                    {""type"": ""tool_call"", ""name"": ""GetWeather"", ""id"": ""call_1"", ""arguments"": {""location"": ""Seattle""}}
                ]}
            ]";

            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, inputMessages);

            var result = AgentFrameworkMessageMapper.MapInputMessages(activity);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("\"version\":\"0.1.0\""), "Should contain version");
            Assert.IsTrue(result.Contains("GetWeather"), "Should contain tool name");
        }

        [TestMethod]
        public void MapInputAndOutput_BothProduceVersionedFormat()
        {
            var activity = new Activity("test");
            activity.SetTag(OpenTelemetryConstants.GenAiInputMessagesKey, @"[{""role"":""user"",""parts"":[{""type"":""text"",""content"":""hi""}]}]");
            activity.SetTag(OpenTelemetryConstants.GenAiOutputMessagesKey, @"[{""role"":""assistant"",""parts"":[{""type"":""text"",""content"":""hello""}]}]");

            var inputResult = AgentFrameworkMessageMapper.MapInputMessages(activity);
            var outputResult = AgentFrameworkMessageMapper.MapOutputMessages(activity);

            Assert.IsNotNull(inputResult);
            Assert.IsNotNull(outputResult);
            Assert.IsTrue(inputResult.Contains("\"version\":\"0.1.0\""));
            Assert.IsTrue(outputResult.Contains("\"version\":\"0.1.0\""));
            Assert.IsTrue(inputResult.Contains("hi"));
            Assert.IsTrue(outputResult.Contains("hello"));
        }
    }
}
