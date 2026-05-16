# Fase Cotización V1.7 — Tests de integración de seguridad para conversión

## A. Objetivo

Agregar cobertura de integración real para los endpoints de conversión de cotización, verificando que `PermisoRequeridoAttribute` efectivamente bloquea (403) a usuarios sin `cotizaciones.convert` y permite el paso a usuarios con ese permiso.

Endpoints cubiertos:
- `POST /api/cotizacion/{id}/conversion/preview`
- `POST /api/cotizacion/{id}/conversion/convertir`

---

## B. Diagnóstico de infraestructura de test auth

### Infraestructura existente

| Componente | Clasificación | Decisión |
|---|---|---|
| `CustomWebApplicationFactory` | canónico | reutilizar y extender |
| `TestAuthHandler` | canónico | extender con header X-Test-User-Id |
| `PermissionClaimsTransformation` | canónico | se ejecuta en runtime de tests |
| `RolService` | canónico | se ejecuta contra SQLite in-memory |
| `PermisoRequeridoAttribute` | canónico | no tocar |
| `CotizacionApiController` | canónico | no tocar |
| `CotizacionConversionService` | canónico | no tocar |

### Cómo funciona la auth en tests

1. `TestAuthHandler` (esquema "Test") autentica toda request, devuelve un principal con `SuperAdmin` role y user ID configurable.
2. `PermissionClaimsTransformation` corre en cada request: consulta `RolService` → BD (SQLite in-memory) → añade/quita roles y claims `Permission:*` según lo que haya en BD para ese user ID.
3. `PermisoRequeridoAttribute` evalúa el principal resultante: primero chequea `AllowSuperAdmin && IsInRole("SuperAdmin")`; si no pasa, chequea el claim `Permission:{modulo}.{accion}`.

### Problema de cobertura en V1.6

Los tests de V1.6 usaban reflexión para verificar que los atributos estaban correctamente declarados, pero no verificaban el comportamiento real en runtime HTTP. Un usuario sin `cotizaciones.convert` podía acceder igualmente si `PermisoRequeridoAttribute` tenía algún bug de evaluación.

---

## C. Decisión técnica

### Mecanismo para usuarios "sin permiso" (403)

`TestAuthHandler` fue extendido con soporte al header `X-Test-User-Id`. Si el header está presente, el handler usa ese ID en lugar del default `test-user-id`.

Al usar `NoPermsUserId = "test-no-perms-id"` (no seedeado en BD), `PermissionClaimsTransformation`:
- Consulta BD → no encuentra roles → elimina el claim SuperAdmin inicial
- No encuentra permisos → no agrega ningún `Permission:*` claim

Resultado: usuario autenticado sin roles ni permisos → `PermisoRequeridoAttribute` devuelve 403.

### Mecanismo para usuarios "con permiso" (no 403)

Se crea `SeedUserWithConvertPermissionAsync()`: siembra en la BD SQLite de test un usuario con un rol propio (`TestConvertRole`) que tiene `RolPermiso(cotizaciones.view)` + `RolPermiso(cotizaciones.convert)`.

Ambos permisos son necesarios porque:
- `CotizacionApiController` exige `cotizaciones.view` a nivel de clase (`[PermisoRequerido]` en el tipo)
- Los endpoints de conversión exigen `cotizaciones.convert` a nivel de método

`PermissionClaimsTransformation` consulta BD → encuentra el rol → agrega ambos claims → `PermisoRequeridoAttribute` permite el paso.

### EF Core SQLite enforcea FKs

EF Core SQLite activa `PRAGMA foreign_keys = ON` por defecto. Para insertar `RolPermiso`, fue necesario seedear primero `ModuloSistema` (id=9999) y `AccionModulo` (ids=9999, 9998) como registros padres.

Se usaron IDs 9999/9998 para no colisionar con datos de producción.

---

## D. Casos cubiertos

| Test | Usuario | Permiso en BD | Resultado esperado | Resultado real |
|---|---|---|---|---|
| `Preview_SinPermisoConvert_DevuelveForbidden` | `test-no-perms-id` (no en BD) | ninguno | 403 | 403 ✓ |
| `Convertir_SinPermisoConvert_DevuelveForbidden` | `test-no-perms-id` (no en BD) | ninguno | 403 | 403 ✓ |
| `Preview_ConPermisoConvert_NoDevuelveForbidden` | `test-convert-perms-id` | `cotizaciones.view` + `cotizaciones.convert` | no 403 (200) | 200 ✓ |
| `Convertir_ConPermisoConvert_NoDevuelveForbidden` | `test-convert-perms-id` | `cotizaciones.view` + `cotizaciones.convert` | no 403 (400) | 400 ✓ |

Cotización ID 999 no existe en BD de test:
- `preview` devuelve 200 con `convertible=false` (el servicio maneja la ausencia sin excepción)
- `convertir` devuelve 400 (el servicio devuelve `Fallido`)

Ambos son resultados funcionales esperados. El foco de los tests es verificar que la autorización pasó, no el comportamiento funcional de la conversión.

---

## E. Archivos modificados / creados

### `TheBuryProyect.Tests/Infrastructure/TestAuthHandler.cs` — modificado

- Agregada constante `UserIdHeader = "X-Test-User-Id"`
- El handler lee el header en cada request y usa ese ID si está presente; si no, usa el default `test-user-id` (comportamiento existente preservado)

### `TheBuryProyect.Tests/CustomWebApplicationFactory.cs` — modificado

- Agregada constante `NoPermsUserId = "test-no-perms-id"`
- Agregada constante `ConvertPermsUserId = "test-convert-perms-id"`
- Agregado método `CreateClientWithUserId(string userId)`: crea client autenticado con header override
- Agregado método `SeedUserWithConvertPermissionAsync()`: siembra usuario, rol, módulo, acción y dos `RolPermiso` (view + convert)

### `TheBuryProyect.Tests/Integration/CotizacionConversionSecurityTests.cs` — creado

4 tests de integración de seguridad HTTP real contra `TestServer`.

---

## F. Qué NO se tocó

- `CotizacionApiController` — sin cambios
- `CotizacionConversionService` / `ICotizacionConversionService` — sin cambios
- `PermisoRequeridoAttribute` — sin cambios
- `PermissionClaimsTransformation` — sin cambios
- `Program.cs` — sin cambios (no se relajó ningún permiso productivo)
- `Views/Cotizacion/Detalles_tw.cshtml` — sin cambios
- Módulos de Juan, Kira, VentaService, VentaController — sin cambios

---

## G. Riesgos / deuda remanente

| Riesgo | Estado |
|---|---|
| Tests de regresión de auth existentes pueden verse afectados si `TestAuthHandler` cambia de comportamiento | Mitigado: el header es opt-in; si no se envía, comportamiento idéntico al original |
| La SharedFactory tiene estado acumulado entre tests de la misma clase | Mitigado: seed idempotente (`AnyAsync` check) + IDs únicos de test |
| `ModulosSistema`/`AccionesModulo` con ID 9999 podrían colisionar si el seed de producción llega a esos IDs | Riesgo bajo: el seed real usa IDs secuenciales pequeños |
| No se cubre el caso de usuario con `cotizaciones.view` pero sin `cotizaciones.convert` | Deuda menor: sería un quinto test. Cubre el contrato de forma más estricta. |

---

## H. Checklist actualizado

### Carlos — Cotización

- [x] V1.1 — Persistencia mínima
- [x] V1.2 — Diseño conversión a venta
- [x] V1.3 — Conversión controlada
- [x] V1.4 — UI conversión a venta
- [x] V1.5 — Trazabilidad e IVA correcto en conversión
- [x] V1.6 — Permiso granular `cotizaciones.convert`
- [x] **V1.7 — Tests integración seguridad conversión 403** ← esta fase
- [ ] V1.8 — Preview tabla cambios unitarios
- [ ] V1.9 — Numeración robusta
- [ ] V1.10 — Cancelación compleja
- [ ] V1.11 — Vencimiento automático
- [ ] V1.12 — Impresión/envío

### Juan

- [ ] 10.18 — DocumentoCliente estado visual
- [ ] 10.19 — DocumentoCliente agrupado por cliente + modal

### Kira

- [ ] Fix test HTTPS TestHost — VentaApiController_ConfiguracionPagosGlobal

---

## I. Prompt siguiente recomendado

**V1.8 — Preview tabla de cambios unitarios**

Mostrar en el modal de conversión una tabla que compare precio actual vs precio de cotización por línea, antes de confirmar la conversión. Backend: extender `CotizacionConversionPreviewResultado` con las diferencias de precio. Frontend: renderizar en `Detalles_tw.cshtml`.
