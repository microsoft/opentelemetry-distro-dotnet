// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests.E2ETests
{
    public class SqlClientInstrumentationTests
    {
        private const string TestSqlConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Database=master";
        private readonly DiagnosticSource _fakeSqlDiagnosticSource = new DiagnosticListener("SqlClientDiagnosticListener");

        private readonly ITestOutputHelper _output;

        public SqlClientInstrumentationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task SqlClientCallsAreCaptured()
        {
            // ARRANGE
            var exportedActivities = new List<Activity>();
            var services = new ServiceCollection();

            services.AddOpenTelemetry()
                .UseAzureMonitor(o =>
                {
                    o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                    o.EnableLiveMetrics = false;
                    o.DisableOfflineStorage = true;
                    o.SamplingRatio = 1.0f;
                    o.TracesPerSecond = null;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities))
                .ConfigureResource(r => r.AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("service.name", "test-sql-client"),
                }));

            using var sp = services.BuildServiceProvider();
            var hostedServices = sp.GetServices<IHostedService>();
            foreach (var hs in hostedServices) await hs.StartAsync(CancellationToken.None);

            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // ACT — simulate SQL call via DiagnosticSource
            using var sqlConnection = new SqlConnection(TestSqlConnectionString);
            using var sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "SP_GetOrders";
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var operationId = Guid.NewGuid();

            _fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandBefore",
                value: new { OperationId = operationId, Command = sqlCommand, Timestamp = 1000000L });

            _fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandAfter",
                value: new { OperationId = operationId, Command = sqlCommand, Timestamp = 2000000L });

            // SHUTDOWN
            tracerProvider.ForceFlush();
            tracerProvider.Shutdown();

            // ASSERT
            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} tags=[{string.Join(", ", a.Tags.Select(t => $"{t.Key}={t.Value}"))}]");
            }

            var sqlActivities = exportedActivities.Where(a =>
                a.DisplayName.Contains("SP_GetOrders") ||
                a.Tags.Any(t => t.Key == "db.system")).ToList();

            Assert.NotEmpty(sqlActivities);
            var activity = sqlActivities.First();
            Assert.Contains(activity.Tags, t => t.Key == "db.system.name" || t.Key == "db.system");
        }

        [Fact]
        public async Task SqlClientErrorsAreCaptured()
        {
            // ARRANGE
            var exportedActivities = new List<Activity>();
            var services = new ServiceCollection();

            services.AddOpenTelemetry()
                .UseAzureMonitor(o =>
                {
                    o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
                    o.EnableLiveMetrics = false;
                    o.DisableOfflineStorage = true;
                    o.SamplingRatio = 1.0f;
                    o.TracesPerSecond = null;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var hostedServices = sp.GetServices<IHostedService>();
            foreach (var hs in hostedServices) await hs.StartAsync(CancellationToken.None);

            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // ACT
            using var sqlConnection = new SqlConnection(TestSqlConnectionString);
            using var sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "SP_Fail";
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var operationId = Guid.NewGuid();

            _fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandBefore",
                value: new { OperationId = operationId, Command = sqlCommand, Timestamp = 1000000L });

            _fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandError",
                value: new { OperationId = operationId, Command = sqlCommand, Exception = new Exception("SQL Error!"), Timestamp = 2000000L });

            // SHUTDOWN
            tracerProvider.ForceFlush();
            tracerProvider.Shutdown();

            // ASSERT
            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} status={a.Status}");
            }

            var sqlActivities = exportedActivities.Where(a =>
                a.DisplayName.Contains("SP_Fail") ||
                a.Tags.Any(t => t.Key == "db.system")).ToList();

            Assert.NotEmpty(sqlActivities);
            var activity = sqlActivities.First();
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
        }
    }
}
#endif
