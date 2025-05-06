using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using VoiceToEmail.Core.Interfaces;
using VoiceToEmail.Core.Models;

namespace VoiceToEmail.API.Services;

public class AIAgentService : IAIAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIAgentService> _logger;
    private readonly Dictionary<string, ConversationContext> _conversationContexts;
    private readonly JsonSerializerOptions _jsonOptions;

    public AIAgentService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AIAgentService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _conversationContexts = new Dictionary<string, ConversationContext>();

        var apiKey = _configuration["OpenAI:ApiKey"] ?? 
            throw new ArgumentNullException("OpenAI:ApiKey configuration is missing");
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<AIAgentResponse> ProcessMessageAsync(string message, string userId)
    {
        try
        {
            var context = await GetConversationContextAsync(userId);
            var messages = BuildConversationMessages(context, message);

            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = messages,
                temperature = 0.7,
                max_tokens = 500,
                top_p = 1.0,
                frequency_penalty = 0.0,
                presence_penalty = 0.0
            };

            _logger.LogInformation("Sending request to OpenAI for user {UserId}", userId);

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API error: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);

            if (openAIResponse?.Choices == null || !openAIResponse.Choices.Any())
            {
                throw new Exception("No response choices received from OpenAI");
            }

            var aiResponse = await CreateAIResponse(openAIResponse, message);

            // Update conversation context
            await UpdateConversationContextAsync(userId, new ChatMessage 
            { 
                Role = "assistant",
                Content = aiResponse.Response 
            });

            return aiResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message with AI agent for user {UserId}", userId);
            throw;
        }
    }

    public async Task<string> TranslateMessageAsync(string message, string targetLanguage)
    {
        try
        {
            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = $"You are a translator. Translate the following text to {targetLanguage}." },
                    new { role = "user", content = message }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);

            return openAIResponse?.Choices?.FirstOrDefault()?.Message.Content ?? message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating message to {TargetLanguage}", targetLanguage);
            return message; // Return original message if translation fails
        }
    }

    public async Task<ConversationContext> GetConversationContextAsync(string userId)
    {
        return await Task.Run(() =>
        {
            lock (_conversationContexts)
            {
                if (!_conversationContexts.TryGetValue(userId, out var context))
                {
                    context = new ConversationContext
                    {
                        UserId = userId,
                        LastInteraction = DateTime.UtcNow,
                        History = new List<ChatMessage>(),
                        UserPreferences = new Dictionary<string, string>()
                    };
                    _conversationContexts[userId] = context;
                }
                return context;
            }
        });
    }

    public async Task UpdateConversationContextAsync(string userId, ChatMessage message)
    {
        try
        {
            lock (_conversationContexts)
            {
                if (_conversationContexts.TryGetValue(userId, out var context))
                {
                    // Keep only last 10 messages to manage memory
                    if (context.History.Count >= 10)
                    {
                        context.History.RemoveAt(0);
                    }
                    
                    context.History.Add(message);
                    context.LastInteraction = DateTime.UtcNow;
                }
            }
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation context for user {UserId}", userId);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> ExtractDataAsync(string message, List<string> dataPoints)
    {
        try
        {
            var promptContent = $"Extract the following information from the text: {string.Join(", ", dataPoints)}.\n\nText: {message}";
            
            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = "You are a data extraction assistant. Respond only with extracted data in JSON format." },
                    new { role = "user", content = promptContent }
                },
                temperature = 0.0,
                max_tokens = 500
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);

            var extractedDataJson = openAIResponse?.Choices?.FirstOrDefault()?.Message.Content;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(extractedDataJson ?? "{}", _jsonOptions) 
                   ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting data points from message");
            return new Dictionary<string, string>();
        }
    }

    private List<object> BuildConversationMessages(ConversationContext context, string newMessage)
    {
        var messages = new List<object>
        {
            new { role = "system", content = GetSystemPrompt() }
        };

        // Add relevant conversation history
        foreach (var historyMessage in context.History.TakeLast(5))
        {
            messages.Add(new { role = historyMessage.Role, content = historyMessage.Content });
        }

        // Add new message
        messages.Add(new { role = "user", content = newMessage });

        return messages;
    }

    private string GetSystemPrompt()
    {
        return @"You are an AI assistant helping with WhatsApp messages. Your role is to:
1. Understand and respond to user queries
2. Help process voice notes and messages
3. Assist with categorizing and prioritizing messages
4. Provide helpful and concise responses
5. Maintain a professional and friendly tone

If you detect any sensitive information, flag it appropriately.";
    }

    private async Task<AIAgentResponse> CreateAIResponse(OpenAIResponse openAIResponse, string originalMessage)
    {
        var responseContent = openAIResponse.Choices[0].Message.Content;
        
        // Await all async operations
        var detectedLanguage = await DetectLanguage(originalMessage);       
        var category = await DetermineCategory(responseContent);
        var suggestedActions = await GenerateSuggestedActions(responseContent);
        
        return new AIAgentResponse
        {
            Response = responseContent,
            DetectedLanguage = detectedLanguage,            
            Category = category,
            ExtractedData = new Dictionary<string, string>(),
            SuggestedActions = suggestedActions
        };
    }


    private async Task<string> DetectLanguage(string text)
    {
        try
        {
            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = "You are a language detection specialist. Respond only with the ISO 639-1 language code." },
                    new { role = "user", content = $"Detect the language of this text: {text}" }
                },
                temperature = 0.0,
                max_tokens = 10
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);
            var languageCode = openAIResponse?.Choices?.FirstOrDefault()?.Message.Content.Trim().ToLower() ?? "en";

            return languageCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting language");
            return "en"; // Default to English on error
        }
    }


    private async Task<string> DetermineCategory(string content)
    {
        try
        {
            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = @"Categorize this message into ONE of these categories:
    - event: Any requests related to tickets, matches, games, or sporting events
    - support: Customer support or technical issues
    - sales: Sales-related inquiries or opportunities
    - billing: Payment or invoice related
    - feedback: Customer feedback or suggestions
    - inquiry: General questions or information requests
    - complaint: Customer complaints or issues
    - general: Other general communication
    Respond with only the category name in lowercase." },
                    new { role = "user", content = content }
                },
                temperature = 0.0,
                max_tokens = 10
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);
            return openAIResponse?.Choices?.FirstOrDefault()?.Message.Content.Trim().ToLower() ?? "general";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining category");
            return "general";
        }
    }

    public async Task<EventDetails> ExtractEventDetailsFromMessage(string content)
    {
        try
        {
            var promptContent = @"Extract the following information from the text. If any field is not found, leave it as null or empty:
        - eventName (look for team names, match names)
        - requestedDate
        - fanName
        - fanEmail
        - ticketQuantity (default to 1 if not specified)
        - specialRequirements

        Respond in JSON format matching the following structure:
        {
            ""eventName"": """",
            ""requestedDate"": """",
            ""fanName"": """",
            ""fanEmail"": """",
            ""ticketQuantity"": 1,
            ""specialRequirements"": """"
        }";

            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = "You are a data extraction assistant. Extract event booking details from the text. Return only valid JSON with no markdown formatting." },
                    new { role = "user", content = $"{promptContent}\n\nText: {content}" }
                },
                temperature = 0.0,
                max_tokens = 500
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);
            var extractedDataJson = openAIResponse?.Choices?.FirstOrDefault()?.Message.Content;
            
            // Clean up the JSON string if it contains markdown formatting
            if (!string.IsNullOrEmpty(extractedDataJson))
            {
                // Remove markdown code blocks if present
                extractedDataJson = extractedDataJson
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();
            }
            
            var eventDetails = JsonSerializer.Deserialize<EventDetails>(extractedDataJson ?? "{}", _jsonOptions) 
                            ?? new EventDetails();

            // Ensure default values
            eventDetails.TicketQuantity = eventDetails.TicketQuantity == 0 ? 1 : eventDetails.TicketQuantity;
            eventDetails.RequestedDate = string.IsNullOrWhiteSpace(eventDetails.RequestedDate)
                ? DateTime.Now.AddDays(1).ToString("yyyy-MM-dd")
                : eventDetails.RequestedDate;
            
            return eventDetails;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting event details");
            return new EventDetails();
        }
    }

    private async Task<List<string>> GenerateSuggestedActions(string content)
    {
        try
        {
            var requestBody = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new { role = "system", content = @"Generate 1-3 suggested actions based on the message content.
    Rules:
    1. Each action should start with a verb
    2. Keep actions concise and actionable
    3. Format as JSON array of strings
    4. Consider message context and priority
    Example: ['Schedule follow-up call', 'Send pricing document']" },
                    new { role = "user", content = content }
                },
                temperature = 0.7,
                max_tokens = 150
            };

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, _jsonOptions);
            var actionsJson = openAIResponse?.Choices?.FirstOrDefault()?.Message.Content ?? "[]";
            
            return JsonSerializer.Deserialize<List<string>>(actionsJson, _jsonOptions) ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating suggested actions");
            return new List<string>();
        }
    }

    private class OpenAIResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = string.Empty;

        [JsonPropertyName("created")]
        public int Created { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        public Usage Usage { get; set; } = new();
    }

    private class Choice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = string.Empty;
    }

    private class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}