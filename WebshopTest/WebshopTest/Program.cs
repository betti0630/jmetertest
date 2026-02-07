using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Diagnostics;
using System.Text.Json;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL adatbázis
builder.Services.AddDbContext<WebshopDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "webshop:";
});

var app = builder.Build();

// Adatbázis inicializálás (újrapróbálkozással, amíg a DB elérhető lesz)
var retryCount = 0;
while (retryCount < 10)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebshopDbContext>();
        db.Database.EnsureCreated();

        if (!db.Products.Any())
        {
            var categories = new[] { "Elektronika", "Ruházat", "Élelmiszer", "Könyv" };
            var seedProducts = Enumerable.Range(1, 100).Select(i => new Product
            {
                Name = $"Termék {i}",
                Price = Random.Shared.Next(1000, 50000),
                Stock = Random.Shared.Next(10, 100),
                Category = categories[Random.Shared.Next(categories.Length)]
            });
            db.Products.AddRange(seedProducts);
            db.SaveChanges();
        }
        break;
    }
    catch (Exception ex)
    {
        retryCount++;
        Console.WriteLine($"Adatbázis kapcsolódás sikertelen ({retryCount}/10): {ex.Message}");
        Thread.Sleep(2000);
    }
}

app.UseSwagger();
app.UseSwaggerUI();

var requestCount = 0;

// ============ ENDPOINTOK ============

// Health check
app.MapGet("/health", () =>
{
    Interlocked.Increment(ref requestCount);
    return Results.Ok(new
    {
        Status = "OK",
        Uptime = DateTime.Now - Process.GetCurrentProcess().StartTime,
        Requests = requestCount
    });
});

// Összes termék listázása
app.MapGet("/api/products", async ([FromQuery] int limit = 20, [FromQuery] int offset = 0, WebshopDbContext db = null!) =>
{
    Interlocked.Increment(ref requestCount);

    var result = await db.Products.Skip(offset).Take(limit).ToListAsync();
    var total = await db.Products.CountAsync();

    return Results.Ok(new
    {
        Products = result,
        Total = total,
        Limit = limit,
        Offset = offset
    });
});

// Összes termék listázása (Redis cache-ből)
app.MapGet("/api/cached/products", async (HttpContext context, [FromQuery] int limit = 20, [FromQuery] int offset = 0,
    WebshopDbContext db = null!) =>
{
    Interlocked.Increment(ref requestCount);

    var cache = context.RequestServices.GetRequiredService<IDistributedCache>();
    var cacheKey = $"products:{limit}:{offset}";
    var cached = await cache.GetStringAsync(cacheKey);

    if (cached != null)
    {
        var cachedResult = JsonSerializer.Deserialize<object>(cached);
        return Results.Ok(cachedResult);
    }

    var result = await db.Products.Skip(offset).Take(limit).ToListAsync();
    var total = await db.Products.CountAsync();

    var response = new
    {
        Products = result,
        Total = total,
        Limit = limit,
        Offset = offset,
        Source = "database"
    };

    await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });

    return Results.Ok(response);
});

// Egy termék részletei
app.MapGet("/api/products/{id:int}", async (int id, WebshopDbContext db) =>
{
    Interlocked.Increment(ref requestCount);

    var product = await db.Products.FindAsync(id);

    return product != null
        ? Results.Ok(product)
        : Results.NotFound(new { Error = "Termék nem található" });
});

// Termék keresése
app.MapGet("/api/search", async ([FromQuery] string q = "", WebshopDbContext db = null!) =>
{
    Interlocked.Increment(ref requestCount);

    var results = await db.Products
        .Where(p => p.Name.Contains(q) || p.Category.Contains(q))
        .Take(10)
        .ToListAsync();

    return Results.Ok(new
    {
        Query = q,
        Results = results,
        Count = results.Count
    });
});

// Kosárba tétel (írási művelet)
app.MapPost("/api/cart/add", async ([FromBody] AddToCartRequest request, WebshopDbContext db) =>
{
    Interlocked.Increment(ref requestCount);

    var product = await db.Products.FindAsync(request.ProductId);

    if (product == null)
        return Results.NotFound(new { Error = "Termék nem található" });

    if (product.Stock < request.Quantity)
        return Results.BadRequest(new { Error = "Nincs elegendő készlet" });

    db.CartItems.Add(new CartItem
    {
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        AddedAt = DateTime.Now
    });

    product.Stock -= request.Quantity;
    await db.SaveChangesAsync();

    var cartCount = await db.CartItems.CountAsync(c => c.OrderId == null);

    return Results.Ok(new
    {
        Success = true,
        Cart = cartCount,
        Message = "Termék hozzáadva a kosárhoz"
    });
});

// Kosár tartalmának lekérése
app.MapGet("/api/cart", async (WebshopDbContext db) =>
{
    Interlocked.Increment(ref requestCount);

    var cartItems = await db.CartItems
        .Where(c => c.OrderId == null)
        .ToListAsync();

    var productIds = cartItems.Select(c => c.ProductId).Distinct().ToList();
    var products = await db.Products
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    var cartWithDetails = cartItems.Select(item => new
    {
        item.ProductId,
        item.Quantity,
        item.AddedAt,
        Product = products.GetValueOrDefault(item.ProductId)
    }).ToList();

    return Results.Ok(new { Cart = cartWithDetails, Total = cartItems.Count });
});

// Rendelés leadása
app.MapPost("/api/order", async (WebshopDbContext db) =>
{
    Interlocked.Increment(ref requestCount);

    var cartItems = await db.CartItems
        .Where(c => c.OrderId == null)
        .ToListAsync();

    if (cartItems.Count == 0)
        return Results.BadRequest(new { Error = "A kosár üres" });

    var productIds = cartItems.Select(c => c.ProductId).Distinct().ToList();
    var products = await db.Products
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    var order = new Order
    {
        CreatedAt = DateTime.Now,
        Total = cartItems.Sum(item =>
        {
            var product = products[item.ProductId];
            return product.Price * item.Quantity;
        })
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    foreach (var item in cartItems)
    {
        item.OrderId = order.Id;
    }
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        Success = true,
        Order = new { order.Id, order.CreatedAt, order.Total, Items = cartItems },
        Message = "Rendelés sikeresen leadva"
    });
});

// Statisztikák
app.MapGet("/api/stats", async (WebshopDbContext db) =>
{
    Interlocked.Increment(ref requestCount);

    return Results.Ok(new
    {
        TotalProducts = await db.Products.CountAsync(),
        TotalOrders = await db.Orders.CountAsync(),
        CartItems = await db.CartItems.CountAsync(c => c.OrderId == null),
        RequestCount = requestCount,
        Uptime = DateTime.Now - Process.GetCurrentProcess().StartTime
    });
});

Console.WriteLine(@"
╔═══════════════════════════════════════════════════════╗
║        🛒 WEBSHOP TESZT SZERVER ELINDULT 🛒          ║
╠═══════════════════════════════════════════════════════╣
║  URL: http://localhost:5000                          ║
║  Swagger: http://localhost:5000/swagger              ║
║                                                       ║
║  Elérhető endpointok:                                ║
║  • GET  /health                                      ║
║  • GET  /api/products                                ║
║  • GET  /api/cached/products                         ║
║  • GET  /api/products/{id}                           ║
║  • GET  /api/search?q=termék                         ║
║  • POST /api/cart/add                                ║
║  • GET  /api/cart                                    ║
║  • POST /api/order                                   ║
║  • GET  /api/stats                                   ║
╚═══════════════════════════════════════════════════════╝
");

app.Run("http://0.0.0.0:5000");

// ============ MODELLEK ============

record Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int Stock { get; set; }
    public string Category { get; set; } = string.Empty;
}

record CartItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; }
    public int? OrderId { get; set; }
}

record Order
{
    public int Id { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public int Total { get; set; }
}

record AddToCartRequest(int ProductId, int Quantity);

// ============ ADATBÁZIS KONTEXTUS ============

class WebshopDbContext : DbContext
{
    public WebshopDbContext(DbContextOptions<WebshopDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
}
