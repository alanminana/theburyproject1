# MISA-INVENTARIO-FISICO-UX-2G — Auditoría visual Playwright de Producto/Unidades

## A. Objetivo

Auditar visualmente la pantalla `Producto/Unidades/{id}` con Playwright en desktop y mobile para detectar problemas reales de UI/UX que no se perciben solo leyendo Razor. No se implementaron cambios.

---

## B. Base y contexto

- Base: main en `6519c2d` (COTIZ-UX-0), con `d1d9dfa` (MISA-INVENTARIO-FISICO-UX-2F) en historia.
- La pantalla fue cerrada como "B — cerrado con observaciones" en 2F.
- El usuario reportó que "no se ve bien" tras revisión visual directa.
- Esta fase abre una auditoría real con Playwright para documentar los problemas antes de corregirlos.

---

## C. URL auditada

```
http://localhost:5187/Producto/Unidades/20
```

---

## D. Producto/id usado

- **Producto:** televisor samsung 40 pulgadas
- **Código:** tele-smart-sam
- **ID:** 20
- **Stock agregado:** 5
- **Trazabilidad individual:** No requerida
- **Unidades físicas cargadas:** 3 (tele-smart-sam-U-0001/0002/0003, todas En stock)
- **Diferencia de conciliación:** +2 (stock 5 vs unidades físicas disponibles 3)

---

## E. Resultado desktop (1440x900)

### E.1. Encabezado del producto

- El encabezado ocupa una sección completa con: nombre del producto (h1 grande), código, stock, badge de trazabilidad, dos párrafos explicativos, y botón "Ver Kardex SKU".
- Los **dos párrafos de contexto** son extensos y de poca densidad operativa. Para un usuario que ya sabe qué es el producto, estos textos son ruido.
- El badge "No requiere trazabilidad individual" está en amber tenue — no es urgente pero su color puede confundirse con una advertencia.

### E.2. Nav sticky (tabs)

- Los tabs ("Unidades | Carga | Conciliación | Configuración") son anchor links a secciones en la misma página, no un sistema de tabs JS.
- El tab "Unidades" siempre aparece con `bg-primary/15 border-primary/40` como activo, independientemente de la sección visible. Los otros tres se ven idénticos entre sí.
- **No hay estado activo dinámico**: el usuario no sabe visualmente en qué sección está.

### E.3. Overlap del sticky nav con headings de sección (CRÍTICO)

Al navegar a `#modo-carga`, `#modo-conciliacion` o `#modo-configuracion`, el heading de la sección queda **parcialmente oculto detrás del sticky nav**.

- El `scroll-mt-24` (96px) no compensa la altura real del nav más el topbar.
- En desktop el heading "Alta individual y carga masiva" es parcialmente visible pero superpuesto con los tabs.
- El texto de descripción de la sección también queda detrás del nav al anclar.
- **Este es el problema visual principal que reporta el usuario**: la pantalla "no se ve bien" porque el contenido de cada modo aparece cortado por encima.

### E.4. Modo Unidades — listado operativo

- Layout correcto: resumen de unidades físicas, filtros, tabla.
- Las tarjetas del resumen (UNIDADES LISTADAS, EN STOCK) tienen `border-slate-800 bg-slate-950 p-4` — poco contraste con el fondo. Los labels en `text-slate-500` son de contraste bajo.
- La tabla tiene 9 columnas. En 1440px, la columna ACCIONES queda al extremo derecho y su contenido (dos botones apilados verticalmente) ocupa un `min-w-[14rem]` que puede empujar columnas anteriores.
- Los botones "Ver historial" y "Gestionar unidad" son correctos en semántica.
- El hint "Deslizá para ver más columnas →" solo aparece en mobile (`lg:hidden`) — correcto.

### E.5. Dialog "Gestionar unidad" — CRÍTICO

- El dialog está declarado dentro del `<td>` de la tabla, dentro del contenedor con `overflow-x: auto`.
- Al invocar `showModal()`, el dialog aparece en la **esquina superior izquierda del viewport** en lugar de centrarse.
- El `backdrop:bg-slate-950/80` no produce el efecto de dimming esperado — el fondo permanece completamente visible.
- **Causa probable**: el `overflow: auto` del contenedor padre rompe el stacking context del `::backdrop` pseudoelemento del `<dialog>`. Los `<dialog>` necesitan estar como hijos directos de `<body>` o de un contenedor sin `overflow` hidden/auto para que el backdrop funcione correctamente con `showModal()`.
- Accesibilidad semántica: correcta (`role="dialog"`, `aria-modal="true"`, `aria-labelledby`). El problema es puramente de posicionamiento/stacking.
- Contenido del dialog (dos acciones: Marcar faltante en amber, Dar de baja en rojo): estructura clara y correcta cuando es visible.

### E.6. Modo Carga

- Dos secciones en grid `xl:grid-cols-[0.9fr_1.1fr]`: Alta individual | Carga masiva.
- Cada sección tiene un badge decorativo ("Una unidad" / "Varias unidades") que **visualmente parece un toggle o botón** pero es un `<span>` no interactivo. Genera expectativa falsa de interacción.
- Los párrafos descriptivos dentro de cada sección son extensos (2 párrafos por sección).
- El botón "Agregar unidad" en desktop tiene `lg:grid-cols-[1fr_1fr_1.4fr_auto]` — los 3 campos se disponen horizontalmente y el botón aparece al final de la fila con `lg:mt-5`. El resultado es funcional pero visualmente denso.
- La advertencia en amber ("Crear una unidad física no ajusta el stock agregado...") es importante pero se pierde en el flujo textual.

### E.7. Modo Conciliación

- Muestra el panel "Conciliacion stock vs unidades fisicas" con badge "Diferencia detectada".
- **4 párrafos de explicación** dentro del panel antes de llegar a los datos concretos. Para una pantalla operativa, esto es excesivo.
- El panel de descripción de columnas (STOCK AGREGADO / UNIDADES FÍSICAS / DIFERENCIA) en gris oscuro con texto `text-sm` es una segunda capa de explicación antes de los números reales.
- Las métricas (STOCK AGREGADO ACTUAL: 5, UNIDADES FISICAS DISPONIBLES: 3, DIFERENCIA: +2, UNIDADES REGISTRADAS: 3) son claras y bien organizadas.
- La tarjeta "DIFERENCIA" con `+2` en amber oscuro es el elemento visual más informativo y se destaca correctamente.
- El "Desglose informativo de unidades registradas" con 6 cards en 3 columnas (Vendidas, Faltantes, Baja, Devueltas, Reservadas, En reparación) — **todas muestran 0 en este caso**. 6 cards vacías agregan ruido visual significativo. Deberían ocultarse o minimizarse cuando son todas cero.
- El botón "Volver al listado de unidades" en la esquina derecha tiene tamaño grande (`w-52 h-24` aproximado) — desproporcionado para un link de navegación.

### E.8. Modo Configuración

- Sección mínima: solo "Trazabilidad individual" con toggle Desactivada / Activar.
- La sección es pequeña pero clara.
- En el scroll de la página, Configuración aparece inmediatamente sobre Conciliación. El usuario que llega a `#modo-configuracion` ve Configuración en la parte superior pero Conciliación visible abajo. Las secciones se mezclan visualmente por la continuidad del scroll.

---

## F. Resultado mobile (390x844)

### F.1. Encabezado

- El nombre del producto ("televisor samsung 40 pulgadas") ocupa 2 líneas en mobile — aceptable.
- Los dos párrafos explicativos consumen aproximadamente el 40% del primer viewport mobile antes de llegar a los tabs.
- El badge y el texto de contexto son correctos semánticamente pero excesivos en densidad para mobile.

### F.2. Nav tabs en mobile

- Los tabs usan `overflow-x-auto` — scrollables horizontalmente. Correcto técnicamente.
- "Concilia..." aparece truncado en 390px.
- "Configuración" no es visible sin scroll horizontal en el nav.
- El usuario en mobile no sabe que existe la sección Configuración hasta que scrollea el nav.

### F.3. Overlap del sticky nav en mobile (CRÍTICO)

- El overlap es más severo que en desktop. Al anclar a `#modo-carga`, el heading "Alta individual y carga masiva" queda **completamente superpuesto** con el sticky nav y los tabs. El heading es ilegible.
- El texto descriptivo de la sección también queda debajo del nav overlap.

### F.4. Tabla en mobile (INUTILIZABLE)

- La columna UNIDAD muestra `tele-smart-sam-U-0001` en **4 líneas** por el ancho limitado.
- Las columnas INGRESO, CLIENTE, VENTA, OBSERVACIONES, y **ACCIONES quedan completamente fuera de pantalla**.
- El usuario no puede acceder a "Gestionar unidad" ni "Ver historial" sin scroll horizontal.
- Cuando el usuario scrollea horizontalmente para llegar a la columna ACCIONES, pierde el contexto de la fila (no sabe a qué unidad corresponde).
- El puntero de "Deslizá para ver más columnas →" aparece pero no resuelve la inutilidad operativa de la tabla en mobile.

### F.5. Dialog en mobile (ROTO)

- Al abrir el dialog en mobile, aparece en la **esquina superior izquierda sin backdrop**.
- El contenido de la página (tabla de unidades, sección Carga) es **completamente visible e interactivo** detrás del dialog.
- No hay dimming, no hay bloqueo de interacción con el fondo.
- El dialog es usable en términos de contenido (Marcar faltante / Dar de baja), pero el comportamiento no es modal.
- Para un usuario mobile, el dialog parece un tooltip o popover flotante, no una acción destructiva/significativa que requiere atención plena.

### F.6. Modo Carga en mobile

- El layout `xl:grid-cols-[0.9fr_1.1fr]` colapsa correctamente a columna única en mobile.
- El botón "Agregar unidad" es full-width en mobile — correcto.
- Los campos del formulario son accesibles.
- Sigue habiendo exceso de texto descriptivo antes de los inputs.

---

## G. Problemas visuales encontrados

| # | Problema | Severidad |
|---|----------|-----------|
| G1 | Sticky nav superpone headings de sección al anclar | Alta |
| G2 | Dialog aparece top-left sin backdrop (stacking context roto) | Alta |
| G3 | Tabla inutilizable en mobile (9 columnas, ACCIONES off-screen) | Alta |
| G4 | 6 cards de cero en Conciliación generan ruido visual | Media |
| G5 | 4 párrafos explicativos en Conciliación antes de los datos | Media |
| G6 | Pills "Una unidad" / "Varias unidades" parecen interactivos pero son `<span>` | Media |
| G7 | Tabs sin estado activo dinámico — "Unidades" siempre activo | Media |
| G8 | Dos párrafos explicativos en encabezado del producto consumen espacio operativo | Baja |
| G9 | Botón "Volver al listado de unidades" en Conciliación es desproporcionadamente grande | Baja |
| G10 | Labels de tarjetas de resumen en `text-slate-500` de contraste bajo | Baja |

---

## H. Problemas UX encontrados

- **H1:** El modelo mental del usuario asume tabs que cambian vistas. La realidad es un scroll continuo. Esta disonancia causa confusión cuando el usuario "cambia de tab" y ve contenido de la sección anterior todavía visible.
- **H2:** La acción "Gestionar unidad" es destructiva (Marcar faltante / Dar de baja). El dialog roto hace que esas acciones no se perciban como críticas — riesgo de errores operativos.
- **H3:** El flujo para agregar una unidad en mobile requiere: scroll pasando el encabezado extenso → scroll pasando el resumen → scroll pasando los filtros → scroll pasando la tabla → llegar al modo Carga. Hay demasiada fricción.
- **H4:** En Conciliación, el usuario no puede ejecutar ninguna acción de ajuste (el botón de ajuste asistido solo aparece si `RequiereNumeroSerie && HayDiferencia`). Para este producto (sin trazabilidad individual), la conciliación es solo lectura. Pero la sección tiene tanto peso visual como si fuera accionable.

---

## I. Problemas mobile

- I1: Tabla con 9 columnas — ACCIONES completamente inaccesible sin scroll horizontal.
- I2: Código de unidad (`tele-smart-sam-U-0001`) en 4 líneas — baja legibilidad.
- I3: Dialog roto: sin backdrop, sin centrado, fondo interactivo.
- I4: Nav tabs: "Concilia..." truncado, "Configuración" fuera de pantalla.
- I5: 40% del primer viewport consumido por texto de encabezado antes de los tabs.
- I6: El overlap del sticky nav en mobile es más severo que en desktop.

---

## J. Problemas de accesibilidad / baja visión

- J1: Labels de tarjetas en `text-slate-500` sobre `bg-slate-950` — ratio de contraste bajo para baja visión.
- J2: El badge "Diferencia detectada" en amber tiene `border-amber-500/30 bg-amber-500/10 text-amber-200` — el texto amber claro sobre fondo casi transparente puede ser insuficiente.
- J3: El dialog sin backdrop en mobile/desktop no provee el foco de atención que accesibilidad requiere en un modal.
- J4: Semántica del dialog: correcta (`aria-modal`, `aria-labelledby`, labels `sr-only`). El problema no es semántico sino de posicionamiento.
- J5: Los tabs son `<a>` anchor links, no `<button role="tab">` con `tablist`. Para tecnologías asistivas, no son tabs propiamente dichos, aunque navegan por la página.

---

## K. Problemas de tabs/modos

- K1: Los "tabs" son anchor links — no hay ocultamiento de secciones no activas. Todo el contenido de todos los modos está en el DOM y visible en el scroll.
- K2: Solo el primer tab ("Unidades") tiene estilo activo permanente. Los otros tres tienen el mismo estilo inactivo entre sí.
- K3: El usuario no tiene feedback de qué sección está viendo actualmente (sin Intersection Observer u otro mecanismo).
- K4: La separación visual entre modos es solo un `border-t border-slate-800 pt-4` — demasiado sutil para una pantalla con mucho contenido.

---

## L. Problemas de acciones por unidad / dialog

- L1: Dialog posicionado dentro de `<td>` en un contenedor `overflow-x: auto` — rompe el stacking context del `::backdrop`.
- L2: En desktop: dialog aparece top-left, no centrado.
- L3: En mobile: dialog aparece top-left sin backdrop, fondo completamente interactivo.
- L4: El heading del dialog solo muestra "Unidad tele-smart-sam-U-0001" (nombre largo). No hay contexto del producto padre.
- L5: Las dos acciones del dialog (Marcar faltante / Dar de baja) están bien diferenciadas por color (amber / rojo) y son semánticamente correctas. El problema es solo de posicionamiento.

---

## M. Problemas de modo Carga

- M1: Heading de sección oculto por overlap del sticky nav al anclar (crítico).
- M2: Los pills "Una unidad" / "Varias unidades" parecen botones de selección de modo pero son `<span>` decorativos.
- M3: En desktop `lg:`, los 3 campos del alta individual se disponen en fila horizontal con el botón al final — el layout `lg:grid-cols-[1fr_1fr_1.4fr_auto]` es funcional pero en 1440px los campos quedan estrechos.
- M4: El advertencia amber sobre stock agregado y MovimientoStockService es importante pero se pierde entre textos descriptivos del mismo tamaño.
- M5: El paso "PASO 1" en carga masiva es un buen patrón pero el paso 2 (confirmar preview) no es visible en la pantalla sin scroll adicional.

---

## N. Problemas de modo Conciliación

- N1: 4 párrafos de texto antes de llegar a los datos.
- N2: Segunda capa de descripción de columnas (3 cards de texto) antes de las métricas.
- N3: 6 cards de desglose "en cero" agregan ruido visual sin valor operativo.
- N4: Para productos sin trazabilidad individual (`!RequiereNumeroSerie`), la conciliación es solo lectura — no hay acción disponible. La pantalla debería comunicar esto de forma más compacta.
- N5: El botón "Volver al listado de unidades" tiene tamaño desproporcionado para un link de navegación secundaria.

---

## O. Qué se ve bien

- Dark theme consistente en toda la pantalla.
- Paleta coherente (slate-800/900/950 base, primary azul, amber para advertencias, emerald para éxito, rojo para baja).
- Las métricas de Conciliación (STOCK AGREGADO ACTUAL, UNIDADES FISICAS DISPONIBLES, DIFERENCIA) son claras una vez que el usuario llega a ellas.
- La tarjeta "DIFERENCIA +2" en amber oscuro se destaca correctamente entre las métricas.
- Los badges de estado "En stock" en verde son legibles y consistentes.
- Semántica del dialog correcta (`aria-modal`, `aria-labelledby`, labels `sr-only`).
- Los filtros de la tabla (búsqueda + combobox de estado + checkboxes) son funcionales y bien organizados.
- El nav tiene `aria-label="Modos de trabajo de unidades"` — correcto.
- El "Ver Kardex SKU" tiene posición clara en el encabezado.
- El link "Volver al Catálogo" es visible e inmediato.

---

## P. Qué se ve mal

1. **Dialog roto** — ni en desktop ni en mobile funciona como modal real (sin backdrop, sin centrado). Para acciones destructivas (Dar de baja) esto es un riesgo operativo.
2. **Overlap sticky nav** — el heading de cada modo queda oculto al anclar. En mobile es peor que en desktop.
3. **Tabla inutilizable en mobile** — 9 columnas que requieren scroll horizontal para llegar a ACCIONES.
4. **Secciones se mezclan** — al ser un scroll continuo sin JS de tab switching, el usuario ve contenido de múltiples modos al mismo tiempo.
5. **Exceso de texto explicativo** — especialmente en el encabezado del producto y en el panel de Conciliación.
6. **Ruido visual en Conciliación** — 6 cards en cero y 2 capas de descripción antes de los datos.
7. **Pills no interactivos** que parecen interactivos.
8. **Sin estado activo dinámico en tabs**.

---

## Q. Recomendación de rediseño

El problema estructural más importante es el **modelo de navegación**: usar anchor links como si fueran tabs reales, en una sola página sin JS de switching, produce una pantalla que mezcla todos los modos visualmente.

Dos caminos posibles:

### Opción A — Arreglar el scroll (menor riesgo, recomendado como primer paso)
- Aumentar `scroll-mt` para compensar sticky nav + topbar (aproximadamente `scroll-mt-28` o `scroll-mt-32`).
- Mover los `<dialog>` fuera de la tabla (al final de `<main>` o al nivel del `<body>`).
- Comprimir el encabezado del producto (reducir a una línea de contexto, mover el texto largo a un info expandible).
- Simplificar Conciliación: ocultar cards en cero, reducir párrafos a máximo 1 por panel.
- En mobile: reemplazar tabla de 9 columnas por card layout por fila.

### Opción B — Convertir a tabs reales con JS (mayor impacto, más trabajo)
- Agregar JS que oculte/muestre secciones según tab activo.
- Actualizar estado activo del tab dinámicamente.
- Resuelve el problema de mezcla visual de secciones.

Recomendación: **Opción A primero**, completamente en Razor/CSS sin JS adicional. Resuelve los problemas críticos (dialog, scroll offset, mobile) sin tocar la arquitectura. La Opción B puede venir después como mejora.

---

## R. Roadmap de corrección por microfases

### MISA-INVENTARIO-FISICO-UX-2H (CRÍTICO — Prioridad 1)
**Fix del dialog y scroll offset**

Archivos: `Views/Producto/Unidades.cshtml`

1. Mover los `<dialog>` de cada unidad fuera del `<td>` → al final del `<main>` (antes del `</div>` de cierre de la vista), manteniendo todos los IDs y contratos JS.
2. Aumentar `scroll-mt-24` a `scroll-mt-32` en los divs `#modo-unidades`, `#modo-carga`, `#modo-conciliacion`, `#modo-configuracion`.
3. No tocar lógica backend, endpoints, ni contratos.

Validar: build + tests de layout + Playwright visual.

---

### MISA-INVENTARIO-FISICO-UX-2I (VISUAL — Prioridad 2)
**Reducir ruido en encabezado y Conciliación**

1. Comprimir los 2 párrafos del encabezado a 1 línea `text-xs text-slate-400` con texto esencial.
2. En Conciliación: reducir de 4 párrafos a 1 párrafo introductorio. Mover el resto a un detalle expandible o eliminarlo.
3. En Conciliación: ocultar el bloque "Desglose informativo" cuando todos los estados son 0 (con `@if`).
4. En Conciliación: reducir el botón "Volver al listado de unidades" a un link small.
5. Convertir pills "Una unidad" / "Varias unidades" a labels estáticos sin aspecto de botón.

Validar: build + tests de contrato UI.

---

### MISA-INVENTARIO-FISICO-UX-2J (MOBILE — Prioridad 3)
**Tabla mobile y tabs mobile**

1. En mobile (`lg:hidden`): reemplazar la tabla por cards por fila (código, serie, estado, ubicación, botones de acción apilados).
2. Abreviar labels de tabs en mobile o mostrar solo iconos con texto solo en ≥md.
3. Ajustar el encabezado del producto en mobile para reducir el bloque textual.

Validar: build + Playwright mobile.

---

### MISA-INVENTARIO-FISICO-UX-2K (POLISH — Prioridad 4)
**Estado activo de tabs y contraste**

1. Agregar estado activo dinámico a los tabs con Intersection Observer (JS mínimo) o con scroll-linked CSS.
2. Mejorar contraste de labels de tarjetas de `text-slate-500` a `text-slate-400`.
3. Agregar separador visual más fuerte entre modos (`border-t-2 border-slate-700` en lugar de `border-t border-slate-800`).

Validar: build + tests de contraste.

---

## S. Decisión

**C — Requiere corrección visual**

Los problemas encontrados son reales y verificables con Playwright. El más crítico (dialog roto con stacking context) afecta la usabilidad de acciones operativas. El overlap del sticky nav afecta la orientación en la pantalla. La tabla mobile hace inaccesibles las acciones por unidad.

El core funcional (filtros, tabla, datos de conciliación, formularios de carga) está operativo. No es un rediseño desde cero, sino una serie de correcciones concretas y microfases acotadas.

---

## T. Próximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-FISICO-UX-2H — Fix dialog y scroll-mt en Producto/Unidades

Actuá como Misa y seguí estrictamente AGENTS.md / CLAUDE.md.

Base: main actualizado. Último estado esperado: MISA-INVENTARIO-FISICO-UX-2G integrada.

Fase: MISA-INVENTARIO-FISICO-UX-2H
Tipo: Razor-only — corrección crítica de posicionamiento

Archivos a tocar: Views/Producto/Unidades.cshtml únicamente.

Cambios a implementar:
1. Mover todos los <dialog> de acciones por unidad fuera del <td> y de la tabla,
   al final del <div id="modo-unidades"> (antes de su cierre) o al final del <main>.
   Mantener todos los IDs (acciones-unidad-{id}), aria-modal, aria-labelledby,
   el onclick con showModal(), los forms internos y los contratos JS.
   El fix resuelve el stacking context roto del ::backdrop.

2. Aumentar scroll-mt de scroll-mt-24 a scroll-mt-32 en los cuatro divs:
   #modo-unidades, #modo-carga, #modo-conciliacion, #modo-configuracion.

No tocar: backend, services, endpoints, payloads, entidades, migraciones,
tests, CSS, JS externo, otros archivos.

Validar: build Release + dotnet test LayoutUiContractTests + Playwright visual.
```
