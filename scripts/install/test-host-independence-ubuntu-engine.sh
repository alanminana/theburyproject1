#!/usr/bin/env bash
# P-04: corre test-host-independence.sh sobre un Ubuntu 24.04 DESCARTABLE con Docker Engine (docker-ce) propio, en lugar de Docker Desktop.
# Sirve para comprobar, sin un servidor Ubuntu real, que el ERP no depende de Docker Desktop: otro motor (dockerd + containerd + overlay2
# sobre ext4), otro usuario, otro sistema de archivos. NO sustituye una prueba en el hardware Ubuntu final (kernel, ufw, systemd, reinicio).
#
#   bash scripts/install/test-host-independence-ubuntu-engine.sh [--image repo:tag] [--keep] [-- <flags de test-host-independence.sh>]
#     --image   imagen del ERP ya construida en el daemon del host (se copia al Ubuntu con docker save | docker load). Por defecto se
#               construye/usa theburyproject/erp:p04hi-<sha> en el host.
#
# Aisla: contenedor privilegiado `buryub-<pid>` (su propio dockerd y su propio /var/lib/docker en un volumen propio); no toca contenedores,
# redes ni volumenes del host salvo los propios. Necesita Internet (apt y las imagenes mssql/caddy/alpine fijadas por digest en el compose).
# Se ejecuta con Docker del host accesible (Linux, WSL2 o Git Bash). Salida: la del arnés (0 todo OK).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
export MSYS_NO_PATHCONV=1
IMAGE="" KEEP=0 PASSTHRU=()
while (( $# )); do
  case "$1" in
    --image) IMAGE="${2:-}"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    --) shift; PASSTHRU=("$@"); break ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1"; exit 2 ;;
  esac
done
command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || { echo "docker del host no responde"; exit 2; }

N="buryub-$$"; VOL="$N-docker"
cleanup() { local ec=$?; (( KEEP )) && { echo "--keep: contenedor $N y volumen $VOL conservados"; exit $ec; }
  docker rm -f "$N" >/dev/null 2>&1; docker volume rm -f "$VOL" >/dev/null 2>&1; exit $ec; }
trap cleanup EXIT; trap 'exit 143' TERM INT HUP

if [[ -z "$IMAGE" ]]; then
  IMAGE="theburyproject/erp:p04hi-$(git -C "$REPO" rev-parse --short HEAD 2>/dev/null || echo nogit)"
  docker image inspect "$IMAGE" >/dev/null 2>&1 || { echo "construyendo $IMAGE en el host"; docker build -t "$IMAGE" "$REPO" || exit 2; }
fi
docker image inspect "$IMAGE" >/dev/null 2>&1 || { echo "la imagen $IMAGE no existe en el host"; exit 2; }

echo "== Ubuntu 24.04 + Docker Engine descartable ($N) =="
docker run -d --name "$N" --privileged -v "$VOL:/var/lib/docker" -e DEBIAN_FRONTEND=noninteractive ubuntu:24.04 sleep infinity >/dev/null || exit 2
docker exec "$N" bash -c '
  set -e
  apt-get update -qq >/dev/null
  apt-get install -y -qq ca-certificates curl gnupg git openssl python3 iproute2 iptables util-linux xz-utils >/dev/null
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  echo "deb [signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" > /etc/apt/sources.list.d/docker.list
  apt-get update -qq >/dev/null
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-compose-plugin >/dev/null
' || { echo "no se pudo instalar Docker Engine en el Ubuntu descartable"; exit 2; }
# cgroup v2 anidado: sacar los procesos del cgroup raiz del contenedor y delegar los controladores (lo mismo que hace el entrypoint de docker:dind)
docker exec "$N" bash -c 'if [ -f /sys/fs/cgroup/cgroup.controllers ]; then mkdir -p /sys/fs/cgroup/init && xargs -rn1 < /sys/fs/cgroup/cgroup.procs > /sys/fs/cgroup/init/cgroup.procs 2>/dev/null; sed -e "s/ / +/g" -e "s/^/+/" < /sys/fs/cgroup/cgroup.controllers > /sys/fs/cgroup/cgroup.subtree_control; fi; true'
docker exec -d "$N" bash -c 'dockerd >/var/log/dockerd.log 2>&1'
for _ in $(seq 1 40); do docker exec "$N" docker info >/dev/null 2>&1 && break; sleep 2; done
docker exec "$N" docker info >/dev/null 2>&1 || { echo "dockerd no arranco"; docker exec "$N" tail -20 /var/log/dockerd.log; exit 2; }
echo "  $(docker exec "$N" bash -c '. /etc/os-release; echo "$PRETTY_NAME"; docker version --format "docker-ce {{.Server.Version}} storage={{.Server.Driver}}" 2>/dev/null || docker info --format "docker-ce {{.ServerVersion}} storage={{.Driver}}"')"

echo "  copiando la imagen del ERP ($IMAGE) y el codigo"
docker save "$IMAGE" | docker exec -i "$N" docker load >/dev/null || exit 2
# solo archivos versionados o nuevos no ignorados (sin node_modules/bin/obj)
( cd "$REPO" && git ls-files -co --exclude-standard -z | tar --null -T - -cf - 2>/dev/null ) | docker exec -i "$N" bash -c 'mkdir -p /srv/erp && tar -xf - -C /srv/erp' || exit 2
docker exec "$N" bash -c 'cd /srv/erp && git init -q . 2>/dev/null; chmod +x scripts/*/*.sh scripts/backup/container/*.sh docker/db-init/*.sh 2>/dev/null; true'

echo "== arnés en Ubuntu + Docker Engine =="
docker exec "$N" bash -c "cd /srv/erp && bash scripts/install/test-host-independence.sh --image '$IMAGE' --out /tmp/out ${PASSTHRU[*]:-}"
rc=$?
docker exec "$N" cat /tmp/out/host-profile.json 2>/dev/null
exit $rc
