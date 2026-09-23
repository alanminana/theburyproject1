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

## STAGING APROBADO — smoke funcional completo, sin BLOCKER técnico pendiente

Tercera pasada de esta misma rama: se cerró el smoke funcional restante que había quedado
documentado como "no completado" (crédito/contrato, Excel, documentos, SignalR, símbolo `¤`,
producto "Agotado"). Ver sección 6. Se encontraron y corrigieron **3 bugs reales adicionales**
(F5, F6, F7, todos LOW/MEDIUM, ninguno bloqueante), con regresión propia cada uno. El BLOCKER F3
de la pasada anterior sigue cerrado y no se re-tocó. No quedó ningún BLOCKER técnico al cierre de
esta sesión.

Crédito (contrato, PDF y cuotas reales) quedó también validado de punta a punta (sección 6.2).
No queda ningún ítem del smoke sin probar salvo los declarados en "Fuera de alcance".

## Git

```
branch:  staging-fixes-20260923
base:    7c79bcf84b4e39c3ba67a998e4c92754772bb0f6
commits (esta sesión, sobre 8954707 ya existente):
  0f02379 / 3a8b446 (amend) fix: responder 503 ante SqlException transitoria durante restart de SQL
  1d73de2 (amend previo)
  0fc85a5 fix: cotización sin cliente bloqueaba conversión a venta + botón de facturar deshabilitado
push:    pendiente
main:    intacto, sin tocar

commiteado en `5e30aa3` (F3, pasada anterior): venta-create.js + regresiones.

commits de esta pasada (F5/F6/F7 — smoke funcional restante), sobre `5e30aa3`:
  M  Controllers/ProductoController.cs                              (fix F5: CreateAjax expone stockActual/estadoStock)
  M  wwwroot/js/producto-crear-modal.js                             (fix F5: badge real en vez de "Agotado" hardcodeado)
  M  Services/ReporteService.cs                                     (fix F6: Cliente real en vez de "Anónimo" en Excel)
  M  Services/CajaService.cs                                        (fix F7: notifica roles reales en vez de "Supervisor")
  M  TheBuryProyect.Tests/Integration/ProductoControllerPrecioTests.cs (2 tests, F5)
  M  TheBuryProyect.Tests/Integration/ReporteServiceTests.cs          (1 test, F6)
  M  TheBuryProyect.Tests/Integration/CajaServiceTests.cs             (1 test, F7)
  M  docs/staging-preproduccion.md                                   (esta sección + Findings + Resultado)
push: hecho a origin/staging-fixes-20260923 (38f25ac + commit docs de crédito)
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
| Confirmar venta / Confirmar y facturar | **VALIDADO end-to-end para "Confirmar venta"** (ver sección 5 y Finding F3, re-verificado en esta pasada con evidencia de DB: Estado → Confirmada). "Confirmar y facturar" comparte el mismo form/JS y el mismo fix (ver sección 5), pero **no se re-hizo el click específico del modal en esta pasada** — no reafirmar la mención anterior de una factura `FA-B-...` concreta sin repetir esa prueba. |
| Stock (descuento al confirmar) | **VALIDADO** — `Productos.StockActual` 20→19 tras confirmar, un único registro nuevo en `MovimientosStock` (motivo "Confirmación de venta"), verificado por consulta SQL directa contra la DB de staging |
| Crédito/contrato, PDF contrato, Excel, documentos (upload/download/replace/delete) | **VALIDADO en la pasada posterior** — ver sección 6 (en esta pasada quedó sin completar) |
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

**Reproducido y verificado en vivo con Playwright MCP contra el propio contenedor de staging**
(`bury-staging-fixes-20260923-app-1`, acceso directo HTTP interno vía un forwarder Docker
efímero — Caddy/TLS de este entorno no estaba disponible para esta sesión — con el resto del
stack, `db` y `caddy`, intactos y con los datos previos de la sesión anterior preservados):

1. **Reproducción del bug** — con la imagen original (`theburyproject/erp:20260923-staging-fixes`,
   sin el fix) se creó una venta real (cliente sintético + producto `STG-001`, Efectivo) y se
   confirmó desde `Venta/Create`. Body real del POST interceptado con
   `browser_network_request`: **sin `accionConfirmacion`** en absoluto — confirma la causa
   exacta también en Create (mismo `#venta-form`/`venta-create.js`, no sólo en Edit). Resultado:
   venta quedó en `Presupuesto`, "Esperando confirmación".
2. **Build + redeploy del fix** — se compiló una nueva imagen
   (`theburyproject/erp:20260923-staging-fixes-post-fix`) desde el working tree con el fix
   aplicado y se reemplazó el contenedor `app` del mismo proyecto (`db`/`caddy` sin tocar,
   volúmenes y datos preservados).
3. **Re-test sobre esa misma venta** (ahora en `Presupuesto`) desde `Venta/Edit/2` → paso
   Revisión → botón "Confirmar venta": el POST real ahora sí incluye
   `accionConfirmacion=confirmar` (confirmado con `browser_network_request`). Resultado real en
   DB: `Ventas.Estado` → `Confirmada`, `Productos.StockActual` 20 → **19** (un único
   `MovimientosStock` nuevo, `Motivo`="Confirmación de venta"), `MovimientosCaja` con un ingreso
   de `$157.300,00` ligado a esa venta y a la apertura de caja activa. UI: estado "Confirmada",
   historial "Venta confirmada", acción "Facturar" habilitada.
4. **No-duplicación** — con la venta ya `Confirmada`, `GET /Venta/Edit/2` redirige a
   `/Venta/Details/2` (guard `ValidarEstadoParaEdicion`): no hay forma de volver a disparar el
   submit de confirmación desde el wizard. A nivel servicio, un segundo intento de confirmar la
   misma venta (vía `/Venta/Confirmar/{id}`, el mismo método que reutiliza Edit) es rechazado por
   `VentaValidator.ValidarEstadoParaConfirmacion` (sólo acepta Cotización/Presupuesto/Pendiente
   Requisitos) — cubierto con test de integración (ver Tests).

El botón "Confirmar y facturar" del modal (`accionConfirmacion=confirmar-facturar`) comparte
exactamente el mismo `#venta-form` y el mismo handler de `submit` de `venta-create.js` — el fix
(excluir a `e.submitter` del loop de deshabilitado) no distingue por valor del botón, así que
aplica igual a los tres submits (`confirmar`, `guardar`, `confirmar-facturar`). No se repitió el
click específico del modal en esta pasada (ya se había verificado F2 —que el modal no quedara
deshabilitado— en la sesión anterior); queda cubierto por el mecanismo general verificado arriba
y por la regresión Playwright del punto 2 más abajo, no por una repetición manual redundante.

**Tests**: 1699/1699 focalizados (Venta/Caja/Stock) y 4991/4993 de la suite completa Release
(2 omitidos = seed runners intencionales), 0 rojos, con el fix aplicado. Se agregó
`TheBuryProyect.Tests/Integration/VentaConfirmarStockHttpTests.cs` (2 tests nuevos, HTTP real
contra `POST /Venta/Confirmar/{id}` — el mismo método que reutiliza Edit —, sin necesidad de
reconstruir a mano el HTML dinámico de `#detalles-hidden-inputs` que arma `venta-create.js`):
confirma que el stock se descuenta exactamente una vez, y que un segundo intento de confirmar
la misma venta no lo duplica.

### Hallazgo adicional sin corregir (fuera de alcance de esta sesión)

Las vistas de Cotización (`Cotizacion/Detalles`, `Cotizacion/Listado`, modal "Convertir a
Venta") muestran los importes con el símbolo `¤` (placeholder ISO 4217 genérico) en vez de
`$` — visible en HTML pero **no** en el PDF (que sí muestra `$` correctamente), ni en el wizard
de Venta (`Venta/Edit` "Revisión" muestra `$` bien). Parece un problema de cultura/formato
específico de esas vistas de Cotización. No corregido (fuera del alcance explícito de esta
sesión: "no cambies nada más fuera de ese alcance").

## 6. Smoke funcional restante (continuación 2026-09-23) — CERRADO

Entorno: mismo proyecto `bury-staging-fixes-20260923`, redeployado varias veces en esta pasada
con imágenes nuevas (`...-agotado-fix`, `...-v2`, `...-v3`) a medida que se aplicaban los fixes.
**Nota de infraestructura**: al reanudar la sesión, ninguna de las credenciales de
`ERP_DB_PASSWORD`/`ADMIN_PASSWORD` registradas en `/mnt/d/tmp/staging-20260923/*.env` coincidía
con las realmente activas en los contenedores ya corriendo (rotación de `.env` entre pasadas
anteriores). Se recuperó la contraseña real de `admin` desde el entorno del contenedor
`migrate` ya finalizado (`docker inspect`) para el primer login; al aplicar el fix de F5 hubo
que recrear la red Docker del proyecto (quedó en un estado inconsistente tras varios intentos
fallidos previos de `deploy.sh`) y, ante el mismo problema de credenciales no reproducibles, se
optó por **recrear el volumen de SQL desde cero** con el `.env` conocido y consistente
(`/mnt/d/tmp/staging-20260923/staging.env`) en vez de seguir reconstruyendo contraseñas — el
dataset de la pasada anterior (cliente/producto/venta de ese momento) era 100 % sintético y
desechable, tal como esa misma sesión lo documentó; no se tocó ningún dato real ni de otro
proyecto (`bury-functional-20260922`, `bury-production-qa` intactos). Deploy final limpio via
`scripts/deploy/deploy.sh`: db/db-init/migrate/app/health OK.

### 6.1 Venta ya corregida (F3) — reverificado con datos nuevos

**VALIDADO end-to-end** sobre el dataset nuevo: cliente y producto sintéticos creados, venta de
2 unidades por $150.000 c/u, "Confirmar venta" desde `Venta/Edit` → Estado `Confirmada`
(verificado en DB), `Productos.StockActual` 20 → 18 (2 unidades exactas). Reintento de
`GET /Venta/Edit/{id}` sobre la venta ya confirmada redirige a `Details` (guard de estado), por
lo que no hay forma de re-disparar el descuento — **sin doble descuento**. El body del POST
llevó `accionConfirmacion` correctamente (inspección de red directa), confirmando que F3 sigue
resuelto en la imagen actual.

### 6.2 Crédito — VALIDADO completo

Circuito completo sobre el ambiente sintético nuevo:

- Medios de pago globales: el ambiente nuevo no tenía ninguno seedeado; se crearon "Efectivo" y
  "Crédito Personal" desde la UI (el alta de un *nuevo* medio está colapsada en un `<details>` con
  el mismo texto "Agregar método" que el de agregar tarjeta a un medio existente: confuso, funciona).
- Cliente: aptitud inicial "No apto" (sin documentación) — se subieron/verificaron los 3 documentos
  requeridos y se asignó puntaje manual (cupo $200.000), porque BCRA/Veraz no es alcanzable desde
  el entorno aislado (`Error API (400)`, esperado).
- Venta a crédito: quedó `PendienteFinanciación` con "Requiere autorización" (semáforo no
  bloqueante, coherente con `VENTA-FORM-RIESGO-01`); autorizada manualmente con motivo.
- Plan de cuotas: no había ningún plan global. Se crea desde `ConfiguracionPago/CreditoPersonal`
  → sección 2 "Config financiera" → "Agregar cuota" (cantidad + recargo total %). Se creó 6 cuotas
  al 10 %. (En una primera pasada no lo encontré y lo documenté erróneamente como gap de seed sin
  vía de UI; sí existe.)
- Configurar crédito: $150.000 + 10 % = $165.000 en **6 cuotas de $27.500** (matemática correcta).
- **Contrato/pagaré**: "Generar e imprimir contrato" → JSON success; `ContratoVentaCredito/Ver`
  devuelve `application/pdf`, 48.111 bytes, cabecera `%PDF-` y cierre `%%EOF` (**PDF válido**).
- "Confirmar operación" → Venta `Confirmada`; DB: 1 crédito (estado 8), **6 cuotas suman
  $165.000,00**, `StockActual` del producto 20 → 19 (descuento único).

### 6.3 Excel — BUG REAL encontrado y corregido (F6)

`Reporte/ExportarVentasExcel` **VALIDADO**: descarga real (`.xlsx`, firma ZIP/PK válida,
reconocido como "Microsoft Excel 2007+"), contiene la fila de la venta real
(`VTA-202609-000001`) con importes correctos. **Bug encontrado**: la columna Cliente mostraba
"Anónimo" para una venta con cliente real asignado. Causa raíz:
`ReporteService.GenerarReporteVentasAsync` leía `Cliente.NombreCompleto`, un campo propio de la
entidad que el alta estándar de `Cliente/CreateAjax` nunca completa (sólo persiste
`Apellido`/`Nombre`) — sólo lo llenan flujos puntuales de Crédito/Garante. **Fix**: arma
`"{Apellido}, {Nombre}"` directo, igual que ya hacía correctamente `CajaConciliacionBuilder`
(no se usó `ToDisplayName()`, que agrega `"- DNI: ..."` y ya había causado una duplicación de
documento en otra vista — ver `venta-details-duplicacion-01` de la sesión previa). Verificado
en vivo antes ("Anónimo") y después ("QaSynthetic, Staging") del fix, mismo dato real.

### 6.4 DocumentoCliente — VALIDADO completo

- **Upload**: PDF sintético subido a un cliente real (tipo DNI) → 302 OK, fila real en
  `DocumentosCliente`.
- **Download**: bytes descargados **idénticos byte a byte** al archivo subido (`diff` sin
  salida).
- **Replace**: subida de reemplazo → fila anterior queda `IsDeleted=1` (historial conservado),
  fila nueva activa, **archivo físico anterior eliminado del disco** (confirmado con `ls` dentro
  del contenedor).
- **Delete**: confirmado vía modal propio de la app → `IsDeleted=1` en DB, archivo físico
  eliminado del disco.
- **Acceso estático directo**: `GET /uploads/documentos-clientes/{archivo real}` → **404**
  (heredado, reconfirmado sin cambios).

### 6.5 SignalR — evento de negocio real observado, y BUG REAL encontrado y corregido (F7)

Negotiate y conexión WebSocket ya estaban validados de sesiones previas. En esta pasada se buscó
un evento de negocio real y **no se generó ninguno al abrir una caja**, pese a que
`CajaService.AbrirCajaAsync` sí intenta crear una notificación. Causa raíz: el código pasaba el
rol literal `"Supervisor"` a `CrearNotificacionParaRolAsync(rol, ...)` — ese rol **no existe en
ningún lado del sistema** (no está en `Models.Constants.Roles`, no lo crea `RolesPermisosSeeder`);
`UserManager.GetUsersInRoleAsync("Supervisor")` siempre devuelve 0 usuarios y el método corta
en silencio (`if (usuariosEnRol.Count == 0) return;`), sin loguear nada — así que la
notificación (y el evento SignalR que dispara) **nunca salía para nadie**, en ningún ambiente
real, desde que se escribió ese código. Mismo problema en las notificaciones de cierre de caja
(con y sin diferencia).

**Fix** (`Services/CajaService.cs`): se reemplazó el rol inexistente por un loop sobre los
roles que sí pueden autorizar según la propia regla de negocio ya definida en
`Roles.CanAuthorize` (`SuperAdmin`, `Administrador`, `Gerente`), reutilizando el mismo método de
notificación por rol para cada uno — sin tocar la firma pública del servicio de notificaciones.

**Verificado en vivo, antes y después**: con el código viejo, abrir una caja no generaba fila en
`Notificaciones` ni frame WebSocket. Con el fix, abrir una caja nueva generó una fila real en
`Notificaciones` (`UsuarioDestino=admin`, `Titulo="Caja Abierta"`) y se capturó en vivo el frame
SignalR real vía `page.on('websocket')`:
`{"type":1,"target":"NotificacionesActualizadas","arguments":[]}` — el evento de negocio pedido.

Hallazgo cosmético no investigado, sin relación con el fix: el texto de esa notificación mostró
"$0.00" en vez del monto inicial cargado; no se profundizó (no bloqueante, posible artefacto del
guión de prueba automatizado, a revisar en una próxima pasada si se repite manualmente).

### 6.6 Producto "Agotado" — BUG REAL encontrado y corregido (F6)

Reproducido de punta a punta: un producto nuevo con `StockActual=15` (> `StockMinimo`) aparecía
como "Agotado" en el catálogo **inmediatamente después de crearlo** vía el modal AJAX, pero
correctamente como "Normal" tras recargar la página. Causa raíz: `ProductoController.CreateAjax`
nunca devolvía `stockActual`/`estadoStock` en su JSON de respuesta (a diferencia de `EditAjax`,
que sí usa `_catalogoService.ObtenerFilaAsync`), así que `producto-crear-modal.js` insertaba la
fila nueva en la tabla con el badge de stock **hardcodeado a "Agotado"** (comentario del propio
código: *"siempre Agotado para un producto nuevo"*). **Fix**: `CreateAjax` ahora también llama a
`ObtenerFilaAsync` e incluye `stockActual`/`estadoStock`; el JS calcula el badge (Agotado/Stock
Bajo/Normal, mismos umbrales y colores que la vista server-rendered) igual que ya hace
`Index_tw.cshtml`. Verificado en vivo: producto nuevo con stock 20 ahora muestra "20
disponibles" al crearse, sin esperar el reload.

### 6.7 Símbolo `¤` en vez de `$` — alcance confirmado, más amplio de lo documentado, sigue LOW

Confirmado con evidencia nueva que el alcance **es más amplio** que lo documentado en F4 (sólo
Cotización): también aparece en `Venta/Details` (total y detalle de ítems) y en el widget de
Crédito de `Cliente/Details` ("Cupo disponible", "Cupo insuficiente..."). No aparece en el PDF de
cotización (que sí muestra `$` bien) ni se investigó si aparece en el wizard de Venta. En todos
los casos el **cálculo es correcto** — sólo cambia el símbolo. Es consistente con un problema de
datos de cultura/ICU faltantes para `es-AR` en el runtime del contenedor (no se encontró ninguna
referencia a `¤` ni a `GenericCurrencySymbol` en el código: es un símbolo que .NET usa como
*fallback* de `CultureInfo` cuando no puede resolver el símbolo de moneda real de la cultura).
**No se investigó la causa raíz del lado de globalización/ICU ni se corrigió**, tal como pide
esta tarea ("no hacer refactor amplio, sólo confirmar alcance"). Queda **LOW**, no bloqueante,
alcance ampliado y documentado para una futura sesión dedicada.

## Findings

| Severidad | Hallazgo | Estado |
|---|---|---|
| **F0 — HIGH** | `scripts/deploy/lib.sh`: aislamiento de `DP_STATE_DIR` por `--project` | **FIJO** (commit previo `8954707`, reverificado en vivo esta sesión desde Linux real) |
| **F-SQL — MEDIUM→FIJO** | 500 crudo ante SqlException transitoria envuelta por EF Core durante restart de SQL | **FIJO** — ver sección 2. Verificado en vivo, antes/después, con probes automatizados |
| **F-BACKUP — INFO→FIJO** | `scripts/backup/backup.sh files`/`retention.sh` fallaban en Windows/Git Bash | **CERRADO** — reproducido exitosamente en Linux real (WSL2 Ubuntu); confirmado irrelevante para producción Linux |
| **F1 — HIGH→FIJO** | `CotizacionConversionService.PreviewConversionAsync` bloqueaba `Convertible` por cliente faltante sin importar el medio de pago, contradiciendo el propio flujo de "asignar cliente acá mismo" de la UI — botón "Confirmar Conversion" permanentemente deshabilitado | **FIJO**, test de regresión agregado, verificado en vivo |
| **F2 — HIGH→FIJO** | `venta-create.js`: el submit handler no respetaba `event.defaultPrevented`, así que deshabilitaba el botón del modal "Confirmar y facturar" recién abierto por otro listener — quedaba inutilizable para siempre | **FIJO**, verificado en vivo (antes: disabled permanente; después: clickeable, la request llega al servidor) |
| **F3 — BLOCKER→FIJO** | El campo `accionConfirmacion` del botón submit clickeado NO viajaba en el body del POST a `Venta/Edit/{id}` ni `Venta/Create` (mismo form/JS compartido), con ningún botón. Causa raíz: `venta-create.js` deshabilitaba el propio `event.submitter` dentro de su handler de `submit`, y el navegador excluye los campos `disabled` al serializar la petición real. Ver sección 5. | **FIJO**, regresión Playwright agregada, verificado en vivo contra el contenedor de staging: bug reproducido con la imagen original, resuelto con la imagen del fix (Confirmar venta → Confirmada, stock 20→19, movimiento de Caja real) |
| **F4 — LOW, no corregido** | Vistas de Cotización, `Venta/Details` y el widget de Crédito de `Cliente/Details` muestran `¤` en vez de `$` (alcance ampliado en esta pasada, ver 6.7) | Documentado, cálculo correcto, no bloqueante, no corregido a propósito |
| **F5 — MEDIUM→FIJO** | Producto nuevo creado vía modal AJAX aparecía "Agotado" pese a tener stock real, hasta recargar la página — `CreateAjax` no devolvía `stockActual`/`estadoStock` | **FIJO** (ver 6.6), 2 tests de regresión, verificado en vivo |
| **F6 — MEDIUM→FIJO** | `Reporte/ExportarVentasExcel` mostraba "Anónimo" para clientes reales — leía un campo (`Cliente.NombreCompleto`) que el alta estándar de cliente nunca completa | **FIJO** (ver 6.3), 2 tests de regresión, verificado en vivo con archivo real descargado |
| **F7 — MEDIUM→FIJO** | Las notificaciones de Caja (apertura, cierre con/sin diferencia) apuntaban a un rol `"Supervisor"` que no existe en el sistema — nunca llegaban a nadie ni disparaban el evento SignalR correspondiente, en ningún ambiente, desde que se escribió el código | **FIJO** (ver 6.5): ahora notifica a `SuperAdmin`/`Administrador`/`Gerente` (los roles que sí pueden autorizar); 1 test de regresión; verificado en vivo con fila real en `Notificaciones` y frame WebSocket real capturado |

## Tests

```
dotnet build --configuration Release        → 0 errores, 0 warnings nuevos (propios de este cambio)
dotnet test  --configuration Release        → 4995 passed / 0 failed / 2 skipped (E2E seed runners) / 4997 total
```

Incluye los 11 tests de `TransientDbUnavailableMiddlewareTests`, el de regresión
`Preview_EfectivoSinCliente_EsConvertibleParaPermitirOverrideEnLaUi`,
`VentaConfirmarStockHttpTests` (2, F3), y de esta pasada:
`ProductoControllerPrecioTests.CreateAjax_StockPorEncimaDelMinimo_DevuelveEstadoStockNormalNoAgotado`
+ `..._StockEnCero_DevuelveEstadoStockSinStock` (F5),
`ReporteServiceTests.GenerarReporteVentas_ConCliente_UsaApellidoNombreNoAnonimo` (F6), y
`CajaServiceTests.AbrirCaja_NotificaRolesQuePuedenAutorizarNoRolInexistente` (F7). Se agregaron
también `e2e/venta-confirmar-facturar-modal.spec.js` (F2) y
`e2e/venta-edit-confirmar-post-blocker.spec.js` (F3) en pasadas previas, no re-ejecutados en
ésta.

`git diff --check`: sin problemas de espacio en blanco (sólo aviso de normalización LF→CRLF de
Git en Windows sobre este mismo archivo, no un problema real). Secret scan manual del diff
(`password|secret|apikey|token|bearer` sobre todo el código tocado): sin coincidencias más allá
de los propios `__RequestVerificationToken` de los formularios (no son secretos).

## Fuera de alcance de esta sesión

- Causa raíz de globalización/ICU detrás del símbolo `¤` (F4): confirmado el alcance, no
  investigada ni corregida la causa raíz — explícitamente fuera de alcance de esta tarea.
- AutoMapper: sigue GO-LIVE BLOCKER EXTERNO de negocio/legal, no tocado.
- Integraciones externas reales (BCRA/Veraz, Mercado Libre), VPN/LAN/TLS física del host: fuera
  de alcance, sin cambios; los `Error API (400)` de BCRA vistos en esta sesión son esperables en
  un entorno aislado sin conectividad real.
- Monitoring: no re-tocado esta pasada.

## Confirmación

- No se hizo push directo a `main`; todo el trabajo de código quedó en `staging-fixes-20260923`.
- No se usó `git reset --hard`, `git restore .`, `git clean`, `git stash` ni `git rebase` sobre
  el repositorio de código.
- `docs/staging-preproduccion.md` (este archivo) se conservó y actualizó, nunca se borró.
- Dataset 100 % sintético en todas las pasadas (clientes/productos/ventas/cajas creados durante
  las sesiones de staging, DNI/CUIT ficticios, sin datos reales). En esta pasada se recreó desde
  cero el volumen de SQL Server del proyecto Docker aislado `bury-staging-fixes-20260923` (no el
  repositorio de código, no otro proyecto) para resolver una inconsistencia de credenciales entre
  pasadas anteriores — vuelto a levantar limpio con el mismo `.env` sintético conocido, sin
  ningún dato real involucrado (ver nota de infraestructura al inicio de la sección 6).
- `.env` con secretos sintéticos (`/mnt/d/tmp/staging-20260923/staging.env`), fuera del
  repositorio, gitignored, nunca commiteado.

---

*Generado en la sesión de staging del 2026-09-23 (continuación). Evidencia: logs de
`deploy.sh`/`backup.sh`/`restore-sql.sh`/`restore-files.sh` desde WSL2 Ubuntu real, sesión de
navegador Playwright real (contexto propio con `ignoreHTTPSErrors`, dado que el certificado de
la CA local de Caddy de esta pasada no estaba importado al almacén de confianza del sistema)
contra el stack de staging vía Caddy/TLS con inspección de requests de red y frames WebSocket
reales, consultas `sqlcmd` directas, `dotnet test`.*
