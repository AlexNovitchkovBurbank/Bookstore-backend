using BookstoreAPI.Controllers;
using BookstoreAPI.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;

namespace BookstoreAPI.IntegrationTests;

/// <summary>
/// Spins up the real ASP.NET Core pipeline in-process,
/// but replaces the SQLite DB with an isolated SQLite In-Memory database.
/// </summary>
public class BookstoreWebAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public BookstoreWebAppFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BookstoreDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

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
