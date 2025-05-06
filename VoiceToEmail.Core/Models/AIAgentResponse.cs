using System.Text.Json.Serialization;

namespace VoiceToEmail.Core.Models;

public class AIAgentResponse
{
    public string Response { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; }
    public MessagePriority Priority { get; set; }
    public string Category { get; set; }
    public Dictionary<string, string> ExtractedData { get; set; }
    public List<string> SuggestedActions { get; set; }
}

public class ConversationContext
{
    public string UserId { get; set; }
    public List<ChatMessage> History { get; set; } = new();
    public string DetectedLanguage { get; set; }
    public Dictionary<string, string> UserPreferences { get; set; } = new();
    public DateTime LastInteraction { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string KickoffTime { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, decimal> TicketPrices { get; set; } = new();
    public Queue<string> SeatNumbers { get; set; } = new();
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }
    
    [JsonPropertyName("content")]
    public string Content { get; set; }
}

public enum MessagePriority
{
    Low,
    Medium,
    High,
    Urgent
}