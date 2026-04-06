using Microsoft.VisualStudio.TestTools.UnitTesting;
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.Agent365.Etw;
using System.Diagnostics.Tracing;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Etw
{
    [TestClass]
    public class EtwEventSourceConfigureTests
    {
        [TestMethod]
        public void Log_HasThrowOnEventWriteErrors()
        {
            Assert.IsTrue(EtwEventSource.Log.Settings.HasFlag(EventSourceSettings.ThrowOnEventWriteErrors));
        }

        [TestMethod]
        public void Log_ReturnsSameInstance()
        {
            var a = EtwEventSource.Log;
            var b = EtwEventSource.Log;

            Assert.AreSame(a, b);
        }
    }
}
