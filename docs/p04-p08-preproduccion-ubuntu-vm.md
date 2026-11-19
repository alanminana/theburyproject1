# P-04 a P-08 — validación en la VM Ubuntu de preproducción (2026-11-19)

Entorno **descartable de ensayo**: VM Ubuntu 26.04 (VMware, 4 CPU, 7,8 GiB, disco 100 GB) con Docker Engine 29.10 y Compose 5.8
(repositorio oficial de Docker), ERP instalado con `scripts/install/install.sh` sobre `main` (02c01635), SQL Server Express **nuevo** y datos
**sintéticos** (un cliente creado por la propia app). No se tocó ninguna base histórica. Matriz por criterio y pruebas posteriores (corte de energía, disco lleno, drill en segunda máquina, Tailscale de ensayo, observador externo, D-05): [p04-p08-matriz-criterios-vm-simulada.md](p04-p08-matriz-criterios-vm-simulada.md). Este documento **no cierra** P-04..P-08: separa lo
comprobado aquí de lo que sigue dependiendo del servidor definitivo y del propietario. Sin secretos ni credenciales.

## 1. Validado en esta VM (con evidencia)

| Área | Prueba | Resultado |
|---|---|---|
| P-04 instalación | `install.sh` en host limpio: 144 migraciones, `/health/live`+`ready` 200, smoke, Caddy + CA | OK |
| P-04 persistencia | `restart`, `down`/`up` sin `-v`, reinicio completo de la VM (×3) | El cliente sintético persiste; ERP `ready` 200 a ~44 s del reinicio |
| P-04 fallos reales | `kill -9` de app → vuelve en ~2 s; `kill -9` de SQL → `ready` 503 con `live` 200, recupera en ~4 s con datos intactos | OK (`RestartCount`=1, sin OOM) |
| P-04 almacenamiento | Disco ampliado de 20 a 100 GB en línea (`growpart` + `resize2fs`, ext4) | 81 GB libres, `e2fsck -n` limpio |
| P-05 red | `bury-firewall.sh` (cadena `DOCKER-USER`) + `ufw` por `server-linux-bootstrap.sh`: 443 solo desde la LAN y `100.64.0.0/10@tailscale0`; SSH solo desde el equipo administrador | LAN → 200; origen ajeno (contenedor en `docker0`) → bloqueado por timeout; persiste tras reinicio |
| P-05 HTTPS | CA interna de Caddy: con la CA → 200; sin la CA → falla; desde Windows con `--ssl-no-revoke` (schannel no puede comprobar revocación de una CA interna) | OK |
| P-05 Tailscale | Paquete oficial instalado (1.104.7, repo `pkgs.tailscale.com`), `tailscaled` activo | **Sin autenticar** (ver §3) |
| P-06 backups | `backup.sh full/files/log`, `restore-test.sh` (DBCC CHECKDB OK), cron instalado (`/etc/cron.d/bury-backup`), log de transacciones cada 15 min verificado | OK; solo copia local |
| P-06 offsite (mecanismo) | `test-offsite-sftp.sh` contra SFTP **descartable** + rclone crypt | 86 PASS, 0 FAIL, sin residuos |
| P-06 DR | `test-dr-integral.sh`: pérdida total → recuperación solo desde la copia externa aislada | 53 PASS; RTO técnico 38 s (fetch 7 s, files 2 s, SQL 4 s, stack 16 s), CA restaurada, cookie vieja válida; sin residuos |
| P-07 independencia de host | `test-host-independence.sh` nativo en Ubuntu + Engine (clon aparte) | 48 PASS, 0 FAIL, 0 SKIP (perfil `linux-engine`); instalación en 103 s; limpió sus propios recursos |
| P-08 monitoreo | `monitor.py` como servicio systemd + receptor de **ensayo** (webhook local, dedupe por `event_id`): `--test-channel` y caída controlada de Caddy | `http.erp/live/ready` y `docker.caddy` CRITICAL → `resolved` al volver; sin tormenta de repetidos |
| P-08 observador | `Check-ErpHealth.ps1 -VmHost` desde esta PC (host distinto): HTTPS, contenedores, backups, servicios | Funciona; marcó FALLA solo por Tailscale sin autenticar |

## 2. Defectos y hallazgos corregidos

1. **`tls.expiry` CRITICAL permanente con la CA interna de Caddy** (`scripts/monitoring/monitor.py`). Las hojas de `tls internal` duran ~12 h y se
   renuevan solas; el umbral fijo de 21/7 días nunca podía cumplirse. Ahora, con vida total ≤ 7 días, se mide la **fracción** de vida restante
   (warning < 25 %, critical < 10 %; Caddy renueva con ~33 %); los certificados largos conservan 21/7 días; una vida no positiva es error de colector.
   `test_monitor.py`: 37 tests (bordes, expirado, extracción de fechas de `tls_info`). Verificado en la VM: pasa de CRITICAL a INFO (90 %).
2. **Alertas `backup.offsite-*` permanentes sin copia externa**: `backup_windows` exige un estado verificado por cada clave. En hosts sin offsite hay
   que omitirlas del `config.json` (decisión explícita, no falso incidente). Documentado en `monitoreo-alertas.md`.
3. **Docker publica el 443 saltándose `ufw`**: `ufw` por sí solo no restringe el puerto de Caddy; el filtro real es `bury-firewall.sh`. Aplicado y verificado.
4. **Dos reinicios de `dockerd` seguidos (<30 s)** dejaron los contenedores en `Exited (0)` sin volver. Un reinicio simple sí los recupera. Con
   `live-restore` el cierre de SQL deja de ser ordenado, por lo que se usa `{"shutdown-timeout": 60}` como pide la doc y **no** `live-restore`.
   No encadenar reinicios del daemon (p. ej. actualizaciones automáticas de `docker-ce` + reinicio manual).
5. Superficie innecesaria de Ubuntu Desktop: se deshabilitaron `avahi`, `cups`, `cups-browsed`, `ModemManager` y la suspensión (`sleep.target`…).

## 3. Pendiente — requiere servidor definitivo, propietario o credenciales reales (NO cerrado)

| Ítem | Qué falta |
|---|---|
| Tailscale (P-05) | Autenticar el nodo (`sudo tailscale up --hostname=tbp --advertise-tags=tag:erp --accept-dns=false --accept-routes=false --ssh=false`) con la cuenta del propietario, aplicar `deploy/tailscale/policy.hujson`, comprobar `tag:erp`, expiración deshabilitada y acceso desde dos identidades reales |
| Offsite real (P-06) | Destino SFTP/S3/B2 en **otro sitio**, credenciales, `known_hosts` por canal independiente, custodia de la contraseña crypt y de `BACKUP_CA_KEY`, y un restore **desde esa copia** |
| Receptor/observador externo (P-08) | Canal de aviso humano (correo/Telegram/…), Uptime Kuma u otro en **otro sitio** con heartbeat; sin él, la caída total del host/energía/router no avisa a nadie |
| Servidor definitivo (P-04) | Hardware, UPS, *restore on AC power loss*, Windows Server/Hyper-V (o Ubuntu físico) con la decisión de plataforma, y repetir el reinicio/corte de energía real |
| Umbrales con carga real (P-08) | CPU/RAM/disco/crecimiento con semanas de histórico y usuarios reales |
| SSH administración | Hoy solo desde el equipo administrador (IP /32, DHCP); pasar a la IP de Tailscale al autenticarla. Autenticación por contraseña de SSH sigue habilitada (decisión del propietario) |
| Windows | El único acceso HTTPS desde Windows con CA interna requiere `--ssl-no-revoke` (curl/schannel); los navegadores usan el almacén instalado con `Install-ErpCa.ps1` |

## 4. Reproducción

```bash
# en la VM (checkout aparte del real: el arnés protege la CA existente)
bash scripts/backup/test-offsite-sftp.sh
bash scripts/backup/test-dr-integral.sh
bash scripts/install/test-host-independence.sh
# monitor (hasta contar con receptor real): /etc/bury-monitor/{config.json,monitor.env}, unidad scripts/monitoring/bury-monitor.service
python3 scripts/monitoring/monitor.py --test-channel      # con monitor.env cargado
```

Ver también: `docs/estado-software-pre-instalacion.md`, `docs/p05-tailscale-acceso-remoto.md`, `docs/p08-receptor-alertas-procedimiento.md`,
`docs/offsite-monitoring-activacion.md`.
