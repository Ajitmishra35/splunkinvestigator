using System.Text.Json.Serialization;

namespace SplunkInvestigator.Models;

// Represents one log entry from the exported Splunk JSON/CSV file
public class SplunkLogEntry
{
    [JsonPropertyName("_time")]
    public string? Time { get; set; }

    [JsonPropertyName("index")]
    public string? Index { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("transaction_ref")]
    public string? TransactionRef { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("amount")]
    public double? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("attempt")]
    public int? Attempt { get; set; }
}

// Chat message model for the UI
public class ChatMessage
{
    public string Role { get; set; } = "user"; // "user" | "assistant" | "tool"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsToolCall { get; set; }
    public string? ToolName { get; set; }
}

// Investigation result returned by tools
public class InvestigationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<SplunkLogEntry> Logs { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}
