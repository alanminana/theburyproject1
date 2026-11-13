# P-05 — Red y acceso seguro (Tailscale, dos personas)

Fecha: 2026-11-12. Estado: **preparado y probado en entornos aislados; NO cerrado.** No se instaló ni autenticó Tailscale, no se tocó el firewall/Docker del
equipo de desarrollo, no se abrió ningún puerto, no se tocó `TheBuryProjectDb`. Faltan las pruebas reales desde los dos clientes autorizados (§8).

> **Actualización 2026-11-12 (permisos diferenciados):** el modelo de "dos personas iguales" de §3-§4 fue reemplazado por tres perfiles (LAN solo web; usuario remoto solo web; administrador remoto web + administración) con
> grupos `group:erp-usuarios` y `group:erp-admins`, dos perfiles de política por plataforma y administración con mínimo privilegio. Ver [p05-permisos-acceso-remoto.md](p05-permisos-acceso-remoto.md). Donde este documento hable de "las dos personas" con el mismo acceso, rige el nuevo.

Decisiones del propietario (2026-11-12): dos personas acceden por Tailscale, cada una con su **cuenta independiente**; el acceso se **revoca individualmente**;
el ERP **no se expone a Internet**; el servidor definitivo **no está decidido** (Windows 11 Pro + Docker Desktop/WSL2, o Windows Server + Hyper-V + VM Ubuntu).
Complementa (no reemplaza) `red-privada-lan-vpn.md`, `p07-preparacion-windows-docker-tailscale.md` e `instalacion-windows-server.md`.

## 1. Qué existía y qué faltaba

| Pieza | Antes de este trabajo | Ahora |
|---|---|---|
| Caddy `tls internal`, solo 443 publicado, `app`/`db` sin `ports:` | Existía (`docker-compose.yml`, `Caddyfile`) | Probado con Caddy real: `scripts/install/test-caddy-tls.sh` |
| Firewall de puertos publicados (`DOCKER-USER`) | `bury-firewall.sh`, probado **solo con un iptables simulado** | Probado con Docker real y clientes en redes distintas: `test-firewall-isolation.sh` |
| Origen de Tailscale (`100.64.0.0/10`) | Aceptado desde **cualquier interfaz** | **Atado a `tailscale0`** (`100.64.0.0/10@tailscale0`): un equipo de la LAN no puede suplantar un origen 100.x (hallazgo H-1, §6) |
| Política de acceso de Tailscale (ACL/grants) | **No existía** | `deploy/tailscale/policy.hujson` + validador/evaluador `tailscale-policy-check.py` + 53 pruebas |
| Confianza de la CA en clientes | `Import-Certificate` a ciegas, sin comprobar el archivo | `Install-ErpCa.ps1`: exige **huella SHA-256 entregada por otro canal**, CA raíz vigente, sin clave privada; `-Remove` para la baja del equipo |
| Windows 11: firewall de Windows | Regla única con `100.64.0.0/10` válida por cualquier adaptador | Regla separada atada al adaptador `Tailscale` (sin probar en un Windows real, §8) |
| Instalación de Tailscale en el servidor | `-Tailscale` instala el paquete sin autenticar | Sin cambios (correcto: la autenticación es del propietario) |
| Pruebas desde dos clientes reales por Tailscale | Ninguna | **Siguen pendientes** (§8) |

## 2. Arquitectura

```text
Persona 1 (cuenta SSO propia, su equipo) ─┐                               ┌─ tag:erp (el servidor, nodo propio en la tailnet)
Persona 2 (cuenta SSO propia, su equipo) ─┴─ Tailscale (WireGuard) ──────>│  Caddy :443  ──> app:8080 ──> db:1433  (solo red bury-net)
                                              ACL: solo tcp/udp 443        └─ firewall del host: 443 solo desde la LAN y 100.64.0.0/10 por tailscale0
Internet público ──X── sin port-forwarding, sin Funnel, sin subnet router, sin exit node
```

Cuatro capas. **Solo las dos primeras niegan acceso de red** (Docker publica el 443 tal cual y la aplicación autentica pero no limita el alcance); ninguna reemplaza a las otras.
**Para que el acceso sea realmente "solo por Tailscale", el firewall debe permitir únicamente `100.64.0.0/10`** (sin `LocalSubnet` ni la LAN); los ejemplos de los instaladores incluyen la LAN por
compatibilidad y deben ajustarse a esa decisión (§10, punto 4). Mientras la LAN esté permitida, cualquier equipo de la LAN llega al 443 sin pasar por la identidad de Tailscale (seguiría necesitando el login del ERP).

1. **Tailscale ACL (`policy.hujson`)**: solo las dos identidades llegan a `tag:erp`, y solo a `443`.
2. **Firewall del host**: `bury-firewall.sh` (Linux/VM, cadena `DOCKER-USER`) o reglas de Windows Defender (Windows 11); `ERP_BIND_ADDRESS` opcional.
3. **Docker**: solo Caddy publica puertos; SQL (1433) y Kestrel (8080) viven en `bury-net`.
4. **Aplicación**: login del ERP, permisos y roles (independiente de la red: quitar a alguien de la red **no** lo da de baja en el ERP, ni al revés).

## 3. Política de Tailscale (`deploy/tailscale/policy.hujson`)

- `group:erp-usuarios`: **una línea por persona**. Quitar la línea revoca solo a esa persona.
- Un único grant: `group:erp-usuarios → tag:erp`, `tcp:443` y `udp:443` (HTTP/3 de Caddy). Nada de 22, 3389, 1433, 8080.
- `tag:erp` solo lo puede asignar `autogroup:admin`; los usuarios del ERP no pueden crear nodos con esa etiqueta.
- Sin `acls` legacy, `autoApprovers`, `ssh`, `nodeAttrs` de Funnel. Las personas no se alcanzan entre sí.
- La sección `tests` la ejecuta la consola de Tailscale **antes de aceptar** la política; el validador offline las evalúa igual.

Validación (CI y local; solo Python 3, sin red):

```bash
python scripts/install/tailscale-policy-check.py                           # política versionada
python scripts/install/tailscale-policy-check.py --require-real-identities # antes de pegarla en la consola: falla si quedan los correos de ejemplo
python scripts/install/tailscale-policy-check.py --can persona1@example.com tag:erp tcp 443
bash scripts/install/test-tailscale-policy.sh
```

El validador es un **análisis estático + evaluador del subconjunto de grants que usa el proyecto**; no reproduce todo Tailscale. La validación definitiva es la
de la consola. El ejemplo usa `example.com`: **reemplazar por las identidades reales** (decisión/dato del propietario).

## 4. Configuración en la consola de Tailscale (la hace el propietario)

1. Crear/usar la tailnet con **SSO** (Google/Microsoft/…): cada persona entra con su cuenta; no compartir cuentas ni dispositivos.
2. *Access controls*: pegar `policy.hujson` con las identidades reales y guardar (la consola corre los `tests`).
3. *Settings → Device management*: activar **Device approval** (un dispositivo nuevo no entra hasta que un administrador lo aprueba) y **deshabilitar Node sharing** (nadie puede compartir el servidor con terceros).
4. *Settings → Keys*: dejar la expiración de clave de usuarios activa (re-autenticación periódica) y **deshabilitar la expiración de clave solo en el nodo del servidor** (si no, sale de la tailnet sin aviso).
5. *DNS*: activar MagicDNS. **No** activar *HTTPS certificates* de Tailscale (decisión de certificado: §5). **No** habilitar Funnel.
6. En el servidor: `sudo tailscale up --hostname=tbp --advertise-tags=tag:erp --accept-dns=false --accept-routes=false --ssh=false` (en Windows: la app de Tailscale + `tailscale up` equivalente).
   Con MagicDNS y `ERP_DOMAIN=tbp`, `https://tbp` resuelve en los clientes de la tailnet **sin editar `hosts`**. No usar `--advertise-routes` ni `--advertise-exit-node`.
7. Verificar en *Machines*: el servidor aparece con `tag:erp`, sin rutas anunciadas, con expiración deshabilitada.

### Alta de una persona
Agregar su correo a `group:erp-usuarios` → aplicar la política → la persona instala Tailscale en SU equipo, entra con SU cuenta → un administrador aprueba el dispositivo →
instala la CA (§5) → prueba `https://tbp`. Después: usuario y rol en el ERP (control aparte).

### Baja / revocación individual
1. Quitar su correo de `group:erp-usuarios` y aplicar (efecto inmediato: ya no alcanza `tag:erp`).
2. En *Machines*, eliminar sus dispositivos; en *Users*, suspender/eliminar la cuenta; si usa SSO, deshabilitarla en el proveedor.
3. En su equipo: `Install-ErpCa.ps1 -Remove -Sha256 <huella>`. **No es solo higiene**: una raíz instalada es confiable para *cualquier* sitio HTTPS de ese equipo; quien obtenga `root.key` podría hacer MITM de todo su tráfico HTTPS, no solo el del ERP.
4. Dar de baja su usuario en el ERP (sesión y permisos) — control independiente de la red.
No hace falta rotar ninguna clave ni la CA: la otra persona no se entera ni pierde acceso.

## 5. HTTPS y confianza de certificados

- Caddy emite con su **CA interna** (`tls internal`); sin ACME, sin DNS público, sin puerto 80 publicado (Caddy escucha 80 solo dentro de `bury-net` para redirigir a HTTPS).
- **Mecanismo de confianza** (cada equipo cliente):
  1. El administrador extrae **solo el certificado público** y su huella: `docker compose -p theburyproject cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt` y `openssl x509 -in caddy-root-ca.crt -noout -fingerprint -sha256`.
  2. Entrega el archivo por un canal y **la huella por otro** (en persona/teléfono). Nunca `root.key`/`intermediate.key`.
  3. En el cliente: `.\Install-ErpCa.ps1 -CertPath .\caddy-root-ca.crt -Sha256 '<huella>'` (almacén del usuario, sin administrador; `-Scope LocalMachine` con administrador; `-VerifyOnly` para solo comprobar). Reiniciar el navegador. Firefox: `security.enterprise_roots.enabled=true` o importar en su almacén.
- Probado con Caddy real (`test-caddy-tls.sh`): con la CA, HTTPS 200 y SAN correcto; sin la CA o con una CA ajena, el cliente rechaza el certificado; un nombre no configurado no obtiene certificado; HTTP solo redirige.
- La CA raíz dura ~10 años y vive en el volumen `caddy-data` (incluido en el backup de archivos). **`caddy-data` contiene `root.key`: tratarlo y sus copias como secreto crítico** (cifrado, acceso restringido); su filtración permite emitir certificados aceptados por los equipos que instalaron la CA. Si se pierde o se sospecha de filtración, crear una CA nueva, redistribuirla y retirar la anterior de todos los clientes. Ninguna prueba cubre esta custodia.
- **Decisión pendiente del propietario**: CA interna instalada en cada equipo (hoy) vs certificado público de `tailscale cert` (evita instalar la CA, pero publica el nombre de la máquina en los registros de transparencia de certificados y exige activar HTTPS en la tailnet).

## 6. Hallazgos y correcciones

| # | Hallazgo | Corrección |
|---|---|---|
| H-1 | `bury-firewall.sh` aceptaba `100.64.0.0/10` (y subredes/IP dentro) por **cualquier interfaz**. Tailscale protege `INPUT` con su propia cadena, **pero no los puertos publicados por Docker** (van por `FORWARD`): un equipo de la LAN podía enviar paquetes con origen 100.x (sobre todo UDP/HTTP3). | `ALLOW_FROM` admite `CIDR@interfaz`; el bootstrap ata a `tailscale0` **cualquier** origen dentro de `100.64.0.0/10` (red, subred o IP). Probado con un cliente que suplanta 100.64.88.200 desde la LAN (cae en el `DROP`). |
| H-2 | No había política de Tailscale versionada: el aislamiento entre personas y el mínimo privilegio dependían de configurar la consola a mano. | `policy.hujson` + validador + pruebas, ejecutadas en CI. |
| H-3 | La instalación de la CA no verificaba el archivo (cualquier `.crt`, incluido uno con clave privada). | `Install-ErpCa.ps1` (huella fuera de banda, CA raíz, vigencia, sin clave). Revisión independiente: se lee el archivo **una vez**, se exige un único bloque PEM y se instala el **objeto ya validado** (no se relee por ruta), para que un segundo certificado no pase con la huella del primero. |
| H-4 | El firewall de puertos publicados solo estaba probado con un `iptables` simulado. | Prueba real con Docker: línea base (sin firewall el ajeno llega), autorizado, denegado, 1433/8080 inalcanzables, revocación, idempotencia, egreso no afectado. |
| H-5 | Windows 11: una regla Allow de `100.64.0.0/10` valía por cualquier adaptador. | `install-windows.ps1` crea `TheBury ERP 443 Tailscale` atada al adaptador `Tailscale` (sin probar en Windows real); aborta si el adaptador no existe. |
| H-6 | Compose/Caddy: confirmado que `db` y `app` no publican puertos y Caddy solo 443 tcp/udp. | Sin cambios; ahora verificado por test (`test-caddy-tls.sh`). |

**Límite de la prueba del firewall (a verificar en la VM real):** `test-firewall-isolation.sh` simula la tailnet con un bridge de Docker llamado `tailscale0`; la interfaz real es un TUN y `tailscaled` mantiene sus propias reglas en `FORWARD` (cadenas `ts-*`) que podrían evaluarse antes que `DOCKER-USER`. Si fuera así, el tráfico real de la tailnet no pasa por `BURY-ERP` y el filtro efectivo para Tailscale es la política (§3); la regla `@tailscale0` sigue siendo correcta para el spoof desde la LAN (que sí está probado). En el servidor real: `iptables -S FORWARD` y `iptables -S DOCKER-USER`, y probar desde un dispositivo sin permiso en la política.

Riesgo residual conocido (sin cambio): una regla Allow de `com.docker.backend.exe` en Windows 11 puede anular el filtro por puerto; el preflight la detecta. La regla Block a Internet
documentada en `red-privada-lan-vpn.md` §6 **no se añadió** porque la palabra clave `Internet` de Windows Firewall podría incluir el rango CGNAT de Tailscale y cortar el acceso legítimo: probar en el servidor real antes de usarla.

## 7. Plataforma: Windows 11 vs Windows Server (sin decidir)

| | Windows 11 Pro + Docker Desktop/WSL2 | Windows Server + Hyper-V + VM Ubuntu |
|---|---|---|
| Instalador | `install-windows.ps1 -AllowFrom LocalSubnet,100.64.0.0/10` | `install-windows-server.ps1 ... -AllowFrom ... -Tailscale` (VM + `server-linux-bootstrap.sh`) |
| Dónde corre Tailscale | En Windows (servicio `Tailscale`) | **Dentro de la VM** (nodo propio; el host Hyper-V no es subnet router) |
| Filtro de red | Firewall de Windows (reglas separadas LAN / adaptador Tailscale) | `bury-firewall.sh` en `DOCKER-USER` (+ ufw solo SSH) |
| Mismo en ambos | `docker-compose.yml`, `Caddyfile`, `policy.hujson`, `Install-ErpCa.ps1`, `ERP_DOMAIN=tbp`, `--hostname=tbp` | |
| Pruebas automáticas | Política, CA y TLS son idénticas; el filtro de Windows **no** tiene prueba automática | Firewall, política, TLS, bootstrap |
| Si falta el adaptador `Tailscale` | `install-windows.ps1` aborta **antes** de tocar el firewall con un mensaje claro | n/a |

La política de Tailscale, el nombre y la CA no dependen de la plataforma: la decisión se puede tomar después sin rehacer nada de lo anterior.

## 8. Pruebas: qué está automatizado y qué exige lo real

Automatizado (CI, job `network-security` + pasos de `compose-validate` y `windows-scripts`; datos sintéticos, redes y contenedores efímeros):

| Prueba | Cubre |
|---|---|
| `test-tailscale-policy.sh` (53) | autorizado, denegado (22/80/1433/3389/5432/8080), aislamiento entre personas, cuenta ajena, revocación individual y de grupo vacío, rechazo de políticas permisivas (`*`, SSH, rangos, Funnel, autoApprovers, ACL legacy…) |
| `test-firewall-isolation.sh` (18) | puerto publicado con Docker real: línea base sin firewall, tailnet (`tailscale0`) y LAN autorizadas, ajeno denegado, 1433/8080 inalcanzables, spoof 100.x desde la LAN, egreso no afectado, revocación, idempotencia, retiro |
| `test-caddy-tls.sh` (14) | CA pública sin clave, huella, confianza/no confianza, SAN, CA ajena, nombre no configurado, HTTP solo redirige, compose publica solo 443 |
| `Test-InstallErpCa.ps1` (16) | huella, CA raíz vs hoja/intermedia, vencida, clave privada, múltiples certificados, basura (solo `-VerifyOnly`) |
| `test-install.sh` (84) | bootstrap/firewall (incluye `@interfaz`), instalador |

Local, sin tocar el equipo: `bash scripts/install/test-firewall-isolation.sh --in-dind` y `... test-caddy-tls.sh --in-dind` usan un Docker-in-Docker descartable.

**Requiere lo real (no se puede ni se debe simular):**

| Falta | Necesita |
|---|---|
| Aplicar la política en la consola y que pasen sus `tests` | Tailnet real, identidades reales, administrador |
| Nodo del servidor con `tag:erp`, expiración deshabilitada, Device approval, Node sharing off | Tailscale instalado y autenticado en el servidor definitivo |
| **Acceso autorizado desde los dos clientes reales** (`https://tbp`, login, cookies, `Test-NetConnection` 443 sí / 22,1433,8080 no) | Servidor + dos equipos/cuentas reales |
| **Denegado**: tercera cuenta/dispositivo sin acceso; Tailscale apagado/datos móviles no llega | Una cuenta extra de prueba y un teléfono |
| **Revocación real**: quitar a una persona y comprobar que pierde acceso y la otra no | Las dos cuentas reales |
| Instalación de la CA con la huella real en ambos equipos; Firefox si se usa | Servidor y clientes |
| Firewall de Windows (reglas por adaptador) y la regla Block | Windows 11 o Windows Server reales |
| Reinicio del servidor: Tailscale + Docker + Caddy vuelven solos | Servidor definitivo |
| Router: confirmar que no hay port-forwarding al 443; CGNAT | Red definitiva |

Lista de aceptación desde cada cliente autorizado: `p07-preparacion-windows-docker-tailscale.md` §5 y `red-privada-lan-vpn.md` §6 punto 4, más: tercera cuenta denegada y revocación individual.

## 9. Avance de P-05

| Criterio | Estado |
|---|---|
| Caddy/TLS interno, puertos mínimos publicados | ✅ hecho y probado |
| Firewall del host, sin saltarse con puertos publicados de Docker | ✅ Linux/VM probado con Docker real; ⚠️ Windows sin prueba real |
| Política Tailscale de mínimo privilegio, dos identidades, aislamiento, revocación | ✅ versionada y validada offline; ⏳ sin aplicar en tailnet real |
| Mecanismo de confianza de la CA | ✅ script + pruebas; ⏳ sin instalar en clientes reales |
| Pruebas automatizadas en CI | ✅ |
| Documentación y procedimientos de alta/baja | ✅ |
| Instalación en el servidor definitivo | ⏳ |
| Acceso real desde los dos clientes, denegado y revocación reales | ⏳ |
| Decisiones: plataforma, certificado, identidades reales, administrador de la tailnet | ⏳ |

**Avance estimado: ~65 %** (todo lo verificable sin infraestructura real está hecho y probado; el ~35 % restante exige servidor, Tailscale y cuentas reales).
**P-05 no se declara finalizado** hasta completar las pruebas reales desde los dos clientes autorizados.

## 10. Decisiones y datos del propietario

1. Correos reales de las dos personas y quién administra la tailnet y su política.
2. Plataforma definitiva (Windows 11 + Docker Desktop o Windows Server + Hyper-V + VM).
3. Certificado: CA interna en cada equipo (preparado) o `tailscale cert`.
4. ¿Los equipos del local también entran por Tailscale o por la LAN? (con la LAN: IP reservada y `hosts`/DNS local; con Tailscale: nada que editar).
5. Cuenta de prueba adicional y un teléfono para la prueba de acceso denegado.
