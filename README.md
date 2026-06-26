# Autonomous Self-Healing Observability Platform

A high-performance, real-time log ingestion, metrics aggregation, and AI-driven automated patching ecosystem built with .NET 9, React, Python, Redis, and PostgreSQL.

## System Architecture

### Data Flow

1. **Python Log Emulator** generates log entries from simulated microservices (AuthService, PaymentGateway, DataAnalytics) and sends them via HTTP POST to the backend API
2. **Backend API** (.NET 9) accepts logs through `POST /api/logs`, validates payload, applies rate limiting (100 req/min per IP), and pushes them to a Redis queue (`logs_queue`)
3. **LogWorker** (BackgroundService) consumes logs from the Redis queue, saves them to PostgreSQL, and tracks real-time metrics via `MetricsAggregator`
4. **Error Grouping** — for Error/Critical logs, the system groups them by error class using SHA256 hashing, calls Mistral-7B-Instruct via HuggingFace for analysis, and generates a C# patch suggestion
5. **SignalR Hub** — backend pushes new logs, metrics, alerts, and error group updates to all connected frontend clients in real-time via WebSocket
6. **Frontend** (React + TailwindCSS) displays a Grafana/Kibana-style dark-theme dashboard with live log stream, metrics, anomaly alerts, and AI error analysis

### Clean Architecture

```
src/
  Api/              — ASP.NET Core Web API (controllers, hubs, middleware, Program.cs)
  Application/      — Services + interfaces (ingestion, query, grouping, metrics, cache, AI)
  Domain/           — Entities, models, enums (LogEntry, ErrorGroup, ServiceMetric, LogLevel)
  Infrastructure/   — AI client, background workers, PostgreSQL, SignalR hub
frontend/           — React + TypeScript + Vite + TailwindCSS
emulator.py         — Python log generator
docker-compose.yml  — PostgreSQL + Redis + pgAdmin
```

- **Domain** — no dependencies, pure entities and enums
- **Application** — depends on Domain, defines interfaces and service implementations
- **Infrastructure** — depends on Application + Domain, implements AI, persistence, background services
- **Api** — depends on all layers, wires DI, controllers, middleware

## Core Features

### High-Performance Ingestion

Logs enter the system through a REST endpoint with input validation (service name max 100 chars, message max 10,000 chars) and sliding-window rate limiting. Valid logs are pushed to a Redis queue for asynchronous processing, decoupling ingestion from persistence.

### Lock-Free Concurrency

`MetricsAggregator` uses `ConcurrentDictionary` for service-level isolation, `ConcurrentQueue` for lock-free enqueue/dequeue, and `Interlocked` atomic operations for counters. A sliding 60-second window continuously evicts stale entries, keeping memory bounded at O(1) per service regardless of uptime.

### AI Self-Healing

Each unique error is analyzed by Mistral-7B-Instruct-v0.2 via the HuggingFace Inference API. The model receives the exception message and stack trace, and returns a structured JSON response with:
- **Summary** — a developer-friendly diagnostic explanation of the root cause
- **SuggestedPatch** — copy-pastable C# code that directly mitigates the failure

When the AI service is unavailable, an offline fallback provides pattern-matched diagnostics and patches for known exception types (NullReferenceException, HttpRequestException, OutOfMemoryException, UnauthorizedAccessException, etc.).

### Error Deduplication

Errors are deduplicated via SHA256 hashing of `message|stackTrace`. A static `ConcurrentDictionary` cache maps hashes to error group IDs, ensuring repeated errors increment a counter instead of triggering redundant AI calls and database inserts.

### Anomaly Detection

A 10-second sliding window per service checks for anomaly conditions: if log volume exceeds 10 entries AND error rate exceeds 50%, an alert is pushed to all frontend clients immediately. The alert banner auto-dismisses after 5 seconds.

### Dual-Mode Infrastructure

The system runs with or without Docker:
- **PostgreSQL unavailable** → falls back to EF Core InMemory database
- **Redis unavailable** → falls back to a Moq-powered in-memory queue (`ConcurrentQueue<RedisValue>`)

This enables zero-dependency local development and demos on machines without Docker.

### Real-Time Dashboard

- **Metrics Dashboard** — RPS and Error Rate bars per service, updated every 5 seconds
- **Live Log Stream** — real-time logs with service name and level filters, color-coded badges
- **AI Error Analysis** — expandable error group cards with AI summary, suggested C# patch, and "Create Pull Request" button
- **Alert Banner** — red banner on anomaly detection, auto-dismiss after 5 seconds
- **Connection Status** — green/red indicator for SignalR connection state

## Tech Stack

- **Backend:** .NET 9, EF Core, PostgreSQL (Npgsql), Redis (StackExchange.Redis), SignalR, HuggingFace Inference API, Moq (fallback)
- **Frontend:** React 18, TypeScript, Vite, TailwindCSS, @microsoft/signalr, lucide-react
- **Automation/ML:** Python 3, requests, colorama
- **Infrastructure:** Docker Compose (PostgreSQL 16, Redis 7, pgAdmin 4)

## Prerequisites

- Docker Desktop
- .NET 9 SDK
- Node.js (v18+) and npm
- Python 3.11+

## Installation & Usage

### One-Click Startup (Recommended)

- **Windows:** Run `run_all.bat` from the project root
- **Linux/macOS:** Run `chmod +x run_all.sh && ./run_all.sh`
- **Windows (no Docker):** Run `run_local_NO_DOCKER.bat`

The script provisions Docker containers, restores .NET dependencies, installs npm packages, and launches the backend, frontend, and log emulator in separate windows.

### Manual Execution

1. **Infrastructure:**
   ```bash
   docker compose up -d
   ```

2. **Backend API:**
   ```bash
   cd src/Api
   dotnet restore
   dotnet run
   ```

3. **Frontend Dashboard:**
   ```bash
   cd frontend
   npm install
   npm run dev
   ```

4. **Python Log Emulator:**
   ```bash
   pip install requests colorama
   python emulator.py
   ```

### Configuration

Edit `src/Api/appsettings.Development.json` (gitignored) with your local settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ailogs;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "HuggingFace": {
    "ApiKey": "your-huggingface-api-key",
    "ModelUrl": "https://api-inference.huggingface.co/models/mistralai/Mistral-7B-Instruct-v0.2"
  }
}
```

## Verification Checklist

- **Live Log Stream** — Open http://localhost:5173, watch real-time logs streaming from the emulator
- **Sliding Window Validation** — Stop the emulator, watch RPS drop to 0 after 60 seconds
- **Metrics Dashboard** — RPS and Error Rate bars update every 5 seconds per service
- **Anomaly Detection** — Error spike triggers a red alert banner at the top of the page
- **AI Code Generation** — Click an error log card to see AI diagnostics and auto-generated C# patch
- **Patch Application** — Click "Create Pull Request" to simulate patch application with branch creation
- **Rate Limiting** — Run multiple emulators in parallel to trigger HTTP 429 responses

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/logs` | Ingest a log entry (rate-limited) |
| GET | `/api/logs` | Query logs with optional service/level filters, pagination |
| GET | `/api/logs/errors/groups` | Get all error groups with AI analysis |
| POST | `/api/logs/errors/{id}/apply-patch` | Simulate patch application |
| WS | `/logs-stream` | SignalR hub for real-time logs, metrics, alerts |

## Security

- **Rate Limiting** — 100 requests per minute per IP (sliding window)
- **CORS** — Only `http://localhost:5173` allowed
- **Security Headers** — `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy`
- **Input Validation** — Service name (max 100 chars), message (max 10,000 chars)
- **Secret Protection** — `appsettings.Development.json` in `.gitignore`
- **Exception Handling** — Global middleware prevents stack trace leakage to clients

## Documentation

- [STARTUP.md](STARTUP.md) — Detailed startup guide with troubleshooting
- [additions.md](additions.md) — Technical breakdown of every feature
- [docs/architecture_ideas.md](docs/architecture_ideas.md) — Design rationale for each architectural decision
