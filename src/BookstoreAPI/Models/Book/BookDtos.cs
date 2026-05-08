namespace BookstoreAPI.Models.DTOs;

public record CreateBookDto(
    string Title,
    string Author,
    string ISBN,
    string Genre,
    decimal Price,
    int StockQuantity,
    string Description,
    DateTime PublishedDate
);

public record UpdateBookDto(
    string? Title,
    string? Author,
    string? ISBN,
    string? Genre,
    decimal? Price,
    int? StockQuantity,
    string? Description,
    DateTime? PublishedDate
);

public record BookSearchDto(
    string? Query,       // searches title, author, ISBN
    string? Genre,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? SortBy,      // "title", "author", "price", "publishedDate"
    bool SortDescending = false,
    int Page = 1,
    int PageSize = 20
);

public record BookResponseDto(
    int Id,
    string Title,
    string Author,
    string ISBN,
    string Genre,
    decimal Price,
    int StockQuantity,
    string Description,
    DateTime PublishedDate,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);
