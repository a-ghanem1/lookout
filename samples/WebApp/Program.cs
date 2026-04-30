using Lookout.AspNetCore;
using Lookout.EntityFrameworkCore;
using static Lookout.Core.Lookout;
using Microsoft.AspNetCore.Diagnostics;
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

// Typed HttpClient — self-call so dogfood stays in-process and offline-tolerant.
builder.Services.AddHttpClient<WeatherForecastClient>(client =>
    client.BaseAddress = new Uri("http://localhost:5080"));

// Payments stub client — same base URL, calls /payments/check/{id} on this app.
builder.Services.AddHttpClient<PaymentsClient>(client =>
    client.BaseAddress = new Uri("http://localhost:5080"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
    SampleSeed.Seed(db);
}

// UseLookout must be outermost so the N+1/exception scope stays active while
// UseExceptionHandler (downstream) calls LookoutExceptionHandler.TryHandleAsync.
app.UseLookout();

// Inline exception handler — avoids a second HTTP entry from the /error re-execute path.
app.UseExceptionHandler(eh => eh.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is OrderNotFoundException notFound)
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = notFound.Message });
    }
    else
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = ex?.Message ?? "An error occurred" });
    }
}));

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

// ── M6.5 endpoints ────────────────────────────────────────────────────────────

// Outbound HTTP demo: the typed WeatherForecastClient issues an HttpClient call captured by Lookout.
app.MapGet("/weather/forecast", async (WeatherForecastClient client) =>
    await client.GetForecastAsync());

// IMemoryCache demo: first hit is a miss (EF query issued + cached), subsequent hits are cache hits.
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
app.MapGet("/orders/{id:int}/full", async (int id, SampleDbContext db, IMemoryCache cache, WeatherForecastClient weatherClient) =>
{
    var order = await db.Orders
        .AsNoTracking()
        .Include(o => o.Customer)
        .Include(o => o.Product)
        .FirstOrDefaultAsync(o => o.Id == id);
    if (order is null) return Results.NotFound();

    var shipping = cache.GetOrCreate($"shipping-{id}", entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return new { carrier = "FedEx", estimatedDays = 3 };
    });

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

// ── M7.5 — Milestone M2 demo endpoint ────────────────────────────────────────

// Payments stub: returns a simulated payment approval for the given order.
// The PaymentsClient calls this endpoint as the outbound HTTP capture demo.
app.MapGet("/payments/check/{id:int}", (int id) =>
    Results.Ok(new PaymentStatus("approved", $"PAY-{id:D6}")));

// The M2 demo endpoint: one request produces HTTP + ~8 EF queries (with N+1 on OrderItems) +
// 2 IMemoryCache reads (miss+hit) + 1 outbound HTTP + 3 logs + 1 Dump + exception on unknown id.
//
// Happy path:  GET /orders/1   (or any seeded order id)
// Unhappy path: GET /orders/9999  → throws OrderNotFoundException → red badge in the list
app.MapGet("/orders/{id:int}", async (
    int id,
    SampleDbContext db,
    IMemoryCache cache,
    PaymentsClient paymentsClient,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Sample.Orders");

    // Query 1: load order
    var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    if (order is null)
        throw new OrderNotFoundException(id);

    // Query 2: load customer (deliberate separate round-trip to show in the DB panel)
    var customer = await db.Customers.AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == order.CustomerId);

    // Dump the freshly loaded order so it appears in the Dump section
    Dump(new { order.Id, order.CustomerId, order.ProductId, order.Quantity, order.PlacedAt }, "loaded");

    // Log 1: information
    logger.LogInformation("Loaded order {OrderId} for customer {CustomerName}", id, customer?.Name ?? "—");

    // Query 3: load order items
    var items = await db.OrderItems.AsNoTracking()
        .Where(i => i.OrderId == id)
        .ToListAsync();

    // Queries 4-N+2 (N+1 pattern): load each item's product individually.
    // With 3+ seeded items per order Lookout's N+1 detector fires and raises the banner.
    var loadedProducts = new List<Product?>(items.Count);
    foreach (var item in items)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == item.ProductId);
        loadedProducts.Add(product);
    }

    // Log 2: warning (stock advisory — always fires so the log.maxLevel=Warning tag is set)
    logger.LogWarning(
        "Stock advisory: verify availability for order {OrderId} before shipping", id);

    // Cache read 1 (miss on first request): check for a cached order summary
    var summaryCacheKey = $"order-summary-{id}";
    var cachedSummary = cache.Get<object>(summaryCacheKey);
    if (cachedSummary is null)
    {
        // Query N+3: product count used to build the summary payload
        var totalProducts = await db.Products.AsNoTracking().CountAsync();
        var summary = new { orderId = id, totalProducts, customerName = customer?.Name };
        cache.Set(summaryCacheKey, summary, TimeSpan.FromMinutes(5));
    }

    // Cache read 2 (hit): the summary is now cached — second Get returns it immediately
    var finalSummary = cache.Get<object>(summaryCacheKey);

    // Query N+4: count prior orders for this customer (realistic business query)
    var priorOrderCount = await db.Orders.AsNoTracking()
        .CountAsync(o => o.CustomerId == order.CustomerId);

    // Outbound HTTP call to the payments stub (captured as an http-out entry)
    PaymentStatus? payment = null;
    try
    {
        payment = await paymentsClient.CheckPaymentAsync(id);
    }
    catch
    {
        // offline-tolerant: if the app isn't reachable on port 5080 the demo still works
    }

    // Log 3: information
    logger.LogInformation(
        "Payment check for order {OrderId} returned {Status}",
        id,
        payment?.Status ?? "unknown");

    return Results.Ok(new
    {
        order.Id,
        Customer = customer?.Name,
        Items = items.Select((item, i) => new
        {
            item.ProductId,
            ProductName = i < loadedProducts.Count ? loadedProducts[i]?.Name : null,
            item.Quantity,
        }),
        PriorOrders = priorOrderCount,
        Payment = payment,
        Summary = finalSummary,
    });
});

app.MapLookout();

app.Run();

// ── Types ─────────────────────────────────────────────────────────────────────

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public record PaymentStatus(string Status, string PaymentId);

/// <summary>Typed HttpClient wrapper for the local /weatherforecast endpoint.</summary>
public sealed class WeatherForecastClient(HttpClient http)
{
    public async Task<WeatherForecast[]?> GetForecastAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<WeatherForecast[]>("/weatherforecast", ct);
}

/// <summary>Typed HttpClient wrapper for the local /payments/check/{id} stub.</summary>
public sealed class PaymentsClient(HttpClient http)
{
    public async Task<PaymentStatus?> CheckPaymentAsync(int orderId, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<PaymentStatus>($"/payments/check/{orderId}", ct);
}

/// <summary>Thrown when an order cannot be found. Produces a 404 and an exception entry in Lookout.</summary>
public sealed class OrderNotFoundException(int orderId)
    : Exception($"Order {orderId} not found.")
{
    public int OrderId { get; } = orderId;
}

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
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

/// <summary>
/// Line item on an order — seeded with 3 per order so the N+1 detector fires
/// when the demo endpoint loads each item's Product individually.
/// </summary>
public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
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

        var order1 = new Order { CustomerId = alice.Id, ProductId = widget.Id, Quantity = 2, PlacedAt = DateTime.UtcNow.AddHours(-3) };
        var order2 = new Order { CustomerId = alice.Id, ProductId = gadget.Id, Quantity = 1, PlacedAt = DateTime.UtcNow.AddHours(-2) };
        var order3 = new Order { CustomerId = bob.Id, ProductId = gizmo.Id, Quantity = 5, PlacedAt = DateTime.UtcNow.AddHours(-1) };
        db.AddRange(order1, order2, order3);
        db.SaveChanges();

        // 3 OrderItems for order1 — triggers the N+1 detector in /orders/{id}
        db.AddRange(
            new OrderItem { OrderId = order1.Id, ProductId = widget.Id, Quantity = 2 },
            new OrderItem { OrderId = order1.Id, ProductId = gadget.Id, Quantity = 1 },
            new OrderItem { OrderId = order1.Id, ProductId = gizmo.Id, Quantity = 3 });
        db.SaveChanges();
    }
}
