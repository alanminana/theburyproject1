# Crédito FASE 2 — Garante Validado

## Objetivo

Implementar el garante como relación real entre clientes del sistema. Reemplaza el campo de texto libre hardcodeado por una relación persistida, validada y visible en la ficha del cliente.

---

## Reglas de negocio aplicadas

| # | Regla | Implementación |
|---|---|---|
| 1 | El garante no puede ser el mismo cliente | `ValidarGaranteAsync` — cortocircuito inmediato |
| 2 | El garante debe existir en el sistema | Query sobre `Clientes` por `Id`, excluye `IsDeleted` |
| 3 | El garante debe estar activo | `Cliente.Activo == true` |
| 4 | El garante debe haber comprado antes | `Cliente.CantidadComprasCliente > 0` |
| 5 | El garante debe tener `PuntajeCliente >= 4` | Constante `PuntajeMinimoGarante = 4` |
| 6 | El garante no puede garantizar más de 3 clientes activos | Count sobre `Garantes` por `GaranteClienteId` sin `IsDeleted` |
| 7 | El garante puede tener deuda | Regla **no existe** — deuda no invalida |
| 8 | El garante no modifica el cupo del cliente garantizado | `AsignarGaranteAsync` no toca `LimiteCredito` |
| 9 | El garante sirve como requisito alternativo al recibo de sueldo | Solo validación de relación; lógica de requisitos queda en fases posteriores |

---

## Entidades y campos

### `Garante` (existente, extendido)

Campos agregados en esta fase:

| Campo | Tipo | Descripción |
|---|---|---|
| `FechaBaja` | `DateTime?` | Fecha de baja de la relación garante |
| `MotivoBaja` | `string? (500)` | Motivo textual de la baja |

Campos preexistentes clave:

| Campo | Descripción |
|---|---|
| `ClienteId` | ID del cliente que **necesita** el garante (garantizado) |
| `GaranteClienteId?` | ID del cliente que **actúa** como garante |
| `IsDeleted` | Baja lógica del registro |
| `CreatedAt / CreatedBy` | Auditoría de alta (de `AuditableEntity`) |

### `Cliente` (sin cambios en schema)

- `GaranteId?` — FK al registro `Garante` activo del cliente. Ya existía.
- `Garante` nav property — ya existía.

### `ClienteDetalleViewModel` (extendido)

- `GaranteInfo (GaranteInfoViewModel?)` — panel de estado del garante en la ficha.

---

## Migración

**`20260701155943_AddGaranteFechaBajaMotivo`**

- Aditiva: agrega 2 columnas `nullable` en la tabla `Garantes`.
- Sin riesgo: no toca datos existentes, no modifica índices ni FKs.

---

## Servicios y archivos creados/modificados

### Creados

| Archivo | Contenido |
|---|---|
| `Services/Interfaces/IGaranteService.cs` | Interfaz con 5 contratos: `ValidarGaranteAsync`, `AsignarGaranteAsync`, `RemoverGaranteAsync`, `ObtenerInfoGaranteAsync`, `BuscarCandidatosAsync` |
| `Services/GaranteService.cs` | Implementación completa con las 6 validaciones |
| `ViewModels/GaranteViewModel.cs` | `GaranteInfoViewModel`, `AsignarGaranteRequest`, `RemoverGaranteRequest` |
| `TheBuryProyect.Tests/Integration/GaranteServiceTests.cs` | 10 tests de integración con SQLite in-memory |
| `Migrations/20260701155943_AddGaranteFechaBajaMotivo.cs` | Migración aditiva |

### Modificados

| Archivo | Cambio |
|---|---|
| `Models/Entities/Garante.cs` | +`FechaBaja`, +`MotivoBaja` |
| `ViewModels/ClienteDetalleViewModel.cs` | +`GaranteInfo` property |
| `Controllers/ClienteController.cs` | +`IGaranteService` en constructor; +3 endpoints (`BuscarPosiblesGarantes`, `AsignarGarante`, `RemoverGarante`); carga `GaranteInfo` en `ConstructDetalleViewModel` |
| `Views/Cliente/Details_tw.cshtml` | Reemplazo completo del mock garante por panel real + modal + JS |
| `Program.cs` | `AddScoped<IGaranteService, GaranteService>()` |

---

## Endpoints agregados

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/Cliente/BuscarPosiblesGarantes?q=...&clienteId=...` | Búsqueda AJAX de candidatos a garante |
| `POST` | `/Cliente/AsignarGarante` | Asigna un cliente como garante (JSON body, AntiForgery) |
| `POST` | `/Cliente/RemoverGarante` | Remueve el garante activo (JSON body, AntiForgery) |

---

## Tests

**Archivo:** `TheBuryProyect.Tests/Integration/GaranteServiceTests.cs`

| Test | Resultado esperado |
|---|---|
| `Validar_MismoCliente_RetornaError` | Error "mismo cliente" |
| `Validar_GaranteInexistente_RetornaError` | Error "no existe" |
| `Validar_GaranteInactivo_RetornaError` | Error "activo" |
| `Validar_GarantePuntajeInsuficiente_RetornaError` | Error "puntaje" |
| `Validar_GaranteSinCompras_RetornaError` | Error "compras" |
| `Validar_GaranteMaximoGarantias_RetornaError` | Error "garantiza" |
| `Validar_GaranteConDeuda_EsValido` | OK — deuda no invalida |
| `Validar_GaranteValido_RetornaOk` | OK — todas las condiciones cumplidas |
| `Asignar_GaranteNoModificaCupo_LimiteSinCambios` | `LimiteCredito` sin cambios post-asignación |
| `Asignar_GaranteValido_PersistoRelacionEnDB` | Registro `Garante` con `ClienteId` y `GaranteClienteId` correctos |

**Resultado:** 10/10 — OK.

---

## Validación de builds

| Paso | Resultado |
|---|---|
| `dotnet build TheBuryProyect.csproj` (Lote 1 — entidad) | 0 errores / 0 advertencias |
| `dotnet build TheBuryProyect.csproj` (Lote 2 — service) | 0 errores / 0 advertencias |
| Tests focalizados GaranteServiceTests | 10/10 OK |
| `dotnet build TheBuryProyect.csproj` (Lote 4 — controller) | 0 errores / 0 advertencias |
| `dotnet build TheBuryProyect.csproj` (Lote 5 — UI) | 0 errores / 0 advertencias |
| Migración aplicada en DB local (`TheBuryProjectDb`) | OK — columnas `FechaBaja`/`MotivoBaja` creadas |

---

## QA visual live — 01-jul-2026

**URL:** `http://localhost:5187/Cliente/Details/1`
**Viewports probados:** 1440×900 (desktop), 390×844 (mobile)

| Caso | Resultado |
|---|---|
| Vista carga sin error (no 500) | ✅ OK |
| No aparece mock "Juan Pérez — DNI 12345678" | ✅ OK — eliminado |
| Panel "Sin garante asignado" visible | ✅ OK |
| Modal abre al hacer click en "Asignar garante" | ✅ OK |
| Búsqueda AJAX retorna candidatos | ✅ OK |
| Selección de candidato activa botón Confirmar | ✅ OK |
| Garante inválido (puntaje 1, 0 compras) muestra error en modal | ✅ OK — 2 mensajes correctos |
| Modal permanece abierto ante error (no recarga) | ✅ OK |
| Garante válido (puntaje 5, 3 compras) se asigna y panel muestra estado | ✅ OK |
| Chip "Válido" visible, datos correctos en panel | ✅ OK |
| "Quitar" remueve garante y vuelve al estado "Sin garante" | ✅ OK |
| Sin overflow horizontal en mobile (390×844) | ✅ OK |
| Errores de consola relacionados con la app | ✅ 0 errores — solo 400 del caso inválido intencional |

---

## Riesgos resueltos

| Riesgo | Estado |
|---|---|
| Migración aditiva sin datos existentes afectados | Resuelto — solo `AddColumn` nullable |
| `CreditoService` legacy (crea garantes texto libre) | Sin cambio — flujo legacy intacto |
| `Cliente.ComoGarante` naming confuso en nav | No tocado — el servicio usa query directo por `GaranteClienteId` |
| Mock hardcodeado en UI | Reemplazado por panel real |
| Token antiforgery disponible para JS | Confirmado — múltiples tokens en el DOM via `@Html.AntiForgeryToken()` |

---

## Pendientes reales para fases posteriores

1. **Requisito compuesto**: integrar en `ClienteAptitudService` la regla "recibo de sueldo O garante válido". Esta fase solo persiste y valida la relación; la decisión de aptitud crediticia es FASE 3+.
2. **Notificación al garante**: alertar si el garante pierde validez (puntaje baja de 4, se vuelve inactivo). Fuera de scope.
3. **Historial de garantes**: actualmente `IsDeleted = true` marca la baja pero no hay vista de historial de garantes anteriores de un cliente.
4. **QA visual en la app real**: ✅ REALIZADO — ver sección "QA visual live".

---

## Working tree al cierre

```
Archivos nuevos:
  docs/credito-fase-2-garante.md
  Migrations/20260701155943_AddGaranteFechaBajaMotivo.cs
  Migrations/20260701155943_AddGaranteFechaBajaMotivo.Designer.cs
  Services/GaranteService.cs
  Services/Interfaces/IGaranteService.cs
  ViewModels/GaranteViewModel.cs
  TheBuryProyect.Tests/Integration/GaranteServiceTests.cs

Archivos modificados:
  Models/Entities/Garante.cs
  Migrations/AppDbContextModelSnapshot.cs
  ViewModels/ClienteDetalleViewModel.cs
  Controllers/ClienteController.cs
  Views/Cliente/Details_tw.cshtml
  Program.cs
```

**QA live realizado. Listo para commit.**
