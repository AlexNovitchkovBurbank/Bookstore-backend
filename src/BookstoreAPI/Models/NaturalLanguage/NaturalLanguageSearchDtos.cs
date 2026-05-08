namespace BookstoreAPI.Models.DTOs;

/// <summary>Request body for the natural language search endpoint.</summary>
public record NaturalLanguageSearchRequest(
    string Prompt,          // e.g. "something spooky for a rainy evening"
    int Page     = 1,
    int PageSize = 20
);

/// <summary>
/// Structured filters that the AI extracts from the natural language prompt.
/// All fields are optional — the AI only populates what it can infer.
/// </summary>
public record AiExtractedFilters(
    string? Query,
    string? Genre,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? SortBy,
    bool SortDescending = false
);

/// <summary>Search results plus the filters the AI inferred, for transparency.</summary>
public record NaturalLanguageSearchResponse(
    string OriginalPrompt,
    AiExtractedFilters InferredFilters,
    PagedResult<BookResponseDto> Results
);
