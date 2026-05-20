# UI-5K — Seguridad Frontend: renderProductos en proveedor-module.js

## A. Objetivo

Corregir la función `renderProductos` en `wwwroot/js/proveedor-module.js` para eliminar la interpolación de datos del servidor (`producto.nombre`, `producto.codigo`) en template strings usadas como HTML. Reemplazar por construcción DOM segura con `createElement` y `textContent`.

## B. Deuda recibida de UI-5J

Detectada en UI-5J (f1b2c77):

| Archivo | Función | Línea aprox. | Problema |
|---|---|---|---|
| `proveedor-module.js` | `renderProductos` | ~184 | `producto.nombre` y `producto.codigo` interpolados en template string sin escape |

Contexto UI-5J: el intento de fix fue bloqueado por el hook de seguridad del proyecto. La corrección fue postergada como deuda para UI-5K.

## C. Archivo modificado

- `wwwroot/js/proveedor-module.js` — función `renderProductos` (único cambio).

## D. Análisis de renderProductos

### Campos procesados

| Campo | Tipo | Estado previo | Estado UI-5K |
|---|---|---|---|
| `producto.nombre` | string del servidor | Interpolado en template string → asignado como markup | `textContent` |
| `producto.codigo` | string del servidor | Interpolado con fallback `\|\| '—'` → asignado como markup | `textContent` |
| `producto.stock` | número | `formatInteger()` (Number → string numérica) — ya seguro | `textContent` |
| `producto.precio` | número | `formatInteger()` (Number → string numérica) — ya seguro | `textContent` |

### Estructura DOM generada

La función produce filas `<tr>` con 4 `<td>`: nombre, código, stock, precio. No hay atributos `data-*` propios en las filas ni eventos delegados sobre ellas.

### Eventos que dependen de la estructura

Ninguno. El delegado de clicks en `bindModuleEvents` usa `[data-proveedor-modal-action]` y `[data-proveedor-delete-form]` — ninguno aparece en las filas de producto. Las filas son presentación de solo lectura.

### Helper escHtml/escAttr en el archivo

No existía en `proveedor-module.js`. Otros archivos del módulo (crear-modal, editar-modal, product-picker) tienen sus propios helpers locales.

## E. Cambio aplicado

### Estrategia

`createElement` + `textContent` para todos los campos de datos del servidor. `tbody.replaceChildren()` para limpiar el cuerpo sin asignar markup. Las clases CSS (strings de desarrollador) se asignan a `className`.

### Código resultante (estado post-UI-5K)

```javascript
function renderProductos(data, tbody, footer, count, badge) {
    tbody.replaceChildren();

    data.forEach(producto => {
        const tr = document.createElement('tr');
        tr.className = 'hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors';

        const tdNombre = document.createElement('td');
        tdNombre.className = 'px-6 py-4 font-medium';
        tdNombre.textContent = producto.nombre;

        const tdCodigo = document.createElement('td');
        tdCodigo.className = 'px-6 py-4 text-center font-mono text-xs';
        tdCodigo.textContent = producto.codigo || '—';

        const tdStock = document.createElement('td');
        tdStock.className = 'px-6 py-4 text-center';
        tdStock.textContent = formatInteger(producto.stock);

        const tdPrecio = document.createElement('td');
        tdPrecio.className = 'px-6 py-4 text-right';
        tdPrecio.textContent = '$' + formatInteger(producto.precio);

        tr.appendChild(tdNombre);
        tr.appendChild(tdCodigo);
        tr.appendChild(tdStock);
        tr.appendChild(tdPrecio);
        tbody.appendChild(tr);
    });

    const label = `${data.length} producto${data.length !== 1 ? 's' : ''} asociado${data.length !== 1 ? 's' : ''}`;
    footer.classList.remove('hidden');
    count.textContent = label;
    badge.textContent = label;
}
```

### Nota sobre replaceChildren()

`Element.replaceChildren()` sin argumentos limpia todos los hijos del elemento. Es la API DOM moderna para vaciar un contenedor sin riesgo de parsing. Soporte: Chrome 86+, Firefox 78+, Safari 14+ (2021).

## F. Contratos preservados

| Contrato | Estado |
|---|---|
| `ProveedorModule.initIndex` | Sin cambios |
| `ProveedorModule.initDetails` | Sin cambios |
| Endpoint `/Proveedor/GetProductos/{id}` | Sin cambios |
| Payload JSON (nombre, codigo, stock, precio) | Sin cambios |
| Estructura visual de la tabla (clases Tailwind) | Idéntica |
| `#productos-tbody`, `#productos-footer`, `#productos-count`, `#productos-badge` | Sin cambios |
| Eventos delegados (`[data-proveedor-modal-action]`, `[data-proveedor-delete-form]`) | Sin cambios |
| `setProductosLoadingState` / `renderProductosEmptyState` / catch block | Sin cambios (ya seguros) |
| Lógica de negocio, controllers, services, rutas, permisos | Sin cambios |

## G. Seguridad frontend

### Riesgo eliminado

Un nombre o código de producto que contuviese HTML arbitrario era renderizado como markup. Con `textContent`, cualquier carácter especial (`<`, `>`, `&`, `"`) es tratado como texto literal — el navegador lo escapa automáticamente.

### Consistencia con el resto del módulo

- `setProductosLoadingState`: strings estáticas — seguro.
- `renderProductosEmptyState`: strings estáticas — seguro.
- Catch block: strings estáticas — seguro.
- `proveedor-crear-modal.js`, `proveedor-editar-modal.js`, `proveedor-product-picker.js`: ya usaban `escHtml()` — protegidos.
- `renderProductos` (UI-5K): ahora usa `textContent` — protegido.

Todo el módulo proveedor queda consistente en el uso de DOM seguro para datos del servidor.

## H. Validaciones

| Validación | Resultado |
|---|---|
| `git diff --check` | OK — sin errores de whitespace |
| `dotnet build --configuration Release` | OK — 0 advertencias, 0 errores |
| `LayoutUiContractTests` | 57/57 passed |
| Filtro amplio Layout/UI | 230/230 passed |

## I. Tests

- `LayoutUiContractTests`: 57/57 passed.
- Filtro amplio `Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard`: 230/230 passed.

## J. Playwright

- Spec: `e2e/ui-4e-layout-visual.spec.js`.
- Resultado: **169 passed / 0 failed** (1.7 min).
- Credenciales: `E2E_USER=Admin` / `E2E_PASS=Admin123!`.
- Entorno: `ASPNETCORE_ENVIRONMENT=Development`.

## K. Cierre de procesos

### Al inicio de UI-5K

Solo procesos externos: Porofessor Standalone, VS Code + extensiones, Playwright MCP, Context7 MCP. Ningún proceso de build/test/servidor del repo activo.

### Durante UI-5K

- Se levantó `dotnet run --launch-profile http` (background) para Playwright E2E. Servidor HTTP 200 en localhost:5187.

### Al finalizar

- Servidor `dotnet run` (TheBuryProyect en localhost:5187): activo intencionalmente para E2E. Puede detenerse manualmente si no se requiere más.

## L. Riesgos y deudas

1. **`replaceChildren()` compatibility**: soporte amplio desde 2021. Riesgo muy bajo en browsers modernos.
2. **Tailwind inline en strings estáticas**: `setProductosLoadingState` y `renderProductosEmptyState` usan Tailwind inline. No son datos del servidor — fuera del alcance de UI-5K. Deuda cosmética documentada en UI-5J.
3. **Otros módulos con AJAX dinámico**: `credito-module.js`, `documento-module.js`, `devolucion-module.js`, `venta-module.js` pueden tener patrones similares. No auditados en UI-5K.

## M. Próximo paso recomendado

**UI-5L (propuesta)**: Auditoría de seguridad frontend en módulos JS adicionales con renderizado AJAX dinámico: `credito-module.js`, `documento-module.js`, `devolucion-module.js`, `venta-module.js`. Aplicar el mismo criterio de `textContent`/`createElement` para datos del servidor.
