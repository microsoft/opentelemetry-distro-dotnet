// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Etw;

namespace Microsoft.OpenTelemetry.Agent365.Tests
{
    /// <summary>
    /// Guards the type forwards that keep <c>Microsoft.OpenTelemetry</c> binary compatible after the
    /// Agent365 contracts and ETW types moved into the standalone
    /// <c>Microsoft.Agents.A365.Observability.Contracts</c> and
    /// <c>Microsoft.Agents.A365.Observability.Etw</c> assemblies.
    /// </summary>
    /// <remarks>
    /// The expected sets are written out explicitly instead of being parsed from
    /// <c>PublicAPI.Shipped.txt</c> or <c>TypeForwards.Agent365.cs</c> at runtime: a hand-maintained list
    /// fails loudly (and readably) when a forward is dropped, whereas parsing the same sources the
    /// production code is generated from would make the guard agree with the bug.
    /// </remarks>
    [TestClass]
    public class TypeForwardingGuardTests
    {
        /// <summary>
        /// Public types that shipped in <c>Microsoft.OpenTelemetry</c> before the split and now live in a
        /// standalone assembly. Every one of these must stay forwarded or already-compiled consumers break.
        /// </summary>
        private static readonly string[] ExpectedMovedShippedTypes =
        {
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.ApplyGuardrailData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.BaseData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.ApplyGuardrailDataBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.BaseDataBuilder`1",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.ExecuteInferenceDataBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.ExecuteToolDataBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.InvokeAgentDataBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.OutputDataBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders.SpanStatusBuilder",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.ExecuteInferenceData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.ExecuteToolData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.InvokeAgentData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.OutputData",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.SpanStatus",
            "Microsoft.Agents.A365.Observability.Runtime.DTOs.SpanStatusCode",
            "Microsoft.Agents.A365.Observability.Runtime.Etw.EtwEventSource",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.AgentDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.AgentType",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.CallerDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Channel",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GenAiRequestParameters",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GenAiResponseParameters",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GuardrailDecisionType",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GuardrailDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GuardrailFinding",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GuardrailRiskSeverity",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.GuardrailTargetType",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.InferenceCallDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.InferenceOperationType",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.InvokeAgentScopeDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.BlobPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ChatMessage",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.FilePart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.FinishReason",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.GenericPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.IMessagePart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.InputMessages",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.MessageRole",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.Modality",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.OutputMessage",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.OutputMessages",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ReasoningPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ServerToolCallPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ServerToolCallResponsePart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.TextPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ToolCallRequestPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ToolCallResponsePart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.UriPart",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.OperationSource",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Request",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Response",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.SpanDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.ThreatDiagnosticsSummary",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.ToolCallDetails",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.ToolType",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.UserDetails",
        };

        /// <summary>
        /// Moved public types that were still unshipped (<c>PublicAPI.Unshipped.txt</c>) when the split
        /// happened. They are forwarded too, so the full forwarded set is the union of both arrays.
        /// </summary>
        private static readonly string[] ExpectedMovedUnshippedTypes =
        {
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ExecuteToolCallArguments",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ExecuteToolCallResult",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallAction",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallContainer",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallIdentifier",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallOutcomeStatus",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResource",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultOutcome",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultPagination",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultPolicy",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultResource",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultSecurity",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolCallResultSensitivity",
            "Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools.ToolPolicyDecision",
        };

        private static readonly string[] StandaloneAssemblyNames =
        {
            "Microsoft.Agents.A365.Observability.Contracts",
            "Microsoft.Agents.A365.Observability.Etw",
        };

        private static Assembly DistroAssembly => typeof(EtwLoggingBuilder).Assembly;

        private static Type[] ForwardedTypes => DistroAssembly.GetForwardedTypes();

        private static string[] ForwardedTypeNames =>
            ForwardedTypes.Select(type => type.FullName!).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        [TestMethod]
        public void Guard_AnchorsTheDistroAssembly()
        {
            DistroAssembly.GetName().Name.Should().Be("Microsoft.OpenTelemetry");
        }

        [TestMethod]
        public void DistroAssembly_ForwardsEveryMovedShippedPublicType()
        {
            var forwarded = new HashSet<string>(ForwardedTypeNames, StringComparer.Ordinal);

            var missing = ExpectedMovedShippedTypes
                .Where(name => !forwarded.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            missing.Should().BeEmpty(
                "every shipped public type moved out of Microsoft.OpenTelemetry needs a [TypeForwardedTo] " +
                "entry in Properties/TypeForwards.Agent365.cs so already-compiled consumers keep binding");
        }

        [TestMethod]
        public void DistroAssembly_ForwardedTypeSet_MatchesExpectedSet()
        {
            var expected = ExpectedMovedShippedTypes
                .Concat(ExpectedMovedUnshippedTypes)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            ForwardedTypeNames.Should().BeEquivalentTo(
                expected,
                "the forwarded set must be exactly the moved Agent365 public types; update both this guard " +
                "and Properties/TypeForwards.Agent365.cs when the split changes");
        }

        [TestMethod]
        public void ForwardedTypes_ResolveToTheStandaloneAgent365Assemblies()
        {
            var unexpected = ForwardedTypes
                .Where(type => !StandaloneAssemblyNames.Contains(type.Assembly.GetName().Name, StringComparer.Ordinal))
                .Select(type => $"{type.FullName} -> {type.Assembly.GetName().Name}")
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();

            unexpected.Should().BeEmpty();
        }
    }
}
