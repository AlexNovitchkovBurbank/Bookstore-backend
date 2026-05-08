using BookstoreAPI.Models.DTOs;

namespace BookstoreAPI.Services;

public interface IBookService
{
    Task<BookResponseDto?> GetByIdAsync(int id);
    Task<PagedResult<BookResponseDto>> SearchAsync(BookSearchDto searchDto);
    Task<BookResponseDto> CreateAsync(CreateBookDto dto);
    Task<BookResponseDto?> UpdateAsync(int id, UpdateBookDto dto);
    Task<bool> DeleteAsync(int id);
}
