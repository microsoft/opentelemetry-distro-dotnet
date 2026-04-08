// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder;
using Microsoft.Agents.Core;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.AI;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.OpenTelemetry;
using Microsoft.OpenTelemetry.Agent365.Demo;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Microsoft OpenTelemetry Distro — one line for all telemetry
// ---------------------------------------------------------------------------
builder.UseMicrosoftOpenTelemetry(o =>
{
    // Export to Agent365 backend + Console (for local debugging).
    // Azure Monitor is auto-detected if APPLICATIONINSIGHTS_CONNECTION_STRING is set.
    o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;

    // Wire the Agent365 exporter token resolver.
    // In production, use AgenticTokenCache with UserAuthorization.
    // For local dev/testing, a static bearer token from env var works.
    o.Agent365.Exporter.TokenResolver = TokenResolverFactory.Create(builder.Configuration);
});

// ---------------------------------------------------------------------------
// 2. Agent Framework setup
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.AddAgentApplicationOptions();

// Register IChatClient (Azure OpenAI)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["AIServices:AzureOpenAI:Endpoint"]
        ?? throw new InvalidOperationException("Set AIServices:AzureOpenAI:Endpoint in appsettings or user secrets.");
    var apiKey = config["AIServices:AzureOpenAI:ApiKey"]
        ?? throw new InvalidOperationException("Set AIServices:AzureOpenAI:ApiKey in appsettings or user secrets.");
    var deployment = config["AIServices:AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

    return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
        .GetChatClient(deployment)
        .AsIChatClient();
});

// Register the agent
builder.AddAgent<HelloAgent>();

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Map the /api/messages endpoint — A365 sends messages here
app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response,
    IAgentHttpAdapter adapter, IAgent agent, CancellationToken ct) =>
{
    await adapter.ProcessAsync(request, response, agent, ct);
});

app.MapGet("/", () => "Agent365 E2E Demo — talk to this agent via Teams or Copilot.");
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Urls.Add("http://localhost:3978");
app.Run();
