#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${SCRIPT_DIR}/../deploy"
BACKUP_DIR="${SCRIPT_DIR}/../backups"
TIMESTAMP="$(date -u +"%Y%m%dT%H%M%SZ")"
BACKUP_FILE="${BACKUP_DIR}/momo_quant_${TIMESTAMP}.sql"

mkdir -p "${BACKUP_DIR}"

docker compose -f "${DEPLOY_DIR}/docker-compose.yml" exec -T momo-mysql \
  mysqldump -u"${MYSQL_USER}" -p"${MYSQL_PASSWORD}" "${MYSQL_DATABASE}" > "${BACKUP_FILE}"

echo "Database backup created: ${BACKUP_FILE}"
