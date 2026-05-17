#!/bin/bash

echo "========================================"
echo "AI Log Observability MVP - Startup"
echo "========================================"
echo ""

# Function to cleanup on exit
cleanup() {
    echo ""
    echo "Stopping all components..."
    kill $DOCKER_PID 2>/dev/null
    kill $BACKEND_PID 2>/dev/null
    kill $FRONTEND_PID 2>/dev/null
    kill $EMULATOR_PID 2>/dev/null
    echo "All components stopped."
    exit 0
}

# Trap SIGINT and SIGTERM
trap cleanup SIGINT SIGTERM

# Window 1: Docker Infrastructure
echo "[1/4] Starting Docker containers..."
docker-compose up -d
echo ""
echo "Docker containers started successfully!"
docker-compose ps
echo ""

# Wait for Docker to initialize (10 seconds for PostgreSQL and Redis)
echo "Waiting for Docker containers to initialize..."
sleep 10

# Window 2: Backend API
echo "[2/4] Starting Backend API..."
cd src/Api
dotnet restore
echo ""
echo "Starting backend on http://localhost:5000..."
dotnet run &
BACKEND_PID=$!
cd ../..

# Wait for backend to start
sleep 5

# Window 3: Frontend
echo "[3/4] Starting Frontend..."
cd frontend
npm install
echo ""
echo "Starting frontend on http://localhost:5173..."
npm run dev &
FRONTEND_PID=$!
cd ..

# Wait for frontend to start
sleep 5

# Window 4: Log Emulator
echo "[4/4] Starting Log Emulator..."
pip install requests colorama
echo ""
echo "Starting emulator..."
python emulator.py &
EMULATOR_PID=$!

echo ""
echo "========================================"
echo "All components started successfully!"
echo "========================================"
echo ""
echo "Frontend: http://localhost:5173"
echo "Backend:  http://localhost:5000"
echo ""
echo "Press Ctrl+C to stop all components"
echo ""

# Wait for all background processes
wait
