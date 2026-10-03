#!/bin/sh
# Corre DENTRO del contenedor backup-files. Retencion LOCAL de los backups.
# Reglas de seguridad:
#   - Solo actua sobre /backups/sql y /backups/files (rutas literales, sin variables), y solo si existe /backups/.bury-backup-root
#     (marca que crea backup.sh: si el volumen no esta montado o apunta a otro lado, aborta sin borrar nada).
#   - Solo borra archivos cuyo NOMBRE cumple exactamente el patron que generan los scripts (+ su .sha256). Nada mas.
#   - La antiguedad se toma del TIMESTAMP DEL NOMBRE (UTC), no del mtime (que cambia al copiar/restaurar el directorio).
#   - Siempre conserva los MIN_KEEP mas recientes de cada tipo, aunque sean viejos (si los backups dejan de generarse no se vacia el directorio).
#   - Los log backups (.trn) se borran solo si son anteriores al full mas antiguo que se conserva (sin ese full no sirven).
# Env: DB_NAME (identificador), RETENTION_DAYS (entero >= 1), MIN_KEEP (entero >= 1, default 3)
set -eu

: "${DB_NAME:?falta DB_NAME}"
: "${RETENTION_DAYS:?falta RETENTION_DAYS}"
MIN_KEEP="${MIN_KEEP:-3}"

case "$DB_NAME" in ''|[!A-Za-z_]*|*[!A-Za-z0-9_]*) echo "DB_NAME invalido" >&2; exit 2 ;; esac
case "$RETENTION_DAYS" in ''|*[!0-9]*) echo "RETENTION_DAYS invalido" >&2; exit 2 ;; esac
case "$MIN_KEEP" in ''|*[!0-9]*) echo "MIN_KEEP invalido" >&2; exit 2 ;; esac
[ "$RETENTION_DAYS" -ge 1 ] || { echo "RETENTION_DAYS debe ser >= 1" >&2; exit 2; }
[ "$MIN_KEEP" -ge 1 ] || { echo "MIN_KEEP debe ser >= 1" >&2; exit 2; }
[ -f /backups/.bury-backup-root ] || { echo "falta /backups/.bury-backup-root: directorio de backups no montado; no se borra nada" >&2; exit 2; }
[ -d /backups/sql ] || { echo "falta /backups/sql" >&2; exit 2; }

now=$(date -u +%s)
cutoff=$(date -u -d "@$((now - RETENTION_DAYS * 86400))" +%Y-%m-%d_%H%M%SZ)
case "$cutoff" in [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9][0-9][0-9][0-9][0-9]Z) ;; *) echo "corte invalido: '$cutoff'" >&2; exit 2 ;; esac
echo "retencion: RETENTION_DAYS=$RETENTION_DAYS MIN_KEEP=$MIN_KEEP corte(UTC)=$cutoff"

TS='[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z'
deleted=0

ts_of() { echo "$1" | grep -oE "$TS"; }

# prune <dir> <regex-del-nombre>: borra los mas viejos que el corte, salvo los MIN_KEEP mas recientes.
prune() {
  dir="$1"; rx="$2"
  [ -d "$dir" ] || return 0
  files=$(ls -1 "$dir" | grep -E "^${rx}$" | sort || true)
  [ -n "$files" ] || { echo "  $dir: sin archivos que coincidan"; return 0; }
  total=$(echo "$files" | wc -l)
  protected=$(echo "$files" | tail -n "$MIN_KEEP")
  for f in $files; do
    t=$(ts_of "$f")
    if [ "$t" \< "$cutoff" ] && ! echo "$protected" | grep -qxF "$f"; then
      rm -f -- "$dir/$f" "$dir/$f.sha256"
      echo "  DELETE $dir/$f"
      deleted=$((deleted + 1))
    fi
  done
  echo "  $dir: $total archivo(s) evaluados para $rx"
}

prune /backups/sql "${DB_NAME}_full_${TS}\.bak"
prune /backups/files "files_${TS}\.tar\.gz"

# Logs: solo los anteriores al full mas antiguo que sobrevive.
oldest_full=$(ls -1 /backups/sql | grep -E "^${DB_NAME}_full_${TS}\.bak$" | sort | head -n1 || true)
if [ -n "$oldest_full" ]; then
  limit=$(ts_of "$oldest_full")
  for f in $(ls -1 /backups/sql | grep -E "^${DB_NAME}_log_${TS}\.trn$" | sort || true); do
    t=$(ts_of "$f")
    if [ "$t" \< "$limit" ]; then
      rm -f -- "/backups/sql/$f" "/backups/sql/$f.sha256"
      echo "  DELETE /backups/sql/$f (anterior al full mas antiguo conservado)"
      deleted=$((deleted + 1))
    fi
  done
else
  echo "  sin full en /backups/sql: no se tocan los logs"
fi

# Restos de ejecuciones interrumpidas (mas de 1 dia).
find /backups/files -maxdepth 1 -type f -name 'files_*.tar.gz.partial' -mmin +1440 -print -delete 2>/dev/null | sed 's/^/  DELETE partial /' || true

echo "retencion: $deleted archivo(s) borrado(s)"
