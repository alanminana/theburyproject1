# P-08 — Chequeo de salud del servidor (`Check-ErpHealth.ps1`)

Script de **solo lectura** para el servidor Windows 11 + Docker Desktop (WSL2) + Tailscale. Complementa a
`scripts/install/preflight-windows.ps1` (P-07, *antes* de instalar): este se usa *después*, a mano o desde el Programador de tareas.
No arranca, detiene ni repara nada; no escribe archivos ni toca la base, Docker, WSL, firewall, registro o tareas.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install\Check-ErpHealth.ps1            # tabla
powershell -ExecutionPolicy Bypass -File scripts\install\Check-ErpHealth.ps1 -Json      # JSON para otro observador
```

Parámetros: `-Dominio tbp`, `-Puerto 443`, `-Proyecto theburyproject`, `-Distro <WSL>`, `-BackupDir /srv/bury-backups` (dentro de WSL),
`-StatusDir <carpeta de Windows con los .json>` (alternativa a WSL), `-DiscoMinGB 20`.

**Código de salida:** `0` todo OK · `1` hay ATENCIÓN y ninguna FALLA · `2` hay al menos una FALLA.

## Qué verifica

| Área | Chequeo | FALLA cuando | ATENCIÓN cuando |
|---|---|---|---|
| Equipo | disco del sistema, reinicio pendiente | < `-DiscoMinGB` | < 2× mínimo; reinicio pendiente |
| Docker | daemon; `db`, `app`, `caddy` corriendo y sanos; `db-init`/`migrate` terminados con éxito | daemon caído, contenedor ausente/caído/unhealthy/en bucle, tarea única con error | `health: starting` |
| ERP | `https://<dominio>/health/live` y `/health/ready` contra `127.0.0.1` (cuerpo `Healthy`) | sin respuesta, 503/`Unhealthy`, otro código | — (certificado no confiable = INFO) |
| Backups | `monitor-status/*.json` (los escribe `scripts/backup`) | `full`/`files` > 30 h o no verificado; `log` > 45 min o no verificado; sin registros; copia offsite con error o > 30 h; corrida `run-*` fallida posterior al último éxito | restore-test > 8 días o sin hacer; `log` > 30 min; sin registro offsite; no se pudo leer |
| Tareas | `TheBury*` | deshabilitada, último resultado ≠ 0, última corrida más vieja que el umbral de su tipo (Full 30 h, Log 45 min, Restore 8 días) | no corrió todavía; sin próxima ejecución; no hay tareas |
| Tailscale | servicio, `BackendState`, vencimiento de clave | servicio detenido, desconectado, clave vencida | clave vence en < 30 días; no instalado |

## Límites (no vender más de lo que hace)

- Es un chequeo **local**: si el servidor entero está caído, nadie corre el script. La alerta ante caída total necesita un observador en
  **otro sitio** (Uptime Kuma externo, ver `propuesta-infraestructura-produccion.md` §4). `-Json` está pensado para alimentarlo.
- *Backup verificado* significa lo que registran los scripts (`verified: true`); que la copia offsite sea recuperable solo lo prueba un restore real.
- La lectura de backups necesita que WSL responda; si falla queda como ATENCIÓN con el motivo, no como OK.
- `curl.exe` se llama con `-k` para el chequeo de salud (la CA de Caddy puede no estar instalada en el servidor); la confianza del certificado se informa aparte.
- Las tareas programadas se consultan con el usuario que ejecuta el script: ejecutar con el **mismo usuario** que corre las tareas de backup.

## Pruebas

`scripts/install/Test-ErpHealth.ps1` carga el script por *dot-source*, reemplaza las funciones de acceso al sistema
(`Invoke-Native`, `Get-DiskInfo`, `Get-TaskInfoList`, …) y recorre 27 escenarios simulados (Docker apagado, contenedor unhealthy,
`/health/ready` 503, backup viejo/fallido, offsite fallido, restore atrasado, disco bajo, tareas deshabilitadas, Tailscale caído o con clave vencida, salida JSON…).
No usa Docker, WSL, Tailscale ni red reales. Compatible con Windows PowerShell 5.1.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install\Test-ErpHealth.ps1
```

Pendiente (requiere el servidor real): ejecutarlo contra el stack levantado para validar la ruta `wsl.exe` → `monitor-status` y `curl.exe --resolve`, que en las pruebas simuladas están sustituidas.

## Modo Windows Server (`-VmHost`)

Con la VM Hyper-V de `docs/instalacion-windows-server.md`, Docker, los backups (`monitor-status`), `systemd` y Tailscale se consultan **por SSH (solo lectura)** dentro de la VM,
y el estado de la VM (latido, autoarranque, apagado, checkpoints) por Hyper-V:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install\Check-ErpHealth.ps1 -VmHost 192.168.1.50 `
    -VmKey C:\ProgramData\TheBury\server\bury_vm_ed25519 -VmKnownHosts C:\ProgramData\TheBury\server\known_hosts
```

Parámetros: `-VmHost` (activa el modo), `-VmName` (TheBury-ERP), `-VmUser` (bury), `-VmKey`, `-VmKnownHosts`. En este modo no se evalúan las tareas programadas de Windows
(los backups los programa `cron` en la VM; se verifica `/etc/cron.d/bury-backup`). `StrictHostKeyChecking=yes`: usa el `known_hosts` que creó el instalador.
Las pruebas simuladas (`Test-ErpHealth.ps1`) cubren 17 escenarios de este modo; la ruta SSH real contra una VM Hyper-V sigue pendiente de validar en el servidor.
