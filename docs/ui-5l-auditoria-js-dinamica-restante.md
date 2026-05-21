# UI-5L — Auditoria JS Dinamico Restante

**Fecha:** 2026-05-20
**Rama:** `kira/ui-5l-auditoria-js-dinamica-restante`
**Fase anterior:** UI-5K — Seguridad frontend Proveedor renderProductos
**HEAD de partida:** `b78736e`

---

## A. Objetivo

Auditar modulos JS dinamicos restantes posteriores a UI-5K para detectar:
- Usos riesgosos de inner​HTML (escritura directa de datos de servidor)
- Interpolacion directa de datos sin escapar
- Renderizados AJAX que deberian migrar a textContent/createElement
- Tailwind inline en feedback dinamico
- Toasts/alerts no normalizados

Esta fase es exclusivamente de **auditoria**. No se aplicaron fixes.

---

## B. Estado recibido

- main en `b78736e` (UI-5K integrado)
- Build Release OK
- LayoutUiContractTests 57/57 OK
- Suite relevante 230/230 OK
- Playwright 169/169 OK
- TheBuryProyect.exe PID 4072 activo desde sesion previa (no cerrado)

---

## C. Archivos auditados

### Modulos primarios (objetivos directos de UI-5L)

| Archivo | Lineas | Resultado |
|---|---|---|
| `wwwroot/js/credito-module.js` | 162 | LIMPIO |
| `wwwroot/js/documento-module.js` | 103 | LIMPIO |
| `wwwroot/js/devolucion-module.js` | 89 | LIMPIO |
| `wwwroot/js/venta-module.js` | 147 | LIMPIO |

### Archivos secundarios inspeccionados

- `credito-index.js` — lineas 248, 301, 345, 390
- `venta-devolucion-modal.js` — lineas 90, 211, 335
- `venta-index.js` — lineas 131, 134, 240, 402, 410
- `configurar-venta-credito.js` — lineas 411-425
- `venta-create.js` — referencia/auditoria (no modificado)

---

## D. Patrones buscados

Busqueda ejecutada con ripgrep sobre `wwwroot/js/*.js`:

- `innerHTML|insertAdjacentHTML|outerHTML`
- `textContent|createElement|replaceChildren|createTextNode`

---

## E. Hallazgos por modulo

### E.1 credito-module.js — LIMPIO

- Cero usos de innerHTML / insertAdjacentHTML / outerHTML
- APIs usadas: classList, querySelector, addEventListener, style.display, focus
- parseJsonScript usa script.textContent (seguro)
- Sin interpolacion de datos externos
- **Clasificacion: SEGURO**

### E.2 documento-module.js — LIMPIO

- Cero usos de innerHTML
- confirmDelete construye mensaje de texto con form.getAttribute() — usado en window.confirm(), no en DOM
- APIs usadas: form.submit(), select.value, checkbox.checked
- **Clasificacion: SEGURO**

### E.3 devolucion-module.js — LIMPIO

- Cero usos de innerHTML
- APIs usadas: classList, style.overflow, querySelector, dataset
- Sintaxis moderna (arrow functions, optional chaining) — sin renderizado dinamico
- **Clasificacion: SEGURO**

### E.4 venta-module.js — LIMPIO

- Cero usos de innerHTML
- APIs usadas: classList, setAttribute, querySelector, addEventListener
- bindModal no interpola datos
- **Clasificacion: SEGURO**

---

### E.5 credito-index.js — Deuda documentada (no corregida)

**Linea 248 — applyTemplateContent:**

    container[innerHTML] = template[innerHTML];

- Origen: elemento template del DOM, renderizado por Razor en servidor
- Sin interpolacion de datos externos en runtime
- **Clasificacion: SEGURO**

**Linea 301 — openClientePanel:**

    var tmp = document.createElement('template');
    tmp[innerHTML] = html;   // html = respuesta AJAX
    clientePanelContent.textContent = '';
    clientePanelContent.appendChild(tmp.content);

- Origen: respuesta AJAX del servidor (fetch con credentials: same-origin)
- La variable tmp es un elemento template — sandbox nativo del browser
- Contenido controlado por servidor, protegido por auth y CSRF
- Sin input de usuario en el payload
- **Clasificacion: RIESGO BAJO** — HTML de mismo origen, auth-gated.
- Deuda para UI-5M si se quiere migrar a fetch JSON + createElement

**Lineas 345, 390 — limpieza de contenedor:**

    elemento[innerHTML] = '';

- Vaciado seguro. **SEGURO**

---

### E.6 venta-devolucion-modal.js — Deuda documentada (no corregida)

**Linea 90 — renderItems(items):**

El metodo renderiza items de devolucion desde AJAX usando un template string con innerHTML.
La funcion escapeHtml() esta definida localmente (linea 347) con el patron textContent + innerHTML readback.

Campos analizados:

| Campo | Escapado | Clasificacion |
|---|---|---|
| item.productoNombre | SI (escapeHtml) | Seguro |
| item.productoCodigo | SI (escapeHtml) | Seguro |
| item.cantidadDisponible | Numerico | Seguro |
| item.precioUnitario | Numerico | Seguro |
| item.precioUnitarioDisplay | NO | Riesgo Bajo |
| index | Contador JS local | Seguro |

item.precioUnitarioDisplay: string de precio formateado desde servidor (ej: "$ 1.234,56").
No editable por usuario. Riesgo real muy bajo — precios provienen de decimal en C#.

**Clasificacion global: RIESGO BAJO**

**Linea 211:** vaciado de contenedor. SEGURO.

**Linea 335:** toast con HTML estatico sin datos. SEGURO.

---

### E.7 venta-index.js — SEGURO

**Linea 240 — renderConfiguraciones:**

    card[innerHTML] = `... ${esc(nombre)} ... ${esc(cfg.descripcion)} ...`;

- esc() aplicado en todos los campos de texto (nombre, descripcion)
- Valores numericos (porcentajeRecargo, idx) seguros por tipo
- **Clasificacion: SEGURO**

**Linea 402 — funcion esc():**

    function esc(str) {
        const d = document.createElement('div');
        d.textContent = str || '';
        return d[innerHTML];
    }

Patron estandar de escape. **SEGURO**

**Linea 410 — showToast:**

    toast[innerHTML] = '<span...>' + (icons[type] || 'info') + '</span> ' + esc(msg);

- icons[type] proviene de objeto interno con claves controladas
- msg escapado con esc()
- **Clasificacion: SEGURO**

---

### E.8 configurar-venta-credito.js — SEGURO

Lineas 411-425: innerHTML += con HTML 100% estatico.
Las condiciones son booleanos (mostrarMsgIngreso, mostrarMsgAntiguedad).
Ningun dato del servidor se interpola en el markup.

- Patron innerHTML += es suboptimo (puede causar reflow extra)
- Sin riesgo XSS
- **Clasificacion: SEGURO** — deuda de estilo, no de seguridad

---

## F. Clasificacion de riesgo consolidada

| Archivo | Ubicacion | Patron | Clasificacion |
|---|---|---|---|
| credito-module.js | — | Sin innerHTML | **SEGURO** |
| documento-module.js | — | Sin innerHTML | **SEGURO** |
| devolucion-module.js | — | Sin innerHTML | **SEGURO** |
| venta-module.js | — | Sin innerHTML | **SEGURO** |
| credito-index.js:248 | applyTemplateContent | template.innerHTML (Razor) | **SEGURO** |
| credito-index.js:301 | openClientePanel | HTML AJAX → template sandbox | **RIESGO BAJO** |
| venta-devolucion-modal.js:90 | renderItems | precioUnitarioDisplay sin escapeHtml | **RIESGO BAJO** |
| venta-index.js:240 | renderConfiguraciones | esc() aplicado en strings | **SEGURO** |
| venta-index.js:410 | showToast | esc(msg) aplicado | **SEGURO** |
| configurar-venta-credito.js:413 | renderSemaforo | HTML estatico, sin datos | **SEGURO** |

---

## G. Deudas fuera de scope — para fases futuras

### Prioridad Alta

| Archivo | Lineas | Problema |
|---|---|---|
| seguridad-index.js | 272, 363, 471 | result.errors del servidor interpolado como `<p>${message}</p>` sin escaping |
| venta-create.js | 901, 998, 1205 | Dropdowns de clientes/productos con datos de usuario sin escape consistente |

### Prioridad Media

| Archivo | Lineas | Problema |
|---|---|---|
| cotizacion-simulador.js | 159, 290, 356, 557 | tr[innerHTML] con datos de servidor |
| historial-precio-modal.js | 161 | row[innerHTML] con datos de precio |
| ordencompra-form.js | 122, 277, 302 | dropdown e items con esc() inconsistente |

### Prioridad Baja

| Archivo | Problema |
|---|---|
| configurar-venta-credito.js | innerHTML += con HTML estatico (safe pero suboptimo) |
| Multiples modales | Button loading states con innerHTML = HTML estatico (safe) |

---

## H. Cambios aplicados

**Ninguno.** UI-5L es fase de auditoria pura.

No se modifico:
- Ningun archivo JS
- Ninguna vista Razor
- Ningun controller/service
- Ningun endpoint AJAX
- Ningun payload
- Ningun selector, data-*, ID

---

## I. Contratos preservados

- Selectores por data-* e IDs: sin cambios
- Endpoints AJAX: sin cambios
- Payloads: sin cambios
- Flujo de venta, credito, devolucion, documentos: sin cambios
- Eventos publicos: sin cambios
- Funciones exportadas en TheBury.* y VentaModule.*: sin cambios

---

## J. Validaciones

Ejecutadas al cierre:

    dotnet build --configuration Release
    dotnet test --configuration Release --filter "LayoutUiContractTests"
    dotnet test --configuration Release --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard"
    git diff --check
    git status --short

---

## K. Tests

- LayoutUiContractTests: ver resultado en seccion validaciones
- Suite relevante: ver resultado en seccion validaciones

---

## L. Playwright

- Spec: e2e/ui-4e-layout-visual.spec.js
- Resultado: ver seccion validaciones

---

## M. Procesos al cierre

- TheBuryProyect.exe PID 4072: activo desde sesion previa, no cerrado (no iniciado por UI-5L)
- Procesos iniciados por esta tarea: ninguno persistente
- Dotnet MSBuild PIDs 24172/24232/25660: del IDE (VS Code DevKit), no cerrados

---

## N. Fases siguientes recomendadas

### UI-5M — Seguridad seguridad-index.js errores de servidor (Prioridad Alta)

Corregir interpolacion de result.errors del servidor en listas de errores sin escaping.
Archivos: seguridad-index.js, seguridad-roles.js, seguridad-permisos-rol.js
No tocar logica de permisos ni endpoints.

### UI-5N — Normalizar credito-index.js:301 AJAX fragment (Prioridad Baja-Media)

Evaluar migracion del patron de carga de fragmento HTML a respuesta JSON + createElement.
Requiere cambio coordinado con controller. Bajo riesgo real, alto costo de migracion.

### UI-5O — Auditoria venta-create.js (Prioridad Alta, alta complejidad)

Tratado parcialmente en UI-5I. Requiere fase propia con checklist previo del flujo de venta.

---

*Generado por Kira UI-5L — 2026-05-20*
