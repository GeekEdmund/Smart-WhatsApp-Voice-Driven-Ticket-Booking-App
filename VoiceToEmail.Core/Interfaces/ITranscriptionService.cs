// ITranscriptionService.cs
using VoiceToEmail.Core.Models;

namespace VoiceToEmail.Core.Interfaces;

public interface ITranscriptionService
{
    Task<string> TranscribeAudioAsync(byte[] audioData);
}

// IContentService.cs
public interface IContentService
{
    Task<string> EnhanceContentAsync(string transcribedText);
}

// IEmailService.cs
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string content);

    Task SendSoccerTicketEmailAsync(string toEmail, string subject, string body, object bookingDetails);

    Task SendSoccerTicketEmailAsync(string userEmail, string subject, string body, BookingDetails bookingDetails);

}