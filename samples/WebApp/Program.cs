using Lookout.AspNetCore;
using Lookout.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLookout(o =>
{
    o.CaptureRequestBody = true;
    o.CaptureResponseBody = true;
});
builder.Services.AddEntityFrameworkCore();

builder.Services.AddDbContext<SampleDbContext>((sp, opts) =>
{
    opts.UseSqlite("Data Source=sample.db");
    opts.UseLookout(sp);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
    SampleSeed.Seed(db);
}

app.UseLookout();

app.MapGet("/weatherforecast", () =>
{
    string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
    return Enumerable.Range(1, 5).Select(index => new WeatherForecast(
        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        Random.Shared.Next(-20, 55),
        summaries[Random.Shared.Next(summaries.Length)]
    )).ToArray();
});

app.MapGet("/products", async (SampleDbContext db) =>
    await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync());

app.MapGet("/orders", async (SampleDbContext db) =>
    await db.Orders
        .AsNoTracking()
        .Include(o => o.Customer)
        .Include(o => o.Product)
        .OrderByDescending(o => o.PlacedAt)
        .Select(o => new
        {
            o.Id,
            Customer = o.Customer!.Name,
            Product = o.Product!.Name,
            o.Quantity,
            o.PlacedAt,
        })
        .ToListAsync());

// N+1 demo endpoint: loads all orders then fetches each customer individually.
// With 3 seeded orders Lookout detects 3 identical-shape customer queries and raises the N+1 banner.
// Open /lookout after hitting this endpoint to see the red N+1 indicator.
app.MapGet("/orders/n1", async (SampleDbContext db) =>
{
    var orders = await db.Orders.AsNoTracking().ToListAsync();

    var results = new List<object>(orders.Count);
    foreach (var order in orders)
    {
        // Classic N+1: one extra query per order instead of a JOIN.
        var customer = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .SingleOrDefaultAsync();
        results.Add(new { order.Id, Customer = customer?.Name ?? "—", order.Quantity });
    }

    return results;
});

// Raw ADO.NET demo endpoint: issues a query via Microsoft.Data.Sqlite directly (no EF).
// Lookout captures raw ADO.NET via DiagnosticListener for SQL Server (SqlClientDiagnosticListener).
// SQLite does not publish to that source, so this query will not appear in the Lookout dashboard.
// The endpoint exists to confirm the code path compiles and runs; the capture is verified by
// the integration tests in Lookout.AspNetCore.Tests for SQL Server-compatible scenarios.
app.MapGet("/products/raw-sql", async () =>
{
    await using var conn = new SqliteConnection("Data Source=sample.db");
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Price FROM Products ORDER BY Name";
    await using var reader = await cmd.ExecuteReaderAsync();

    var products = new List<object>();
    while (await reader.ReadAsync())
        products.Add(new { Id = reader.GetInt32(0), Name = reader.GetString(1), Price = reader.GetDecimal(2) });
    return products;
});

app.MapLookout();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public DateTime PlacedAt { get; set; }
}

static class SampleSeed
{
    public static void Seed(SampleDbContext db)
    {
        if (db.Products.Any()) return;

        var alice = new Customer { Name = "Alice" };
        var bob = new Customer { Name = "Bob" };
        var widget = new Product { Name = "Widget", Price = 9.99m };
        var gadget = new Product { Name = "Gadget", Price = 19.99m };
        var gizmo = new Product { Name = "Gizmo", Price = 29.99m };

        db.AddRange(alice, bob, widget, gadget, gizmo);
        db.SaveChanges();

        db.AddRange(
            new Order { CustomerId = alice.Id, ProductId = widget.Id, Quantity = 2, PlacedAt = DateTime.UtcNow.AddHours(-3) },
            new Order { CustomerId = alice.Id, ProductId = gadget.Id, Quantity = 1, PlacedAt = DateTime.UtcNow.AddHours(-2) },
            new Order { CustomerId = bob.Id, ProductId = gizmo.Id, Quantity = 5, PlacedAt = DateTime.UtcNow.AddHours(-1) });
        db.SaveChanges();
    }
}
