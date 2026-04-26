using System.Net;
using System.Net.Http.Json;
using BookstoreAPI.Models.DTOs;
using FluentAssertions;
using Xunit;

namespace BookstoreAPI.IntegrationTests;

/// <summary>
/// Integration tests that hit the real HTTP pipeline end-to-end.
/// The in-memory DB is shared within the class via IClassFixture.
/// </summary>
public class BooksControllerTests : IClassFixture<BookstoreWebAppFactory>
{
    private readonly HttpClient _client;

    public BooksControllerTests(BookstoreWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CreateBookDto MakeCreateDto(
        string title  = "Integration Test Book",
        string author = "Test Author",
        string isbn   = "",
        string genre  = "Fiction",
        decimal price = 19.99m,
        int stock     = 5) =>
        new(title, author,
            string.IsNullOrEmpty(isbn) ? $"978-{Guid.NewGuid():N}"[..13] : isbn,
            genre, price, stock, "Test description.", DateTime.UtcNow.AddYears(-1));

    private async Task<BookResponseDto> CreateBookAsync(CreateBookDto? dto = null)
    {

        var responseToAPI = await _client.GetAsync("/swagger/v1/swagger.json");
        Console.WriteLine(await responseToAPI.Content.ReadAsStringAsync());
        dto ??= MakeCreateDto();
        var response = await _client.PostAsJsonAsync("/api/books", dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookResponseDto>())!;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // POST /api/books
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_ValidBook_Returns201WithBody()
    {
        var dto = MakeCreateDto("Dune", "Frank Herbert", genre: "Sci-Fi", price: 14.99m);

        var response = await _client.PostAsJsonAsync("/api/books", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BookResponseDto>();
        body.Should().NotBeNull();
        body!.Id.Should().BeGreaterThan(0);
        body.Title.Should().Be("Dune");
        body.Price.Should().Be(14.99m);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Post_MissingRequiredFields_Returns400()
    {
        // Send an empty object — Title and Author are required
        var response = await _client.PostAsJsonAsync("/api/books", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/books/{id}
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_ExistingBook_Returns200()
    {
        var created = await CreateBookAsync();

        var response = await _client.GetAsync($"/api/books/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BookResponseDto>();
        body!.Id.Should().Be(created.Id);
        body.Title.Should().Be(created.Title);
    }

    [Fact]
    public async Task Get_NonExistentBook_Returns404()
    {
        var response = await _client.GetAsync("/api/books/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/books  (list)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAll_Returns200WithPagedResult()
    {
        await CreateBookAsync(MakeCreateDto("List Book A"));
        await CreateBookAsync(MakeCreateDto("List Book B"));

        var response = await _client.GetAsync("/api/books?pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/books/search
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_ByTitle_ReturnsMatchingBooks()
    {
        var unique = $"UniqueTitle-{Guid.NewGuid():N}";
        await CreateBookAsync(MakeCreateDto(unique));

        var response = await _client.GetAsync($"/api/books/search?query={unique}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.Items.Should().ContainSingle(b => b.Title == unique);
    }

    [Fact]
    public async Task Search_ByGenre_ReturnsOnlyThatGenre()
    {
        var uniqueGenre = $"Genre-{Guid.NewGuid():N}";
        await CreateBookAsync(MakeCreateDto(genre: uniqueGenre));
        await CreateBookAsync(MakeCreateDto(genre: uniqueGenre));

        var response = await _client.GetAsync($"/api/books/search?genre={uniqueGenre}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.Items.Should().HaveCount(2);
        body.Items.Should().AllSatisfy(b => b.Genre.Should().Be(uniqueGenre));
    }

    [Fact]
    public async Task Search_ByPriceRange_FiltersCorrectly()
    {
        var tag = Guid.NewGuid().ToString("N");
        await CreateBookAsync(MakeCreateDto($"Cheap-{tag}", price: 5m));
        await CreateBookAsync(MakeCreateDto($"Mid-{tag}",   price: 25m));
        await CreateBookAsync(MakeCreateDto($"Pricey-{tag}", price: 99m));

        var response = await _client.GetAsync($"/api/books/search?query={tag}&minPrice=10&maxPrice=50");

        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.Items.Should().ContainSingle();
        body.Items.First().Price.Should().Be(25m);
    }

    [Fact]
    public async Task Search_NoResults_ReturnsEmptyPagedResult()
    {
        var response = await _client.GetAsync("/api/books/search?query=xyzzy-no-match-ever");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.TotalCount.Should().Be(0);
        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_SortByPriceDescending_OrdersCorrectly()
    {
        var tag = Guid.NewGuid().ToString("N");
        await CreateBookAsync(MakeCreateDto($"A-{tag}", price: 10m));
        await CreateBookAsync(MakeCreateDto($"B-{tag}", price: 30m));
        await CreateBookAsync(MakeCreateDto($"C-{tag}", price: 20m));

        var response = await _client.GetAsync($"/api/books/search?query={tag}&sortBy=price&sortDescending=true");

        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.Items.Select(b => b.Price).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Search_Pagination_ReturnsCorrectPageSize()
    {
        var tag = Guid.NewGuid().ToString("N");
        for (var i = 1; i <= 6; i++)
            await CreateBookAsync(MakeCreateDto($"Paged-{tag}-{i}"));

        var response = await _client.GetAsync($"/api/books/search?query={tag}&page=1&pageSize=4");

        var body = await response.Content.ReadFromJsonAsync<PagedResult<BookResponseDto>>();
        body!.Items.Should().HaveCount(4);
        body.TotalCount.Should().Be(6);
        body.TotalPages.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /api/books/{id}
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Patch_ExistingBook_UpdatesFields()
    {
        var created = await CreateBookAsync();

        var patch = new UpdateBookDto("Updated Title", null, null, null, 99.99m, null, null, null);
        var response = await _client.PatchAsJsonAsync($"/api/books/{created.Id}", patch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BookResponseDto>();
        body!.Title.Should().Be("Updated Title");
        body.Price.Should().Be(99.99m);
        body.Author.Should().Be(created.Author); // unchanged
    }

    [Fact]
    public async Task Patch_NonExistentBook_Returns404()
    {
        var patch = new UpdateBookDto("Ghost", null, null, null, null, null, null, null);
        var response = await _client.PatchAsJsonAsync("/api/books/99999", patch);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DELETE /api/books/{id}
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_ExistingBook_Returns204AndRemovesIt()
    {
        var created = await CreateBookAsync();

        var deleteResponse = await _client.DeleteAsync($"/api/books/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/books/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistentBook_Returns404()
    {
        var response = await _client.DeleteAsync("/api/books/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
