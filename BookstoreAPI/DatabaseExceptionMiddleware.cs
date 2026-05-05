using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace BookstoreAPI.Middleware;

public class DatabaseExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DatabaseExceptionMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseExceptionMiddleware(
        RequestDelegate next,
        ILogger<DatabaseExceptionMiddleware> logger,
        IConfiguration configuration)
    {
        _next        = next;
        _logger      = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (IsDatabaseException(ex))
                await HandleDatabaseExceptionAsync(context, ex);
            else if (ex is ArgumentException)
                throw new ArgumentException("Invalid argument provided", ex);
            else
                await HandleGenericExceptionAsync(context,ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsDatabaseException(Exception ex) =>
        ex is DbUpdateException
        || ex is DbUpdateConcurrencyException
        || ex is Microsoft.Data.Sqlite.SqliteException
        || ex is InvalidOperationException invalidOperationException
            && (invalidOperationException.Message.Contains("database", StringComparison.OrdinalIgnoreCase)
                || invalidOperationException.Message.Contains("connection", StringComparison.OrdinalIgnoreCase));

    private async Task HandleDatabaseExceptionAsync(HttpContext context, Exception ex)
    {
        var connectionString = _configuration.GetConnectionString("Default")
                               ?? "Data Source=bookstore.db";

        // Log full details server-side only
        _logger.LogError(
            ex,
            """
            DATABASE ERROR on {Method} {Path}
            Connection String : {ConnectionString}
            Exception Type    : {ExceptionType}
            Message           : {Message}
            Stack Trace       :
            {StackTrace}
            """,
            context.Request.Method,
            context.Request.Path,
            connectionString,
            ex.GetType().FullName,
            ex.Message,
            ex.StackTrace);

        // Return a clean 503 to the caller — no internal details exposed
        context.Response.StatusCode  = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            status  = 503,
            error   = "Service Unavailable",
            message = "The database is currently unavailable. Please try again later.",
            path    = context.Request.Path.Value,
            timestamp = DateTime.UtcNow
        });

        await context.Response.WriteAsync(body);
    }

    private async Task HandleGenericExceptionAsync(HttpContext context, Exception ex)
    {
        // Log generic exceptions as well
        _logger.LogError(ex, "An unexpected error occurred: {Message}", ex.Message);

        var body = JsonSerializer.Serialize(new
        {
            status = 503,
            error = "Service Unavailable",
            message = "An error happened at the database layer. Please try again later.",
            path = context.Request.Path.Value,
            timestamp = DateTime.UtcNow
        });

        await context.Response.WriteAsync(body);
    }
}

// Extension method for clean registration in Program.cs
public static class DatabaseExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseDatabaseExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<DatabaseExceptionMiddleware>();
}
