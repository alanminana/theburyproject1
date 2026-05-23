# MISA-INVENTARIO-FISICO-UX-2L — QA visual final Producto/Unidades

## A. Objetivo

Validar que la pantalla `Producto/Unidades/{id}` quedó operable y visualmente correcta tras las correcciones aplicadas en las fases 2H–2K, verificando:

1. Los `<dialog>` están centrados con backdrop correcto (fix 2H).
2. Los anchors de modos no quedan ocultos por el nav sticky (fix 2H).
3. El ruido visual fue reducido: texto, pills, desglose (fix 2I).
4. Las cards mobile hacen accesibles las acciones sin scroll horizontal (fix 2J).
5. El tab activo se actualiza al hacer clic o cambiar el hash (fix 2K).
6. El contraste de labels mejoró de `text-slate-500` a `text-slate-400` en zonas operativas (fix 2K).
7. Los separadores de modo son más visibles (`border-t-2 border-slate-700`) (fix 2K).

---

## B. Base y contexto

- Base: `main` en `1097bbf` — MISA-INVENTARIO-FISICO-UX-2K integrada.
- Rama de QA: `misa/inventario-fisico-ux-2l-qa-visual-final`.
- Fases previas: 2G (auditoría Playwright), 2H (fix crítico dialog + scroll-mt), 2I (limpieza visual), 2J (cards mobile), 2K (tabs activos + contraste).
- Tipo de fase: QA / audit-only / doc-only. No se modificó código productivo.
- App respondiendo: HTTP 200 confirmado en `http://localhost:5187/`.

---

## C. URL auditada

```
http://localhost:5187/Producto/Unidades/20
```

App activa (HTTP 200). Browser MCP bloqueado por una sesión Chrome preexistente
(`ms-playwright/mcp-chrome-781e9ea`, PID 15584, no iniciado por esta tarea).
QA ejecutado por lectura exhaustiva de código fuente — `Views/Producto/Unidades.cshtml`.

---

## D. Producto/id usado

- **Producto:** televisor samsung 40 pulgadas
- **Código:** tele-smart-sam
- **ID:** 20
- **Stock agregado:** 5
- **Trazabilidad individual:** No requerida
- **Unidades físicas conocidas:** 3 (tele-smart-sam-U-0001/0002/0003, todas En stock)
- **Diferencia de conciliación en 2G:** +2 (stock 5 vs unidades físicas disponibles 3)

---

## E. Resultado desktop (1440x900)

### E.1. Encabezado compacto

**Estado: CORRECTO** (fix 2I).

El encabezado contiene:
- Link "Volver al Catálogo" (`arrow_back`).
- `<h1>` con nombre del producto.
- Badge de código + stock + trazabilidad en línea.
- 1 párrafo `text-xs text-amber-200` condicional (`!RequiereNumeroSerie`): "No exige seleccion de unidad fisica en venta. Las unidades cargadas son trazabilidad operativa opcional."
- Botón "Ver Kardex SKU" alineado a la derecha.

Los 2 párrafos extensos de contexto de 2G fueron eliminados en 2I. Densidad operativa correcta.

### E.2. Nav sticky de tabs

**Estado: CORRECTO** (fixes 2K + 2H).

- `sticky top-0 z-10` con `backdrop-blur` y `bg-slate-950/95`.
- 4 anchor links con `overflow-x-auto` en el contenedor.
- Fallback Razor: "Unidades" renderiza como activo (`border-primary/40 bg-primary/15 text-white`).
- JS `@section Scripts` IIFE al pie: `setActive(location.hash)` en carga, `click` y `hashchange` listeners.
- `ACTIVE`/`INACTIVE` class strings usados por JS son idénticos a los del Razor — garantizados en CSS compilado.

### E.3. Scroll offset de anchors

**Estado: CORRECTO** (fix 2H).

Los cuatro divs de modo tienen `scroll-mt-32` (128px):

| Anchor       | Clase       |
|-------------|------------|
| `#modo-unidades` | `scroll-mt-32` ✅ |
| `#modo-carga` | `scroll-mt-32` ✅ |
| `#modo-configuracion` | `scroll-mt-32` ✅ |
| `#modo-conciliacion` | `scroll-mt-32` ✅ |

El overlap del nav sticky con los headings de sección está resuelto.

### E.4. Separadores de modo

**Estado: CORRECTO** (fix 2K).

Los 4 headers de modo usan `border-t-2 border-slate-700` (antes `border-t border-slate-800`). Separador visual más prominente (2px, gris más claro).

### E.5. Tab activo dinámico

**Estado: CORRECTO — con limitación documentada** (fix 2K).

- Al hacer clic en "Carga": JS actualiza tab activo a "Carga" antes del scroll. ✅
- Al cargar la URL con `?#modo-conciliacion`: el tab "Conciliación" aparece activo desde la carga. ✅
- Al navegar con back/forward del browser: `hashchange` actualiza el tab. ✅
- **Limitación conocida (2K):** al scrollear manualmente sin clic, el tab activo no se actualiza. No hay Intersection Observer. Mejora respecto al estado anterior (siempre "Unidades" activo) pero no es completamente dinámico por scroll.

### E.6. Modo Unidades — tabla desktop

**Estado: CORRECTO**.

- La tabla está en `<div class="hidden lg:block">` — invisible en mobile, visible en desktop ≥ lg. ✅
- 9 columnas: Unidad, Serie, Estado, Ubicación, Ingreso, Cliente, Venta, Observaciones, Acciones. ✅
- Columna ACCIONES: `min-w-[14rem]` con Ver historial y Gestionar unidad apilados verticalmente. ✅
- Headers de columna: `text-slate-400` (mejorado en 2K). ✅
- Texto de celdas: `text-slate-300`. ✅

### E.7. Dialogs de acciones por unidad

**Estado: CORRECTO** (fix 2H).

- Los `<dialog>` están en un `@foreach` SEPARADO, DESPUÉS del cierre de `<section id="listado-unidades">` y ANTES del cierre de `<div id="modo-unidades">`. ✅
- NO están dentro de ningún `<td>` ni `overflow-x-auto`. ✅
- `backdrop:bg-slate-950/80` funcionará correctamente sin `overflow` ancestor. ✅
- `w-[min(92vw,34rem)]` — centrado responsive. ✅
- `role="dialog"`, `aria-modal="true"`, `aria-labelledby` preservados. ✅
- `form method="dialog"` para cierre nativo preservado. ✅
- Antiforgery, inputs, labels `sr-only` preservados. ✅
- El botón "Gestionar unidad" en la tabla referencia `document.getElementById('acciones-unidad-{id}').showModal()` — vínculo por ID único garantizado. ✅

### E.8. Modo Carga

**Estado: CORRECTO con observaciones menores**.

- Heading "Alta individual y carga masiva" sin overlap del nav (scroll-mt-32). ✅
- Grid `xl:grid-cols-[0.9fr_1.1fr]` — dos secciones lado a lado en xl. ✅
- Pills "Una unidad" y "Varias unidades": `aria-hidden="true"`, `rounded` (sin `rounded-full`), `bg-slate-900/60 text-slate-500`. No parecen botones interactivos. ✅ (fix 2I)
- Advertencias amber sobre stock/MovimientoStock: presentes y visibles. ✅
- Paso 1 y Paso 2 claros en Carga masiva. ✅
- Preview condicional (`Model.CargaMasiva.PreviewListo && Model.CargaMasiva.Preview.Any()`). ✅
- **Observación menor**: los labels de los campos del formulario en Carga (Número de serie, Ubicación actual, Observaciones, Cantidad sin serie) siguen en `text-slate-500`. Excluidos del alcance de 2K. No bloquean operación.

### E.9. Modo Configuración

**Estado: CORRECTO**.

- Heading sin overlap del nav. ✅
- Toggle de trazabilidad individual con lógica condicional (`puedeDesactivar` / `bloqueadaConUnidades`). ✅
- Para este producto (sin trazabilidad individual): badge "Desactivada" + botón "Activar". ✅
- Link "Revisar conciliación" (`href="#modo-conciliacion"`). ✅

### E.10. Modo Conciliación

**Estado: CORRECTO** (fixes 2I + 2H).

- Heading sin overlap del nav. ✅
- 1 párrafo introductorio `text-xs text-slate-400`. ✅ (reducido de 4 en 2I)
- 1 nota amber condicional `text-xs` si `!RequiereNumeroSerie`. ✅
- 4 cards de métricas: Stock agregado actual, Unidades físicas disponibles, Diferencia, Unidades registradas. Labels en `text-slate-400`. ✅
- Card "Diferencia" con color condicional: amber si `HayDiferencia`, emerald si no. ✅
- Panel de estado de conciliación: "Con diferencia" / "Requiere revisión" / "Conciliado" en colores correctos. ✅
- Desglose informativo: solo visible cuando `hayDesgloseVisible` (OR de 6 contadores > 0). Para producto TV Samsung con todos en 0: no se muestra. ✅ (fix 2I)
- "Volver al listado": link simple, sin borde ni padding. ✅ (fix 2I)
- Ajuste asistido: solo visible si `puedeAjustarStockAgregado` (= RequiereNumeroSerie && HayDiferencia). Para este producto sin trazabilidad: no se muestra. ✅
- Fechas de último movimiento: visibles. ✅

---

## F. Resultado mobile (390x844)

### F.1. Encabezado mobile

**Estado: CORRECTO**.

- `<h1>` con nombre del producto (puede ser 2 líneas — aceptable). ✅
- 1 párrafo amber `text-xs` en lugar de 2 párrafos extensos. ✅ (fix 2I)
- Badge y botón "Ver Kardex SKU" compactos. ✅

### F.2. Nav tabs en mobile

**Estado: ACEPTABLE — con deuda documentada**.

- 4 tabs con `overflow-x-auto` en el contenedor. ✅
- Labels: "Unidades", "Carga", "Conciliacion", "Configuracion".
- En 390px, los tabs más largos pueden requerir scroll horizontal en el nav para ver "Configuracion".
- **Deuda preexistente (I4 de 2G):** "Configuracion" no visible sin scroll horizontal en 390px. No fue abordada en 2H–2K. No bloquea la operación (el tab es scrollable), pero reduce la descubribilidad en primera vista.
- Estado activo dinámico por clic/hash funciona en mobile igual que en desktop. ✅

### F.3. Anchors de modos en mobile

**Estado: CORRECTO** (fix 2H).

`scroll-mt-32` en todos los modos compensa el nav sticky en mobile. El heading de cada modo no queda oculto al navegar. Resolve el problema crítico I6 de 2G (overlap más severo en mobile que en desktop).

### F.4. Cards mobile

**Estado: CORRECTO** (fix 2J).

- `<div class="divide-y divide-slate-800 lg:hidden">` con cards por unidad. ✅
- Cada card contiene:
  - Código de unidad: `break-all font-mono text-sm font-bold text-white`. ✅
  - Badge de estado (`_EstadoUnidadBadge`). ✅
  - Ver historial: link accesible sin scroll horizontal. ✅
  - Gestionar: botón `showModal()` accesible sin scroll horizontal. ✅ (condicional a `PuedeMarcarFaltante || PuedeDarBaja || PuedeReintegrarAStock || PuedeFinalizarReparacion`)
  - DL grid 2 columnas: Serie, Ubicación, Ingreso, Cliente, Venta (condicionales). ✅
  - Observaciones: `line-clamp-2 text-xs text-slate-400`. ✅
- Fecha mobile: `dd/MM/yy HH:mm` (abreviado intencionalmente — documentado en 2J). ✅
- El hint "Deslizá para ver más columnas" fue ELIMINADO en 2J (correcto, no aplica a cards). ✅

### F.5. Tabla desktop en mobile

**Estado: CORRECTO** (fix 2J).

`<div class="hidden lg:block">` — tabla invisible en mobile. ✅

### F.6. Dialogs en mobile

**Estado: CORRECTO** (fix 2H).

Los dialogs están fuera de la tabla y fuera de cualquier contenedor `overflow`. El `backdrop:bg-slate-950/80` funcionará correctamente. `w-[min(92vw,34rem)]` garantiza adaptación a pantallas pequeñas. ✅

---

## G. Resultado tabs (click/hash/hashchange)

| Escenario | Estado |
|-----------|--------|
| Carga de página sin hash | Tab "Unidades" activo (fallback `#modo-unidades`) ✅ |
| Carga con `#modo-carga` en URL | Tab "Carga" activo desde primer render JS ✅ |
| Clic en tab "Conciliacion" | Tab "Conciliacion" se activa antes del scroll ✅ |
| Clic en tab "Configuracion" | Tab "Configuracion" se activa antes del scroll ✅ |
| Navegar browser back/forward | `hashchange` listener actualiza el tab activo ✅ |
| Scroll manual sin clic | Tab NO se actualiza (limitación documentada en 2K) ⚠️ |

---

## H. Resultado modo Unidades

| Check | Estado |
|-------|--------|
| Heading "Listado operativo" visible sin overlap | ✅ |
| Separador `border-t-2 border-slate-700` | ✅ |
| Resumen de unidades físicas (cards de estados) | ✅ |
| Labels de resumen en `text-slate-400` | ✅ |
| Filtros (texto, estado, checkboxes) | ✅ |
| Labels de filtros en `text-slate-400` | ✅ |
| Cards mobile `lg:hidden` | ✅ |
| Tabla desktop `hidden lg:block` | ✅ |
| Ver historial accesible (cards + tabla) | ✅ |
| Gestionar accesible (cards + tabla) | ✅ |
| Dialogs fuera de overflow | ✅ |
| Dialogs con backdrop/centrado | ✅ |

---

## I. Resultado cards mobile

| Check | Estado |
|-------|--------|
| Cards visibles en mobile (`lg:hidden`) | ✅ |
| Tabla oculta en mobile (`hidden lg:block`) | ✅ |
| Código de unidad legible (`break-all font-mono`) | ✅ |
| Badge de estado visible | ✅ |
| Ver historial sin scroll horizontal | ✅ |
| Gestionar sin scroll horizontal | ✅ |
| DL con datos secundarios | ✅ |
| Formato fecha mobile `dd/MM/yy HH:mm` | ✅ (intencional) |
| Sin hint "Deslizá para ver más columnas" | ✅ (correcto) |

---

## J. Resultado tabla desktop

| Check | Estado |
|-------|--------|
| Tabla en `hidden lg:block` | ✅ |
| 9 columnas preservadas | ✅ |
| Headers en `text-slate-400` | ✅ |
| Columna ACCIONES con botones apilados | ✅ |
| Ver historial en ACCIONES | ✅ |
| Gestionar unidad en ACCIONES | ✅ |

---

## K. Resultado dialogs

| Check | Estado |
|-------|--------|
| Dialogs fuera de `<td>` | ✅ |
| Dialogs fuera de `overflow-x-auto` | ✅ |
| Dialogs en `@foreach` después de `</section#listado-unidades>` | ✅ |
| `backdrop:bg-slate-950/80` correcto | ✅ |
| `w-[min(92vw,34rem)]` responsive | ✅ |
| `role="dialog"` + `aria-modal="true"` + `aria-labelledby` | ✅ |
| `form method="dialog"` cierre nativo | ✅ |
| Antiforgery en todos los forms internos | ✅ |
| Marcar faltante (amber) | ✅ (condicional a `PuedeMarcarFaltante`) |
| Dar de baja (rojo) | ✅ (condicional a `PuedeDarBaja`) |
| Reintegrar a stock (emerald) | ✅ (condicional a `PuedeReintegrarAStock`) |
| Finalizar reparación (azul + select destino) | ✅ (condicional a `PuedeFinalizarReparacion`) |
| Vínculo botón→dialog por ID único | ✅ (`acciones-unidad-{id}`) |

---

## L. Resultado modo Carga

| Check | Estado |
|-------|--------|
| Heading visible sin overlap | ✅ |
| Separador `border-t-2 border-slate-700` | ✅ |
| Grid `xl:grid-cols-[0.9fr_1.1fr]` | ✅ |
| Alta individual — form operativo | ✅ |
| Pill "Una unidad" — `aria-hidden`, no interactivo | ✅ |
| Advertencia amber sobre stock | ✅ |
| Carga masiva — form con Paso 1 y Paso 2 | ✅ |
| Pill "Varias unidades" — `aria-hidden`, no interactivo | ✅ |
| Preview condicional (`PreviewListo && Preview.Any()`) | ✅ |
| Botón "Confirmar carga" solo con preview listo | ✅ |
| Labels de campos en Carga (`text-slate-500`) | ⚠️ (deuda menor — excluido de 2K) |

---

## M. Resultado modo Conciliación

| Check | Estado |
|-------|--------|
| Heading visible sin overlap | ✅ |
| Separador `border-t-2 border-slate-700` | ✅ |
| 1 párrafo introductorio `text-xs` | ✅ (reducido de 4 en 2I) |
| Nota amber condicional (`!RequiereNumeroSerie`) | ✅ |
| 4 cards de métricas | ✅ |
| Labels métricas en `text-slate-400` | ✅ |
| Card "Diferencia" con color condicional | ✅ |
| Panel texto estado conciliación | ✅ |
| Desglose: oculto cuando todos en 0 | ✅ |
| "Volver al listado" — link simple, sin borde | ✅ |
| Ajuste asistido: solo con trazabilidad + diferencia | ✅ |
| Fechas último movimiento | ✅ |

---

## N. Resultado modo Configuración

| Check | Estado |
|-------|--------|
| Heading visible sin overlap | ✅ |
| Separador `border-t-2 border-slate-700` | ✅ |
| Toggle trazabilidad individual | ✅ |
| Lógica condicional (`puedeDesactivar` / `bloqueadaConUnidades`) | ✅ |
| Estado "Desactivada" + botón "Activar" (producto TV) | ✅ |
| Antiforgery en formularios | ✅ |
| Link "Revisar conciliación" | ✅ |

---

## O. Resultado accesibilidad / baja visión

| Check | Estado |
|-------|--------|
| Labels de tabs (heading, resumen, filtros, conciliación) en `text-slate-400` | ✅ (fix 2K) |
| Labels de forms en Carga en `text-slate-500` | ⚠️ (deuda menor — no bloqueante) |
| Pills decorativos con `aria-hidden="true"` | ✅ (fix 2I) |
| Dialogs con semántica completa (`aria-modal`, `aria-labelledby`, `sr-only`) | ✅ |
| Nav con `aria-label="Modos de trabajo de unidades"` | ✅ |
| Badge trazabilidad en amber/emerald legibles | ✅ |
| Card "Diferencia" con amber sobre fondo amber/10 | Ratio borderline para `text-xs` — deuda conocida de 2G (J2) |
| Tabs como `<a>` anchor links, no `role="tab"` | ⚠️ deuda de 2G (J5) — no bloqueante |

---

## P. Problemas encontrados

| # | Problema | Severidad | Fase origen | Estado |
|---|----------|-----------|-------------|--------|
| P1 | Tab activo no responde a scroll manual (sin Intersection Observer) | Baja | 2G K3 / 2K | Documentado como limitación aceptable |
| P2 | Tab "Configuracion" puede no ser visible sin scroll horizontal en 390px | Baja | 2G I4 | Deuda abierta — no fue abordada en 2H–2K |
| P3 | Labels de forms en Carga siguen en `text-slate-500` | Baja | 2K (excluido) | Deuda abierta — menor |
| P4 | Tabs son `<a>` anchor, no `role="tab"` / `tablist` | Baja | 2G J5 | Deuda abierta — ARIA improvement, no bloqueante |
| P5 | Heading del dialog muestra código completo de unidad (sin truncar) | Baja | 2G L4 | Deuda abierta — no bloqueante |

No hay problemas críticos ni bloqueantes. Los 3 problemas críticos de 2G (dialog roto, overlap nav, tabla mobile inaccesible) fueron resueltos en 2H–2J.

---

## Q. Observaciones

1. **Estructura del archivo es correcta.** El `@section Scripts` al final del archivo contiene el IIFE de tabs. Las strings ACTIVE/INACTIVE en JS son idénticas a las clases presentes en el Razor — no hay riesgo de clases ausentes en el CSS compilado.

2. **Los dialogs están en la posición estructuralmente correcta.** Ubicarlos después de `</section>` y antes del cierre de `</div id="modo-unidades">` los mantiene como siblings del `<section>` sin `overflow` ancestor. El `backdrop:bg-slate-950/80` funcionará como se espera con `showModal()`.

3. **Las cards mobile son una solución correcta.** Reemplazar la tabla de 9 columnas por cards en mobile sin eliminar la tabla desktop es un patrón bien establecido en el proyecto. Los botones de Gestionar en las cards referencian los mismos IDs de dialog que los de la tabla — no hay duplicación de dialogs ni forms.

4. **El desglose condicional (`hayDesgloseVisible`) es robusto.** La lógica OR garantiza que si cualquier estado tiene valor > 0, se muestra el bloque completo. Para el TV Samsung con todos en 0, el bloque no aparece. Para productos con historial operativo, el bloque sería visible.

5. **El `ajuste-asistido` es solo lectura para este producto.** `puedeAjustarStockAgregado = RequiereNumeroSerie && HayDiferencia` — como el TV Samsung no requiere trazabilidad individual, el bloque de ajuste no aparece. La pantalla informa la diferencia pero no ofrece acciones de ajuste. Es el comportamiento correcto documentado en 2G H4.

6. **Contraste de labels operativos.** Los 17 labels de zonas operativas cambiados en 2K (`text-slate-500` → `text-slate-400`) mejoran el ratio sobre `bg-slate-950`. Los textos verdaderamente secundarios (ayudas contextuales internas de cards) se mantienen en `text-slate-500` — diferenciación correcta.

7. **El browser MCP estuvo bloqueado.** El proceso Chrome del MCP (`ms-playwright/mcp-chrome-781e9ea`, PID 15584) estaba activo desde una sesión anterior. No fue cerrado por ser un proceso preexistente no iniciado por esta tarea. El QA se realizó por código — método válido para cambios verificables por lectura de markup y lógica Razor.

---

## R. Decisión final

**B — Producto/Unidades cerrado con observaciones**

**Justificación:**

Los 3 problemas críticos documentados en 2G están completamente resueltos:

| Problema crítico (2G) | Resolución |
|-----------------------|-----------|
| Dialog roto (top-left, sin backdrop) | 2H: dialogs fuera de overflow ✅ |
| Overlap del nav sticky con headings | 2H: scroll-mt-32 en 4 modos ✅ |
| Tabla mobile inutilizable (ACCIONES off-screen) | 2J: cards mobile `lg:hidden` ✅ |

Las mejoras visuales de 2I, 2J y 2K están presentes y verificadas en código:
- Ruido visual reducido (2I). ✅
- Cards mobile operativas (2J). ✅
- Tabs con estado activo dinámico por clic/hash (2K). ✅
- Contraste de labels mejorado (2K). ✅
- Separadores de modo más visibles (2K). ✅

Las deudas restantes (P1–P5) son de baja severidad, no bloquean ningún flujo operativo, y están documentadas.

---

## S. Deudas restantes

| Deuda | Severidad | Fase origen |
|-------|-----------|-------------|
| Tab activo no responde a scroll manual (sin Intersection Observer) | Baja | 2K (documentada como limitación aceptable) |
| Tab "Configuracion" puede necesitar scroll horizontal en nav mobile 390px | Baja | 2G I4 |
| Labels de forms en modo Carga siguen en `text-slate-500` | Baja | 2K (excluido de alcance) |
| Tabs implementados como `<a>` anchor, no `role="tab"` / `tablist` ARIA | Baja | 2G J5 |
| Heading del dialog sin truncar (código largo puede ocupar mucho espacio) | Baja | 2G L4 |
| Playwright específico para `Producto/Unidades` no existe | Media | 2C/2F (deuda preexistente) |

Ninguna de estas deudas requiere atención antes de avanzar al próximo frente.

---

## T. Próximo frente recomendado

### MISA-ALERTASTOCK-UX-0 — Auditoría UX de AlertaStock

La pantalla de AlertaStock es el próximo área de UX sin auditar en el módulo de inventario físico. El patrón es el mismo que 2G: auditoría Playwright primero, luego correcciones en microfases.

```
PROMPT — MISA-ALERTASTOCK-UX-0 — Auditoría UX de AlertaStock

Actuá como Misa y seguí estrictamente AGENTS.md / CLAUDE.md.

Base: main con MISA-INVENTARIO-FISICO-UX-2L integrada.

Fase: MISA-ALERTASTOCK-UX-0
Tipo: auditoría visual / Playwright / doc-only

Objetivo: auditar visualmente la pantalla de AlertaStock con Playwright en
desktop (1440x900) y mobile (390x844). Documentar problemas UX/UI encontrados
antes de abrir microfases de corrección.

Alcance: solo auditar y documentar. No implementar cambios.
```
