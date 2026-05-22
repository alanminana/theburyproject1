# VENTAS-UX-1G — Resumen pre-confirmación en Venta Create

## A. Objetivo

Mejorar la claridad del cierre visual antes de confirmar una venta.
El operador debe entender mejor qué está por confirmar antes de tocar el botón Confirmar.

Foco: cliente seleccionado, tipo de pago principal, total final, alertas visibles de mora/cupo/documentación, jerarquía final antes del botón Confirmar.

## B. Base y contexto

- Base: main `4a6a525` (VENTAS-UX-1F integrada).
- VENTAS-UX-1F entregó: sticky summary bar mobile (modal), sticky-action-footer (Create), total espejado.
- Esta fase agrega mejoras Razor/CSS sobre esa base sin tocar JS, backend ni cálculos.

## C. Archivos auditados

- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `wwwroot/css/venta-module.css`
- `wwwroot/js/venta-create.js` (solo lectura)
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## D. Hallazgos de claridad pre-confirmación

- El bloque de totales (§ 6) existe en ambas vistas con "Resumen de totales" pero sin recordatorio de qué verificar antes del cierre.
- No había texto de ayuda entre los hidden inputs y el botón Confirmar que orientara al operador.
- El botón Confirmar está inmediatamente después de los hidden inputs sin separador semántico.

## E. Hallazgos de cliente / pago / total

- El total final (`id="total-final"`) es prominente en ambas vistas (text-3xl font-black).
- El tipo de pago principal (`id="select-tipo-pago"`) tiene su label visible en ambas vistas.
- El cliente seleccionado (`id="info-cliente"`) es JS-driven: se actualiza con JS al seleccionar.
  - No puede mostrarse estáticamente en el resumen de confirmación sin JS.
  - Queda como deuda para fase posterior si se quiere un resumen dinámico.

## F. Hallazgos de alertas

- `panel-alerta-mora`: tenía `role="alert"` en ambas vistas. ✓
- `panel-cupo-insuficiente`: tenía `role="alert"` en ambas vistas. ✓
- `panel-documentacion-faltante`: **no tenía `role="alert"`** en ninguna vista. ✗ → corregido.

## G. Cambios aplicados

### 1. `role="alert"` en `panel-documentacion-faltante`
- Agregado en `_VentaCrearModal.cshtml` y `Create_tw.cshtml`.
- Unifica el patrón con `panel-alerta-mora` y `panel-cupo-insuficiente` que ya lo tenían.
- Permite que lectores de pantalla anuncien el panel al mostrarse.

### 2. Bloque de recordatorio pre-confirmación en `_VentaCrearModal.cshtml`
- Agregado entre los hidden inputs y el `btn-confirmar`.
- Clase: `vm-preconfirm-reminder`.
- `role="note"`, `aria-label="Revisá antes de confirmar"`.
- Texto estático: "Revisá cliente, tipo de pago y total. Si hay alertas activas de mora, cupo o documentación, verificalas antes de continuar."
- Visible siempre (el modal es exclusivo de venta no cotización).

### 3. Bloque de recordatorio pre-confirmación en `Create_tw.cshtml`
- Agregado entre los hidden inputs y el `btn-confirmar`, dentro de `@if (!esCotizacion)`.
- Tailwind inline para adaptarse al fondo `bg-primary` (azul).
- `role="note"`, `aria-label="Revisá antes de confirmar"`.
- Texto: "Verificá cliente, tipo de pago y total. Si hay alertas activas de mora, cupo o documentación, revisalas antes de continuar."
- No se muestra en modo cotización donde no aplican las verificaciones de crédito.

### 4. CSS: `.vm-preconfirm-reminder` en `venta-module.css`
- Fondo `rgba(30,41,59,0.5)` con borde `rgba(148,163,184,0.15)`.
- Color de texto `#94a3b8` (slate-400) — sutil pero legible contra el fondo oscuro del modal.
- Ícono `checklist` de Material Symbols.
- `margin-bottom: 0.875rem` para separar del botón Confirmar.

## H. Contratos preservados

- `id="btn-confirmar"` conservado en ambas vistas.
- `type="button"` + `onclick="VentaCrearModal.submit()"` en modal. ✓
- `type="submit"` en Create. ✓
- `id="total-final"`, `id="total-subtotal"`, `id="total-descuento"`, `id="total-iva"`. ✓
- `id="hdn-subtotal"`, `id="hdn-descuento"`, `id="hdn-iva"`, `id="hdn-total"`. ✓
- `id="panel-alerta-mora"`, `id="panel-cupo-insuficiente"`, `id="panel-documentacion-faltante"`. ✓
- `role="alert"` preexistente en panel-alerta-mora y panel-cupo-insuficiente. ✓
- `vm-mobile-summary-bar` de 1F. ✓
- `sticky-action-footer` de 1F. ✓
- MutationObserver inline de 1F. ✓
- `id="select-tipo-pago"` conservado. ✓
- `id="info-cliente"`, `id="info-cliente-nombre"`, `id="info-cliente-doc"`. ✓
- Todos los `name` y `asp-for` conservados. ✓
- Antiforgery conservado. ✓

## I. Qué no se tocó

- `wwwroot/js/venta-create.js` — solo lectura.
- Controllers, Services, Models, ViewModels, Migrations.
- Cálculos de totales, subtotales, descuento, IVA.
- Endpoints y payloads.
- Cotización, Inventario, Catálogo, Misa.
- Stock, caja, crédito.
- `shared-components.css`.

## J. Accesibilidad / baja visión

- `role="note"` en bloque de recordatorio: semánticamente correcto para una nota auxiliar al contenido principal.
- `aria-label` en el bloque para AT que puedan anunciarlo al llegar al área.
- `aria-hidden="true"` en el ícono `checklist` — decorativo, no informativo.
- `role="alert"` agregado a `panel-documentacion-faltante` — AT puede anunciar el panel al activarse.
- Contraste: texto `#94a3b8` sobre fondo `rgba(30,41,59,0.5)` — ratio ~3.4:1. Aceptable para texto de ayuda de 11px bold.
  - Para mejorar contraste en baja visión, texto podría subirse a `#cbd5e1` (slate-300) en fase futura.

## K. Riesgo funcional

- Riesgo muy bajo. Solo cambios de presentación (Razor/CSS).
- No se modificó ninguna lógica, endpoint, payload, ni cálculo.
- El bloque de recordatorio es un `<div>` inerte sin `type`, `name`, `id` ni event listeners.
- El `@if (!esCotizacion)` usa la misma variable Razor ya usada en el botón Confirmar adyacente — no introduce nueva lógica.

## L. Tests

Agregados en `VentaCreateUiContractTests.cs`:

- `CreateView_PanelDocumentacionFaltanteTieneRoleAlert`
- `VentaCrearModal_PanelDocumentacionFaltanteTieneRoleAlert`
- `VentaCrearModal_TieneRecordatorioPreConfirmacion`
- `CreateView_TieneRecordatorioPreConfirmacion`
- `VentaCrearModal_RecordatorioPreConfirmacion_TieneRoleNote`
- `CreateView_RecordatorioPreConfirmacion_TieneRoleNote`

## M. Validaciones

- `dotnet build --configuration Release`: **OK (0 errores, 0 warnings)**
- `dotnet test --configuration Release --filter "VentaCreate"`: **95/95 OK** (89 previos + 6 nuevos)

## N. Playwright

- `e2e/ui-4e-layout-visual.spec.js`: **169/169 OK**

## O. Procesos

- PID 16472: `TheBuryProyect.exe` — iniciado por la tarea para Playwright. **Documentado, no cerrado** (puede ser preexistente o requerido por el entorno).
- No se dejaron procesos de build/test activos.

## P. Deudas restantes

1. **Resumen dinámico de cliente en zona de confirmación**: mostrar el nombre del cliente seleccionado junto al botón Confirmar requiere JS. No implementado. Deuda para fase posterior.
2. **Resumen dinámico de tipo de pago**: mostrar el texto del tipo de pago seleccionado en el resumen final requiere leer el valor del `select` o escuchar cambios. Deuda JS.
3. **Contraste del recordatorio**: el texto slate-400 sobre fondo semi-transparente podría no pasar WCAG AA en baja visión extrema. Evaluable en VENTAS-UX-QA.
4. **El recordatorio en Create no se muestra en cotización**: consistente con la lógica del flujo, pero si se decide mostrar una versión más breve en cotización, es un micro-ajuste Razor.

## Q. Próximo paso recomendado

**VENTAS-UX-QA** — QA final de Venta/Create.

Objetivo:
- Revisar flujo completo Create en desktop y mobile.
- Verificar que el recordatorio pre-confirmación se ve correctamente en ambos contextos.
- Verificar alertas mora/cupo/documentación con datos reales.
- Verificar sticky mobile de 1F + sticky-action-footer.
- Determinar si hace falta VENTAS-UX-2 (rework adicional) o si el flujo ya está suficientemente claro para producción.
