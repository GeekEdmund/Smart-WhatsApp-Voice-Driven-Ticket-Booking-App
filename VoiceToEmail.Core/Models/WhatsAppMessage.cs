namespace VoiceToEmail.Core.Models;

public class WhatsAppMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int NumMedia { get; set; }
    public Dictionary<string, string> MediaUrls { get; set; } = new Dictionary<string, string>();
}



public class ConversationState
{
    public string PhoneNumber { get; set; } = string.Empty;
    public EventDetails? PendingEventDetails { get; set; }
    public bool WaitingForEmail { get; set; }
    public bool WaitingForTicketQuantity { get; set; }
    public bool AwaitingConfirmation { get; set; }
    public DateTime LastInteraction { get; set; } = DateTime.UtcNow;
    public string Venue { get; set; } = string.Empty;
    public string KickoffTime { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, decimal> TicketPrices { get; set; } = new();
    public Queue<string> SeatNumbers { get; set; } = new();
}