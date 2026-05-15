# Fase Cotización V1.4 — UI conversión Cotización a Venta

## A. Objetivo

Implementar UI mínima para convertir una Cotización persistida en Venta usando los endpoints de V1.3, con preview de errores/advertencias, ClienteIdOverride, política de precios y redirect a la venta creada.

---

## B. Diagnóstico previo

### B.1 Estado de Detalles_tw.cshtml antes de V1.4

- Vista `Detalles_tw.cshtml` renderizada por `CotizacionController.Detalles` con modelo `CotizacionResultado`.
- Solo tenía botón "Volver" — sin acción de conversión.
- Sin referencia a `EstadoCotizacion` para condicionar visibilidad.
- Sin script de detalle; `cotizacion-simulador.js` pertenece exclusivamente al simulador (Index_tw).

### B.2 Estados que permiten conversión

`CotizacionConversionService.ValidarEstadoConvertible` bloquea:
- `ConvertidaAVenta` → ya convertida
- `Cancelada` → cancelada
- `Vencida` (enum) → vencida
- `Borrador` → solo Emitida es convertible
- `Emitida` con `FechaVencimiento` pasada → vencida por fecha

**Solo `EstadoCotizacion.Emitida` sin fecha vencida puede iniciar conversión.**

### B.3 Ruta de edición de venta

`VentaController.Edit` acepta `EstadoVenta.Cotizacion` como editable (línea 425):
```csharp
if (venta.Estado != EstadoVenta.Cotizacion && venta.Estado != EstadoVenta.Presupuesto)
    return RedirectToAction(nameof(Details), new { id });
```
Redirect destino: `/Venta/Edit/{ventaId}`.

### B.4 Antiforgery

`CotizacionApiController` es `[ApiController]` sin `[ValidateAntiForgeryToken]`. Endpoints de conversión no requieren token antiforgery. Se llaman con `Content-Type: application/json`.

### B.5 Búsqueda de clientes

Endpoint existente en `CotizacionController.BuscarClientes`: `GET /Cotizacion/BuscarClientes?term=...&take=...`. Devuelve `{ id, nombre, apellido, display, ... }`. Reutilizado en `ClienteIdOverride`.

### B.6 Decisión: modal vs inline

Se eligió **modal** con `position: fixed` para no interrumpir el layout del detalle. El modal es generado server-side en el mismo `@if (puedeConvertir)` block, solo presente cuando el estado permite conversión.

---

## C. Clasificación de componentes

| Componente | Clasificación | Decisión |
|---|---|---|
| `CotizacionController` | canónico nuevo | Agregar `@using EstadoCotizacion` en vista, no modificar controller |
| `CotizacionApiController` | canónico nuevo | Usar endpoints existentes sin cambiar |
| `Detalles_tw.cshtml` | canónico nuevo | Modificar — agregar panel + modal + script |
| `cotizacion-conversion.js` | canónico nuevo | Crear archivo dedicado |
| `cotizacion-simulador.js` | canónico nuevo | No tocar — pertenece al simulador |
| `VentaController` | canónico Venta | Solo lectura para detectar ruta Edit |
| `VentaService` | canónico Venta | No tocar |
| `ICotizacionConversionService` | canónico nuevo | No modificar |
| `CotizacionConversionModels` | canónico nuevo | No modificar |

---

## D. Flujo UI implementado

```
[Detalles cotización - EstadoCotizacion.Emitida]
        ↓
[Botón "Convertir a Venta" visible]
        ↓
[Click] → POST /api/cotizacion/{id}/conversion/preview
        ↓
[Modal abierto - cargando preview]
        ↓
[Render preview]
  ├─ Errores bloqueantes → mostrar, deshabilitar confirmación
  ├─ Advertencias → mostrar, requerir checkbox
  ├─ ClienteFaltante → mostrar buscador de cliente
  ├─ TotalCotizado → mostrar
  └─ Política de precios (cotizado / actual) → radios
        ↓
[Usuario completa ClienteIdOverride si falta]
[Usuario elige política de precios]
[Usuario confirma advertencias si las hay]
[Botón "Confirmar Conversión" se habilita]
        ↓
[Click Confirmar] → POST /api/cotizacion/{id}/conversion/convertir
        ↓
  ├─ Exitoso → redirect /Venta/Edit/{ventaId}
  └─ Error → mostrar errores en modal, permitir reintentar
```

---

## E. Preview

**Endpoint**: `POST /api/cotizacion/{id}/conversion/preview`

**Datos renderizados**:
- `errores[]` → lista roja, bloquea confirmación si hay errores
- `advertencias[]` → lista amarilla, requiere checkbox
- `clienteFaltante` → muestra panel de búsqueda de cliente
- `totalCotizado` → monto visible en modal
- `convertible` → controla habilitación del botón confirmar

**Sin datos no renderizados**: no se muestra tabla de detalles de productos en el modal (alcance mínimo V1.4).

---

## F. Confirmación

**Endpoint**: `POST /api/cotizacion/{id}/conversion/convertir`

**Body enviado**:
```json
{
  "usarPrecioCotizado": true | false,
  "confirmarAdvertencias": true | false,
  "clienteIdOverride": <int> | null,
  "observacionesAdicionales": null
}
```

**Lógica de habilitación del botón confirmar**:
- `convertible = true` en preview
- Si `clienteFaltante` → `clienteIdInput.value !== ''`
- Si `advertencias.length > 0` → `checkAdvertencias.checked`

---

## G. ClienteIdOverride

- Solo aparece si `previewData.clienteFaltante === true`.
- Usa `GET /Cotizacion/BuscarClientes?term=...&take=10` (endpoint existente).
- Busqueda con debounce 280ms y AbortController para cancelar requests previos.
- Selección via click en dropdown → ID se guarda en input hidden.
- `clienteIdOverride` se envía como int en el body de convertir.

---

## H. Política de precios

- Radio "Usar precio cotizado (recomendado)" → `usarPrecioCotizado: true`
- Radio "Usar precio actual de lista" → `usarPrecioCotizado: false`
- Default: precio cotizado seleccionado.

---

## I. Redirect a Venta

- `VentaController.Edit` acepta `EstadoVenta.Cotizacion` como editable.
- Redirect: `window.location.href = urls.ventaEdit + data.ventaId`
- Donde `urls.ventaEdit = data-venta-edit-url` del panel = `~/Venta/Edit/`
- URL final: `/Venta/Edit/{ventaId}`

---

## J. Tests

4 tests nuevos en `CotizacionControllerUiTests.cs`:

| Test | Qué verifica |
|---|---|
| `DetallesView_ContieneBotonConversionYScriptConversion` | View tiene atributos data-, modal, y script correcto |
| `DetallesView_MuestraBadgeParaEstadosNoConvertibles` | View tiene badges para ConvertidaAVenta y Cancelada |
| `ScriptConversion_NoDependeDeVentaCreateNiApiVentas` | JS no referencia venta-create ni /api/ventas/ |
| `ScriptConversion_UsaTextContentParaDatosExternos` | JS usa textContent y clearChildren para datos externos (no XSS) |

**Total tests**: 94 Cotizacion / 353 Cotizacion+Permiso+Seguridad+Controller — todos passing.

---

## K. Qué NO se tocó

- `VentaController`, `VentaService`, `VentaApiController`
- `venta-create.js`
- `Views/Venta/*`
- `Services/DevolucionService.cs`
- `Views/Devolucion/*`, `Views/RMA/*`, `Views/NotaCredito/*`
- `cotizacion-simulador.js`
- Cualquier migración
- `CotizacionConversionService` — se usa sin cambiar
- `ICotizacionConversionService` — se usa sin cambiar
- `CotizacionConversionModels` — se usa sin cambiar

---

## L. Riesgos y deuda remanente

| Item | Descripción |
|---|---|
| IVA en VentaDetalle convertido | `IVAUnitario = 0` en ConstruirDetalles. Deuda conocida de V1.3. |
| Trazabilidad bidireccional | `CotizacionOrigenId` en Venta no existe aún. |
| Tabla CotizacionConversion | No hay registro de conversión separado. |
| Permiso granular | No existe `cotizaciones.convert` — usa `cotizaciones.create`. |
| Vencimiento automático | No hay job que pase estado a `Vencida` automáticamente. |
| Preview sin tabla de detalles | Modal no muestra tabla de productos con cambios de precio unitarios. Alcance mínimo. |
| ClienteIdOverride input libre | No valida que el ID exista antes de enviar (el backend lo valida). |
| Cierre de modal con navegación pendiente | Si la conversión inicia redirect y el usuario cierra el modal antes, la navegación sigue. |

---

## M. Checklist actualizado

### Carlos — V1.4

- [x] Diagnóstico Ventas/Cotización
- [x] Diseño V1 Cotización no persistida
- [x] V1A DTOs/interfaz/tests base
- [x] V1B cálculo real read-only básico
- [x] V1C API/controller read-only
- [x] V1D crédito personal simulado read-only
- [x] V1E UI Cotización separada
- [x] V1.1 persistencia mínima
- [x] V1.2 diseño conversión Cotización → Venta
- [x] V1.3 implementación conversión controlada
- [x] V1.4 UI conversión Cotización → Venta

### Pendiente post V1.4

- [ ] Trazabilidad bidireccional `CotizacionId ↔ VentaId`
- [ ] IVA correcto en `VentaDetalle` convertido
- [ ] Permiso granular `cotizaciones.convert`
- [ ] Impresión/envío de cotización
- [ ] Vencimiento automático
- [ ] Cancelación avanzada
- [ ] Tabla modal de productos con cambios de precio

---

## N. Archivos modificados / creados

| Archivo | Tipo | Descripción |
|---|---|---|
| `Views/Cotizacion/Detalles_tw.cshtml` | Modificado | Panel conversión + modal |
| `wwwroot/js/cotizacion-conversion.js` | Creado | JS conversión autónomo |
| `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs` | Modificado | 4 tests nuevos |
| `docs/fase-cotizacion-v1-4-ui-conversion-venta.md` | Creado | Este documento |
