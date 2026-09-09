// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class SafeConsoleLoggerProviderTests
{
    private const string MarkerSecret = "marker-secret-9f3d21a0";

    [TestMethod]
    public void Log_WithException_IncludesLevelCategoryAndMessageButExcludesExceptionDetails()
    {
        var writer = new StringWriter();
        using var provider = new SafeConsoleLoggerProvider(writer);
        var logger = provider.CreateLogger("Agent365ExporterCore");

        var exception = CreateExceptionWithInnerAndMarker();

        logger.LogError(exception, "TokenResolver threw for agent {AgentId} tenant {TenantId}.", "agent-1", "tenant-1");

        var output = writer.ToString();

        // Level, category, and the formatted message must be present.
        output.Should().Contain("fail");
        output.Should().Contain("Agent365ExporterCore");
        output.Should().Contain("TokenResolver threw for agent agent-1 tenant tenant-1.");

        // No exception detail may leak: message, type name, stack trace, inner exception, or marker.
        output.Should().NotContain(MarkerSecret);
        output.Should().NotContain(nameof(InvalidOperationException));
        output.Should().NotContain(nameof(ApplicationException));
        output.Should().NotContain("outer failure");
        output.Should().NotContain("inner failure");
        output.Should().NotContain("at Microsoft.OpenTelemetry");
        output.Should().NotContain("--- End of inner exception stack trace ---");
        output.Should().NotContain(exception.GetType().FullName!);
    }

    [TestMethod]
    public void Log_BelowMinimumLevel_WritesNothing()
    {
        var writer = new StringWriter();
        using var provider = new SafeConsoleLoggerProvider(writer, LogLevel.Information);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogDebug("This should not appear.");

        writer.ToString().Should().BeEmpty();
    }

    [TestMethod]
    public void Log_AtOrAboveMinimumLevel_Writes()
    {
        var writer = new StringWriter();
        using var provider = new SafeConsoleLoggerProvider(writer, LogLevel.Information);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Visible message.");

        writer.ToString().Should().Contain("Visible message.");
    }

    [TestMethod]
    public void Dispose_DoesNotDisposeSuppliedWriter()
    {
        var writer = new StringWriter();
        var provider = new SafeConsoleLoggerProvider(writer);

        provider.Dispose();

        var act = () => writer.Write("still usable after provider disposal");
        act.Should().NotThrow();
        writer.ToString().Should().Contain("still usable after provider disposal");
    }

    [TestMethod]
    public void Log_ConcurrentWrites_ProduceOneCompleteLinePerCall()
    {
        var writer = new SynchronizedNoOpFlushingWriter();
        using var provider = new SafeConsoleLoggerProvider(writer);
        var logger = provider.CreateLogger("ConcurrentCategory");
        const int CallCount = 200;

        Parallel.For(0, CallCount, i =>
        {
            logger.LogInformation("Message number {Number}.", i);
        });

        var lines = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(CallCount);
        for (var i = 0; i < CallCount; i++)
        {
            lines.Should().Contain(line => line.Contains($"Message number {i}."));
        }
    }

    private static Exception CreateExceptionWithInnerAndMarker()
    {
        try
        {
            try
            {
                throw new InvalidOperationException($"inner failure containing {MarkerSecret}");
            }
            catch (Exception inner)
            {
                throw new ApplicationException($"outer failure containing {MarkerSecret}", inner);
            }
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// A minimal thread-unsafe <see cref="TextWriter"/> used to prove the provider's own
    /// locking serializes writes; if the provider did not lock, concurrent
    /// <see cref="StringBuilder"/> appends below would corrupt or drop lines.
    /// </summary>
    private sealed class SynchronizedNoOpFlushingWriter : TextWriter
    {
        private readonly StringBuilder builder = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => this.builder.Append(value).Append(Environment.NewLine);

        public override string ToString() => this.builder.ToString();
    }
}
