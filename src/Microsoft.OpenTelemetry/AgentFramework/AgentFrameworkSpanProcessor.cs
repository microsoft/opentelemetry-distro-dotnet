// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;
using global::OpenTelemetry;
using System.Diagnostics;

namespace Microsoft.OpenTelemetry.AgentFramework
{
    internal class AgentFrameworkSpanProcessor : BaseProcessor<Activity>
    {
        private const string InvokeAgentOperation = "invoke_agent";
        private const string ChatOperation = "chat";
        private const string ExecuteToolOperation = "execute_tool";
        private const string ToolCallResultTag = "gen_ai.tool.call.result";
        private const string EventContentTag = "gen_ai.event.content";
        private readonly string[] _additionalSources;

        public AgentFrameworkSpanProcessor(params string[] additionalSources)
        {
            _additionalSources = additionalSources ?? [];
        }

        public override void OnStart(Activity activity)
        {
        }

        public override void OnEnd(Activity activity)
        {
            if (activity == null)
                return;

            if (IsTrackedSource(activity.Source.Name))
            {
                var operationName = activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey);
                if (operationName is string opName)
                {
                    switch (opName)
                    {
                        case InvokeAgentOperation:
                        case ChatOperation:
                            AgentFrameworkSpanProcessorHelper.ProcessInputOutputMessages(activity);
                            break;

                        case ExecuteToolOperation:
                            var toolCallResult = activity.GetTagItem(ToolCallResultTag);
                            activity.SetTag(EventContentTag, toolCallResult);
                            break;
                    }
                }
            }
        }

        private bool IsTrackedSource(string sourceName)
        {
            if (sourceName.StartsWith(AgentFrameworkConstants.DefaultSource))
            {
                return true;
            }

            foreach (var source in _additionalSources)
            {
                if (!string.IsNullOrWhiteSpace(source) && sourceName.StartsWith(source))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
