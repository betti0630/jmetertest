# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the API project
dotnet build WebshopTest/WebshopTest/WebshopTest.csproj

# Build the entire solution
dotnet build WebshopTest/WebshopTest.slnx

# Run the API locally (requires local PostgreSQL on localhost:5432)
dotnet run --project WebshopTest/WebshopTest/WebshopTest.csproj

# Run with Docker Compose (API + PostgreSQL)
docker-compose up --build

# Run the NBomber load test (requires the API to be running)
dotnet run --project WebshopTest/LoadTest/LoadTest.csproj

# Run the Locust load test (requires the API to be running)
cd LoadTest_Locust && locust
```

## Architecture

This is a .NET 10.0 webshop test API designed for load testing, with two separate load testing tools.

### Projects (solution: `WebshopTest/WebshopTest.slnx`)

- **WebshopTest/WebshopTest/** — ASP.NET Core minimal API serving a mock e-commerce backend. Uses EF Core with PostgreSQL (`WebshopDbContext`). All endpoint definitions, models (`Product`, `CartItem`, `Order`), and the DbContext live in `Program.cs`. Seeds 100 random products on first startup. Listens on port 5000.
- **WebshopTest/LoadTest/** — NBomber console app with three load scenarios: `browse_products` (50 req/s), `view_product` (30 req/s), `search` (10 req/s), each running for 30 seconds.
- **LoadTest_Locust/** — Python Locust alternative load test (`locustfile.py`) with weighted tasks for browsing, viewing, searching, and cart operations.

### Docker Setup

`docker-compose.yml` at repo root orchestrates:
- `db`: PostgreSQL 17 with health check. Database `webshop`, user/password `postgres`.
- `api`: Builds from `WebshopTest/WebshopTest/Dockerfile` (build context is `./WebshopTest`). Connection string injected via `ConnectionStrings__DefaultConnection` env var.

The API has a startup retry loop (10 attempts) for database connectivity. The Dockerfiles use multi-stage builds with `mcr.microsoft.com/dotnet/sdk:10.0` for build and `aspnet:10.0`/`runtime:10.0` for final images.

### API Endpoints

`GET /health`, `GET /api/products`, `GET /api/products/{id}`, `GET /api/search?q=`, `POST /api/cart/add`, `GET /api/cart`, `POST /api/order`, `GET /api/stats`. Swagger UI at `/swagger`.

### Data Model

Cart items with `OrderId == null` represent the active cart. When an order is placed, cart items get assigned to the order via `OrderId`. The legacy `WeatherForecast` controller is leftover from the project template.

## Notes

- The API binds to `0.0.0.0:5000` (all interfaces) so it works inside Docker containers.
- Comments in the codebase are in Hungarian.
- `appsettings.json` has the local dev connection string; Docker Compose overrides it via environment variable.
