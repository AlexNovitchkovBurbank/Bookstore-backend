using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookstoreAPI.Models.DTOs;

namespace BookstoreAPI.Services;

public class NaturalLanguageSearchService : INaturalLanguageSearchService
{
    private readonly IBookService _bookService;
    private readonly HttpClient   _http;
    private readonly ILogger<NaturalLanguageSearchService> _logger;
    private readonly string _apiKey;

    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model            = "claude-sonnet-4-20250514";

    // Prompt that instructs the model to return only JSON — no prose, no markdown fences.
    private const string SystemPrompt = """
        You are a bookstore search assistant. Your job is to extract structured search filters
        from a natural language query about books.

        Respond with ONLY a valid JSON object — no explanation, no markdown, no code fences.

        The JSON must follow this exact schema:
        {
          "query":          string or null,   // keyword to match title, author, or ISBN
          "genre":          string or null,   // e.g. "Fantasy", "Science Fiction", "Technology"
          "minPrice":       number or null,   // minimum price in USD
          "maxPrice":       number or null,   // maximum price in USD
          "sortBy":         string or null,   // one of: "title", "author", "price", "publishedDate"
          "sortDescending": boolean           // default false
        }

        Rules:
        - If the user implies "cheap" or "affordable", set maxPrice to 15.
        - If the user implies "new" or "recent", set sortBy to "publishedDate" and sortDescending to true.
        - If the user implies "expensive" or "premium", set minPrice to 30.
        - Map mood/theme words to a genre where reasonable (e.g. "scary" → "Horror", "space" → "Science Fiction").
        - If nothing can be inferred for a field, use null.
        - sortDescending must always be a boolean, never null.
        """;

    public NaturalLanguageSearchService(
        IBookService bookService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<NaturalLanguageSearchService> logger)
    {
        _bookService = bookService;
        _http        = httpClientFactory.CreateClient("Anthropic");
        _logger      = logger;
        _apiKey      = configuration["Anthropic:ApiKey"]
                       ?? throw new InvalidOperationException(
                           "Anthropic:ApiKey is not configured. Add it to appsettings or user secrets.");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<NaturalLanguageSearchResponse> SearchAsync(NaturalLanguageSearchRequest request)
    {
        var filters = await ExtractFiltersAsync(request.Prompt);

        var searchDto = new BookSearchDto(
            filters.Query,
            filters.Genre,
            filters.MinPrice,
            filters.MaxPrice,
            filters.SortBy,
            filters.SortDescending,
            request.Page,
            request.PageSize);

        var results = await _bookService.SearchAsync(searchDto);

        return new NaturalLanguageSearchResponse(request.Prompt, filters, results);
    }

    public async Task<AiExtractedFilters> ExtractFiltersAsync(string prompt)
    {
        var requestBody = new
        {
            model      = Model,
            max_tokens = 512,
            system     = SystemPrompt,
            messages   = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AnthropicApiUrl)
        {
            Content = content
        };
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        _logger.LogInformation("Sending natural language search prompt to Anthropic: {Prompt}", prompt);

        var response = await _http.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Anthropic API error {StatusCode}: {Error}", response.StatusCode, error);
            throw new HttpRequestException(
                $"Anthropic API returned {(int)response.StatusCode}: {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return ParseFiltersFromResponse(responseJson);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AiExtractedFilters ParseFiltersFromResponse(string responseJson)
    {
        try
        {
            var doc  = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            // Strip any accidental markdown fences
            text = text
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            var filters = JsonDocument.Parse(text).RootElement;

            return new AiExtractedFilters(
                Query:          GetStringOrNull(filters, "query"),
                Genre:          GetStringOrNull(filters, "genre"),
                MinPrice:       GetDecimalOrNull(filters, "minPrice"),
                MaxPrice:       GetDecimalOrNull(filters, "maxPrice"),
                SortBy:         GetStringOrNull(filters, "sortBy"),
                SortDescending: GetBoolOrDefault(filters, "sortDescending")
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response — falling back to empty filters. Response: {Response}", responseJson);
            return new AiExtractedFilters(null, null, null, null, null, false);
        }
    }

    private static string? GetStringOrNull(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static decimal? GetDecimalOrNull(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDecimal()
            : null;

    private static bool GetBoolOrDefault(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True;
}
