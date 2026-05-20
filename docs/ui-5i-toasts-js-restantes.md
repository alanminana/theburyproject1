# UI-5I — Normalización de Toasts JS Restantes

## A. Objetivo

Normalizar los toasts dinámicos JS restantes en `venta-create.js`, `catalogo-index.js` y `proveedor-index.js` aplicando el patrón canónico `.alert-erp` ya validado en `caja-index.js` durante UI-5G.

## B. Archivos auditados

| Archivo | Toast encontrado | Problema |
|---|---|---|
| `wwwroot/js/caja-index.js` | `showPageFeedback()` | Referencia canónica — no modificado |
| `wwwroot/js/shared-ui.js` | `autoDismissToasts()` | Hook `.toast-msg` confirmado — no modificado |
| `wwwroot/js/venta-create.js` | `showFeedback()` | Clases Tailwind inline en `palette` |
| `wwwroot/js/catalogo-index.js` | listener `catalogo:toast` | Tailwind inline + `innerHTML` |
| `wwwroot/js/proveedor-index.js` | listener `proveedor:toast` | Tailwind inline + `innerHTML` |

## C. Patrones encontrados

### venta-create.js — `showFeedback()`
- Usaba objeto `palette` con clases Tailwind inline: `border-primary/20 bg-primary/10 text-primary`, `border-amber-500/20 bg-amber-500/10 text-amber-600 dark:text-amber-400`, `border-red-500/20 bg-red-500/10 text-red-500`.
- Ya usaba `createElement`/`textContent` — sin innerHTML.
- Ya tenía `role` aria (`alert`/`status`).

### catalogo-index.js — `catalogo:toast`
- Usaba `div.className` con larga cadena Tailwind + condicional binario `isError`.
- Usaba `div.innerHTML` con `<span>` de ícono y texto.
- Sin `role` aria.

### proveedor-index.js — `proveedor:toast`
- Idéntico patrón a catalogo-index.js.

## D. Archivos modificados

- `wwwroot/js/venta-create.js`
- `wwwroot/js/catalogo-index.js`
- `wwwroot/js/proveedor-index.js`
- `docs/ui-5i-toasts-js-restantes.md` (este archivo)

## E. Cambios aplicados

### venta-create.js
Función `showFeedback(message, tone)` (línea ~179):
- Reemplazado objeto `palette` con clases Tailwind inline por `typeMap` con `alert-erp-{type}`.
- `toast.className` ahora es `toast-msg alert-erp ${v.cls}`.
- Reemplazado `createElement('p')` + append por `createTextNode` directamente en el toast.
- `role` aria preservado mediante `typeMap` (`alert` para error/warning, `status` para info).
- Lógica `autoDismissToasts` preservada sin cambios.

### catalogo-index.js
Listener `catalogo:toast`:
- Eliminado `isError` booleano.
- Introducido `typeMap` con tres variantes: `success`, `error`, `warning`.
- `div.className` simplificado a `toast-msg alert-erp {v.cls} fixed bottom-4 right-4 z-[60] shadow-lg`.
  - `fixed bottom-4 right-4 z-[60]`: preservado — posicionamiento funcional no cubierto por `.alert-erp`.
  - `shadow-lg`: preservado como decoración ligera.
- Reemplazado `div.innerHTML` por `createElement`/`textContent` (iconSpan + createTextNode).
- Agregado `div.setAttribute('role', v.role)` para accesibilidad.

### proveedor-index.js
Mismo cambio que catalogo-index.js aplicado al listener `proveedor:toast`.

## F. Contratos preservados

- Nombres de función: `showFeedback`, `clearFeedback` sin cambios.
- Eventos: `catalogo:toast`, `proveedor:toast` sin cambios.
- Hook `.toast-msg`: presente en todos los toasts.
- Auto-dismiss via `TheBury.autoDismissToasts()`: preservado.
- Duración: 4000ms (catalogo/proveedor), 4500ms (venta).
- IDs, data-*, selectores de tests: sin cambios.
- Endpoints AJAX: sin cambios.
- Lógica de negocio: no tocada.

## G. Accesibilidad

- `role="alert"` aplicado a toasts de error y warning.
- `role="status"` aplicado a toasts de success/info.
- Material Symbols preservados como decoración visual (no en aria-label).
- Texto del mensaje como `createTextNode` — no escapado manualmente, imposible XSS.

## H. Seguridad frontend

- Eliminado `innerHTML` en `catalogo-index.js` y `proveedor-index.js`.
- Mensajes insertados vía `createTextNode` — sin riesgo XSS.
- Se eliminó la sanitización manual `.replace(/</g, '&lt;')` innecesaria al usar `textContent`.

## I. Validaciones

| Validación | Resultado |
|---|---|
| `git diff --check` | OK — sin errores de whitespace |
| `dotnet build --configuration Release` | OK — 0 advertencias, 0 errores |
| `LayoutUiContractTests` | 57/57 passed |
| `Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard` | 230/230 passed |

## J. Tests

- 57 tests de `LayoutUiContractTests`: OK.
- 230 tests del filtro amplio de Layout/UI: OK.

## K. Playwright

- Spec: `e2e/ui-4e-layout-visual.spec.js`.
- Resultado: **169 passed / 0 failed** (2.2 min).
- Credenciales: `E2E_USER=Admin` / `E2E_PASS=Admin123!`.
- Entorno: `ASPNETCORE_ENVIRONMENT=Development`.

## L. Cierre de procesos

- Proceso del app (`dotnet run`) iniciado para Playwright en PID 26316.
- MCP servers (Playwright MCP, Context7): externos, no relacionados con la tarea.
- VS Code y extensiones: externos, no relacionados.
- Sin `dotnet test`/`vstest`/`testhost` colgados al cierre.

## M. Riesgos y deudas

- `catalogo-index.js` y `proveedor-index.js` siguen usando `fixed bottom-4 right-4 z-[60]` (Tailwind) para posicionamiento del toast flotante. `.alert-erp` no incluye posición — esta es la práctica correcta para toasts appended al body.
- `venta-create.js` contiene otros usos de `innerHTML` en dropdowns de clientes/productos y tabla de detalles. Esos son patrones funcionales de renderizado de listas dinámicas, fuera del alcance de UI-5I.
- `showFeedback` en venta-create.js usa `alert-erp-info` para el tone `info` — la variante existe en CSS pero no estaba en el diseño original. Comportamiento correcto y seguro.

## N. Próximo paso recomendado

**UI-5J**: Revisar si los módulos `CatalogoModule` y `ProveedorModule` (archivos no auditados en esta fase) tienen toasts propios o usan el evento `catalogo:toast`/`proveedor:toast`. Si los tienen, normalizar en la misma línea.
