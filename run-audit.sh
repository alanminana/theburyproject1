#!/usr/bin/env bash
# Auditoría Unlighthouse — TheBuryProject ERP
# Uso: ./run-audit.sh
#
# Requiere .env.unlighthouse con:
#   UNLIGHTHOUSE_USER=...
#   UNLIGHTHOUSE_PASS=...

set -e

ENV_FILE=".env.unlighthouse"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: No encontré $ENV_FILE"
  echo "Copiá .env.unlighthouse.example a .env.unlighthouse y completalo."
  exit 1
fi

# Cargar variables del .env
export $(grep -v '^#' "$ENV_FILE" | xargs)

if [ -z "$UNLIGHTHOUSE_USER" ] || [ -z "$UNLIGHTHOUSE_PASS" ]; then
  echo "ERROR: UNLIGHTHOUSE_USER o UNLIGHTHOUSE_PASS vacíos en $ENV_FILE"
  exit 1
fi

# Verificar que el servidor esté corriendo
if ! curl -s -o /dev/null -w "%{http_code}" http://localhost:5187/ | grep -q "200\|302"; then
  echo "ERROR: El servidor no responde en http://localhost:5187/"
  echo "Arrancá la app primero: dotnet run"
  exit 1
fi

echo "Servidor detectado. Iniciando auditoría Unlighthouse..."
echo "Usuario: $UNLIGHTHOUSE_USER"
echo "Reportes en: ./.unlighthouse/"
echo ""

npx unlighthouse --config unlighthouse.config.mjs
