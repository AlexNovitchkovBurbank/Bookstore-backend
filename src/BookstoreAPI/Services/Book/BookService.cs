using BookstoreAPI.Data;
using BookstoreAPI.Models;
using BookstoreAPI.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BookstoreAPI.Services;

public class BookService : IBookService
{
    private readonly BookstoreDbContext _db;

    public BookService(BookstoreDbContext db)
    {
        _db = db;
    }

    // ──────────────────────────────────────────
    // GET by ID
    // ──────────────────────────────────────────
    public async Task<BookResponseDto?> GetByIdAsync(int id)
    {
        var book = await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        return book is null ? null : MapToResponse(book);
    }

    // ──────────────────────────────────────────
    // SEARCH  (with pagination + sorting)
    // ──────────────────────────────────────────
    public async Task<PagedResult<BookResponseDto>> SearchAsync(BookSearchDto dto)
    {
        IQueryable<Book> books = _db.Books.AsNoTracking();

        // Full-text filter across title, author, ISBN
        if (!string.IsNullOrWhiteSpace(dto.Query))
        {
            var q = dto.Query.ToLower();
            books = books.Where(b =>
                b.Title.ToLower().Contains(q) ||
                b.Author.ToLower().Contains(q) ||
                b.ISBN.Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(dto.Genre))
            books = books.Where(b => b.Genre.ToLower() == dto.Genre.ToLower());

        if (dto.MinPrice.HasValue)
            books = books.Where(b => b.Price >= dto.MinPrice.Value);

        if (dto.MaxPrice.HasValue)
            books = books.Where(b => b.Price <= dto.MaxPrice.Value);

        // Sorting
        books = dto.SortBy?.ToLower() switch
        {
            "author"        => dto.SortDescending ? books.OrderByDescending(b => b.Author)        : books.OrderBy(b => b.Author),
            "price"         => dto.SortDescending ? books.OrderByDescending(b => (double)b.Price)         : books.OrderBy(b => (double)b.Price),
            "publisheddate" => dto.SortDescending ? books.OrderByDescending(b => b.PublishedDate) : books.OrderBy(b => b.PublishedDate),
            _               => dto.SortDescending ? books.OrderByDescending(b => b.Title)         : books.OrderBy(b => b.Title),
        };

        var totalCount = await books.CountAsync();
        var pageSize   = Math.Clamp(dto.PageSize, 1, 100);
        var page       = Math.Max(dto.Page, 1);

        var items = await books
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => MapToResponse(b))
            .ToListAsync();

        return new PagedResult<BookResponseDto>(
            items,
            totalCount,
            page,
            pageSize,
            (int)Math.Ceiling(totalCount / (double)pageSize)
        );
    }

    // ──────────────────────────────────────────
    // CREATE
    // ──────────────────────────────────────────
    public async Task<BookResponseDto> CreateAsync(CreateBookDto dto)
    {
        var book = new Book
        {
            Title         = dto.Title,
            Author        = dto.Author,
            ISBN          = dto.ISBN,
            Genre         = dto.Genre,
            Price         = dto.Price,
            StockQuantity = dto.StockQuantity,
            Description   = dto.Description,
            PublishedDate = dto.PublishedDate,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return MapToResponse(book);
    }

    // ──────────────────────────────────────────
    // UPDATE  (partial / PATCH-style)
    // ──────────────────────────────────────────
    public async Task<BookResponseDto?> UpdateAsync(int id, UpdateBookDto dto)
    {
        var book = await _db.Books.FindAsync(id);
        if (book is null) return null;

        if (dto.Title         is not null) book.Title         = dto.Title;
        if (dto.Author        is not null) book.Author        = dto.Author;
        if (dto.ISBN          is not null) book.ISBN          = dto.ISBN;
        if (dto.Genre         is not null) book.Genre         = dto.Genre;
        if (dto.Price         is not null) book.Price         = dto.Price.Value;
        if (dto.StockQuantity is not null) book.StockQuantity = dto.StockQuantity.Value;
        if (dto.Description   is not null) book.Description   = dto.Description;
        if (dto.PublishedDate is not null) book.PublishedDate = dto.PublishedDate.Value;
        book.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToResponse(book);
    }

    // ──────────────────────────────────────────
    // DELETE
    // ──────────────────────────────────────────
    public async Task<bool> DeleteAsync(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book is null) return false;

        _db.Books.Remove(book);
        await _db.SaveChangesAsync();
        return true;
    }

    // ──────────────────────────────────────────
    // Mapping helper (kept static / allocation-free)
    // ──────────────────────────────────────────
    private static BookResponseDto MapToResponse(Book b) => new(
        b.Id, b.Title, b.Author, b.ISBN, b.Genre,
        b.Price, b.StockQuantity, b.Description,
        b.PublishedDate, b.CreatedAt, b.UpdatedAt
    );
}
