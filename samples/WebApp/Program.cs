using Lookout.AspNetCore;
using Lookout.EntityFrameworkCore;
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
