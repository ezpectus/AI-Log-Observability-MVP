# Architecture Ideas & Design Rationale

This document explains the "why" behind every major architectural decision in the AI Log Observability MVP. Each section covers a feature, the problem it solves, alternatives considered, and why the chosen approach fits this project.

---

## 1. Redis Queue as Ingestion Buffer

### Problem

If logs are written directly to PostgreSQL on every incoming HTTP request, a traffic spike can exhaust the connection pool and bring down the API. The database becomes the bottleneck.

### Decision

Use Redis as a queue (`logs_queue`) between the API and the database. The API pushes logs to Redis via `ListLeftPushAsync` and returns HTTP 202 immediately. A background `LogWorker` pops logs via `ListRightPopAsync` and writes them to PostgreSQL at its own pace.

### Why Redis

Redis is in-memory, sub-millisecond, and supports list operations natively. It acts as a shock absorber — the API never blocks on database writes, and `LogWorker` processes logs sequentially without overwhelming PostgreSQL.

### Alternative Considered

Direct database writes with batch insert. Rejected because it couples ingestion throughput to database performance and offers no buffering under load spikes.

---

## 2. Dual-Mode Infrastructure (Docker / No-Docker)

### Problem

Not every developer has Docker installed. Not every demo environment supports containers. Requiring PostgreSQL and Redis to run the project creates a high barrier to entry.

### Decision

Build fallback modes directly into `Program.cs`:
- PostgreSQL unavailable → EF Core InMemory database
- Redis unavailable → Moq-powered in-memory queue backed by `ConcurrentQueue<RedisValue>`

The fallback is automatic — the system detects availability at startup and configures the appropriate provider.

### Why Moq for Redis

The `IDatabase` interface from StackExchange.Redis is well-defined. Moq can mock `ListLeftPushAsync` and `ListRightPopAsync` with a `ConcurrentQueue` backing store in a few lines. This gives a functionally equivalent queue that works in-process without any external dependencies.

### Alternative Considered

A separate in-memory `IConnectionMultiplexer` implementation. Rejected because it would require implementing the full interface surface. Moq stubs only the methods the application actually calls, which is faster to build and maintain.

---

## 3. Static Error Dedup Cache

### Problem

`ErrorGroupingService` is registered as Scoped in DI. `LogWorker` creates a new DI scope per log entry. An instance-level `_errorCache` field is recreated on every scope, so the cache never hits — every error triggers a database query and potentially an AI API call.

### Decision

Make `_errorCache` a `static readonly ConcurrentDictionary`. The cache persists for the lifetime of the process, shared across all scope instances.

### Why Static Instead of Singleton

Making `ErrorGroupingService` a singleton would also fix the cache, but it would change the service's lifetime semantics. `ErrorGroupingService` depends on `LogRepository` which depends on `ApplicationDbContext` (Scoped). A singleton holding a scoped dependency is a captive dependency bug — the DbContext would never be disposed. The static field approach fixes the cache without changing DI lifetimes.

### Alternative Considered

`IMemoryCache` from `Microsoft.Extensions.Caching.Memory`. Rejected because it adds a dependency for something a `static ConcurrentDictionary` already does. The cache is simple (hash → GUID) and doesn't need expiration policies or size limits.

---

## 4. SHA256 Error Hashing

### Problem

The same error can appear hundreds of times with identical message and stack trace. Without deduplication, each occurrence creates a new error group, triggers an AI API call, and clutters the dashboard.

### Decision

Compute `SHA256(message + "|" + stackTrace)` and use the hex hash as a cache key. If the hash exists in the cache, increment the existing group's counter instead of creating a new one.

### Why SHA256

Deterministic, collision-resistant, and fast enough for this use case. The hash is computed once per error log — not a hot path. Hex encoding makes it easy to use as a dictionary key.

### Alternative Considered

String comparison of `message + stackTrace`. Rejected because messages can be long (up to 10,000 chars) and stack traces can be multi-line. Hashing normalizes the comparison to a fixed-length 64-character string.

---

## 5. Sliding Window Metrics with ConcurrentQueue

### Problem

Real-time metrics (RPS, Error Rate) require tracking every log event with a timestamp. Over time, the number of tracked events grows unboundedly, consuming memory until the process crashes.

### Decision

Use a `ConcurrentQueue<LogEntryData>` per service with a 60-second sliding window. `CleanupOldLogs` evicts entries older than 60 seconds by peeking the front of the queue and dequeuing until the front entry is within the window.

### Why ConcurrentQueue

FIFO ordering guarantees the oldest entries are always at the front. `TryPeek` + `TryDequeue` are lock-free, so cleanup doesn't block `TrackLog` operations. The queue naturally maintains chronological order.

### Why Not Just Counters

A simple counter (increment on each log, decrement on timeout) doesn't track per-entry timestamps, so it can't calculate "logs in the last 10 seconds" vs "logs in the last 60 seconds" — both windows are needed (60s for metrics, 10s for anomaly detection).

### Alternative Considered

A ring buffer with fixed size. Rejected because the number of logs per 60 seconds varies — a ring buffer would either waste memory during low traffic or overflow during spikes. The queue approach adapts to actual volume.

---

## 6. Two-Condition Anomaly Detection

### Problem

Simple threshold alerts (e.g., "more than 5 errors in 10 seconds") produce false positives during normal traffic bursts and false negatives during low-traffic incidents.

### Decision

Require two conditions simultaneously:
1. Log volume > 10 in the last 10 seconds (sufficient activity)
2. Error rate > 50% in the same window (sufficient error density)

Both must be true for an anomaly alert.

### Why Two Conditions

- High volume of Info logs is normal traffic — not an anomaly
- A single Error in low traffic is noise — not a spike
- High volume AND high error rate together is a strong signal of a real incident

### Alternative Considered

Statistical anomaly detection (z-score, moving average + standard deviation). Rejected for an MVP because it requires tuning parameters and a warm-up period. The two-condition heuristic is simple, predictable, and works from the first log.

---

## 7. AI Analysis with Offline Fallback

### Problem

AI APIs (HuggingFace, OpenAI) can be unavailable, rate-limited, or slow. If the system depends on AI for error analysis, a service outage makes the entire error analysis panel useless.

### Decision

`HuggingFaceClient.SummarizeErrorAsync` always returns a result:
1. Try the HuggingFace API
2. If the request fails, response is empty, or JSON parsing fails → return `CreateOfflineFallback`
3. `CreateOfflineFallback` pattern-matches the error message against known exception types and returns hardcoded diagnostic + patch pairs

### Why Offline Fallback

The dashboard must always show useful content. A "AI unavailable" message is not useful. The fallback provides actionable diagnostics for the most common .NET exceptions (NullReferenceException, HttpRequestException, OutOfMemoryException, UnauthorizedAccessException) and a generic try/catch template for everything else.

### Alternative Considered

Caching previous AI responses and reusing them. Partially implemented via the `NeedsAiAnalysis` check in `ErrorGroupingService` — if a group already has non-fallback analysis, it's not regenerated. But for truly new error types, the fallback is necessary.

---

## 8. Mistral-7B-Instruct via HuggingFace

### Problem

Error analysis requires a model that can understand .NET stack traces, exception messages, and generate C# code. The model must be accessible via API without managing infrastructure.

### Decision

Use Mistral-7B-Instruct-v0.2 through the HuggingFace Inference API. The prompt is wrapped in `[INST]` tags (Mistral's instruction format) and requests a JSON response with `summary` and `suggestedPatch` fields.

### Why Mistral-7B

- Free tier on HuggingFace Inference API
- 7B parameters — fast enough for real-time analysis (typically 2-5 seconds)
- Instruction-tuned — follows structured output format reliably
- Good at code generation in C#

### Why HuggingFace API

No infrastructure to manage. No GPU required locally. The API handles model hosting, scaling, and inference. The tradeoff is latency and dependency on an external service — mitigated by the offline fallback.

### Alternative Considered

OpenAI GPT-4. Rejected because it requires a paid API key and adds cost per request. For an MVP, Mistral-7B on the free HuggingFace tier is sufficient. The `IAiAnalysisService` interface makes swapping providers trivial.

---

## 9. SignalR for Real-Time Streaming

### Problem

The dashboard needs to display logs, metrics, and alerts as they happen. HTTP polling introduces latency and unnecessary load.

### Decision

Use SignalR WebSocket hub at `/logs-stream`. The backend pushes events:
- `ReceiveLog` — on every processed log
- `ReceiveMetrics` — every 5 seconds
- `ReceiveAlert` — immediately on anomaly detection
- `ReceiveErrorGroup` — on error group creation/update

### Why SignalR

Native to ASP.NET Core, handles connection management, automatic reconnection, and fallback to long-polling. The frontend `@microsoft/signalr` client is well-documented and integrates cleanly with React.

### Frontend Memory Safety

The `useLogsStream` hook caps the log array at 500 entries via `.slice(0, 500)`. Without this, the array grows indefinitely as logs stream in, eventually crashing the browser tab. The `App.tsx` component properly cleans up SignalR listeners on unmount (`newConnection.off(...)`, `newConnection.stop()`).

### Alternative Considered

Server-Sent Events (SSE). Rejected because SignalR provides bidirectional communication (needed for future features like log subscription by service name) and is the standard real-time solution in the .NET ecosystem.

---

## 10. Rate Limiting with Sliding Window

### Problem

The log ingestion endpoint is public-facing. Without rate limiting, a single client can flood the Redis queue and exhaust backend resources.

### Decision

Use ASP.NET Core's built-in `RateLimiter` with a sliding window policy:
- 100 requests per minute per client IP
- 2 segments per window (more fair than fixed window)
- Queue of 10 oldest-first (smooth bursts)

### Why Sliding Window

A fixed window allows bursts at the boundary (e.g., 100 requests at 0:59 + 100 at 1:00 = 200 in 1 second). The sliding window with 2 segments distributes the limit more evenly across the window, reducing burst impact.

### Alternative Considered

Token bucket. Rejected because the built-in .NET `SlidingWindowRateLimiter` is simpler to configure and doesn't require additional packages. Token bucket is better for smoothing but overkill for an MVP.

---

## 11. Clean Architecture with Shared Compilation

### Problem

A full solution with separate projects for each layer (Domain, Application, Infrastructure, Api) requires project references, NuGet packaging, and complex `.csproj` files. For an MVP, this is overhead.

### Decision

Keep separate folders for each layer (`src/Domain/`, `src/Application/`, `src/Infrastructure/`, `src/Api/`) but compile all `.cs` files into the single `Api.csproj` via `<Compile Include="..\Application\**\*.cs" />` entries.

### Why Shared Compilation

- Preserves folder-level separation for readability and navigation
- Eliminates project reference complexity
- Single `dotnet build` command builds everything
- No inter-project dependency conflicts

### Alternative Considered

Separate `.csproj` per layer with project references. Rejected for an MVP because it adds build complexity and NuGet version management without meaningful benefit at this scale. The folder structure already enforces the dependency direction — Domain has no `using` statements pointing to other layers, Application only uses Domain, etc.

---

## 12. Mock Data Seeding on Startup

### Problem

An empty dashboard on first launch is a poor demo experience. Users need to see logs, metrics, and error groups immediately to understand what the system does.

### Decision

`MockDataSeederHostedService` runs on startup. If the database has zero log entries, it generates 18 mock logs via `MockDataSeeder.GenerateMockLogs(18)` with realistic service names, messages, stack traces, and timestamps spread across the last 30 minutes.

### Why 18 Logs

Enough to populate the dashboard with visible content across all log levels (Info, Warning, Error, Critical) without overwhelming the initial view. The emulator adds more in real-time after startup.

### Why Check Before Seeding

If the database already has logs (e.g., from a previous run), seeding is skipped. This prevents duplicate mock data on restarts and preserves real logs from the emulator.

---

## 13. Security Headers Middleware

### Problem

Web APIs without security headers are vulnerable to clickjacking, MIME-sniffing, and XSS attacks. These are low-effort, high-impact protections.

### Decision

Add inline middleware in `Program.cs` that sets four headers on every response:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`

### Why Inline Middleware

Four headers don't justify a separate middleware class. The inline `app.Use(async (context, next) => { ... })` approach is concise and keeps security configuration visible in `Program.cs` where it's easy to audit.

### Alternative Considered

Using `NWebsec` or `NetEscapades.AspNetCore.SecurityHeaders` packages. Rejected because they add dependencies for something that takes 4 lines of code. The built-in approach is transparent and dependency-free.

---

## 14. Global Exception Handling Middleware

### Problem

Unhandled exceptions in ASP.NET Core return a plain text 500 response with a stack trace in development mode. In production, this leaks internal details to clients.

### Decision

`ExceptionHandlingMiddleware` wraps the entire pipeline. Any unhandled exception is:
1. Logged via `ILogger`
2. Returned as a JSON 500 response with a generic error message — no stack traces, no file paths, no internal state

### Why Not Developer Exception Page

The developer exception page (`app.UseDeveloperExceptionPage()`) is useful in development but must be disabled in production. The custom middleware provides a consistent response format across all environments and never leaks internals.

---

## 15. Frontend Log Buffer Cap

### Problem

The `useLogsStream` hook prepends every incoming log to the `logs` state array. Over hours of running, this array grows to tens of thousands of entries, consuming hundreds of MB of browser memory and eventually crashing the tab.

### Decision

Cap the array at 500 entries: `setLogs((prev) => [receivedLog, ...prev].slice(0, 500))`.

### Why 500

500 entries is enough to see a meaningful history in the UI without scrolling endlessly. The initial load fetches 50 logs from the API, and 500 provides a 10x buffer for real-time streaming. Beyond 500, older logs are only accessible via the API with pagination.

### Alternative Considered

Virtual scrolling with a larger buffer (e.g., 5000). Rejected because it adds rendering complexity for marginal benefit. 500 entries in a scrolling list performs well in all browsers.

---

## Summary

| Feature | Problem Solved | Key Decision |
|---------|---------------|--------------|
| Redis Queue | DB bottleneck under load | Buffer between API and DB |
| Dual-Mode Infra | Docker dependency | InMemory + Moq fallback |
| Static Dedup Cache | Cache never hits (scoped service) | `static readonly` field |
| SHA256 Hashing | Duplicate error groups | Hash-based dedup |
| Sliding Window Queue | Unbounded memory growth | ConcurrentQueue + cleanup |
| Two-Condition Anomaly | False positives/negatives | Volume + error rate |
| AI Offline Fallback | AI service unavailable | Pattern-matched fallback |
| Mistral-7B via HF | Error analysis without infra | Free API, no GPU needed |
| SignalR Streaming | Real-time updates | WebSocket push, no polling |
| Rate Limiting | Endpoint abuse | Sliding window per IP |
| Clean Architecture | Code organization | Folder separation, shared compile |
| Mock Data Seeding | Empty dashboard on startup | Auto-seed 18 logs if DB empty |
| Security Headers | Web vulnerabilities | 4 headers via inline middleware |
| Exception Middleware | Stack trace leakage | Global try/catch, generic response |
| Log Buffer Cap | Browser memory leak | `.slice(0, 500)` |
