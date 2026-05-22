# VENTAS-UX-1C — Copy y accesibilidad en Venta/Create

## A. Objetivo

Aplicar micro-ajustes de copy y accesibilidad en las vistas de Venta/Create sin modificar
comportamiento funcional, cálculos, payloads, stock, caja, crédito ni Cotización.

## B. Base y contexto

- Rama base: `main` @ `1ab59b3` (VENTAS-UX-1B integrada).
- VENTAS-UX-1A y VENTAS-UX-1B ya integradas y en verde.
- Rama de trabajo: `kira/ventas-ux-1c-copy-accesibilidad-venta-create`.

## C. Hallazgos tomados de VENTAS-UX-1B

VENTAS-UX-1B documentó estos micro-ajustes pendientes:

1. Armonizar "Tipo de pago" a "Tipo de pago principal" en `Create_tw.cshtml`.
2. Cambiar "Tipo predeterminado del sistema" por "Igual al pago principal de la venta" en `select-tipo-pago-item`.
3. Agregar `for="select-tipo-pago-item"` al label correspondiente.
4. Agregar `for` a labels de tarjeta, cuotas y autorización.
5. Agregar `role="alert"` a `panel-alerta-mora`.
6. Agregar `role="alert"` a `panel-cupo-insuficiente`.
7. Agregar tests de contrato.

## D. Cambios aplicados

### Views/Venta/Create_tw.cshtml

| Línea | Cambio |
|-------|--------|
| ~202 | Label "Tipo de pago" → "Tipo de pago principal" + `for="select-tipo-pago"` |
| ~409 | Label "Tarjeta" + `for="select-tarjeta"` |
| ~424 | Label "Cuotas" + `for="select-cuotas-tarjeta"` |
| ~434 | Label "N° Autorización" + `for="txt-num-autorizacion-tarjeta"` |
| ~623 | `panel-cupo-insuficiente`: agregado `role="alert"` |
| ~640 | `panel-alerta-mora`: agregado `role="alert"` |

### Views/Venta/_VentaCrearModal.cshtml

| Línea | Cambio |
|-------|--------|
| ~770 | Label "Tipo de pago para este producto" + `for="select-tipo-pago-item"` |
| ~772 | Option vacía: "Tipo predeterminado del sistema" → "Igual al pago principal de la venta" |
| ~547 | `panel-cupo-insuficiente`: agregado `role="alert"` (antes de `class`) |
| ~562 | `panel-alerta-mora`: agregado `role="alert"` (antes de `class`) |

### TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs

- Actualizada aserción en `CreateView_TieneSelectorTipoPagoGeneralVisible` (línea 23): refleja label con `for` y texto "Tipo de pago principal".
- Actualizada aserción en `VentaCreate_View_ConservaTipoPagoPrincipalVisible` (línea 66): idem.
- Agregados 7 tests de contrato nuevos bajo comentario `// ── VENTAS-UX-1C`:
  - `CreateView_LabelTipoPagoPrincipalTieneForYTextoArmonizado`
  - `CreateView_PanelAlertaMoraTieneRoleAlert`
  - `CreateView_PanelCupoInsuficienteTieneRoleAlert`
  - `CreateView_LabelesTarjetaCuotasAutorizacionTienenFor`
  - `ModalPagoItem_LabelTipoPagoTieneForSelectTipoPagoItem`
  - `ModalPagoItem_OpcionDefaultDiceIgualAlPagoPrincipal`
  - `VentaCrearModal_PanelAlertaMoraTieneRoleAlert`
  - `VentaCrearModal_PanelCupoInsuficienteTieneRoleAlert`

## E. Archivos modificados

- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/ventas-ux-1c-copy-accesibilidad-venta-create.md` (este archivo)

## F. Contratos preservados

- IDs de elementos: sin cambios.
- `name` / `asp-for` / `data-*`: sin cambios.
- Values del select-tipo-pago-item (0–8): sin cambios.
- Payloads: sin cambios.
- Endpoint `/api/ventas/`: sin cambios.

## G. Qué no se tocó

- `wwwroot/js/venta-create.js` — solo lectura de referencia, no modificado.
- `showFeedback` — no modificado.
- Controllers / Services / Models / Migrations / DTOs.
- CajaService / CreditoService / MovimientoStockService / CotizacionConversionService.
- CSS compartido (`shared-components.css`, `venta-module.css`).
- Flujo de Cotización.
- Lógica de cálculo / stock / caja / crédito.

## H. Accesibilidad

- Labels con `for` correctamente asociados a sus controles en toda la sección de cobro.
- `role="alert"` en `panel-alerta-mora` y `panel-cupo-insuficiente` permite que lectores
  de pantalla anuncien automáticamente estos paneles cuando se vuelven visibles.

## I. Riesgo funcional

Riesgo: **mínimo**. Solo atributos HTML semánticos (`for`, `role="alert"`) y texto visible.
Sin cambios en IDs, names, values, payloads ni lógica JS.

## J. Validaciones

- `dotnet build --configuration Release`: OK, 0 errores, 0 advertencias.
- `git diff --check`: OK.

## K. Tests

| Suite | Resultado |
|-------|-----------|
| VentaCreate (68 tests) | 68/68 OK |
| LayoutUiContractTests | 57/57 OK |
| Cotización | 170/170 OK |
| Layout\|Shared\|UiContract\|… | 243/243 OK |

## L. Playwright

| Spec | Resultado |
|------|-----------|
| ui-4e-layout-visual.spec.js | 169/169 OK |
| cotizacion-simulador.spec.js | 57/57 OK |
| cotizacion-conversion.spec.js | 29/29 OK |
| venta-pago-por-item.spec.js | 1 passed + 42 skipped OK |

## M. Procesos

- App `TheBuryProyect` iniciada por esta tarea (PID 17256) para correr Playwright.
  Debe cerrarse al finalizar.
- Proceso MSBuild (PID 17580) preexistente, no iniciado por esta tarea.

## N. Deudas

- La rama `origin/kira/ventas-create-frontend-tipo-pago-ux` (vieja, descartada) modifica
  `venta-create.js` e `Index_tw.cshtml`. Queda pendiente evaluar si esos cambios son
  relevantes para una fase futura.
- El modal `_VentaCrearModal.cshtml` tiene varios labels sin `for` en la sección de filtros
  de búsqueda de productos (fuera del alcance de esta fase).

## O. Próximo paso recomendado

**VENTAS-UX-1D** — auditoría de accesibilidad en la tabla de detalle de productos dentro
del modal de nueva venta: headers con `scope`, `aria-label` en botones de acción por fila,
y contraste de textos de estado (`text-slate-400` en labels de estado). Bajo riesgo,
sin tocar cálculos ni backend.
