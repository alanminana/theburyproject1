# VENTAS-UX-1A — Tipo de pago principal visible en Venta/Create

**Rama:** `kira/ventas-ux-1a-tipo-pago-principal-visible`
**Base:** `main` @ `153dc60` (COTIZ-QA-3)
**Fecha:** 2026-05-22

---

## A. Objetivo

Hacer visible el campo "Tipo de pago principal" en el modal de creación de venta (`_VentaCrearModal.cshtml`), mejorando la claridad del flujo y reduciendo la confusión entre pago global y pago específico por producto.

---

## B. Rama vieja evaluada y motivo de descarte

- **Rama evaluada:** `origin/kira/ventas-create-frontend-tipo-pago-ux`
- **Clasificación:** SALIDA C — descartar / rehadir desde main
- **Base de esa rama:** `e0d7603` (muy anterior al trabajo de UI, UX-COMERCIAL y Cotización)
- **Motivo de descarte:** merge directo destruiría tests, CSS del design system, specs Playwright y documentación reciente. No se mergeó ni se hizo cherry-pick completo.

---

## C. Cambios portados (desde commit 7b5e9bb como referencia)

Reimplementados manualmente sobre `main`:

1. `_VentaCrearModal.cshtml` — select-tipo-pago visible (eliminar `class="hidden"` del wrapper)
2. Label "Tipo de pago principal" con ícono `payments`
3. Texto de ayuda bajo el select
4. Copy paso 1 actualizado (menciona tipo de pago principal)
5. Copy paso 3 actualizado (referencia al tipo de pago principal elegido)
6. Copy modal pago-item actualizado (referencia al tipo de pago principal de la venta)
7. 5 tests UI contract portados/adaptados

---

## D. Cambios descartados

- Nada de `venta-create.js`
- Nada de `showFeedback()`
- Nada de controllers/services/models/migrations
- Nada de CSS compartido
- Nada de Cotización ni conversión Cotización → Venta
- Nada de stock/caja/crédito

---

## E. Archivos modificados

| Archivo | Tipo de cambio |
|---------|---------------|
| `Views/Venta/_VentaCrearModal.cshtml` | UX — visibilidad y copy |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | +5 tests UI contract |
| `docs/ventas-ux-1a-tipo-pago-principal-visible.md` | Documentación de fase |

**No modificados (contrato preservado):**
- `wwwroot/js/venta-create.js`
- `Controllers/`
- `Services/`
- `Models/`
- `ViewModels/`
- `Migrations/`
- `wwwroot/css/shared-components.css`
- Cualquier archivo de Cotización

---

## F. Contratos preservados

- `id="select-tipo-pago"` — mismo id, mismo name, mismos data-*, mismos asp-*, mismo vm-select
- `select-tipo-pago` sigue siendo el selector JS principal en `venta-create.js`
- `onTipoPagoChange` listener preservado
- `showFeedback()` sin cambios
- `alert-erp` sin cambios
- Estructura del modal intacta
- Antiforgery token intacto
- Scripts intactos
- Paneles de pago (tarjeta, cheque, crédito, planes) intactos

---

## G. Cambios que debería notar el usuario

- Campo "Tipo de pago principal" visible en el modal de venta (paso 1 — Datos generales)
- Label con ícono `payments` y texto de ayuda explicativo
- Copy más claro sobre pago principal vs pago por producto
- Texto del modal pago-item referencia al tipo de pago principal de la venta

**No debería notar:**
- Cambios en cálculos, stock, caja, crédito
- Cambios en endpoints o payloads
- Cambios en Cotización o conversión
- Cambios en backend

---

## H. Validaciones ejecutadas

| Validación | Resultado |
|-----------|-----------|
| `dotnet build --configuration Release` | OK — 0 errores |
| `git diff --check` | OK — EXIT:0 |

---

## I. Tests .NET

| Suite | Resultado |
|-------|-----------|
| `--filter "VentaCreate"` | 60/60 OK (+5 nuevos) |
| `--filter "LayoutUiContractTests"` | 57/57 OK |
| `--filter "Cotizacion"` | 170/170 OK |
| `--filter "Layout|Shared|...|UiContract|..."` | 235/235 OK |

### Tests nuevos portados

1. `VentaCrearModal_MuestraTipoPagoPrincipalVisible` — select visible, sin `class="hidden"` en contexto inmediato
2. `VentaCrearModal_NoDiceQueTipoPagoSoloSeConfiguraDesdeCadaProducto` — old copy eliminado
3. `VentaCrearModal_AclaraPagoPrincipalYAjustePorProducto` — copy nuevo coherente
4. `VentaCreate_View_ConservaTipoPagoPrincipalVisible` — Create_tw.cshtml sin regresión
5. `VentaCreateJs_SigueUsandoSelectTipoPago` — contrato JS documentado

---

## J. Playwright

| Suite | Resultado |
|-------|-----------|
| `ui-4e-layout-visual.spec.js` | 169/169 OK |
| `cotizacion-simulador.spec.js` | 57/57 OK |
| `cotizacion-conversion.spec.js` | 29/29 OK |
| `venta-pago-por-item.spec.js` | 1 passed, 42 skipped (skips esperados por datos) |

---

## K. Procesos

- App iniciada vía `dotnet TheBuryProyect.dll --urls http://localhost:5187` para Playwright (PID 22772)
- App levantada antes de esta tarea: no — iniciada por esta tarea
- Recomendación: cerrar el proceso PID 22772 si ya no se necesita

---

## L. Riesgos y deuda remanente

- **Bajo riesgo:** cambio puramente de visibilidad y copy en el modal. JS ya tenía el select referenciado.
- **Deuda:** el spec `venta-pago-por-item.spec.js` tiene 42 skips por falta de datos de productos. No es regresión de esta tarea.
- **Sin deuda nueva introducida.**

---

## M. Próximo paso recomendado

- **VENTAS-UX-1B** (si existe): continuar mejoras visuales del modal de venta (hero de tipo de pago, feedback de cobro, etc.)
- Alternativamente: iniciar siguiente frente de Venta funcional o UI pendiente.
