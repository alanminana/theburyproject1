# Instalación del servidor (Windows o Linux)

Guía para instalar TheBuryProject en un servidor propio, accesible solo por LAN/VPN. Es **una sola versión** para los dos sistemas: el ERP corre
en contenedores Docker (Linux) y solo cambia cómo se prepara la máquina anfitriona. Windows 10/11 con Docker Desktop (WSL2) es el camino
principal; Linux (Ubuntu Server) funciona con el mismo instalador.

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433 (SQL Server Express)
```

Solo se publica el puerto **443**. `app` y `db` no son alcanzables desde fuera. Detalle de red y certificados: [red-privada-lan-vpn.md](red-privada-lan-vpn.md).

## 1. Qué automatiza el instalador

| Script | Dónde corre | Qué hace |
|---|---|---|
| `scripts/install/install.sh` | Linux o WSL2 | Genera `.env` con secretos aleatorios, construye la imagen con tag inmutable, despliega (`deploy.sh`) y extrae la CA de Caddy |
| `scripts/install/install-windows.ps1` | PowerShell **como administrador** | Verifica WSL2/Docker, ejecuta `install.sh` dentro de WSL, y configura firewall, tareas de backup, arranque y energía |
| `scripts/install/test-install.sh` | CI / desarrollo | Prueba el generador de `.env` sin Docker (corre en CI) |

Todo es **idempotente**: se puede volver a ejecutar sin romper nada. Un `.env` existente nunca se pisa.

## 2. Requisitos

- 8 GB de RAM o más (SQL Server Express usa hasta 1 GB, más Docker y la app), 20 GB de disco libre como mínimo.
- IP fija para el servidor: reserva DHCP en el router.
- Salida a Internet del **servidor** (descargar imágenes de Docker y el código). El ERP en sí no necesita Internet.
- Licencia de AutoMapper: por defecto **Community** (gratuita, sin clave). Ver [despliegue-produccion.md](despliegue-produccion.md) §9.

## 3. Windows 10/11 (camino principal)

### 3.1 Preparar la máquina (una vez)

1. Instalar WSL2 con Ubuntu: en PowerShell como administrador, `wsl --install -d Ubuntu` y reiniciar. Crear el usuario de Ubuntu cuando lo pida.
2. Instalar **Docker Desktop** con el backend WSL2. En *Settings > General* activar **Start Docker Desktop when you sign in**.
   En *Settings > Resources > WSL integration* activar la integración con **Ubuntu**.
3. (Recomendado) Limitar la memoria de WSL2 creando `C:\Users\<usuario>\.wslconfig`:

   ```ini
   [wsl2]
   memory=6GB
   swap=2GB
   ```

   y aplicar con `wsl --shutdown`.
4. Clonar el repositorio **dentro de WSL** (no en `C:\` ni en `/mnt/c`: los permisos y los saltos de línea se rompen):

   ```powershell
   wsl -d Ubuntu -- git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1
   ```

### 3.2 Instalar

PowerShell **como administrador**, desde el repositorio (clonado en WSL, accesible como `\\wsl.localhost\Ubuntu\home\<usuario>\theburyproject1`):

```powershell
cd \\wsl.localhost\Ubuntu\home\<usuario>\theburyproject1\scripts\install
powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com -AllowFrom LocalSubnet,10.8.0.0/24
```

- Primero probar con `-DryRun`: muestra todo lo que haría sin modificar nada.
- `-AllowFrom` define desde dónde se acepta el 443: `LocalSubnet` (la LAN) y la subred de la VPN (ejemplo WireGuard `10.8.0.0/24`).
- Parámetros útiles: `-Domain tbp`, `-BackupDir /srv/bury-backups`, `-FullBackupAt 02:30`, `-HostsIp <IP>` (agrega `IP tbp` a `hosts`, para probar en el propio servidor), `-TrustCaHere` (instala la CA en este equipo).
- Al terminar, el script de instalación muestra **una sola vez** la contraseña inicial del administrador. Anotarla.

El script hace, en orden: verificación de WSL2/Docker → `install.sh` en WSL → regla de Firewall (443 TCP/UDP, solo desde `-AllowFrom`) → tareas programadas
(`TheBury Backup Full` diaria, `TheBury Backup Log` cada 15 min, `TheBury Restore Test` semanal, `TheBury Docker Start` al iniciar sesión) → energía (sin suspender ni hibernar con corriente, tapa cerrada sin efecto).

Para quitar tareas y regla de firewall: `install-windows.ps1 -Uninstall` (no toca datos, volúmenes ni backups).

### 3.3 Arranque automático tras un corte de luz

Docker Desktop corre dentro de la **sesión del usuario**, y las tareas de backup usan esa misma sesión. Para que todo vuelva solo tras un reinicio hay que habilitar el **inicio de sesión automático** de Windows:

1. `netplwiz` → desmarcar *Los usuarios deben escribir su nombre y contraseña*. (Guarda la contraseña en el equipo: usar una cuenta dedicada sin privilegios innecesarios y proteger físicamente el servidor.)
2. En el BIOS/UEFI, activar *Restore on AC Power Loss* (o equivalente) para que el equipo encienda solo al volver la luz.
3. Fijar el horario de reinicio de Windows Update fuera del horario de uso.

Los contenedores tienen `restart: unless-stopped`: vuelven con Docker. Probarlo reiniciando el equipo y abriendo `https://tbp` sin tocar nada.

## 4. Linux (Ubuntu Server)

```bash
# Docker Engine + plugin compose según la guía oficial de Docker; luego:
sudo usermod -aG docker $USER   # volver a iniciar sesión
sudo systemctl enable docker
git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1 && cd ~/theburyproject1
sudo mkdir -p /srv/bury-backups && sudo chown $USER: /srv/bury-backups && chmod 700 /srv/bury-backups
bash scripts/install/install.sh --admin-email correo@dominio.com --schedule
sudo ufw allow from 192.168.0.0/24 to any port 443    # LAN; agregar la subred de la VPN
```

`--schedule` instala el cron de backups (`/etc/cron.d/bury-backup`, ver [offsite-monitoring-activacion.md](offsite-monitoring-activacion.md) §3).

## 5. Clientes (cada PC)

1. **Nombre `tbp`.** Preferido: un registro DNS local `tbp` → IP del servidor en el router (si no lo permite, `nslookup tbp` falla: usar `hosts`). Alternativa por PC: agregar `IP-DEL-SERVIDOR tbp` en `C:\Windows\System32\drivers\etc\hosts`.
   Por VPN (WireGuard), usar la IP del servidor dentro de la VPN y poner `DNS = <IP del router>` en la configuración del cliente.
2. **Confiar en la CA.** El instalador deja `caddy-root-ca.crt` en la raíz del repositorio (solo el certificado público). En cada PC, PowerShell como administrador:

   ```powershell
   Import-Certificate -FilePath .\caddy-root-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
   ```

   Firefox usa su propio almacén (importarlo allí o activar `security.enterprise_roots.enabled`). Reiniciar el navegador.
3. Abrir **`https://tbp`** completo (con `https://`; si se escribe solo `tbp` el navegador lo busca como texto).

## 6. Después de instalar

1. **Guardar el `.env` completo en el gestor de contraseñas** (nota segura de Bitwarden). Contiene todos los secretos y **no está en los backups**: sin él no se puede recuperar el sistema en otro servidor.
2. Entrar con el usuario inicial, **cambiar la contraseña** y comprobar un segundo login. Luego quitar `ADMIN_PASSWORD` del `.env` y ejecutar `docker compose -p theburyproject rm -f migrate`.
3. Copiar la carpeta de backups a un disco/pendrive externo con regularidad (ver §8: hoy las copias son solo locales).
4. Crear la cuenta de soporte propia (SuperAdmin nominal) desde la pantalla de usuarios; no compartir la del cliente.

## 7. Actualizar a una versión nueva

En el servidor (WSL en Windows), dentro del repositorio:

```bash
git pull --ff-only
bash scripts/install/install.sh --yes
```

Construye una imagen con tag nuevo (`AAAAMMDD-<commit>`), hace backup, aplica migraciones, reemplaza la app y verifica salud. Los datos viven en volúmenes y no se tocan. Si algo falla,
la imagen anterior sigue disponible: `bash scripts/deploy/rollback.sh --to-image theburyproject/erp:<tag-anterior> --confirm-compatible`. Detalle y garantías: [deploy-rollback.md](deploy-rollback.md).

## 8. Backups: decisión actual

La instalación usa **solo backups locales** (`BACKUP_REQUIRE_OFFSITE=false`): full diario, log de transacciones cada 15 min y prueba de restauración semanal, en `BACKUP_DIR`.
Esto cubre errores de aplicación y borrados accidentales, **no** la pérdida del servidor. Para reducir el riesgo:

- Dejar `BACKUP_DIR` dentro de WSL (en Linux, `/srv/bury-backups`). **No usar `/mnt/c` ni `/mnt/d`:** la subcarpeta `sql` debe pertenecer al usuario del contenedor de SQL Server (uid 10001) y los discos de Windows (NTFS) no lo permiten. Esa subcarpeta no se puede listar con el usuario normal del host: es lo esperado.
- **Copiar los backups a un disco externo periódicamente** y guardarlo en otro lugar. En Windows la carpeta se ve desde el Explorador en `\\wsl.localhost\Ubuntu\<ruta>` (el contenido de `sql` requiere `wsl -u root`).
- En Linux, si hay un segundo disco físico, montarlo y usarlo como `BACKUP_DIR`.
- Para activar copia externa más adelante: [offsite-monitoring-activacion.md](offsite-monitoring-activacion.md) (definir `BACKUP_REMOTE=secure:erp` y `BACKUP_REQUIRE_OFFSITE=true`).

Restauración: [backup-restore.md](backup-restore.md) §10.

## 9. Verificación rápida

```bash
docker compose -p theburyproject ps                      # app, db y caddy healthy/running
curl --cacert caddy-root-ca.crt --resolve tbp:443:127.0.0.1 https://tbp/health/ready   # 200
bash scripts/backup/backup.sh full                       # backup de prueba (exit 0)
bash scripts/backup/restore-test.sh                      # restauración no destructiva
```

## 10. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| `bad interpreter` / `\r` en un `.sh` | El repositorio se clonó desde Windows. Volver a clonarlo **dentro de WSL** |
| El instalador dice que Docker no responde | Abrir Docker Desktop y activar la integración WSL con la distro (Settings > Resources > WSL integration) |
| Otras PCs no llegan a `https://tbp` | Firewall (regla `TheBury ERP 443` y `-AllowFrom`), resolución de `tbp` (`nslookup`/`hosts`), o reenvío del puerto desde WSL2 |
| Advertencia de certificado | Falta instalar `caddy-root-ca.crt` en esa PC y reiniciar el navegador |
| `http://tbp` no responde | Es lo esperado: solo se publica 443. Usar `https://tbp` |
| El ERP no vuelve tras reiniciar | Falta el inicio de sesión automático o Docker Desktop no arranca al iniciar sesión (§3.3) |
| Backup termina en error 6 | `BACKUP_REQUIRE_OFFSITE=true` sin `BACKUP_REMOTE`. Para solo local: `false` |
| `preflight` rechaza el `.env` | Quedó un valor `CHANGE_ME` o falta `AUTOMAPPER_LICENSE_MODE=community` / `AUTOMAPPER_LICENSE_KEY` |

## 11. Alcance y pendientes

- **Validación (2026-10-03):** `install.sh` se ejecutó de punta a punta en WSL2 (Ubuntu) con Docker Desktop: instalación nueva (115 migraciones, health y smoke OK), actualización a una imagen nueva con backup previo verificado, Caddy con HTTPS y CA (verificación TLS correcta con la CA y rechazada sin ella), y 8080/1433 sin publicar. El generador de `.env` tiene 40 comprobaciones (corren en CI).
  `install-windows.ps1` está probado con `-DryRun`, validaciones de parámetros y `-Uninstall -DryRun`; **sus efectos reales (firewall, tareas programadas, energía) se ejecutan por primera vez en la instalación del equipo de destino.**
- **No incluido todavía:** alertas por Telegram, observador externo de caídas, copia externa de backups y el planificador de backups dentro de Docker (hoy corre en el anfitrión, con cron en Linux y Programador de tareas en Windows).
