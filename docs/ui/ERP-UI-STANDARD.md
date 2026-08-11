# ERP UI Standard

Fuente canónica de cómo debe construirse la UI de TheBuryProject. Reglas generalizables
únicamente — no historia de micro-lotes, no datos temporales. Ver estado real de cada
pantalla en [UI-REFACTOR-STATUS.md](./UI-REFACTOR-STATUS.md).

## 1. Propósito

Que cualquier chat o agente pueda auditar o construir una pantalla del ERP sin releer
la historia del proyecto. Este documento define reglas; el código real (Foundation/
`_Layout` + `Venta/Index`) es la implementación de referencia.

## 2. Principios

- Consistencia significa mismo lenguaje y reglas, **no** mismo layout exacto.
- Priorizar jerarquía clara, densidad útil y baja carga cognitiva sobre expresividad visual.
- El backend es la autoridad de negocio; la UI no la duplica ni la reemplaza.
- Preservar ids, nombres, bindings, rutas y `data-*` que sean contrato con JS o backend.
- Cambio mínimo: no rediseñar una pantalla cerrada salvo regresión demostrada.

## 3. Estructura Razor / Layout

- Un único `main#main-content`, provisto por `_Layout`. Una vista que usa `_Layout` **no**
  crea otro `<main>`.
- No incluir datos globales hardcodeados en el header (nombre de usuario, contadores, etc.).
- CSS de un módulo no debe romper componentes compartidos (header, sidebar, modales globales).
- Scopear excepciones locales de `.hidden` al selector más específico posible (ej. por id
  de elemento), nunca a un contenedor genérico de página — un override amplio apaga
  `.hidden` legítimos de otras vistas que comparten el mismo layout.
- Evitar `!important` salvo necesidad demostrada y documentada inline.

## 4. Headers y acciones

- Header de página: breadcrumb + título + acciones primarias agrupadas a la derecha.
- Acciones condicionadas por permiso (`User.TienePermiso`) se resuelven en el servidor,
  no se ocultan solo con CSS.
- Acciones no disponibles no se muestran si un estado disabled no aporta explicación (ver
  §8).

## 5. Tabs

Usar patrón ARIA completo:

```
role="tablist" / role="tab" / role="tabpanel"
aria-selected, aria-controls, aria-labelledby
roving tabindex (solo el tab activo tiene tabindex="0")
ArrowLeft / ArrowRight mueven foco y activan
Home / End van al primer/último tab
```

Además:

- el tab activo debe quedar visible dentro del scroll de la barra;
- si los tabs no entran en el ancho disponible, usar scroll horizontal interno;
- mostrar affordance visual de contenido desplazable (fade lateral) cuando hay overflow;
- mantener el foco visible (`:focus-visible`) en todo momento.

## 6. Tablas y listados

- Tabla real (`<table>`) con `<thead>`/`th scope="col"`, no divs simulando filas.
- Envolver en un contenedor con `role="region"`, `aria-label` descriptivo y scroll
  horizontal propio (`tabindex="0"` para que sea alcanzable por teclado).
- Estados de fila (pill semántica) en columna dedicada, nunca solo color de fondo.
- Acciones por fila agrupadas en una celda `Acciones`, mismo orden en toda la tabla.

## 7. Mobile

- Desktop y mobile pueden usar representaciones diferentes (tabla vs. cards) cuando eso
  mejora la usabilidad.
- Si existen dos representaciones, **deben compartir la misma lógica funcional** (mismo
  helper de servidor o de JS resuelve qué acciones/estados se muestran en ambas).
- Nunca permitir overflow horizontal de página completa; una tabla puede tener scroll
  horizontal interno.
- Mobile prioriza lectura, acciones claras y targets táctiles adecuados (mínimo ~40px).
- CTAs repetidos entre header y barra sticky mobile no se duplican como dos autoridades:
  misma condición de permiso, mismo destino.

## 8. Acciones y estados

**Acciones**

- Desktop y mobile deben resolver las mismas acciones disponibles para el mismo registro.
- No duplicar reglas funcionales entre dos layouts — calcular una vez (helper compartido,
  ej. `@functions` en Razor) y reusar en ambas representaciones.
- Acciones destructivas (cancelar, anular, eliminar) se diferencian visualmente (color de
  peligro) y se separan espacialmente de las acciones neutras.

**Estados**

- Usar pills/labels semánticos con texto, no depender únicamente del color.
- No usar `line-through` para estados terminales tipo "Cancelada" (ambigüedad con
  tachado de error/typo); un tono `muted` puede complementar el estado.
- No crear controles falsamente interactivos o deshabilitados sin explicación
  (title/aria-label) accesible.

## 9. Filtros y paginación

- Filtros server-rendered vía querystring: la URL con filtros aplicados debe ser
  reproducible (compartible, recargable).
- Cambiar cualquier filtro vuelve a página 1.
- Una página inválida (fuera de rango) se clampea al límite válido, no rompe.
- El total global (para KPIs/tabs) es un dato separado del contenido de la página actual.
- Proveer variante compacta de paginación para mobile (ej. "Página X de Y" en vez de
  todos los números).
- Marcar la página activa con `aria-current="page"`.
- No convertir un tamaño de página puntual (ej. `PageSize=20`) en estándar universal;
  cada listado define el suyo según su propio volumen y necesidad.

## 10. Modales

- Un modal es responsabilidad de un único script (una sola autoridad); no registrar
  listeners de apertura/cierre por duplicado.
- Estado accesible: `aria-hidden` sincronizado con visibilidad real, foco inicial dentro
  del modal, `Escape` cierra, foco vuelve al disparador al cerrar.
- Footer de acciones: en mobile las acciones se apilan (`column-reverse`, ancho completo);
  en desktop van en fila alineadas a la derecha.

## 11. Accesibilidad

Mínimo exigible en toda pantalla:

- un solo `main` por página;
- skip link funcional al contenido principal;
- foco visible en todo elemento interactivo;
- navegación completa por teclado (tabs, modales, paginación, filtros);
- ARIA coherente (roles, `aria-selected`, `aria-current`, `aria-controls`, `aria-label`);
- nombres accesibles en botones con solo ícono (`aria-label` o `title`);
- paginación y modales accesibles según §9 y §10;
- no crear controles falsamente interactivos o deshabilitados sin motivo comunicado.

## 12. CSS

- CSS de módulo scopeado a su contenedor raíz (ej. `#venta-index-rework .clase`), no
  selectores globales que puedan filtrar comportamiento a otras vistas.
- Preferir tokens/componentes ya definidos (`shared-components.css`, `theme-ml.css`,
  variables `--vm-*` del módulo) antes que crear valores nuevos.
- No crear variantes locales de un componente que ya existe compartido, salvo necesidad
  real documentada.
- Eliminar CSS muerto solo con evidencia (sin selector usado en el HTML renderizado real).
- No reorganizar ni fusionar reglas existentes solo por estética.
- Respetar `@media (prefers-reduced-motion: reduce)` en animaciones nuevas.

## 13. JavaScript

- Una sola autoridad por comportamiento (un único script inicializa un componente dado).
- No mantener timers/listeners duplicados; usar flags (`dataset.xBound`) para evitar
  doble bind al re-renderizar parciales.
- Eliminar JS sin consumidor demostrado (verificar carga dinámica, no solo texto).
- Preferir helpers compartidos (`VentaModule`, `TheBury`) para comportamiento transversal
  (scroll affordance, toasts) antes de reimplementarlo por módulo.
- Handlers idempotentes cuando el nodo puede volver a inicializarse (modales, tabs tras
  fetch parcial).

## 14. Metodología de auditoría (4 capas)

Que una pantalla esté técnicamente normalizada, sea responsive, accesible y comparta
bien componentes **no significa que su UX esté optimizada**. Toda auditoría UI/UX
evalúa explícitamente estas 4 capas, no solo la primera:

1. **Técnica** — Razor/HTML, partials, CSS, JS, duplicación, contratos, responsive
   técnico, accesibilidad técnica, tests. Pregunta: ¿está bien construido y es
   consistente?
2. **Visual** — jerarquía, densidad, spacing, uso del color, contraste semántico,
   cards/bordes/pills, prioridad visual, scanability, redundancia visual. Pregunta:
   ¿se entiende qué es importante?
3. **Flujo UX** — la pantalla como tarea real: qué intenta hacer el usuario, qué ve
   primero, qué necesita decidir, qué información necesita, qué lo bloquea, qué acción
   espera, si hay acciones compitiendo, pasos o información innecesarios, ambigüedad
   entre guardar/confirmar/aplicar/continuar, dead ends, paridad de prioridad funcional
   en mobile. Sin cambiar reglas de negocio.
4. **Estados reales** — no solo happy path: vacío, con datos, completado, error,
   bloqueado, no viable, estado terminal, mobile. Muchos problemas de UX aparecen solo
   con datos, errores, bloqueos o estados terminales.

**Regla de evidencia**: una pantalla puede pasar la auditoría técnica y aun así
requerir mejoras de UX. El cierre distingue explícitamente
`Técnicamente: ✅/🟡`, `Visualmente: ✅/🟡`, `Flujo UX: ✅/🟡`. Buena paridad, responsive
y tests no habilitan por sí solos la conclusión "no hace falta tocarla".

**Regla de valor real**: toda propuesta visual o de flujo debe responder al menos una:
¿reduce carga cognitiva?, ¿aclara prioridad?, ¿elimina redundancia?, ¿reduce pasos?,
¿hace más evidente una acción?, ¿evita errores?, ¿mejora lectura, responsive o
accesibilidad?, ¿elimina ambigüedad? Si ninguna aplica, no cambiar.

**Redundancia cognitiva**: mismo estado repetido, mismo error mostrado varias veces,
mismo total destacado en varios lugares, badges + color + ícono + label diciendo lo
mismo, información duplicada entre resumen y detalle. Clasificar cada caso como
duplicación útil, duplicación accidental o redundancia cognitiva antes de tocar nada.

**Contradicciones de acciones**: detectar casos como un estado "Rechazado" junto a un
CTA "Confirmar", un CTA primario habilitado sobre un estado "no viable", o la misma
acción duplicada en header y panel. Distinguir contradicción visual (el layout la
sugiere) de contradicción funcional (el backend realmente lo permite); no afirmar bug
funcional sin verificar la lógica real.

**Color y señales**: rojo = bloqueo/error real, amarillo = advertencia, verde =
aprobado/completado, neutro = dato. No comunicar lo mismo simultáneamente con color +
borde + pill + ícono + badge + barra + texto; no tratar cada importe como si fuera un
estado semántico.

**Tablas y resúmenes densos** (complementa §6): priorizar scanability y alineación de
valores, no destacar todas las cifras por igual, dar ancho suficiente a las columnas
importantes, evitar repetir el mismo total con la misma prominencia, separar
claramente editable / calculado / resumen / detalle.

**Microcopy**: para títulos, labels, badges, mensajes, errores y acciones — ¿se
entiende?, ¿usa jerga interna innecesaria?, ¿explica qué hacer después?, ¿distingue
acción de resultado? No cambiar terminología de negocio establecida sin evidencia.

**Flujo end-to-end**: cuando la pantalla es parte de un flujo evidente (pantalla
anterior → actual → acción → resultado/siguiente pantalla), no auditarla como isla.

## 15. QA obligatorio

Antes de marcar una pantalla como cerrada, como mínimo:

```
Playwright contra la app real (no solo tests HTTP)
Viewports: 1440x900, 1280x720, 768x1024, 390x844, 360x800
  (+ 1024x720 / 900x720 si el problema está cerca de un breakpoint)
Navegación por teclado (tabs, modal, paginación)
Sin overflow horizontal de página
Consola sin errores nuevos
dotnet build del proyecto afectado
Tests focalizados relevantes
git diff revisado
```

Playwright observa, además de responsive: estado vacío, con datos, errores/bloqueos,
prioridad de acciones, scroll, redundancias visibles, modales, feedback y mobile — no
solo el happy path. No ejecutar acciones destructivas.

No afirmar que una pantalla quedó responsive o accesible sin haberla probado en
navegador real. No hacer push sin autorización explícita.

## 16. Implementaciones de referencia

| Referencia | Cubre |
|---|---|
| Global / `_Layout` (`Views/Shared/_Layout.cshtml`) | `main` único, skip link, sidebar, header, permisos por sección, modales globales |
| Venta / Index (`Views/Venta/Index_tw.cshtml`, `ventas-index.css`, `venta-index.js`, `venta-index-rework.js`) | listados ERP, tabla desktop + cards mobile paritarias, tabs responsive accesibles, paginación server-rendered, filtros preservados, acciones normalizadas, sticky mobile |

No asumir que toda pantalla nueva debe copiar literalmente `Venta/Index`; debe seguir
las mismas reglas de este documento, con el layout que su contenido requiera.
