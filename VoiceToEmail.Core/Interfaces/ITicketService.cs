using VoiceToEmail.Core.Models;

namespace VoiceToEmail.Core.Interfaces;

public interface ITicketService
{
    Task<EventDetails> ExtractEventDetailsAsync(string transcription);
    Task<bool> CheckAvailabilityAsync(string eventName, DateTime date);
    Task<TicketBookingResult> BookTicketAsync(string eventName, DateTime date, string userEmail, int quantity = 1);
    Task<List<DateTime>> GetAlternativeDatesAsync(string eventName);
    Task<List<string>> GetAvailableMatchesAsync();
    Task<EventInfo> GetMatchInfoAsync(string eventName);
}