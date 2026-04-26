using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BookstoreAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ISBN = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    StockQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Books",
                columns: new[] { "Id", "Author", "CreatedAt", "Description", "Genre", "ISBN", "Price", "PublishedDate", "StockQuantity", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "David Thomas", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3758), "Your journey to mastery.", "Technology", "978-0135957059", 49.99m, new DateTime(2019, 9, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), 25, "The Pragmatic Programmer", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3760) },
                    { 2, "Robert C. Martin", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3765), "A handbook of agile software craftsmanship.", "Technology", "978-0132350884", 39.99m, new DateTime(2008, 8, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 18, "Clean Code", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3765) },
                    { 3, "Frank Herbert", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3768), "A science fiction masterpiece.", "Science Fiction", "978-0441013593", 14.99m, new DateTime(1965, 8, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 42, "Dune", new DateTime(2026, 4, 24, 18, 50, 41, 476, DateTimeKind.Utc).AddTicks(3768) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_Author",
                table: "Books",
                column: "Author");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Genre",
                table: "Books",
                column: "Genre");

            migrationBuilder.CreateIndex(
                name: "IX_Books_ISBN",
                table: "Books",
                column: "ISBN",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_Title",
                table: "Books",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Books");
        }
    }
}
