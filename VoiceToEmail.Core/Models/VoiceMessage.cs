namespace VoiceToEmail.Core.Models;

public class VoiceMessage
{
    public Guid Id { get; set; }
    public string SenderEmail { get; set; }
    public string RecipientEmail { get; set; }
    public string AudioUrl { get; set; }
    public string TranscribedText { get; set; }
    public string EnhancedContent { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; }
}