using Lookout.AspNetCore;
using Lookout.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Cache registrations must come before AddLookout() so the decorators can wrap them.
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

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

// Typed HttpClient — self-call so the dogfood stays in-process and offline-tolerant.
builder.Services.AddHttpClient<WeatherForecastClient>(client =>
    client.BaseAddress = new Uri("http://localhost:5080"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
    SampleSeed.Seed(db);
}

app.UseLookout();

// ── Existing endpoints ────────────────────────────────────────────────────────

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

// N+1 demo: loads all orders then fetches each customer individually.
// With 3 seeded orders Lookout detects 3 identical-shape customer queries and raises the N+1 banner.
app.MapGet("/orders/n1", async (SampleDbContext db) =>
{
    var orders = await db.Orders.AsNoTracking().ToListAsync();

    var results = new List<object>(orders.Count);
    foreach (var order in orders)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .SingleOrDefaultAsync();
        results.Add(new { order.Id, Customer = customer?.Name ?? "—", order.Quantity });
    }

    return results;
});

// Raw SQL demo: captured by the EF interceptor; note SQLite does not emit SqlClientDiagnosticListener
// events so raw ADO.NET capture (without EF) only works with SQL Server.
app.MapGet("/products/raw-sql", async (SampleDbContext db) =>
    await db.Products
        .FromSqlRaw("SELECT Id, Name, Price FROM Products ORDER BY Name")
        .AsNoTracking()
        .Select(p => new { p.Id, p.Name, p.Price })
        .ToListAsync());

// ── New M6.5 endpoints ────────────────────────────────────────────────────────

// Outbound HTTP demo: the typed WeatherForecastClient issues an HttpClient call captured by Lookout.
// Open /lookout after hitting this endpoint to see the http-out entry and the http: badge.
app.MapGet("/weather/forecast", async (WeatherForecastClient client) =>
    await client.GetForecastAsync());

// IMemoryCache demo: first hit is a miss (EF query issued + cached), subsequent hits are cache hits.
// Open /lookout and hit /products/{id} twice; first request shows Get(miss)+Set, second shows Get(hit).
app.MapGet("/products/{id:int}", async (int id, SampleDbContext db, IMemoryCache cache) =>
{
    var product = await cache.GetOrCreateAsync($"product-{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    });
    return product is null ? Results.NotFound() : Results.Ok(product);
});

// IDistributedCache demo: GET creates the session on first call (Set), returns it on repeat calls (Get).
// DELETE removes it (Remove). Exercises all three distributed-cache operations.
app.MapGet("/sessions/{id}", async (string id, IDistributedCache cache) =>
{
    var key = $"session-{id}";
    var existing = await cache.GetStringAsync(key);
    if (existing is not null)
        return Results.Ok(new { id, data = JsonSerializer.Deserialize<object>(existing), fromCache = true });

    var payload = JsonSerializer.Serialize(new { createdAt = DateTime.UtcNow, userId = id });
    await cache.SetStringAsync(key, payload, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    });
    return Results.Ok(new { id, data = (object)new { createdAt = DateTime.UtcNow, userId = id }, fromCache = false });
});

app.MapDelete("/sessions/{id}", async (string id, IDistributedCache cache) =>
{
    await cache.RemoveAsync($"session-{id}");
    return Results.NoContent();
});

// Combined demo: EF query + outbound HTTP + memory cache — all three captures in one request.
// Open /lookout after hitting /orders/{id}/full; the detail should show db, http, and cache sections.
app.MapGet("/orders/{id:int}/full", async (int id, SampleDbContext db, IMemoryCache cache, WeatherForecastClient weatherClient) =>
{
    // 1. EF query
    var order = await db.Orders
        .AsNoTracking()
        .Include(o => o.Customer)
        .Include(o => o.Product)
        .FirstOrDefaultAsync(o => o.Id == id);
    if (order is null) return Results.NotFound();

    // 2. IMemoryCache lookup (first call = miss + set; subsequent = hit)
    var shipping = cache.GetOrCreate($"shipping-{id}", entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return new { carrier = "FedEx", estimatedDays = 3 };
    });

    // 3. Outbound HTTP call (captured as http-out entry, correlated to this parent request)
    WeatherForecast[]? forecast = null;
    try { forecast = await weatherClient.GetForecastAsync(); }
    catch { /* sample only — offline-tolerant */ }

    return Results.Ok(new
    {
        order.Id,
        Customer = order.Customer?.Name,
        Product = order.Product?.Name,
        order.Quantity,
        shipping,
        weatherForecast = forecast?.FirstOrDefault(),
    });
});

app.MapLookout();

app.Run();

// ── Types ─────────────────────────────────────────────────────────────────────

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

/// <summary>Typed HttpClient wrapper for the local /weatherforecast endpoint.</summary>
public sealed class WeatherForecastClient(HttpClient http)
{
    public async Task<WeatherForecast[]?> GetForecastAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<WeatherForecast[]>("/weatherforecast", ct);
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
