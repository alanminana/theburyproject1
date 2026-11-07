# Propuesta de infraestructura y recuperación para producción (P-04, P-05, P-06)

Fecha: 2026-11-07. Estado: **propuesta para decisión del propietario; no se desplegó nada ni se tocaron datos reales.**
Base técnica ya existente (no se rediseña): `docker-compose.yml` (db → db-init → migrate → app → caddy), `Caddyfile` (`tls internal`),
`scripts/install/`, `scripts/backup/`, `scripts/deploy/`, `docs/instalacion-servidor.md`, `docs/red-privada-lan-vpn.md`,
`docs/backup-restore.md`, `docs/offsite-monitoring-activacion.md`, `docs/monitoreo-alertas.md`.

## 1. Servidor (P-04)

El diseño es una sola imagen Docker; solo cambia el anfitrión. Opciones:

| Opción | Cómo | A favor | En contra |
|---|---|---|---|
| **A. Mini-PC/PC propia en el local con Windows 11 + Docker Desktop (WSL2)** | `install-windows.ps1` ya configura firewall, backups, arranque y energía | Camino principal documentado y probado; datos en el local; sin cuota mensual | Depende de luz/internet del local; Docker Desktop en Windows es menos robusto que Linux nativo (reinicios por actualizaciones, WSL2); permisos de backup no reproducen Linux |
| **B. Misma máquina con Ubuntu Server** (recomendada si se compra/dedica equipo) | `install.sh` | Es el entorno que valida CI; cron nativo; menos sorpresas de Windows Update | Alguien debe mantener un Linux (parches de seguridad) |
| **C. VPS en la nube (Linux)** | Mismo instalador | Energía/red resueltas; offsite casi gratis (otra región) | Cuota mensual; el ERP queda en Internet salvo que se use VPN; datos fuera del local; requiere decidir exposición |

**Recomendación:** B si hay equipo dedicado; A si se usa la PC actual. Evitar C salvo que el local no tenga conectividad estable.
Mínimos (de `instalacion-servidor.md`): 8 GB RAM, 20 GB libres, **UPS** (SQL Server y un corte de energía no se llevan) y SSD.

## 2. Red segura (P-05)

- Acceso interno: LAN → `https://tbp` → Caddy (443) → `app:8080` → `db:1433`. Solo 443 publicado; 1433 y 8080 no (ya así en `docker-compose.yml`).
- TLS: CA interna de Caddy (`tls internal`); hay que instalar la CA raíz en cada equipo cliente (script de extracción ya existe). Alternativa si hay dominio propio: certificado público por DNS-01, sin abrir puertos.
- Acceso remoto: **VPN, nunca port-forwarding al ERP.** Recomendado WireGuard o Tailscale (más simple; funciona detrás de CGNAT, que está **sin verificar**). Rutas VPN solo hacia la IP del servidor.
- Firewall del host: permitir 443 desde la LAN/VPN y administración (SSH/RDP) solo desde la LAN/VPN.
- Pendientes para cerrar (requieren el servidor y el router real): IP fija/reservada, hostname/DNS o `hosts`, firewall, comprobar que el router no expone el ERP, y probar hostname/cert/login/cookies desde LAN y desde VPN.

## 3. SQL Server, Caddy y Docker

- SQL Server 2022 **Express** en contenedor, volumen `bury-sqldata`. Límite de 10 GB por base, sin compresión de backup. Alcanza para una tienda; revisar el tamaño trimestralmente (base de pruebas actual ≈ 45 MB).
- Recovery model FULL con log cada 15 min (necesario para no llenar el `.ldf`).
- Caddy: igual que hoy. Pendiente: HTTP→HTTPS y bindings finales según el host.
- Imágenes con tag inmutable; `deploy.sh`/`rollback.sh` ya existen. Las migraciones las aplica el servicio `migrate` con backup pre-deploy.

## 4. Backups y recuperación (P-06)

| Capa | Frecuencia | Retención | Estado |
|---|---|---|---|
| SQL full | diario 02:30 | local 14 d / remoto 30 d | scripts listos; scheduler **no instalado** |
| SQL log | cada 15 min | ídem | ídem |
| Archivos (uploads, App_Data, claves) | diario | ídem | ídem |
| Prueba de restore | semanal automática | — | lista |
| **Copia externa (rclone)** | tras cada backup | 30 d | **sin destino real** |

**RPO ≈ 15 min (SQL), ≤ 24 h (archivos subidos).** **RTO estimado:** 1–3 h en un servidor nuevo con el paquete de recuperación y la copia externa (drill completo documentado en `backup-restore.md` §10; hay que repetirlo contra el destino real para medirlo).

**Destino offsite — recomendación:** Backblaze B2 o S3 compatible con cifrado `crypt` de rclone (hashes remotos, costo bajo, versionado). SFTP solo si ya hay otro servidor en otro sitio. La clave `crypt` y el *recovery package* (`.enc` + passphrase) deben guardarse **por separado y fuera del servidor** (gestor de contraseñas + soporte offline); hoy están en el mismo host.

Otros pendientes: alerta activa ante fallo (webhook + observador externo tipo Uptime Kuma en otro sitio, porque si todo está en el local un corte no avisa), `--install` del cron nunca ejecutado, retención mensual/anual si se quiere histórico largo, `.env`/`backup-offsite.env` fuera del backup (copia manual).

## 5. Orden de ejecución sugerido (cuando se apruebe)

1. Elegir host (A/B/C) y comprar/reservar. 2. Elegir proveedor offsite y crear credenciales con permiso solo sobre su prefijo.
3. Instalar con `install.sh`/`install-windows.ps1` en el host vacío (base nueva, sin datos reales). 4. Red: IP fija, hostname, CA en clientes, firewall, VPN.
5. Offsite + scheduler + monitor (checklist de `offsite-monitoring-activacion.md`). 6. Drill de recuperación completo en una segunda máquina o VM.
7. Recién después, decidir fecha de migración de la base real (con backup verificado y autorización expresa).

## 6. Decisiones que necesita el propietario

1. Host: A (Windows), B (Ubuntu) o C (VPS).
2. VPN: WireGuard propio o Tailscale; ¿quién accede de forma remota?
3. Proveedor offsite (B2 / S3 / SFTP) y quién custodia la clave `crypt` y el recovery package.
4. Destino de las alertas (correo/WhatsApp/webhook) y observador externo.
5. Retención histórica requerida (¿1 año de mensuales?) y presupuesto mensual aceptado.
6. ¿Hay UPS? ¿Cuál es la conectividad del local (IP pública/CGNAT, proveedor)?
