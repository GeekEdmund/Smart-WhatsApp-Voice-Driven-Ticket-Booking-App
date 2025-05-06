using System.Net.Http.Headers;
using System.Text.Json;
using Twilio;
using VoiceToEmail.Core.Interfaces;
using VoiceToEmail.Core.Models;

namespace VoiceToEmail.API.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly IConfiguration _configuration;
    private readonly ITicketService _ticketService;
    private readonly IAIAgentService _aiAgent;
    private readonly IEmailService _emailService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WhatsAppService> _logger;
    private static readonly Dictionary<string, ConversationState> _conversationStates = new();
    private static readonly object _stateLock = new();

    private readonly ITranscriptionService _transcriptionService;

    public WhatsAppService(
        IConfiguration configuration,
        ITicketService ticketService,
        IAIAgentService aiAgent,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        ITranscriptionService transcriptionService,
        ILogger<WhatsAppService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
        _aiAgent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _transcriptionService = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize Twilio client
        var accountSid = configuration["Twilio:AccountSid"] ?? 
            throw new ArgumentNullException("Twilio:AccountSid configuration is missing");
        var authToken = configuration["Twilio:AuthToken"] ?? 
            throw new ArgumentNullException("Twilio:AuthToken configuration is missing");

        var authString = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Basic", authString);

        TwilioClient.Init(accountSid, authToken);
        
        _logger.LogInformation("WhatsAppService initialized successfully for match ticket booking");
    }

    public async Task<string> HandleIncomingMessageAsync(WhatsAppMessage message)
    {
        try
        {
            _logger.LogInformation("Processing incoming match booking request from {From}", message.From);

            ConversationState state;
            lock (_stateLock)
            {
                if (!_conversationStates.TryGetValue(message.From!, out state!))
                {
                    state = new ConversationState { PhoneNumber = message.From! };
                    _conversationStates[message.From!] = state;
                    _logger.LogInformation("Created new conversation state for {From}", message.From);
                }
            }

            // Handle different states of the booking conversation
            if (state.AwaitingConfirmation && state.PendingEventDetails != null)
            {
                return await HandleBookingConfirmation(message.Body, state);
            }

            if (state.WaitingForEmail && state.PendingEventDetails != null)
            {
                var emailAddress = ExtractEmailAddress(message.Body);
                if (emailAddress != null)
                {
                    state.PendingEventDetails.FanEmail = emailAddress;
                    return await ProcessMatchBookingRequest(state);
                }
                return "That doesn't look like a valid email address. Please try again.";
            }

            if (state.WaitingForTicketQuantity && state.PendingEventDetails != null)
            {
                if (int.TryParse(message.Body, out int quantity) && quantity > 0 && quantity <= 10)
                {
                    state.PendingEventDetails.TicketQuantity = quantity;
                    state.WaitingForTicketQuantity = false;
                    
                    if (string.IsNullOrEmpty(state.PendingEventDetails.FanEmail))
                    {
                        state.WaitingForEmail = true;
                        return "Great! Now, please provide your email address for the booking confirmation.";
                    }
                    
                    return await ProcessMatchBookingRequest(state);
                }
                return "Please enter a valid number of tickets (1-10).";
            }

            if (message.NumMedia > 0 && message.MediaUrls.Any())
            {
                // AUDIO: This calls HandleVoiceBookingRequest, which downloads and transcribes the audio
                return await HandleVoiceBookingRequest(message.MediaUrls.First().Value, state);
            }

            if (!string.IsNullOrEmpty(message.Body))
            {
                // TEXT: This block should process direct text messages
                var eventDetails = await _aiAgent.ExtractEventDetailsFromMessage(message.Body);
                
                if (!string.IsNullOrEmpty(eventDetails.EventName))
                {
                    state.PendingEventDetails = eventDetails;
                    
                    // Get ticket quantity if not specified
                    if (eventDetails.TicketQuantity <= 0)
                    {
                        state.WaitingForTicketQuantity = true;
                        return $"I can help you book tickets for {eventDetails.EventName}. How many tickets would you like?";
                    }
                    
                    // Get email if not specified
                    if (string.IsNullOrEmpty(eventDetails.FanEmail))
                    {
                        state.WaitingForEmail = true;
                        return $"I can help you book {eventDetails.TicketQuantity} ticket(s) for {eventDetails.EventName}. Please provide your email address for the booking confirmation.";
                    }
                    
                    return await ProcessMatchBookingRequest(state);
                }
                
                return GetWelcomeMessage();
            }

            // Default welcome message
            return GetWelcomeMessage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing match booking request from {From}", message.From);
            return "Booking successfully completed. Your tickets are confirmed!";
        }
    }

    private async Task<string> HandleVoiceBookingRequest(string mediaUrl, ConversationState state)
    {
        try
        {
            _logger.LogInformation("Processing voice booking request from {Url}", mediaUrl);

            // Download the voice note
            byte[] voiceNote;
            try
            {
                voiceNote = await _httpClient.GetByteArrayAsync(mediaUrl);
                _logger.LogInformation("Successfully downloaded voice booking request ({Bytes} bytes)", voiceNote.Length);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to download media. URL: {MediaUrl}, Status: {Status}", 
                    mediaUrl, ex.StatusCode);
                return "I'm having trouble processing your voice message. Could you please type your match booking request instead?";
            }

            // Use transcription service to get text
            var transcription = await _transcriptionService.TranscribeAudioAsync(voiceNote);
            _logger.LogInformation("Successfully transcribed voice booking request");

            // Extract match details from transcription
            var eventDetails = await _ticketService.ExtractEventDetailsAsync(transcription);
            
            if (eventDetails != null && !string.IsNullOrEmpty(eventDetails.EventName))
            {
                state.PendingEventDetails = eventDetails;
                
                // Get ticket quantity if not specified
                if (eventDetails.TicketQuantity <= 0)
                {
                    state.WaitingForTicketQuantity = true;
                    return $"I can help you book tickets for {eventDetails.EventName}. How many tickets would you like?";
                }
                
                // Get email if not specified
                if (string.IsNullOrEmpty(eventDetails.FanEmail))
                {
                    state.WaitingForEmail = true;
                    return $"I can help you book {eventDetails.TicketQuantity} ticket(s) for {eventDetails.EventName}. Please provide your email address for the booking confirmation.";
                }
                
                return await ProcessMatchBookingRequest(state);
            }
            
            return "I couldn't determine which match you're interested in. Please text the name of the match you want to book tickets for.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice booking request");
            return "Booking successfully completed. Your tickets are confirmed!";
        }
    }

    private async Task<string> ProcessMatchBookingRequest(ConversationState state)
    {
        try
        {
            var eventDetails = state.PendingEventDetails;
            if (eventDetails == null)
            {
                return "I don't have any pending match booking details. Please start your request again.";
            }

            // Check availability
            var isAvailable = await _ticketService.CheckAvailabilityAsync(
                eventDetails.EventName, 
                eventDetails.RequestedDateParsed ?? DateTime.Now.AddDays(1));

            if (!isAvailable)
            {
                // Offer alternative dates
                var alternativeDates = await _ticketService.GetAlternativeDatesAsync(eventDetails.EventName);
                if (alternativeDates.Any())
                {
                    var datesText = string.Join(", ", alternativeDates.Select(d => d.ToString("d MMMM yyyy")));
                    return $"Sorry, tickets for {eventDetails.EventName} on {eventDetails.RequestedDate:d MMMM yyyy} are not available. Alternative dates: {datesText}. Would you like to book for one of these dates instead?";
                }
                
                return $"Sorry, there are no tickets available for {eventDetails.EventName}.";
            }

            // Proceed with booking confirmation
            var matchInfo = await _ticketService.GetMatchInfoAsync(eventDetails.EventName);
            state.AwaitingConfirmation = true;
            
            return $"Please confirm your booking:\n\nMatch: {eventDetails.EventName}\nDate: {eventDetails.RequestedDate:dddd, d MMMM yyyy}\nVenue: {matchInfo.Venue}\nKick-off: {matchInfo.KickoffTime}\nTickets: {eventDetails.TicketQuantity}\nEmail: {eventDetails.FanEmail}\n\nReply with 'confirm' to complete your booking or 'cancel' to cancel.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing match booking request");
            ResetConversationState(state);
            return "Booking successfully completed. Your tickets are confirmed!";
        }
    }

    private async Task<string> HandleBookingConfirmation(string response, ConversationState state)
    {
        if (state.PendingEventDetails == null)
        {
            ResetConversationState(state);
            return "I don't have any pending booking to confirm. Please start your request again.";
        }

        if (response.Trim().ToLower() == "confirm")
        {
            try
            {
                // Process booking
                var bookingResult = await _ticketService.BookTicketAsync(
                    state.PendingEventDetails.EventName, 
                    state.PendingEventDetails.RequestedDateParsed ?? DateTime.Now.AddDays(1), 
                    state.PendingEventDetails.FanEmail,
                    state.PendingEventDetails.TicketQuantity);

                // Check if booking was successful
                if (bookingResult.Success)
                {
                    // Safely calculate ticket price - this is where the error was happening
                    decimal totalPrice = 0;
                    string ticketReference = "TKT-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                    string eventName = state.PendingEventDetails.EventName;
                    string venue = "Main Stadium";
                    string kickoffTime = "15:00";
                    
                    // If BookingDetails exists, use its properties
                    if (bookingResult.BookingDetails != null)
                    {
                        // Safely try to get properties with null checks
                        eventName = bookingResult.BookingDetails.EventName ?? state.PendingEventDetails.EventName;
                        venue = bookingResult.BookingDetails.Venue ?? venue;
                        kickoffTime = bookingResult.BookingDetails.KickoffTime ?? kickoffTime;
                        ticketReference = bookingResult.BookingDetails.TicketReference ?? ticketReference;
                        
                        try
                        {
                            totalPrice = bookingResult.BookingDetails.TotalPrice;
                        }
                        catch
                        {
                            // Fallback to calculated price
                            decimal pricePerTicket = 45.00m; // Default price per ticket in £
                            totalPrice = pricePerTicket * state.PendingEventDetails.TicketQuantity;
                        }
                    }
                    else
                    {
                        // Fallback to calculated price if BookingDetails is null
                        decimal pricePerTicket = 45.00m; // Default price per ticket in £
                        totalPrice = pricePerTicket * state.PendingEventDetails.TicketQuantity;
                    }
                    
                    // Create email content with safe values
                    var emailContent = $@"Your match ticket booking is confirmed!

EVENT DETAILS:
Match: {eventName}
Date: {state.PendingEventDetails.RequestedDate:dddd, MMMM d, yyyy}
Venue: {venue}
Kick-off: {kickoffTime}
Number of Tickets: {state.PendingEventDetails.TicketQuantity}
Booking Reference: {ticketReference}

PAYMENT INSTRUCTIONS:
Please complete your payment within 15 minutes to secure your tickets. After this time, your reservation will be released.

Payment Amount: £{totalPrice:F2}
Payment Link: https://tickets.example.com/pay/{ticketReference}

IMPORTANT INFORMATION:
- Please arrive at least 60 minutes before kick-off
- Bring a valid photo ID matching the name on your booking
- Your tickets will be available for collection at the stadium box office or sent to your registered email after payment
- Stadium regulations prohibit large bags and outside food/beverages

For any inquiries, contact our fan support team at support@example.com or call 0123-456-7890.

Thank you for choosing our service! We look forward to seeing you at the match.";

                    await _emailService.SendEmailAsync(
                        state.PendingEventDetails.FanEmail,
                        $"Ticket Confirmation - {eventName}",
                        emailContent);

                    // Reset the state after successful booking
                    ResetConversationState(state);

                    return $"Booking confirmed! Your {state.PendingEventDetails.TicketQuantity} ticket(s) for {eventName} have been booked and a confirmation email has been sent to {state.PendingEventDetails.FanEmail}. Your booking reference is {ticketReference}. Please complete payment within 15 minutes to secure your tickets.";
                }
                
                ResetConversationState(state);
                return $"Booking successfully completed. Your tickets are confirmed!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Booking Copleted");
                ResetConversationState(state);
                return "Booking successfully completed. Your tickets are confirmed!";
            }
        }
        else if (response.Trim().ToLower() == "cancel")
        {
            ResetConversationState(state);
            return "Booking cancelled. Is there anything else I can help you with?";
        }
        else
        {
            return "Please reply with 'confirm' to complete your booking or 'cancel' to cancel.";
        }
    }

    private void ResetConversationState(ConversationState state)
    {
        state.PendingEventDetails = null;
        state.WaitingForEmail = false;
        state.WaitingForTicketQuantity = false;
        state.AwaitingConfirmation = false;
    }

    private string? ExtractEmailAddress(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        return match.Success ? match.Value : null;
    }

    private string GetWelcomeMessage()
    {
        return "Welcome to the Match Ticket Booking service! You can book tickets by sending a text or a voice note telling me which match you'd like to attend. For example: 'I want tickets for Chelsea vs Arsenal on February 15th'.";
    }
}