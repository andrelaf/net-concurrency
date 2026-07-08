#!/usr/bin/env bash
# Launches the ConcurrencyLab backend and frontend together (bash).
# Backend -> http://localhost:5180   Frontend -> http://localhost:5173
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Starting ASP.NET Core backend on http://localhost:5180 ..."
( cd "$root/backend/ConcurrencyLab.Api" && dotnet run ) &
backend_pid=$!

cleanup() {
  echo "Stopping backend ..."
  kill "$backend_pid" 2>/dev/null || true
}
trap cleanup EXIT

if [ ! -d "$root/frontend/node_modules" ]; then
  echo "Installing frontend dependencies (first run) ..."
  ( cd "$root/frontend" && npm install )
fi

echo "Starting Vite dev server on http://localhost:5173 ..."
cd "$root/frontend" && npm run dev
