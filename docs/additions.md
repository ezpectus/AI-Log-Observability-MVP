# AI Log Observability MVP — Additions & Technical Breakdown

Detailed description of every integrated feature: where it lives, how it works, which files are involved.

---

## 1. One-Click Startup Automation

**Files:** `run_all.bat`, `run_all.sh`, `run_local_NO_DOCKER.bat`

Cross-platform orchestrator scripts that launch all components in separate terminal windows. The Windows version opens 4 CMD windows: Docker, Backend, Frontend, Emulator — with timed delays between them for correct startup order. The `run_local_NO_DOCKER.bat` variant skips Docker entirely and runs the system in in-memory mode (EF Core InMemory + Moq-backed Redis queue).

---

## 2. MetricsAggregator — Real-Time Metrics

**Files:**
- `src/Domain/Entities/ServiceMetric.cs` — entity: `ServiceName`, `Rps`, `ErrorRate`, `Timestamp`
- `src/Application/Interfaces/Services/MetricsAggregator.cs` — singleton aggregator
- `src/Infrastructure/Background/LogWorker.cs` — broadcasts metrics every 5 seconds via SignalR

### Mechanics

`MetricsAggregator` maintains a `ConcurrentDictionary<string, ServiceMetricsData>` keyed by service name. Each entry holds a `ConcurrentQueue<LogEntryData>` with timestamp + level.

- **TrackLog(serviceName, logLevel)** — enqueues a timestamped entry, atomically increments `TotalLogs` via `Interlocked.Increment`. Triggers cleanup when queue exceeds 1000 entries.
- **GetMetrics()** — cleans up entries older than 60 seconds, calculates RPS = count / 60, ErrorRate = (Error + Critical) / total * 100. Returns a `List<ServiceMetric>`.
- **CleanupOldLogs()** — `TryPeek` + `TryDequeue` loop under `lock(_cleanupLock)`, `Interlocked.Decrement` on each evicted entry.

### Thread Safety

`ConcurrentDictionary` for service-level isolation, `ConcurrentQueue` for lock-free enqueue/dequeue, `Interlocked` for atomic counters, `lock` only in the cleanup critical section.

---

## 3. Anomaly Detection

**Files:** `MetricsAggregator.cs` (`CheckAnomalies` method), `LogWorker.cs`, `frontend/src/App.tsx`

### Algorithm

Checks a 10-second sliding window per service:
1. If total logs in 10s <= 10 → not an anomaly (insufficient volume)
2. Calculates error rate over the same 10s window
3. If error rate > 50% → anomaly detected

When triggered, `LogWorker` sends a `ReceiveAlert` event via SignalR. The frontend displays a red banner that auto-dismisses after 5 seconds or can be closed manually.

### Two-Condition Rationale

High volume of Info logs is normal traffic. A single Error in low traffic is not a spike. Only the combination of high volume AND high error rate within a short window is a reliable signal of a real problem.

---

## 4. AI Self-Healing Error Analysis

**Files:**
- `src/Infrastructure/AI/OpenAiClient.cs` — class `HuggingFaceClient`, implements `IAiAnalysisService`
- `src/Application/Interfaces/IAiAnalysisService.cs` — interface
- `src/Application/Interfaces/Services/ErrorGroupingService.cs` — calls AI on new error groups
- `src/Api/Program.cs` — registered as scoped via `IHttpClientFactory` with 30s timeout

### Flow

1. `SummarizeErrorAsync(message, stackTrace)` builds a Mistral-7B-Instruct prompt wrapped in `[INST]` tags
2. The prompt requests a JSON response with two fields: `summary` (diagnostic explanation) and `suggestedPatch` (C# code snippet)
3. POST to HuggingFace Inference API with Bearer auth
4. Response parsing pipeline: `ExtractGeneratedText` → `StripPromptEcho` (removes prompt echo after `[/INST]`) → `TryParseStructuredAnalysis` (extracts JSON object, deserializes)
5. `CleanPatch` — strips markdown code fences (` ```csharp ... ``` `)

### Offline Fallback

`CreateOfflineFallback` — pattern-matches the error message against known exception types (`UnauthorizedAccessException`, `OutOfMemoryException`, `HttpRequestException` / timeout / DNS, and a generic fallback). Returns a hardcoded diagnostic + patch pair. Guarantees a useful response even when the AI service is unreachable.

### Null-Safe API Key Handling

`NormalizeApiKey` guards against null/empty input with `string.IsNullOrWhiteSpace` before calling `Trim()`, preventing `NullReferenceException` when the key is missing from configuration.

---

## 5. Error Deduplication with SHA256 Hashing

**Files:** `ErrorGroupingService.cs` — `ComputeErrorHash`, `HandleErrorGroupAsync`, `ExtractErrorClass`

### Mechanics

1. **ExtractErrorClass** — parses the exception type from the message (text before `:` or first space; fallback: first 50 characters)
2. **ComputeErrorHash** — `SHA256.Create()` over `message|stackTrace`, output as hex string
3. **Static cache lookup** — `static ConcurrentDictionary<string, Guid>` maps hash to error group ID. Cache hit → increment count, update `LastSeenUtc`, optionally regenerate AI analysis if missing or fallback
4. **Database lookup** — `GetErrorGroupByErrorClassAsync(errorClass)`. Found → increment, update, add to cache
5. **New group creation** — call AI, save to database, add to cache. If AI fails, create group with fallback summary

### Why Static Cache

`ErrorGroupingService` is registered as Scoped. `LogWorker` creates a new DI scope per log. An instance field would be recreated every time and never hit. `static readonly` ensures the cache persists for the lifetime of the process.

---

## 6. Dual-Mode Infrastructure (Docker / No-Docker)

**Files:** `src/Api/Program.cs`, `docker-compose.yml`

### Database

In `Program.cs` during `DbContext` configuration:
- Connection string set + not Development → attempt PostgreSQL connection via `NpgsqlConnection.Open()`
- Success → `UseNpgsql(connectionString)`
- Failure → `UseInMemoryDatabase("LogDb")`
- Development → `UseInMemoryDatabase` directly

### Redis

- `ConfigurationOptions.Parse` + `AbortOnConnectFail = true` + `ConnectTimeout = 2000ms`
- Success → real `IConnectionMultiplexer` + `IDatabase`
- Failure → Moq mock backed by `ConcurrentQueue<RedisValue>`:
  - `ListLeftPushAsync` → `inMemoryQueue.Enqueue`
  - `ListRightPopAsync` → `inMemoryQueue.TryDequeue`
  - `IsConnected` → always returns `true`

### Result

The system runs without Docker, without PostgreSQL, without Redis. The in-memory mode is functionally equivalent for development and demos.

---

## 7. Rate Limiting

**Files:** `Program.cs` (configuration), `LogsController.cs` (`[EnableRateLimiting("LogIngestionPolicy")]`)

Sliding window rate limiter per client IP:
- 100 requests per minute
- Window: 60 seconds, 2 segments
- Queue: oldest-first, max 10 queued

Exceeding the limit returns HTTP 429 Too Many Requests. The Python emulator displays 429 status in colored output.

---

## 8. Security Headers & Exception Middleware

**Files:** `src/Api/Middleware/ExceptionHandlingMiddleware.cs`, `Program.cs`

### Security Headers

Added via inline middleware in `Program.cs`:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`

### Exception Handling

`ExceptionHandlingMiddleware` wraps the entire pipeline. Unhandled exceptions are logged via `ILogger` and returned as a JSON 500 response with a generic error message — no stack traces or internal details leaked to the client.

---

## 9. SignalR Real-Time Streaming

**Files:**
- `src/Infrastructure/Realtime/LogHub.cs` — hub class (empty, used as hub endpoint)
- `src/Api/Hubs/LogHub.cs` — hub with `SubscribeToLogs` / `UnsubscribeFromLogs` group methods (unused, dead code)
- `src/Api/Program.cs` — `MapHub<LogHub>("/logs-stream")` (maps `Infrastructure.Realtime.LogHub`)

### Events

- **ReceiveLog** — pushed by `LogWorker` on every processed log
- **ReceiveMetrics** — pushed every 5 seconds with `List<ServiceMetric>`
- **ReceiveAlert** — pushed immediately when anomaly detected
- **ReceiveErrorGroup** — pushed when an error group is created or updated

### Frontend Handling

- `useLogsStream.ts` — subscribes to `ReceiveLog`, caps the array at 500 entries to prevent unbounded memory growth
- `App.tsx` — subscribes to `ReceiveMetrics` and `ReceiveAlert`, properly cleans up listeners on unmount
- `ErrorGroups.tsx` — subscribes to `ReceiveErrorGroup`, upserts into local state, polls `/api/logs/errors/groups` every 10 seconds as fallback

---

## 10. Mock Data Seeding

**Files:**
- `src/Application/Interfaces/Services/MockDataSeeder.cs` — static generator
- `src/Infrastructure/Background/MockDataSeederHostedService.cs` — hosted service

### Mechanics

On startup, `MockDataSeederHostedService` waits 500ms, then checks if the database has any logs. If empty, it generates 18 mock log entries via `MockDataSeeder.GenerateMockLogs(18)` with:
- 8 service names (LLMOrchestrator, TokenCounter, LatencyMonitor, etc.)
- 29 message templates across Info / Warning / Error / Critical levels
- Realistic stack traces for Error/Critical entries
- Timestamps spread across the last 30 minutes

If the database already has logs, seeding is skipped. Database errors are caught and logged — the application continues normally.

---

## 11. Patch Application Endpoint

**Files:** `src/Api/Controllers/LogsController.cs` — `ApplyPatch` method

### Endpoint

```
POST /api/logs/errors/{id}/apply-patch
```

Simulates patch application with a 2-second delay, then returns a JSON response with a generated branch name (`fix/issue-{guid-prefix}`). The frontend `ErrorGroups.tsx` component calls this endpoint when the user clicks "Create Pull Request", showing loading state and success confirmation.

---

## 12. Frontend Dashboard

**Files:**
- `frontend/src/App.tsx` — main layout, metrics, alerts
- `frontend/src/components/LogStream.tsx` — live log stream with filters
- `frontend/src/components/ErrorGroups.tsx` — AI error analysis panel
- `frontend/src/hooks/useLogsStream.ts` — SignalR connection + log state
- `frontend/src/types.ts` — TypeScript interfaces

### Stack

React 18, TypeScript, Vite, TailwindCSS, `@microsoft/signalr`, `lucide-react` icons.

### Features

- **Metrics Dashboard** — RPS and Error Rate bars per service, updated every 5s
- **Live Log Stream** — real-time logs with service name / level filters, color-coded badges
- **AI Error Analysis** — expandable error group cards with AI summary, suggested patch, and "Create Pull Request" button
- **Alert Banner** — red banner on anomaly, auto-dismiss after 5s
- **Connection Status** — green/red indicator for SignalR connection
- **Memory Safety** — log array capped at 500 entries via `.slice(0, 500)`

---

## 13. Python Log Emulator

**Files:** `emulator.py`

### Mechanics

Generates log entries from 3 microservices (AuthService, PaymentGateway, DataAnalytics) with weighted distribution:
- 60% Info, 20% Warning, 15% Error, 5% Critical

Error and Critical entries include realistic stack traces (NullReferenceException, HttpRequestException, TimeoutException, DbUpdateException, UnauthorizedAccessException, etc.).

Sends logs via HTTP POST to `http://localhost:5000/api/logs` with 0.5–2.0 second random delays. Colored terminal output via `colorama` shows timestamp, level, service, message, and API response status (200/202/429/500).

---

## 14. Stability Fixes

### Static Dedup Cache

**File:** `ErrorGroupingService.cs`

Changed `_errorCache` from instance field to `static readonly`. Without this fix, the cache was recreated on every log entry (because `LogWorker` creates a new DI scope per log and `ErrorGroupingService` is Scoped), causing every error to hit the database and trigger AI analysis instead of being deduplicated.

### Frontend Log Buffer Cap

**File:** `frontend/src/hooks/useLogsStream.ts`

Added `.slice(0, 500)` to the log array update. Without this, the array grew indefinitely as logs streamed in, eventually causing browser tab crashes due to memory exhaustion.

### Null-Safe API Key Normalization

**File:** `src/Infrastructure/AI/OpenAiClient.cs`

Added `string.IsNullOrWhiteSpace(apiKey)` guard in `NormalizeApiKey` before calling `Trim()`. Prevents `NullReferenceException` when the HuggingFace API key is missing from configuration.

---

## 15. Docker Infrastructure

**File:** `docker-compose.yml`

Three containers:
- **PostgreSQL 16 Alpine** — port 5432, database `ai_log_observability`, healthcheck via `pg_isready`
- **Redis 7 Alpine** — port 6379, healthcheck via `redis-cli ping`
- **pgAdmin 4** — port 8080, email `admin@admin.com` / password `admin`

PostgreSQL data persisted via named volume `postgres_data`.

---

## 16. Project Structure

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

### Clean Architecture

- **Domain** — no dependencies, pure entities and enums
- **Application** — depends on Domain, defines interfaces and service implementations
- **Infrastructure** — depends on Application + Domain, implements AI, persistence, background services
- **Api** — depends on all layers, wires DI, controllers, middleware

All `Application`, `Domain`, and `Infrastructure` `.cs` files are compiled into the `Api` project via `<Compile Include="..\Application\**\*.cs" />` in `Api.csproj`.
