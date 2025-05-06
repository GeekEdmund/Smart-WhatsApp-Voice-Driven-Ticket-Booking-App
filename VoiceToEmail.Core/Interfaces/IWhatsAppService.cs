using VoiceToEmail.Core.Models;

namespace VoiceToEmail.Core.Interfaces;

public interface IWhatsAppService
{
    Task<string> HandleIncomingMessageAsync(WhatsAppMessage message);
}