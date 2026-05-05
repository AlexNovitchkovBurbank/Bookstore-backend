using BookstoreAPI.Controllers;
using BookstoreAPI.Data;
using BookstoreAPI.Middleware;
using BookstoreAPI.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http.Json;
using Xunit;

namespace BookstoreAPI.DatabaseMiddlewareTests;

/// <summary>
/// Factory variant that swaps BookService with a broken implementation
/// so we can test that the middleware returns 503 end-to-end.
/// </summary>
public class BrokenDbWebAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public BrokenDbWebAppFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real BookService
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IBookService));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Register a broken implementation that always throws DbUpdateException
            services.AddScoped<IBookService, BrokenBookService>();

            // Still need a DbContext (even if unused) to satisfy DI
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BookstoreDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            // use the sqlite in-memory db provider instead of ef core's in memory provider, so that we can test real SQL queries and migrations
            services.AddDbContext<BookstoreDbContext>(options =>
                options.UseSqlite(_connection));

            services.AddControllers().AddApplicationPart(typeof(BooksController).Assembly);

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
            db.Database.Migrate();
        });

        builder.UseEnvironment("Development");
    }
}

/// <summary>
/// Always throws a DbUpdateException to simulate a database outage.
/// </summary>
public class BrokenBookService : IBookService
{
    #region use last fail<T> to throw the same exception from all methods
    public Task<BookstoreAPI.Models.DTOs.BookResponseDto?> GetByIdAsync(int id) => Fail<BookstoreAPI.Models.DTOs.BookResponseDto?>();
    public Task<BookstoreAPI.Models.DTOs.PagedResult<BookstoreAPI.Models.DTOs.BookResponseDto>> SearchAsync(BookstoreAPI.Models.DTOs.BookSearchDto dto) => Fail<BookstoreAPI.Models.DTOs.PagedResult<BookstoreAPI.Models.DTOs.BookResponseDto>>();
    public Task<BookstoreAPI.Models.DTOs.BookResponseDto> CreateAsync(BookstoreAPI.Models.DTOs.CreateBookDto dto) => Fail<BookstoreAPI.Models.DTOs.BookResponseDto>();
    public Task<BookstoreAPI.Models.DTOs.BookResponseDto?> UpdateAsync(int id, BookstoreAPI.Models.DTOs.UpdateBookDto dto) => Fail<BookstoreAPI.Models.DTOs.BookResponseDto?>();
    public Task<bool> DeleteAsync(int id) => Fail<bool>();
    #endregion

    private static Task<T> Fail<T>() =>
        throw new DbUpdateException("Simulated database outage");
}

// ═══════════════════════════════════════════════════════════════════════════════
// DatabaseExceptionMiddleware Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class DatabaseExceptionMiddlewareTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DatabaseExceptionMiddleware middleware, DefaultHttpContext context, MemoryStream responseBody)
        BuildMiddleware(RequestDelegate next)
    {
        var logger = Mock.Of<ILogger<DatabaseExceptionMiddleware>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Data Source=test.db"
            })
            .Build();

        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        context.Request.Method = "GET";
        context.Request.Path = "/api/books";

        var middleware = new DatabaseExceptionMiddleware(next, logger, config);
        return (middleware, context, responseBody);
    }

    private static string ReadBody(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        return new StreamReader(stream).ReadToEnd();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Happy path — passes through when no exception is thrown
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_NoException_PassesThrough()
    {
        var called = false;
        var (middleware, context, _) = BuildMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DbUpdateException → 503
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_DbUpdateException_Returns503()
    {
        var (middleware, context, responseBody) = BuildMiddleware(
            _ => throw new DbUpdateException("Simulated DB write failure"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);
        var body = ReadBody(responseBody);
        body.Should().Contain("Service Unavailable");
        body.Should().Contain("503");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DbUpdateConcurrencyException → 503
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_DbUpdateConcurrencyException_Returns503()
    {
        var (middleware, context, responseBody) = BuildMiddleware(
            _ => throw new DbUpdateConcurrencyException("Simulated concurrency conflict"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);
        ReadBody(responseBody).Should().Contain("503");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Non-DB exception is NOT caught — rethrows
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_NonDatabaseException_Rethrows()
    {
        var (middleware, context, _) = BuildMiddleware(
            _ => throw new ArgumentException("Not a DB error"));

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Response body is valid JSON with correct fields
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_DbException_ResponseBodyIsValidJson()
    {
        var (middleware, context, responseBody) = BuildMiddleware(
            _ => throw new DbUpdateException("DB failure"));

        await middleware.InvokeAsync(context);

        var body = ReadBody(responseBody);
        var act = () => System.Text.Json.JsonDocument.Parse(body);
        act.Should().NotThrow("response body must be valid JSON");

        var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        doc.RootElement.GetProperty("error").GetString().Should().Be("Service Unavailable");
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("path").GetString().Should().Be("/api/books");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Security — response must not leak connection string or stack trace
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_DbException_ResponseDoesNotLeakConnectionString()
    {
        var (middleware, context, responseBody) = BuildMiddleware(
            _ => throw new DbUpdateException("DB failure"));

        await middleware.InvokeAsync(context);

        ReadBody(responseBody).Should().NotContain("Data Source=test.db",
            because: "connection strings must never be exposed in HTTP responses");
    }

    [Fact]
    public async Task Invoke_DbException_ResponseDoesNotLeakStackTrace()
    {
        var (middleware, context, responseBody) = BuildMiddleware(
            _ => throw new DbUpdateException("DB failure"));

        await middleware.InvokeAsync(context);

        ReadBody(responseBody).Should().NotContain("at BookstoreAPI",
            because: "stack traces must never be exposed in HTTP responses");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Content-Type must be application/json
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invoke_DbException_SetsJsonContentType()
    {
        var (middleware, context, _) = BuildMiddleware(
            _ => throw new DbUpdateException("DB failure"));

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Contain("application/json");
    }
}


// ═══════════════════════════════════════════════════════════════════════════════
// Database error middleware — end-to-end 503 tests
// ═══════════════════════════════════════════════════════════════════════════════

public class DatabaseMiddlewareIntegrationTests : IClassFixture<BrokenDbWebAppFactory>
{
    private readonly HttpClient _client;

    public DatabaseMiddlewareIntegrationTests(BrokenDbWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetById_DbOutage_Returns503()
    {
        var response = await _client.GetAsync("/api/books/1");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetAll_DbOutage_Returns503()
    {
        var response = await _client.GetAsync("/api/books");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Search_DbOutage_Returns503()
    {
        var response = await _client.GetAsync("/api/books/search?books=dune");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Post_DbOutage_Returns503()
    {
        var dto = new { title = "Test", author = "Author", isbn = "000", genre = "Fiction", price = 9.99, stockQuantity = 1, description = "D", publishedDate = "2020-01-01" };
        var response = await _client.PostAsJsonAsync("/api/books", dto);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Patch_DbOutage_Returns503()
    {
        var response = await _client.PatchAsJsonAsync("/api/books/1", new { title = "New" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Delete_DbOutage_Returns503()
    {
        var response = await _client.DeleteAsync("/api/books/1");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DbOutage_ResponseBody_IsValidJson()
    {
        var response = await _client.GetAsync("/api/books/1");
        var body = await response.Content.ReadAsStringAsync();

        var act = () => System.Text.Json.JsonDocument.Parse(body);
        act.Should().NotThrow();

        var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        doc.RootElement.GetProperty("error").GetString().Should().Be("Service Unavailable");
    }

    [Fact]
    public async Task DbOutage_ResponseBody_DoesNotLeakInternals()
    {
        var response = await _client.GetAsync("/api/books/1");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContainAny(
            new[] { "StackTrace", "at BookstoreAPI", "Data Source", "DbUpdateException" },
            because: "internal details must never be exposed to callers"); // A human reason
    }
}


