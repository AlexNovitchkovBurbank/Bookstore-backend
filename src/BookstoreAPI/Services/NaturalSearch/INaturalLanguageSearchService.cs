using BookstoreAPI.Models.DTOs;

namespace BookstoreAPI.Services;

public interface INaturalLanguageSearchService
{
    /// <summary>
    /// Translates a plain-English prompt into structured filters,
    /// then runs those filters against the book catalogue.
    /// </summary>
    Task<NaturalLanguageSearchResponse> SearchAsync(NaturalLanguageSearchRequest request);

    /// <summary>
    /// Calls the Anthropic API and returns the raw extracted filters.
    /// Exposed separately so it can be unit tested in isolation.
    /// </summary>
    Task<AiExtractedFilters> ExtractFiltersAsync(string prompt);
}
