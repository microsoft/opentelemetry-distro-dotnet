// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Logging;
using global::OpenTelemetry;
using global::OpenTelemetry.Resources;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Minimal OTLP/HTTP JSON exporter for traces.
    /// Sends POST {Endpoint}/v1/traces with application/json.
    /// </summary>

    public sealed class Agent365Exporter : BaseExporter<Activity>
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly Resource _resource;
        private readonly ILogger<Agent365Exporter> _logger;
        private readonly Agent365ExporterOptions _options;
        private readonly Agent365ExporterCore _core;
        private readonly IAgent365ReplayCoordinator? _replayCoordinator;
        private readonly IAgent365PersistentStorage? _ownedStorage;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Agent365Exporter"/> class.
        /// </summary>
        /// <param name="core">The Agent365ExporterCore instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="options">The exporter configuration options.</param>
        /// <param name="resource">Optional OpenTelemetry resource information.</param>
        /// <param name="httpClient">Optional HttpClient instance.</param>
        public Agent365Exporter(
            Agent365ExporterCore core,
            ILogger<Agent365Exporter> logger,
            Agent365ExporterOptions options,
            Resource? resource = null,
            HttpClient? httpClient = null)
            : this(core, logger, options, resource, httpClient, replayCoordinator: null, wireDurableDelivery: false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agent365Exporter"/> class with durable-delivery
        /// wiring. Used by the builder (which requests a coordinator built from the core's shared store)
        /// and by tests (which inject a fake coordinator).
        /// </summary>
        /// <param name="core">The Agent365ExporterCore instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="options">The exporter configuration options.</param>
        /// <param name="resource">Optional OpenTelemetry resource information.</param>
        /// <param name="httpClient">Optional HttpClient instance. When null, the exporter owns and disposes an internally created client.</param>
        /// <param name="replayCoordinator">An explicit replay coordinator to own; when null and <paramref name="wireDurableDelivery"/> is true, one is built from the core's shared store.</param>
        /// <param name="wireDurableDelivery">When true, build and start a replay coordinator (and take ownership of the shared store for disposal) when one is not supplied.</param>
        internal Agent365Exporter(
            Agent365ExporterCore core,
            ILogger<Agent365Exporter> logger,
            Agent365ExporterOptions options,
            Resource? resource,
            HttpClient? httpClient,
            IAgent365ReplayCoordinator? replayCoordinator,
            bool wireDurableDelivery)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (_options.TokenResolver == null && _options.ContextualTokenResolver == null)
                throw new ArgumentNullException(nameof(options.TokenResolver),
                    "Agent365 exporter requires a TokenResolver or ContextualTokenResolver. " +
                    "Configure one via UseMicrosoftOpenTelemetry(o => o.Agent365.TokenResolver = ...) or " +
                    "UseMicrosoftOpenTelemetry(o => o.Agent365.ContextualTokenResolver = ...).");

            // Ownership convention: the exporter disposes the HttpClient only when it created it. A
            // caller-supplied client is never disposed.
            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? HttpClientFactory.CreateWithTimeout(options.ExporterTimeoutMilliseconds);
            _resource = resource ?? ResourceBuilder.CreateEmpty().Build();

            if (replayCoordinator != null)
            {
                _replayCoordinator = replayCoordinator;
            }
            else if (wireDurableDelivery)
            {
                // Own the shared store for disposal and build a coordinator that drains it. Both are null
                // when offline storage is disabled/unavailable, leaving the live core to drop gracefully.
                _ownedStorage = _core.Storage;
                _replayCoordinator = Agent365DurableDelivery.CreateCoordinator(_core, _options, _httpClient, _logger);
            }

            _replayCoordinator?.Start();
        }

        /// <summary>
        /// Exports a batch of OpenTelemetry activities to the Microsoft Agent 365 observability platform.
        /// </summary>
        /// <param name="batch">The batch of activities to export.</param>
        /// <returns>The export result indicating success or failure.</returns>
        public override ExportResult Export(in Batch<Activity> batch)
        {
            _logger.LogDebug("Agent365Exporter: Exporting batch of {Count} spans.", batch.Count);

            try
            {
                var groups = _core.PartitionByIdentity(batch);
                if (groups.Count == 0)
                {
                    _logger.LogDebug("Agent365Exporter: No spans with tenant/agent identity found; nothing exported.");
                    return ExportResult.Success;
                }

                // Use the async core method, synchronously
                return _core.ExportBatchCoreAsync(
                    groups: groups,
                    resource: _resource,
                    options: _options,
                    tokenResolver: (agentId, tenantId) => _options.TokenResolver!(agentId, tenantId),
                    sendAsync: request => _httpClient.SendAsync(request)
                ).GetAwaiter().GetResult();
            }
            catch (Exception exOuter)
            {
                _logger.LogError(exOuter, "Agent365Exporter: Unhandled export exception.");
                return ExportResult.Failure;
            }
        }

        /// <summary>
        /// Stops the durable-delivery replay loop as part of OpenTelemetry's cooperative shutdown,
        /// bounded by <paramref name="timeoutMilliseconds"/>. This is the graceful counterpart to
        /// <see cref="Dispose(bool)"/>: shutdown halts the background drain (so no replay pass outlives
        /// the provider's shutdown deadline) while dispose releases the coordinator, the shared store,
        /// and any owned HttpClient. The base class guarantees this runs at most once. It never throws —
        /// a failure to stop within the deadline is logged and reported as an unsuccessful shutdown.
        /// </summary>
        /// <param name="timeoutMilliseconds">
        /// The number of milliseconds allowed for the stop to complete, or <see cref="Timeout.Infinite"/>
        /// (-1) to wait indefinitely.
        /// </param>
        /// <returns><c>true</c> when the replay loop stopped within the deadline; otherwise <c>false</c>.</returns>
        protected override bool OnShutdown(int timeoutMilliseconds)
        {
            if (_replayCoordinator == null)
            {
                return true;
            }

            try
            {
                using var cts = timeoutMilliseconds == Timeout.Infinite
                    ? new CancellationTokenSource()
                    : new CancellationTokenSource(timeoutMilliseconds);

                // StopAsync observes the token cooperatively and returns (without throwing) once the loop
                // drains or the deadline fires, so this wait is bounded by timeoutMilliseconds.
                _replayCoordinator.StopAsync(cts.Token).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent365Exporter: Failed to stop the replay coordinator during shutdown.");
                return false;
            }
        }

        /// <summary>
        /// Releases the resources this exporter owns. Stops and disposes the replay coordinator (awaiting
        /// its background loop so no pass outlives the exporter), disposes the shared store it owns, and
        /// disposes the HttpClient only when the exporter created it — a caller-supplied client is never
        /// disposed. The base class guards against a double dispose, so the coordinator and store are
        /// released exactly once.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                _replayCoordinator?.Dispose();

                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }

                _ownedStorage?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

