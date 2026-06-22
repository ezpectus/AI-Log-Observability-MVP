using Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Infrastructure.AI;

public class HuggingFaceClient : IAiAnalysisService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _apiKey;
    private readonly string _modelUrl;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HuggingFaceClient>? _logger;

    public HuggingFaceClient(
        string apiKey,
        string modelUrl,
        HttpClient httpClient,
        ILogger<HuggingFaceClient>? logger = null)
    {
        _apiKey = apiKey;
        _modelUrl = modelUrl;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(string Analysis, string PatchCode)> SummarizeErrorAsync(string message, string? stackTrace)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_modelUrl))
            {
                _logger?.LogWarning("Hugging Face model URL is not configured");
                return CreateOfflineFallback(message, stackTrace);
            }

            using var request = CreateRequest(message, stackTrace);
            using var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "Hugging Face request failed with HTTP {StatusCode}: {Response}",
                    (int)response.StatusCode,
                    Truncate(responseJson, 500));
                return CreateOfflineFallback(message, stackTrace);
            }

            var generatedText = ExtractGeneratedText(responseJson);

            if (string.IsNullOrWhiteSpace(generatedText))
            {
                _logger?.LogWarning(
                    "Hugging Face returned HTTP 200 but no generated_text payload: {Response}",
                    Truncate(responseJson, 500));
                return CreateOfflineFallback(message, stackTrace);
            }

            generatedText = StripPromptEcho(generatedText);

            return TryParseStructuredAnalysis(generatedText, out var summary, out var suggestedPatch)
                ? (summary, suggestedPatch)
                : CreateOfflineFallback(message, stackTrace);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to generate AI analysis");
            return CreateOfflineFallback(message, stackTrace);
        }
    }

    private HttpRequestMessage CreateRequest(string message, string? stackTrace)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _modelUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(CreateRequestBody(message, stackTrace), SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        var token = NormalizeApiKey(_apiKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static object CreateRequestBody(string message, string? stackTrace)
    {
        var prompt = CreatePrompt(message, stackTrace);

        return new
        {
            inputs = prompt,
            parameters = new
            {
                max_new_tokens = 450,
                temperature = 0.15,
                return_full_text = false
            }
        };
    }

    private static string CreatePrompt(string message, string? stackTrace)
    {
        var stackTraceText = string.IsNullOrWhiteSpace(stackTrace)
            ? "No stack trace available"
            : stackTrace;

        return $$"""
<s>[INST]
You are an expert .NET production debugging assistant for an AI Log Observability dashboard.

Analyze the exception below and return exactly one valid JSON object. Do not include Markdown, prose, comments outside JSON, or conversational filler.

The JSON object must contain exactly these two string keys:
- "summary": A brief, professional, developer-friendly diagnostic explanation of why this error happens and what area to inspect.
- "suggestedPatch": Clean, copy-pastable C# code only. It must directly mitigate or prevent this specific failure, such as null checks, authorization checks, resilient HttpClient usage, timeout handling, streaming, pagination, or try/catch. Do not wrap it in Markdown fences.

Required response shape:
{"summary":"...","suggestedPatch":"..."}

Exception message:
{{message}}

Stack trace:
{{stackTraceText}}
[/INST]
""";
    }

    private static string NormalizeApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        var token = apiKey.Trim();
        const string bearerPrefix = "Bearer ";

        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[bearerPrefix.Length..].Trim()
            : token;
    }

    private static string ExtractGeneratedText(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return TryGetStringProperty(root, out var generatedText, "generated_text", "generatedText", "text")
                ? generatedText
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string StripPromptEcho(string generatedText)
    {
        var text = generatedText.Trim();
        var promptEndIndex = text.LastIndexOf("[/INST]", StringComparison.OrdinalIgnoreCase);

        if (promptEndIndex >= 0)
        {
            text = text[(promptEndIndex + "[/INST]".Length)..].Trim();
        }

        return text;
    }

    private static bool TryParseStructuredAnalysis(string generatedText, out string summary, out string suggestedPatch)
    {
        summary = string.Empty;
        suggestedPatch = string.Empty;

        var json = ExtractJsonObject(generatedText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            TryGetStringProperty(root, out summary, "summary", "Summary", "analysis", "Analysis");
            TryGetStringProperty(root, out suggestedPatch, "suggestedPatch", "SuggestedPatch", "patchCode", "PatchCode", "patch", "Patch");

            summary = CleanText(summary);
            suggestedPatch = CleanPatch(suggestedPatch);

            return !string.IsNullOrWhiteSpace(summary)
                && !string.IsNullOrWhiteSpace(suggestedPatch);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var cleaned = CleanText(text);
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');

        return start >= 0 && end > start
            ? cleaned[start..(end + 1)]
            : string.Empty;
    }

    private static bool TryGetStringProperty(JsonElement element, out string value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? string.Empty;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static (string Analysis, string PatchCode) CreateOfflineFallback(string message, string? stackTrace)
    {
        var text = $"{message}\n{stackTrace}".ToLowerInvariant();

        if (text.Contains("unauthorizedaccessexception"))
        {
            return (
                "The application attempted to access a secured resource without proper credentials, invalid tokens, or insufficient OS-level file permissions.",
                """
[Authorize]
public async Task<IActionResult> GetSecureData()
{
    var user = HttpContext.User;
    if (user?.Identity?.IsAuthenticated != true)
    {
        return Unauthorized();
    }

    return Ok();
}
""");
        }

        if (text.Contains("outofmemoryexception"))
        {
            return (
                "The system ran out of allocatable memory. This usually indicates a memory leak, unmanaged resource leakage, or loading a dataset that is too large into RAM.",
                """
var users = await _dbContext.Users
    .AsNoTracking()
    .Skip(page * size)
    .Take(size)
    .ToListAsync();
""");
        }

        if (text.Contains("httprequestexception") || text.Contains("timeout") || text.Contains("dns"))
        {
            return (
                "An inbound or outbound HTTP network request failed. The remote server might be offline, DNS resolution failed, or a network timeout occurred.",
                """
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    var response = await _httpClient.GetAsync("https://api.external.com/data", cts.Token);
    response.EnsureSuccessStatusCode();
}
catch (HttpRequestException ex)
{
    _logger.LogWarning(ex, "External API request failed");
}
catch (TaskCanceledException ex) when (!cts.IsCancellationRequested)
{
    _logger.LogWarning(ex, "External API request timed out");
}
""");
        }

        return (
            "The application hit an unhandled runtime exception. Inspect the failing method inputs, validate nullable values before use, and add focused exception handling around the risky operation.",
            """
try
{
    if (request is null)
    {
        return BadRequest("Request payload is required.");
    }

    var result = await _service.ProcessAsync(request);
    return Ok(result);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process request");
    return StatusCode(StatusCodes.Status500InternalServerError, "Request processing failed.");
}
""");
    }

    private static string CleanText(string text)
    {
        return text.Trim();
    }

    private static string CleanPatch(string patch)
    {
        var cleaned = patch.Trim();

        if (!cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            return cleaned;
        }

        var firstLineEnd = cleaned.IndexOf('\n');
        if (firstLineEnd >= 0)
        {
            cleaned = cleaned[(firstLineEnd + 1)..].Trim();
        }

        return cleaned.EndsWith("```", StringComparison.Ordinal)
            ? cleaned[..^3].Trim()
            : cleaned;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
