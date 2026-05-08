using BookstoreAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookstoreAPI.Data;

public class BookstoreDbContext : DbContext
{
    public BookstoreDbContext(DbContextOptions<BookstoreDbContext> options)
        : base(options) { }

    public DbSet<Book> Books => Set<Book>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title).IsRequired().HasMaxLength(300);
            entity.Property(b => b.Author).IsRequired().HasMaxLength(200);
            entity.Property(b => b.ISBN).HasMaxLength(20);
            entity.Property(b => b.Genre).HasMaxLength(100);
            entity.Property(b => b.Price).HasColumnType("decimal(10,2)");
            entity.Property(b => b.Description).HasMaxLength(2000);

            entity.HasIndex(b => b.ISBN).IsUnique();
            entity.HasIndex(b => b.Title);
            entity.HasIndex(b => b.Author);
            entity.HasIndex(b => b.Genre);

            // Seed data
            entity.HasData(
                new Book { Id = 1, Title = "The Pragmatic Programmer", Author = "David Thomas", ISBN = "978-0135957059", Genre = "Technology", Price = 49.99m, StockQuantity = 25, Description = "Your journey to mastery.", PublishedDate = new DateTime(2019, 9, 13) },
                new Book { Id = 2, Title = "Clean Code", Author = "Robert C. Martin", ISBN = "978-0132350884", Genre = "Technology", Price = 39.99m, StockQuantity = 18, Description = "A handbook of agile software craftsmanship.", PublishedDate = new DateTime(2008, 8, 1) },
                new Book { Id = 3, Title = "Dune", Author = "Frank Herbert", ISBN = "978-0441013593", Genre = "Science Fiction", Price = 14.99m, StockQuantity = 42, Description = "A science fiction masterpiece.", PublishedDate = new DateTime(1965, 8, 1) }
            );
        });
    }
}
