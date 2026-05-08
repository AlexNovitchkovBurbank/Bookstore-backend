using BookstoreAPI.Models.DTOs;
using BookstoreAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookstoreAPI.Controllers;

[ApiController]
[Route("api/books")]
[Produces("application/json")]
public class NaturalLanguageSearchController : ControllerBase
{
    private readonly INaturalLanguageSearchService _nlSearch;

    public NaturalLanguageSearchController(INaturalLanguageSearchService nlSearch)
    {
        _nlSearch = nlSearch;
    }

    /// <summary>
    /// Search for books using a plain-English description.
    /// The AI interprets the prompt and converts it into structured filters.
    /// </summary>
    /// <example>
    /// POST /api/books/natural-search
    /// { "prompt": "something spooky for a rainy evening, not too expensive" }
    /// </example>
    [HttpPost("natural-search")]
    [ProducesResponseType(typeof(NaturalLanguageSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> NaturalSearch([FromBody] NaturalLanguageSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt cannot be empty." });

        try
        {
            var result = await _nlSearch.SearchAsync(request);
            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            // Anthropic API is unreachable or returned an error
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error   = "AI search service is unavailable.",
                details = ex.Message
            });
        }
    }
}
