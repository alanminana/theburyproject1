# P-05 — Permisos diferenciados de acceso (LAN, usuario remoto, administrador remoto)

Fecha: 2026-11-12. Estado: **preparado y probado con datos sintéticos; NO cerrado.** Complementa `p05-tailscale-acceso-remoto.md` (red, Caddy, CA) y reemplaza su
modelo de "dos personas iguales" por tres perfiles. No se instaló ni autenticó Tailscale, no se abrió ningún puerto, no se tocó el firewall ni Docker del equipo
de desarrollo ni `TheBuryProjectDb`. El servidor definitivo (Windows 11 Pro o Windows Server + Hyper-V + VM Ubuntu / Linux) **sigue sin elegirse**.

## 1. Modelo (decidido por el propietario)

| Quién | Cómo llega | Qué puede | Qué NO puede |
|---|---|---|---|
| Usuario de la **LAN** | Red local, `https://tbp` | Solo la web HTTPS (443) | Administrar el host (SSH/RDP/SMB/WinRM), SQL 1433, Kestrel 8080 |
| **Usuario remoto** (identidad Tailscale 1) | Tailscale | Solo la web (tcp/udp 443) | Todo lo demás |
| **Administrador remoto** (identidad Tailscale 2) | Tailscale | La web + administrar el servidor (**un** puerto) | SQL, Kestrel, otros puertos; alcanzar al usuario |

Las dos identidades Tailscale son **independientes** (cuentas SSO y dispositivos distintos, grupos disjuntos). Ninguna interfaz administrativa se expone a Internet
(sin port-forwarding, Funnel, subnet router ni exit node). Iniciar sesión en el ERP (usuario/rol) es un control aparte de la red.

## 2. Capas y dónde se hace cumplir cada permiso

| Capa | Usuario LAN | Usuario remoto | Administrador remoto |
|---|---|---|---|
| Tailscale (`policy*.hujson`) | no aplica (no usa Tailscale) | `tag:erp` tcp/udp 443 | `tag:erp` 443 + **22** (perfil `linux-vm`) o **3389** (perfil `windows`) |
| Firewall de puertos publicados (`bury-firewall.sh`, `DOCKER-USER`; Windows: regla 443) | 443 desde la LAN | 443 desde `100.64.0.0/10` **solo por `tailscale0`** | ídem |
| Firewall de administración del host | **ninguna regla** | ninguna regla | Linux: `ufw allow in on tailscale0 from <IP /32 de Tailscale del administrador> to any port 22 proto tcp`; Windows: `Enable-ErpRemoteAdmin.ps1` (RDP, solo desde su IP, solo adaptador `Tailscale`, una regla por administrador) |
| Aplicación | login/roles del ERP (independiente) | ídem | ídem |

La política **concede**; el firewall del host **abre**. Mientras no se habilite explícitamente la administración en el host, el puerto sigue cerrado aunque la política
lo permita (los dos deben coincidir). **Hoy no hay ningún puerto administrativo abierto en ninguna plataforma** (Linux: `--admin-from` no se usa sin pedirlo; Windows: sin `-Apply` no se crea nada).

### Perfiles de plataforma (misma política, un solo puerto distinto)

| Plataforma | Dónde corre Tailscale | Administración remota | Archivo de política | Cómo se habilita en el host |
|---|---|---|---|---|
| Linux o VM Ubuntu (Windows Server + Hyper-V) | dentro del Linux/VM | SSH (22) | `deploy/tailscale/policy.hujson` (`--profile linux-vm`) | `server-linux-bootstrap.sh --admin-from 100.x.y.z` |
| Windows 11 Pro + Docker Desktop | en el propio Windows | RDP (3389) | `deploy/tailscale/policy.windows.hujson` (`--profile windows`) | `Enable-ErpRemoteAdmin.ps1 -AdminTailscaleIp 100.x.y.z -Apply` |

**Usar la IP /32 del administrador, no el rango `100.64.0.0/10`:** con el rango, la capa del host no distingue al administrador del usuario remoto (solo la política de Tailscale los separa). `--admin-from` acepta el rango por compatibilidad, pero la recomendación es una IP por dispositivo.

Los dos archivos solo pueden diferir en ese puerto (lo comprueba una prueba). Elegir plataforma después no obliga a rehacer nada.
En Windows Server + Hyper-V el host Hyper-V **no** entra a la tailnet: se administra por consola/RDP local y la VM por SSH.

## 3. Qué cambió respecto de #297

| # | Cambio | Archivos |
|---|---|---|
| 1 | Política con dos grupos disjuntos `group:erp-usuarios` (solo web) y `group:erp-admins` (web + 1 puerto de administración). Dos perfiles por plataforma. | `deploy/tailscale/policy.hujson`, `policy.windows.hujson` |
| 2 | Validador por roles: grants exactos por rol, un único puerto de administración, grupos disjuntos, acceso **efectivo** evaluado sobre 12 puertos tcp/udp, pruebas obligatorias (incluido el administrador y la cuenta ajena). | `scripts/install/tailscale-policy-check.py` |
| 3 | `--admin-from` **rechaza redes LAN completas** (solo tailnet atada a `tailscale0`, o equipos /32); `--allow-lan-admin` es la única excepción. Antes el ejemplo documentado daba SSH a toda la LAN. | `server-linux-bootstrap.sh` |
| 4 | El instalador de Windows Server **ya no usa la LAN completa como administración por defecto**: agrega solo la IP /32 del host Hyper-V (el instalador la necesita para llegar a la VM) y rechaza redes completas (`-AllowLanAdmin` para forzar). | `install-windows-server.ps1` |
| 5 | Auditoría del host Linux (`ufw status` + `ss`): nadie administra desde Anywhere/LAN/tailnet sin atar a `tailscale0`; SQL/Kestrel no abiertos ni publicados. | `access-model-audit.py` |
| 6 | Windows 11: habilitación opt-in de RDP para el administrador (apagada por defecto) y auditoría de reglas entrantes del Firewall de Windows. | `Enable-ErpRemoteAdmin.ps1`, `Test-ErpRemoteAccess.ps1` |
| 6b | `Enable-ErpRemoteAdmin.ps1 -Apply` **deshabilita las reglas predefinidas de Escritorio remoto** (`RemoteDesktop-UserMode-In-TCP/UDP`, `-Shadow-In-TCP`): al habilitar RDP en Windows se activan desde cualquier origen, LAN incluida, y anularían la regla estrecha. La auditoría las detecta si quedan. | `Enable-ErpRemoteAdmin.ps1` |
| 7 | Pruebas (todas sintéticas, sin red ni administrador) y pasos en CI. | `test-tailscale-policy.sh`, `test-access-model.sh`, `Test-RemoteAccessModel.ps1`, `test-install.sh`, `Test-WindowsServerInstall.ps1`, `.github/workflows/ci.yml` |

`install-windows.ps1` **no se modificó** (lo toca la PR #299 de P-06): ya crea solo reglas de 443 (LAN y adaptador `Tailscale`) y ninguna de administración; la auditoría
`Test-ErpRemoteAccess.ps1` lo confirma y detecta cualquier regla ajena que abra administración.

## 4. Pruebas

| Qué | Cuántas | Cubre |
|---|---|---|
| `test-tailscale-policy.sh` | 131 | Acceso autorizado por rol (443 tcp/udp, administración), denegado (22/3389/445/1433/5985/8080… para el usuario; todo salvo 1 puerto para el administrador), aislamiento entre personas, cuenta ajena, **revocación individual** (quitar usuario no afecta al administrador y viceversa; degradar administrador→usuario; grupos vacíos), ambos perfiles, deriva entre perfiles, más de 45 políticas permisivas rechazadas |
| `test-access-model.sh` | 44 | **La LAN no administra**: SSH/RDP/SMB/WinRM desde LAN, Anywhere o IPv6 = falla; tailnet sin atar a `tailscale0` = falla; SQL/Kestrel abiertos o escuchando = falla; reglas de `bury-firewall.sh` de la LAN = solo 443 |
| `test-install.sh` | 95 | Bootstrap: LAN completa rechazada como administración, /32 y Tailscale (`tailscale0`) aceptados, ufw sin 443 |
| `Test-RemoteAccessModel.ps1` | 49 | Auditoría de reglas de Windows (LocalSubnet/Any/LAN/mascaras/rangos/Tailscale sin adaptador), especificación de la regla de administración, nada se abre sin `-Apply` |
| `Test-WindowsServerInstall.ps1` | 22 | Incluye `Test-AdminSource` (LAN rechazada, /32 y tailnet aceptados) |

Revocación individual en la práctica: **usuario** = quitar su correo de `group:erp-usuarios`; **administrador** = quitarlo de `group:erp-admins`, borrar sus dispositivos,
y retirar su acceso al host (`ufw delete allow in on tailscale0 from <IP> to any port 22 proto tcp` / `Enable-ErpRemoteAdmin.ps1 -AdminTailscaleIp <IP> -Remove -Apply` (retira solo la regla de esa IP)); además
desinstalar la CA en su equipo (`Install-ErpCa.ps1 -Remove`). Ver `p05-tailscale-acceso-remoto.md` §4.

### Revisión independiente (Sonnet) y correcciones

Revisión de solo lectura con 2 hallazgos Altos/Medios críticos corregidos: (A1) el auditor ufw daba falso OK con `OpenSSH`, `LIMIT`, reglas con IP de destino, columna To larga con un solo espacio y formato numerado → ahora
**falla cerrado** ante líneas o servicios no interpretables; (M7) IP de la tailnet /32 sin atar a la interfaz, origen vacío y puertos con nombre pasaban en los auditores → rechazados; (M1) la guarda anti-bloqueo del bootstrap
dependía de `SSH_CLIENT` (que `sudo` suele borrar) → recurre a `who -m`; (M2) con `-UseDhcp` el instalador podía quedar sin SSH → ahora falla si no conoce la IP del host; (M3) Escritorio remoto abría RDP a la LAN → se deshabilitan
las reglas predefinidas; (M4) una sola regla por nombre → una por administrador; (B1) equipo público `/32` y `0.0.0.0` aceptados → solo privados (RFC 1918) o de la tailnet; (B6) identidades por mayúsculas y estructuras JSON inesperadas.
**Límites sin verificar** (no hay Linux/ufw ni Windows de servidor reales aquí): el formato exacto de `ufw status` en la VM (el auditor falla cerrado ante lo que no entiende), `sudo`/`who -m` en la VM, los valores reales de `Get-NetFirewall*Filter`,
el comportamiento de ufw con `0.0.0.0`, y la interacción `DOCKER-USER`/ufw. El auditor no revisa la política por defecto de ufw (`ufw status verbose` muestra `Default:`): comprobar a mano `deny (incoming)`.

## 5. Riesgo: custodia de la clave privada de Caddy (punto 6)

**Hechos verificados en el código** (no se modificó nada):
- Caddy con `tls internal` guarda su **CA raíz y su clave privada** (`root.key`, `intermediate.key`) en el volumen `caddy-data` (`/data/caddy/pki/authorities/local/`); `test-caddy-tls.sh` lo comprueba.
- `scripts/backup/container/files-backup.sh` incluye `caddy-data` completo en `files_*.tar.gz` si `BACKUP_INCLUDE_CADDY=true` (valor **por defecto: true**). Por lo tanto `root.key`
  está en **cada copia de archivos**, y viaja al disco externo y al SFTP remoto de P-06 (#300). El informe de #299 (ya fusionada, sin tratar `root.key` aparte) lo anota ("el tar de files … caddy-data … viaja al remoto: su
  confidencialidad depende por completo de las passwords crypt").
- Quien obtenga `root.key` puede emitir certificados aceptados por **todos los equipos que instalaron la CA** (usuario remoto, administrador remoto, equipos de la LAN) para **cualquier** sitio HTTPS,
  no solo el ERP: es un MITM total sobre esos equipos. Es el activo más sensible de P-05 y hoy no tiene custodia propia (mismo nivel de protección que cualquier backup).
- `caddy-data` se monta de escritura en Caddy; en Docker, un miembro del grupo `docker` es equivalente a root y lo lee.

**Opciones** (la decisión es del propietario; las dos primeras tocan archivos de P-06 y requieren su acuerdo):

| Opción | Efecto | Costo | Quién |
|---|---|---|---|
| A. Excluir la clave de los backups (`BACKUP_INCLUDE_CADDY=false`, o excluir `pki/authorities/local/*.key` en `files-backup.sh`) | La clave deja de salir del servidor | Si se pierde el servidor hay que crear una CA nueva y reinstalarla en cada equipo (3-4 equipos) | P-06 (backup) |
| B. Cifrado/custodia de los backups que la contienen (verificador de custodia de #299) | La clave sigue en la copia pero protegida | Depende de la custodia de las passwords | P-06 (#299) |
| C. **Raíz offline con restricción de nombres** (recomendada a mediano plazo): raíz generada fuera del servidor, con `nameConstraints` solo para `tbp`, y solo la **intermedia** en `caddy-data` | Una filtración de la intermedia solo permite certificados para `tbp` y se rota sin tocar los clientes; la raíz (instalada en clientes) nunca está en el servidor | Procedimiento de generación y custodia de la raíz; Caddyfile `pki { ca local { root … intermediate … } }` y un volumen/secret | P-05 (Caddyfile, `Install-ErpCa.ps1`) + P-06 (backup) |
| D. Certificado público de `tailscale cert` | No hay CA privada que instalar ni custodiar | Publica el nombre en Certificate Transparency, exige activar HTTPS en la tailnet | propietario |

**Coordinación con P-06:** este trabajo **no modificó** ningún archivo de P-06 (`scripts/backup/*`, `docs/backup-custodia.md`, `scripts/install/install-windows.ps1`, `scripts/monitoring/*`, `.env.example`,
`docker-compose.offsite-sftp.yml`). Pedido concreto a P-06: (1) decidir A o B para `caddy-data` en #299/#300 y que el verificador de custodia trate `root.key` como secreto crítico; (2) no
mezclar el cambio C con sus PR hasta cerrarlas. **Pendiente: no se pudo enviar el aviso directamente porque no se identificó la sesión de P-06 entre las sesiones abiertas**; queda a cargo de quien coordine.
La opción C no se implementó: cambia el despliegue real de TLS (Caddyfile, volumen, restauración) y necesita decisión del propietario y acuerdo con P-06.

## 6. Qué falta para cerrar P-05 (exige infraestructura real)

No se declara P-05 finalizado. Pendiente, igual que en `p05-tailscale-acceso-remoto.md` §8 más lo nuevo de los roles:

| Falta | Necesita |
|---|---|
| Aplicar la política (perfil según plataforma) en la consola y que pasen sus `tests`; reemplazar los correos de ejemplo (`--require-real-identities`) | Tailnet real, **dos identidades reales** y administrador de la tailnet |
| Nodo del servidor con `tag:erp`, expiración deshabilitada, Device approval, Node sharing off | Tailscale instalado y autenticado en el servidor definitivo |
| **Prueba real por rol**: LAN (443 sí; 22/3389/1433/8080 no), usuario remoto (443 sí; administración no), administrador (443 y administración sí; 1433/8080 no), cuenta ajena (nada) | Servidor + 3 clientes + una cuenta extra |
| **Revocación real** de cada identidad sin afectar a la otra | Las cuentas reales |
| Habilitar la administración en el host (`--admin-from` / `Enable-ErpRemoteAdmin.ps1 -Apply`) y correr `access-model-audit.py` / `Test-ErpRemoteAccess.ps1` **en el servidor real** | Servidor definitivo |
| Reglas de Windows Defender reales (sin prueba en Windows real); `iptables -S FORWARD`/`DOCKER-USER` con `ts-*` de tailscaled (límite de §6 de `p05-tailscale-acceso-remoto.md`) | Windows 11/Server y la VM |
| Decisión sobre la custodia de la clave de Caddy (§5) y acuerdo con P-06 | Propietario + P-06 |
| Decisiones: plataforma, certificado, si el local usa LAN o Tailscale, qué equipo es el del administrador | Propietario |

Nota sobre la auditoría Windows: `Test-ErpRemoteAccess.ps1` sobre un Windows 11 real puede listar reglas **preexistentes** del sistema (p. ej. Uso compartido de archivos/SMB habilitado en red privada):
es intencional, forman parte de "la LAN no debe poder administrar el host" y se resuelven deshabilitando esas reglas, no la auditoría. En CI el job `windows-scripts` es `continue-on-error`.
