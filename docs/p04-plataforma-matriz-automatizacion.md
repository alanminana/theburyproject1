# P-04…P-08 / C-03 / D-05 — Independencia del host y matriz de automatización

Fecha: 2026-11-14. Rama `task/p04-p08-independiente-host-20261114`. **No se instaló ni configuró el servidor definitivo, no se tocó `TheBuryProjectDb`, no se aplicó ninguna migración real, no se activó CSP `Enforce`, no se tocaron credenciales ni respaldos reales.** Todo lo de abajo corre sobre stacks Docker sintéticos y descartables (proyecto Compose propio, `.env` generado con secretos aleatorios en un directorio temporal, puerto y subred propios, solo `127.0.0.1`).

## 0. Decisión de plataforma

El ERP se despliega en **contenedores Docker Linux**. El sistema operativo del host **no está decidido**: Windows 11 (Docker Desktop + WSL2) o Ubuntu (Docker Engine). **Windows Server (Hyper-V + VM) no es la plataforma elegida**; `instalacion-windows-server.md` y `Test-WindowsServerInstall.ps1` quedan como alternativa archivada, sin trabajo nuevo. Evidencia comparada y recomendación técnica para elegir entre ambos: [p04-decision-sistema-operativo-servidor.md](p04-decision-sistema-operativo-servidor.md). Lo que sigue está escrito para que **ninguna prueba dependa de esa decisión**: lo común se prueba una vez (§2) y lo específico de cada host está separado (§4).

## 1. Qué se cierra con pruebas automatizadas y qué exige hardware, usuarios o infraestructura reales

| Trabajo | Se cierra con pruebas automatizadas (evidencia de esta ronda) | Necesariamente requiere algo real |
|---|---|---|
| **P-04** Compose, persistencia, restauración, reinicio | Instalador real (`install.sh`) sobre un stack sintético; `down`/`up` conserva datos **y la sesión** (claves de Data Protection); caída real (`kill -9` del proceso 1) de `app`, `db` y `caddy` → Docker los reinicia solos; `docker kill` manual **no** reinicia (esperado); backup + restauración real + `DBCC CHECKDB`. En Docker Desktop/WSL2 **y** en Ubuntu + Docker Engine nativo (§3) | Reinicio del **equipo** (autoarranque de Docker/WSL, inicio de sesión automático en Windows, `systemctl enable docker` en Ubuntu), corte de energía/UPS, `restart` ante reinicio del daemon (en el host real), rendimiento real del disco |
| **P-05** Tailscale, Caddy, firewall | Política ACL/grants para ambos perfiles (131 pruebas), modelo de acceso Linux (44), firewall `DOCKER-USER` real con clientes tailnet/LAN/ajeno (18, en dockerd descartable), Caddy `tls internal` + confianza de la CA (14), modelo de acceso Windows 11 (49), instalación de la CA (16). Runner único: `scripts/install/test-p05-suite.sh` | Nodo real en la tailnet y ACL **publicada** en la consola de Tailscale; cliente remoto real (acceso permitido) y desde datos móviles (denegado); clave sin expiración, *Run unattended*, MagicDNS; CA instalada en los clientes reales; reglas Allow de Docker Desktop frente al firewall con un cliente LAN real; `ufw` + `DOCKER-USER` en el Ubuntu real |
| **P-07** Configuración inicial sintética | `install.sh` completo sobre entorno sintético: `.env` 0600 sin `CHANGE_ME`/CRLF y secretos distintos, `db-init`, `migrate`, logins SQL separados, administrador único, **primer ingreso en dos fases (credenciales + Términos y Condiciones)**, 24 pantallas principales y de configuración responden 200 con base vacía, 443 solo en loopback, db/app sin puertos | Datos reales (empresa, plan de cuentas/precios, catálogo), correo del administrador real, dominio definitivo, certificado/CA en clientes, licencia comercial de AutoMapper si se elige, cuenta Tailscale |
| **P-08** Monitoreo, healthchecks, alertas | `isolated_smoke.py` en Windows 11 (Python 3.14) y en Ubuntu/WSL (Python 3.12): SQL, app y Caddy detenidos y recuperados → alertas `docker.*`/`http.*` y resolución, sin tormenta de repetidas (16 notificaciones, 28 pruebas unitarias + `test_status_chain.py`: fallos de offsite/restore/custodia producidos por los scripts reales llegan como alerta y se resuelven; repetido el 2026-11-14). Procedimiento del receptor: `p08-receptor-alertas-procedimiento.md`. `Check-ErpHealth` (27 escenarios simulados) | Receptor de alertas **externo** (otro sitio/Uptime Kuma), canal de notificación real (correo/WhatsApp), cuotas de CPU/RAM bajo carga real y semanas de histórico, ejecutar `Check-ErpHealth` contra el servidor vivo (ruta `wsl.exe` → `monitor-status`) |
| **C-03** E2E omitidos | Ya cerrado en lo automatizable: 4 omitidos clasificados con cobertura alternativa (`docs/e2e-omitidos-clasificacion.md`), gate `control-omitidos.js` | Ninguno pendiente por hardware; los 4 restantes son por diseño |
| **D-05** CSP | Ejecutado: `Enforce` y `ReportOnly` en 3 navegadores × 7 roles, escenarios dinámicos, HTTPS+SignalR. Otra sesión (rama `task/c03-d05-cierre-20261114`, **PR #309 ya fusionada**) cubrió los módulos sin escenario CSP y el cruce SQL de C-03; **no se tocó ni se duplicó** | Safari/iOS real y móvil real, host real y `ERP_DOMAIN` real, **ventana de observación en uso real** con `ReportOnly` y la **decisión** de activar `Enforce` (propietario) |

## 2. Resultado de esta ronda (mismo arnés, dos motores)

`scripts/install/test-host-independence.sh` (48 comprobaciones; 47 en Windows 11, donde el repo en `/mnt/d` genera un aviso en lugar de la comprobación de sistema de archivos nativo). Los números salen de las corridas del 2026-11-14 sobre el mismo commit.

| Host / motor | Resultado | Notas |
|---|---|---|
| **Windows 11 Pro + Docker Desktop 29.6.2 (WSL2, Ubuntu 24.04)** | **47 PASS, 0 FAIL** | Perfil: `familia=docker-desktop`, cgroup v2, repo en `/mnt/d` (`v9fs`), estado/backups en ext4 de WSL |
| **Ubuntu 24.04 + Docker Engine nativo (docker-ce 29.9.3, containerd, overlayfs)** | **48 PASS, 0 FAIL** | Ubuntu descartable (`test-host-independence-ubuntu-engine.sh`): dockerd propio, cgroup v2 anidado. Kernel compartido con WSL2: valida el motor y el userland, **no** el kernel/ufw/systemd del servidor final |

### 2.1 Windows 11 / Docker Desktop

Arnés: 47 PASS, 0 FAIL, con el repo en `/mnt/d` (aviso de lentitud, no fallo). Las pruebas PowerShell se corrieron en el propio equipo (Windows 11 build 26300, PowerShell 5.1): `Test-RemoteAccessModel` 49/0, `Test-ErpHealth` (todos los escenarios), `Test-InstallErpCa` 16/0. `preflight-windows.ps1` (solo lectura) sobre esta PC de desarrollo: 1 FALLA (Tailscale no instalado) y 4 ATENCIÓN (sin inicio de sesión automático, sin `.wslconfig`, 2 reglas Allow de Docker, puerto 443 en uso); **no es el servidor**.

## 3. Hallazgos (lo que estas pruebas descubrieron)

1. **Checkout de Windows 11 con CRLF en archivos que consume Linux (corregido).** `.gitattributes` solo fijaba LF para `*.sh`, `Dockerfile` y `Caddyfile`. Con `core.autocrlf=true`, `.env.example`, `docker-compose*.yml`, `docker/db-init/*.sql`, `*.service`, `*.py` y `*.hujson` quedaban en CRLF (61 archivos): valores con `\r` en el `.env` generado, unit de systemd inválido. Ahora todos son `eol=lf`; `test-host-portability.sh` lo verifica (por atributo y en el árbol de trabajo) y corre en CI.
2. **`docker kill` / `docker stop` manual no activa la política `restart`.** Solo reinician las caídas reales (OOM, crash, `kill -9` del proceso). Se prueba con `kill -9` del PID desde el motor; el `docker kill` manual queda como prueba **negativa** explícita. Consecuencia operativa: una parada manual solo la detecta el monitor (`docker.app` CRITICAL), no Docker.
3. **El primer ingreso del administrador tiene dos fases**: credenciales y aceptación de Términos y Condiciones en la misma pantalla (`Input.NombreCompleto` + `Input.AceptaTerminos`). Un script de humo que solo envíe usuario/contraseña ve un 200 que parece fallo. Documentado y cubierto por el arnés.
4. **Modo de ejecución del instalador**: `install.sh` y los scripts de respaldo no funcionan desde Git Bash (reescribe rutas, rompe los *bind mounts*); el arnés lo rechaza y exige Linux/WSL2.
5. **Repo en `/mnt/c` o `/mnt/d` (9p) en Windows 11**: lento y sin permisos POSIX; el arnés avisa. En el servidor, clonar dentro del sistema de archivos de Linux (`~/…`); SQL Server y los respaldos ya viven en volúmenes/ext4.
6. **Bit ejecutable**: `scripts/backup/test-offsite-sftp.sh` estaba en modo 100644 en el índice; corregido (solo el modo, sin tocar su contenido, para no colisionar con #308).
7. **Job `windows-scripts` de CI es `continue-on-error: true`**: sus fallos no bloquean. Las tres pruebas PowerShell relevantes pasan hoy en Windows 11 real; queda como **decisión** quitar `continue-on-error` (no se cambió: depende de la estabilidad del runner `windows-latest`, no medible desde acá).
8. **`production-qa`** (imagen usada por `isolated_smoke.py`) ya existía localmente y es anterior a `main`: el smoke valida el monitor, no la versión actual de la app.

## 4. Adaptaciones por plataforma (lo que NO es común)

| Tema | Windows 11 + Docker Desktop (WSL2) | Ubuntu + Docker Engine |
|---|---|---|
| Instalación | `install-windows.ps1` (administrador) → `install.sh` dentro de WSL; `preflight-windows.ps1` antes | `server-linux-bootstrap.sh` + `install.sh` |
| Memoria / recursos | `.wslconfig` (`memory=6GB`); sin él WSL2 toma hasta la mitad de la RAM | cgroups nativos; límites del compose (`DB_MEMORY`, `DB_CPUS`) |
| Arranque tras reinicio | Docker Desktop *al iniciar sesión* + inicio de sesión automático con cuenta dedicada (sin sesión no arranca nada) | `systemctl enable docker` (arranca sin sesión); `restart: unless-stopped` hace el resto |
| Firewall | Reglas de Windows (`New-NetFirewallRule`, `-AllowFrom`); **las reglas Allow de `com.docker.backend.exe` pueden anular el filtro** → verificar con cliente LAN real | `bury-firewall.sh` sobre `DOCKER-USER` (un puerto publicado **no** pasa por `ufw`/`INPUT`); `ufw` para el resto |
| Tailscale | Servicio de Windows, *Run unattended*, IP `100.x` puede no existir al arrancar Docker (si `ERP_BIND_ADDRESS` es esa IP, Caddy no arranca) | `tailscale0` + `systemd`; atar `100.64.0.0/10` a `tailscale0` |
| Respaldos programados | Programador de tareas (`TheBury*`), con el **mismo usuario** que corre las tareas | `cron` (`install-schedule.sh`) o `systemd timers` |
| Archivos | `core.autocrlf=true` por defecto → depende de `.gitattributes` (§3.1); repo en el FS de Linux | LF nativo; el bit ejecutable sale del índice |
| Salud | `Check-ErpHealth.ps1` (Windows → `wsl.exe`) | `monitor.py` como servicio `bury-monitor.service` |
| Parada limpia | Docker Desktop envía SIGTERM; SQL Server ignora SIGTERM salvo el entrypoint fijado | `systemd` espera `TimeoutStopSec`; mismo entrypoint |

## 5. Cómo reproducir

```bash
# Estático, sin Docker ni root (CI lo corre):                bash scripts/install/test-host-portability.sh
# P-05, todo lo automatizable (en PC: dockerd descartable):  bash scripts/install/test-p05-suite.sh --in-dind
# P-04/P-07 en tu host (Linux o WSL2, ~5-10 min, construye la imagen la primera vez):
bash scripts/install/test-host-independence.sh                       # wsl -d Ubuntu -- bash ... desde Windows
# El mismo arnés en un Ubuntu + Docker Engine descartable (necesita Internet):
bash scripts/install/test-host-independence-ubuntu-engine.sh --image theburyproject/erp:<tag>
# P-08 (stack descartable propio; usa la imagen theburyproject/erp:production-qa):
python scripts/monitoring/isolated_smoke.py                          # Windows o Linux
```

CI: `host-independence.yml` (Ubuntu nativo, en cada PR que toque instalador, deploy, backup, compose, Dockerfile, `.env.example` o `.gitattributes`) y el paso `test-host-portability.sh` en el job de scripts.

## 6. Coordinación y límites del trabajo

- **P-03 (#301, `task/p03-cuentas-20261112`) y P-06 (#308, `task/p06-rotacion-dr-20261113`) — ambas ya fusionadas durante esta tarea**: no se modificaron sus ramas ni se duplicó su trabajo. Esta rama **invoca** `scripts/backup/*.sh` tal como están en `main` (no edita su contenido; solo el modo ejecutable de un archivo, §3.6). Si #308 cambia los parámetros de `backup.sh`/`restore-*.sh`, el arnés los vería romperse en CI, que es lo buscado.
- **C-03/D-05**: resueltos por otra sesión en #309 (fusionada durante esta tarea): CSP de módulos sin escenario, cruce SQL de C-03 y `Program.cs` que falla cerrado (exit 1) si la inicialización de la base falla fuera de desarrollo (no aplica con `Database:InitializeOnStartup=false`, que es el modo Docker que prueba este arnés). No se tocó; esta rama solo se actualizó con `main` después.
- **Revisión independiente (Codex, `adversarial-review`, solo lectura, worktree sin cambios durante el job)**: 4 hallazgos, los 4 confirmados y corregidos. (1) alto: variables exportadas (`BACKUP_DIR`, `BACKUP_REMOTE`, `COMPOSE_FILE`, `DP_*`) podían apuntar los scripts de backup a recursos reales → el arnés las descarta al empezar, rechaza daemons remotos/contextos no locales, fija `BACKUP_DIR`/`COMPOSE_FILE`/`BACKUP_REMOTE=""` y comprueba que el `db` y el `/backups/sql` sean los del proyecto propio **antes** de escribir un respaldo. (2) alto: `install.sh` sobrescribe `caddy-root-ca.crt` del checkout → el arnés se niega a correr si ya existe. (3) alto: `test-p05-suite.sh` corría el firewall real como root → ahora solo con `--in-dind`. (4) medio: la limpieza dejaba `backups/sql` (uid de SQL Server) sin borrar → se borra con un contenedor auxiliar acotado al directorio temporal y la corrida falla si queda algo.
- Todos los recursos creados (contenedores, volúmenes, redes, directorios temporales, el Ubuntu descartable) se eliminan al terminar cada corrida; la imagen `theburyproject/erp:p04hi-*` se conserva como caché.

## 7. Resultado Windows 11 y pendientes del propietario

Pendientes que **solo** el propietario o el hardware pueden cerrar: elegir plataforma (Windows 11 o Ubuntu); autorizar la instalación del servidor; nodo y ACL de Tailscale reales y pruebas desde clientes remotos; copia externa (SFTP) y custodia de claves; receptor de alertas externo; ventana de observación de CSP y decisión de `Enforce`; dominio/certificado definitivos; quitar `continue-on-error` del job Windows de CI.
