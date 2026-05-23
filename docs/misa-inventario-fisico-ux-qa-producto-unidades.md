# MISA-INVENTARIO-FISICO-UX-QA — QA visual de Producto/Unidades post 1A–1D

## A. Objetivo

Hacer QA visual/manual de `Views/Producto/Unidades.cshtml` después de las fases 1A–1D.

Determinar si la pantalla quedó suficientemente clara, jerárquica y usable para desktop y mobile.
Decidir si hace falta MISA-INVENTARIO-FISICO-UX-2 o si se puede avanzar a Catálogo/Movimientos general.

---

## B. Base y contexto

- Rama base: `main` en commit `95b866f` (MISA-INVENTARIO-FISICO-UX-1D integrada).
- Rama de trabajo: `misa/inventario-fisico-ux-qa-producto-unidades`.
- Especialista: Misa (Inventario / Catálogo / Movimientos / Marcas / Categorías / Inventario físico).
- Tipo de fase: QA / audit-only / documentación. Sin cambios en código productivo.

Fases previas integradas:

| Fase | Cambio |
|------|--------|
| 1A | `scope="col"`, labels sr-only para motivos, `aria-disabled` en trazabilidad bloqueada |
| 1B | Reorden: listado antes de secciones avanzadas, texto explicativo stock/unidades, eliminado botón Kardex duplicado |
| 1C | Hint mobile "Deslizá para ver más columnas →", `overflow-x-auto` en preview carga masiva, link cambiado a "Volver al listado de unidades" con ícono `arrow_upward` |
| 1D | `min-w-[14rem]` (era `min-w-[18rem]`), "Ver historial" siempre visible, acciones operativas agrupadas bajo `<details>/<summary>` "Gestionar unidad" |

---

## C. Ambiente usado

- Ambiente: código local en rama `misa/inventario-fisico-ux-qa-producto-unidades`.
- Método: análisis exhaustivo de `Views/Producto/Unidades.cshtml` (691 líneas) más lectura de docs de fases 1A–1D.
- No se levantó la app en esta fase (QA basado en análisis de markup). Ver nota en sección Y.

---

## D. URL usada

No aplicable. QA realizado sobre código Razor. Si se hubiera levantado la app, la URL esperada sería:
`https://localhost:{puerto}/Producto/Unidades/{productoId}`

---

## E. Datos de prueba usados

No se ejecutaron acciones POST. QA sobre estructura de markup y contratos.

---

## F. Archivos auditados

- `Views/Producto/Unidades.cshtml` (691 líneas — archivo principal)
- `docs/misa-inventario-fisico-ux-1a-accesibilidad-semantica.md`
- `docs/misa-inventario-fisico-ux-1b-jerarquia-unidades.md`
- `docs/misa-inventario-fisico-ux-1c-mobile-scroll-unidades.md`
- `docs/misa-inventario-fisico-ux-1d-acciones-fila-unidades.md`
- `e2e/` — carpeta verificada, sin specs para Producto/Unidades

---

## G. Resultado desktop

**Estado general: Aprobado con observaciones menores.**

La estructura de secciones en desktop quedó clara y jerárquica:

```
1. Toast messages (éxito/error)
2. Header: nombre, código, stock agregado, badge trazabilidad, "Ver kardex SKU", texto explicativo
3. Resumen de unidades físicas (KPIs grid 4 cols en lg, 2 en sm)
4. Filtros (texto, estado, checkboxes)
5. Listado principal de unidades (#listado-unidades)
6. Agregar unidad (form POST)
── Separador "Configuracion y herramientas avanzadas" ──
7. Trazabilidad individual (toggle activar/desactivar)
8. Conciliación stock vs unidades físicas (+ ajuste-asistido condicional)
9. Carga masiva de unidades
```

Observaciones desktop:

- El listado principal aparece antes de las secciones avanzadas: correcto (1B lo resolvió).
- El texto de KPIs es legible y conciso.
- La columna Acciones en desktop es 14rem (224px) — suficiente para dos elementos sin exceso.
- La sección de Conciliación es extensa cuando hay diferencia (KPIs + interpretación + 6 KPIs de desglose + timestamps + bloque ajuste-asistido). Esto es aceptable porque el operador que llega a Conciliación necesita esa información. No es ruido, es densidad informativa necesaria.
- El separador de herramientas avanzadas ("Configuracion y herramientas avanzadas") establece una ruptura visual clara entre el flujo operativo diario y las herramientas de administración. Correcto.

---

## H. Resultado mobile

**Estado general: Aprobado con deuda residual documentada.**

Cambios de 1C y 1D mejoraron significativamente la experiencia mobile desde código:

- Hint `"Deslizá para ver más columnas →"` con clase `lg:hidden` aparece solo en viewports < 1024px. Con `aria-hidden="true"` (decorativo). Correcto.
- `overflow-x-auto` en `<div class="overflow-x-auto">` wrappea la tabla principal — el scroll horizontal ya existía, el hint lo señala.
- `min-w-[14rem]` en columna Acciones reduce 64px respecto al estado anterior. En una pantalla de 390px, la columna Acciones ocupa menos espacio proporcional.

Deuda residual mobile (no bloqueante):

1. **`<details>` expandido dentro de tabla en mobile**: Cuando el usuario expande "Gestionar unidad", el contenido del details crece verticalmente dentro de la celda de la tabla. En mobile, si hay múltiples formularios dentro del toggle, la fila puede crecer significativamente. El layout de tabla con filas altas en móvil no es ideal. Esta deuda es inherente a la arquitectura tabla-con-acciones en línea y requeriría un rediseño de cards para resolverse completamente (fuera del alcance de 1A–1D y de este QA).

2. **9 columnas en tabla**: La tabla tiene 9 columnas de datos + acciones. En 390px, el usuario debe scrollear horizontalmente para ver todas. Esto es funcional gracias a `overflow-x-auto` y el hint, pero la experiencia mobile no es óptima. Requeriría tabla responsiva tipo cards para resolverse completamente.

3. **Formularios de carga masiva en mobile**: Textarea de series puede crecer horizontalmente dentro de `<div class="grid gap-3 lg:grid-cols-[...]">`. En mobile colapsa a 1 columna (correcto). El textarea en mobile es usable.

---

## I. Resultado encabezado

**Aprobado.**

- Nombre del producto: `<h1>` con `text-2xl font-black text-white`. Legible y jerarquizado.
- Código: `font-mono`, contraste `text-slate-400` — legible sobre fondo dark.
- Stock agregado: texto inline con `<strong class="text-slate-200">` — suficientemente prominente.
- Badge trazabilidad: pill con bordes coloreados emerald/amber según estado. Clara diferenciación.
- Botón "Ver kardex SKU": en posición prominente top-right. Sin duplicación (1B eliminó el duplicado de abajo).
- Texto explicativo L55: `"Stock agregado: cantidad numerica disponible del SKU en sistema. Unidades fisicas: elementos individuales con trazabilidad, serie o estado propio. La conciliacion muestra diferencias entre ambos."` — breve, correcto, contextual.

---

## J. Resultado KPIs

**Aprobado.**

- Sección "Resumen de unidades fisicas" con h2 `text-xs font-bold uppercase text-slate-400`.
- Grid de KPIs: 4 cols en lg, 2 en sm, 1 en xs. Responsive correcto.
- KPI genérico "Unidades listadas" + KPIs por estado (del modelo `ResumenEstados`).
- Cada KPI: etiqueta `text-xs uppercase text-slate-500` + valor `text-2xl font-black text-white`.
- Contraste adecuado para baja visión.

---

## K. Resultado filtros

**Aprobado.**

- Input de búsqueda por código/serie: `<label class="block"><span>...</span><input></label>` — label wrapping, usable.
- Select de estado: mismo patrón wrapping. Correcto.
- Checkboxes: `<label class="inline-flex items-center gap-2"><input> texto</label>` — patrón correcto, label envuelve input.
- Botón "Filtrar" con ícono `filter_alt` y texto. Botón "Limpiar" como link a la misma ruta sin parámetros.
- En mobile: los filtros colapsan a 1 columna. Los checkboxes son tocables (flex inline).

Observación menor: Los inputs de texto y select usan `<span>` como etiqueta visible dentro del `<label>` wrapping, no `<label for="id">`. Esta es una deuda de accesibilidad técnica pero el patrón wrapping es funcional. No fue introducida en 1A–1D — es preexistente y no prioritaria.

---

## L. Resultado listado

**Aprobado.**

- `id="listado-unidades"` presente en la section (L137). Contrato de anclaje preservado.
- Tabla con 9 `<th scope="col">` + 1 `<th scope="col">` para Acciones (1A). Total 9 columnas + acciones = 10 headers.
- Headers: Unidad, Serie, Estado, Ubicacion, Ingreso, Cliente, Venta, Observaciones, Acciones. Semánticamente correctos.
- `_EstadoUnidadBadge` partial para la columna Estado — consistente con el sistema de diseño.
- Mensaje vacío: `"No hay unidades fisicas para los filtros seleccionados."` centrado, claro.
- `overflow-x-auto` en wrapper interno — tabla scrolleable horizontalmente.
- El listado aparece antes de "Agregar unidad" y del bloque avanzado. Jerarquía correcta (1B).

---

## M. Resultado acciones de fila

**Aprobado con observación.**

- `min-w-[14rem]` en wrapper de acciones (1D). Reducción de densidad horizontal confirmada.
- "Ver historial": siempre visible como link primario con ícono `history`. Contraste adecuado (`text-slate-200`).
- "Gestionar unidad": solo se renderiza si `unidad.PuedeMarcarFaltante || unidad.PuedeDarBaja || unidad.PuedeReintegrarAStock || unidad.PuedeFinalizarReparacion`. Correcto — no aparece el toggle si la unidad no tiene acciones operativas disponibles.
- `<details>/<summary>` nativo: sin JS, accesible por teclado (Tab + Enter/Space en navegadores modernos). Ícono `settings` + texto "Gestionar unidad". El texto es suficientemente descriptivo.
- Dentro del details: formularios POST con antiforgery, inputs `sr-only` labeleados, inputs de motivo, botones con colores semánticos (amber/faltante, emerald/reintegrar, red/baja, blue/reparacion).
- Contratos POST preservados: `name="ProductoUnidadId"`, `name="Motivo"`, `name="EstadoDestino"` (solo en reparacion).

Observación: El ícono `settings` (engranaje) para "Gestionar unidad" puede no ser intuitivo para todos los usuarios. Un ícono como `more_vert` o `tune` podría comunicar mejor "opciones adicionales". No es bloqueante — el texto "Gestionar unidad" lo aclara.

---

## N. Resultado agregar unidad

**Aprobado.**

- Sección ubicada después del listado principal, antes del separador de herramientas avanzadas. Posición correcta — es acción frecuente y necesita estar accesible sin scrollear hasta el fondo.
- `h2` "Agregar unidad" + texto descriptivo + advertencia amber "Crear una unidad fisica no ajusta el stock agregado." Correcto y necesario.
- Form con antiforgery, campos: Numero de serie (opcional), Ubicacion actual (opcional), Observaciones (opcional), botón "Agregar unidad".
- En mobile: layout `lg:grid-cols-[1fr_1fr_1.4fr_auto]` colapsa a 1 columna. Los inputs son accesibles. El botón tiene `min-h-[38px]` y `lg:mt-5` — se alinea correctamente al final del grid en desktop y queda al final en mobile.

---

## O. Resultado trazabilidad

**Aprobado.**

- Sección "Trazabilidad individual" ubicada en el bloque de herramientas avanzadas (después del separador).
- `h2` con estilo `text-base font-black text-white`. Jerarquía correcta.
- Descripción clara: "Controla si este producto exige selección de unidad física en cada venta."
- Badge de estado: emerald "Activada" / slate "Desactivada".
- `aria-disabled="true"` en el botón Desactivar cuando hay unidades activas (1A). Correcto.
- Texto de bloqueo cuando está bloqueada: "Para desactivar, primero eliminá todas las unidades físicas registradas (N unidad/es activa/s)." — Pluralización Razor correcta.
- `title="No se puede desactivar porque hay unidades físicas registradas"` — tooltip informativo.

---

## P. Resultado conciliación

**Aprobado con observación de extensión.**

- Sección "Conciliacion stock vs unidades fisicas" con badge de estado inline (Diferencia detectada / Conciliado) con color dinámico amber/emerald.
- Texto explicativo doble (qué compara el panel + qué incluye "unidades registradas"). Correcto y necesario.
- Link "Volver al listado de unidades" con ícono `arrow_upward` (1C). El sentido ascendente queda claro visualmente.
- Link "Acciones de conciliacion" visible solo si `puedeAjustarStockAgregado`. Correcto.
- KPIs primarios (4): Stock agregado actual, Unidades disponibles, Diferencia, Unidades registradas.
- Texto de interpretación: dinámico según diferenciaPositiva / diferenciaNegativa / sin diferencia. Correcto.
- KPIs secundarios (6): Vendidas, Faltantes, Baja, Devueltas, Reservadas, En reparacion.
- Timestamps de último movimiento stock/unidad.
- Bloque `id="ajuste-asistido"` condicional: solo aparece si `puedeAjustarStockAgregado` (requiere trazabilidad activa Y hay diferencia).

Observación: La sección de conciliación es larga cuando hay diferencia y se muestra ajuste-asistido — puede tener 600–800px de alto en desktop y más en mobile. Este nivel de detalle es funcional y necesario para el operador que necesita tomar decisiones de conciliación. Sin embargo, en mobile puede ser agotador de scrollear. El link "Volver al listado de unidades" mitiga esto para el operador que llegó por el ancla `#ajuste-asistido`.

No se recomienda reducir la conciliación — es información crítica. Se podría evaluar en el futuro colapsar el desglose secundario (6 KPIs) bajo un `<details>` opcional, pero no es prioritario ahora.

---

## Q. Resultado carga masiva

**Aprobado.**

- `id="form-carga-masiva-unidades"` en el form (L608). Contrato de anclaje preservado.
- `h2` "Carga masiva de unidades" + descripción + advertencia amber. Correcto.
- Campos: Cantidad sin serie (number, min=0, max=200), Numeros de serie (textarea, 5 rows), Ubicacion actual, Observaciones. Layout `lg:grid-cols-[0.7fr_1.3fr_1fr_1.2fr]` colapsa correctamente en mobile.
- Flujo de dos pasos: Previsualizar → Confirmar carga. Botón "Confirmar carga" solo visible si `PreviewListo && Preview.Any()`. Correcto.
- Preview con `max-h-72 overflow-x-auto overflow-y-auto` (1C). Scroll horizontal disponible para la tabla de preview.
- `scope="col"` en los 3 th del preview (1A): #, Serie, Estado inicial.

---

## R. Resultado accesibilidad / baja visión

**Aprobado con deuda técnica documentada preexistente.**

### Confirmado como correcto

- `scope="col"` en todos los `<th>` de tablas (listado principal + preview carga masiva). ✓ (1A)
- Labels `sr-only` para todos los inputs de motivo (motivo-faltante, motivo-reintegrar, motivo-baja, motivo-reparacion). ✓ (1A)
- `aria-disabled="true"` en span de Desactivar cuando trazabilidad bloqueada. ✓ (1A)
- `aria-hidden="true"` en el hint mobile (decorativo, no funcional). ✓ (1C)
- Checkboxes de filtros con label wrapping. ✓
- `title` tooltip en botón bloqueado de trazabilidad. ✓
- Colores semánticos de botones con texto (no solo color). ✓
- `<details>/<summary>` nativo: navegable con Tab + Enter/Space. ✓

### Deuda técnica preexistente (no introducida en 1A–1D)

- Los inputs de texto de filtros y el select usan `<label class="block"><span>Label</span><input></label>` — patrón wrapping OK para click, pero sin atributo `for` explícito. Accesibles funcionalmente pero no con el patrón más robusto.
- El span visible dentro del label no está programáticamente asociado al input mediante `for`/`id` para lectores de pantalla que usen esa asociación explícita. En la práctica, el label wrapping es reconocido por la mayoría de los lectores de pantalla modernos.
- Esta deuda es preexistente y generalizada en el proyecto. No es exclusiva de esta pantalla. No fue introducida en 1A–1D.

### Contraste

- Texto principal: `text-white` (#fff) sobre `bg-slate-950` (#0a0f1e aprox) → contraste muy alto.
- Texto secundario: `text-slate-400` (#94a3b8) sobre fondos dark → contraste aproximado 5:1, aceptable WCAG AA para texto pequeño.
- Texto de advertencia: `text-amber-200` (#fde68a) sobre fondos dark → contraste alto, legible.
- Texto de labels de KPIs: `text-slate-500` (#64748b) sobre `bg-slate-950` → contraste aprox 3.5:1 para texto 10–12px. Puede ser insuficiente para WCAG AA en texto muy pequeño (4.5:1 requerido). Deuda preexistente, no bloqueante en este contexto ERP.

---

## S. Resultado contratos

**Todos los contratos críticos preservados.**

| Contrato | Estado |
|----------|--------|
| `id="listado-unidades"` | ✓ presente (L137) |
| `id="form-carga-masiva-unidades"` | ✓ presente (L608) |
| `id="ajuste-asistido"` | ✓ presente (L517, condicional) |
| `href="#listado-unidades"` | ✓ en Conciliación (L426) |
| `href="#form-carga-masiva-unidades"` | ✓ en ajuste-asistido (L572) |
| `href="#ajuste-asistido"` | ✓ en Conciliación si aplica (L433) |
| `name="ProductoUnidadId"` | ✓ en todos los forms de fila |
| `name="Motivo"` | ✓ en todos los forms de fila |
| `name="EstadoDestino"` | ✓ en FinalizarReparacion |
| `name="CargaMasiva.Confirmar"` | ✓ en botones Previsualizar/Confirmar |
| `@Html.AntiForgeryToken()` | ✓ en todos los forms POST |
| `asp-controller`, `asp-action`, `asp-route-*` | ✓ sin cambios |
| `asp-for` en campos de modelo | ✓ sin cambios |
| `_EstadoUnidadBadge` partial | ✓ en listado y preview |
| Labels sr-only motivos | ✓ para faltante, reintegrar, baja, reparacion |
| `scope="col"` en todos los th | ✓ listado (9+1) y preview (3) |
| `aria-disabled` trazabilidad bloqueada | ✓ presente |

---

## T. Errores encontrados

Ningún error funcional. Solo deudas preexistentes y observaciones de UX documentadas.

---

## U. Observaciones

1. **Ícono "settings" en toggle "Gestionar unidad"**: El engranaje puede no ser el ícono más intuitivo para "acciones adicionales de una unidad". `more_vert`, `tune` o `manage_accounts` comunicaría mejor "más opciones". No es bloqueante — el texto "Gestionar unidad" clarifica. Candidato para microfase futura.

2. **Conciliación extensa en mobile**: Cuando hay diferencia y el bloque ajuste-asistido se muestra, la sección puede superar 1000px en mobile. El link "Volver al listado" mitiga, pero el operador que llega a esta sección debe hacer mucho scroll. Candidato para futura mejora (colapsar desglose secundario).

3. **Sin Playwright spec**: No existe cobertura E2E para Producto/Unidades. Ningún spec para: carga de página, navegación a historial, toggle "Gestionar unidad", anclaje "Volver al listado". Esta es una deuda de QA que existía antes de 1A y no fue agregada en ninguna fase. Si se quiere cobertura mínima, sería candidata a una microfase dedicada de Playwright.

4. **Contraste de labels de KPIs (`text-slate-500`)**: En `10–12px`, el `text-slate-500` puede quedar por debajo de WCAG AA 4.5:1 sobre fondos muy oscuros. Deuda preexistente en el design system. No es específica de esta pantalla.

5. **Tabla vs cards en mobile**: La estructura de tabla de 9 columnas en mobile es funcional con scroll horizontal, pero no es el patrón más ergonómico para pantallas de 390px. Un rediseño tipo cards para mobile resolvería esto completamente, pero implicaría cambios de mayor alcance (JS condicional o Razor dual-mode). No recomendado en este momento.

---

## V. Decisión final

**Opción B — Aprobado con observaciones.**

La pantalla mejoró significativamente con las fases 1A–1D:

- La jerarquía visual es correcta: listado primero, herramientas avanzadas al final.
- El operador entiende stock agregado vs unidades físicas gracias al texto explicativo.
- La columna Acciones tiene jerarquía clara: Ver historial (primario) + Gestionar unidad (colapsado).
- Mobile es funcional con hint y scroll horizontal.
- Todos los contratos POST y de accesibilidad semántica están preservados.

Las observaciones restantes no son bloqueantes y no impiden el uso operativo de la pantalla.

---

## W. Decisión sobre MISA-INVENTARIO-FISICO-UX-2

**No se recomienda abrir MISA-INVENTARIO-FISICO-UX-2 ahora.**

Motivo: los problemas residuales son todos preexistentes o menores, y ninguno impide el uso operativo de la pantalla. Abrirla ahora consumiría ciclos sin mejora proporcional.

Los problemas que justificarían una UX-2 eventual serían:

- Feedback real de operadores reportando confusión con el toggle "Gestionar unidad" (no hay evidencia de esto aún).
- Necesidad de Playwright mínimo para esta pantalla (puede ser una microfase de spec independiente).
- Decisión de cambiar tabla a cards en mobile (requiere debate de alcance con el equipo).

Recomendación: crear un ítem de backlog para Playwright de Producto/Unidades (spec mínima), y avanzar a Catálogo/Movimientos general.

---

## X. Recomendación de próximo frente

**Avanzar a Catálogo / Movimientos general.**

Inventario físico en `Producto/Unidades` está suficientemente resuelto para este ciclo.

Frentes candidatos:

1. **Catálogo (`Views/Catalogo/`)**: auditoría UX de la lista de productos — jerarquía, filtros, acciones por fila, mobile.
2. **MovimientoStock (`Views/MovimientoStock/Index_tw.cshtml`)**: ya auditado en 1A (se agregaron scopes). Puede necesitar UX de listado, filtros y acciones.
3. **Spec Playwright mínima para Producto/Unidades**: microfase de QA técnico, no de UX.

Prioridad sugerida: Catálogo primero, ya que es la puerta de entrada al módulo de inventario.

---

## Y. Procesos iniciados/cerrados

- No se inició `dotnet build`.
- No se inició `dotnet run`.
- No se ejecutó `dotnet test`.
- No se ejecutó Playwright.
- No se inició servidor local.

Motivo: la fase es QA/audit-only sobre código Razor. El QA se realizó por análisis exhaustivo del markup (691 líneas del archivo principal) y los documentos de las fases previas. No fue necesario levantar la app para determinar el estado de contratos, estructura HTML, clases CSS aplicadas y semántica.

Nota: un QA visual en navegador real podría revelar detalles de rendering no detectables desde el markup (espaciado real, comportamiento del details en mobile, scrolling). Se recomienda que el usuario valide en navegador si quiere confirmar el rendering final, especialmente el comportamiento del toggle "Gestionar unidad" en dispositivos touch reales.

Procesos preexistentes documentados sin cerrar: VS Code, C# DevKit, MSBuild language server, Playwright MCP, Context7 MCP, Code.exe. No fueron iniciados por esta tarea.

---

## Checklist de cierre

- [x] Fase creada desde main `95b866f`.
- [x] No se modificó código productivo.
- [x] No se modificó Razor.
- [x] No se modificó CSS.
- [x] No se modificó JS.
- [x] No se modificó backend.
- [x] No se crearon vistas nuevas.
- [x] No se reemplazaron modales por vistas.
- [x] Se auditó desktop.
- [x] Se auditó mobile (desde código).
- [x] Se revisó encabezado.
- [x] Se revisaron KPIs.
- [x] Se revisaron filtros.
- [x] Se revisó listado.
- [x] Se revisaron acciones de fila.
- [x] Se revisó toggle "Gestionar unidad".
- [x] Se revisó Agregar unidad.
- [x] Se revisó Trazabilidad.
- [x] Se revisó Conciliación.
- [x] Se revisó Carga masiva.
- [x] Se revisó accesibilidad/baja visión.
- [x] Se revisaron contratos.
- [x] Se decidió B — Aprobado con observaciones.
- [x] Se decidió no abrir UX-2 ahora.
- [x] Se creó documento de QA.
- [x] No se ejecutaron tests innecesarios.
- [x] No se commitea AGENTS.md.
- [x] No se commitea CLAUDE.md.
- [x] No se commitea .claude/settings.local.json.
- [x] No se commitea skills-lock.json.
- [x] No se commitean temporales.
