using Microsoft.VisualStudio.TestTools.UnitTesting;
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.OpenTelemetry.Agent365.DTOs;
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.Tests.DTOs
{
    [TestClass]
    public class ExecuteToolDataTests
    {
        [TestMethod]
        public void Name_ReturnsExecuteTool()
        {
            var data = new ExecuteToolData();
            data.Name.Should().Be(OpenTelemetryConstants.OperationNames.ExecuteTool.ToString());
        }

        [TestMethod]
        public void SpanKind_DefaultsToNull()
        {
            var data = new ExecuteToolData();
            data.SpanKind.Should().BeNull();
        }

        [TestMethod]
        public void SpanKind_UsesProvidedValue()
        {
            var data = new ExecuteToolData(spanKind: SpanKindConstants.Client);
            data.SpanKind.Should().Be(SpanKindConstants.Client);
        }
    }
}
