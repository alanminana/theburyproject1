# Despliegue privado LAN/VPN (Caddy con CA interna)

Instalación para ~6 clientes en LAN o VPN. **El ERP no recibe conexiones desde Internet**: sin DNS público,
sin port-forwarding, sin Let's Encrypt/ACME. La VPN se instala y configura aparte.

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433
```

- Caddy usa `tls internal` (Caddyfile): emite el certificado para `ERP_DOMAIN` con su **CA interna**; no necesita Internet ni el puerto 80.
- Único puerto publicado en producción: **443 TCP y 443 UDP (HTTP/3)**. `app` (8080) y `db` (1433) no definen `ports:`; solo son
  alcanzables por la red `bury-net`. Consecuencia: `http://tbp` no responde; hay que escribir `https://tbp`.
- `ERP_DOMAIN=tbp` en el `.env` privado (no versionado). El preflight solo exige que exista y no esté vacío; acepta hostnames sin FQDN.
- Salida a Internet del host: solo para apt/updates, GitHub, registro Docker, DNS y NTP. MercadoLibre desactivado. Backups locales por ahora.

## 1. Resolución del nombre `tbp` desde los clientes

**Opción simple (hasta ~6 PCs):** en cada PC Windows, editor como Administrador, agregar al final de
`C:\Windows\System32\drivers\etc\hosts`:

```text
<IP-LAN-DEL-SERVIDOR> tbp
```

(reemplazar por la IP real y reservada/estática del servidor; se define al instalar el host). Para clientes por VPN, usar la IP a la
que el servidor sea alcanzable desde la VPN.

**Alternativa:** registro DNS local `tbp` → IP del servidor en el router o DNS del local, si lo permite. No se exige un servidor DNS propio.

## 2. Confianza TLS (CA raíz de Caddy)

Tras el primer arranque, Caddy genera su CA en el volumen `caddy-data`. Solo la **CA raíz pública** se distribuye; **nunca** la clave
privada (`root.key`, `intermediate.key`) ni se versiona nada de `caddy-data`.

En el servidor, extraer únicamente el certificado público:

```bash
docker compose -p theburyproject cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt
```

(equivalente: `docker compose exec caddy cat /data/caddy/pki/authorities/local/root.crt > caddy-root-ca.crt`). Verificar que es un certificado
público: `openssl x509 -in caddy-root-ca.crt -noout -subject -dates`.

**Windows (cada cliente), PowerShell como Administrador:**

```powershell
Import-Certificate -FilePath .\caddy-root-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
```

o gráficamente: doble clic en el `.crt` → Instalar certificado → Equipo local → "Colocar todos los certificados en el siguiente almacén" →
**Entidades de certificación raíz de confianza**. Firefox usa su propio almacén (Configuración → Certificados → Importar, o
`security.enterprise_roots.enabled=true`). Reiniciar el navegador.

La CA raíz dura ~10 años; el certificado de `tbp` se renueva solo. Si se pierde `caddy-data`, Caddy crea una CA nueva y hay que redistribuirla
(por eso `caddy-data` está en el backup de archivos).

### Verificación

```bash
curl --cacert caddy-root-ca.crt https://tbp/health/live    # 200
curl --cacert caddy-root-ca.crt https://tbp/health/ready   # 200
openssl s_client -connect tbp:443 -servername tbp </dev/null 2>/dev/null | openssl x509 -noout -subject -issuer -ext subjectAltName
```

Desde un navegador que confía en la CA: `https://tbp/health/live` y `https://tbp/health/ready` sin advertencias. (`curl -k` solo como diagnóstico.)

## 3. Portabilidad del host

| Portable (Docker, idéntico en cualquier host) | Específico del host |
|---|---|
| `db` (SQL Server Express 2022, contenedor propio), `db-init`, `migrate`, `app`, `caddy` | Instalación de Docker |
| Volúmenes (`bury-sqldata`, `bury-keys`, `bury-uploads`, `bury-appdata`, `caddy-data`), red `bury-net` | Firewall (permitir 443 solo desde LAN/VPN) |
| `scripts/deploy/deploy.sh` y `rollback.sh` | Autoarranque de Docker tras reinicio |
| Imagen con tag inmutable | Ubicación física de `BACKUP_DIR` |
| | Programador de tareas (cron / Task Scheduler) |
| | Red LAN/VPN, IP fija del servidor |

**A. Linux + Docker Engine.** Docker Engine + plugin compose; `systemctl enable docker`; firewall `ufw allow from <LAN/VPN> to any port 443`;
backups programados con `scripts/backup/install-schedule.sh --install` (cron) o timer systemd.

**B. Windows 10/11 + Docker Desktop/WSL2.** Docker Desktop con backend WSL2, "Start Docker Desktop when you sign in" (o servicio equivalente)
y ejecutar los scripts `.sh` desde WSL2 o Git Bash sobre el mismo repo; regla de Windows Defender Firewall solo para 443 entrante desde la
LAN/VPN; backups con Task Scheduler invocando los mismos scripts (`wsl.exe -e ...`); en WSL2 el puerto publicado debe ser alcanzable desde la
LAN (verificar el reenvío de Docker Desktop; en modo mirrored networking suele funcionar directo). `install-schedule.sh` genera cron y **no**
está adaptado a Task Scheduler todavía. Ambos caminos usan el mismo stack Docker; sin cambios de scripts funcionales.

## 4. Actualización y rollback (sin cambios conceptuales)

Nueva release: obtener el tag Git → construir/cargar la imagen con **tag inmutable nuevo** (`ERP_IMAGE=theburyproject/erp:<tag>`) →
`scripts/deploy/deploy.sh` (preflight → backup → db-init → migrate → app → health → smoke) → queda registrada la release y la imagen anterior
se conserva. Detalle: `docs/deploy-rollback.md`.

Rollback: `scripts/deploy/rollback.sh --to-image theburyproject/erp:<tag-anterior> --confirm-compatible`.

Nunca: copiar archivos sobre el contenedor, reutilizar `:latest` ni un tag anterior para otro contenido, borrar volúmenes, `docker compose down -v`.
SQL, uploads, `App_Data` y las claves Data Protection viven en volúmenes y sobreviven a deploy y rollback.

## 5. Base de datos

Sin cambios: SQL Server Express 2022 en contenedor `db` separado, datos en `bury-sqldata`, solo en `bury-net`, puerto 1433 **no** publicado
en producción. No se mueve SQL a otra máquina ni se instala en el host.

## Pendiente (requiere el servidor final)

IP reservada, `hosts`/DNS, extracción e instalación de la CA en clientes, reglas de firewall, autoarranque, scheduler de backups y VPN.
