#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE="$SCRIPT_DIR/../compose.dev.yml"
REALMS="$SCRIPT_DIR/../keycloak/realms"

docker compose -f "$COMPOSE" stop keycloak
docker compose -f "$COMPOSE" run --rm -v "$REALMS:/export" keycloak export --dir /export --users skip
docker compose -f "$COMPOSE" up -d --wait keycloak
