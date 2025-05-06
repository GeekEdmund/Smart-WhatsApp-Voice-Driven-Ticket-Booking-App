using System.Text.Json.Serialization;
using VoiceToEmail.Core.Interfaces;

namespace VoiceToEmail.API.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly ILogger<TranscriptionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    
    public TranscriptionService(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<TranscriptionService> logger)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiKey = configuration["AssemblyAI:ApiKey"] ?? 
            throw new ArgumentNullException("AssemblyAI:ApiKey configuration is missing");
            
        _httpClient.DefaultRequestHeaders.Add("Authorization", _apiKey);
        _httpClient.BaseAddress = new Uri("https://api.assemblyai.com/v2/");
    }
    
    public async Task<string> TranscribeAudioAsync(byte[] audioData)
    {
        try
        {
            _logger.LogInformation("Starting audio transcription with AssemblyAI");
            
            // Upload the audio file
            using var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.Add("Content-Type", "application/octet-stream");
            
            var uploadResponse = await _httpClient.PostAsync("upload", audioContent);
            
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorContent = await uploadResponse.Content.ReadAsStringAsync();
                _logger.LogError("Upload failed with status {Status}. Response: {Response}", 
                    uploadResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Upload failed with status {uploadResponse.StatusCode}");
            }
            
            var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResponse>();
            
            if (uploadResult?.upload_url == null)
            {
                _logger.LogError("Upload response missing upload_url. Response: {Response}", 
                    await uploadResponse.Content.ReadAsStringAsync());
                throw new InvalidOperationException("Failed to get upload URL from response");
            }
            
            _logger.LogInformation("Audio file uploaded successfully. Creating transcription request");
            
            // Create transcription request
            var transcriptionRequest = new TranscriptionRequest
            {
                audio_url = uploadResult.upload_url,
                language_detection = true
            };
            
            var transcriptionResponse = await _httpClient.PostAsJsonAsync("transcript", transcriptionRequest);
            
            if (!transcriptionResponse.IsSuccessStatusCode)
            {
                var errorContent = await transcriptionResponse.Content.ReadAsStringAsync();
                _logger.LogError("Transcription request failed with status {Status}. Response: {Response}", 
                    transcriptionResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Transcription request failed with status {transcriptionResponse.StatusCode}");
            }
            
            var transcriptionResult = await transcriptionResponse.Content
                .ReadFromJsonAsync<TranscriptionResponse>();
                
            if (transcriptionResult?.id == null)
            {
                _logger.LogError("Transcription response missing ID. Response: {Response}", 
                    await transcriptionResponse.Content.ReadAsStringAsync());
                throw new InvalidOperationException("Failed to get transcript ID from response");
            }
            
            // Poll for completion
            int attempts = 0;
            const int maxAttempts = 60; // 1 minute timeout
            
            while (attempts < maxAttempts)
            {
                var pollingResponse = await _httpClient.GetAsync($"transcript/{transcriptionResult.id}");
                
                if (!pollingResponse.IsSuccessStatusCode)
                {
                    var errorContent = await pollingResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Polling failed with status {Status}. Response: {Response}", 
                        pollingResponse.StatusCode, errorContent);
                    throw new HttpRequestException($"Polling failed with status {pollingResponse.StatusCode}");
                }
                
                var pollingResult = await pollingResponse.Content
                    .ReadFromJsonAsync<TranscriptionResponse>();
                
                if (pollingResult?.status == "completed")
                {
                    if (string.IsNullOrEmpty(pollingResult.text))
                    {
                        throw new InvalidOperationException("Received empty transcription text");
                    }
                    
                    _logger.LogInformation("Transcription completed successfully");
                    return pollingResult.text;
                }
                
                if (pollingResult?.status == "error")
                {
                    var error = pollingResult.error ?? "Unknown error";
                    _logger.LogError("Transcription failed: {Error}", error);
                    throw new Exception($"Transcription failed: {error}");
                }
                
                _logger.LogInformation("Waiting for transcription to complete. Current status: {Status}", 
                    pollingResult?.status);
                    
                attempts++;
                await Task.Delay(1000);
            }
            
            throw new TimeoutException("Transcription timed out after 60 seconds");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during transcription");
            throw;
        }
    }
    
    private class UploadResponse
    {
        [JsonPropertyName("upload_url")]
        public string? upload_url { get; set; }
    }
    
    private class TranscriptionRequest
    {
        [JsonPropertyName("audio_url")]
        public string? audio_url { get; set; }
        
        [JsonPropertyName("language_detection")]
        public bool language_detection { get; set; }
    }
    
    private class TranscriptionResponse
    {
        [JsonPropertyName("id")]
        public string? id { get; set; }
        
        [JsonPropertyName("status")]
        public string? status { get; set; }
        
        [JsonPropertyName("text")]
        public string? text { get; set; }
        
        [JsonPropertyName("error")]
        public string? error { get; set; }
    }
}