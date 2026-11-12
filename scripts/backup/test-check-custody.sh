#!/usr/bin/env bash
# Tests de scripts/backup/check-custody.sh con casos sinteticos (positivos y negativos). Sin docker, sin red, sin tocar datos
# reales: HOME, BACKUP_DIR, .env y backup-offsite.env apuntan a un directorio temporal; las passwords son valores inventados.
#   bash scripts/backup/test-check-custody.sh        (exit 0 = todo OK)
# En NTFS/Git Bash los casos de permisos (C01/C02) se omiten (SKIP); en Linux/WSL/Docker corren siempre.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }

P1='aB3dE5gH7jK9mN1pQ3sT5vW7yZ'      # sinteticas; con formato de salida de `rclone obscure`
P2='Zy9xW7vU5tS3rQ1pO9nM7lK5jI'
SECRETS=("$P1" "$P2")

: >"$T/probe"; chmod 0600 "$T/probe"; if [[ "$(stat -c %a "$T/probe")" == 600 ]] && ! [[ "$(uname -s)" =~ MINGW|MSYS|CYGWIN ]]; then MODES=1; else MODES=0; echo "SKIP casos de permisos: el filesystem no respeta chmod"; fi

# new_case NOMBRE -> crea $T/NOMBRE con backups/, .env y backup-offsite.env VALIDOS (caso base: todo OK)
new_case() {
  C="$T/$1"; mkdir -p "$C/backups/sql" "$C/backups/files" "$C/backups/logs" "$C/backups/monitor-status" "$C/home"
  echo "x" >"$C/backups/sql/TheBuryProjectDb_full_2026-01-01_000000Z.bak"
  echo "$(date -u +%FT%TZ) [INFO] backup ok" >"$C/backups/logs/backup.log"
  printf 'BACKUP_REMOTE=secure:erp\nBACKUP_REQUIRE_OFFSITE=true\nMSSQL_DATABASE=TheBuryProjectDb\n' >"$C/.env"
  printf 'RCLONE_CONFIG_OFFSITE_TYPE=sftp\nRCLONE_CONFIG_SECURE_TYPE=crypt\nRCLONE_CONFIG_SECURE_REMOTE=offsite:erp\nRCLONE_CONFIG_SECURE_PASSWORD=%s\nRCLONE_CONFIG_SECURE_PASSWORD2=%s\n' "$P1" "$P2" >"$C/backup-offsite.env"
  chmod 600 "$C/.env" "$C/backup-offsite.env"
}
# run_case [VAR=valor...] -> deja la salida en $OUT y el exit en $RC. Entorno limpio: no hereda RCLONE_*/BACKUP_* del host.
run_case() {
  OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" \
        CUSTODY_RECOVERY_DIRS="" "$@" bash "$HERE/check-custody.sh" --no-status 2>&1); RC=$?
}
# expect NOMBRE RC_ESPERADO [REGEX_EN_SALIDA] [REGEX_PROHIBIDO]; siempre verifica que no se filtre ningun secreto.
expect() {
  local name=$1 want=$2 re=${3:-} no=${4:-} s leak=0
  for s in "${SECRETS[@]}"; do [[ "$OUT" == *"$s"* ]] && leak=1; done
  if (( leak )); then bad "$name: LA SALIDA FILTRA UN SECRETO"; return; fi
  if [[ "$RC" != "$want" ]]; then bad "$name (rc=$RC esperado=$want) :: $(tail -n 3 <<<"$OUT" | tr '\n' '|')"; return; fi
  if [[ -n "$re" ]] && ! grep -qE "$re" <<<"$OUT"; then bad "$name: falta /$re/ en la salida :: $(grep -E '^(FAIL|WARN)' <<<"$OUT" | head -n3 | tr '\n' '|')"; return; fi
  if [[ -n "$no" ]] && grep -qE "$no" <<<"$OUT"; then bad "$name: no debia aparecer /$no/"; return; fi
  ok "$name"
}

for f in check-custody.sh test-check-custody.sh; do bash -n "$HERE/$f" && ok "bash -n $f" || bad "bash -n $f"; done

# --- positivo --------------------------------------------------------------------------------------------------------------
new_case base;            run_case; expect "caso base valido: exit 0 y sin FAIL" 0 'custodia OK' '^FAIL'
if (( MODES )); then
  new_case perm_env;      chmod 644 "$C/.env";                run_case; expect "C01 .env con modo 644" 11 '^FAIL C01 .*modo 644'
  new_case perm_off;      chmod 640 "$C/backup-offsite.env";  run_case; expect "C02 backup-offsite.env con modo 640" 11 '^FAIL C02 .*modo 640'
fi
new_case env_in_backup;   cp "$C/backup-offsite.env" "$C/backups/backup-offsite.env"; run_case; expect "C03 backup-offsite.env copiado dentro de BACKUP_DIR" 11 '^FAIL C03 .*backup-offsite.env'
new_case offsite_in_bk;   mv "$C/backup-offsite.env" "$C/backups/cfg.env"; run_case BURY_OFFSITE_ENV_FILE="$C/backups/cfg.env"; expect "C02 el archivo de claves esta dentro de BACKUP_DIR" 11 '^FAIL C02 .*dentro de'
new_case pass_file;       echo "x" >"$C/backups/recovery-passphrase.txt"; run_case; expect "C03 recovery-passphrase.txt dentro de BACKUP_DIR" 11 '^FAIL C03 .*recovery-passphrase'
new_case key_file;        echo "x" >"$C/backups/files/server.key"; run_case; expect "C03 *.key dentro de BACKUP_DIR" 11 '^FAIL C03 .*server\.key'
new_case example_ok;      echo "x" >"$C/backups/.env.example"; run_case; expect ".env.example no se considera secreto" 0 '' '^FAIL'
new_case offsite_dir;     mkdir -p "$C/disk"; echo "x" >"$C/disk/id_rsa"; run_case BACKUP_OFFSITE_DISK_DIR="$C/disk"; expect "C04 clave dentro del directorio offsite local" 11 '^FAIL C04 .*id_rsa'
new_case content_leak;    printf 'RCLONE_CONFIG_SECURE_PASSWORD=%s\n' "$P2" >"$C/backups/logs/debug.txt"; run_case; expect "C05 credencial en texto dentro de BACKUP_DIR" 11 '^FAIL C05 .*debug.txt'
new_case content_redact;  printf 'RCLONE_CONFIG_SECURE_PASSWORD2=[REDACTED]\n' >"$C/backups/logs/backup.log"; run_case; expect "C05 valor [REDACTED] en el log NO es fuga" 0 '' '^FAIL'
new_case pem;             printf -- '-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n' >"$C/backups/logs/k.txt"; run_case; expect "C05 clave privada PEM en texto" 11 '^FAIL C05 .*k.txt'
new_case log_leak;        echo "error: pass=$P1 fallo" >>"$C/backups/logs/backup.log"; run_case; expect "C06 password crypt en backup.log" 11 '^FAIL C06 .*backup.log'
new_case status_leak;     echo "{\"message\":\"$P2\"}" >"$C/backups/monitor-status/x.json"; run_case; expect "C06 password crypt en monitor-status" 11 '^FAIL C06 .*x.json'
new_case errlog_leak;     echo "$P1" >"$C/err.log"; run_case CUSTODY_EXTRA_LOGS="$C/err.log"; expect "C06 password crypt en log de errores adicional" 11 '^FAIL C06 .*err.log'
new_case same_pw;         sed -i "s/^RCLONE_CONFIG_SECURE_PASSWORD2=.*/RCLONE_CONFIG_SECURE_PASSWORD2=$P1/" "$C/backup-offsite.env"; run_case; expect "C07 PASSWORD == PASSWORD2" 11 '^FAIL C07 .*iguales'
new_case no_pw2;          sed -i '/PASSWORD2/d' "$C/backup-offsite.env"; run_case; expect "C07 falta PASSWORD2" 11 '^FAIL C07 .*PASSWORD2: no esta definida'
new_case trivial;         sed -i "s/^RCLONE_CONFIG_SECURE_PASSWORD=.*/RCLONE_CONFIG_SECURE_PASSWORD=changeme/" "$C/backup-offsite.env"; run_case; expect "C07 password trivial/corta" 11 '^FAIL C07 .*PASSWORD:'
new_case placeholder;     sed -i "s/^RCLONE_CONFIG_SECURE_PASSWORD=.*/RCLONE_CONFIG_SECURE_PASSWORD=<obscure P1>/" "$C/backup-offsite.env"; run_case; expect "C07 placeholder sin reemplazar" 11 '^FAIL C07'
new_case repeated;        sed -i "s/^RCLONE_CONFIG_SECURE_PASSWORD=.*/RCLONE_CONFIG_SECURE_PASSWORD=aaaaaaaaaaaaaaaaaaaaaaaaaa/" "$C/backup-offsite.env"; run_case; expect "C07 caracteres repetidos" 11 '^FAIL C07 .*repetidos'
new_case no_require;      sed -i '/REQUIRE_OFFSITE/d' "$C/.env"; run_case; expect "C08 remoto sin BACKUP_REQUIRE_OFFSITE=true" 11 '^FAIL C08 .*BACKUP_REQUIRE_OFFSITE'
new_case not_crypt;       sed -i '/SECURE_TYPE/d' "$C/backup-offsite.env"; printf 'RCLONE_CONFIG_SECURE_TYPE=alias\n' >>"$C/backup-offsite.env"; run_case; expect "C08 remoto que no es crypt" 11 '^FAIL C08 .*no crypt'
new_case no_type;         sed -i '/SECURE_TYPE/d' "$C/backup-offsite.env"; run_case; expect "C08 remoto sin tipo verificable" 11 '^FAIL C08 .*TYPE'
new_case fname_off;       echo 'RCLONE_CONFIG_SECURE_FILENAME_ENCRYPTION=off' >>"$C/backup-offsite.env"; run_case; expect "C08 nombres sin cifrar" 11 '^FAIL C08 .*FILENAME_ENCRYPTION'
new_case require_noremote; printf 'BACKUP_REQUIRE_OFFSITE=true\n' >"$C/.env"; run_case; expect "C08 REQUIRE sin BACKUP_REMOTE" 11 '^FAIL C08 .*vacio'
new_case no_offsite;      printf 'MSSQL_DATABASE=TheBuryProjectDb\n' >"$C/.env"; rm -f "$C/backup-offsite.env"; run_case; expect "sin copia externa: WARN (no falla) y se informa" 0 '^WARN C08 .*sin BACKUP_REMOTE' '^FAIL'
# C09: paquete .enc + passphrase en la misma raiz (caso historico ~/secure-preprod)
new_case coexist;         mkdir -p "$C/sec/recovery-package"; echo x >"$C/sec/recovery-package/recovery.enc"; echo x >"$C/sec/recovery-passphrase.txt"; run_case CUSTODY_RECOVERY_DIRS="$C/sec"; expect "C09 .enc y passphrase en la misma raiz" 11 '^FAIL C09 .*recovery-passphrase.txt'
new_case separated;       mkdir -p "$C/sec/recovery-package" "$C/otro"; echo x >"$C/sec/recovery-package/recovery.enc"; echo x >"$C/otro/passphrase.txt"; run_case CUSTODY_RECOVERY_DIRS="$C/sec"; expect "C09 .enc solo, passphrase en otra raiz: OK" 0 '' '^FAIL'
new_case enc_in_backup;   echo x >"$C/backups/files/recovery.enc"; run_case; expect "C09 .enc dentro de BACKUP_DIR: solo aviso" 0 '^WARN C09' '^FAIL'
# C11: remoto
printf 'sql/TheBuryProjectDb_full_2026-01-01_000000Z.bak\nfiles/files_x.tar.gz\n' >"$T/remote-ok.txt"
printf 'sql/a.bak\nextra/recovery-passphrase.txt\n' >"$T/remote-bad.txt"
new_case remote_ok;       OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" BURY_CUSTODY_REMOTE_LIST_FILE="$T/remote-ok.txt" bash "$HERE/check-custody.sh" --no-status --check-remote 2>&1); RC=$?; expect "C11 remoto limpio" 0 'C11 remoto sin archivos' '^FAIL'
new_case remote_bad;      OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" BURY_CUSTODY_REMOTE_LIST_FILE="$T/remote-bad.txt" bash "$HERE/check-custody.sh" --no-status --check-remote 2>&1); RC=$?; expect "C11 passphrase en el remoto" 11 '^FAIL C11 .*passphrase'

# --- nombres en espanol y paquetes alternativos (C03/C09) -------------------------------------------------------------------
for nm in clave-recuperacion.txt contraseña-sa.txt llave.txt secreto.txt credenciales.json; do
  new_case "es_${nm%%.*}"; echo x >"$C/backups/files/$nm"; run_case; expect "C03 nombre en espanol '$nm' dentro de BACKUP_DIR" 11 '^FAIL C03'
done
for ext in gpg age 7z zip enc; do
  new_case "pkg_$ext"; mkdir -p "$C/sec"; echo x >"$C/sec/paquete.$ext"; echo x >"$C/sec/contraseña.txt"; run_case CUSTODY_RECOVERY_DIRS="$C/sec"
  expect "C09 paquete .$ext + contrasena en la misma raiz" 11 '^FAIL C09 .*contrase'
done
new_case pkg_solo;        mkdir -p "$C/sec"; echo x >"$C/sec/paquete.7z"; run_case CUSTODY_RECOVERY_DIRS="$C/sec"; expect "C09 paquete .7z solo: OK" 0 '' '^FAIL'

# --- C10 sin repositorio Git: aviso explicito ------------------------------------------------------------------------------
new_case nogit;           mkdir -p "$T/nogit/scripts/backup"; cp "$HERE/check-custody.sh" "$HERE/lib.sh" "$T/nogit/scripts/backup/"
OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" GIT_CEILING_DIRECTORIES="$T" bash "$T/nogit/scripts/backup/check-custody.sh" --no-status --quiet 2>&1); RC=$?
expect "C10 fuera de un repo Git: WARN explicito (no falla)" 0 '^WARN C10 .*no es un repositorio Git' '^FAIL'

# --- C01/C02 omitidos por filesystem: WARN visible con --quiet -------------------------------------------------------------
if (( ! MODES )); then
  new_case skipwarn;      OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" bash "$HERE/check-custody.sh" --no-status --quiet 2>&1); RC=$?
  expect "C01/C02 omitidos (NTFS): WARN visible con --quiet" 0 '^WARN C0[12] .*NO verificables' '^FAIL'
fi

# --- symlinks (C02/C04) -----------------------------------------------------------------------------------------------------
: >"$T/pf"; ln -s "$T/pf" "$T/pl" 2>/dev/null; if [[ -L "$T/pl" ]]; then LINKS=1; else LINKS=0; echo "SKIP casos de symlink: el entorno no crea symlinks"; fi
rm -f "$T/pl" "$T/pf"
if (( LINKS )); then
  new_case lnk_env;       mv "$C/backup-offsite.env" "$C/backups/real.cfg"; ln -s "$C/backups/real.cfg" "$C/backup-offsite.env"; run_case; expect "C02 backup-offsite.env es un symlink hacia dentro de BACKUP_DIR" 11 '^FAIL C02 .*dentro de'
  new_case lnk_bk;        mv "$C/backups" "$C/realbk"; ln -s "$C/realbk" "$C/backups"; echo x >"$C/realbk/files/server.key"; run_case; expect "C03 BACKUP_DIR es un symlink: se inspecciona el destino real" 11 '^FAIL C03 .*server\.key'
  new_case lnk_off;       mkdir -p "$C/realdisk"; ln -s "$C/realdisk" "$C/disk"; echo x >"$C/realdisk/id_rsa"; run_case BACKUP_OFFSITE_DISK_DIR="$C/disk"; expect "C04 directorio offsite via symlink" 11 '^FAIL C04 .*id_rsa'
  new_case lnk_envin;     mkdir -p "$C/disk"; mv "$C/.env" "$C/disk/real.env"; ln -s "$C/disk/real.env" "$C/.env"; run_case BACKUP_OFFSITE_DISK_DIR="$C/disk"; expect "C02 .env symlink hacia el directorio offsite" 11 '^FAIL C02 .*dentro de'
fi

# --- estado para el monitor ------------------------------------------------------------------------------------------------
new_case status_ok
OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" bash "$HERE/check-custody.sh" 2>&1); RC=$?
if [[ $RC == 0 ]] && grep -q '"exit":0,"verified":true' "$C/backups/monitor-status/custody.json"; then ok "escribe monitor-status/custody.json (exit 0, verified true)"; else bad "estado OK :: rc=$RC"; fi
echo "x" >"$C/backups/recovery-key.pem"
OUT=$(env -i PATH="$PATH" HOME="$C/home" BACKUP_DIR="$C/backups" COMPOSE_ENV_FILES="$C/.env" BURY_OFFSITE_ENV_FILE="$C/backup-offsite.env" CUSTODY_RECOVERY_DIRS="" bash "$HERE/check-custody.sh" 2>&1); RC=$?
if [[ $RC == 11 ]] && grep -q '"exit":11,"verified":false' "$C/backups/monitor-status/custody.json" && grep -q 'C03' "$C/backups/monitor-status/custody.json"; then ok "estado FAIL: exit 11, verified false, reglas en el mensaje (sin rutas ni secretos)"; else bad "estado FAIL :: rc=$RC"; fi
if ! grep -qF -e "$P1" -e "$P2" "$C/backups/monitor-status/custody.json"; then ok "el estado no contiene secretos"; else bad "el estado contiene secretos"; fi
if (( MODES )); then
  [[ "$(stat -c %a "$C/backups/monitor-status/custody.json")" == 600 ]] && ok "custody.json con modo 600" || bad "custody.json con modo 600"
fi

echo; echo "resultado: $PASS OK, $FAIL fallos"; (( FAIL == 0 ))
