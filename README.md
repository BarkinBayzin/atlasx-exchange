# AtlasX Exchange (Demo Project)

AtlasX is a **demo cryptocurrency exchange backend** built to demonstrate
**exchange-core fundamentals** and **production-grade backend engineering**
commonly required in crypto and fintech platforms.

The project focuses on **correctness, determinism, and system design**
rather than UI or commercial features.

---

## 🎯 Purpose

This project was built to showcase how a modern cryptocurrency exchange core
can be designed and implemented, including:

- Order matching with price-time priority
- Pre-trade risk validation
- Wallet & balance management with reserved funds
- Event-driven workflows
- Real-time market data delivery
- Security fundamentals (JWT, idempotency)

---

## 🧠 Core Features

### Trading & Matching
- REST-based Order API
- In-memory **price-time priority matching engine**
- Partial fills and multi-level price matching
- Deterministic trade execution

### Risk Management
- Pre-trade validation (limits, price bands)
- Client-based rate limiting
- Idempotent order submission

### Wallet & Ledger
- In-memory **double-entry style ledger**
- Available / Reserved balance separation
- Trade settlement logic
- Deposit & balance endpoints

### Real-time & Events
- WebSocket market data feed (order book snapshots & trades)
- Domain events with in-memory outbox
- RabbitMQ publisher (graceful fallback if unavailable)

### Observability & Security
- Structured logging
- OpenTelemetry tracing & metrics
- JWT-based authentication with scope-based authorization
- Idempotency handling for order requests

---

## 🧱 Architecture Overview

API Layer (ASP.NET Core)
├─ Authentication & Authorization
├─ Idempotency Handling
├─ REST & WebSocket Endpoints
│
Application Core
├─ Matching Engine (AtlasX.Matching)
├─ Risk Engine (AtlasX.Risk)
├─ Ledger & Settlement (AtlasX.Ledger)
│
Infrastructure
├─ Event Bus & Outbox
├─ RabbitMQ Publisher
├─ Observability


> ⚠️ Persistence is intentionally **in-memory** to keep the focus on
> exchange logic, determinism, and system design.

---

## 🛠 Tech Stack

- **.NET 8 / ASP.NET Core**
- PostgreSQL, Redis
- RabbitMQ
- Docker Compose
- xUnit (unit & integration tests)

---

## 🚀 Running the Project

```bash
docker compose up -d
dotnet run --project src/AtlasX.Api

```
## Swagger UI:
http://localhost:5000/swagger

## 📡 WebSocket Market Data

Use wscat to connect and receive order book snapshots and trades:
wscat -c ws://localhost:5000/ws/market?symbol=BTC-USD

## 🔐 Development JWT Token

Generate a development JWT token with scopes trade and wallet
using PowerShell:
```bash
$secret = "atlasx-dev-secret-key-please-change"
$header = @{ alg = "HS256"; typ = "JWT" } | ConvertTo-Json -Compress
$payload = @{
  sub = "demo-client"
  scope = "trade wallet"
  exp = [int][DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
} | ConvertTo-Json -Compress

function Base64Url([string]$text) {
  [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text)).TrimEnd("=") `
    -replace "\+","-" -replace "/","_"
}

$unsigned = "$(Base64Url $header).$(Base64Url $payload)"
$hmac = New-Object System.Security.Cryptography.HMACSHA256 `
  ([Text.Encoding]::UTF8.GetBytes($secret))
$sig = [Convert]::ToBase64String(
  $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))
).TrimEnd("=") -replace "\+","-" -replace "/","_"

"$unsigned.$sig"
```
## ⚠️ Disclaimer

This project is for educational and demonstration purposes only.
It is not production-ready and omits persistence, regulatory compliance,
and security hardening required for a real exchange.

## 📌 Why AtlasX?

AtlasX demonstrates how to think and design exchange systems,
not just how to implement APIs.
The emphasis is on system boundaries, correctness, and extensibility.