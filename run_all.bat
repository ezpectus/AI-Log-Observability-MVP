@echo off
echo ========================================
echo AI Log Observability MVP - Startup
echo ========================================
echo.

REM Window 1: Docker Infrastructure
echo [1/4] Starting Docker containers...
start "AI Log Observability - Docker" cmd /k "echo Starting Docker containers... && docker-compose up -d && echo. && echo Docker containers started successfully! && echo. && docker-compose ps && echo. && echo Press any key to stop containers... && pause && docker-compose down"

REM Wait for Docker to initialize (10 seconds for PostgreSQL and Redis)
echo Waiting for Docker containers to initialize...
timeout /t 10 /nobreak >nul

REM Window 2: Backend API
echo [2/4] Starting Backend API...
start "AI Log Observability - Backend" cmd /k "echo Starting Backend API... && cd src\Api && dotnet restore && echo. && echo Starting backend on http://localhost:5000... && dotnet run"

REM Wait for backend to start
timeout /t 5 /nobreak >nul

REM Window 3: Frontend
echo [3/4] Starting Frontend...
start "AI Log Observability - Frontend" cmd /k "echo Starting Frontend... && cd frontend && npm install && echo. && echo Starting frontend on http://localhost:5173... && npm run dev"

REM Wait for frontend to start
timeout /t 5 /nobreak >nul

REM Window 4: Log Emulator
echo [4/4] Starting Log Emulator...
start "AI Log Observability - Emulator" cmd /k "echo Starting Log Emulator... && pip install requests colorama && echo. && echo Starting emulator... && python emulator.py"

echo.
echo ========================================
echo All components started successfully!
echo ========================================
echo.
echo Frontend: http://localhost:5173
echo Backend:  http://localhost:5000
echo.
echo Close this window to stop all components
echo (or close individual windows to stop specific components)
echo.
pause
