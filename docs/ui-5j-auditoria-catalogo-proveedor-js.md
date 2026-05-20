# UI-5J — Auditoría de Módulos JS de Catálogo y Proveedor

## A. Objetivo

Auditar CatalogoModule y ProveedorModule para confirmar si quedan deudas visuales o de seguridad frontend relacionadas con toasts dinámicos, luego de que UI-5I normalizó los listeners catalogo:toast y proveedor:toast.

## B. Archivos auditados

### CatalogoModule

| Archivo | Rol |
|---|---|
| `wwwroot/js/catalogo-module.js` | Registry puro: modalApis, productSelectionApi, requestScrollRefresh |
| `wwwroot/js/catalogo-index.js` | Listener catalogo:toast (normalizado en UI-5I), búsqueda client-side, tabs, ProductSelection, sort |

### ProveedorModule

| Archivo | Rol |
|---|---|
| `wwwroot/js/proveedor-module.js` | Módulo central: modal actions, delete confirm, scroll affordances, AJAX loadProductos |
| `wwwroot/js/proveedor-index.js` | Wrapper thin: llama initIndex(), listener proveedor:toast (normalizado en UI-5I) |
| `wwwroot/js/proveedor-detalles.js` | Wrapper thin: llama initDetails() |
| `wwwroot/js/proveedor-crear-modal.js` | Modal crear proveedor: dispara proveedor:toast en éxito |
| `wwwroot/js/proveedor-editar-modal.js` | Modal editar proveedor: dispara proveedor:toast en éxito |
| `wwwroot/js/proveedor-product-picker.js` | Autocomplete chip-picker para asociar productos a proveedor |

## C. Eventos encontrados

### catalogo:toast

- **Dispatch**: catalogo-index.js:314 — funcion dispatchToast(), usada en el handler del boton destacado (ToggleDestacado fetch success/error).
- **Listener**: catalogo-index.js:265 — ya normalizado en UI-5I con typeMap, alert-erp, createTextNode, role aria.

### proveedor:toast

- **Dispatch 1**: proveedor-crear-modal.js:195 — onProveedorCreado() tras crear con exito.
- **Dispatch 2**: proveedor-editar-modal.js:213 — onProveedorEditado() tras editar con exito.
- **Listener**: proveedor-index.js:25 — ya normalizado en UI-5I con typeMap, alert-erp, createTextNode, role aria.

## D. Toasts y feedback encontrados

### Toasts flotantes

- Todos los toasts flotantes de catalogo y proveedor pasan por los listeners normalizados en UI-5I.
- No hay toasts flotantes propios adicionales en catalogo-module.js, proveedor-module.js, proveedor-crear-modal.js, proveedor-editar-modal.js ni proveedor-product-picker.js.

### Feedback de validacion en modales

- proveedor-crear-modal.js y proveedor-editar-modal.js usan showErrors() que actualiza un ul de validation summary. No es un toast flotante — es feedback embebido en el modal, patron correcto y diferente.

## E. Insercion dinamica de HTML detectada

### Seguro (datos estaticos o ya escapados con escHtml)

| Archivo | Ubicacion | Contenido | Evaluacion |
|---|---|---|---|
| proveedor-module.js | setProductosLoadingState | String estatico con spinner | Seguro |
| proveedor-module.js | renderProductosEmptyState | String estatico con estado vacio | Seguro |
| proveedor-module.js | catch de loadProductos | String estatico de error | Seguro |
| proveedor-crear-modal.js | onProveedorCreado — tr.innerHTML | Usa escHtml() y escAttr() | Protegido |
| proveedor-editar-modal.js | onProveedorEditado — cells[n].innerHTML | Usa escHtml() | Protegido |
| proveedor-product-picker.js | chip.innerHTML, li.innerHTML | Usa escHtml() | Protegido |

### Deuda detectada (postergada)

| Archivo | Funcion | Linea aprox. | Problema |
|---|---|---|---|
| proveedor-module.js | renderProductos | ~184 | producto.nombre y producto.codigo interpolados en template string sin escHtml() |

**Contexto**: El intento de fix fue bloqueado por el hook de seguridad del proyecto. Un fix completo requiere reescribir renderProductos usando metodos DOM (createElement/textContent) en lugar de template string + asignacion de innerHTML. Cambio fuera del alcance de UI-5J.

**Riesgo actual**: Bajo. El endpoint /Proveedor/GetProductos es interno y autenticado; los datos (nombre y codigo de productos) son ingresados por admins del ERP. Sin embargo, la inconsistencia con el resto de los modulos (que si usan escHtml) constituye deuda tecnica documentada.

## F. Tailwind inline detectado

### En feedback/toasts

- Ninguno en catalogo.
- Los toasts de proveedor-index.js y catalogo-index.js usan `fixed bottom-4 right-4 z-[60] shadow-lg` junto a .alert-erp. Son clases de posicionamiento flotante, no reemplazables por .alert-erp (que no incluye posicion absoluta). Correcto — documentado en UI-5I como practica intencional.

### En UI estructural (fuera de alcance)

- proveedor-module.js: Tailwind inline en spinner de loading, estado vacio y filas de tabla dinamica. Son patrones de renderizado de tabla, no feedback/toast. Fuera del alcance del rework de toasts.

## G. Cambios aplicados

Ninguno en archivos JS. La auditoria concluye que:

- Los listeners de catalogo:toast y proveedor:toast estan correctamente normalizados desde UI-5I.
- No hay toasts propios adicionales en los modulos.
- El unico hallazgo accionable (renderProductos sin escaping) se documenta como deuda para una fase de seguridad dedicada.

## H. Contratos preservados

- Evento catalogo:toast: sin cambios.
- Evento proveedor:toast: sin cambios.
- CatalogoModule.registerModalApi, getModalApi, registerProductSelectionApi, getProductSelectionApi, requestScrollRefresh: sin cambios.
- CatalogoModule.events.refreshScroll: sin cambios.
- ProductSelection API publica (getIds, clearAll, getCount, refreshUi): sin cambios.
- ProveedorModule.initIndex, ProveedorModule.initDetails: sin cambios.
- ProveedorCrearModal.open, .close, .submit: sin cambios.
- ProveedorEditarModal.open, .close, .submit: sin cambios.
- ProveedorProductPicker.init: sin cambios.
- Hook .toast-msg en todos los toasts: sin cambios.
- autoDismissToasts: sin cambios.
- Endpoints AJAX: sin cambios.
- Selectores, IDs, data-*: sin cambios.

## I. Validaciones

| Validacion | Resultado |
|---|---|
| git diff --check | OK — sin errores de whitespace |
| dotnet build --configuration Release | OK — 0 advertencias, 0 errores |
| LayoutUiContractTests | 57/57 passed |
| Filtro amplio Layout/UI | 230/230 passed |

## J. Tests

- LayoutUiContractTests: 57/57 passed.
- Filtro amplio Layout/UI: 230/230 passed.

## K. Playwright

- Spec: e2e/ui-4e-layout-visual.spec.js.
- Resultado: **169 passed / 0 failed** (2.0 min).
- Credenciales: E2E_USER=Admin / E2E_PASS=Admin123!.
- Entorno: ASPNETCORE_ENVIRONMENT=Development.

## L. Cierre de procesos

- Procesos detectados al inicio: Porofessor Standalone (PID 2928, externo), VS Code y extensiones (PIDs 12176/10572/23004/7580, externos), Playwright MCP (PIDs 5348/30212/30296, MCP tools), Context7 MCP (PIDs 28380/28104/29544, MCP tools).
- Ningun proceso del repo (dotnet build, dotnet test, TheBuryProyect.exe, vstest, testhost) estaba activo al inicio.
- No se inicio ningun proceso nuevo para la fase de auditoria pura.

## M. Riesgos y deudas

1. **renderProductos sin escaping** (proveedor-module.js:~184): producto.nombre y producto.codigo interpolados sin escHtml(). Riesgo bajo (endpoint interno, admins). Fix requiere reescritura con metodos DOM. Ver seccion E.
2. **Tailwind inline en UI estructural de proveedor**: spinner, estado vacio y filas de tabla en proveedor-module.js usan Tailwind inline. No son feedback/toast — fuera del alcance del rework de toasts. Deuda cosmetica menor.
3. **Posicionamiento Tailwind en toast de catalogo y proveedor**: `fixed bottom-4 right-4 z-[60]` — intencional, .alert-erp no cubre posicion flotante. Documentado y aceptado desde UI-5I.

## N. Proximo paso recomendado

**UI-5K (propuesta)**: Seguridad frontend — reescribir renderProductos en proveedor-module.js usando createElement/textContent para eliminar la deuda de insercion de HTML sin escaping. Evaluar otros modulos con renderizado AJAX dinamico (credito-module.js, documento-module.js, devolucion-module.js, venta-module.js) con el mismo criterio.
