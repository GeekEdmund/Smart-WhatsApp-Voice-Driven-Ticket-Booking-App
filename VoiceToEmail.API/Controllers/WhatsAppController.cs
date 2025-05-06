using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml.Linq;
using VoiceToEmail.Core.Models;
using VoiceToEmail.Core.Interfaces;

namespace VoiceToEmail.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WhatsAppController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(
        IWhatsAppService whatsAppService,
        ILogger<WhatsAppController> logger)
    {
        _whatsAppService = whatsAppService ?? throw new ArgumentNullException(nameof(whatsAppService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public IActionResult Test()
    {
        _logger.LogInformation("Test endpoint hit at: {Time}", DateTime.UtcNow);
        return Ok(new { status = "success", message = "WhatsApp endpoint is working!" });
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Webhook([FromForm] Dictionary<string, string> form)
    {
        try
        {
            _logger.LogInformation("Webhook received at: {Time}", DateTime.UtcNow);
            
            if (form == null || !form.Any())
            {
                _logger.LogWarning("Empty or null form data received");
                return BadTwiMLResponse("Invalid request format.");
            }

            // Log incoming data securely (excluding sensitive information)
            LogFormData(form);

            // Validate required fields
            if (!ValidateRequiredFields(form, out string errorMessage))
            {
                _logger.LogWarning("Missing required fields: {ErrorMessage}", errorMessage);
                return BadTwiMLResponse(errorMessage);
            }

            var message = CreateWhatsAppMessage(form);

            try
            {
                // The response now comes from the WhatsAppService which should handle booking logic
                var response = await _whatsAppService.HandleIncomingMessageAsync(message);
                _logger.LogInformation("Message processed successfully for {From}", message.From);
                return TwiMLResponse(response);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TimeoutException)
            {
                _logger.LogError(ex, "Service communication error for {From}", message.From);
                return BadTwiMLResponse("We're experiencing technical difficulties. Please try again shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message for {From}", message.From);
                return BadTwiMLResponse("Sorry, we couldn't process your message. Please try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in webhook");
            return BadTwiMLResponse("An unexpected error occurred. Please try again later.");
        }
    }

    private WhatsAppMessage CreateWhatsAppMessage(Dictionary<string, string> form)
    {
        var message = new WhatsAppMessage
        {
            MessageSid = form.GetValueOrDefault("MessageSid", string.Empty),
            From = form.GetValueOrDefault("From", string.Empty),
            To = form.GetValueOrDefault("To", string.Empty),
            Body = form.GetValueOrDefault("Body", string.Empty)
        };

        if (int.TryParse(form.GetValueOrDefault("NumMedia", "0"), out int numMedia))
        {
            message.NumMedia = numMedia;
            ProcessMediaAttachments(form, message, numMedia);
        }

        return message;
    }

    private void ProcessMediaAttachments(Dictionary<string, string> form, WhatsAppMessage message, int numMedia)
    {
        for (int i = 0; i < numMedia; i++)
        {
            var mediaUrl = form.GetValueOrDefault($"MediaUrl{i}");
            var mediaContentType = form.GetValueOrDefault($"MediaContentType{i}");
            
            if (!string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(mediaContentType))
            {
                message.MediaUrls[mediaContentType] = mediaUrl;
                _logger.LogInformation("Media attachment processed - Type: {MediaType}, URL: {MediaUrl}", 
                    mediaContentType, mediaUrl);
            }
        }
    }

    private bool ValidateRequiredFields(Dictionary<string, string> form, out string errorMessage)
    {
        var requiredFields = new[] { "From", "To" };
        var missingFields = requiredFields.Where(field => !form.ContainsKey(field) || string.IsNullOrEmpty(form[field]));

        if (missingFields.Any())
        {
            errorMessage = $"Missing required fields: {string.Join(", ", missingFields)}";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private void LogFormData(Dictionary<string, string> form)
    {
        var sensitiveKeys = new[] { "AccountSid", "ApiKey", "ApiSecret" };
        
        foreach (var item in form.Where(x => !sensitiveKeys.Contains(x.Key)))
        {
            _logger.LogInformation("Form data - {Key}: {Value}", 
                item.Key, 
                item.Key.Contains("Media") ? "[Media Content]" : item.Value);
        }
    }

    private IActionResult TwiMLResponse(string message)
    {
        var response = new XDocument(
            new XElement("Response",
                new XElement("Message", new XCData(message))
            ));

        return Content(response.ToString(), "application/xml", Encoding.UTF8);
    }

    private IActionResult BadTwiMLResponse(string errorMessage)
    {
        return TwiMLResponse(errorMessage);
    }
}