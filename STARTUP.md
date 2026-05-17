# AI Log Observability MVP - Startup Guide

## 🧠 Architecture Overview

AI Log Observability MVP is a real-time log monitoring system with AI-powered error analysis and anomaly detection. The architecture consists of the following components:

**Data Flow:**
1. **Log Emulator** (Python) generates logs from microservices (AuthService, PaymentGateway, DataAnalytics) and sends them via HTTP POST to the backend API
2. **Backend API** (.NET 9) accepts logs through the `/api/logs` endpoint, validates data, and checks Rate Limiting (100 requests/minute)
3. **Redis Queue** — logs are queued in Redis (`logs_queue`) for asynchronous processing
4. **LogWorker** — background service consumes logs from the Redis queue, saves them to PostgreSQL, and tracks real-time metrics
5. **MetricsAggregator** — thread-safe service that aggregates log statistics over the last 60 seconds and detects anomalies
6. **Error Grouping** — for errors (Error/Critical), the system groups them by error class and calls AI (Mistral-7B-Instruct-v0.2) for analysis
7. **SignalR Hub** — backend sends new logs, metrics, and alerts in real-time to the frontend via WebSocket
8. **Frontend** (React + Tailwind CSS) displays:
   - **Top section**: Metrics Dashboard — RPS and ErrorRate status bars for each service
   - **Left section**: Live Log Stream — black web console with real-time log stream via SignalR
   - **Right section**: AI Error Analysis — cards with grouped errors and suggestions from Mistral AI
   - **Alert Banner**: red banner when anomalies are detected

**Technologies:**
- Backend: .NET 9, EF Core, PostgreSQL, Redis (StackExchange.Redis), SignalR, HuggingFace Inference API
- Frontend: React (Vite), TypeScript, Tailwind CSS, @microsoft/signalr, lucide-react
- Infrastructure: Docker Compose (PostgreSQL, Redis, pgAdmin)

## 📦 Prerequisites

Before starting, ensure the following components are installed:

- **Docker** and **Docker Compose** — for running PostgreSQL and Redis
- **.NET 9 SDK** — for running the backend
- **Node.js** (v18+) and **npm** — for running the frontend
- **Python 3** and **pip** — for running the log emulator

Check versions:
```bash
docker --version
docker-compose --version
dotnet --version
node --version
npm --version
python --version
```

## � One-Click Startup (Recommended)

For quick startup, use the automation scripts that launch all components automatically:

### Windows
```bash
run_all.bat
```

This will open 4 separate command windows:
1. **Docker** — Starts PostgreSQL, Redis, and pgAdmin containers
2. **Backend** — Restores dependencies and starts the .NET API on port 5000
3. **Frontend** — Installs npm packages and starts React dev server on port 5173
4. **Emulator** — Installs Python dependencies and starts the log emulator

### Linux/macOS
```bash
chmod +x run_all.sh
./run_all.sh
```

This will start all components in the background with proper signal handling for graceful shutdown.

---

## � Manual Step-by-Step Launch

If you prefer manual control or need to troubleshoot, follow these steps:

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

**Interface:**
- **Top section**: Metrics Dashboard — RPS and ErrorRate status bars for each service (AuthService, PaymentGateway, DataAnalytics)
- **Left column (wide)**: Live Log Stream — real-time log stream with color coding by level (Info — green, Warning — yellow, Error/Critical — red)
- **Right column (narrow)**: AI Error Analysis — cards with grouped errors and AI suggestions from Mistral

### Step 4: Log Emulator (Python)

Open a new terminal in the project root and install Python dependencies:
```bash
pip install requests colorama
```

Start the emulator:
```bash
python emulator.py
```

The emulator will start generating logs and sending them to the backend. You will see colored output in the terminal:
- Format: `[TIME] [LEVEL] Service -> Message -> [API RESPONSE STATUS]`
- Response status: `[200 OK]` or `[429 Too Many Requests]` when Rate Limiting triggers

To stop the emulator, press `Ctrl+C`.

## 🎯 Demo Verification Scenarios

### Verification 1: Real-time Log Stream

1. Start all components (infrastructure, backend, frontend, emulator)
2. Open browser at `http://localhost:5173`
3. Watch the left column — logs should appear in real-time as the emulator generates them
4. Check color coding: Info (green), Warning (yellow), Error/Critical (red)
5. Use filters by Service Name and Log Level to filter logs

### Verification 2: Real-time Metrics

1. Watch the metric cards for each service in the top section of the dashboard
2. RPS status bars show requests per second (updated every 5 seconds)
3. Error Rate status bars show error percentage (green if <50%, red if >50%)
4. Error Rate should increase when errors appear

### Verification 3: Anomaly Detection

1. Wait a few minutes — the emulator will generate a sufficient number of logs
2. If log volume >10 in the last 10 seconds and error percentage >50%, a red Alert Banner will appear
3. The banner will show: "🚨 ANOMALY DETECTED: Sudden error spike!"
4. The banner will automatically disappear after 5 seconds or can be closed manually

### Verification 4: AI Error Analysis

1. Wait a few minutes — the emulator will generate errors (Error/Critical)
2. Look at the right column — cards with grouped errors should appear
3. Click on an error card — it will expand and show:
   - Error class name (ErrorClass)
   - Duplicate counter (Count)
   - Last seen time (LastSeenUtc)
   - AI analysis block from Mistral (purple border)
4. AI suggestions should be in English and contain a brief description of the cause and solution

### Verification 5: Rate Limiting (Spam Protection)

1. In the emulator terminal, watch API response statuses
2. Normally you should see `[200 OK]` or `[202 Accepted]`
3. To test Rate Limiting, you can temporarily reduce the limit in `Program.cs` or run multiple emulators in parallel
4. When exceeding the limit, you will see `[429 Too Many Requests]` — this means protection is working

### Verification 6: Data Validation

1. Try sending a log with empty ServiceName or Message via Postman/curl:
   ```bash
   curl -X POST http://localhost:5000/api/logs -H "Content-Type: application/json" -d '{"serviceName":"","level":0,"message":""}'
   ```
2. You should receive a validation error: `400 Bad Request` with an error message
3. This confirms that protection against invalid data is working

## 📝 Useful Commands

**Stopping all services:**
```bash
# Stop Docker containers
docker-compose down

# Stop backend (Ctrl+C in terminal)
# Stop frontend (Ctrl+C in terminal)
# Stop emulator (Ctrl+C in terminal)
```

**Viewing Docker logs:**
```bash
# PostgreSQL logs
docker-compose logs postgres

# Redis logs
docker-compose logs redis
```

**Recreating the database:**
```bash
docker-compose down -v
docker-compose up -d
```

## 🔒 Security

- **Rate Limiting**: 100 requests per minute from a single IP
- **CORS**: Only `http://localhost:5173` is allowed
- **Security Headers**: X-Content-Type-Options, X-Frame-Options, X-XSS-Protection
- **Data Validation**: ServiceName (up to 100 characters), Message (up to 10000 characters)
- **Secret Protection**: `appsettings.Development.json` in `.gitignore`
- **Anomaly Detection**: Automatic detection of error spikes (>50% in 10 seconds)

## 🐛 Troubleshooting

**Backend won't start:**
- Check that Docker containers are running: `docker-compose ps`
- Check `appsettings.Development.json` — it should have actual data
- Ensure port 5000 is not occupied by another application

**Frontend won't connect to SignalR:**
- Check that backend is running on `http://localhost:5000`
- Check CORS settings in `Program.cs`
- Open browser console (F12) to view errors

**Metrics not updating:**
- Check that LogWorker is running and processing logs
- Check backend console for errors when sending metrics
- Ensure SignalR connection is active in the browser

**Emulator not sending logs:**
- Check that backend is running and accessible
- Check API URL in `emulator.py`: `http://localhost:5000/api/logs`
- Ensure Python dependencies are installed: `pip install requests colorama`

**AI analysis not working:**
- Check HuggingFace token in `appsettings.Development.json`
- Ensure internet access is available for calling HuggingFace API
- Check backend logs for errors when calling AI

**Automation script issues:**
- **Windows**: If `run_all.bat` doesn't work, ensure you're running from the project root directory and have Administrator privileges if needed
- **Linux/macOS**: If `run_all.sh` doesn't work, run `chmod +x run_all.sh` first to make it executable
- If components don't start in the correct order, increase the sleep delays in the script
- For manual troubleshooting, use the manual step-by-step launch instructions above

---

**Happy using AI Log Observability MVP! 🚀**

