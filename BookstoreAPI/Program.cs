using BookstoreAPI.Controllers;
using BookstoreAPI.Data;
using BookstoreAPI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Bookstore API", Version = "v1" });
});

// Database — switch the provider string below as needed:
//   SQLite  (default / zero-config)  → "Data Source=bookstore.db"
//   SQL Server                       → builder.Configuration.GetConnectionString("SqlServer")
//   PostgreSQL (Npgsql)              → builder.Configuration.GetConnectionString("Postgres")
builder.Services.AddDbContext<BookstoreDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=bookstore.db"
    )
);

builder.Services.AddScoped<IBookService, BookService>();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Pipeline ──────────────────────────────────────────────────────────────────

var app = builder.Build();

// Auto-apply migrations + seed on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Expose Program to integration test projects
public partial class Program { }
