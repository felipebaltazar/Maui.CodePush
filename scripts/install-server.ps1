# ============================================================================
# CodePush Server — Self-hosted Installation Script (Windows PowerShell)
# ============================================================================
#
# Usage:
#   irm https://raw.githubusercontent.com/felipebaltazar/Maui.CodePush/main/scripts/install-server.ps1 | iex
#
# Or download and run manually:
#   .\install-server.ps1
#
# Requirements:
#   - Docker Desktop for Windows
#   - A MongoDB instance (local or Atlas)
#
# ============================================================================

$ErrorActionPreference = "Stop"

function Write-Info($msg)    { Write-Host "  ● $msg" -ForegroundColor Cyan }
function Write-Ok($msg)      { Write-Host "  ✔ $msg" -ForegroundColor Green }
function Write-Warn($msg)    { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err($msg)     { Write-Host "  ✖ $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "  CodePush Server — Self-hosted Setup" -ForegroundColor Magenta
Write-Host "  ─────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── Check prerequisites ─────────────────────────────────────────────────────

Write-Info "Checking prerequisites..."

try {
    $dockerVersion = docker --version 2>$null
    Write-Ok "Docker $($dockerVersion -replace 'Docker version ','')"
} catch {
    Write-Err "Docker not found. Install Docker Desktop from https://www.docker.com/products/docker-desktop/"
}

# ── Choose install directory ─────────────────────────────────────────────────

$defaultDir = "$env:USERPROFILE\codepush-server"
$installDir = Read-Host "  Install directory [$defaultDir]"
if ([string]::IsNullOrWhiteSpace($installDir)) { $installDir = $defaultDir }

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Set-Location $installDir

Write-Info "Installing to $installDir"

# ── Collect configuration ────────────────────────────────────────────────────

Write-Host ""
Write-Host "  Configuration" -ForegroundColor White
Write-Host "  ─────────────" -ForegroundColor DarkGray
Write-Host ""

$mongoConn = Read-Host "  MongoDB connection string [mongodb://localhost:27017]"
if ([string]::IsNullOrWhiteSpace($mongoConn)) { $mongoConn = "mongodb://localhost:27017" }

$mongoDb = Read-Host "  MongoDB database name [codepush]"
if ([string]::IsNullOrWhiteSpace($mongoDb)) { $mongoDb = "codepush" }

$jwtSecret = -join ((1..64) | ForEach-Object { [char](Get-Random -Minimum 33 -Maximum 126) })
Write-Info "Generated JWT secret"

$port = Read-Host "  Server port [8080]"
if ([string]::IsNullOrWhiteSpace($port)) { $port = "8080" }

Write-Host ""

# ── Create .env ──────────────────────────────────────────────────────────────

$envContent = @"
MONGODB_CONNECTION_STRING=$mongoConn
MONGODB_DATABASE_NAME=$mongoDb
CODEPUSH_JWT_SECRET=$jwtSecret
CODEPUSH_PORT=$port
"@

Set-Content -Path ".env" -Value $envContent
Write-Ok "Created .env"

# ── Create docker-compose.yml ────────────────────────────────────────────────

$includeMongo = $false
if ($mongoConn -match "localhost|127\.0\.0\.1") {
    $answer = Read-Host "  Include local MongoDB container? [Y/n]"
    if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match "^[Yy]") {
        $includeMongo = $true
    }
}

if ($includeMongo) {
    $composeContent = @'
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "${CODEPUSH_PORT:-8080}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=mongodb://mongo:27017
      - MONGODB_DATABASE_NAME=${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=${CODEPUSH_JWT_SECRET}
    volumes:
      - codepush-uploads:/app/uploads
    depends_on:
      - mongo

  mongo:
    image: mongo:7
    container_name: codepush-mongo
    restart: unless-stopped
    volumes:
      - codepush-mongo-data:/data/db

volumes:
  codepush-uploads:
  codepush-mongo-data:
'@
    # Fix .env for internal network
    $envContent = $envContent -replace "MONGODB_CONNECTION_STRING=.*", "MONGODB_CONNECTION_STRING=mongodb://mongo:27017"
    Set-Content -Path ".env" -Value $envContent
    Write-Ok "Added local MongoDB container"
} else {
    $composeContent = @'
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "${CODEPUSH_PORT:-8080}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=${MONGODB_CONNECTION_STRING}
      - MONGODB_DATABASE_NAME=${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=${CODEPUSH_JWT_SECRET}
    volumes:
      - codepush-uploads:/app/uploads

volumes:
  codepush-uploads:
'@
}

Set-Content -Path "docker-compose.yml" -Value $composeContent
Write-Ok "Created docker-compose.yml"

# ── Pull and start ───────────────────────────────────────────────────────────

Write-Host ""
Write-Info "Pulling Docker image..."
docker compose pull

Write-Host ""
Write-Info "Starting server..."
docker compose up -d

Write-Host ""
Write-Info "Waiting for server to start..."
Start-Sleep -Seconds 5

# ── Verify ───────────────────────────────────────────────────────────────────

try {
    $response = Invoke-WebRequest -Uri "http://localhost:$port/api/auth/me" -UseBasicParsing -ErrorAction SilentlyContinue
} catch {}

Write-Host ""
Write-Host "  ─────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
Write-Ok "CodePush Server is running!"
Write-Host ""
Write-Info "URL:       http://localhost:$port"
Write-Info "Directory: $installDir"
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor DarkGray
Write-Host "    1. codepush login --server http://localhost:$port --email you@email.com --password yourpass --register --name `"Your Name`"" -ForegroundColor DarkGray
Write-Host "    2. codepush apps add --package-name com.yourapp --name `"My App`" --set-default" -ForegroundColor DarkGray
Write-Host "    3. codepush release create --version 1.0.0" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Manage:" -ForegroundColor DarkGray
Write-Host "    Start:  cd $installDir; docker compose up -d" -ForegroundColor DarkGray
Write-Host "    Stop:   cd $installDir; docker compose down" -ForegroundColor DarkGray
Write-Host "    Logs:   docker logs -f codepush-server" -ForegroundColor DarkGray
Write-Host "    Update: cd $installDir; docker compose pull; docker compose up -d" -ForegroundColor DarkGray
Write-Host ""
