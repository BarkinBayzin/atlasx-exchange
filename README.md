# AtlasX Exchange (Demo Project)

AtlasX is a demo cryptocurrency exchange backend designed to showcase
exchange-core fundamentals and production-grade backend architecture.

## Features (Phase-based)
- Order API (REST)
- Matching Engine (price-time priority)
- Double-entry Ledger (available / reserved balances)
- Real-time WebSocket market data
- Event-driven architecture (RabbitMQ)
- Observability (metrics, tracing, logs)

## Tech Stack
- .NET 8 / ASP.NET Core
- PostgreSQL, Redis
- RabbitMQ
- Docker Compose

## Run
docker compose up -d
dotnet run --project src/AtlasX.Api
