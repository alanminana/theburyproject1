# KIRA-VENTAS-MODAL-REWORK-1F - Pago principal y pago por producto

## A. Objetivo

Conectar visualmente el tipo de pago principal, los totales, los productos, el panel de revision y la confirmacion del wizard fullscreen de Nueva Venta, sin cambiar reglas de negocio, calculos, backend, endpoints ni payloads.

## B. Base y contexto

- Base declarada: `main` actual `5958f26` - KIRA-VENTAS-MODAL-REWORK-1E integrada.
- 1E dejo `venta-modal-rework.js` con `evaluateStepStates()`, `refreshState()`, `goToFirstInvalidStep()`, `initStateObservers()` e `initSubmitNavigation()`.
- `venta-create.js` no habia sido modificado en 1E.

## C. Deudas tomadas desde 1E

- El wizard ya navegaba y pintaba estados, pero no copiaba datos reales a sidebar/revision/confirmacion.
- El total mobile solo espejaba `#total-final`; faltaba homogeneizarlo con otros nodos visuales.
- `#modal-pago-item` existia, pero no habia flujo productivo actual de pago por item desde `venta-create.js`.

## D. Archivos auditados

- `docs/kira-ventas-modal-rework-1a-skeleton-razor.md`
- `docs/kira-ventas-modal-rework-1b-css-wizard.md`
- `docs/kira-ventas-modal-rework-1c-js-wizard.md`
- `docs/kira-ventas-modal-rework-1d-integracion.md`
- `docs/kira-ventas-modal-rework-1e-navegacion-inteligente.md`
- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/js/venta-create.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## E. Archivos modificados

- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/js/venta-modal-rework.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-modal-rework-1f-pago-principal-producto.md`

## F. Flujo de pago actual detectado

`venta-create.js` mantiene la autoridad del flujo:

- `#select-tipo-pago` dispara `onTipoPagoChange()`.
- `onTipoPagoChange()` muestra/oculta `#panel-tarjeta`, `#panel-cheque`, `#panel-credito-personal` y verificacion crediticia.
- La carga de tarjetas/planes globales queda en `venta-create.js`.
- `recalcularTotales()` llama `/api/ventas/CalcularTotalesVenta`.
- `actualizarTotalesUI()` escribe `#total-subtotal`, `#total-descuento`, `#total-iva`, `#total-final` y los hidden totals.

## G. Flujo de pago por producto detectado

El submodal `#modal-pago-item` existe con:

- `#modal-pago-item-titulo`
- `#select-tipo-pago-item`
- `#modal-pago-item-planes`
- `#modal-pago-item-resumen`
- `#btn-guardar-pago-item`

Pero `venta-create.js` protege que Nueva Venta no renderice `btn-configurar-pago-item`, no genere hidden inputs por detalle y no llame endpoints legacy de medios por producto. Por eso esta fase preserva el submodal y mejora su resumen visual, sin reintroducir payload ni logica legacy.

## H. Sincronizacion implementada

En `venta-modal-rework.js` se agregaron:

- `syncPaymentSummary()`
- `syncTotalsSummary()`
- `syncReviewPanel()`
- `syncConfirmationPanel()`
- `syncItemPaymentModal()`
- `syncVisualSummaries()`

`refreshState()` llama `syncVisualSummaries()` despues de actualizar estados del wizard.

## I. Totales sincronizados

Se leen valores existentes, sin recalcular:

- `#total-subtotal`
- `#total-descuento`
- `#total-iva`
- `#total-final`

Y se copian a:

- `[data-side-subtotal]`
- `[data-side-descuento]`
- `[data-side-iva]`
- `[data-side-total]`
- `[data-rev-subtotal]`
- `[data-rev-descuento]`
- `[data-rev-iva]`
- `[data-rev-total]`
- `[data-mobile-total]`
- `[data-conf-total]`

## J. Revision sincronizada

El paso Revision ahora tiene hooks visuales para:

- `[data-rev-cliente]`
- `[data-rev-fecha]`
- `[data-rev-pago]`
- `[data-rev-items]`
- totales `data-rev-*`

`#revision-alertas` se actualiza con `replaceChildren()` y `textContent`, sin `innerHTML`.

## K. Confirmacion final sincronizada

Se agrego un resumen dentro del recordatorio pre-confirmacion:

- `[data-conf-cliente]`
- `[data-conf-items]`
- `[data-conf-pago]`
- `[data-conf-total]`
- `[data-conf-credito]`

El boton final conserva `onclick="VentaCrearModal.submit()"`. No se reemplazo el submit ni se agrego un submit intermedio.

## L. Submodal pago por producto

`syncItemPaymentModal()` preserva el submodal y solo ajusta texto visual:

- titulo fallback si esta vacio;
- opcion default "Igual al pago principal";
- mensaje visual si no hay planes cargados.

No se agrego serializacion por item, no se tocaron endpoints y no se reintrodujo `btn-configurar-pago-item`.

## M. Cambios en venta-create.js si hubo

No hubo cambios en `wwwroot/js/venta-create.js`.

## N. Contratos preservados

Preservados:

- `VentaCrearModal.submit()`
- `VentaCrearModal.open()`
- `VentaCrearModal.close()`
- `VentaModalRework.refreshState()`
- `VentaModalRework.activateStep()`
- `#venta-form`
- `#btn-confirmar`
- `#select-tipo-pago`
- paneles de pago y credito existentes
- `#modal-pago-item`
- `#btn-guardar-pago-item`
- `#tbody-detalles`
- `#detalles-hidden-inputs`
- totales visibles y hidden totals
- antiforgery, `name`, `id`, endpoints y payloads.

## O. Que no se toco

- Controllers
- Services
- Models
- Migrations
- Endpoints
- Payloads
- Stock
- Caja
- Credito backend
- Cotizacion backend
- CSS global
- Playwright specs
- Calculo de totales, intereses, IVA o validacion backend

## P. Riesgo funcional

Riesgo bajo-medio. El cambio agrega sincronizacion visual por observers y listeners sobre DOM existente. La fuente de verdad de calculos sigue siendo `venta-create.js` y backend.

Riesgo acotado: los observers llaman `refreshState()` con frecuencia cuando cambian totales o detalles. `setText()` evita escrituras cuando el texto ya coincide para no crear loops de MutationObserver.

## Q. Validaciones

- `node --check wwwroot/js/venta-modal-rework.js` - OK.
- `dotnet build --configuration Release` - primer intento excedio 120s.
- `dotnet build --configuration Release --no-restore -nodeReuse:false` - OK, 0 advertencias, 0 errores.
- `git diff --check` - falla solo por trailing whitespace preexistente en `AGENTS.md` y `CLAUDE.md`, no tocados por esta fase.
- `git diff --check -- wwwroot/js/venta-modal-rework.js Views/Venta/_VentaCrearModal.cshtml TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs docs/kira-ventas-modal-rework-1f-pago-principal-producto.md` - OK.

## R. Tests ejecutados

- `dotnet test --configuration Release --filter "VentaCreate" --no-build`
- Resultado: 128/128 OK, 0 fallidos, 0 omitidos.

## S. Playwright ejecutado u omitido

No ejecutado.

Motivo exacto:

- No habia app escuchando en `localhost:5187`.
- No habia proceso `TheBuryProyect`/app corriendo.
- No existe spec actual que apunte al wizard modal `#modal-crear-venta`, `data-side-*` o `data-rev-*`.
- El prompt pidio no modificar specs Playwright.

No se afirma validacion visual OK; queda para QA con app disponible.

## T. Deudas restantes

- El flujo real de pago por producto sigue sin entrada desde filas de detalle por contrato actual de `venta-create.js`.
- `#modal-confirmar-operacion` no existe en el sistema actual; se sincronizo la confirmacion visual existente sin reemplazar `VentaCrearModal.submit()`.
- QA visual desktop/mobile queda sujeto a disponibilidad de la app.

## U. Proximo prompt recomendado

**KIRA-VENTAS-MODAL-REWORK-1G - Credito/documentacion/excepcion**
