# Self-hosted Server

Host your own CodePush server with Docker.

---

## Quick Install

### Linux / macOS

```bash
curl -fsSL https://raw.githubusercontent.com/monk-seal/Maui.CodePush/main/scripts/install-server.sh | bash
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/monk-seal/Maui.CodePush/main/scripts/install-server.ps1 | iex
```

## What the installer does

1. Checks Docker is installed
2. Asks for: install directory, MongoDB connection, server port
3. Generates a secure JWT secret
4. Optionally includes a local MongoDB container
5. Creates `.env` and `docker-compose.yml`
6. Pulls the Docker image from `ghcr.io/felipebaltazar/codepush-server:latest`
7. Starts the server

## Manual Install

```bash
mkdir codepush-server && cd codepush-server

# Create .env
cat > .env <<EOF
MONGODB_CONNECTION_STRING=mongodb://mongo:27017
MONGODB_DATABASE_NAME=codepush
CODEPUSH_JWT_SECRET=$(openssl rand -base64 48)
EOF

# Create docker-compose.yml
cat > docker-compose.yml <<'EOF'
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=${MONGODB_CONNECTION_STRING}
      - MONGODB_DATABASE_NAME=${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=${CODEPUSH_JWT_SECRET}
    volumes:
      - codepush-uploads:/app/uploads

  mongo:
    image: mongo:7
    restart: unless-stopped
    volumes:
      - mongo-data:/data/db

volumes:
  codepush-uploads:
  mongo-data:
EOF

docker compose up -d
```

## After installation

```bash
# Point the CLI to your server
codepush login --server http://your-server:8080

# Register your app
codepush apps add --package-name com.yourapp --name "My App" --set-default
```

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `MONGODB_CONNECTION_STRING` | Yes | MongoDB connection string |
| `MONGODB_DATABASE_NAME` | No | Database name (default: `codepush`) |
| `CODEPUSH_JWT_SECRET` | Yes | Secret key for JWT tokens (min 32 chars) |
| `AZURE_STORAGE_CONNECTION_STRING` | No | Azure Blob Storage (uses local disk if not set) |
| `CODEPUSH_CDN_URL` | No | CDN base URL (uses server download if not set) |
| `STRIPE_SECRET_KEY` | No | Stripe API key (billing disabled if not set) |
| `STRIPE_WEBHOOK_SECRET` | No | Stripe webhook signing secret |

## Managing the server

```bash
docker compose up -d           # Start
docker compose down             # Stop
docker compose pull && docker compose up -d  # Update
docker logs -f codepush-server  # View logs
```

## Monitoring

The server exposes:

- `GET /health` — Health check (returns 200 or 503)
- `GET /metrics` — Prometheus metrics (restricted to internal access)

### Adding Prometheus + Grafana

See the [docker-compose.yml](https://github.com/monk-seal/Maui.CodePush/blob/main/docker-compose.yml) in the repository for a complete setup with Prometheus and Grafana.
