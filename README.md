# 🤖 Autonomous Self-Healing Observability Platform

A high-performance, real-time log ingestion, metrics aggregation, and AI-driven automated patching ecosystem built with .NET 10, React, Python, Redis, and PostgreSQL.

## 🚀 System Architecture & Core Logic
- **High-Performance Ingestion:** Tailored for massive log streams, utilizing a deterministic SHA256 fast deduplication hashing pipeline before hitting PostgreSQL.
- **Lock-Free Concurrency:** Uses thread-safe sliding window metrics aggregators (ConcurrentQueue + Interlocked atomic operations) keeping metrics processing at O(1) bounded memory.
- **AI Self-Healing:** Seamlessly interfaces with Mistral AI via Hugging Face to evaluate runtime exceptions, deliver dynamic diagnostics, and generate structured C# auto-fix patches.

## 🛠 Tech Stack
- **Backend:** .NET 10 Core Web API, SignalR Hubs, StackExchange.Redis (Cache/Queue), Entity Framework Core / Dapper.
- **Frontend:** React, TypeScript, Vite, TailwindCSS (Live Log Streams, Anomaly Alerts, and AI Patching Panel).
- **Automation/ML:** Python 3.11 Log Stream Emulator.
- **Infrastructure:** Docker & Docker Compose (PostgreSQL, Redis).

## 📦 Usage & Installation

### Prerequisites
Ensure you have the following installed on your machine:
- Docker Desktop
- .NET 10 SDK
- Node.js (v18+) & npm
- Python 3.11+

### One-Click Startup (Recommended)
We provide an automated cross-platform orchestrator to boot the entire environment:
- **Windows:** Double-click or execute `run_all.bat` from the root directory.
- **Linux / macOS:** Run `chmod +x run_all.sh && ./run_all.sh`.

*The script automatically provisions Docker containers, triggers .NET migrations/restore, updates npm packages, and launches the native console windows for the backend, frontend, and log emulator.*

### Manual Execution Pipeline
If you prefer to start components individually, execute these commands in separate terminal sessions:

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
   python emulator.py
   ```

## 📊 Verification Checklist (What to Look For)
- **Live Log Stream:** Open http://localhost:5173 to see real-time distributed logs streaming from the Python emulator.
- **Sliding Window Validation:** Stop the emulator; watch the RPS metric drop back to 0 precisely after 60 seconds (O(1) memory bound cleanup).
- **AI Code Generation:** Click any error log in the UI stream to fetch instant English diagnostics and preview the auto-generated code patch layout.

Maintained with strict adherence to clean architecture and performant systems design.
