#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${SCRIPT_DIR}/../deploy"
SERVICE="${1:-}"

if [[ -n "${SERVICE}" ]]; then
  docker compose -f "${DEPLOY_DIR}/docker-compose.yml" logs -f "${SERVICE}"
else
  docker compose -f "${DEPLOY_DIR}/docker-compose.yml" logs -f
fi
