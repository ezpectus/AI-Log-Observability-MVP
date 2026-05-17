using Application.Interfaces;
using System.Text.Json;

namespace Infrastructure.AI;

public class HuggingFaceClient : IAiAnalysisService
{
    private readonly string _apiKey;
    private readonly string _modelUrl;
    private readonly HttpClient _httpClient;

    public HuggingFaceClient(string apiKey, string modelUrl, HttpClient httpClient)
    {
        _apiKey = apiKey;
        _modelUrl = modelUrl;
        _httpClient = httpClient;
    }

    public async Task<(string Analysis, string PatchCode)> SummarizeErrorAsync(string message, string? stackTrace)
    {
        try
        {
            var stackTraceText = stackTrace ?? "No stack trace available";
            var prompt = $"<s>[INST] You are a Senior DevOps and SRE expert. Analyze this error log and stack trace. Provide your response strictly in JSON format with two fields: \"Analysis\" (a clear explanation of the root cause and the fix in English, max 2 sentences) and \"PatchCode\" (a corrected C# code snippet that solves this problem, e.g., wrapped in try-catch, null check, or proper HttpClient usage). Output nothing except the JSON. Log: {message}\nStack trace: {stackTraceText} [/INST]";

            var requestBody = new
            {
                inputs = prompt,
                parameters = new
                {
                    max_new_tokens = 300,
                    temperature = 0.3,
                    return_full_text = false
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.PostAsync(_modelUrl, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var responses = JsonSerializer.Deserialize<List<HuggingFaceResponse>>(responseJson);

            if (responses != null && responses.Count > 0)
            {
                var generatedText = responses[0].GeneratedText;
                var promptIndex = generatedText.IndexOf("[/INST]");
                if (promptIndex >= 0)
                {
                    generatedText = generatedText.Substring(promptIndex + 7).Trim();
                }

                // Try to parse JSON response
                try
                {
                    var jsonDoc = JsonDocument.Parse(generatedText);
                    var analysis = jsonDoc.RootElement.GetProperty("Analysis").GetString() ?? "AI analysis temporarily unavailable";
                    var patchCode = jsonDoc.RootElement.GetProperty("PatchCode").GetString() ?? "// Patch not generated";
                    return (analysis, patchCode);
                }
                catch
                {
                    // Fallback if JSON parsing fails
                    return (generatedText, "// Patch not generated");
                }
            }

            return ("AI analysis temporarily unavailable. Check server logs.", "// Patch not generated");
        }
        catch
        {
            return ("AI analysis temporarily unavailable. Check server logs.", "// Patch not generated");
        }
    }

    private class HuggingFaceResponse
    {
        public string GeneratedText { get; set; } = string.Empty;
    }
}
