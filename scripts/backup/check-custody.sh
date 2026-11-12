#!/usr/bin/env bash
# VERIFICADOR DE CUSTODIA DE CLAVES (P-06/P-07). Solo lectura. Nunca imprime secretos: unicamente reglas, rutas y motivos.
# Criterio: las claves de descifrado (passwords crypt de rclone, passphrase del paquete de recuperacion) y el material de
# recuperacion NO pueden vivir junto a los respaldos cifrados (BACKUP_DIR, directorio offsite local, remoto), ni en Git, ni en logs.
# Ver docs/backup-custodia.md.
#
#   scripts/backup/check-custody.sh [--no-status] [--check-remote] [--quiet]
#
#   --no-status     no escribe $BACKUP_DIR/monitor-status/custody.json (el monitor usa ese estado: falla y antiguedad)
#   --check-remote  ademas lista los nombres del remoto (rclone lsf -R, via docker) y busca archivos con aspecto de clave
#   --quiet         solo imprime FAIL/WARN y el resumen
#
# Reglas (cada FAIL => exit 11):
#   C01 .env / COMPOSE_ENV_FILES con permisos para grupo/otros (solo Linux; se omite en NTFS/DrvFs/Git Bash)
#   C02 backup-offsite.env con permisos para grupo/otros; o ubicado dentro de BACKUP_DIR / directorio offsite local
#   C03 BACKUP_DIR con archivos de aspecto clave/passphrase/.env/rclone.conf (nombre)
#   C04 el directorio offsite local (BACKUP_OFFSITE_DISK_DIR) con los mismos archivos
#   C05 BACKUP_DIR con contenido de secretos (RCLONE_CONFIG_*PASS/KEY/SECRET=valor, clave privada PEM)
#   C06 una password crypt aparece (en claro, tal cual esta en el env) en backup.log / monitor-status / logs de errores
#   C07 passwords crypt ausentes, iguales, con formato invalido o triviales
#   C08 hay remoto sin BACKUP_REQUIRE_OFFSITE=true, o el remoto no es crypt, o falta cifrar nombres, o REQUIRE sin remoto
#   C09 material de recuperacion (passphrase/clave) coexiste con un .enc (paquete de recuperacion) en la misma raiz
#   C10 secretos versionados en Git (.env, backup-offsite.env, *passphrase*, *.enc, *.pem, *.key)
#   C11 (--check-remote) el remoto contiene archivos con aspecto de clave/passphrase
#
# Variables (todas opcionales): BURY_OFFSITE_ENV_FILE (default <repo>/backup-offsite.env), BACKUP_OFFSITE_DISK_DIR (.env),
#   CUSTODY_RECOVERY_DIRS (lista separada por ':'; default ~/secure-preprod:~/secure-prod; se inspeccionan NOMBRES, no contenidos),
#   CUSTODY_EXTRA_LOGS (lista ':' de logs adicionales), BURY_CUSTODY_REMOTE_LIST_FILE (solo tests: nombres en vez de rclone).
# Codigos: 0 ok | 1 configuracion/uso | 11 custodia insegura
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
set +e   # este script acumula hallazgos; lib.sh activa -e

EX_CUSTODY=11
STATUS=1 CHECK_REMOTE=0 QUIET=0
while (( $# )); do
  case "$1" in
    --no-status) STATUS=0; shift ;;
    --check-remote) CHECK_REMOTE=1; shift ;;
    --quiet) QUIET=1; shift ;;
    -h|--help) sed -n '2,29p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
LOG_FILE=""   # este script no escribe en backup.log (no mezclar su salida con el log operativo)

FAILS=0 WARNS=0 FAILED_RULES=""
say()  { (( QUIET )) || printf 'OK   %s\n' "$*"; }
fail() { FAILS=$((FAILS+1)); [[ ",$FAILED_RULES," == *",$1,"* ]] || FAILED_RULES+="${FAILED_RULES:+,}$1"; printf 'FAIL %s %s\n' "$1" "$2"; }
warn() { WARNS=$((WARNS+1)); printf 'WARN %s %s\n' "$1" "$2"; }

# ---- lectura de configuracion (env del proceso > archivo). Los valores quedan en variables locales; jamas se imprimen.
kv_file() { # kv_file ARCHIVO CLAVE -> valor (sin comillas) o vacio
  local f=$1 key=$2 line val
  [[ -f "$f" ]] || return 0
  line=$(grep -E "^[[:space:]]*(export[[:space:]]+)?${key}=" "$f" | tail -n1 || true)
  [[ -n "$line" ]] || return 0
  val=${line#*=}; val=${val%$'\r'}
  if [[ "$val" =~ ^\"(.*)\"$ || "$val" =~ ^\'(.*)\'$ ]]; then val=${BASH_REMATCH[1]}; fi
  printf '%s' "$val"
}
OFFSITE_ENV=${BURY_OFFSITE_ENV_FILE:-$REPO_DIR/backup-offsite.env}
ofv() { # ofv CLAVE -> variable de entorno del proceso o valor en backup-offsite.env
  if [[ -n "${!1:-}" ]]; then printf '%s' "${!1}"; else kv_file "$OFFSITE_ENV" "$1"; fi
}

# ---- utilidades
fs_ignores_modes() { # NTFS / DrvFs / Git Bash: chmod no tiene efecto y stat inventa modos
  case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) return 0 ;; esac
  local t; t=$(stat -f -c %T "$1" 2>/dev/null || true)
  case "$t" in ntfs*|fuseblk|vfat|msdos|exfat|9p|drvfs|v9fs|cifs|smb*) return 0 ;; esac
  return 1
}
check_mode() { # check_mode REGLA ARCHIVO
  local rule=$1 f=$2 m
  [[ -e "$f" ]] || return 0
  if fs_ignores_modes "$f"; then say "$rule $f: permisos no verificables en este filesystem (omitido)"; return 0; fi
  m=$(stat -c %a "$f" 2>/dev/null) || { warn "$rule" "$f: no se pudo leer el modo"; return 0; }
  if (( (8#$m & 8#077) != 0 )); then fail "$rule" "$f: modo $m (debe ser 600: sin acceso para grupo/otros)"; else say "$rule $f: modo $m"; fi
}
under() { # under RUTA BASE -> 0 si RUTA esta dentro de BASE
  local p base; p=$(cd "$(dirname "$1")" 2>/dev/null && pwd -P)/$(basename "$1"); base=$(cd "$2" 2>/dev/null && pwd -P) || return 1
  [[ "$p" == "$base"/* ]]
}

# Nombres que delatan material de clave. find -iname, sin leer contenido.
KEYISH=(-iname '*passphrase*' -o -iname '*passwd*' -o -iname '*password*' -o -iname '*recovery*' -o -iname '*.key' -o -iname '*.pem'
        -o -iname '*.p12' -o -iname '*.pfx' -o -iname 'id_rsa*' -o -iname 'id_ed25519*' -o -iname 'rclone.conf'
        -o -iname 'backup-offsite.env' -o -iname '.env' -o -iname '.env.*' -o -iname '*.env')
scan_names() { # scan_names REGLA DIR -> FAIL por cada archivo/directorio con nombre sospechoso (excluye *.enc: ver C09)
  local rule=$1 d=$2 p n=0
  [[ -d "$d" ]] || { say "$rule $d: no existe (nada que revisar)"; return 0; }
  while IFS= read -r p; do
    [[ -n "$p" ]] || continue
    [[ "${p,,}" == *.enc ]] && continue
    case "${p##*/}" in .env.example|*.env.example|*.example) continue ;; esac
    fail "$rule" "$p: nombre con aspecto de clave/passphrase/secreto dentro de un directorio de respaldos"; n=$((n+1))
  done < <(find "$d" -mindepth 1 \( "${KEYISH[@]}" \) 2>/dev/null | sort)
  (( n )) || say "$rule $d: sin nombres con aspecto de clave"
}

# ---- C01 / C02: permisos de los archivos de configuracion con secretos
while IFS= read -r f; do check_mode C01 "$f"; done < <(_envfiles)
check_mode C02 "$OFFSITE_ENV"
OFFSITE_DIR=$(cfg BACKUP_OFFSITE_DISK_DIR "")
OFFSITE_DIR=${OFFSITE_DIR%/}
for base in "$BACKUP_DIR" "$OFFSITE_DIR"; do
  [[ -n "$base" && -d "$base" ]] || continue
  if [[ -e "$OFFSITE_ENV" ]] && under "$OFFSITE_ENV" "$base"; then fail C02 "$OFFSITE_ENV: dentro de $base (la clave quedaria junto a los respaldos)"; fi
  while IFS= read -r f; do
    [[ -e "$f" ]] && under "$f" "$base" && fail C02 "$f: archivo de entorno con credenciales dentro de $base"
  done < <(_envfiles)
done

# ---- C03 / C04: nombres con aspecto de clave dentro de BACKUP_DIR y del directorio offsite local
scan_names C03 "$BACKUP_DIR"
if [[ -n "$OFFSITE_DIR" ]]; then scan_names C04 "$OFFSITE_DIR"; else say "C04 sin BACKUP_OFFSITE_DISK_DIR: sin directorio offsite local"; fi

# ---- valores de las passwords (solo en memoria)
# El remoto crypt activo es el de BACKUP_REMOTE (por convencion del repo se llama "secure"); sin remoto se mira "secure".
rname=${BACKUP_REMOTE%%:*}; rname=${rname:-secure}; RN=${rname^^}; RN=${RN//-/_}
SECURE_PW=$(ofv "RCLONE_CONFIG_${RN}_PASSWORD")
SECURE_PW2=$(ofv "RCLONE_CONFIG_${RN}_PASSWORD2")

# ---- C05: contenido de secretos en BACKUP_DIR (texto pequeno; se omiten binarios y respaldos)
if [[ -d "$BACKUP_DIR" ]]; then
  n=0
  while IFS= read -r f; do
    fail C05 "$f: contiene credenciales/clave privada en texto"; n=$((n+1))
  done < <(find "$BACKUP_DIR" -type f -size -2048k ! -name '*.bak' ! -name '*.trn' ! -name '*.gz' ! -name '*.tar' ! -name '*.enc' ! -name '*.sha256' -print0 2>/dev/null \
            | xargs -0 -r grep -IlE -e '^[[:space:]]*(export[[:space:]]+)?RCLONE_CONFIG_[A-Za-z0-9_]*(PASS|PASSWORD2?|KEY|SECRET)[A-Za-z0-9_]*=[^[:space:]\[]' \
                                  -e '-----BEGIN [A-Z ]*PRIVATE KEY-----' -e '^[[:space:]]*MSSQL_SA_PASSWORD=[^[:space:]\[]' 2>/dev/null | sort -u)
  (( n )) || say "C05 $BACKUP_DIR: sin contenido de secretos en archivos de texto"
fi

# ---- C06: la password crypt (tal como esta en el env) no debe aparecer en logs ni estado de monitoreo
LOGS=()
[[ -d "$BACKUP_DIR/logs" ]] && LOGS+=("$BACKUP_DIR/logs")
[[ -d "$BACKUP_DIR/monitor-status" ]] && LOGS+=("$BACKUP_DIR/monitor-status")
for l in "$HOME/bury-backup-errors.log" /var/log/bury-backup-errors.log; do [[ -f "$l" ]] && LOGS+=("$l"); done
IFS=: read -r -a extra <<<"${CUSTODY_EXTRA_LOGS:-}"
for l in "${extra[@]}"; do [[ -n "$l" && -e "$l" ]] && LOGS+=("$l"); done
if (( ${#LOGS[@]} )); then
  PATS=(); [[ ${#SECURE_PW} -ge 8 ]] && PATS+=("$SECURE_PW"); [[ ${#SECURE_PW2} -ge 8 ]] && PATS+=("$SECURE_PW2")
  if (( ${#PATS[@]} )); then
    hits=$(printf '%s\n' "${PATS[@]}" | grep -rIlF -f - "${LOGS[@]}" 2>/dev/null | sort -u)
    if [[ -n "$hits" ]]; then while IFS= read -r f; do fail C06 "$f: contiene una password crypt en claro"; done <<<"$hits"
    else say "C06 logs y monitor-status sin passwords crypt"; fi
  else say "C06 sin passwords crypt configuradas: no hay nada que buscar en logs"; fi
fi

# ---- C07 / C08: passwords crypt y exigencia de cifrado
trivial_reason() { # trivial_reason NOMBRE VALOR -> imprime motivo (sin el valor) si es invalido
  local v=$2
  if [[ -z "$v" ]]; then echo "no esta definida"; return; fi
  if (( ${#v} < 22 )); then echo "demasiado corta para ser una salida de 'rclone obscure' (${#v} caracteres)"; return; fi
  if [[ ! "$v" =~ ^[A-Za-z0-9_-]+$ ]]; then echo "no parece una salida de 'rclone obscure' (caracteres no validos o placeholder)"; return; fi
  if [[ "${v,,}" =~ (changeme|password|obscure|example|secret|xxxx|1234) ]]; then echo "contiene un placeholder/valor trivial"; return; fi
  if [[ -z "${v//"${v:0:1}"/}" ]]; then echo "caracteres repetidos"; return; fi
}

if [[ -n "$BACKUP_REMOTE" ]]; then
  [[ "$BACKUP_REQUIRE_OFFSITE" == "true" ]] || fail C08 "BACKUP_REQUIRE_OFFSITE no es 'true' con BACKUP_REMOTE configurado (un fallo de la copia externa pasaria como WARN)"
  rtype=$(ofv "RCLONE_CONFIG_${RN}_TYPE")
  if [[ -z "$rtype" ]]; then
    fail C08 "remoto '$rname': sin RCLONE_CONFIG_${RN}_TYPE (en $OFFSITE_ENV o en el entorno): no se puede comprobar que sea crypt"
  elif [[ "$rtype" != crypt ]]; then
    fail C08 "remoto '$rname': tipo '$rtype', no crypt: los respaldos viajarian sin cifrar al remoto"
  else
    say "C08 remoto '$rname' es crypt"
    fne=$(ofv "RCLONE_CONFIG_${RN}_FILENAME_ENCRYPTION"); fne=${fne:-standard}
    case "$fne" in
      standard) say "C08 cifrado de nombres: standard" ;;
      obfuscate) warn C08 "remoto '$rname': FILENAME_ENCRYPTION=obfuscate (nombres solo ofuscados; usar standard)" ;;
      *) fail C08 "remoto '$rname': FILENAME_ENCRYPTION='$fne' deja los nombres de archivo en claro (usar standard)" ;;
    esac
    for pair in "PASSWORD:$SECURE_PW" "PASSWORD2:$SECURE_PW2"; do
      why=$(trivial_reason "${pair%%:*}" "${pair#*:}")
      if [[ -n "$why" ]]; then fail C07 "RCLONE_CONFIG_${RN}_${pair%%:*}: $why"; else say "C07 RCLONE_CONFIG_${RN}_${pair%%:*}: formato valido"; fi
    done
    if [[ -n "$SECURE_PW" && "$SECURE_PW" == "$SECURE_PW2" ]]; then fail C07 "RCLONE_CONFIG_${RN}_PASSWORD y _PASSWORD2 son iguales (PASSWORD2 es la sal: deben ser independientes)"; fi
  fi
else
  if [[ "$BACKUP_REQUIRE_OFFSITE" == "true" ]]; then fail C08 "BACKUP_REQUIRE_OFFSITE=true pero BACKUP_REMOTE esta vacio (backup.sh fallaria en cada corrida)"
  else warn C08 "sin BACKUP_REMOTE: no hay copia externa; la custodia de claves crypt no aplica (los respaldos existen SOLO en este servidor)"; fi
fi

# ---- C09: material de recuperacion junto al paquete .enc
RECOVERY_DIRS=${CUSTODY_RECOVERY_DIRS-$HOME/secure-preprod:$HOME/secure-prod}
declare -a ROOTS=("$BACKUP_DIR")
[[ -n "$OFFSITE_DIR" ]] && ROOTS+=("$OFFSITE_DIR")
IFS=: read -r -a rd <<<"$RECOVERY_DIRS"
for d in "${rd[@]}"; do [[ -n "$d" ]] && ROOTS+=("$d"); done
SECRETISH=(-iname '*passphrase*' -o -iname '*passwd*' -o -iname '*password*' -o -iname '*recovery-key*' -o -iname '*.key' -o -iname '*.pem' -o -iname 'rclone.conf')
coexist=0
for root in "${ROOTS[@]}"; do
  [[ -d "$root" ]] || continue
  enc=$(find "$root" -maxdepth 5 -type f -iname '*.enc' 2>/dev/null | head -n1)
  [[ -n "$enc" ]] || continue
  while IFS= read -r p; do
    [[ -n "$p" ]] || continue
    fail C09 "$p: material de recuperacion junto al paquete cifrado '$enc' (misma raiz $root): separar en custodia distinta"; coexist=1
  done < <(find "$root" -maxdepth 5 -type f \( "${SECRETISH[@]}" \) ! -iname '*.enc' 2>/dev/null | sort)
  [[ "$root" == "$BACKUP_DIR" || "$root" == "$OFFSITE_DIR" ]] && warn C09 "$enc: paquete .enc dentro de un directorio de respaldos (viaja con ellos; la passphrase debe estar en custodia separada)"
done
(( coexist )) || say "C09 sin material de recuperacion junto a un paquete .enc"

# ---- C10: secretos versionados en Git
if git -C "$REPO_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  tracked=$(git -C "$REPO_DIR" ls-files 2>/dev/null | grep -iE '(^|/)(\.env|backup-offsite\.env|rclone\.conf|id_rsa|id_ed25519)$|passphrase|\.enc$|\.pem$|\.key$' | grep -viE '\.example$|(^|/)(wwwroot|node_modules)/' || true)
  if [[ -n "$tracked" ]]; then while IFS= read -r f; do fail C10 "$f: archivo con aspecto de secreto versionado en Git"; done <<<"$tracked"
  else say "C10 Git: ningun archivo con aspecto de secreto versionado"; fi
fi

# ---- C11 (opcional): nombres en el remoto
if (( CHECK_REMOTE )); then
  if [[ -n "${BURY_CUSTODY_REMOTE_LIST_FILE:-}" ]]; then listing=$(cat "$BURY_CUSTODY_REMOTE_LIST_FILE")
  elif [[ -n "$BACKUP_REMOTE" ]] && command -v docker >/dev/null 2>&1; then
    listing=$(dc run --rm -T --no-deps --quiet-pull backup-offsite lsf -R --files-only "$BACKUP_REMOTE" 2>/dev/null | tr -d '\r') || { listing=""; warn C11 "no se pudo listar el remoto"; }
  else listing=""; warn C11 "--check-remote sin remoto o sin docker: omitido"; fi
  bad=$(grep -iE 'passphrase|passwd|password|recovery|\.key$|\.pem$|rclone\.conf|(^|/)\.env|backup-offsite\.env' <<<"$listing" | sort -u || true)
  if [[ -n "$bad" ]]; then while IFS= read -r f; do fail C11 "remoto:$f: archivo con aspecto de clave/secreto en la copia externa"; done <<<"$bad"
  else say "C11 remoto sin archivos con aspecto de clave"; fi
fi

# ---- resumen y estado para el monitor
if (( FAILS )); then msg="$FAILS fallo(s): $FAILED_RULES"; else msg="custodia OK ($WARNS aviso(s))"; fi
echo "---"; echo "check-custody: $msg"
code=0; (( FAILS )) && code=$EX_CUSTODY
if (( STATUS )) && [[ -d "$BACKUP_DIR" ]]; then
  if (( code == 0 )); then restore_test_status 0 true "$msg" custody; else restore_test_status "$code" false "$msg" custody; fi
fi
exit "$code"
