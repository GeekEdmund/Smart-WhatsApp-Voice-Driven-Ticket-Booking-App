using VoiceToEmail.Core.Models;

namespace VoiceToEmail.Core.Interfaces;

public interface IAIAgentService
{
    Task<AIAgentResponse> ProcessMessageAsync(string message, string userId);
    Task<string> TranslateMessageAsync(string message, string targetLanguage);
    Task<ConversationContext> GetConversationContextAsync(string userId);
    Task UpdateConversationContextAsync(string userId, ChatMessage message);
    Task<Dictionary<string, string>> ExtractDataAsync(string message, List<string> dataPoints);
    Task<EventDetails> ExtractEventDetailsFromMessage(string message);
}

