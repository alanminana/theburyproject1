# P-05: cierre técnico del alcance LOCAL / LAN (2026-11-20)

Base: `main` 58592023 + #338 (929b1978) desplegado en la VM Ubuntu `tbp` (servidor provisional, datos sintéticos, entorno Production, Docker + Caddy + SQL Server Express).
Cliente de las pruebas: este PC (192.168.50.164), un equipo real de la LAN. **No se instaló ni configuró Tailscale**, no se tocaron cuentas/ACL/tags/tailnet, la
Raspberry Pi ni otras máquinas, y no se cambió ninguna regla de firewall (todo fue lectura o prueba dinámica con un namespace temporal que se elimina solo).
Complementa `p05-tailscale-acceso-remoto.md` y `p05-permisos-acceso-remoto.md`; no repite lo ya aprobado (HTTPS/HSTS/cabeceras, puertos 22/443/80/1433/8080, 11 rutas al login).

> **Veredicto: P-05 queda FINALIZADO en su alcance técnico local (LAN).** No se encontró ningún defecto que corregir. El **acceso remoto** (Tailscale real, clientes reales, tercera
> cuenta, revocación real) sigue **pendiente y separado** (§4): no bloquea este cierre.

## 1. Criterios verificados y evidencia

| # | Criterio | Resultado | Evidencia |
|---|---|---|---|
| 1 | HTTPS y cadena de certificados desde un cliente real de la LAN | **OK** | `openssl s_client` desde la LAN (SNI `tbp`): TLS 1.3, `Verify return code: 0` con la CA publicada. Cadena: hoja (SAN `DNS:tbp`, 12 h, EKU servidor) ← intermedia (hasta 26/11, la renueva Caddy) ← raíz «Caddy Local Authority - 2026 ECC Root» (huella SHA-256 `D6:82:32:1F:0C:FB:A3:A4:27:46:7D:1C:0E:EA:9C:E5:51:4B:AD:E3:7D:F3:81:C4:E4:94:D8:30:90:54:68:B4`, válida hasta 2036). El `root.crt` publicado **no contiene clave privada** |
| 2 | Autenticación y autorización por rol, permitido y denegado | **OK** | `scripts/qa/p05/p05_roles_vm.py` (HTTP real, cuentas sintéticas): **27 OK / 0 FALLA**. 23 rutas protegidas × 7 roles (las redirecciones de alias, p. ej. `/Categoria → /Catalogo`, se siguen hasta el destino real; una redirección inconclusa cuenta como falla); lo que responde el servidor coincide con lo que la **base de datos** dice que cada rol puede (el oráculo es `RolPermisos`, no el código): Administrador 23/0 (permitidas/denegadas), Gerente 11/12, Vendedor 7/16, Cajero 6/17, Contador 9/14, Repositor 5/18, Técnico 3/20. SuperAdmin accede a todas; sin sesión las 23 redirigen al login |
| 2b | Escalada de privilegios | **OK** | Los 6 roles sin permiso de usuarios intentan, con **formularios válidos y token antiforgery**, **crear** un SuperAdmin, **cambiar la clave** y **desactivar** a otra cuenta: los tres intentos reciben **denegación explícita** (302 a AccessDenied), sin efecto en la base y la víctima conserva clave y estado. **Control positivo:** esos mismos formularios, ejecutados por `admin` sobre una cuenta de control, sí funcionan (así un rechazo no puede deberse a un formulario roto). POST válido sin token → 400; con token → creado |
| 3 | SSH 22 solo desde orígenes administrativos autorizados | **OK** | `ufw` activo, `deny incoming`, **una** regla: `22/tcp ALLOW 192.168.50.164`; `INPUT` policy DROP. Desde el origen autorizado el 22 responde; desde un origen no autorizado (namespace, 10.77.0.2) **22 sin respuesta** y `ufw` registra `[UFW BLOCK] SRC=10.77.0.2` (1 → 12 bloqueos). `access-model-audit.py` → exit 0 |
| 4 | Reglas efectivas Ubuntu / UFW / Docker | **OK** | `DOCKER-USER → BURY-ERP`: 443 tcp/udp solo desde `192.168.50.0/24` y `100.64.0.0/10` por `tailscale0`; el resto **DROP** (contador 0 → 5 con el origen ajeno). Docker publica solo `443/tcp,udp`; 80, 2019, 8080 y 1433 **no están publicados**. `FORWARD` policy DROP |
| 5 | SQL Server y servicios internos inaccesibles desde redes no autorizadas | **OK** | Desde la LAN autorizada: 80, 1433, 8080, 2019, 3389, 445, 5432, 23, 5985, 9099, 9100 **sin respuesta**; desde el origen ajeno además 443 sin respuesta. Sockets en escucha: 22, 443, `127.0.0.1:9099` (receptor local), DNS/NTP locales y UDP 41641 (`tailscaled`, sin sesión; lo cubre el DROP por defecto) |
| 6 | Endpoints sensibles, Swagger, APIs, administración | **OK** | 36 rutas probadas **sin sesión**: solo `/health/live` y `/health/ready` (200, texto `Healthy`, sin detalle) y `/Home/Privacy`/`/Home/Error` son públicas; `/hubs/notificaciones` 401; `/Identity/Account/Register` 404 (sin auto-registro). **Con SuperAdmin**: `/Diagnostico` (sus acciones de reseteo de claves solo existen en Development), `/swagger`, `/swagger/v1/swagger.json`, `/metrics`, `/hangfire`, `/Identity/Account/Register` → **404**. Rol mínimo (Técnico): `/Diagnostico` y `/Mantenimiento/ScoringGlobal` → AccessDenied. Sin trazas ni cadenas de conexión en `/Home/Error` |
| 7 | Las restricciones persisten tras reiniciar servicios individuales | **OK** | `scripts/qa/p05/p05_red_vm.sh --reinicios`: **25 OK / 0 FALLA** (comprueba el código de salida de cada reinicio y exige que el ERP vuelva a responder `live` y `ready` 200 por `https://tbp` antes de aprobar cada etapa). Tras `docker compose restart caddy`, `restart app` y `ufw reload` la huella de reglas (`DOCKER-USER`, `BURY-ERP`, `INPUT`, ufw 22) es **idéntica** (`f1c7279f1e51a7d4`), el origen ajeno sigue sin llegar a 443 y 1433/8080 siguen inalcanzables; el ERP volvió `ready=200`. El SSH de la sesión no se interrumpió. (Reinicio de la VM ×4 y de `dockerd`: ya probados en `p04-p08-matriz-criterios-vm-simulada.md`) |
| 8 | Defectos demostrados | **Ninguno** | No hubo nada que corregir |

Reproducción: `p05_roles_vm.py` y `p05_red_vm.sh` se copian a la VM y se ejecutan con `--confirmo-entorno-de-ensayo` (el primero requiere `scripts/qa/p03/p03_vm_produccion.py` al lado).
Ambos son **solo para un servidor de ensayo**: el de roles crea y elimina cuentas `p05.*` (verifica que `admin` conserve su clave) y el de red crea y destruye un namespace temporal.

## 2. Observaciones (no son defectos demostrados; decisión del propietario)

- **SSH por contraseña activo** (`PasswordAuthentication yes`, `MaxAuthTries 6`, `X11Forwarding yes`, `AllowTcpForwarding yes`; `PermitRootLogin prohibit-password`). Hoy solo
  llega el PC 192.168.50.164, que ya entra por clave. Endurecer a solo-clave y sin reenvíos se puede hacer sin riesgo **después** de comprobar el acceso por clave desde cada administrador;
  no se cambió (riesgo de bloqueo; se pidió no tocar el SSH actual).
- **El origen administrativo 192.168.50.164 es una IP por DHCP**: si el router le asigna otra, el administrador pierde el SSH. Reservar la IP en el router (decisión/acción física).
- **Cabeceras de banner** `Server: Kestrel` y `Via: 1.1 Caddy` revelan la pila; HSTS `max-age=2592000` (30 días) sin `includeSubDomains`. Informativo; no explotable por sí solo.
- **La CA no está instalada en el almacén de confianza de ningún equipo**: las pruebas fijaron la CA por archivo (`--cacert`). Instalarla en equipos reales modifica su almacén de confianza:
  `scripts/install/Install-ErpCa.ps1` con la huella de arriba (acción del propietario).

## 3. Qué cubre y qué no esta evidencia

Cubre: el servidor provisional tal como está hoy (Ubuntu + UFW + Docker + Caddy + ERP en Production) frente a un cliente de la LAN y a un origen no autorizado emulado en el propio servidor.
No cubre: un segundo equipo físico distinto de la LAN intentando el 22 (se emuló con un namespace; la regla `ufw` es por origen, así que el resultado es equivalente pero no es un segundo equipo),
el servidor definitivo ni Windows. **IPv6 sí se verificó**: `IPV6=yes` en ufw, `ip6tables` `INPUT`/`FORWARD` en DROP con cadenas `ufw6-*`, y desde este PC a la dirección link-local de la VM los puertos 22, 443, 80, 1433 y 8080 **no responden** (el 22 escucha en `[::]` pero queda cerrado a cualquier origen sin regla; 443 solo se publica en IPv4).

## 4. Pendiente (acceso remoto, separado; lo administra el propietario)

Tailscale real (cuenta/tailnet nueva solo para el ERP o la existente), aplicar `policy.hujson` y sus `tests`, nodo con `tag:erp`, expiración deshabilitada, *Device approval*, *Node sharing* off,
`--netfilter-mode=nodivert` (obligatorio en Linux), acceso desde dos clientes reales + tercera cuenta denegada + revocación real, router sin *port-forwarding*/CGNAT, CA instalada en los equipos
reales (y Firefox si se usa), servidor definitivo y Windows. Nada de esto se probó ni se simuló como real en esta tanda.

## 5. Estado propuesto para Notion

> **P-05 — FINALIZADO (alcance técnico local/LAN), 2026-11-20.** HTTPS/cadena verificados desde un cliente LAN; autorización por rol 27/27 contra la BD (7 roles × 23 rutas, escalada con formularios válidos y control positivo, CSRF);
> SSH solo desde 192.168.50.164 (ufw + prueba dinámica desde origen ajeno); firewall UFW/DOCKER-USER efectivo, solo 443 publicado, SQL/Kestrel inalcanzables; endpoints sensibles (Swagger, diagnóstico,
> métricas) 404 incluso para SuperAdmin; restricciones persisten tras reiniciar caddy/app y `ufw reload` (25/25). Sin defectos. **Pendiente separado (propietario):** Tailscale real y pruebas remotas, instalación de la CA en
> equipos reales, reserva de IP del administrador, decisión SSH solo-clave, servidor definitivo.
