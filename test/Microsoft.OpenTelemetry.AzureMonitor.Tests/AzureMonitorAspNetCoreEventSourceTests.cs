// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;
using Microsoft.OpenTelemetry.AzureMonitor.Tests.CommonTestFramework;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    public class AzureMonitorAspNetCoreEventSourceTests
    {
        /// <summary>
        /// This test uses reflection to invoke every Event method in our EventSource class.
        /// This validates that parameters are logged and helps to confirm that EventIds are correct.
        /// </summary>
        [Fact]
        public void EventSourceTest_AzureMonitorAspNetCoreEventSource()
        {
            EventSourceTestHelper.MethodsAreImplementedConsistentlyWithTheirAttributes(AzureMonitorAspNetCoreEventSource.Log);
        }
    }
}
