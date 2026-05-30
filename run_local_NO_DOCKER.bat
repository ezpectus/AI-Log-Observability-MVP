@echo off
chcp 65001 > nul
echo ====================================================
echo AI Log Observability MVP - Startup (БЕЗ ДОКЕРА)
echo ====================================================
echo.

REM Шаг 1: Пропускаем Docker, сразу собираем Бэкенд
echo [1/3] Starting Backend API...
start "AI Log Observability - Backend" cmd /k "echo Starting Backend API... && cd src\Api && dotnet restore && echo. && echo Starting backend on http://localhost:5000... && dotnet run"

REM Небольшая пауза, пока бэк компилируется
timeout /t 3 /nobreak >nul

REM Шаг 2: Запускаем Фронтенд
echo [2/3] Starting Frontend...
start "AI Log Observability - Frontend" cmd /k "echo Starting Frontend... && cd frontend && npm install && echo. && echo Starting frontend on http://localhost:5173... && npm run dev"

REM Пауза перед эмулятором
timeout /t 3 /nobreak >nul

REM Шаг 3: Запускаем Эмулятор логов
echo [3/3] Starting Log Emulator...
start "AI Log Observability - Emulator" cmd /k "echo Starting Log Emulator... && pip install requests colorama && echo. && echo Starting emulator... && python emulator.py"

echo.
echo ====================================================
echo All components started successfully (In-Memory Mode)!
echo ====================================================
echo.
echo Frontend: http://localhost:5173
echo Backend:  http://localhost:5000
echo.
echo Close this window to stop monitoring.
echo.
pause