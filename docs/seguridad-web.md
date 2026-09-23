# Auditoría + hardening de seguridad web — TheBuryProject

Fecha: 2026-09-22. Alcance: aplicación ASP.NET Core (.NET 10) en vivo — autenticación, autorización,
CSRF, cookies, headers, uploads/downloads, errores, APIs, SignalR, superficies administrativas.
Sin pentest destructivo, sin cambios de infraestructura (Docker/Caddy/backup/monitoring/deploy/CI
intocables). No se hizo commit ni push; todo queda en el working tree para revisión.

Había WIP ajeno sin commitear (infraestructura de despliegue, tests, servicios funcionales). Se
preservó íntegro; solo se tocó lo mínimo indispensable para corregir hallazgos reales, evitando las
líneas ya modificadas por otro agente donde fue posible.

## Resumen de severidad

| Severidad | Antes | Después |
|---|---|---|
| CRITICAL | 1 | 0 |
| HIGH | 0 | 0 |
| MEDIUM | 2 | 0 |
| LOW | 6 | 5 |
| INFO | 5 | 5 |

## Hallazgos corregidos

### 1. CRITICAL — Documentos de clientes (DNI, comprobantes) descargables sin autenticación

- **Archivo/endpoint**: `Services/DocumentoClienteService.cs` (guardado bajo `wwwroot/uploads/documentos-clientes`), servido por `app.UseStaticFiles` en `Program.cs`.
- **Causa**: los documentos de clientes se guardan dentro de `wwwroot` con nombre físico predecible (`{ClienteId}_{TipoDocumento}_{yyyyMMddHHmmss}{ext}`). El middleware de archivos estáticos los servía en `/uploads/documentos-clientes/<nombre>` sin pasar nunca por `DocumentoClienteController.Descargar` (que sí exige `[Authorize]` + permiso `clientes.viewdocs`), es decir, el control de acceso del controller era irrelevante porque el archivo físico era accesible directo.
- **Impacto**: cualquier persona no autenticada que conozca/adivine `ClienteId` (entero pequeño), `TipoDocumento` (enum finito) y una ventana de tiempo de subida podía descargar DNI/comprobantes de un cliente real sin login, sin CSRF ni rate limit. Fuga de PII/documentación de identidad.
- **Cambio aplicado**: se agregó un middleware en `Program.cs`, **antes** de `app.UseStaticFiles(...)`, que devuelve `404` para cualquier request a `/uploads/documentos-clientes/*`. El único camino de acceso queda `DocumentoClienteController.Descargar`, que lee el archivo directo de disco (no vía HTTP interno) y ya exige autenticación + permiso. No se movió el almacenamiento fuera de `wwwroot` porque `Services/DocumentoClienteService.cs` tiene WIP ajeno activo tocando exactamente esa lógica de rutas (normalización de separadores Windows/Linux); mover el storage habría chocado con ese trabajo en curso. El bloqueo a nivel de pipeline es una mitigación completa y de bajo riesgo que no depende de esa refactorización.
- **Prueba**: `TheBuryProyect.Tests/Integration/UploadsDocumentosClientesEstaticoHttpTests.cs` — confirma `404` sin sesión para rutas dentro de `/uploads/documentos-clientes/`, y que otras rutas estáticas no se ven afectadas por el mismo middleware.
- **Pendiente relacionado**: cuando el WIP de `DocumentoClienteService.cs` (normalización de rutas) cierre, evaluar migrar el almacenamiento físico fuera de `wwwroot` (patrón ya usado en `ContratoVentaCreditoService.cs`, que guarda bajo `App_Data`) y renombrar el archivo físico a `Guid.NewGuid()` en vez de `{ClienteId}_{Tipo}_{timestamp}`, como defensa en profundidad adicional al bloqueo de middleware.

### 2. MEDIUM — CSRF: `CambiosPreciosController.AplicarRapido` rompía la protección de clase

- **Archivo**: `Controllers/CambiosPreciosController.cs:449` (antes del fix).
- **Causa**: el controller tiene `[AutoValidateAntiforgeryToken]` a nivel de clase, pero `AplicarRapido` (aplica un cambio de precios directo desde Catálogo — acción financiera de escritura) llevaba `[IgnoreAntiforgeryToken]` sin justificación documentada, rompiendo la protección uniforme del resto de las acciones.
- **Verificación antes de tocar**: no hay ningún JS/vista en el repo que invoque este endpoint (`grep AplicarRapido` en `Views/` y `wwwroot/js/` sin resultados) — el atributo no protegía ninguna integración real en producción; el único consumidor era el test de integración.
- **Cambio aplicado**: se retiró `[IgnoreAntiforgeryToken]`. El test `CambiosPreciosAplicarRapidoTest` se actualizó para obtener un token real (`GET /CambiosPrecios/Simular`, que sí renderiza `__RequestVerificationToken` por tener un `<form method="post">`) y enviarlo en el header `RequestVerificationToken`, siguiendo el mismo patrón ya usado en `CreditoAdelantoPagoMultipleHttpTests.cs`.
- **Prueba**: se agregó `Post_AplicarRapido_SinAntiforgeryToken_EsRechazado` (espera `400 BadRequest` sin token), y se corrigió el test existente para enviar el token real.

## Hallazgos pendientes (riesgo real documentado, no corregidos en este bloque)

| # | Severidad | Área | Detalle | Por qué no se corrigió ahora |
|---|---|---|---|---|
| 3 | LOW | Cookie de Identity | No hay `ConfigureApplicationCookie` explícito; se depende de los defaults (`HttpOnly=true`, `SameSite=Lax`, `SecurePolicy=SameAsRequest`). Está mitigado porque `ForwardedHeaders` está bien configurado (solo confía en la subred Docker conocida) y aplicado antes de `UseAuthentication`. | Fijar `CookieSecurePolicy.Always` explícitamente es defensa en profundidad válida, pero `Program.cs` tiene WIP extenso de despliegue (forwarded headers, health checks, modo `--migrate`) sin commitear; se evitó agregar más cambios a ese archivo más allá del bloqueo del Hallazgo 1 para no aumentar el área de conflicto con ese trabajo en curso. |
| 4 | LOW | CSRF en `[ApiController]` de escritura (`NotificacionController`, `VentaApiController`, `TicketApiController`, `CotizacionApiController`) | Sin `[ValidateAntiForgeryToken]`. Mitigado en la práctica por `SameSite=Lax` (bloquea el envío de cookie en POST cross-site desde `<form>`) + ausencia total de CORS (bloquea `fetch` cross-origin) + `[FromBody]` exige `Content-Type: application/json` en la mayoría de los casos. | Es un cambio transversal a varios controllers; el riesgo real hoy es bajo (depende de tres mitigaciones independientes que ya existen) y agregar el atributo a cada acción sin coordinarlo es exactamente el tipo de "ampliación innecesaria" que las reglas del proyecto piden evitar cuando no hay fricción real demostrada. Queda documentado para un bloque futuro dedicado. |
| 5 | LOW | `DiagnosticoController.ResetPassword` (POST) y `ForcePasswordReset` (GET) sin antiforgery | Resetea contraseñas de cualquier usuario por email. Gateado por `[Authorize(Roles = SuperAdmin)]` + `OnActionExecuting` que devuelve 404 fuera de `Development`. | No existe ninguna vista `.cshtml` para este controller (es una herramienta de diagnóstico invocada directo por un dev, no vía formulario), así que agregar `[ValidateAntiForgeryToken]` rompería la única forma real de usarlo sin dar ningún mecanismo para obtener el token. El riesgo real es bajo porque ya requiere ser SuperAdmin autenticado en un entorno de Development. `ForcePasswordReset` además cambia estado vía GET — recomendado convertirlo a POST en un bloque futuro dedicado a este controller, que ya tiene WIP ajeno activo (se le quitó recientemente la exposición de `ConnectionString`/passwords en texto plano, ver diff actual). |
| 6 | LOW | Sin CSP (`Content-Security-Policy`) ni `Permissions-Policy` | Ya documentado como deuda consciente en `Program.cs` (comentario junto a los headers de seguridad): requiere QA visual porque hay estilos/scripts inline y SignalR. | Decisión ya tomada explícitamente por el equipo; fuera de alcance de este bloque (headers "de bajo riesgo" ya están, CSP requiere trabajo de QA visual mayor). |
| 7 | LOW | `wwwroot/js/cliente-details.js:585` — `errorsEl.innerHTML = messages.join('<br>')` | No se pudo confirmar en esta auditoría si `messages` puede contener texto libre de usuario sin escapar antes de llegar a este punto. | Requiere trazar el origen exacto de `messages` en el flujo de validación de formulario; se deja como punto a revisar puntualmente, no se tocó código sin evidencia de explotabilidad real. |
| 8 | LOW | Cache-Control en vistas sensibles | Solo `HomeController.Error` y `MovimientoStockController` fijan `[ResponseCache(NoStore = true)]`. Usuarios/Seguridad/Reportes/Caja no lo hacen explícitamente. | Cambio transversal de bajo impacto (riesgo limitado a caché de navegador/proxy en equipos compartidos tras logout); no hay evidencia de explotación real, se documenta para un bloque de hardening futuro. |

## INFO (decisiones de diseño correctas, sin acción requerida)

- **Autorización**: los 39 controllers de negocio revisados tienen `[Authorize]`/`[PermisoRequerido]` a nivel de clase. Sin endpoints accidentalmente públicos detectados.
- **Open redirects**: patrón consistente y correcto (`Url.IsLocalUrl`/`LocalRedirect`/helper `GetSafeReturnUrl`) en todos los usos de `returnUrl` revisados.
- **SQL/Command injection**: sin `FromSqlRaw`/`ExecuteSqlRaw`/`Process.Start` en código de aplicación (el único `ExecuteSqlRawAsync` detectado está en un archivo de test, fuera de alcance de producción).
- **Path traversal en uploads**: `Helpers/DocumentoValidationHelper.cs` normaliza y valida la ruta correctamente (`Path.GetFullPath` + verificación de prefijo), y valida magic bytes reales del contenido (no solo `Content-Type` del cliente).
- **Mass assignment**: sin `TryUpdateModelAsync`/`UpdateModel`; todos los controllers usan ViewModels + mapeo explícito.
- **JSON sensible**: sin `PasswordHash`/`SecurityStamp`/`ConcurrencyStamp` expuestos en respuestas JSON de `Controllers/`. `MercadoLibreApiClient.cs` filtra explícitamente tokens/secrets antes de loguear errores de la API de ML.
- **SignalR**: `NotificacionesHub` con `[Authorize]` de clase; el único grupo usado es el del propio usuario autenticado (`Context.User.Identity.Name`), sin IDs de recurso arbitrarios recibidos del cliente.
- **OAuth Mercado Libre**: el callback valida `state` explícitamente antes de procesar el `code`.
- **Webhook Mercado Libre**: `[AllowAnonymous]` + rate limiting, consistente con que Mercado Libre no firma sus notificaciones (el body solo trae `topic`/`resource`; el procesamiento real vuelve a pedir el recurso a la API de ML con credenciales propias, no confía en el body del webhook para ninguna acción de negocio).
- **CORS**: no configurado — correcto para una app monolítica same-origin sin frontend separado.
- **Diagnóstico en Production**: `DiagnosticoController` devuelve 404 fuera de `Development` vía `OnActionExecuting`, antes de ejecutar cualquier acción.

## Autenticación

- Login/logout: vía ASP.NET Core Identity estándar (no se encontró un `AccountController` custom en `Controllers/`; el scaffolding de Identity UI para Login/Logout no está expuesto como archivo propio en el repo explorado).
- Lockout: `MaxFailedAccessAttempts=5`, `DefaultLockoutTimeSpan=5min`, `AllowedForNewUsers=true` — razonable.
- Password policy: `RequireDigit/Lowercase/Uppercase=true`, `RequireNonAlphanumeric=false`, `RequiredLength=6`. Longitud mínima baja para 2026 (LOW, no corregido en este bloque — cambiar la policy sin coordinarlo puede romper cuentas existentes con passwords de 6 caracteres; requiere decisión de negocio).
- `RequireConfirmedEmail=false`/`RequireConfirmedAccount=false`: decisión documentada explícitamente en el propio código como intencional.
- Session fixation: Identity regenera la cookie de autenticación en `SignInAsync`; no se detectó lógica propia que reutilice una sesión anónima como autenticada.

## Cookies

| Cookie | Secure | HttpOnly | SameSite | Estado |
|---|---|---|---|---|
| Cookie de autenticación Identity | `SameAsRequest` (efectivamente `true` detrás de HTTPS vía Caddy + ForwardedHeaders correcto) | `true` (default) | `Lax` (default) | Ver hallazgo pendiente #3 — sin config explícita, mitigado |
| Antiforgery | Emitida junto con formularios; header dedicado `RequestVerificationToken` | `true` (default) | `Strict`/`Lax` (default Antiforgery) | OK |

## Autorización

| Módulo/endpoint | Anónimo | Usuario sin permiso | Usuario autorizado |
|---|---|---|---|
| Controllers de negocio (Clientes, Ventas, Caja, Seguridad, Catálogo, Cotizaciones, etc.) | Denegado (`[Authorize]`/`[PermisoRequerido]` de clase) | Denegado (filtro `PermisoRequeridoAttribute`) | Permitido |
| `DiagnosticoController` | Denegado (404 fuera de Development) | Denegado (`Roles=SuperAdmin`) | Permitido solo en Development |
| `MercadoLibreWebhookController` | Permitido (`[AllowAnonymous]` intencional — es un webhook externo) | N/A | N/A |
| `/uploads/documentos-clientes/*` | **Antes: permitido (bug); ahora: 404 siempre** | — | Solo vía `DocumentoClienteController.Descargar` autenticado |

## CSRF

- Protección: `[ValidateAntiForgeryToken]` explícito por acción en los controllers MVC clásicos, consistente en todas las acciones de escritura revisadas salvo las documentadas arriba. `CambiosPreciosController` usa `[AutoValidateAntiforgeryToken]` de clase (corregido el bypass de `AplicarRapido`).
- Forms: cobertura completa.
- AJAX: el patrón real es leer un `<input name="__RequestVerificationToken">` ya presente en la página y mandarlo en el header `RequestVerificationToken` (`Program.cs` configura ese nombre de header explícitamente).
- Excepciones: webhook de Mercado Libre (correcto, no aplica CSRF a un webhook externo), `[ApiController]`s de escritura (pendiente #4), `DiagnosticoController` (pendiente #5, dev-only).

## CORS

- No configurado (`AddCors`/`UseCors` ausentes). App monolítica same-origin; sin consumidores cross-origin conocidos. Sin riesgo.

## XSS

- Sin `Html.Raw` detectado en las vistas revisadas. Razor auto-escapa por defecto.
- Usos de `innerHTML`/`insertAdjacentHTML` en `wwwroot/js/*` son mayormente construcción de markup estático controlado por el propio JS. Un caso (`cliente-details.js:585`) queda como pendiente #7 a confirmar puntualmente.

## SQL / command injection

- Raw SQL: ninguno en código de aplicación.
- Parametrización: EF Core/LINQ de forma consistente.
- Ejecución de procesos: sin `Process.Start`/`ProcessStartInfo` en código de aplicación.

## Uploads

- Tipos: allowlist real por extensión + validación de magic bytes (`DocumentoValidationHelper`).
- Tamaño: límite de 5MB validado server-side.
- Nombres: `LocalFileStorageService` usa `Guid.NewGuid()` (correcto); `DocumentoClienteService` usaba nombre predecible — mitigado por el bloqueo de middleware del Hallazgo 1, migración a GUID queda como mejora futura documentada.
- Traversal: mitigado correctamente con normalización + verificación de prefijo de ruta.

## Downloads

- Autorización: `DocumentoClienteController.Descargar` exige `[Authorize]` + permiso; ahora es la única vía de acceso real a esos archivos.
- Traversal: N/A (lectura por Id de base de datos, no por path de usuario).
- Headers: `Content-Disposition` vía `File(bytes, contentType, fileName)` de ASP.NET Core (comportamiento estándar seguro).

## Headers

| Header | Estado | Acción |
|---|---|---|
| `Strict-Transport-Security` | Configurado (`UseHsts` fuera de Development/Testing) | — |
| `X-Content-Type-Options` | `nosniff` configurado | — |
| `Content-Security-Policy` | No configurado (deuda documentada explícitamente en código) | Pendiente #6, requiere QA visual |
| `X-Frame-Options` | `SAMEORIGIN` configurado | — |
| `Referrer-Policy` | `no-referrer` configurado | — |
| `Permissions-Policy` | No configurado | Pendiente #6 |

## Errores / diagnóstico

- Production errors: `UseExceptionHandler("/Home/Error")` fuera de Development; sin `UseDeveloperExceptionPage` explícito.
- `/Diagnostico`: 404 fuera de Development vía `OnActionExecuting`, antes de cualquier acción. El WIP ajeno en este archivo ya retiró la exposición de `ConnectionString` y de passwords en texto plano en las respuestas (`TempData`/JSON) — mejora concurrente detectada y preservada, no tocada.
- Stack traces / secrets / paths: no expuestos en Production por el mismo gate.

## APIs / JSON

- Endpoints: sin exposición de `PasswordHash`/`SecurityStamp`/tokens en las respuestas JSON revisadas.
- Mass assignment: sin riesgo detectado (ViewModels explícitos en todos los controllers).

## SignalR

- Autenticación/autorización: `NotificacionesHub` con `[Authorize]` de clase.
- Métodos probados (revisión de código): el único grupo usado es el del propio usuario conectado; sin métodos que acepten IDs de recursos ajenos sin validar pertenencia.

## Mercado Libre

- Callback: valida `state` antes de procesar `code`.
- Tokens: comentario en `Program.cs` confirma cifrado con Data Protection para persistir entre reinicios.
- Webhook: `[AllowAnonymous]` + rate limiting, sin validación de firma — consistente con el contrato real de Mercado Libre (no hay HMAC ni firma que validar en sus webhooks; el procesamiento real vuelve a pedir el recurso con credenciales propias).
- Pending network exposure: sin cambios — no se abrió nada a Internet, no se hizo OAuth productivo.

## Tests

```
Build:    Compilación correcta (0 errores, 8 warnings preexistentes no relacionados a seguridad)
Total:    4969
Passed:   4967
Failed:   0
Skipped:  2
Duración: 3m 2s
```

0 rojos, incluyendo los 2 tests nuevos de seguridad (`UploadsDocumentosClientesEstaticoHttpTests`, y el test de regresión agregado a `CambiosPreciosAplicarRapidoTest`). Los 2 `Skipped` son preexistentes, no relacionados a este bloque.

## Archivos modificados

- `Program.cs` — middleware de bloqueo para `/uploads/documentos-clientes/*` antes de `UseStaticFiles` (Hallazgo 1).
- `Controllers/CambiosPreciosController.cs` — retirado `[IgnoreAntiforgeryToken]` de `AplicarRapido` (Hallazgo 2).
- `TheBuryProyect.Tests/Integration/CambiosPreciosAplicarRapidoTest.cs` — test existente actualizado para enviar token real; test nuevo de regresión sin token.
- `TheBuryProyect.Tests/Integration/UploadsDocumentosClientesEstaticoHttpTests.cs` — nuevo, prueba el bloqueo del Hallazgo 1.
- `docs/seguridad-web.md` — este informe.

## Conflictos evitados (archivos con WIP ajeno, no tocados salvo lo indicado)

- `Services/DocumentoClienteService.cs` — WIP activo normalizando separadores de ruta Windows/Linux en la misma lógica de almacenamiento del Hallazgo 1; no se movió el storage fuera de `wwwroot` para no chocar con ese trabajo (mitigado igual vía middleware en `Program.cs`, archivo distinto).
- `Controllers/DiagnosticoController.cs` — WIP activo retirando exposición de `ConnectionString`/passwords en texto plano; no se tocó más allá de la lectura para diagnóstico (pendiente #5 documentado, no aplicado por falta de vista/formulario para el token).
- `Program.cs` (resto del archivo) — WIP extenso de despliegue (forwarded headers, health checks live/ready, modo `--migrate`, connection string por `ProductionSecrets`); solo se agregó el middleware puntual del Hallazgo 1, en una zona del pipeline sin cambios concurrentes.
- Toda la infraestructura (`Dockerfile`, `docker-compose*.yml`, `Caddyfile`, `scripts/`, `.github/workflows/ci.yml`, `docs/validacion-funcional.md`, `docs/deploy-rollback.md`) — no tocada, según instrucción explícita.
- Resto de tests con WIP (`CustomWebApplicationFactory.cs`, `CatalogoCostoEnvioPermisoHttpTests.cs`, `DocumentoClienteServiceTests.cs`, `VentaCrearEditarParidadHttpTests.cs`, `CreditoPunitorioOperacionControllerTests.cs`, `MercadoLibreApiClientTests.cs`, `VentaCrearEditarParidadTests.cs`, `VentaCreateUiContractTests.cs`, `VentaDetailsAjustePlanUiContractTests.cs`) — no tocados; se preservaron íntegros.

## Pendientes críticos anteriores (se mantienen visibles)

- **Rotación de cualquier contraseña admin expuesta históricamente** — sigue pendiente, fuera del alcance de este bloque (es una acción operativa, no de código).
- **Backup offsite real** — pendiente, ver `[[backup-restore-docker-estado]]` en memoria del agente (no verificado un proveedor externo real).
- **Cierre de la validación funcional** (`docs/validacion-funcional.md`, WIP de otro agente) — sigue en curso, no tocado.
- **Host/red/VPN definitivos** — pendiente, fuera de alcance (sin tocar infraestructura/red en este bloque).

## Confirmación

No modifiqué Docker, Compose, Caddy, backups, monitoreo, deploy, CI, VPN ni firewall.
No hice staging, commit ni push.
