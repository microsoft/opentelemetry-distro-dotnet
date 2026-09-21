// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Contracts.Tests;

[TestClass]
public class PublicSurfaceTests
{
    [TestMethod]
    public void CrossAssemblyContractTypes_ArePublic()
    {
        Type[] types =
        [
            typeof(SpanKindConstants),
            typeof(MessageUtils),
            typeof(MessageUtils.SnakeCaseJsonStringEnumConverter),
            typeof(AutoInstrumentationConstants),
            typeof(OpenTelemetryConstants),
            typeof(MessagePartConverter),
            typeof(InputMessagesConverter),
            typeof(OutputMessagesConverter),
        ];

        types.Should().OnlyContain(type => type.IsPublic || type.IsNestedPublic);
    }

    [TestMethod]
    public void CrossAssemblyContractMembers_ArePublic()
    {
        MemberInfo[] members =
        [
            typeof(MessageUtils).GetField(nameof(MessageUtils.SerializerOptions))!,
            typeof(OpenTelemetryConstants).GetField(nameof(OpenTelemetryConstants.GenAiOperationNames))!,
            typeof(ThreatDiagnosticsSummary).GetMethod(nameof(ThreatDiagnosticsSummary.ToJson))!,
            typeof(TransferDetails).GetProperty(nameof(TransferDetails.ModeValue))!,
            typeof(TransferDetails).GetProperty(nameof(TransferDetails.TargetTypeValue))!,
        ];

        members.Should().OnlyContain(member => IsPublic(member));
    }

    private static bool IsPublic(MemberInfo member) => member switch
    {
        FieldInfo field => field.IsPublic,
        MethodBase method => method.IsPublic,
        PropertyInfo property => property.GetMethod?.IsPublic == true,
        _ => false,
    };
}
