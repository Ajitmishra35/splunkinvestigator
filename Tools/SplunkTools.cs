using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SplunkInvestigator.Models;
using SplunkInvestigator.Services;

namespace SplunkInvestigator.Tools;

/// <summary>
/// Tools attached to the Splunk Investigator Agent.
/// Each method decorated with [Description] becomes an AITool via AIFunctionFactory.
/// This replaces MCP - tools read from local exported log files.
/// </summary>
public class SplunkTools
{
    private readonly LogFileService _logService;

    public SplunkTools(LogFileService logService)
    {
        _logService = logService;
    }

    /// <summary>
    /// Build the list of AIFunctions to pass to the chat client.
    /// </summary>
    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(SearchByTransactionRef);
        yield return AIFunctionFactory.Create(RunSplQuery);
        yield return AIFunctionFactory.Create(GetLogStatistics);
        yield return AIFunctionFactory.Create(GetErrorEvents);
        yield return AIFunctionFactory.Create(GetTransactionTimeline);
    }

    [Description("Search payment logs for a specific transaction reference number. Returns all log events related to that transaction in chronological order.")]
    private async Task<string> SearchByTransactionRef(
        [Description("The transaction reference number, e.g. TXN-98765")] string transactionRef)
    {
        var logs = await _logService.SearchByTransactionRefAsync(transactionRef);
        if (logs.Count == 0)
            return $"No logs found for transaction reference: {transactionRef}";

        return SerializeLogs(logs);
    }

    [Description("Run a SPL (Splunk Processing Language) style query against the payments index log files. Supports key=value filters like index=payments, level=ERROR, status=FAILED, event=FRAUD_DETECTION, and free-text search terms.")]
    private async Task<string> RunSplQuery(
        [Description("SPL query string, e.g. 'index=payments level=ERROR' or 'index=payments status=FAILED'")] string spl)
    {
        var logs = await _logService.SearchBySplQueryAsync(spl);
        if (logs.Count == 0)
            return $"No results found for SPL query: {spl}";

        return SerializeLogs(logs);
    }

    [Description("Get overall statistics about the loaded payment logs including total event count, error count, warning count, and unique transaction count.")]
    private async Task<string> GetLogStatistics()
    {
        return await _logService.GetLogStatisticsAsync();
    }

    [Description("Get all ERROR level events from the payments index to identify failures and issues.")]
    private async Task<string> GetErrorEvents()
    {
        var logs = await _logService.SearchBySplQueryAsync("index=payments level=ERROR");
        if (logs.Count == 0)
            return "No error events found in payments index.";

        return SerializeLogs(logs);
    }

    [Description("Get a full timeline of events for a transaction - shows every step from initiation to completion or failure, useful for understanding what happened.")]
    private async Task<string> GetTransactionTimeline(
        [Description("The transaction reference number, e.g. TXN-98765")] string transactionRef)
    {
        var logs = await _logService.SearchByTransactionRefAsync(transactionRef);
        if (logs.Count == 0)
            return $"No timeline data found for transaction: {transactionRef}";

        // Format as a clean timeline without sensitive data
        var timeline = logs.Select(l => new
        {
            time = l.Time,
            @event = l.Event,
            status = l.Status,
            level = l.Level,
            host = l.Host,
            message = l.Message,
            error_code = l.ErrorCode,
            attempt = l.Attempt
        });

        return JsonSerializer.Serialize(timeline, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SerializeLogs(List<SplunkLogEntry> logs)
    {
        // Strip sensitive fields before sending to AI
        var safe = logs.Select(l => new
        {
            time = l.Time,
            index = l.Index,
            host = l.Host,
            level = l.Level,
            transaction_ref = l.TransactionRef,
            @event = l.Event,
            status = l.Status,
            message = l.Message,
            error_code = l.ErrorCode,
            gateway = l.Gateway,
            currency = l.Currency,
            attempt = l.Attempt
            // Note: user_id, amount intentionally excluded from AI context
        });

        return JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true });
    }
}
