# Staging / preproducción integrada — 2026-09-23 (cierre de reservas)

Continuación de la sesión de staging inicial de este mismo día (ver historial git de este
archivo / commit `8954707`). Objetivo de esta segunda pasada: cerrar las reservas registradas
en la aprobación con reservas — smoke funcional de negocio, 500 transitorio de SQL, backup de
archivos en Linux — antes de instalar el host definitivo. Rama de trabajo:
`staging-fixes-20260923` (base `7c79bcf`). **No se avanzó a producción.**

Todo lo marcado **VALIDADO** tiene evidencia real de esta sesión (logs de comando, capturas de
red de Playwright, consultas SQL directas). Todo lo marcado **NO PROBADO** o **NO COMPLETADO**
se deja así explícitamente.

## Resultado

## STAGING NO APROBADO — BLOCKER técnico nuevo encontrado (no relacionado a infraestructura)

Se cerraron las tres reservas técnicas de infraestructura de la sesión anterior (fix de
`--project`, 500 transitorio de SQL, backup de archivos en Linux), y se encontraron y
corrigieron dos bugs funcionales reales durante el smoke. Pero el smoke funcional profundo
reveló un **BLOCKER de aplicación no relacionado con nada de lo anterior**: el campo
`accionConfirmacion` no viaja en el POST final de `Venta/Edit` sin importar qué botón dispare
el submit (ni "Confirmar venta" simple ni el modal "Confirmar y facturar"), así que **toda
confirmación de venta cae al branch por defecto de `ProcesarAccionPostGuardadoAsync` y sólo
guarda los cambios — nunca confirma, nunca descuenta stock, nunca factura**. No se identificó
la causa raíz dentro del presupuesto de esta sesión (ver Findings). Esto bloquea el flujo
central de negocio (cerrar una venta) y no se puede recomendar "STAGING APROBADO" con esto
abierto, aunque la infraestructura (deploy, SQL, backup/restore, TLS, health gates) funcionó
de punta a punta.

## Git

```
branch:  staging-fixes-20260923
base:    7c79bcf84b4e39c3ba67a998e4c92754772bb0f6
commits (esta sesión, sobre 8954707 ya existente):
  0f02379 / 3a8b446 (amend) fix: responder 503 ante SqlException transitoria durante restart de SQL
  1d73de2 (amend previo)
  0fc85a5 fix: cotización sin cliente bloqueaba conversión a venta + botón de facturar deshabilitado
push:    pendiente (ver sección PR más abajo)
main:    intacto, sin tocar
```

## 1. Fix de deploy (`8954707`) — reverificado

**VALIDADO en vivo**, esta vez desde WSL Ubuntu real (no Git Bash/Windows) para descartar
cualquier interferencia de path-mangling en el propio mecanismo de verificación:

```
scripts/deploy/preflight.sh --image theburyproject/erp:20260923-staging-fixes --project bury-staging-fixes-20260923 → OK
scripts/deploy/deploy.sh    --image theburyproject/erp:20260923-staging-fixes --project bury-staging-fixes-20260923 → OK (db/db-init/migrate/app/health/smoke, ~71s)
```

`.deploy-state/bury-staging-fixes-20260923/` se creó correctamente aislado del proyecto
(confirmado con `ls`), sin colisión con otros proyectos activos en la misma máquina
(`bury-functional-20260922`, `bury-production-qa`, `bury-e2e-ci`).

## 2. SQL 500 transitorio — CAUSA ENCONTRADA, CORREGIDO, VERIFICADO EN VIVO

**Causa raíz real** (más precisa que la hipótesis de la sesión anterior): sin
`EnableRetryOnFailure`, EF Core no deja propagar el `SqlException` transitorio (error 4060,
"Cannot open database") directo — lo envuelve en un `System.InvalidOperationException`
("...consider enabling transient error resiliency...") antes de que llegue a
`PermissionClaimsTransformation`/`UseExceptionHandler`. Cualquier intento de atrapar
`SqlException` directo (mi primer intento de fix) no lo detecta.

**Fix**: `Middleware/TransientDbUnavailableMiddleware.cs`, registrado antes de
`UseAuthentication()`. Recorre la cadena de `InnerException` buscando un `SqlException` con un
código de error conocido como transitorio (4060 y ~15 códigos más de la misma familia que EF
Core reconoce). Si lo encuentra: responde `503 + Retry-After: 2`, logueado como warning.
Cualquier otra excepción (SQL no-transitoria o no-SQL) se re-lanza intacta — sin
`catch(Exception)` global silencioso.

**Reproducido y verificado en vivo dos veces** (`docker kill` + `docker start` sobre el
contenedor `db`, con requests autenticados concurrentes durante toda la ventana de
recuperación):

| Momento | `/Cliente` (autenticado) | `/health/live` | `/health/ready` |
|---|---|---|---|
| Antes del fix (sólo `catch(SqlException)` directo) | **500** × 16 requests seguidos, luego recupera solo | — | — |
| Después del fix (recorre `InnerException`) | **503 + Retry-After: 2** × 15 requests, luego 200 automático, sin reintento manual | **200 todo el tiempo** (el proceso nunca se cae) | **503** durante la ventana, **200** apenas SQL vuelve |

11 tests unitarios nuevos (`TransientDbUnavailableMiddlewareTests`), incluido el caso real
(SqlException envuelto en InvalidOperationException) que el primer intento de fix no cubría.

## 3. Backup / restore de archivos en Linux — CERRADO

Reproducido íntegramente en **WSL2 Ubuntu real** (no Git Bash/Windows), con Docker Desktop
integrado ahí (habilitado durante esta sesión). Mismo mecanismo de bind-mounts, mismo
`docker compose`, evitando por completo el bug de reescritura de paths POSIX→Windows de Git
Bash que bloqueaba `files-backup.sh`/`retention.sh` en la sesión anterior.

```
COMPOSE_PROJECT_NAME=bury-staging-fixes-20260923 BACKUP_DIR=~/staging-fixes-20260923/backups \
  scripts/backup/backup.sh all
```

- **SQL**: VALIDADO — `BACKUP DATABASE` real, 958.464 bytes, `RESTORE VERIFYONLY WITH CHECKSUM` OK, sha256 OK.
- **files**: **VALIDADO — ya no falla.** `files_2026-09-23_042742Z.tar.gz`, 2958 bytes, 22 entradas
  (`[keys uploads appdata caddy-data]`), sha256 OK. `app` se detuvo y volvió a levantar sola.
- **retention**: VALIDADO — corrió sin error (`0 archivo(s) borrado(s)`, esperado con 1 solo backup).
- **restore SQL**: VALIDADO en proyecto aislado nuevo (`bury-staging-restore-linux-20260923`,
  subred propia, volúmenes nuevos) — `RESTORE DATABASE` OK, `DBCC CHECKDB WITH NO_INFOMSGS`
  limpio (sin salida = sin errores), `__EFMigrationsHistory`=106, `AspNetUsers`=1 (coincide con
  el origen).
- **restore files**: VALIDADO en el mismo proyecto aislado — `files-restore.sh` restauró
  `[appdata caddy-data keys uploads]`; comparación de contenido byte a byte del volumen
  `bury-keys` restaurado contra el original: **sha256 idéntico**
  (`1e66f78415f53af43191da3ba617d3766072f2b045bc4ff7f45062de0d03922e`).

**Windows/Git Bash queda cerrado como limitación irrelevante para producción Linux real**, tal
como se documentó en la sesión anterior — confirmado ahora con evidencia positiva en Linux real
en vez de sólo la ausencia del bug.

## 4. Smoke funcional de negocio

Entorno: `bury-staging-fixes-20260923` (imagen `theburyproject/erp:20260923-staging-fixes`,
compilada desde esta rama con los 3 fixes de código incluidos), vía Caddy con TLS real
(CA interna de Caddy importada al store de usuario de Windows para que el navegador de
Playwright MCP confíe en el certificado — `staging.bury.local` mapeado a 127.0.0.1 en el hosts
del host por el usuario).

| Flujo | Resultado |
|---|---|
| Login (usuario/contraseña sintéticos) vía Caddy/TLS, aceptación de T&C | **VALIDADO** — llega a Dashboard, WebSocket de SignalR conectado en el login mismo |
| Crear cliente sintético (DNI 30111222, "Staging QA-Synthetic") | **VALIDADO — causa raíz de la sesión anterior confirmada**: `ClienteViewModel` requiere `Teléfono` y `Domicilio` (tab "Contacto"), no sólo Documento/Apellido/Nombre (tab "Personales"). No es un bug: cliente creado en el primer intento completando las 2 tabs. |
| Crear producto (código STG-001, categoría/marca vía autocomplete, precio, stock inicial) | **VALIDADO** — creado, aparece en catálogo. Observación: queda "Agotado" pese a `StockActual=20`; el catálogo parece trackear stock via unidades físicas, no el contador simple — no investigado a fondo (no bloqueante para el smoke). |
| Cotización (producto + envío a domicilio $2.500, simular, guardar) | **VALIDADO** — Total a cobrar = Productos + Envío = $159.800,00 (confirma `VentaMontos`/envío cobrable) |
| PDF de cotización | **VALIDADO** — descarga 200, contenido correcto: `$` formateado es-AR, línea de Envío, TOTAL correcto, sin `¤` |
| Convertir cotización → venta | **BUG REAL encontrado y corregido** (ver Findings F1) — antes: botón permanentemente deshabilitado aun eligiendo cliente; después: convierte y crea la Venta |
| Abrir caja (crear caja nueva + turno) | **VALIDADO** |
| Confirmar venta / Confirmar y facturar | **BUG REAL encontrado y parcialmente corregido, BLOCKER nuevo detrás** (ver Findings F2 y F3) — el botón del modal ya no queda deshabilitado, pero el POST final nunca confirma de verdad (cae al branch "guardar") |
| Stock (descuento al confirmar) | **NO VERIFICADO** — depende de F3 (la venta nunca llegó a confirmarse de verdad) |
| Crédito/contrato, PDF contrato, Excel, documentos (upload/download/replace/delete) | **NO COMPLETADO** — presupuesto de la sesión agotado en el diagnóstico de F1/F2/F3 |
| `/uploads/documentos-clientes/*` sin archivo real | **VALIDADO** (heredado, ya cerrado en sesión anterior, no re-tocado) |
| SignalR negotiate + WebSocket | **VALIDADO** — conectado automáticamente en cada carga de página autenticada (`wss://staging.bury.local/hubs/notificaciones`), visto en consola del navegador real en cada flujo de este smoke |
| SignalR evento real de negocio | **NO PROBADO** — no se llegó a un flujo que dispare una notificación verificable |

### Hallazgo adicional sin corregir (fuera de alcance de esta sesión)

Las vistas de Cotización (`Cotizacion/Detalles`, `Cotizacion/Listado`, modal "Convertir a
Venta") muestran los importes con el símbolo `¤` (placeholder ISO 4217 genérico) en vez de
`$` — visible en HTML pero **no** en el PDF (que sí muestra `$` correctamente), ni en el wizard
de Venta (`Venta/Edit` "Revisión" muestra `$` bien). Parece un problema de cultura/formato
específico de esas vistas de Cotización. No corregido (fuera del alcance explícito de esta
sesión: "no cambies nada más fuera de ese alcance").

## Findings

| Severidad | Hallazgo | Estado |
|---|---|---|
| **F0 — HIGH** | `scripts/deploy/lib.sh`: aislamiento de `DP_STATE_DIR` por `--project` | **FIJO** (commit previo `8954707`, reverificado en vivo esta sesión desde Linux real) |
| **F-SQL — MEDIUM→FIJO** | 500 crudo ante SqlException transitoria envuelta por EF Core durante restart de SQL | **FIJO** — ver sección 2. Verificado en vivo, antes/después, con probes automatizados |
| **F-BACKUP — INFO→FIJO** | `scripts/backup/backup.sh files`/`retention.sh` fallaban en Windows/Git Bash | **CERRADO** — reproducido exitosamente en Linux real (WSL2 Ubuntu); confirmado irrelevante para producción Linux |
| **F1 — HIGH→FIJO** | `CotizacionConversionService.PreviewConversionAsync` bloqueaba `Convertible` por cliente faltante sin importar el medio de pago, contradiciendo el propio flujo de "asignar cliente acá mismo" de la UI — botón "Confirmar Conversion" permanentemente deshabilitado | **FIJO**, test de regresión agregado, verificado en vivo |
| **F2 — HIGH→FIJO** | `venta-create.js`: el submit handler no respetaba `event.defaultPrevented`, así que deshabilitaba el botón del modal "Confirmar y facturar" recién abierto por otro listener — quedaba inutilizable para siempre | **FIJO**, verificado en vivo (antes: disabled permanente; después: clickeable, la request llega al servidor) |
| **F3 — BLOCKER, NO RESUELTO** | El campo `accionConfirmacion` del botón submit clickeado NO viaja en el body del POST a `Venta/Edit/{id}` — confirmado con ambos botones ("Confirmar venta" simple y "Confirmar y facturar" del modal), inspeccionando el body real de la request (`browser_network_request`). El resto de los campos del form sí viajan correctamente (incluido `tipoFactura=B` del mismo modal). El servidor cae siempre al branch `default` de `ProcesarAccionPostGuardadoAsync` ("Venta actualizada exitosamente", sin cambiar `Estado`). No se identificó la causa exacta (¿atributos del botón alterados por algún script?, ¿algo en el árbol DOM del wizard rompe la asociación submitter↔form?) dentro del presupuesto de esta sesión. Bloquea el flujo central de negocio: ninguna venta puede confirmarse, facturarse ni descontar stock desde este wizard tal como está. **No parece causado por F1 ni F2** (ambos cambios de este commit no tocan la serialización del formulario ni los atributos del botón). Requiere una sesión dedicada con más presupuesto para instrumentar el DOM real (o revisar con `git bisect` si es reciente). | **BLOCKER — abierto** |
| **F4 — LOW, no corregido** | Vistas de Cotización (Detalles/Listado/modal de conversión) muestran `¤` en vez de `$` | Documentado, fuera de alcance explícito de esta sesión |
| **F5 — INFO** | Producto con `StockActual=20` recién creado aparece "Agotado" en el catálogo | Documentado, no investigado a fondo, no bloqueante |

## Tests

```
dotnet build --configuration Release        → 0 errores, 0 warnings nuevos
dotnet test  --configuration Release        → 4989 passed / 0 failed / 2 skipped (E2E seed runners) / 4991 total
```

Incluye los 11 tests nuevos de `TransientDbUnavailableMiddlewareTests` y el test de regresión
`Preview_EfectivoSinCliente_EsConvertibleParaPermitirOverrideEnLaUi`. Se agregó también
`e2e/venta-confirmar-facturar-modal.spec.js` (Playwright) para F2, **no ejecutado** en esta
sesión (el `storageState` versionado apunta a otro entorno de desarrollo, no a este staging
ad-hoc) — la corrección de F2 se verificó a mano con Playwright MCP contra el staging real.

`git diff --check`: sin problemas de espacio en blanco. Secret scan manual del diff: sin
credenciales ni secretos.

## Fuera de alcance de esta sesión

- Crédito/contrato, PDF de contrato, Excel, documentos (upload/download/replace/delete de
  cliente), evento SignalR de negocio real, monitoring: no alcanzados — el presupuesto se
  concentró en diagnosticar F1/F2/F3, que son más severos (bloquean el flujo central de venta).
- F3 (BLOCKER) queda para una sesión dedicada.
- F4 (¤ en Cotización) y F5 (stock "Agotado") quedan documentados, no corregidos.
- AutoMapper: sigue GO-LIVE BLOCKER EXTERNO de negocio/legal, no tocado.
- Integraciones externas, VPN/LAN/TLS física del host: fuera de alcance, sin cambios.

## Confirmación

- No se hizo push directo a `main`; todo el trabajo de código quedó en `staging-fixes-20260923`.
- No se usó `git reset --hard`, `git restore .`, `git clean`, `git stash` ni `git rebase`.
- `docs/staging-preproduccion.md` (este archivo) se conservó y actualizó, nunca se borró.
- Dataset 100% sintético (cliente/producto/cotización/venta/caja creados en esta sesión,
  DNI/CUIT ficticios, sin datos reales).
- `.env` con secretos sintéticos generado con `openssl rand -base64`, gitignored, nunca
  commiteado (verificado con `git status .env` → vacío).

---

*Generado en la sesión de staging del 2026-09-23 (continuación). Evidencia: logs de
`deploy.sh`/`backup.sh`/`restore-sql.sh`/`restore-files.sh` desde WSL2 Ubuntu real, sesión de
navegador Playwright MCP real contra el stack de staging vía Caddy/TLS con inspección de
requests de red (`browser_network_request`), consultas `sqlcmd` directas, `dotnet test`.*
