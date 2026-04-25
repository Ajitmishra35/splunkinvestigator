using System.Text.Json;
using SplunkInvestigator.Models;

namespace SplunkInvestigator.Services;

/// <summary>
/// Reads exported Splunk log files from a local folder.
/// Supports JSON exports (the standard Splunk export format).
/// Drop any Splunk exported JSON file into the SampleLogs folder.
/// </summary>
public class LogFileService
{
    private readonly string _logsFolder;
    private readonly ILogger<LogFileService> _logger;
    private List<SplunkLogEntry>? _cachedLogs;

    public LogFileService(IConfiguration config, ILogger<LogFileService> logger)
    {
        _logger = logger;
        var folder = config["SplunkSettings:LogsFolder"] ?? "SampleLogs";
        _logsFolder = Path.IsPathRooted(folder)
            ? folder
            : Path.Combine(AppContext.BaseDirectory, folder);
    }

    /// <summary>
    /// Loads all log entries from all JSON files in the logs folder.
    /// Cached after first load.
    /// </summary>
    public async Task<List<SplunkLogEntry>> GetAllLogsAsync()
    {
        if (_cachedLogs is not null)
            return _cachedLogs;

        var allLogs = new List<SplunkLogEntry>();

        if (!Directory.Exists(_logsFolder))
        {
            _logger.LogWarning("Logs folder not found: {Folder}", _logsFolder);
            return allLogs;
        }

        // Read all JSON files - each can be a single export or array
        foreach (var file in Directory.GetFiles(_logsFolder, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var entries = JsonSerializer.Deserialize<List<SplunkLogEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (entries is not null)
                    allLogs.AddRange(entries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse log file: {File}", file);
            }
        }

        _cachedLogs = allLogs;
        _logger.LogInformation("Loaded {Count} log entries from {Folder}", allLogs.Count, _logsFolder);
        return allLogs;
    }

    /// <summary>
    /// Searches logs by transaction reference number (case-insensitive).
    /// </summary>
    public async Task<List<SplunkLogEntry>> SearchByTransactionRefAsync(string transactionRef)
    {
        var logs = await GetAllLogsAsync();
        return logs
            .Where(l => l.TransactionRef?.Contains(transactionRef, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(l => l.Time)
            .ToList();
    }

    /// <summary>
    /// Searches logs using a simple SPL-like query string.
    /// Supports: index=X, level=X, status=X, event=X, host=X, free-text search.
    /// </summary>
    public async Task<List<SplunkLogEntry>> SearchBySplQueryAsync(string spl)
    {
        var logs = await GetAllLogsAsync();
        var query = spl.ToLowerInvariant();

        // Parse key=value filters from SPL
        var filters = new Dictionary<string, string>();
        foreach (var part in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains('='))
            {
                var kv = part.Split('=', 2);
                var key = kv[0].Trim('"');
                var value = kv[1].Trim('"');
                filters[key] = value;
            }
        }

        var results = logs.AsEnumerable();

        // Apply structured filters
        if (filters.TryGetValue("index", out var idx))
            results = results.Where(l => l.Index?.Equals(idx, StringComparison.OrdinalIgnoreCase) == true);
        if (filters.TryGetValue("level", out var level))
            results = results.Where(l => l.Level?.Equals(level, StringComparison.OrdinalIgnoreCase) == true);
        if (filters.TryGetValue("status", out var status))
            results = results.Where(l => l.Status?.Equals(status, StringComparison.OrdinalIgnoreCase) == true);
        if (filters.TryGetValue("event", out var evt))
            results = results.Where(l => l.Event?.Equals(evt, StringComparison.OrdinalIgnoreCase) == true);
        if (filters.TryGetValue("host", out var host))
            results = results.Where(l => l.Host?.Equals(host, StringComparison.OrdinalIgnoreCase) == true);
        if (filters.TryGetValue("transaction_ref", out var txRef))
            results = results.Where(l => l.TransactionRef?.Contains(txRef, StringComparison.OrdinalIgnoreCase) == true);

        // Free-text: search in message field for any remaining terms
        var freeTerms = query.Split(' ')
            .Where(p => !p.Contains('=') && p.Length > 2)
            .ToList();
        foreach (var term in freeTerms)
            results = results.Where(l => l.Message?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);

        return results.OrderBy(l => l.Time).ToList();
    }

    /// <summary>
    /// Returns summary statistics about the loaded logs.
    /// </summary>
    public async Task<string> GetLogStatisticsAsync()
    {
        var logs = await GetAllLogsAsync();
        if (logs.Count == 0) return "No logs loaded.";

        var errorCount = logs.Count(l => l.Level?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true);
        var warnCount = logs.Count(l => l.Level?.Equals("WARN", StringComparison.OrdinalIgnoreCase) == true);
        var txRefs = logs.Select(l => l.TransactionRef).Where(r => r != null).Distinct().Count();

        return $"Total events: {logs.Count} | Errors: {errorCount} | Warnings: {warnCount} | Unique transactions: {txRefs}";
    }

    // Invalidate cache when user drops new files
    public void InvalidateCache() => _cachedLogs = null;
}
