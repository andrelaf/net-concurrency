# Launches the ConcurrencyLab backend and frontend together (Windows / PowerShell).
# Backend -> http://localhost:5180   Frontend -> http://localhost:5173
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host 'Starting ASP.NET Core backend on http://localhost:5180 ...' -ForegroundColor Cyan
$backend = Start-Process -PassThru -WorkingDirectory "$root/backend/ConcurrencyLab.Api" `
    -FilePath 'dotnet' -ArgumentList 'run'

if (-not (Test-Path "$root/frontend/node_modules")) {
    Write-Host 'Installing frontend dependencies (first run) ...' -ForegroundColor Cyan
    Push-Location "$root/frontend"; npm install; Pop-Location
}

Write-Host 'Starting Vite dev server on http://localhost:5173 ...' -ForegroundColor Cyan
try {
    Push-Location "$root/frontend"
    npm run dev
}
finally {
    Pop-Location
    Write-Host 'Stopping backend ...' -ForegroundColor Cyan
    if ($backend -and -not $backend.HasExited) { Stop-Process -Id $backend.Id -Force }
}
