# Instalación en Windows Server (Hyper-V + VM Linux + Docker Compose) — P-04

Decisión del propietario (2026-11-12): **plataforma principal = Windows Server**; alternativa = Windows 11. El ERP sigue ejecutándose con
el **mismo** `docker-compose.yml`. Este documento cubre la ruta Windows Server; la ruta Windows 11 (Docker Desktop + WSL2) sigue en
[`instalacion-servidor.md`](instalacion-servidor.md) §3 con `install-windows.ps1`.

Estado: **implementado y probado todo lo que se puede probar sin el servidor físico** (ver §9). La instalación definitiva en el servidor
real está **pendiente** y necesita datos del propietario (§10). No se tocó `TheBuryProjectDb` ni ninguna instalación real.

## 1. Qué es común y qué cambia entre plataformas

| Pieza | Windows Server (Hyper-V + VM Ubuntu) | Windows 11 (Docker Desktop + WSL2) |
|---|---|---|
| Stack (`docker-compose.yml`: db → db-init → migrate → app → caddy) | **igual** | **igual** |
| Instalador del ERP (`install.sh`: `.env` aleatorio, imagen, deploy, CA de Caddy) | **igual**, corre dentro de la VM | **igual**, corre dentro de WSL |
| Deploy / rollback (`deploy.sh`, `rollback.sh`), backup/restore (`scripts/backup/`) | **igual** | **igual** |
| Instalador de la plataforma | `install-windows-server.ps1` (crea la VM) + `server-linux-bootstrap.sh` (Docker Engine) | `install-windows.ps1` |
| Docker | Docker Engine nativo en Linux (servicio systemd) | Docker Desktop (depende de la **sesión** de usuario) |
| Arranque tras un corte de luz | La VM arranca con el host (`AutomaticStartAction=Start`); systemd arranca Docker; las políticas `restart: unless-stopped` levantan los contenedores. **No requiere iniciar sesión.** | Requiere inicio de sesión automático de Windows (contraseña guardada) para que arranque Docker Desktop |
| Programación de backups | `cron` dentro de la VM (`/etc/cron.d/bury-backup`, `install-schedule.sh`) | Tareas programadas de Windows que llaman a `wsl.exe` |
| Firewall del puerto 443 | `bury-firewall` (cadena `DOCKER-USER`) + `ufw` para SSH, dentro de la VM | Regla de Windows Defender Firewall |
| Cierre ordenado de SQL Server | `entrypoint: sqlservr` + `stop_grace_period: 60s` (compose, **común a ambas plataformas**, ver §2) + `shutdown-timeout: 60` (dockerd) + Integration Services de Hyper-V | Compose común; el apagado de Windows depende de Docker Desktop |
| Chequeo de salud | `Check-ErpHealth.ps1 -VmHost <ip>` (SSH, solo lectura) | `Check-ErpHealth.ps1` |
| Pre-chequeo | `install-windows-server.ps1 -PreflightOnly` | `preflight-windows.ps1` |

**Por qué una VM Linux y no Docker Desktop en Server:** Docker Desktop no está soportado en Windows Server, y los contenedores
Windows no sirven (la imagen del ERP y SQL Server son Linux). Hyper-V + Ubuntu es el entorno que ya valida CI y tiene un modelo de
arranque, firewall y programación más robusto que un Windows con sesión interactiva. Windows 11 Pro también puede usar esta ruta
(Hyper-V está incluido), pero la alternativa documentada para Windows 11 sigue siendo Docker Desktop + WSL2.

## 2. Arquitectura

```
Clientes LAN / Tailscale ──► https://tbp (IP de la VM, conmutador externo de Hyper-V)
                               │
        Windows Server (host)  │  Hyper-V  ─ VM "TheBury-ERP" (Ubuntu 24.04, Gen2, sin checkpoints)
        solo administra la VM  │     ├─ ufw: SSH(22) solo desde -AdminFrom (tailnet o equipos /32, nunca la LAN completa) + el host Hyper-V
                               │     ├─ bury-firewall: 443 (publicado por Docker) solo desde -AllowFrom
                               │     ├─ Docker Engine ─ db (SQL Server 2022, volumen bury-sqldata)
                               │     │                 ├ migrate / db-init (one-shot)
                               │     │                 ├ app (:8080 interno)
                               │     │                 └ caddy (443, TLS interno)
                               │     ├─ cron: backups (full diario, log cada 15 min, restore-test semanal)
                               │     └─ (opcional) tailscaled
                               └─ el host NO publica el ERP; el firewall de Windows no filtra el tráfico de la VM
```

Puntos de diseño que importan:

- **Conmutador externo**: la VM obtiene una IP de la LAN. El Firewall de Windows **no** filtra ese tráfico; por eso el filtrado vive
  dentro de la VM. Solo 443 (ERP) y 22 (SSH, solo desde `-AdminFrom`) quedan accesibles. `1433` y `8080` no se publican (igual que antes).
- **`bury-firewall` en `DOCKER-USER`**: `ufw` no filtra los puertos que publica Docker (Docker inserta sus reglas antes). La regla usa
  `conntrack --ctstate DNAT --ctorigdstport 443`: solo afecta a las conexiones **entrantes** redirigidas a un contenedor (si no llevara
  `--ctstate DNAT`, descartaría también el HTTPS saliente de los contenedores; ese error se encontró y corrigió en las pruebas, §9).
- **Sin checkpoints** (`CheckpointType=Disabled`): revertir un checkpoint de una VM con SQL Server puede corromper la base.
- **Apagado ordenado**: `AutomaticStopAction=ShutDown` pide al invitado que apague; dockerd espera hasta 60 s a SQL Server.
- **Hallazgo corregido en el compose (afecta también a Windows 11/Linux)**: el `ENTRYPOINT` de la imagen de SQL Server (`launch_sqlservr.sh`, un bash como PID 1)
  **no reenvía SIGTERM**. Medido: `docker stop` esperaba el plazo completo y terminaba matando a `sqlservr` con SIGKILL (cierre no limpio en cada parada,
  reinicio del host o recreación del contenedor). Con `entrypoint: ["/opt/mssql/bin/sqlservr"]` el stop dura <1 s y el log muestra
  *«SQL Server is terminating in response to a 'stop' request»*. Efecto operativo: **la primera vez que se haga `docker compose up -d` con esta versión
  se recrea el contenedor `db` (unos segundos de corte; los volúmenes y los datos no se tocan)**. Se omiten `permissions_check.sh`/`init_custom_setup.sh`
  (solo verifican permisos y hooks opcionales que el ERP no usa); el usuario sigue siendo `mssql`.
- **Memoria estática** (8 GB por defecto: `db` 3 GB + `app` 2 GB + build/migrate): SQL Server no debe competir con Dynamic Memory.

## 3. Requisitos del host

- Windows Server 2022/2025 (o Windows 11 Pro/Enterprise) con el rol **Hyper-V** y herramientas de administración:
  `Install-WindowsFeature Hyper-V -IncludeManagementTools -Restart` (Server) o `Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All` (11).
- RAM: VM (8 GB) + 4 GB para el host. Disco: 80 GB para la VM + 10 GB; SSD. **UPS** (ver riesgos P-04 de `propuesta-infraestructura-produccion.md` §7).
- Virtualización activada en el firmware; si el host fuera una VM, virtualización anidada.
- OpenSSH Client (`ssh.exe`, `ssh-keygen.exe`, `scp.exe`; incluido en Server 2022/2025), `git` si se usa `-RepoPath`, `qemu-img` si la imagen no es VHDX
  (o descargar la imagen y convertirla a `.vhdx` en otro equipo).
- Un conmutador virtual **externo**. Se puede crear con el instalador (`-CreateExternalSwitch -ExternalAdapter <nombre>`); la red del host se corta unos segundos.
- IP fija reservada para la VM y su gateway (o reserva DHCP en el router).

## 4. Procedimiento

PowerShell **como Administrador** en el host, desde el repositorio:

```powershell
# 1. Pre-chequeo (solo lectura; no instala nada)
.\scripts\install\install-windows-server.ps1 -PreflightOnly -DownloadImage -RepoUrl https://<repo>.git `
    -VmIp 192.168.1.50/24 -Gateway 192.168.1.1 -SwitchName TheBury-External -AdminEmail soporte@ejemplo.com

# 2. Ver exactamente qué haría (no modifica nada)
.\scripts\install\install-windows-server.ps1 -DryRun <mismos parámetros>

# 3. Instalar
.\scripts\install\install-windows-server.ps1 -DownloadImage -AdminEmail soporte@ejemplo.com `
    -VmIp 192.168.1.50/24 -Gateway 192.168.1.1 -SwitchName TheBury-External `
    -RepoPath D:\git\theburyproject1 -AllowFrom 192.168.1.0/24,100.64.0.0/10 -AdminFrom 100.64.0.0/10 -Tailscale
```

Parámetros principales (todos en `Get-Help .\scripts\install\install-windows-server.ps1 -Full`): `-VmName`, `-CpuCount`, `-MemoryGB`, `-DiskGB`,
`-BaseImage` (`.vhdx`/`.img`/`.qcow2`) o `-DownloadImage` (verifica SHA256 contra `SHA256SUMS` de Canonical), `-VmIp/-Gateway/-Dns` o `-UseDhcp`,
`-RepoUrl` o `-RepoPath` (+ `-Ref`), `-Domain`, `-AdminEmail`, `-License`, `-AllowFrom`, `-AdminFrom`, `-Tailscale`, `-TrustCaHere`, `-SkipInstall`.

Qué hace, en orden (todo idempotente; si algo falla se puede volver a ejecutar):

1. Preflight (§6). 2. Clave SSH ed25519 dedicada en `C:\ProgramData\TheBury\server` (ACL solo Administradores/SYSTEM).
3. Imagen base → VHDX dinámico de `-DiskGB`. 4. ISO `cidata` de cloud-init (usuario `bury` solo con clave SSH, sin contraseña; hora; red fija).
5. VM Gen2 (Secure Boot con plantilla *UEFI CA*, memoria estática, sin checkpoints, arranque automático, apagado ordenado).
6. Por SSH: repositorio (clone o `git bundle` desde el host) → `server-linux-bootstrap.sh` (Docker Engine oficial, `shutdown-timeout`, `bury-firewall.service`, `ufw`,
   directorio de backups 700) → `install.sh --schedule` (`.env` con secretos aleatorios 0600, imagen con tag inmutable, `db-init → migrate → app`, Caddy, **cron de backups**).
7. Trae la CA pública de Caddy a `C:\ProgramData\TheBury\server\caddy-root-ca.crt`.

`install.sh` muestra **una sola vez** la contraseña inicial del administrador: guardarla y cambiarla al entrar. El `.env` (en la VM, `~/theburyproject1/.env`) es
el único lugar con los secretos: copiarlo al gestor de contraseñas (ver `secretos-produccion.md`).

## 5. Persistencia, arranque, reinicio, red y recuperación

| Evento | Qué ocurre | Cómo se comprobó |
|---|---|---|
| Datos de SQL Server | Volumen `bury-sqldata` dentro del VHDX de la VM; sobrevive a `down/up`, reinicio de Docker y de la VM | §9: fila centinela tras `down`/`up` y tras reinicio completo de la distro de prueba |
| Reinicio de Docker (`systemctl restart docker`) | `bury-firewall` se reaplica (`PartOf=docker.service`); contenedores vuelven por `unless-stopped` | §9 |
| Reinicio de la VM | systemd inicia `docker`, `cron` y `bury-firewall` antes que nada; los contenedores se levantan solos | §9 (reinicio de la distro con systemd) |
| Reinicio / corte de luz del host | Hyper-V arranca la VM 30 s después del host (`AutomaticStartDelay`), sin sesión de usuario | **No verificable sin Hyper-V real** (§10) |
| Apagado ordenado del host | `ShutDown` → Integration Services → dockerd espera hasta 60 s a SQL Server | Configuración verificada; apagado real **no probado** |
| Corte de luz brusco | SQL Server hace recuperación de cola al arrancar; sin UPS el riesgo es del propietario (aceptado). Mitigación: backup de log cada 15 min + restore-test semanal | Recuperación con `restore-test.sh`/DR: `backup-restore.md` §10 |
| Contraseña/clave del host perdida | El ERP no depende de ninguna sesión interactiva | — |

**Red:** IP fija por cloud-init (`network-config`), DNS configurable. Clientes: `hosts` con `<ip-vm> tbp` + instalar la CA (`red-privada-lan-vpn.md`).
Verificación desde un cliente: `Test-NetConnection <ip-vm> -Port 443` → True; puertos `1433`, `8080` → False; desde una red **no** incluida en `-AllowFrom`, 443 no responde.

**Recuperación del servidor (resumen):** (1) la VM se reconstruye con este mismo instalador sobre un host nuevo; (2) restaurar el `.env` y el paquete de
recuperación desde el gestor de contraseñas; (3) traer el último backup desde la copia externa (`fetch-offsite.sh`) y restaurar con `restore-sql.sh`/`restore-files.sh`.
El procedimiento completo y medido sigue siendo `backup-restore.md` §10; ver el riesgo de §7 (backups dentro del mismo VHDX).

## 6. Pre-chequeo (P-07 para Windows Server)

`install-windows-server.ps1 -PreflightOnly` es solo lectura y verifica: SO (Server/Pro; Home = FALLA), administrador, módulo Hyper-V y servicio `vmms`, RAM (VM+4 GB),
CPU, disco de destino, virtualización, `ssh/ssh-keygen/scp`, `git`/`qemu-img` según corresponda, imagen base, conmutador y adaptador, parámetros de red y redes permitidas.
Devuelve exit 1 si hay FALLA. `preflight-windows.ps1` (P-07 original) sigue siendo el chequeo para la ruta Windows 11.

## 7. Dependencias de P-05, P-06, P-07, P-08 (integradas, **no cerradas**)

| Item | Qué quedó integrado en esta ruta | Qué sigue abierto (necesita al propietario/servidor) |
|---|---|---|
| **P-05 red/Tailscale** | `-AllowFrom` admite `100.64.0.0/10`; `-Tailscale` instala el paquete y habilita `tailscaled` **sin autenticar**; `Check-ErpHealth.ps1 -VmHost` verifica `tailscaled`, estado y vencimiento de clave. Tailscale va **dentro de la VM** (nodo propio), el host no actúa de subnet router | `sudo tailscale up` con la cuenta del propietario, ACL `tag:erp → :443`, deshabilitar expiración de clave del nodo, probar desde un cliente remoto real |
| **P-06 backups/DR** | `cron` nativo en la VM (`install.sh --schedule`); mismos scripts, mismo `monitor-status`; `Check-ErpHealth` los lee por SSH; `BACKUP_DIR` en 700 | **Riesgo: los backups locales viven en el mismo VHDX que los datos** (un fallo del disco del host pierde ambos). Falta: destino SFTP real en otro sitio + `known_hosts`, custodia de la clave `crypt` y passphrase **fuera** del servidor, y (propuesta, no implementada) copia periódica del directorio de backups al host o a otro disco (`E:`) vía `scp` con clave de solo lectura |
| **P-07 preflight** | `-PreflightOnly` (§6) | Ejecutarlo en el servidor real; UPS y «Restore on AC power loss» en BIOS (no verificables por software) |
| **P-08 salud** | `Check-ErpHealth.ps1 -VmHost <ip> [-VmName] [-VmKey] [-VmKnownHosts]`: estado/latido/autoarranque/checkpoints de la VM (Hyper-V), `docker`, `cron`, `bury-firewall` activos y habilitados, `cron.d` instalado, disco de la VM, HTTPS `/health/{live,ready}`, backups, Tailscale | Programar su ejecución periódica y un **observador externo** (si cae todo el servidor, nadie ejecuta el script): decisión de alertas del propietario («ninguna por ahora») |

## 8. Operación

- Actualizar el ERP: por SSH en la VM, `cd ~/theburyproject1 && git pull && bash scripts/deploy/deploy.sh ...` (ver `deploy-rollback.md`; mismo flujo que Linux).
- Acceso administrativo: `ssh -i C:\ProgramData\TheBury\server\bury_vm_ed25519 bury@<ip-vm>` (solo desde `-AdminFrom`).
- Primera actualización con este compose: `docker compose up -d` recrea `db` (entrypoint nuevo, §2). Hacerlo en una ventana de mantenimiento corta; antes, `backup.sh all`.
- Parches del SO invitado: `sudo apt-get update && sudo apt-get upgrade` en una ventana de mantenimiento (reiniciar la VM, no el host). Parches del host Windows: programarlos fuera de horario; la VM se apaga ordenadamente.
- Estado: `.\scripts\install\Check-ErpHealth.ps1 -VmHost <ip> -VmKey C:\ProgramData\TheBury\server\bury_vm_ed25519 -VmKnownHosts C:\ProgramData\TheBury\server\known_hosts`.
- Quitar todo: detener y eliminar la VM y su VHDX **a mano** (`Stop-VM; Remove-VM; Remove-Item <vhdx>`); el instalador no borra nada. **No eliminar antes de copiar los backups.**

## 9. Qué se probó (2026-11-12, sin Hyper-V, datos sintéticos, aislado)

Entorno de prueba: distro WSL2 **desechable** `bury-p04-test` (copia de Ubuntu 24.04 con systemd, descartada al terminar) con Docker Engine propio y
volúmenes nuevos; base `TheBuryProjectDb` real y Docker Desktop no intervinieron.

| Prueba | Resultado |
|---|---|
| `test-install.sh` (CI): bootstrap `--dry-run`, validaciones (inyección en CIDR, `/0`, octetos, listas vacías, opción sin valor), `bury-firewall` con iptables simulado (cadena nueva enganchada antes de retirar la vieja, reemplazo atómico) | 75/75 |
| `Test-ErpHealth.ps1`: 27 escenarios previos + 18 de modo VM (SSH caído, VM apagada, sin latido/latido ilegible/autoarranque/checkpoints, cron/firewall caídos, disco, Docker, backups, Tailscale, `-VmHost` malicioso) | todos pasan |
| `Test-WindowsServerInstall.ps1`: CIDR/red, cloud-init (inyección rechazada; YAML válido con PyYAML), **ISO `cidata` real** generado con IMAPI2 y montado en Linux (`blkid`: `LABEL="cidata"`, `user-data`/`meta-data`/`network-config` legibles), `-DryRun`/`-PreflightOnly` sin efectos | todos pasan |
| `server-linux-bootstrap.sh` **real** (2 ejecuciones: idempotente): Docker Engine 29.x + Compose v5, `shutdown-timeout`, `bury-firewall.service` y drop-in `ExecStartPost` | OK |
| **Instalación completa** con `install.sh` en la distro de prueba: build de la imagen, `db-init`, `migrate` (**137 migraciones**), `app`, Caddy, health gate `live`/`ready` = 200, smoke de login | OK |
| **Persistencia**: fila centinela en una base aparte sobrevive a `down`/`up`, a `docker stop`, a `systemctl restart docker` y a una **terminación abrupta** de todo el sistema (equivalente a un corte de luz del invitado) | OK; SQL Server completó la recuperación sola |
| **Reinicio**: tras terminar la distro y volver a arrancarla (systemd), `docker`, `cron` y `bury-firewall` habilitados; 3 contenedores arriba y `healthy` sin intervención; `/health/ready` = 200; cadena `DOCKER-USER` reaplicada | OK |
| **Apagado ordenado de SQL Server**: antes del cambio `docker stop db` = 60 s (SIGKILL); después = 0 s con log de parada limpia | corregido en el compose |
| **Firewall** (contadores de iptables): 443 desde una red no permitida → 5 paquetes en `DROP`; al agregarla → 5 en `RETURN`, 0 en `DROP`; al quitarla → vuelve a `DROP`. Tráfico saliente de contenedores (HTTPS a NuGet) no afectado | OK |
| **Backups**: `install-schedule.sh --install` (`/etc/cron.d/bury-backup`), `backup.sh all` (exit 0, `monitor-status/*.json`) y `restore-test.sh` (DBCC CHECKDB OK, 137 migraciones, 133 tablas restauradas) | OK |
| **Hallazgo y corrección 1**: la primera versión de la regla `DOCKER-USER` bloqueaba el HTTPS **saliente** de los contenedores (restore de NuGet colgado). Corregido con `--ctstate DNAT`; cubierto por test de CI | corregido |
| **Hallazgo y corrección 2**: ver `entrypoint` de SQL Server arriba (cierre no limpio) | corregido |
| Revisión estática independiente (2 revisores Sonnet): 19 + 21 observaciones; aplicadas las de impacto real (SIDs en `icacls`, idempotencia del VHDX, `git` antes del clone, validación de `-Ref`/CIDR, reemplazo atómico del firewall, bloqueo SSH por `ufw`, lock de apt, latido por Id, etc.) | aplicadas |

Entorno: toda la parte «dentro de la VM» se ejecutó en una **distro WSL2 desechable** con systemd, Docker Engine propio, volúmenes nuevos y datos sintéticos (`admin` generado,
correo `qa-sintetico@example.test`), que se eliminó al terminar. Es el mismo código (`server-linux-bootstrap.sh` → `install.sh` → compose) que correrá en la VM Ubuntu.

### 9.1 Límites conocidos (no bloquean, quedan documentados)

- `-UseDhcp` es de mejor esfuerzo: descubre la IP por KVP de Hyper-V, que necesita `linux-cloud-tools-virtual` (cloud-init lo instala, pero requiere Internet en el primer arranque); la IP fija es lo recomendado.
- Windows Server **Core** sin Desktop Experience no trae IMAPI2 (el preflight lo marca FALLA); usar Desktop Experience.
- `--automapper-key` (licencia comercial) viaja por línea de comandos y es visible en `ps` dentro de la VM mientras corre `install.sh`.
- La VM no tiene contraseña de login (solo clave SSH, cuenta bloqueada): si su red queda inutilizable no hay login por la consola de Hyper-V; se repara montando el VHDX en otra VM o recreando la VM sobre el mismo disco.
- Docker se instala desde el repositorio oficial de Docker sin verificar el fingerprint de la clave (igual que su documentación); no se admite `docker.io`/snap (el script se detiene).

## 10. Qué **no** se pudo validar y qué se necesita del propietario

No validado por falta de Hyper-V/Windows Server (no hay rol Hyper-V ni elevación en el equipo de desarrollo): creación real de la VM (`New-VM`, Secure Boot con la plantilla UEFI CA,
`Set-VM` de autoarranque/apagado), arranque de cloud-init desde el ISO en Hyper-V, nombres reales de NIC (`hv_netvsc`), Integration Services (apagado ordenado, latido),
reinicio/corte del host con la VM sola, conmutador externo y red LAN real, descarga/conversión de la imagen (`qemu-img`), `New-SeedIso` en Server Core.
El código para todo esto está escrito y revisado, pero **solo se ejecutó su lógica pura y la parte Linux**.

Para la instalación definitiva se necesita:

1. **Servidor**: Windows Server (versión), rol Hyper-V instalado, acceso administrador, RAM/disco finales, UPS y opción de BIOS «Restore on AC Power Loss = On».
2. **Red**: IP fija para la VM (+ gateway/DNS), adaptador de red para el conmutador externo, redes que pueden usar el ERP (`-AllowFrom`) y administrar por SSH (`-AdminFrom`), nombre de host (`tbp`).
3. **Repositorio**: URL accesible desde la VM, o usar `-RepoPath` (bundle) desde un checkout actualizado del host.
4. **Credenciales**: correo del administrador inicial; licencia comercial de AutoMapper (si `commercial`). *No se solicitan ni se tocan credenciales reales hasta entonces.*
5. **P-05**: cuenta Tailscale, ACL, expiración de clave deshabilitada. **P-06**: servidor SFTP en otro sitio (host/usuario/`known_hosts`), custodia de la clave `crypt`/passphrase fuera del servidor, decisión de la copia de backups fuera del VHDX. **P-08**: destino de alertas y observador externo.
6. **Base real**: migrar `TheBuryProjectDb` solo con backup verificado y autorización expresa (fuera del alcance de P-04).

Tras instalar, el criterio de aceptación es: `Check-ErpHealth -VmHost` sin FALLA, reinicio del host sin tocar nada (el ERP vuelve solo), apagado ordenado
sin recuperación de SQL Server en el log, 443 inaccesible desde una red fuera de `-AllowFrom`, y el drill de `backup-restore.md` §10.
