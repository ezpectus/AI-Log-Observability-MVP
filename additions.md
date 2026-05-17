# AI Log Observability MVP - Additions and New Features

## � One-Click Startup Automation

For quick and easy startup, the project includes cross-platform automation scripts:

### Windows (run_all.bat)

The Windows batch script opens 4 separate command windows:
1. **Docker Window** — Starts PostgreSQL, Redis, and pgAdmin containers
2. **Backend Window** — Restores dependencies and starts the .NET API on port 5000
3. **Frontend Window** — Installs npm packages and starts React dev server on port 5173
4. **Emulator Window** — Installs Python dependencies and starts the log emulator

Usage:
```bash
run_all.bat
```

Each window runs independently, allowing you to monitor logs for each component. Close individual windows to stop specific components, or close the main window to stop everything.

### Linux/macOS (run_all.sh)

The shell script starts all components in the background with proper signal handling:
- All processes run in the background
- Press Ctrl+C to gracefully stop all components
- Logs are output to the terminal

Usage:
```bash
chmod +x run_all.sh
./run_all.sh
```

### Manual Launch

If you prefer manual control or need to troubleshoot, see the manual step-by-step instructions in `STARTUP.md`.

---

## �📊 Real-time Analytics Features

### 1. ServiceMetric Entity

**File:** `src/Domain/Entities/ServiceMetric.cs`

Entity for storing real-time service metrics:

```csharp
public class ServiceMetric
{
    public string ServiceName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int Rps { get; set; }
    public double ErrorRate { get; set; }
}
```

### 2. MetricsAggregator Service

**File:** `src/Application/Services/MetricsAggregator.cs`

Thread-safe service for aggregating log metrics:

- **TrackLog(string serviceName, string logLevel)** — tracks each log, increments counters
- **GetMetrics()** — returns list of metrics for all services over the last 60 seconds
- **CheckAnomalies(string serviceName)** — detects anomalies:
  - If log volume > 10 in the last 10 seconds
  - And error percentage (ErrorRate) exceeds 50%
  - Returns true (anomaly spike detected)

### 3. LogWorker Integration

**File:** `src/Infrastructure/Background/LogWorker.cs`

Updates in LogWorker:

- When processing each log, `_metricsAggregator.TrackLog` is called
- Every 5 seconds, the worker collects metrics for all services and pushes them via SignalR:
  - `_hubContext.Clients.All.SendAsync("ReceiveMetrics", metricsList)`
- When an anomaly is detected, an alert is sent immediately:
  - `_hubContext.Clients.All.SendAsync("ReceiveAlert", new { Service = service, Message = "🚨 ANOMALY DETECTED: Sudden error spike!" })`

### 4. Program.cs Registration

**File:** `src/Api/Program.cs`

Added MetricsAggregator registration as a singleton:

```csharp
builder.Services.AddSingleton<MetricsAggregator>();
```

### 5. Frontend Updates

**File:** `frontend/src/App.tsx`

New features in the frontend:

- Subscription to SignalR events `"ReceiveMetrics"` and `"ReceiveAlert"`
- Metrics displayed in the top section of the dashboard with status bars:
  - **RPS** — requests per second (blue status bar)
  - **Error Rate** — error percentage (green if <50%, red if >50%)
- When an alert is received, a red Alert Banner appears at the top of the page
- The banner automatically disappears after 5 seconds or can be closed manually

## 🛠 Step-by-Step Launch

### Step 1: Infrastructure (Docker)

Start PostgreSQL and Redis in Docker containers:

```bash
docker-compose up -d
```

Check that containers are running:

```bash
docker-compose ps
```

The following containers should be running: `postgres`, `redis`, `pgadmin`.

### Step 2: Backend (.NET API)

Navigate to the backend folder and restore dependencies:

```bash
cd src/Api
dotnet restore
```

**Important:** Check the `appsettings.Development.json` file — it should contain actual local data for connecting to Docker containers:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ailogs;Username=postgres;Password=postgres"
  },
  "HuggingFace": {
    "ApiKey": "HF_TOKEN_PLACEHOLDER",
    "ModelUrl": "https://api-inference.huggingface.co/models/mistralai/Mistral-7B-Instruct-v0.2"
  }
}
```

Start the backend:

```bash
dotnet run
```

The backend will start on `http://localhost:5000`. You will see startup logs in the console.

### Step 3: Frontend (React)

Open a new terminal and navigate to the frontend folder:

```bash
cd frontend
```

Install dependencies:

```bash
npm install
```

Start the frontend:

```bash
npm run dev
```

The frontend will start on `http://localhost:5173`. Open this address in your browser — you will see an interactive web dashboard in Grafana/Kibana style with a dark theme.

### Step 4: Log Emulator (Python)

Open a new terminal in the project root and install Python dependencies:

```bash
pip install requests colorama
```

Start the emulator:

```bash
python emulator.py
```

The emulator will start generating logs and sending them to the backend.

## 🎯 Verifying New Features

### Verifying Real-time Metrics

1. In the top section of the dashboard, watch the metric cards for each service
2. RPS status bars show requests per second (updated every 5 seconds)
3. Error Rate status bars show error percentage (green if <50%, red if >50%)
4. When errors appear, Error Rate should increase

### Verifying Anomaly Detection

1. Wait a few minutes — the emulator will generate a sufficient number of logs
2. If log volume >10 in the last 10 seconds and error percentage >50%, a red Alert Banner will appear
3. The banner will show: "🚨 ANOMALY DETECTED: Sudden error spike!"
4. The banner will automatically disappear after 5 seconds or can be closed manually

## 📝 Technical Details

### Anomaly Detection Algorithm

1. MetricsAggregator stores logs in a sliding 60-second window
2. For each service, the following is calculated:
   - **RPS**: number of logs in 60 seconds / 60
   - **Error Rate**: (number of errors in 60 seconds / total logs in 60 seconds) * 100
3. Anomaly detection:
   - Checks a 10-second window
   - If log count > 10 AND Error Rate > 50% → anomaly
4. When an anomaly is detected, an alert is sent via SignalR

### Thread Safety

MetricsAggregator uses `ConcurrentDictionary` and `ConcurrentQueue` for thread-safe operations with multiple LogWorker threads. Atomic operations (`Interlocked.Increment`/`Interlocked.Decrement`) are used for log counting to eliminate race conditions.

### Update Frequency

- Metrics are updated every 5 seconds
- Logs are processed in real-time as they arrive from the Redis queue
- Alerts are sent immediately when an anomaly is detected

## 🤖 Auto-Fixer: Automatic Error Correction

### 1. Mistral AI Prompt Update

**File:** `src/Infrastructure/AI/OpenAiClient.cs`

Updated prompt to return JSON with two fields:
- **Analysis** — error explanation in English (root cause and fix)
- **PatchCode** — ready-to-use C# code snippet

The `SummarizeErrorAsync` method now returns a tuple `(string Analysis, string PatchCode)`.

### 2. Adding SuggestedPatch Field

**File:** `src/Domain/Models/ErrorGroup.cs`

Added field to store the suggested patch:

```csharp
public string SuggestedPatch { get; set; } = string.Empty;
```

### 3. ErrorGroupingService Update

**File:** `src/Application/Services/ErrorGroupingService.cs`

The service now saves the patch to the database when creating a new error group:

```csharp
var (analysis, patchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);

var newGroup = new ErrorGroup
{
    Summary = analysis,
    SuggestedPatch = patchCode,
    // ...
};
```

### 4. Error Deduplication with Hashing

**File:** `src/Application/Services/ErrorGroupingService.cs`

Implemented high-performance error deduplication using SHA256 hashing:

- Computes hash of message + stack trace
- Uses in-memory `ConcurrentDictionary<string, Guid>` cache for instant lookup
- Checks cache before hitting PostgreSQL to reduce database overhead
- Automatically removes stale cache entries on cache miss

### 5. Frontend Updates

**File:** `frontend/src/components/ErrorGroups.tsx`

Added code snippet display and apply button:
- Error card now shows `Suggested Fix` block with syntax highlighting
- Added "🤖 Create Pull Request (Auto-Fix)" button
- On click, sends POST request to backend endpoint `/api/errors/{id}/apply-patch`
- Button changes to "Patch applied! 🚀" on success

**File:** `frontend/src/types.ts`

Added field to the interface:

```typescript
export interface ErrorGroup {
  // ...
  suggestedPatch: string;
}
```

### 6. Patch Application Endpoint

**File:** `src/Api/Controllers/LogsController.cs`

Added endpoint to simulate patch application:

```csharp
[HttpPost("errors/{id}/apply-patch")]
public async Task<IActionResult> ApplyPatch(Guid id)
{
    await Task.Delay(2000);
    var branchName = $"fix/issue-{Guid.NewGuid().ToString().Substring(0, 8)}";
    return Ok(new { message = $"Patch successfully generated and applied to local branch {branchName}" });
}
```

### 7. Memory Management Optimization

**File:** `src/Application/Services/MetricsAggregator.cs`

Implemented efficient sliding window memory management:

- Periodic cleanup when log count exceeds 1000 entries
- Thread-safe cleanup using lock mechanism
- Atomic operations for log counting using `Interlocked.Increment`/`Interlocked.Decrement`
- Prevents memory leaks by removing logs older than 60 seconds

### 8. React SignalR Memory Leak Fix

**File:** `frontend/src/App.tsx`

Fixed React memory leak by properly cleaning up SignalR event listeners:

```typescript
return () => {
  newConnection.off('ReceiveMetrics');
  newConnection.off('ReceiveAlert');
  newConnection.stop();
};
```

## 🤖 Testing Auto-Fixer

### Verifying Patch Generation

1. Start all components (infrastructure, backend, frontend, emulator)
2. Wait a few minutes — the emulator will generate errors (Error/Critical)
3. Error group cards should appear in the right column of the dashboard
4. Click on an error card to expand it
5. Below the AI Analysis block, a `Suggested Fix` block with highlighted C# code should appear
6. The code should be realistic and solve the problem (e.g., try-catch, null check)

### Verifying Patch Application

1. In the expanded error card, click the "🤖 Create Pull Request (Auto-Fix)" button
2. The button should change state to "Applying..." with loading animation
3. After 2 seconds, the button should change to "Patch applied! 🚀" with a green checkmark
4. In the browser console (F12), you can see the successful response from the backend with the branch name

### Demo for Client

To demonstrate Auto-Fixer to a client:

1. **Show analysis**: Expand an error card and show the AI analysis in English
2. **Show patch**: Demonstrate the generated C# code with syntax highlighting
3. **Show application**: Click the "Auto-Fix" button and show how the system simulates patch application
4. **Explain**: The system not only analyzes errors but also provides ready-to-use code solutions

**Key advantage**: The system saves developers time by providing ready patches for typical errors (NullReferenceException, HttpRequestException, TimeoutException, etc.).
