using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using Microsoft.OpenTelemetry.Agent365.DTOs;
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.Tests.DTOs
{
    [TestClass]
    public class InvokeAgentDataTests
    {
        [TestMethod]
        public void Name_ReturnsInvokeAgent()
        {
            var data = new InvokeAgentData();
            data.Name.Should().Be(OpenTelemetryConstants.OperationNames.InvokeAgent.ToString());
        }

        [TestMethod]
        public void SpanKind_DefaultsToNull()
        {
            var data = new InvokeAgentData();
            data.SpanKind.Should().BeNull();
        }

        [TestMethod]
        public void SpanKind_UsesProvidedValue()
        {
            var data = new InvokeAgentData(spanKind: SpanKindConstants.Server);
            data.SpanKind.Should().Be(SpanKindConstants.Server);
        }
    }
}
