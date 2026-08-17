// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using global::OpenTelemetry.Resources;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Minimal OTLP/HTTP JSON exporter for traces.
    /// Sends POST {Endpoint}/v1/traces with application/json.
    /// </summary>

    public sealed class Agent365ExporterAsync : BaseExporterAsync<Activity>
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
        /// Initializes a new instance of the <see cref="Agent365ExporterAsync"/> class.
        /// </summary>
        /// <param name="core">The Agent365ExporterCore instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="options">The exporter configuration options.</param>
        /// <param name="resource">Optional OpenTelemetry resource information.</param>
        /// <param name="httpClient">Optional HttpClient instance.</param>
        public Agent365ExporterAsync(
            Agent365ExporterCore core,
            ILogger<Agent365Exporter> logger,
            Agent365ExporterOptions options,
            Resource? resource = null,
            HttpClient? httpClient = null)
            : this(core, logger, options, resource, httpClient, replayCoordinator: null, wireDurableDelivery: false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agent365ExporterAsync"/> class with
        /// durable-delivery wiring. Used by the builder (which requests a coordinator built from the
        /// core's shared store) and by tests (which inject a fake coordinator).
        /// </summary>
        /// <param name="core">The Agent365ExporterCore instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="options">The exporter configuration options.</param>
        /// <param name="resource">Optional OpenTelemetry resource information.</param>
        /// <param name="httpClient">Optional HttpClient instance. When null, the exporter owns and disposes an internally created client.</param>
        /// <param name="replayCoordinator">An explicit replay coordinator to own; when null and <paramref name="wireDurableDelivery"/> is true, one is built from the core's shared store.</param>
        /// <param name="wireDurableDelivery">When true, build and start a replay coordinator (and take ownership of the shared store for disposal) when one is not supplied.</param>
        internal Agent365ExporterAsync(
            Agent365ExporterCore core,
            ILogger<Agent365Exporter> logger,
            Agent365ExporterOptions options,
            Resource? resource,
            HttpClient? httpClient,
            IAgent365ReplayCoordinator? replayCoordinator,
            bool wireDurableDelivery)
        {
            this._core = core ?? throw new ArgumentNullException(nameof(core));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this._options = options ?? throw new ArgumentNullException(nameof(options));

            if (_options.TokenResolver == null && _options.ContextualTokenResolver == null)
                throw new ArgumentNullException(nameof(options.TokenResolver),
                    "Agent365 exporter requires a TokenResolver or ContextualTokenResolver. " +
                    "Configure one via UseMicrosoftOpenTelemetry(o => o.Agent365.TokenResolver = ...) or " +
                    "UseMicrosoftOpenTelemetry(o => o.Agent365.ContextualTokenResolver = ...).");

            // Ownership convention: the exporter disposes the HttpClient only when it created it. A
            // caller-supplied client is never disposed.
            this._ownsHttpClient = httpClient == null;
            this._httpClient = httpClient ?? HttpClientFactory.CreateWithTimeout(options.ExporterTimeoutMilliseconds);
            this._resource = resource ?? ResourceBuilder.CreateEmpty().Build();

            if (replayCoordinator != null)
            {
                this._replayCoordinator = replayCoordinator;
            }
            else if (wireDurableDelivery)
            {
                // Own the shared store for disposal and build a coordinator that drains it. Both are null
                // when offline storage is disabled/unavailable, leaving the live core to drop gracefully.
                this._ownedStorage = this._core.Storage;
                this._replayCoordinator = Agent365DurableDelivery.CreateCoordinator(this._core, this._options, this._httpClient, this._logger);
            }

            this._replayCoordinator?.Start();
        }

        /// <summary>
        /// Exports a batch of OpenTelemetry activities to the Microsoft Agent 365 observability platform asynchronously.
        /// </summary>
        /// <param name="batch">The batch of activities to export.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous export operation.</returns>
        public override async Task ExportAsync(IReadOnlyCollection<Activity> batch, CancellationToken cancellationToken)
        {
            this._logger.LogDebug("Agent365ExporterAsync: Exporting batch of {Count} spans.", batch.Count);

            try
            {
                var groups = _core.PartitionByIdentity(batch);
                if (groups.Count == 0)
                {
                    this._logger.LogDebug("Agent365ExporterAsync: No spans with tenant/agent identity found; nothing exported.");
                    return;
                }

                await _core.ExportBatchCoreAsync(
                    groups: groups,
                    resource: this._resource,
                    options: this._options,
                    tokenResolver: (agentId, tenantId) => this._options.TokenResolver!(agentId, tenantId),
                    sendAsync: request => this._httpClient.SendAsync(request, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this._logger.LogWarning("Agent365ExporterAsync: Export operation was canceled.");
                throw;
            }
            catch (Exception exOuter)
            {
                this._logger.LogError(exOuter, "Agent365ExporterAsync: Unhandled export exception.");
            }
        }

        /// <summary>
        /// Stops the background replay loop asynchronously, awaiting its completion so an in-flight replay
        /// send observes <paramref name="cancellationToken"/>. The shared store and HttpClient are not
        /// released here — that happens in <see cref="Dispose"/> — so a processor may shut the loop down
        /// ahead of disposal. Safe when no coordinator is wired (offline storage disabled/unavailable).
        /// </summary>
        /// <param name="cancellationToken">A token that bounds how long the stop may take.</param>
        /// <returns><c>true</c> once the replay loop has stopped (or when there was none).</returns>
        public override async Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (this._replayCoordinator != null)
            {
                await this._replayCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Releases the resources this exporter owns. Stops and disposes the replay coordinator (awaiting
        /// its background loop so no pass outlives the exporter), disposes the shared store it owns, and
        /// disposes the HttpClient only when the exporter created it — a caller-supplied client is never
        /// disposed. Idempotent, so a dispose after <see cref="ShutdownAsync"/> or a double dispose is safe.
        /// </summary>
        public override void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;

            this._replayCoordinator?.Dispose();

            if (this._ownsHttpClient)
            {
                this._httpClient.Dispose();
            }

            this._ownedStorage?.Dispose();

            base.Dispose();
        }
    }
}

