using FluentAssertions;
using Microsoft.Identity.Client;
using Microsoft.OpenTelemetry.Agent365.S2S.Demo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class MsalTokenExchangeClientTests
{
    [TestMethod]
    public void CreateExchangeFailure_IncludesStageAndErrorCode()
    {
        var exception = new MsalServiceException(
            "invalid_client",
            "contains sample-secret and blueprint-token");

        var result = MsalTokenExchangeClient.CreateExchangeFailure(
            "The blueprint app token exchange",
            exception);

        result.Message.Should().Be("The blueprint app token exchange failed (invalid_client).");
        result.Message.Should().NotContain("sample-secret");
        result.Message.Should().NotContain("blueprint-token");
    }

    [TestMethod]
    public void CreateExchangeFailure_PreservesInnerException()
    {
        var exception = new MsalServiceException(
            "invalid_scope",
            "contains observability-token");

        var result = MsalTokenExchangeClient.CreateExchangeFailure(
            "The Agent365 observability token exchange",
            exception);

        result.InnerException.Should().BeSameAs(exception);
    }
}
