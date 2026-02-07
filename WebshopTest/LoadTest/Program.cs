using NBomber.CSharp;
using NBomber.Http.CSharp;

// HTTP Client létrehozása
var httpClient = new HttpClient();

// Termékek lekérése (gyakori)
var browseProducts = Scenario.Create("browse_products", async context =>
{
    var request = Http.CreateRequest("GET", "http://localhost:5000/api/products?limit=20")
        .WithHeader("Accept", "application/json");

    var response = await Http.Send(httpClient, request);

    return response;
})
.WithLoadSimulations(
    Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);

// Termék részletek (közepes)
var viewProduct = Scenario.Create("view_product", async context =>
{
    var productId = Random.Shared.Next(1, 101);
    var request = Http.CreateRequest("GET", $"http://localhost:5000/api/products/{productId}")
        .WithHeader("Accept", "application/json");

    var response = await Http.Send(httpClient, request);

    return response;
})
.WithLoadSimulations(
    Simulation.Inject(rate: 30, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);

// Keresés (lassú)
var search = Scenario.Create("search", async context =>
{
    var request = Http.CreateRequest("GET", "http://localhost:5000/api/search?q=elektronika")
        .WithHeader("Accept", "application/json");

    var response = await Http.Send(httpClient, request);

    return response;
})
.WithLoadSimulations(
    Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);

NBomberRunner
    .RegisterScenarios(browseProducts, viewProduct, search)
    .Run();

Console.WriteLine("\nTeszt befejezve! Nyomd meg az ENTER-t a kilépéshez.");
Console.ReadLine();