// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public class Agent365RetryPolicyTests
{
    [DataTestMethod]
    [DataRow(HttpStatusCode.RequestTimeout)]
    [DataRow((HttpStatusCode)429)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.GatewayTimeout)]
    public void RetryableStatusesReturnTrue(HttpStatusCode statusCode)
    {
        Agent365RetryPolicy.IsRetryable(statusCode).Should().BeTrue();
    }

    [TestMethod]
    public void NonRetryableStatusReturnsFalse()
    {
        Agent365RetryPolicy.IsRetryable(HttpStatusCode.Forbidden).Should().BeFalse();
    }

    [TestMethod]
    public void UsesExponentialFallback()
    {
        using var response = new HttpResponseMessage();
        var now = DateTimeOffset.UtcNow;

        Agent365RetryPolicy.GetDelay(response.Headers, 0, now).Should().Be(TimeSpan.FromMilliseconds(500));
        Agent365RetryPolicy.GetDelay(response.Headers, 1, now).Should().Be(TimeSpan.FromSeconds(1));
        Agent365RetryPolicy.GetDelay(response.Headers, 2, now).Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void HonorsAndCapsRetryAfterDelta()
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));

        Agent365RetryPolicy.GetDelay(response.Headers, 0, DateTimeOffset.UtcNow)
            .Should().Be(TimeSpan.FromSeconds(60));
    }

    [TestMethod]
    public void HonorsRetryAfterDate()
    {
        using var response = new HttpResponseMessage();
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(12));

        Agent365RetryPolicy.GetDelay(response.Headers, 0, now)
            .Should().Be(TimeSpan.FromSeconds(12));
    }

    [TestMethod]
    public void PastRetryAfterDateUsesFallback()
    {
        using var response = new HttpResponseMessage();
        var now = DateTimeOffset.UtcNow;
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(-1));

        Agent365RetryPolicy.GetDelay(response.Headers, 0, now)
            .Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void ZeroRetryAfterDeltaIsValidForImmediateRetry()
    {
        using var response = new HttpResponseMessage();
        var now = DateTimeOffset.UtcNow;
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);

        Agent365RetryPolicy.GetDelay(response.Headers, 0, now)
            .Should().Be(TimeSpan.Zero);
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.MultipleChoices)]    // 300
    [DataRow(HttpStatusCode.MovedPermanently)]   // 301
    [DataRow(HttpStatusCode.Found)]               // 302
    [DataRow(HttpStatusCode.BadRequest)]          // 400
    [DataRow(HttpStatusCode.Unauthorized)]        // 401
    [DataRow(HttpStatusCode.Forbidden)]           // 403
    [DataRow(HttpStatusCode.NotFound)]            // 404
    public void NonRetryableStatusCodesReturnFalse(HttpStatusCode statusCode)
    {
        Agent365RetryPolicy.IsRetryable(statusCode).Should().BeFalse();
    }
}
