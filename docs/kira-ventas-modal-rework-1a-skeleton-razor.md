# KIRA-VENTAS-MODAL-REWORK-1A — Skeleton Razor del modal nueva venta

## Objetivo

Reemplazar el layout del modal `_VentaCrearModal.cshtml` de panel centrado (`max-w-7xl`) a wizard fullscreen (`fixed inset-0 flex flex-col`) con navegación por tabs y sidebar fijo, preservando todos los contratos funcionales.

---

## Rama

`kira/ventas-modal-rework-1a-skeleton-razor`

---

## Archivos modificados

| Archivo | Tipo |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Modificado (reescritura estructural) |
| `docs/kira-ventas-modal-rework-1a-skeleton-razor.md` | Nuevo (este documento) |

---

## Tipo de fase

Razor-only / skeleton / riesgo bajo-medio.

No se tocaron: controllers, services, models, migrations, endpoints, JS productivo, CSS productivo, Playwright specs.

---

## Cambios estructurales

### Layout raíz

**Antes:** `class="fixed inset-0 z-50 hidden overflow-y-auto"` con panel centrado `max-w-7xl rounded-2xl`

**Después:** `class="fixed inset-0 z-50 hidden flex flex-col"` con:
- `<header>` sticky con `#cv-title`, badge `#vm-estado-global`, botón cerrar
- `<nav role="tablist">` con 5 tabs (`#step-btn-cliente`, `#step-btn-productos`, `#step-btn-pago`, `#step-btn-credito`, `#step-btn-revision`)
- `<main class="flex-1 overflow-y-auto">` con grid 12 columnas
  - Área principal `lg:col-span-8`: 5 step panels (`#step-panel-cliente` … `#step-panel-revision`)
  - Sidebar `lg:col-span-4`: vendedor, observaciones, totals, confirmación

### Wizard steps

| ID tab | ID panel | Contenido principal |
|---|---|---|
| `#step-btn-cliente` | `#step-panel-cliente` | select-cliente, tipo-pago, condición de pago, ajuste por producto |
| `#step-btn-productos` | `#step-panel-productos` | tabla oc-scroll, cards carrito, selector-unidad |
| `#step-btn-pago` | `#step-panel-pago` | efectivo, transferencia, tarjeta, mercadopago, planes de pago, diagnóstico |
| `#step-btn-credito` | `#step-panel-credito` | verificación crediticia, cupo, mora |
| `#step-btn-revision` | `#step-panel-revision` | items resumen, alertas |

---

## Correcciones de riesgos detectados en fase 0B

| Riesgo 0B | Solución aplicada |
|---|---|
| `name="AplicarExcepcion"` incorrecto | Corregido a `name="AplicarExcepcionDocumental"` |
| `name` faltante en `#txt-excepcion-documental` | Agregado `name="MotivoExcepcionDocumentalCreate"` |
| `data-venta-modal-action/target` ausentes en `#btn-cargar-documentacion` | Preservados del modal actual |
| `data-oc-scroll` ausente en `#venta-detalles-scroll` | Preservado con todos sus attrs (`oc-scroll-*`) |
| Opciones de tipo-pago hardcodeadas | Reemplazadas por `@foreach (var tp in tiposPago)` |
| `@Html.AntiForgeryToken()` faltante | Agregado al inicio del formulario |
| Sidebar HTML truncado en 0B | Tomado del modal actual como fallback (permitido por prompt) |

---

## Elementos agregados (ausentes del nuevo HTML de 0B)

| ID | Ubicación | Motivo |
|---|---|---|
| `#panel-selector-unidad` | step-panel-productos | Trazabilidad; usado por JS de venta |
| `#hdn-tarjeta-nombre` | step-panel-pago, junto a select-tarjeta | Oculto requerido por flujo tarjeta |
| `#hdn-tarjeta-tipo` | step-panel-pago, junto a select-tarjeta | Oculto requerido por flujo tarjeta |
| `#hdn-configuracion-pago-plan-id` | step-panel-pago, tras grid tarjeta | Oculto requerido para planes |
| `#panel-planes-pago` | step-panel-pago | Sección planes de pago |
| `#lista-planes-pago` | dentro de panel-planes-pago | Lista dinámica de planes |
| `#configuracion-pagos-global-estado` | step-panel-pago | Estado global de configuración |
| `#panel-diagnostico-condiciones-pago` | step-panel-pago | Panel diagnóstico completo |
| `#panel-aviso-cuotas-sin-interes` | panel-tarjeta | Aviso cuotas sin interés |
| `#panel-credito-no-requerido` | step-panel-credito | Estado crédito no requerido |

---

## Elementos nuevos del diseño fullscreen

| ID | Ubicación |
|---|---|
| `#vm-estado-global` | Header — badge de estado general |
| `#cv-title` | Header — título del modal (para `aria-labelledby`) |
| `#step-btn-{cliente,productos,pago,credito,revision}` | Nav tablist — 5 tabs |
| `#step-panel-{cliente,productos,pago,credito,revision}` | Main — 5 paneles |
| `#venta-modal-caja-cerrada` | step-panel-cliente — aviso caja cerrada |
| `#venta-detalles-cards` | step-panel-productos — cards del carrito |
| `#venta-detalle-card-template` | step-panel-productos — template oculto |
| `#panel-mercadopago` | step-panel-pago — sección MercadoPago |
| `#revision-alertas` | step-panel-revision — grupo de alertas |
| `#detalle-items-badge` | Movido a dentro de `#step-btn-productos` |

---

## Contratos preservados

### IDs críticos (muestra — todos preservados)

`#venta-crear-form`, `#select-cliente`, `#select-tipo-pago`, `#select-condicion-pago`, `#venta-detalles-scroll`, `#lista-detalle-venta`, `#select-vendedor`, `#txt-observaciones`, `#input-monto-efectivo`, `#input-monto-transferencia`, `#select-tarjeta`, `#input-cuotas-tarjeta`, `#panel-documentacion-faltante`, `#panel-alerta-mora`, `#panel-cupo-insuficiente`, `#btn-confirmar`, `#vm-preconfirm-reminder`, `#txt-excepcion-documental`, `#btn-cargar-documentacion`, `#modal-pago-item`, `#modal-documentacion`, y todos los demás listados en 0B.

### Names para POST

`VendedorUserId`, `Observaciones`, `ClienteId`, `TipoPagoId`, `MontoEfectivo`, `MontoTransferencia`, `TarjetaId`, `CuotasTarjeta`, `AplicarExcepcionDocumental` (corregido), `MotivoExcepcionDocumentalCreate` (agregado).

### Data-attrs

`data-oc-scroll`, `oc-scroll-x`, `oc-scroll-panel`, `oc-scroll-left`, `oc-scroll-right` en `#venta-detalles-scroll`; `data-venta-modal-action`, `data-venta-modal-target` en `#btn-cargar-documentacion`.

### JS API

`onclick="VentaCrearModal.submit()"` preservado en `#btn-confirmar` con `class="vm-btn-confirm"`.

### ARIA

`role="dialog"`, `aria-modal="true"`, `aria-labelledby="cv-title"`, `role="tablist"`, `role="tab"`, `role="tabpanel"`, `role="alert"` en paneles de alerta, `role="note"` en `#vm-preconfirm-reminder`, `aria-live="polite"` en `#vm-estado-global`.

---

## Deuda remanente

| Ítem | Motivo |
|---|---|
| `#panel-mercadopago` sin `name` attrs | Backend model para MercadoPago pendiente de definición |
| CSS del wizard (`vm-step-*`, `vm-sidebar`, etc.) | Fase 1B — extracción CSS |
| Comportamiento JS de tabs | Fase 1C o bien `venta-create.js` ya lo maneja por ID |
| Sidebar HTML definitivo de la nueva maqueta | Pendiente de entrega del HTML nuevo del sidebar |

---

## Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | OK — sin errores |
| `dotnet test --configuration Release --filter "VentaCreate"` | 100/100 OK |
| `git diff --check` (modal file) | Sin trailing whitespace en `_VentaCrearModal.cshtml` |
| Verificación manual de 44 contratos críticos con Grep | Todos encontrados |

---

## Procesos

No se iniciaron servidores ni procesos de larga duración por esta tarea.

---

## Próximo paso recomendado

**KIRA-VENTAS-MODAL-REWORK-1B** — Extracción y escritura de CSS para el wizard fullscreen: clases `vm-step-*`, `vm-sidebar`, `vm-header`, estilos de tabs activos/inactivos, transiciones, responsive.
