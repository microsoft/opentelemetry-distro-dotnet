using Microsoft.VisualStudio.TestTools.UnitTesting;
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.OpenTelemetry.Agent365.DTOs;
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.Tests.DTOs
{
    [TestClass]
    public class OutputDataTests
    {
        [TestMethod]
        public void Name_ReturnsOutputMessages()
        {
            var data = new OutputData();
            data.Name.Should().Be(OpenTelemetryConstants.OperationNames.OutputMessages.ToString());
        }
    }
}
