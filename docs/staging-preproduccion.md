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

## STAGING APROBADO — BLOCKER F3 resuelto y verificado en vivo

Continuación de esta misma rama: el BLOCKER F3 (`accionConfirmacion` no viajaba en el POST
final de `Venta/Edit`) quedó **encontrado, corregido, cubierto con regresión y verificado en
vivo con ambos botones** ("Confirmar venta" y "Confirmar y facturar" del modal). Ver sección 3
y el finding F3 actualizado más abajo. Con esto se cierran los tres findings HIGH/BLOCKER de
esta rama (F1, F2, F3); la infraestructura (deploy, SQL, backup/restore, TLS, health gates) ya
había funcionado de punta a punta en la pasada anterior.

Quedan fuera de este bloque de trabajo, sin cambios: crédito/contrato, Excel, documentos
(upload/download/replace/delete), evento SignalR de negocio real y monitoring (F-CREDITO-EXCEL-DOC,
ver "Fuera de alcance") — no están relacionados con F3 y no se tocaron, tal como se pidió.

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
| Confirmar venta / Confirmar y facturar | **VALIDADO end-to-end** (ver sección 5 y Findings F2/F3) — ambos botones confirman de verdad: estado pasa a Confirmada/Facturada, se emite factura FA-B-... en el flujo "Confirmar y facturar" |
| Stock (descuento al confirmar) | **VALIDADO** — banner real del sistema tras "Confirmar y facturar": "Venta confirmada y facturada en un solo paso. El stock fue descontado." |
| Crédito/contrato, PDF contrato, Excel, documentos (upload/download/replace/delete) | **NO COMPLETADO** — presupuesto de la sesión agotado en el diagnóstico de F1/F2/F3 |
| `/uploads/documentos-clientes/*` sin archivo real | **VALIDADO** (heredado, ya cerrado en sesión anterior, no re-tocado) |
| SignalR negotiate + WebSocket | **VALIDADO** — conectado automáticamente en cada carga de página autenticada (`wss://staging.bury.local/hubs/notificaciones`), visto en consola del navegador real en cada flujo de este smoke |
| SignalR evento real de negocio | **NO PROBADO** — no se llegó a un flujo que dispare una notificación verificable |

## 5. BLOCKER F3 (`accionConfirmacion` no viaja en el POST) — RESUELTO

**Causa raíz encontrada**: en `wwwroot/js/venta-create.js`, el handler de `submit` del form
compartido (`#venta-form`, usado tanto por Create como por Edit) deshabilitaba **todos** los
`button[type="submit"]` del form como guarda anti-doble-envío — incluido el propio botón que
había disparado ese mismo submit (`event.submitter`). Deshabilitar el submitter *dentro* del
handler de `submit` hace que el navegador lo excluya del conjunto de datos del formulario en el
momento real de serializar la petición (la exclusión de campos `disabled` ocurre en el algoritmo
de envío del formulario, después de que corren los listeners de `submit`, no antes) — así que el
par `name="accionConfirmacion" value="..."` de ese botón nunca llegaba al servidor, sin importar
cuál de los tres botones se clickeara ("Confirmar venta", "Guardar sin confirmar" o "Confirmar y
facturar" del modal). El servidor caía siempre al branch `default` de
`ProcesarAccionPostGuardadoAsync` ("Venta actualizada exitosamente", sin cambiar `Estado`).

No relacionado con F1 ni F2 (ninguno de esos dos cambios toca este loop de deshabilitado).

**Fix** (`wwwroot/js/venta-create.js`, línea ~2733): el loop que deshabilita los botones excluye
ahora a `e.submitter` — el resto de los botones se sigue deshabilitando (la protección
anti-doble-envío queda intacta), pero el botón que disparó el submit queda habilitado y viaja en
el body de la petición real.

**Regresión agregada**: `e2e/venta-edit-confirmar-post-blocker.spec.js` (Playwright) — intercepta
el POST real a `/Venta/Edit/{id}`, verifica que `accionConfirmacion` viaje en el body, que la
venta termine en `/Venta/Details` con el mensaje "Venta confirmada" (no el branch `default` de
sólo-guardado), y que un intento posterior de volver a `Venta/Edit/{id}` sobre la venta ya
confirmada sea redirigido (guard de estado existente — no hay forma de re-disparar el confirm ni
de descontar stock una segunda vez por este camino).

**Verificado en vivo con Playwright MCP** contra `http://127.0.0.1:18787` (Development, LocalDB),
con ambos botones, sobre dos cotizaciones reales distintas:

| Botón | Antes del fix (sesión anterior) | Después del fix (esta sesión) |
|---|---|---|
| "Confirmar venta" (sin facturar) | Quedaba en el branch `default`, sin cambiar `Estado` | Estado → **Confirmada**, historial: "Venta confirmada · 23/09/2026 11:56", banner "Venta confirmada; ya podés emitir la factura desde Acciones." |
| "Confirmar y facturar" (checkbox + modal) | Igual — el modal ya no quedaba deshabilitado (F2) pero el POST final tampoco confirmaba | Estado → **Facturada**, factura `FA-B-202609-000001` emitida, banner del sistema: "Venta confirmada y facturada en un solo paso. El stock fue descontado." |
| Re-editar la venta ya confirmada | — | `GET /Venta/Edit/{id}` redirige a `/Venta/Details/{id}` (guard de estado `ValidarEstadoParaEdicion`) — confirma que no hay forma de duplicar el descuento de stock reintentando este flujo |

**Tests**: 1697/1697 focalizados (Venta/Caja/Stock) y 4989/4991 de la suite completa Release
(2 omitidos = seed runners intencionales), 0 rojos, antes y después del fix.

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
| **F3 — BLOCKER→FIJO** | El campo `accionConfirmacion` del botón submit clickeado NO viajaba en el body del POST a `Venta/Edit/{id}`, con ningún botón. Causa raíz: `venta-create.js` deshabilitaba el propio `event.submitter` dentro de su handler de `submit`, y el navegador excluye los campos `disabled` al serializar la petición real. Ver sección 5. | **FIJO**, regresión Playwright agregada, verificado en vivo con ambos botones (Confirmar venta → Confirmada; Confirmar y facturar → Facturada + stock descontado) |
| **F4 — LOW, no corregido** | Vistas de Cotización (Detalles/Listado/modal de conversión) muestran `¤` en vez de `$` | Documentado, fuera de alcance explícito de esta sesión |
| **F5 — INFO** | Producto con `StockActual=20` recién creado aparece "Agotado" en el catálogo | Documentado, no investigado a fondo, no bloqueante |

## Tests

```
dotnet build --configuration Release        → 0 errores, 0 warnings nuevos
dotnet test  --configuration Release        → 4989 passed / 0 failed / 2 skipped (E2E seed runners) / 4991 total
```

Incluye los 11 tests nuevos de `TransientDbUnavailableMiddlewareTests` y el test de regresión
`Preview_EfectivoSinCliente_EsConvertibleParaPermitirOverrideEnLaUi`. Se agregaron también
`e2e/venta-confirmar-facturar-modal.spec.js` (F2) y `e2e/venta-edit-confirmar-post-blocker.spec.js`
(F3), **no ejecutados con datos reales de staging** en esta sesión (el `storageState`/dataset de
staging no tiene un cliente que matchee el término de búsqueda `'an'` que usan los helpers
compartidos; el spec sí corre y pasa su fase de autenticación, sólo se salta el escenario por
falta de dato de QA) — ambas correcciones (F2 y F3) se verificaron a mano con Playwright MCP
contra la app real (staging para F2 en la sesión anterior, Development/LocalDB para F3 en ésta).

`git diff --check`: sin problemas de espacio en blanco. Secret scan manual del diff: sin
credenciales ni secretos.

## Fuera de alcance de esta sesión

- Crédito/contrato, PDF de contrato, Excel, documentos (upload/download/replace/delete de
  cliente), evento SignalR de negocio real, monitoring: siguen sin probarse — el objetivo único
  de este bloque de trabajo fue F3 (BLOCKER), y ninguno de estos ítems resultó causado por él.
  Quedan para una sesión dedicada de smoke funcional, como ya estaba documentado.
- F4 (¤ en Cotización) y F5 (stock "Agotado") quedan documentados, no corregidos — no resultaron
  causados por F3.
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
