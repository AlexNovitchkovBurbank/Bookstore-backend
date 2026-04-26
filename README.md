# 📚 Bookstore API

ASP.NET Core 8 Web API — CRUD + search for a bookstore backend.

## Project Structure

```
BookstoreApi/
├── Controllers/
│   └── BooksController.cs      # HTTP layer — routes + status codes
├── Data/
│   └── BookstoreDbContext.cs   # EF Core context + seed data
├── Models/
│   ├── Book.cs                 # Entity
│   └── DTOs/
│       └── BookDtos.cs         # Request/response shapes
├── Services/
│   ├── IBookService.cs         # Contract
│   └── BookService.cs          # Business logic + EF queries
├── Program.cs                  # App bootstrap, DI, middleware
├── appsettings.json
└── BookstoreApi.csproj
```

## Quick Start

```bash
# 1. Restore packages
dotnet restore

# 2. Apply EF migrations (creates bookstore.db automatically)
dotnet ef migrations add InitialCreate
dotnet ef database update

# 3. Run
dotnet run

# Swagger UI → https://localhost:{port}/swagger
```

## API Endpoints

| Method   | Route                  | Description                        |
|----------|------------------------|------------------------------------|
| GET      | /api/books             | List all books (paginated)         |
| GET      | /api/books/{id}        | Get a single book                  |
| GET      | /api/books/search      | Search / filter / sort books       |
| POST     | /api/books             | Create a new book                  |
| PATCH    | /api/books/{id}        | Partial update a book              |
| DELETE   | /api/books/{id}        | Delete a book                      |

### Search Query Parameters

| Param           | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| query           | string  | Full-text search on title, author, ISBN          |
| genre           | string  | Filter by genre (exact match, case-insensitive)  |
| minPrice        | decimal | Minimum price                                    |
| maxPrice        | decimal | Maximum price                                    |
| sortBy          | string  | `title` · `author` · `price` · `publishedDate`  |
| sortDescending  | bool    | Default false                                    |
| page            | int     | Default 1                                        |
| pageSize        | int     | Default 20 (max 100)                             |

### Example Requests

```http
# Get all books, page 1
GET /api/books

# Search by keyword
GET /api/books/search?query=dune

# Filter by genre + price range, sorted by price
GET /api/books/search?genre=Technology&minPrice=20&maxPrice=60&sortBy=price

# Create a book
POST /api/books
Content-Type: application/json
{
  "title": "The Hobbit",
  "author": "J.R.R. Tolkien",
  "isbn": "978-0547928227",
  "genre": "Fantasy",
  "price": 12.99,
  "stockQuantity": 50,
  "description": "A classic fantasy adventure.",
  "publishedDate": "1937-09-21T00:00:00Z"
}

# Partial update (only fields provided are changed)
PATCH /api/books/1
Content-Type: application/json
{ "price": 44.99, "stockQuantity": 10 }

# Delete
DELETE /api/books/3
```

## Swapping the Database

The default is **SQLite** (zero-config). To switch:

### SQL Server
```csproj
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
```
```csharp
// Program.cs
options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"))
```
```json
// appsettings.json
"ConnectionStrings": { "SqlServer": "Server=.;Database=Bookstore;Trusted_Connection=True;" }
```

### PostgreSQL
```csproj
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
```
```csharp
options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
```

## Next Steps

- **Authentication** — Add `Microsoft.AspNetCore.Authentication.JwtBearer` for JWT auth
- **Validation** — Add FluentValidation or DataAnnotations on the DTOs
- **Caching** — Add `IMemoryCache` or Redis for frequent GET requests
- **Logging** — Wire in Serilog for structured logs
- **Tests** — Add an `xUnit` project + `Microsoft.EntityFrameworkCore.InMemory` for unit tests
