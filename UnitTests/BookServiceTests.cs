using BookstoreAPI.Data;
using BookstoreAPI.Models;
using BookstoreAPI.Models.DTOs;
using BookstoreAPI.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookstoreAPI.UnitTests;

/// <summary>
/// Uses the EF Core In-Memory provider so no real database is needed.
/// Each test gets a fresh context via a unique DB name.
/// </summary>
public class BookServiceTests : IDisposable
{
    private readonly BookstoreDbContext _db;
    private readonly BookService _sut; // System Under Test

    public BookServiceTests()
    {
        var options = new DbContextOptionsBuilder<BookstoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
            .Options;

        _db  = new BookstoreDbContext(options);
        _sut = new BookService(_db);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Book> SeedBookAsync(
        string title  = "Test Book",
        string author = "Test Author",
        string genre  = "Fiction",
        decimal price = 19.99m,
        int stock     = 10)
    {
        var dto = new CreateBookDto(title, author, "978-0000000000", genre, price, stock, "A description.", DateTime.UtcNow.AddYears(-1));
        return (await _sut.CreateAsync(dto)) is { } r
            ? await _db.Books.FindAsync(r.Id) ?? throw new InvalidOperationException()
            : throw new InvalidOperationException();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GetByIdAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetById_ExistingId_ReturnsBook()
    {
        var book = await SeedBookAsync("The Great Gatsby");

        var result = await _sut.GetByIdAsync(book.Id);

        result.Should().NotBeNull();
        result!.Title.Should().Be("The Great Gatsby");
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(999);

        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CreateAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsBook()
    {
        var dto = new CreateBookDto(
            "Dune", "Frank Herbert", "978-0441013593",
            "Sci-Fi", 14.99m, 30, "Epic sci-fi.", new DateTime(1965, 8, 1));

        var result = await _sut.CreateAsync(dto);

        result.Id.Should().BeGreaterThan(0);
        result.Title.Should().Be("Dune");
        result.Author.Should().Be("Frank Herbert");
        result.Price.Should().Be(14.99m);

        var inDb = await _db.Books.FindAsync(result.Id);
        inDb.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_SetsCreatedAtAndUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await _sut.CreateAsync(
            new CreateBookDto("T", "A", "000", "G", 1m, 1, "D", DateTime.UtcNow));

        result.CreatedAt.Should().BeAfter(before);
        result.UpdatedAt.Should().BeAfter(before);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UpdateAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Update_ExistingBook_ChangesOnlySuppliedFields()
    {
        var book = await SeedBookAsync(price: 9.99m, stock: 5);

        var result = await _sut.UpdateAsync(book.Id, new UpdateBookDto(
            null, null, null, null, 24.99m, 100, null, null));

        result.Should().NotBeNull();
        result!.Price.Should().Be(24.99m);
        result.StockQuantity.Should().Be(100);
        result.Title.Should().Be(book.Title);   // unchanged
        result.Author.Should().Be(book.Author); // unchanged
    }

    [Fact]
    public async Task Update_NonExistentId_ReturnsNull()
    {
        var result = await _sut.UpdateAsync(999, new UpdateBookDto("X", null, null, null, null, null, null, null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Update_BumpsUpdatedAt()
    {
        var book = await SeedBookAsync();
        var originalUpdatedAt = book.UpdatedAt;

        await Task.Delay(10); // ensure clock moves
        await _sut.UpdateAsync(book.Id, new UpdateBookDto("New Title", null, null, null, null, null, null, null));

        var updated = await _db.Books.FindAsync(book.Id);
        updated!.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DeleteAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_ExistingBook_RemovesFromDb()
    {
        var book = await SeedBookAsync();

        var deleted = await _sut.DeleteAsync(book.Id);

        deleted.Should().BeTrue();
        var inDb = await _db.Books.FindAsync(book.Id);
        inDb.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistentId_ReturnsFalse()
    {
        var result = await _sut.DeleteAsync(999);

        result.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SearchAsync — query / filtering
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_ByTitleQuery_ReturnsMatchingBooks()
    {
        await SeedBookAsync("Clean Code");
        await SeedBookAsync("Clean Architecture");
        await SeedBookAsync("The Pragmatic Programmer");

        var result = await _sut.SearchAsync(new BookSearchDto("Clean", null, null, null, null));

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Search_ByAuthorQuery_ReturnsMatchingBooks()
    {
        await SeedBookAsync(author: "Robert Martin");
        await SeedBookAsync(author: "Frank Herbert");

        var result = await _sut.SearchAsync(new BookSearchDto("Robert", null, null, null, null));

        result.Items.Should().ContainSingle();
        result.Items.First().Author.Should().Be("Robert Martin");
    }

    [Fact]
    public async Task Search_ByGenre_ReturnsOnlyThatGenre()
    {
        await SeedBookAsync(genre: "Fantasy");
        await SeedBookAsync(genre: "Fantasy");
        await SeedBookAsync(genre: "Science Fiction");

        var result = await _sut.SearchAsync(new BookSearchDto(null, "Fantasy", null, null, null));

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(b => b.Genre.Should().Be("Fantasy"));
    }

    [Fact]
    public async Task Search_ByPriceRange_FiltersCorrectly()
    {
        await SeedBookAsync(price: 5.00m);
        await SeedBookAsync(price: 15.00m);
        await SeedBookAsync(price: 50.00m);

        var result = await _sut.SearchAsync(new BookSearchDto(null, null, 10m, 20m, null));

        result.Items.Should().ContainSingle();
        result.Items.First().Price.Should().Be(15.00m);
    }

    [Fact]
    public async Task Search_NoFilters_ReturnsAllBooks()
    {
        await SeedBookAsync("Book A");
        await SeedBookAsync("Book B");
        await SeedBookAsync("Book C");

        var result = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, null));

        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Search_QueryIsEmpty_ReturnsAllBooks()
    {
        await SeedBookAsync("X");
        await SeedBookAsync("Y");

        var result = await _sut.SearchAsync(new BookSearchDto("", null, null, null, null));

        result.TotalCount.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SearchAsync — sorting
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_SortByPriceAscending_OrdersCorrectly()
    {
        await SeedBookAsync(price: 30m);
        await SeedBookAsync(price: 10m);
        await SeedBookAsync(price: 20m);

        var result = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, "price", SortDescending: false));

        result.Items.Select(b => b.Price).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Search_SortByPriceDescending_OrdersCorrectly()
    {
        await SeedBookAsync(price: 30m);
        await SeedBookAsync(price: 10m);
        await SeedBookAsync(price: 20m);

        var result = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, "price", SortDescending: true));

        result.Items.Select(b => b.Price).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Search_SortByTitleAscending_OrdersCorrectly()
    {
        await SeedBookAsync("Zebra Book");
        await SeedBookAsync("Alpha Book");
        await SeedBookAsync("Mango Book");

        var result = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, "title"));

        result.Items.Select(b => b.Title).Should().BeInAscendingOrder();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SearchAsync — pagination
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_Pagination_ReturnsCorrectPage()
    {
        for (var i = 1; i <= 5; i++)
            await SeedBookAsync($"Book {i:D2}"); // "Book 01" … "Book 05"

        var page1 = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, "title", false, Page: 1, PageSize: 2));
        var page2 = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, "title", false, Page: 2, PageSize: 2));

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.Items.Should().NotIntersectWith(page2.Items);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task Search_LastPage_ReturnsRemainingItems()
    {
        for (var i = 1; i <= 5; i++)
            await SeedBookAsync($"Book {i}");

        var lastPage = await _sut.SearchAsync(new BookSearchDto(null, null, null, null, null, false, Page: 3, PageSize: 2));

        lastPage.Items.Should().HaveCount(1);
    }
}
