using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BookstoreAPI.Models.DTOs;
using BookstoreAPI.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BookstoreAPI.IntegrationTests;

// ═══════════════════════════════════════════════════════════════════════════════
// Factory helpers
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Factory that replaces INaturalLanguageSearchService with a controllable mock,
/// so integration tests never hit the real Anthropic API.
/// </summary>
public class NlSearchWebAppFactory : WebApplicationFactory<Program>
{
    public Mock<INaturalLanguageSearchService> NlSearchMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real service
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(INaturalLanguageSearchService));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton(NlSearchMock.Object);
        });

        builder.UseEnvironment("Development");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Integration tests
// ═══════════════════════════════════════════════════════════════════════════════

public class NaturalLanguageSearchControllerTests : IClassFixture<NlSearchWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly Mock<INaturalLanguageSearchService> _nlMock;

    public NaturalLanguageSearchControllerTests(NlSearchWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _nlMock  = factory.NlSearchMock;
        _nlMock.Reset(); // isolate each test
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NaturalLanguageSearchResponse MakeResponse(
        string prompt,
        AiExtractedFilters? filters = null,
        IEnumerable<BookResponseDto>? books = null)
    {
        filters ??= new AiExtractedFilters(null, null, null, null, null, false);
        var items = (books ?? Array.Empty<BookResponseDto>()).ToList();
        var paged = new PagedResult<BookResponseDto>(items, items.Count, 1, 20,
            (int)Math.Ceiling(items.Count / 20.0));
        return new NaturalLanguageSearchResponse(prompt, filters, paged);
    }

    private static BookResponseDto MakeBook(int id = 1, string title = "Test Book",
        string genre = "Fiction", decimal price = 19.99m) =>
        new(id, title, "Author", "000", genre, price, 10, "Desc",
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, DateTime.UtcNow);

    // ═══════════════════════════════════════════════════════════════════════════
    // Happy path
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_ValidPrompt_Returns200WithResults()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("spooky books", books: [MakeBook(genre: "Horror")]));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "spooky books" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NaturalLanguageSearchResponse>();
        body.Should().NotBeNull();
        body!.OriginalPrompt.Should().Be("spooky books");
        body.Results.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Post_ValidPrompt_ReturnsInferredFilters()
    {
        var filters = new AiExtractedFilters("horror", "Horror", null, 15m, null, false);
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("cheap horror", filters));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "cheap horror" });

        var body = await response.Content.ReadFromJsonAsync<NaturalLanguageSearchResponse>();
        body!.InferredFilters.Genre.Should().Be("Horror");
        body.InferredFilters.MaxPrice.Should().Be(15m);
    }

    [Fact]
    public async Task Post_ValidPrompt_ForwardsPageAndPageSize()
    {
        _nlMock.Setup(s => s.SearchAsync(It.Is<NaturalLanguageSearchRequest>(r =>
                   r.Page == 2 && r.PageSize == 5)))
               .ReturnsAsync(MakeResponse("anything"))
               .Verifiable();

        await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "anything", page = 2, pageSize = 5 });

        _nlMock.Verify();
    }

    [Fact]
    public async Task Post_NoResults_Returns200WithEmptyItems()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("very obscure query"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "very obscure query" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NaturalLanguageSearchResponse>();
        body!.Results.Items.Should().BeEmpty();
        body.Results.TotalCount.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Validation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_EmptyPrompt_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WhitespacePrompt_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MissingBody_Returns400()
    {
        var response = await _client.PostAsync("/api/books/natural-search",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AI service failure → 502
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_AnthropicUnavailable_Returns502()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ThrowsAsync(new HttpRequestException("Anthropic API returned 503: service unavailable"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "any book" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Post_AnthropicUnavailable_ResponseBodyDescribesError()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ThrowsAsync(new HttpRequestException("connection refused"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "any book" });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("AI search service is unavailable");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Response shape
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_ValidPrompt_ResponseIsValidJson()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("test"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "test" });

        var body = await response.Content.ReadAsStringAsync();
        var act  = () => JsonDocument.Parse(body);
        act.Should().NotThrow("response must always be valid JSON");
    }

    [Fact]
    public async Task Post_ValidPrompt_ResponseContainsAllTopLevelFields()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("test"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "test" });

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("originalPrompt",  out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("inferredFilters", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("results",         out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_ValidPrompt_ContentTypeIsJson()
    {
        _nlMock.Setup(s => s.SearchAsync(It.IsAny<NaturalLanguageSearchRequest>()))
               .ReturnsAsync(MakeResponse("test"));

        var response = await _client.PostAsJsonAsync("/api/books/natural-search",
            new { prompt = "test" });

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
