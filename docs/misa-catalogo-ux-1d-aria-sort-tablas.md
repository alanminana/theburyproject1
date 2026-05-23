# MISA-CATALOGO-UX-1D — aria-sort en columnas ordenables de Catálogo

## A. Objetivo

Agregar `aria-sort` en las columnas ordenables de la tabla Productos del Catálogo para exponer el estado de ordenamiento a lectores de pantalla. Deuda detectada en MISA-CATALOGO-UX-0.

## B. Base y contexto

- Rama base: `main` en `da0e624` (MISA-CATALOGO-UX-1C integrada)
- Rama de trabajo: `misa/catalogo-ux-1d-aria-sort-tablas`
- Fase tipo: Razor + JS mínimo / accesibilidad semántica / bajo riesgo

## C. Deuda tomada desde MISA-CATALOGO-UX-0

La auditoría UX-0 detectó que la tabla Productos expone columnas ordenables vía `data-sort` pero no anuncia el estado de ordenamiento a lectores de pantalla (NVDA, JAWS, VoiceOver). Faltaba `aria-sort` tanto en el HTML inicial como en la actualización dinámica por click.

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml`
- `wwwroot/js/catalogo-index.js`
- `wwwroot/js/catalogo-module.js` — sin lógica de ordenamiento (no toca tablas de productos)
- `wwwroot/css/catalogo-module.css` — no toca aria-sort (lectura)
- `docs/misa-catalogo-ux-0-auditoria.md` — referencia de deuda

## E. Columnas ordenables detectadas

Solo en la tabla Productos (tab principal). Ninguna otra tabla de Catálogo (Categorías, Marcas) tiene `data-sort`.

| th | línea Razor | data-sort | scope | aria-sort previo |
|----|------------|-----------|-------|-----------------|
| Producto | 208 | `nombre` | `col` | — (ausente) |
| Precio vigente | 214 | `precio` | `col` | — (ausente) |
| Comisión | 217 | `comision` | `col` | — (ausente) |

## F. Estado previo

- Los 3 `th` tenían `scope="col"` desde MISA-CATALOGO-UX-1C.
- Ninguno tenía `aria-sort`.
- El JS en `catalogo-index.js` (IIFE líneas 487-544) manejaba ordenamiento dinámico y actualizaba íconos visuales (`data-sort-icon`), pero no actualizaba `aria-sort`.

## G. Análisis del JS de ordenamiento

El IIFE de ordenamiento en `catalogo-index.js` (líneas 487-544):

- Guarda estado en `currentKey` (null inicial) y `currentDir` (`'asc'`).
- Al click en un `th[data-sort]`: cambia key/dir, llama `sortRows()` y `updateIcons()`.
- `sortRows()`: reordena filas del DOM sin re-renderizar.
- `updateIcons()`: actualiza `textContent` e ícono visual del `th` activo vs. los demás.
- **No actualizaba `aria-sort` en ningún caso.**

Como el JS gestiona el estado dinámicamente, la solución correcta es Caso 2: agregar `aria-sort="none"` inicial en Razor y actualizarlo en `updateIcons()`.

## H. Decisión: Razor + JS mínimo (Caso 2)

- El orden inicial no tiene columna activa → `aria-sort="none"` en los 3 `th` en Razor.
- El JS cambia el estado dinámicamente → `updateIcons()` debe actualizar `aria-sort` al hacer click.
- Se reestructuró `updateIcons()` mínimamente: se eliminó el `return` temprano por ausencia de ícono y se desacoplaron la actualización de `aria-sort` (siempre aplica) de la actualización del ícono (guarda por existencia de `[data-sort-icon]`).

## I. Cambios aplicados

### Views/Catalogo/Index_tw.cshtml — 3 líneas

```html
<!-- Antes -->
<th scope="col" data-sort="nombre" class="...">
<th scope="col" data-sort="precio" class="...">
<th scope="col" data-sort="comision" class="...">

<!-- Después -->
<th scope="col" data-sort="nombre" aria-sort="none" class="...">
<th scope="col" data-sort="precio" aria-sort="none" class="...">
<th scope="col" data-sort="comision" aria-sort="none" class="...">
```

### wwwroot/js/catalogo-index.js — función updateIcons

```javascript
// Antes
function updateIcons(activeHeader) {
    headers.forEach(function (th) {
        var icon = th.querySelector('[data-sort-icon]');
        if (!icon) return;
        if (th === activeHeader) {
            icon.textContent = currentDir === 'asc' ? '↑' : '↓';
            icon.classList.remove('text-slate-600');
            icon.classList.add('text-slate-300');
        } else {
            icon.textContent = '↕';
            icon.classList.remove('text-slate-300');
            icon.classList.add('text-slate-600');
        }
    });
}

// Después
function updateIcons(activeHeader) {
    headers.forEach(function (th) {
        var icon = th.querySelector('[data-sort-icon]');
        if (th === activeHeader) {
            th.setAttribute('aria-sort', currentDir === 'asc' ? 'ascending' : 'descending');
            if (icon) {
                icon.textContent = currentDir === 'asc' ? '↑' : '↓';
                icon.classList.remove('text-slate-600');
                icon.classList.add('text-slate-300');
            }
        } else {
            th.setAttribute('aria-sort', 'none');
            if (icon) {
                icon.textContent = '↕';
                icon.classList.remove('text-slate-300');
                icon.classList.add('text-slate-600');
            }
        }
    });
}
```

## J. Contratos preservados

- `data-sort="nombre|precio|comision"` — sin cambio
- `data-sort-icon` — sin cambio
- `scope="col"` de 1C — sin cambio
- Lógica de `sortRows()` — sin cambio
- Lógica de click, `currentKey`, `currentDir` — sin cambio
- Comportamiento visual de íconos — idéntico
- IDs, `name`, `data-*`, `asp-*`, antiforgery, contratos JS — sin cambio
- Selectores usados por tests — sin cambio

## K. Qué no se tocó

- Backend (Controllers, Services, Models, DTOs, Migrations)
- CSS
- Tabs de Catálogo
- Modales
- Permisos
- Botón Movimientos
- Tablas de Categorías y Marcas (sin `data-sort`)
- Acciones por fila
- Ventas / Cotización / Caja / Crédito / stock

## L. Accesibilidad / baja visión

Los lectores de pantalla (NVDA, JAWS, VoiceOver) anuncian el estado de ordenamiento de una columna mediante el atributo `aria-sort`. Los valores posibles son `none`, `ascending`, `descending` y `other`.

Comportamiento resultante:
- Al cargar la página: los 3 `th` ordenables anuncian "none" → sin orden activo.
- Al hacer click en "Producto": el `th` pasa a `ascending` o `descending` según el estado; los demás vuelven a `none`.
- Los lectores de pantalla anuncian "Producto, columna, ascendente" (o "descendente") al enfocar o al cambiar.

## M. Riesgo funcional

Riesgo: **mínimo**.

- `aria-sort` es un atributo ARIA semántico, no afecta el layout ni la lógica de ordenamiento.
- La reestructuración de `updateIcons()` preserva exactamente el mismo comportamiento visual: `if (!icon) return` fue el único cambio estructural, reemplazado por `if (icon) { ... }` para cada bloque.
- No se introdujo lógica nueva de ordenamiento.

## N. Tests y validaciones

- Build: OK con `-o tmpbuild_misa_catalogo_ux_1d` (file-lock preexistente PID 11936 — proceso no iniciado por esta tarea).
- Warning NETSDK1194: esperado por uso de `-o` con solución.
- No se ejecutaron `dotnet test`: solo se agregaron atributos ARIA y se reestructuró levemente `updateIcons()`. No hay test específico de `aria-sort` en la suite existente.
- `git diff --check`: warnings en AGENTS.md y CLAUDE.md son preexistentes, no introducidos por esta tarea.

## O. Playwright

No ejecutado. Motivo: solo se agregaron atributos ARIA y se reestructuró mínimamente `updateIcons()`. No hay cambio de layout, clases, comportamiento visual ni endpoints. No existe spec específico de Catálogo que cubra `aria-sort`.

## P. Procesos / file-lock

- `TheBuryProyect (PID 11936)`: proceso preexistente en `localhost:5187`. No iniciado por esta tarea. No se cerró.
- Build directo bloqueado por lock de `TheBuryProyect.exe`. Se usó build con `-o tmpbuild_misa_catalogo_ux_1d` según regla anti-demora.
- Carpeta `tmpbuild_misa_catalogo_ux_1d` eliminada al cerrar la tarea.

## Q. Deudas restantes de Catálogo

Pendientes de fases posteriores (no abiertas en este lote):

1. **Focus management en modales** — MISA-CATALOGO-UX-1E (próxima fase)
2. **Centralizar `escapeHtml`** — detectado como función duplicada en UX-0
3. **Permiso anómalo** — detectado en UX-0, pendiente de verificación

## R. Próximo paso recomendado

**MISA-CATALOGO-UX-1E — Gestión de foco en modales de Catálogo**

Objetivo:
- Auditar apertura/cierre de los 10 modales con semántica de 1B.
- Enfocar el título o primer control al abrir.
- Devolver foco al trigger al cerrar si el patrón actual lo permite.
- Verificar cierre con Escape.
- Preservar modales existentes.
- No reemplazar modales por vistas.
- No tocar backend ni CSS.
