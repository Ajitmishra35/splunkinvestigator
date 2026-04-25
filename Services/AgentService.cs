using Microsoft.Extensions.AI;
using SplunkInvestigator.Models;
using SplunkInvestigator.Tools;

namespace SplunkInvestigator.Services;

/// <summary>
/// The Splunk Investigator Agent.
/// Uses Microsoft.Extensions.AI IChatClient with tool-calling loop.
/// Tools read from local exported log files — no MCP needed.
/// </summary>
public class AgentService
{
    private readonly IChatClient _chatClient;
    private readonly SplunkTools _tools;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentService> _logger;

    private const string SystemPrompt = """
        You are a Splunk Investigator Agent for a payment microservice written in Java.
        You have access to Splunk log exports from the payments index (index="payments").

        Your role:
        - Investigate payment transactions using the tools available to you
        - Given a transaction reference or query, search through the logs
        - Create a brief, neutral investigation/analysis report
        - Do NOT include sensitive data (card numbers, raw amounts, internal technical URLs) in your report
        - Do NOT offer specific recommendations or next steps unless explicitly asked
        - Your tone should be neutral and factual
        - Always use the payments index when calling tools (index="payments")
        - At the end of your report, include the Splunk search URL for reference

        Available log data covers: payment initiations, gateway requests, errors, retries, fraud detection events.

        Format your investigation report with clear sections:
        ## Investigation Report
        ### Transaction Overview
        ### Event Timeline
        ### Issues Identified (if any)
        ### Splunk Reference URL
        """;

    public AgentService(
        IChatClient chatClient,
        SplunkTools tools,
        IConfiguration config,
        ILogger<AgentService> logger)
    {
        _chatClient = chatClient;
        _tools = tools;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends a user message to the agent and returns the full response.
    /// The agent automatically calls tools as needed (agentic loop).
    /// </summary>
    public async Task<string> InvestigateAsync(
        string userQuery,
        List<Models.ChatMessage> conversationHistory,
        IProgress<string>? progress = null)
    {
        var splunkWebUrl = _config["SplunkSettings:WebUrl"] ?? "https://splunk.yourcompany.com:8000";

        // Build the message history for context
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, SystemPrompt + $"\n\nSplunk Web URL: {splunkWebUrl}")
        };

        // Add prior conversation turns (last 10 for context window)
        foreach (var h in conversationHistory.TakeLast(10))
        {
            var role = h.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new(role, h.Content));
        }

        // Add current user message
        messages.Add(new(ChatRole.User, userQuery));

        // Chat options: attach tools + enable automatic tool-calling loop
        var options = new ChatOptions
        {
            Tools = [.. _tools.GetTools()],
            ToolMode = ChatToolMode.Auto,
            Temperature = 0.2f  // Low temp for factual investigation reports
        };

        try
        {
            progress?.Report("Querying logs...");

            // Microsoft.Extensions.AI handles the agentic tool-calling loop automatically
            var response = await _chatClient.GetResponseAsync(messages, options);

            progress?.Report("Generating report...");

            return response.Text ?? "No response generated.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent error for query: {Query}", userQuery);
            return $"Investigation failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Streaming version - yields chunks as they arrive.
    /// </summary>
    public async IAsyncEnumerable<string> InvestigateStreamAsync(
        string userQuery,
        List<Models.ChatMessage> conversationHistory)
    {
        var splunkWebUrl = _config["SplunkSettings:WebUrl"] ?? "https://splunk.yourcompany.com:8000";

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, SystemPrompt + $"\n\nSplunk Web URL: {splunkWebUrl}")
        };

        foreach (var h in conversationHistory.TakeLast(10))
        {
            var role = h.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new(role, h.Content));
        }

        messages.Add(new(ChatRole.User, userQuery));

        var options = new ChatOptions
        {
            Tools = [.. _tools.GetTools()],
            ToolMode = ChatToolMode.Auto,
            Temperature = 0.2f
        };

        await foreach (var chunk in _chatClient.GetStreamingResponseAsync(messages, options))
        {
            if (!string.IsNullOrEmpty(chunk.Text))
                yield return chunk.Text;
        }
    }
}
