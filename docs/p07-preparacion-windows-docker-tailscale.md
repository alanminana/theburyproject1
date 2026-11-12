# P-07 — Preparación del despliegue en Windows + Docker Desktop + Tailscale

Fecha: 2026-11-07. Estado: **preparación; no se instaló ni se modificó ninguna infraestructura real** ni se tocó la base real.
Nota 2026-11-12: la plataforma principal pasó a ser Windows Server (Hyper-V + VM Linux, ver `instalacion-windows-server.md`); este documento aplica a la alternativa Windows 11.
Decisiones del propietario ya tomadas (2026-11-07, `docs/propuesta-infraestructura-produccion.md` §7): host **Windows 11 + Docker Desktop (WSL2)**,
acceso remoto **Tailscale**, copia externa **SFTP**, sin alertas por ahora.

Lo que ya existe y **no se rediseña**: `scripts/install/install-windows.ps1`, `docs/instalacion-servidor.md`, `docs/red-privada-lan-vpn.md`,
`docs/backup-restore.md`, `docs/offsite-monitoring-activacion.md`, `docs/secretos-produccion.md`, `docs/deploy-rollback.md`.

## 1. Qué se preparó ahora

| Entregable | Para qué |
|---|---|
| `scripts/install/preflight-windows.ps1` | Chequeo **solo lectura** del equipo (Windows, RAM, disco, WSL2, Docker, Tailscale, puerto 443, firewall, energía, inicio de sesión automático). Dice qué falta antes de correr el instalador. No instala ni cambia nada. |
| Esta guía (§3-§6) | Lo que falta específicamente por usar **Tailscale**: rango de origen, IP de publicación, nombre, certificado, ACL, arranque y pruebas. |

Resultado del preflight en la PC de desarrollo (2026-11-07), a modo de ejemplo del informe: Windows 11 y WSL2/Ubuntu v2 correctos, RAM y disco suficientes;
**Docker daemon apagado**, **Tailscale no instalado**, sin `.wslconfig`, sin inicio de sesión automático, y 2 reglas Allow entrantes de Docker preexistentes.
Esa máquina **no** es el servidor definitivo; el preflight debe correrse en el equipo destino.

## 2. Qué NO se puede preparar sin el equipo real (bloqueos)

1. Instalar/verificar Docker Desktop, WSL2 y Tailscale **en el servidor**; que el nodo exista en la tailnet del propietario.
2. IP reservada de la LAN y hostname/DNS definitivo.
3. Servidor SFTP en otro sitio y su host key (`known_hosts_file`).
4. Pruebas desde un cliente remoto real por Tailscale (hoy solo hay pruebas contra una pila local; ver `red-privada-lan-vpn.md` §6 punto 5).
5. UPS y opción de BIOS *Restore on AC Power Loss* (no verificables por software).

## 3. Diseño de red con Tailscale (qué decidir y cómo)

Tailscale asigna a cada nodo una IP del rango CGNAT **`100.64.0.0/10`** (IPv4) y un nombre MagicDNS `host.<tailnet>.ts.net`. No requiere abrir puertos en el router ni IP pública.

| Tema | Recomendación | Notas / riesgo |
|---|---|---|
| Origen permitido en el firewall | `install-windows.ps1 -AllowFrom LocalSubnet,100.64.0.0/10` | El ejemplo del instalador usa `10.8.0.0/24` (WireGuard); con Tailscale es `100.64.0.0/10`. |
| IP de publicación (`ERP_BIND_ADDRESS`) | **Opción 1 (simple):** `0.0.0.0` + firewall (`-AllowFrom`) + regla Block a Internet (`red-privada-lan-vpn.md` §6 punto 3). **Opción 2 (más estricta):** la IP `100.x` del servidor, y los equipos del local entran también por Tailscale. | Con la Opción 2, si Tailscale aún no tiene IP cuando arranca Docker, **Caddy no arranca y no se reintenta** (trampa verificada, `red-privada-lan-vpn.md` §6). Mitigación: servicio Tailscale en *Automatic* y `docker compose up -d` después de que la IP exista. Recomendada: Opción 1 al inicio. |
| Arranque antes del inicio de sesión | Tailscale corre como **servicio de Windows** (`Tailscale`, inicio Automático) y conecta sin sesión de usuario si se activa *Run unattended* en la app. El preflight solo verifica servicio Running/Automático e IP actual; *Run unattended* y la conexión tras reiniciar **sin** iniciar sesión se prueban a mano (§5, Reinicio). | Docker Desktop y los backups sí dependen de la sesión de usuario: seguir `instalacion-servidor.md` §3.3 (inicio de sesión automático con cuenta dedicada). |
| Nombre del ERP | **Opción A:** `tbp` en `hosts` de cada cliente (`red-privada-lan-vpn.md` §1) con la IP `100.x` del servidor. **Opción B:** usar el nombre MagicDNS como `ERP_DOMAIN` (sin editar `hosts`). | En ambas, Caddy usa `tls internal`: **cada cliente debe instalar la CA raíz** de Caddy (`red-privada-lan-vpn.md` §2). Un certificado público de `tailscale cert` evitaría instalar la CA, pero publica el nombre de la máquina en registros de transparencia de certificados y exige habilitar HTTPS en la tailnet: es una **decisión del propietario**, no se asumió. |
| Control de acceso (ACL) | Etiquetar el servidor (`tag:erp`) y permitir solo `usuarios del negocio → tag:erp:443`. Nada de `22/3389/1433/8080` hacia el servidor salvo administradores. | Se configura en la consola de administración de Tailscale: no es verificable desde el repositorio. |
| Expiración de clave del nodo | **Deshabilitarla** para el servidor. | Si expira, el servidor sale de la tailnet y el acceso remoto se corta sin aviso (y no hay alertas por decisión del propietario). El preflight lo informa. |
| Funciones a **no** usar | *Funnel* (publica el servicio en Internet: contradice el requisito de no exponer el ERP). *Subnet router* y *exit node* no exponen el ERP por sí mismos, pero se prohíben en este servidor para no ampliar su alcance de red. | Decisión de alcance, no una exposición pública equivalente. |
| Cuentas | Una cuenta/identidad Tailscale por persona; quitar el dispositivo cuando alguien deja de acceder. | La baja de un usuario del ERP no revoca su acceso a la red: son dos controles distintos. |

## 4. Secuencia propuesta (cuando el propietario autorice instalar)

Todo en el **servidor destino**, en este orden. Los pasos 1-3 son los únicos que cambian el equipo antes del instalador del ERP.

1. Instalar WSL2/Ubuntu y Docker Desktop (backend WSL2, arranque con la sesión) según `instalacion-servidor.md` §3.1; `.wslconfig` con `memory=6GB`.
2. Instalar Tailscale, iniciar sesión en la tailnet, marcar *Run unattended*, deshabilitar la expiración de clave del nodo y aplicar la ACL.
3. Ejecutar `preflight-windows.ps1`: no debe haber **FALLA**; revisar cada **ATENCION**.
4. `install-windows.ps1 -DryRun -AdminEmail <correo> -AllowFrom LocalSubnet,100.64.0.0/10` y revisar lo que haría; luego sin `-DryRun`. Base **nueva y vacía** (sin datos reales).
5. Fijar `ERP_BIND_ADDRESS` (Opción 1 o 2 de §3), extraer e instalar la CA raíz en los clientes, `hosts`/MagicDNS.
6. Probar (checklist §5). 7. Offsite SFTP + scheduler (`offsite-monitoring-activacion.md`). 8. Drill de recuperación (`backup-restore.md` §10).
9. Recién después, planificar la migración de la base real con backup verificado y **autorización expresa**.

## 5. Checklist de pruebas de aceptación (con Tailscale)

Desde un cliente **en la tailnet** (con la CA instalada):

- `Test-NetConnection <IP-100.x-del-servidor> -Port 443` → `True`; `-Port 1433` y `-Port 8080` → `False`.
- `curl https://tbp/health/ready` (o el nombre MagicDNS) → `200`, sin advertencia de certificado.
- Login, cerrar sesión y volver a entrar; cookies `Secure` (el login funciona detrás de HTTPS, no por HTTP directo).

Desde un cliente **fuera de la tailnet** (datos móviles, Tailscale apagado):

- La IP LAN y la `100.x` del servidor **no** responden en 443. Si responde, el servicio está expuesto: revisar `ERP_BIND_ADDRESS`, firewall y router antes de seguir.

Reinicio:

- Reiniciar el equipo sin tocarlo: vuelve el inicio de sesión automático, Docker, los tres contenedores y Tailscale; `https://tbp` abre. Probar también un reinicio **durante** `backup.sh all` (deja `app` detenida ~15 s: `instalacion-servidor.md` §3.3).

## 6. Riesgos y lo que sigue abierto

| # | Riesgo | Estado |
|---|---|---|
| 1 | Sin UPS: un corte de luz puede dejar SQL Server en recuperación | Aceptado por el propietario; mitigado por backups cada 15 min |
| 2 | Sin alertas: un backup o una expiración de clave de Tailscale pasan inadvertidos | Aceptado; revisar `monitor-status/` a mano |
| 3 | Clave de cifrado y recovery package en el mismo disco del servidor | Aceptado; custodiar una copia fuera (gestor de claves) |
| 4 | Reglas Allow de `com.docker.backend.exe` amplias pueden anular el filtro de `-AllowFrom` | Detectado por el preflight; la regla Block a Internet **no fue probada** con un cliente remoto |
| 5 | Pruebas remotas reales por Tailscale | **Pendientes** (requieren el servidor) |
| 6 | Certificado: CA interna en cada cliente vs certificado público de Tailscale | **Decisión del propietario** |

## 7. Decisiones que necesito del propietario

1. `ERP_BIND_ADDRESS`: ¿`0.0.0.0` + firewall (recomendada al inicio) o solo la IP de Tailscale?
2. Nombre del ERP: ¿`tbp` en `hosts` (más simple, ya documentado) o el nombre MagicDNS?
3. Certificado: ¿CA interna instalada en cada equipo (hoy) o certificado público de `tailscale cert`?
4. ¿Quiénes acceden por Tailscale y quién administra la tailnet y sus ACL?
