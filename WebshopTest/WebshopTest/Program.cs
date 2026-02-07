using Microsoft.AspNetCore.Mvc;

using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ============ "ADATBÁZIS" SZIMULÁCIÓ ============
var products = Enumerable.Range(1, 100).Select(i => new Product
{
    Id = i,
    Name = $"Termék {i}",
    Price = Random.Shared.Next(1000, 50000),
    Stock = Random.Shared.Next(0, 100),
    Category = new[] { "Elektronika", "Ruházat", "Élelmiszer", "Könyv" }[Random.Shared.Next(4)]
}).ToList();

var cart = new List<CartItem>();
var orders = new List<Order>();
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

// Összes termék listázása (gyors)
app.MapGet("/api/products", async ([FromQuery] int limit = 20, [FromQuery] int offset = 0) =>
{
    Interlocked.Increment(ref requestCount);

    // Szimulált DB késleltetés: 50-150ms
    await Task.Delay(Random.Shared.Next(50, 150));

    var result = products.Skip(offset).Take(limit).ToList();

    return Results.Ok(new
    {
        Products = result,
        Total = products.Count,
        Limit = limit,
        Offset = offset
    });
});

// Egy termék részletei (közepes sebesség)
app.MapGet("/api/products/{id:int}", async (int id) =>
{
    Interlocked.Increment(ref requestCount);

    // Szimulált DB késleltetés: 100-300ms
    await Task.Delay(Random.Shared.Next(100, 300));

    var product = products.FirstOrDefault(p => p.Id == id);

    return product != null
        ? Results.Ok(product)
        : Results.NotFound(new { Error = "Termék nem található" });
});

// Termék keresése (lassú művelet - "komplex query")
app.MapGet("/api/search", async ([FromQuery] string q = "") =>
{
    Interlocked.Increment(ref requestCount);

    // Szimulált komplex DB lekérdezés: 500-1500ms
    await Task.Delay(Random.Shared.Next(500, 1500));

    var results = products
        .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
        .Take(10)
        .ToList();

    return Results.Ok(new
    {
        Query = q,
        Results = results,
        Count = results.Count
    });
});

// Kosárba tétel (írási művelet)
app.MapPost("/api/cart/add", async ([FromBody] AddToCartRequest request) =>
{
    Interlocked.Increment(ref requestCount);

    // Szimulált DB írás: 200-400ms
    await Task.Delay(Random.Shared.Next(200, 400));

    var product = products.FirstOrDefault(p => p.Id == request.ProductId);

    if (product == null)
        return Results.NotFound(new { Error = "Termék nem található" });

    if (product.Stock < request.Quantity)
        return Results.BadRequest(new { Error = "Nincs elegendő készlet" });

    cart.Add(new CartItem
    {
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        AddedAt = DateTime.Now
    });

    product.Stock -= request.Quantity;

    return Results.Ok(new
    {
        Success = true,
        Cart = cart.Count,
        Message = "Termék hozzáadva a kosárhoz"
    });
});

// Kosár tartalmának lekérése
app.MapGet("/api/cart", async () =>
{
    Interlocked.Increment(ref requestCount);

    await Task.Delay(Random.Shared.Next(100, 200));

    var cartWithDetails = cart.Select(item => new
    {
        item.ProductId,
        item.Quantity,
        item.AddedAt,
        Product = products.FirstOrDefault(p => p.Id == item.ProductId)
    }).ToList();

    return Results.Ok(new { Cart = cartWithDetails, Total = cart.Count });
});

// Rendelés leadása (nagyon lassú - tranzakció szimuláció)
app.MapPost("/api/order", async () =>
{
    Interlocked.Increment(ref requestCount);

    // Szimulált komplex tranzakció: 1000-2000ms
    await Task.Delay(Random.Shared.Next(1000, 2000));

    if (cart.Count == 0)
        return Results.BadRequest(new { Error = "A kosár üres" });

    var order = new Order
    {
        Id = orders.Count + 1,
        Items = cart.ToList(),
        CreatedAt = DateTime.Now,
        Total = cart.Sum(item =>
        {
            var product = products.First(p => p.Id == item.ProductId);
            return product.Price * item.Quantity;
        })
    };

    orders.Add(order);
    cart.Clear();

    return Results.Ok(new
    {
        Success = true,
        Order = order,
        Message = "Rendelés sikeresen leadva"
    });
});

// Statisztikák
app.MapGet("/api/stats", () =>
{
    Interlocked.Increment(ref requestCount);

    return Results.Ok(new
    {
        TotalProducts = products.Count,
        TotalOrders = orders.Count,
        CartItems = cart.Count,
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
║  • GET  /api/products/{id}                           ║
║  • GET  /api/search?q=termék                         ║
║  • POST /api/cart/add                                ║
║  • GET  /api/cart                                    ║
║  • POST /api/order                                   ║
║  • GET  /api/stats                                   ║
╚═══════════════════════════════════════════════════════╝
");

app.Run("http://localhost:5000");

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
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; }
}

record Order
{
    public int Id { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public int Total { get; set; }
}

record AddToCartRequest(int ProductId, int Quantity);