using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceToEmail.Core.Interfaces;
using VoiceToEmail.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System.IO;
using SixLabors.ImageSharp; // For logo images
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace VoiceToEmail.API.Services;

public class TicketService : ITicketService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TicketService> _logger;
    private readonly IEmailService _emailService;
    
    // Enhanced mock database with more matches and details
    private static readonly Dictionary<string, EventInfo> _matchDatabase = new()
    {
        { "Chelsea vs Arsenal", new EventInfo { 
            Date = DateTime.Parse("2025-02-15"), 
            AvailableSeats = 50,
            Venue = "Stamford Bridge",
            KickoffTime = "15:00",
            Category = "Premier League",
            TicketPrices = new Dictionary<string, decimal> {
                { "Standard", 60.00m },
                { "Premium", 120.00m }
            },
            SeatNumbers = new Queue<string>(Enumerable.Range(1, 50).Select(i => $"A{i}")) // Example: A1, A2, ..., A50
        }},
        { "Manchester United vs Liverpool", new EventInfo { 
            Date = DateTime.Parse("2025-03-12"), 
            AvailableSeats = 35,
            Venue = "Old Trafford",
            KickoffTime = "17:30",
            Category = "Premier League",
            TicketPrices = new Dictionary<string, decimal> {
                { "Standard", 70.00m },
                { "Premium", 150.00m }
            },
            SeatNumbers = new Queue<string>(Enumerable.Range(1, 35).Select(i => $"B{i}")) // Example: B1, B2, ..., B35
        }},
        { "Arsenal vs Tottenham", new EventInfo { 
            Date = DateTime.Parse("2025-04-05"), 
            AvailableSeats = 40,
            Venue = "Emirates Stadium",
            KickoffTime = "12:30",
            Category = "Premier League",
            TicketPrices = new Dictionary<string, decimal> {
                { "Standard", 65.00m },
                { "Premium", 130.00m }
            },
            SeatNumbers = new Queue<string>(Enumerable.Range(1, 40).Select(i => $"C{i}")) // Example: C1, C2, ..., C40
        }},
        { "Manchester City vs Chelsea", new EventInfo { 
            Date = DateTime.Parse("2025-03-22"), 
            AvailableSeats = 45,
            Venue = "Etihad Stadium",
            KickoffTime = "14:00",
            Category = "Premier League",
            TicketPrices = new Dictionary<string, decimal> {
                { "Standard", 68.00m },
                { "Premium", 140.00m }
            },
            SeatNumbers = new Queue<string>(Enumerable.Range(1, 45).Select(i => $"D{i}")) // Example: D1, D2, ..., D45
        }}
    };

    public class OpenAiResponse
    {
        public List<Choice> Choices { get; set; }
    }

    public class Choice
    {
        public Message Message { get; set; }
    }

    public class Message
    {
        public string Content { get; set; }
    }

    // Custom model for mapping OpenAI response properties to EventDetails
    public class EventDetailsDto
    {
        [JsonPropertyName("Event name")]
        public string EventName { get; set; }
        
        [JsonPropertyName("Requested date")]
        public string RequestedDate { get; set; }
        
        [JsonPropertyName("Fan's name")]
        public string FanName { get; set; }
        
        [JsonPropertyName("Fan's email")]
        public string FanEmail { get; set; }
        
        [JsonPropertyName("Number of tickets requested")]
        public int TicketQuantity { get; set; }
        
        [JsonPropertyName("Special requirements")]
        public string SpecialRequirements { get; set; }
        
        [JsonPropertyName("Ticket type")]
        public string TicketType { get; set; }
    }

    public TicketService(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<TicketService> logger,
        IEmailService emailService)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<EventDetails> ExtractEventDetailsAsync(string transcription)
    {
        try
        {
            var openAiKey = _configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", openAiKey);

            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new 
                    { 
                        role = "system",
                        content = "You are a helpful assistant that extracts information from text. Always respond with only valid JSON with no markdown formatting, code blocks, or explanations."
                    },
                    new 
                    { 
                        role = "user", 
                        content = $@"Extract the following information from this text for a football match ticket booking. Use the exact property names in the response JSON:
                        - ""Event name"": The match name with format 'Team A vs Team B'
                        - ""Requested date"": If specific date mentioned
                        - ""Fan's name"": The person's name
                        - ""Fan's email"": Email address if mentioned (ensure it has an @ symbol)
                        - ""Number of tickets requested"": Default to 1 if not specified
                        - ""Special requirements"": Any accessibility needs, seating preferences
                        - ""Ticket type"": Standard or premium, default to standard if not specified

                        Text: {transcription}" 
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions", 
                requestBody);

            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Raw OpenAI response: {response}", responseContent);
            
            var options = new JsonSerializerOptions { 
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = null // Use exact property names
            };
            
            var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(responseContent, options);
            
            if (openAiResponse?.Choices == null || openAiResponse.Choices.Count == 0)
            {
                throw new Exception("No response content from OpenAI API");
            }
            
            var jsonContent = openAiResponse.Choices[0].Message.Content;
            _logger.LogInformation("OpenAI extracted content: {Content}", jsonContent);
            
            // Clean the content from any markdown code blocks if present
            jsonContent = CleanJsonContent(jsonContent);
            
            // Parse the JSON string from OpenAI's response with custom EventDetailsDto
            var dto = JsonSerializer.Deserialize<EventDetailsDto>(jsonContent, options);
            
            // Map to our EventDetails model
            var eventDetails = new EventDetails
            {
                EventName = dto?.EventName ?? string.Empty,
                RequestedDate = string.IsNullOrEmpty(dto?.RequestedDate) ? 
                    GetDefaultMatchDate(dto?.EventName ?? string.Empty).ToString("yyyy-MM-dd") : 
                    ParseDate(dto.RequestedDate).ToString("yyyy-MM-dd"),
                FanName = dto?.FanName ?? string.Empty,
                FanEmail = FixEmailFormat(dto?.FanEmail ?? string.Empty),
                TicketQuantity = dto?.TicketQuantity <= 0 ? 1 : dto.TicketQuantity,
                SpecialRequirements = dto?.SpecialRequirements ?? string.Empty,
                TicketType = string.IsNullOrEmpty(dto?.TicketType) ? "Standard" : dto.TicketType
            };
            
            return eventDetails;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting event details from transcription");
            return new EventDetails();
        }
    }

    private DateTime ParseDate(string dateString)
    {
        if (DateTime.TryParse(dateString, out DateTime parsedDate))
        {
            return parsedDate;
        }
        
        // Try different formats if standard parsing fails
        string[] formats = { "MMM d yyyy", "MMMM d yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy" };
        if (DateTime.TryParseExact(dateString, formats, null, System.Globalization.DateTimeStyles.None, out parsedDate))
        {
            return parsedDate;
        }
        
        return DateTime.Now.AddDays(7); // Default fallback
    }

    private string FixEmailFormat(string email)
    {
        if (string.IsNullOrEmpty(email))
            return string.Empty;
            
        // Check if @ is missing but there's a domain part
        if (!email.Contains("@") && (email.Contains(".com") || email.Contains(".net") || email.Contains(".org")))
        {
            // Find the domain part
            int domainStart = email.IndexOf('.') - 4; // Approximate position before .com/.net/etc.
            if (domainStart > 0)
            {
                return email.Insert(domainStart, "@");
            }
        }
        
        return email;
    }

    private DateTime GetDefaultMatchDate(string matchName)
    {
        if (_matchDatabase.TryGetValue(matchName, out var eventInfo))
        {
            return eventInfo.Date;
        }
        return DateTime.Now.AddDays(7);
    }

    private string CleanJsonContent(string content)
    {
        // Remove markdown code block formatting if present
        if (content.StartsWith("```") && content.EndsWith("```"))
        {
            // Extract content between first and last ``` markers
            var firstIndex = content.IndexOf('\n');
            var lastIndex = content.LastIndexOf("```");
            
            if (firstIndex > 0 && lastIndex > firstIndex)
            {
                content = content.Substring(firstIndex, lastIndex - firstIndex).Trim();
            }
        }
        
        // Remove any starting/ending ``` and json tag if present
        content = content.Replace("```json", "").Replace("```", "").Trim();
        
        return content;
    }

    public async Task<bool> CheckAvailabilityAsync(string eventName, DateTime date)
    {
        return await Task.Run(() => {
            if (_matchDatabase.TryGetValue(eventName, out var eventInfo))
            {
                return eventInfo.AvailableSeats > 0 && eventInfo.Date.Date == date.Date;
            }
            return false;
        });
    }

    public async Task<EventInfo> GetMatchInfoAsync(string eventName)
    {
        if (_matchDatabase.TryGetValue(eventName, out var eventInfo))
        {
            return eventInfo;
        }
        throw new Exception($"Match '{eventName}' not found");
    }

    public async Task<List<DateTime>> GetAlternativeDatesAsync(string eventName)
    {
        // In a real application, this would query a database for alternative dates
        // Here we're simulating with hardcoded alternatives
        return await Task.Run(() => {
            if (eventName.Contains("Chelsea") && eventName.Contains("Arsenal"))
            {
                return new List<DateTime> { 
                    DateTime.Parse("2025-05-10"), 
                    DateTime.Parse("2025-08-22") 
                };
            }
            else if (eventName.Contains("Manchester United") && eventName.Contains("Liverpool"))
            {
                return new List<DateTime> { 
                    DateTime.Parse("2025-04-15"), 
                    DateTime.Parse("2025-07-30") 
                };
            }
            return new List<DateTime>();
        });
    }

    public async Task<List<string>> GetAvailableMatchesAsync()
    {
        return await Task.Run(() => {
            return _matchDatabase.Where(m => m.Value.AvailableSeats > 0)
                                .Select(m => m.Key)
                                .ToList();
        });
    }

    public async Task<TicketBookingResult> BookTicketAsync(string eventName, DateTime date, string userEmail, int quantity = 1)
    {
        try
        {
            if (!_matchDatabase.TryGetValue(eventName, out var eventInfo))
            {
                return new TicketBookingResult
                {
                    Success = false,
                    Message = "Match not found"
                };
            }

            if (eventInfo.AvailableSeats < quantity)
            {
                return new TicketBookingResult
                {
                    Success = false,
                    Message = $"Not enough tickets available. Only {eventInfo.AvailableSeats} tickets left."
                };
            }

            if (!string.IsNullOrEmpty(userEmail) && !userEmail.Contains('@'))
            {
                return new TicketBookingResult
                {
                    Success = false,
                    Message = "Invalid email format. Please provide a valid email address."
                };
            }

            // Dummy payment check (simulate payment confirmation)
            bool paymentConfirmed = DummyPaymentService.ConfirmPayment(userEmail, eventName, quantity);
            if (!paymentConfirmed)
            {
                return new TicketBookingResult
                {
                    Success = false,
                    Message = "Payment not confirmed. Please complete your payment to receive your ticket."
                };
            }

            // Assign seat numbers
            var assignedSeats = new List<string>();
            for (int i = 0; i < quantity; i++)
            {
                if (eventInfo.SeatNumbers.Count > 0)
                    assignedSeats.Add(eventInfo.SeatNumbers.Dequeue());
                else
                    assignedSeats.Add("Unassigned");
            }

            eventInfo.AvailableSeats -= quantity;

            var bookingDetails = new BookingDetails
            {
                EventName = eventName,
                Date = date,
                Venue = eventInfo.Venue,
                KickoffTime = eventInfo.KickoffTime,
                TicketReference = GenerateTicketReference(),
                UserEmail = userEmail,
                Quantity = quantity,
                Category = eventInfo.Category,
                BookingTime = DateTime.UtcNow,
                TotalPrice = eventInfo.TicketPrices["Standard"] * quantity,
                SeatNumbers = assignedSeats // Add this property to BookingDetails
            };

            bookingDetails.RequestedDate = eventInfo.Date.ToString("yyyy-MM-dd");

            // Only send email if payment is confirmed
            await _emailService.SendSoccerTicketEmailAsync(
                userEmail,
                "Your Soccer Match Ticket",
                $"Please find your ticket attached. Amount paid: £{bookingDetails.TotalPrice:F2}. Seats: {string.Join(", ", assignedSeats)}",
                bookingDetails);

            return new TicketBookingResult
            {
                Success = true,
                Message = "Tickets booked successfully!",
                BookingDetails = bookingDetails
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking ticket");
            throw;
        }
    }

    private string GenerateTicketReference()
    {
        return $"MATCH-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
    }
}

public static class DummyPaymentService
{
    public static bool ConfirmPayment(string userEmail, string eventName, int quantity)
    {
        // Always returns true for demo; replace with real logic as needed
        return true;
    }
}