# COTIZ-1A — Formulario de Cotización: campos comerciales existentes

## A. Objetivo

Auditar modelos, viewmodels, controller, services y JS de Cotización y exponer en la UI los campos comerciales ya soportados de punta a punta: fecha de vencimiento, descuentos generales, nombre libre de cliente y teléfono libre de cliente. Mover ProductoId manual a sección avanzada para limpiar el formulario.

---

## B. Auditoría de campos

### Campo — Fecha de vencimiento (`FechaVencimiento`)

| Capa | Estado |
|---|---|
| Entidad `Cotizacion.cs` | `DateTime? FechaVencimiento` — ✅ EXISTE |
| `CotizacionCrearRequest` | `DateTime? FechaVencimiento` — ✅ EXISTE |
| `CotizacionResultado` | `DateTime? FechaVencimiento` — ✅ EXISTE |
| `CotizacionService.CrearAsync` | `FechaVencimiento = request.FechaVencimiento` — ✅ PROCESADO |
| `CotizacionVencimientoBackgroundService` | ✅ ya gestiona vencimientos automáticos |
| JS `guardar()` payload | ❌ no enviado — **gap corregido en COTIZ-1A** |
| Vista | ❌ sin input — **agregado en COTIZ-1A** |

### Campo — Descuento general porcentaje (`DescuentoGeneralPorcentaje`)

| Capa | Estado |
|---|---|
| `CotizacionSimulacionRequest` | `decimal? DescuentoGeneralPorcentaje` — ✅ EXISTE |
| `CotizacionPagoCalculator.CalcularDescuentoGeneral` | ✅ PROCESADO (valida 0–100, suma al descuentoTotal) |
| Endpoint `POST /api/cotizacion/simular` | ✅ acepta via `CotizacionSimulacionRequest` |
| JS `buildRequest()` | ❌ no incluido — **corregido en COTIZ-1A** |
| Vista | ❌ sin input — **agregado en COTIZ-1A** |

### Campo — Descuento general importe (`DescuentoGeneralImporte`)

| Capa | Estado |
|---|---|
| `CotizacionSimulacionRequest` | `decimal? DescuentoGeneralImporte` — ✅ EXISTE |
| `CotizacionPagoCalculator.CalcularDescuentoGeneral` | ✅ PROCESADO (valida >= 0, suma al descuentoTotal) |
| JS `buildRequest()` | ❌ no incluido — **corregido en COTIZ-1A** |
| Vista | ❌ sin input — **agregado en COTIZ-1A** |

### Campo — Nombre libre de cliente (`NombreClienteLibre`)

| Capa | Estado |
|---|---|
| Entidad `Cotizacion.cs` | `string? NombreClienteLibre` (max 200) — ✅ EXISTE |
| `CotizacionCrearRequest` | `string? NombreClienteLibre` — ✅ EXISTE |
| `CotizacionSimulacionRequest` | `string? NombreClienteLibre` — ✅ EXISTE |
| `CotizacionService.CrearAsync` | `NombreClienteLibre = NormalizarTexto(request.NombreClienteLibre ?? request.Simulacion.NombreClienteLibre, 200)` — ✅ |
| JS `guardar()` antes de COTIZ-1A | ⚠️ hack: usaba `clienteBuscar.value` como nombre libre — **corregido** |
| Vista antes de COTIZ-1A | ❌ sin input dedicado |

### Campo — Teléfono libre de cliente (`TelefonoClienteLibre`)

| Capa | Estado |
|---|---|
| Entidad `Cotizacion.cs` | `string? TelefonoClienteLibre` (max 30) — ✅ EXISTE |
| `CotizacionCrearRequest` | `string? TelefonoClienteLibre` — ✅ EXISTE |
| `CotizacionService.CrearAsync` | `TelefonoClienteLibre = NormalizarTexto(request.TelefonoClienteLibre, 30)` — ✅ |
| JS `guardar()` payload | ❌ no enviado — **corregido en COTIZ-1A** |
| Vista | ❌ sin input — **agregado en COTIZ-1A** |

### Campo — Descuento por producto (`DescuentoPorcentaje` / `DescuentoImporte`)

| Capa | Estado |
|---|---|
| `CotizacionProductoRequest` | `decimal? DescuentoPorcentaje`, `decimal? DescuentoImporte` — ✅ EXISTE |
| `CotizacionPagoCalculator.CalcularDescuentoProducto` | ✅ PROCESADO (valida rango, suma al descuentoTotal) |
| Entidad `CotizacionDetalle` | `DescuentoPorcentajeSnapshot`, `DescuentoImporteSnapshot` — ✅ EXISTE |
| `CotizacionService.CrearAsync` | `DescuentoPorcentajeSnapshot = original?.DescuentoPorcentaje` — ✅ |
| JS `buildRequest()` | ❌ no incluido |
| Vista | ❌ no hay columnas por producto |
| **Decisión** | **POSTERGADO a COTIZ-1B** — requiere cambio mayor en tabla de productos (inputs por fila) |

### Campo — ProductoId manual

| Estado |
|---|
| Vista: ya existía visible | JS: ya implementado `agregarProductoManual()` |
| **Acción COTIZ-1A**: Movido a `<details>` "Agregar por ID (avanzado / soporte)" |

---

## C. Tabla resumen de soporte E2E por campo

| Campo | Estado | Acción COTIZ-1A |
|---|---|---|
| FechaVencimiento | SOPORTADO E2E | EXPUESTO — input date en sección guardar |
| DescuentoGeneralPorcentaje | SOPORTADO E2E | EXPUESTO — input en sección Descuentos generales |
| DescuentoGeneralImporte | SOPORTADO E2E | EXPUESTO — input en sección Descuentos generales |
| NombreClienteLibre | SOPORTADO E2E | EXPUESTO — input dedicado en sección cliente |
| TelefonoClienteLibre | SOPORTADO E2E | EXPUESTO — input en sección cliente |
| DescuentoPorProducto | SOPORTADO E2E | POSTERGADO — COTIZ-1B |
| ProductoId manual | SOPORTADO E2E | MOVIDO a `<details>` avanzado |

---

## D. Campos implementados en COTIZ-1A

1. **FechaVencimiento** — input `date` `#cotizacion-fecha-vencimiento` en sección de guardado.
   - Enviado en payload de `guardar()` como `fechaVencimiento`.
2. **DescuentoGeneralPorcentaje** — input `number` `#cotizacion-descuento-gral-pct` (0–100) en sección Descuentos generales.
   - Enviado en `buildRequest()` como `descuentoGeneralPorcentaje`.
   - Afecta la simulación: cambia descuentoTotal y totalBase antes de calcular medios.
3. **DescuentoGeneralImporte** — input `number` `#cotizacion-descuento-gral-importe` (≥ 0) en sección Descuentos generales.
   - Enviado en `buildRequest()` como `descuentoGeneralImporte`.
4. **NombreClienteLibre** — input `text` `#cotizacion-nombre-libre` (max 200) en sección cliente.
   - Enviado en `buildRequest()` como `nombreClienteLibre` (para simulación con crédito personal).
   - Enviado en payload de `guardar()` como `nombreClienteLibre`.
   - Reemplaza el hack anterior que usaba `clienteBuscar.value`.
5. **TelefonoClienteLibre** — input `tel` `#cotizacion-telefono-libre` (max 30) en sección cliente.
   - Enviado en payload de `guardar()` como `telefonoClienteLibre`.
6. **ProductoId manual** — movido a `<details>` con summary "Agregar por ID (avanzado / soporte)".

---

## E. Campos postergados a COTIZ-1B

- **DescuentoPorProducto** (porcentaje e importe por línea): totalmente soportado en backend (`CotizacionProductoRequest`, `CalcularDescuentoProducto`, `CotizacionDetalle`). Requiere agregar columnas de inputs en la tabla de productos — cambio de mayor superficie de UI, postergado por seguridad.

---

## F. Archivos modificados

| Archivo | Cambio |
|---|---|
| `Views/Cotizacion/Index_tw.cshtml` | Nuevos inputs: FechaVencimiento, NombreLibre, TelefonoLibre, DescuentosGenerales. ProductoId manual → `<details>`. |
| `wwwroot/js/cotizacion-simulador.js` | Nuevas entradas en `els`. Helper `parseNonNegativeDecimal`. `buildRequest()` con descuentos y nombre libre. `guardar()` payload completo. |
| `docs/cotiz-1a-formulario-cotizacion-campos-comerciales.md` | Este documento. |

---

## G. Contratos preservados

- No se modificaron entidades, migraciones, ni la base de datos.
- No se modificó `CotizacionService`, `CotizacionPagoCalculator`, ni ningún servicio.
- No se modificó `CotizacionApiController`.
- No se modificó `CotizacionConversionService`.
- No se modificó `VentaService` ni ninguna vista de Venta.
- Los endpoints existentes mantienen sus contratos.
- Los payloads de simulación y guardado ahora incluyen campos que ya existían en los request — no hay ruptura de contrato.

---

## H. Riesgo sobre cálculos

**Bajo.** Los descuentos generales ya existían en `CotizacionSimulacionRequest` y su lógica en el calculador estaba probada. Solo faltaba el wiring en JS. La validación de rango (0–100 para porcentaje, ≥ 0 para importe) la hace el backend. Si el usuario no ingresa valores, se envía `null` y el calculador los ignora.

---

## I. Riesgo sobre conversión a Venta

**Ninguno.** No se tocó `CotizacionConversionService`. El proceso de conversión lee los datos guardados de la entidad `Cotizacion`, que incluye los nuevos campos — esto es transparente porque esos campos ya existían antes de COTIZ-1A (solo no se exponían en la UI).

---

## J. Accesibilidad

- Todos los inputs nuevos tienen `<label>` con `for` correcto.
- Todos usan clases de foco existentes (`focus:border-primary focus:ring-2 focus:ring-primary/30`).
- No se usó `innerHTML` para datos de usuario.
- `esc()` ya estaba en el JS y no fue removido.
- Los inputs de teléfono usan `type="tel"` (teclado numérico en mobile).
- Los inputs de número usan `type="number"` con `min`/`max` correctos.

---

## K. Mobile/responsive

- Inputs nuevos en el aside usan `w-full`.
- Descuentos generales usan `grid grid-cols-2 gap-3` — se adapta a pantallas pequeñas.
- Playwright visual 412x915 y 390x844 — 169/169 OK.

---

## L. Validaciones

- **Frontend**: `parseNonNegativeDecimal` ignora valores inválidos (null). El backend es la autoridad.
- **Backend**: `CalcularDescuentoGeneral` valida porcentaje 0–100, importe ≥ 0, y que el descuento no supere el total.
- No se introdujo validación de negocio nueva — se usa la existente.

---

## M. Tests

- `LayoutUiContractTests`: 57/57 OK.
- Suite relevante (`Layout|Shared|Navigation|...`): 230/230 OK.
- No se agregaron tests .NET nuevos — los cambios son puramente de UI/wiring y la lógica backend no cambió.

---

## N. Playwright

### cotizacion-simulador.spec.js (COTIZ-QA)

- **36/36 OK** — sin regresiones.
- Los tests T1–T5 no dependían de los campos nuevos; los nuevos inputs no interfieren con los selectores usados.

### ui-4e-layout-visual.spec.js

- **169/169 OK** — sin regresiones visuales.

---

## O. Procesos detectados al cerrar

- `TheBuryProyect.exe` PID 7756 — iniciado por esta tarea para Playwright.
- Debe cerrarse al finalizar la tarea si no estaba corriendo previamente.

---

## P. Próximo paso recomendado

**COTIZ-1B** — Descuentos por producto: agregar columnas de descuento (porcentaje e importe) en la tabla de productos del simulador. El backend ya lo soporta completamente.
