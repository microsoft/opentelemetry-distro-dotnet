// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.AI;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Builder.State;
using Microsoft.Extensions.AI;

namespace Microsoft.OpenTelemetry.Agent365.Demo;

/// <summary>
/// A simple hello-world agent that echoes user messages through Azure OpenAI.
/// </summary>
public class HelloAgent : AgentApplication
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<HelloAgent> _logger;

    public HelloAgent(
        AgentApplicationOptions options,
        IChatClient chatClient,
        ILogger<HelloAgent> logger) : base(options)
    {
        _chatClient = chatClient;
        _logger = logger;

        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeAsync);
        OnActivity(ActivityTypes.Message, OnMessageAsync);
    }

    private async Task WelcomeAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
    {
        await turnContext.SendActivityAsync("Hello! I'm a demo agent instrumented with the Microsoft OpenTelemetry Distro.", cancellationToken: ct);
    }

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
    {
        var userMessage = turnContext.Activity.Text ?? string.Empty;
        _logger.LogInformation("Received message: {Length} chars", userMessage.Length);

        // Call Azure OpenAI via the IChatClient abstraction
        var agent = _chatClient
            .AsAIAgent(instructions: "You are a helpful assistant. Keep answers short and friendly.")
            .AsBuilder()
            .UseOpenTelemetry()           // MAF span instrumentation
            .Build();

        var response = await agent.RunAsync(userMessage, cancellationToken: ct);

        await turnContext.SendActivityAsync(response?.ToString() ?? "Sorry, I couldn't generate a response.", cancellationToken: ct);
    }
}
