using System.Net;
using System.Text;
using System.Text.Json;
using BookstoreAPI.Models.DTOs;
using BookstoreAPI.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace BookstoreAPI.UnitTests;

/// <summary>
/// Unit tests for NaturalLanguageSearchService.
/// The Anthropic HTTP call and IBookService are both mocked so no
/// real network or database is required.
/// </summary>
public class NaturalLanguageSearchServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds the service with a mocked HTTP handler and optional book service mock.</summary>
    private static (NaturalLanguageSearchService sut,
                    Mock<HttpMessageHandler> httpHandler,
                    Mock<IBookService> bookService)
        Build(string anthropicJsonResponse)
    {
        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content    = new StringContent(anthropicJsonResponse, Encoding.UTF8, "application/json")
            });

        var httpClient        = new HttpClient(httpHandler.Object);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(httpClient);

        var bookService = new Mock<IBookService>();
        bookService
            .Setup(s => s.SearchAsync(It.IsAny<BookSearchDto>()))
            .ReturnsAsync(new PagedResult<BookResponseDto>(
                Array.Empty<BookResponseDto>(), 0, 1, 20, 0));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-api-key"
            })
            .Build();

        var logger = Mock.Of<ILogger<NaturalLanguageSearchService>>();

        var sut = new NaturalLanguageSearchService(
            bookService.Object, httpClientFactory.Object, config, logger);

        return (sut, httpHandler, bookService);
    }

    /// <summary>Wraps an AI filter JSON string in the Anthropic message response envelope.</summary>
    private static string AnthropicEnvelope(string innerJson) => JsonSerializer.Serialize(new
    {
        content = new[]
        {
            new { type = "text", text = innerJson }
        }
    });

    private static string FilterJson(
        string? query          = null,
        string? genre          = null,
        decimal? minPrice      = null,
        decimal? maxPrice      = null,
        string? sortBy         = null,
        bool sortDescending    = false) =>
        JsonSerializer.Serialize(new
        {
            query, genre, minPrice, maxPrice, sortBy, sortDescending
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // ExtractFiltersAsync — filter parsing
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExtractFilters_GenreAndMaxPrice_ParsedCorrectly()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(FilterJson(genre: "Horror", maxPrice: 15m)));

        var filters = await sut.ExtractFiltersAsync("something spooky and cheap");

        filters.Genre.Should().Be("Horror");
        filters.MaxPrice.Should().Be(15m);
        filters.MinPrice.Should().BeNull();
        filters.Query.Should().BeNull();
    }

    [Fact]
    public async Task ExtractFilters_KeywordAndSort_ParsedCorrectly()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(
            FilterJson(query: "Tolkien", sortBy: "publishedDate", sortDescending: true)));

        var filters = await sut.ExtractFiltersAsync("latest Tolkien books");

        filters.Query.Should().Be("Tolkien");
        filters.SortBy.Should().Be("publishedDate");
        filters.SortDescending.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractFilters_AllNullFields_ReturnsEmptyFilters()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(FilterJson()));

        var filters = await sut.ExtractFiltersAsync("just browsing");

        filters.Query.Should().BeNull();
        filters.Genre.Should().BeNull();
        filters.MinPrice.Should().BeNull();
        filters.MaxPrice.Should().BeNull();
        filters.SortBy.Should().BeNull();
        filters.SortDescending.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractFilters_PriceRange_BothEndsPopulated()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(
            FilterJson(minPrice: 10m, maxPrice: 30m)));

        var filters = await sut.ExtractFiltersAsync("mid-range books");

        filters.MinPrice.Should().Be(10m);
        filters.MaxPrice.Should().Be(30m);
    }

    [Fact]
    public async Task ExtractFilters_MarkdownFencesInResponse_StillParses()
    {
        // Some models occasionally wrap JSON in fences despite the instruction
        var withFences = "```json\n" + FilterJson(genre: "Fantasy") + "\n```";
        var (sut, _, _) = Build(AnthropicEnvelope(withFences));

        var filters = await sut.ExtractFiltersAsync("magical adventure");

        filters.Genre.Should().Be("Fantasy");
    }

    [Fact]
    public async Task ExtractFilters_MalformedJson_FallsBackToEmptyFilters()
    {
        var (sut, _, _) = Build(AnthropicEnvelope("this is not json at all"));

        var filters = await sut.ExtractFiltersAsync("anything");

        // Should not throw — falls back to empty filters
        filters.Should().NotBeNull();
        filters.Query.Should().BeNull();
        filters.Genre.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ExtractFiltersAsync — HTTP layer
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExtractFilters_SendsCorrectHeadersToAnthropic()
    {
        var (sut, httpHandler, _) = Build(AnthropicEnvelope(FilterJson()));

        await sut.ExtractFiltersAsync("test prompt");

        httpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Headers.Contains("x-api-key") &&
                req.Headers.Contains("anthropic-version")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExtractFilters_PostsToCorrectUrl()
    {
        var (sut, httpHandler, _) = Build(AnthropicEnvelope(FilterJson()));

        await sut.ExtractFiltersAsync("test prompt");

        httpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("anthropic.com")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExtractFilters_AnthropicReturnsError_ThrowsHttpRequestException()
    {
        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content    = new StringContent("invalid api key")
            });

        var httpClient        = new HttpClient(httpHandler.Object);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "bad-key" })
            .Build();

        var sut = new NaturalLanguageSearchService(
            Mock.Of<IBookService>(), httpClientFactory.Object, config,
            Mock.Of<ILogger<NaturalLanguageSearchService>>());

        var act = () => sut.ExtractFiltersAsync("anything");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SearchAsync — end-to-end through book service
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_PassesExtractedFiltersToBookService()
    {
        var (sut, _, bookService) = Build(AnthropicEnvelope(
            FilterJson(genre: "Mystery", maxPrice: 20m)));

        await sut.SearchAsync(new NaturalLanguageSearchRequest("dark detective novel"));

        bookService.Verify(s => s.SearchAsync(It.Is<BookSearchDto>(dto =>
            dto.Genre == "Mystery" &&
            dto.MaxPrice == 20m
        )), Times.Once);
    }

    [Fact]
    public async Task Search_ReturnsOriginalPromptInResponse()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(FilterJson()));

        var result = await sut.SearchAsync(new NaturalLanguageSearchRequest("a classic sci-fi novel"));

        result.OriginalPrompt.Should().Be("a classic sci-fi novel");
    }

    [Fact]
    public async Task Search_ReturnsInferredFiltersInResponse()
    {
        var (sut, _, _) = Build(AnthropicEnvelope(FilterJson(genre: "Science Fiction")));

        var result = await sut.SearchAsync(new NaturalLanguageSearchRequest("space adventure"));

        result.InferredFilters.Genre.Should().Be("Science Fiction");
    }

    [Fact]
    public async Task Search_RespectsPageAndPageSizeFromRequest()
    {
        var (sut, _, bookService) = Build(AnthropicEnvelope(FilterJson()));

        await sut.SearchAsync(new NaturalLanguageSearchRequest("anything", Page: 3, PageSize: 5));

        bookService.Verify(s => s.SearchAsync(It.Is<BookSearchDto>(dto =>
            dto.Page == 3 && dto.PageSize == 5
        )), Times.Once);
    }

    [Fact]
    public async Task Search_AiFilterFallback_StillCallsBookService()
    {
        // Even when AI returns bad JSON, SearchAsync should still call BookService
        // with empty/default filters rather than throwing
        var (sut, _, bookService) = Build(AnthropicEnvelope("not valid json"));

        await sut.SearchAsync(new NaturalLanguageSearchRequest("anything"));

        bookService.Verify(s => s.SearchAsync(It.IsAny<BookSearchDto>()), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Configuration
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_MissingApiKey_ThrowsInvalidOperationException()
    {
        var emptyConfig = new ConfigurationBuilder().Build();

        var act = () => new NaturalLanguageSearchService(
            Mock.Of<IBookService>(),
            Mock.Of<IHttpClientFactory>(),
            emptyConfig,
            Mock.Of<ILogger<NaturalLanguageSearchService>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Anthropic:ApiKey*");
    }
}
