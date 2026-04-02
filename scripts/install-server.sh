#!/bin/bash
# ============================================================================
# CodePush Server — Self-hosted Installation Script (Linux / macOS)
# ============================================================================
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/felipebaltazar/Maui.CodePush/main/scripts/install-server.sh | bash
#
# Or download and run manually:
#   chmod +x install-server.sh && ./install-server.sh
#
# Requirements:
#   - Docker and Docker Compose
#   - A MongoDB instance (local or Atlas)
#
# ============================================================================

set -e

PURPLE='\033[38;5;135m'
CYAN='\033[38;5;81m'
GREEN='\033[38;5;114m'
YELLOW='\033[38;5;221m'
RED='\033[38;5;203m'
GRAY='\033[38;5;245m'
BOLD='\033[1m'
RESET='\033[0m'

info()    { echo -e "  ${CYAN}●${RESET} $1"; }
success() { echo -e "  ${GREEN}✔${RESET} $1"; }
warn()    { echo -e "  ${YELLOW}⚠${RESET} $1"; }
error()   { echo -e "  ${RED}✖${RESET} $1"; exit 1; }

echo ""
echo -e "${BOLD}${PURPLE}  CodePush Server — Self-hosted Setup${RESET}"
echo -e "${GRAY}  ─────────────────────────────────────${RESET}"
echo ""

# ── Check prerequisites ─────────────────────────────────────────────────────

info "Checking prerequisites..."

if ! command -v docker &>/dev/null; then
    error "Docker not found. Install from https://docs.docker.com/get-docker/"
fi

if ! docker compose version &>/dev/null && ! docker-compose version &>/dev/null; then
    error "Docker Compose not found. Install from https://docs.docker.com/compose/install/"
fi

success "Docker $(docker --version | grep -oP '\d+\.\d+\.\d+')"

# ── Choose install directory ─────────────────────────────────────────────────

INSTALL_DIR="${CODEPUSH_INSTALL_DIR:-/opt/codepush}"

read -rp "  Install directory [${INSTALL_DIR}]: " input_dir
INSTALL_DIR="${input_dir:-$INSTALL_DIR}"

mkdir -p "$INSTALL_DIR"
cd "$INSTALL_DIR"

info "Installing to ${BOLD}${INSTALL_DIR}${RESET}"

# ── Collect configuration ────────────────────────────────────────────────────

echo ""
echo -e "${BOLD}  Configuration${RESET}"
echo -e "${GRAY}  ─────────────${RESET}"
echo ""

# MongoDB
read -rp "  MongoDB connection string [mongodb://localhost:27017]: " MONGO_CONN
MONGO_CONN="${MONGO_CONN:-mongodb://localhost:27017}"

read -rp "  MongoDB database name [codepush]: " MONGO_DB
MONGO_DB="${MONGO_DB:-codepush}"

# JWT Secret
JWT_SECRET=$(openssl rand -base64 48 2>/dev/null || head -c 64 /dev/urandom | base64 | tr -d '\n/')
info "Generated JWT secret"

# Port
read -rp "  Server port [8080]: " PORT
PORT="${PORT:-8080}"

echo ""

# ── Create .env ──────────────────────────────────────────────────────────────

cat > .env <<EOF
MONGODB_CONNECTION_STRING=${MONGO_CONN}
MONGODB_DATABASE_NAME=${MONGO_DB}
CODEPUSH_JWT_SECRET=${JWT_SECRET}
CODEPUSH_PORT=${PORT}
EOF

chmod 600 .env
success "Created .env"

# ── Create docker-compose.yml ────────────────────────────────────────────────

cat > docker-compose.yml <<EOF
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "\${CODEPUSH_PORT:-8080}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=\${MONGODB_CONNECTION_STRING}
      - MONGODB_DATABASE_NAME=\${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=\${CODEPUSH_JWT_SECRET}
    volumes:
      - codepush-uploads:/app/uploads

volumes:
  codepush-uploads:
EOF

success "Created docker-compose.yml"

# ── Optionally include local MongoDB ─────────────────────────────────────────

if [[ "$MONGO_CONN" == *"localhost"* ]] || [[ "$MONGO_CONN" == *"127.0.0.1"* ]]; then
    echo ""
    read -rp "  Include local MongoDB container? [Y/n]: " include_mongo
    include_mongo="${include_mongo:-Y}"

    if [[ "$include_mongo" =~ ^[Yy]$ ]]; then
        cat > docker-compose.yml <<EOF
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "\${CODEPUSH_PORT:-8080}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=mongodb://mongo:27017
      - MONGODB_DATABASE_NAME=\${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=\${CODEPUSH_JWT_SECRET}
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
EOF

        # Update .env to use internal network
        sed -i.bak 's|MONGODB_CONNECTION_STRING=.*|MONGODB_CONNECTION_STRING=mongodb://mongo:27017|' .env
        rm -f .env.bak
        success "Added local MongoDB container"
    fi
fi

# ── Pull and start ───────────────────────────────────────────────────────────

echo ""
info "Pulling Docker image..."
docker compose pull

echo ""
info "Starting server..."
docker compose up -d

echo ""

# ── Wait for startup ─────────────────────────────────────────────────────────

info "Waiting for server to start..."
for i in $(seq 1 15); do
    if curl -sf "http://localhost:${PORT}/api/auth/me" -o /dev/null 2>/dev/null; then
        break
    fi
    sleep 1
done

# ── Verify ───────────────────────────────────────────────────────────────────

if curl -sf "http://localhost:${PORT}/api/auth/me" -o /dev/null 2>/dev/null; then
    echo ""
    echo -e "${GRAY}  ─────────────────────────────────────${RESET}"
    echo ""
    success "${BOLD}CodePush Server is running!${RESET}"
    echo ""
    info "URL:       http://localhost:${PORT}"
    info "Directory: ${INSTALL_DIR}"
    echo ""
    echo -e "  ${GRAY}Next steps:${RESET}"
    echo -e "  ${GRAY}  1. codepush login --server http://$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'YOUR_IP'):${PORT} --email you@email.com --password yourpass --register --name \"Your Name\"${RESET}"
    echo -e "  ${GRAY}  2. codepush apps add --package-name com.yourapp --name \"My App\" --set-default${RESET}"
    echo -e "  ${GRAY}  3. codepush release create --version 1.0.0${RESET}"
    echo ""
else
    warn "Server may still be starting. Check with: docker logs codepush-server"
fi

echo -e "  ${GRAY}Manage:${RESET}"
echo -e "  ${GRAY}  Start:   cd ${INSTALL_DIR} && docker compose up -d${RESET}"
echo -e "  ${GRAY}  Stop:    cd ${INSTALL_DIR} && docker compose down${RESET}"
echo -e "  ${GRAY}  Logs:    docker logs -f codepush-server${RESET}"
echo -e "  ${GRAY}  Update:  cd ${INSTALL_DIR} && docker compose pull && docker compose up -d${RESET}"
echo ""
