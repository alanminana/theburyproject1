# MISA-INVENTARIO-FISICO-UX-0 — Auditoría UX de Inventario físico

Fase: audit-only.  
Rama: `misa/inventario-fisico-ux-0-auditoria`.  
Responsable: Misa.  
Fecha: 2026-05-22.

---

## A. Objetivo

Auditar completamente el módulo de Inventario físico del ERP TheBuryProject para entender su estado actual, detectar problemas de UX, visual, mobile y accesibilidad, y proponer un roadmap de rediseño frontend por fases, sin modificar ningún archivo de código.

---

## B. Contexto

El ERP maneja dos conceptos distintos pero relacionados:

- **Stock agregado (SKU):** número agregado de unidades por producto. Fuente de verdad operativa. Gestionado por `MovimientoStockService`. Se refleja en `Producto.StockActual`.
- **Unidades físicas:** tracking individual de cada unidad con código interno, número de serie opcional, estado, ubicación, trazabilidad por venta. Gestionado por `IProductoUnidadService`.

Ambos conceptos son independientes por diseño. Las vistas del módulo deben comunicar esa diferencia al usuario, que actualmente no siempre queda clara.

---

## C. Estado inicial de main

HEAD: `78653c8` — Escapar celdas dinamicas en venta create (VENTAS-UX-1E-B).  
Última fase de Misa integrada: `18975bf` — Auditar UX inventario y catalogo (MISA-INVENTARIO-UX-0).  
Working tree: `.claude/settings.local.json`, `AGENTS.md`, `CLAUDE.md` modificados localmente; `skills-lock.json` eliminado localmente. Ninguno se commitea.

---

## D. Archivos auditados

### Vistas

| Archivo | Líneas | Función |
|---|---|---|
| `Views/Producto/Unidades.cshtml` | 674 | Vista principal de inventario físico por producto |
| `Views/Producto/UnidadesGlobal.cshtml` | 277 | Reporte global de unidades físicas |
| `Views/Producto/UnidadHistorial.cshtml` | 77 | Historial de transiciones de una unidad |
| `Views/MovimientoStock/Index_tw.cshtml` | 254 | Movimientos de stock (nivel SKU) |
| `Views/MovimientoStock/Kardex_tw.cshtml` | 242 | Kardex por producto |
| `Views/MovimientoStock/Create_tw.cshtml` | 174 | Registro de ajuste de stock |
| `Views/AlertaStock/Index_tw.cshtml` | 369 | Listado de alertas de stock |
| `Views/AlertaStock/Criticos_tw.cshtml` | 190 | Productos agotados o bajo mínimo |
| `Views/AlertaStock/Estadisticas_tw.cshtml` | 194 | Estadísticas de alertas |
| `Views/AlertaStock/PorProducto.cshtml` | 145 | Alertas por producto |
| `Views/Shared/_EstadoUnidadBadge.cshtml` | 34 | Partial badge de estado de unidad |

### Controllers

| Archivo | Acciones auditadas |
|---|---|
| `Controllers/ProductoController.cs` | `Unidades`, `UnidadesGlobal`, `UnidadHistorial`, `CrearUnidad`, `CrearUnidadesMasivas`, `ActivarTrazabilidad`, `DesactivarTrazabilidad`, `MarcarUnidadFaltante`, `DarUnidadBaja`, `ReintegrarUnidadAStock`, `FinalizarReparacionUnidad`, `AjustarStockAgregadoAUnidadesFisicas`, `AjustarStockAgregadoHaciaAbajo` |

### JS

| Archivo | Función |
|---|---|
| `wwwroot/js/alerta-stock-index.js` | Auto-dismiss de toasts + confirmación de acciones + scroll affordance en AlertaStock |

### CSS relevantes

`wwwroot/css/shared-components.css`, `wwwroot/css/layout.css`, `wwwroot/css/catalogo-module.css`.

---

## E. Mapa funcional de Inventario físico

### ¿Qué representa Inventario físico en el sistema?

Es la gestión de unidades físicas individuales que forman parte del catálogo de productos. Cada unidad tiene un ciclo de vida rastreable: ingresa al stock, puede ser vendida, entregada, devuelta, marcada faltante, enviada a reparación, dada de baja o anulada.

No reemplaza el stock agregado (que es el número total de unidades disponibles de un SKU). Ambos sistemas coexisten y pueden estar desincronizados, de ahí la funcionalidad de conciliación.

### Diferencia entre conceptos clave

| Concepto | Qué es | Dónde se ve |
|---|---|---|
| **Producto / SKU** | Ficha del producto en catálogo | Catálogo / Producto/Edit |
| **Stock agregado** | Número total de unidades de ese SKU | `Producto.StockActual`, Kardex |
| **Movimiento de stock** | Transacción de entrada/salida/ajuste del SKU | MovimientoStock/Index + Kardex |
| **Unidad física** | Objeto físico individual (con código interno + serie) | Producto/Unidades |
| **Trazabilidad individual** | Bandera que exige seleccionar unidad física en cada venta | Toggle en Producto/Unidades |
| **Conciliación** | Comparación entre stock agregado y unidades en estado EnStock | Panel en Producto/Unidades |

### Acciones que puede hacer el usuario

**Frecuentes (operativas diarias):**
- Ver cuántas unidades físicas hay de un producto y en qué estado.
- Ver si hay diferencia entre el stock agregado y las unidades disponibles.
- Ver el historial de una unidad individual.
- Agregar una unidad física (una a la vez).
- Marcar una unidad como faltante, darla de baja o reintegrarla.

**Avanzadas (periódicas o de gestión):**
- Carga masiva de unidades (pegar series por línea o generar N sin serie).
- Activar o desactivar trazabilidad individual del producto.
- Ajustar el stock agregado para que coincida con las unidades físicas (conciliación).
- Ver el reporte global de todas las unidades de todos los productos (UnidadesGlobal).
- Ver alertas de stock, críticos, estadísticas.

### ¿Qué datos son críticos?

- Estado de cada unidad (EnStock, Vendida, Faltante, Baja, etc.)
- Número de serie (para trazabilidad fiscal/garantía)
- Diferencia de conciliación (si hay diferencia, hay un problema operativo)
- Acciones disponibles por estado de unidad

### ¿Qué datos sobran o están mal jerarquizados?

- Los KPIs de conciliación se muestran dos veces: grilla de 4 KPIs grandes + grilla de 6 estados menores. La segunda grilla (estados) debería ser colapsable o de menor jerarquía.
- Los botones "Ver kardex SKU" aparecen dos veces en la misma página (header + sección conciliación).
- Las notas aclaratorias en `text-slate-500/text-xs` en la sección de conciliación son críticas para entender el sistema pero tienen la misma jerarquía visual que texto decorativo.

---

## F. Pantallas detectadas

### F.1. Producto/Unidades — Vista principal de inventario físico por producto

**Ruta:** `/Producto/Unidades/{productoId:int}`  
**Modelo:** `ProductoUnidadesViewModel`  
**Largo:** 674 líneas. **Es la vista más densa del módulo.**

**Estructura:**
```
1. Alertas TempData (success/error)
2. Header: nombre, código, stock agregado, badge trazabilidad, back link, btn kardex
3. Sección: Toggle trazabilidad individual
4. Sección: Conciliación (KPI 4-up + KPI 6-up + mensaje interpretación + panel de ajuste asistido condicional)
5. Sección: Resumen de estados (grid de cards)
6. Sección: Agregar unidad (form inline, 4 campos)
7. Sección: Carga masiva (form inline, 4 campos + textarea + preview)
8. Form filtros (search + select estado + 3 checkboxes)
9. Tabla listado: 9 columnas + acciones por fila (forms inline con inputs de motivo + buttons)
```

**Objetivo de la vista:** Gestión completa del inventario físico de un producto específico.  
**Usuario esperado:** Encargado de inventario / administrador.

### F.2. Producto/UnidadesGlobal — Reporte global

**Ruta:** `/Producto/UnidadesGlobal`  
**Modelo:** `ProductoUnidadesGlobalViewModel`

**Estructura:**
```
1. TempData
2. Header con descripción
3. KPI grid: Total + En stock + Vendidas + Faltantes + Baja (+ condicionales: Devueltas, EnReparacion, Anuladas, Reservadas)
4. Filtros: producto (dropdown), estado (dropdown), texto (free), 6 checkboxes
5. Tabla: Producto, Código interno, N° serie, Estado, Ubicación, Cliente, Fecha venta, Ingreso, Acciones
```

**Objetivo:** Visión transversal de todas las unidades físicas. No permite acciones de cambio de estado, solo navegación.

### F.3. Producto/UnidadHistorial — Historial de una unidad

**Ruta:** `/Producto/UnidadHistorial/{unidadId:int}`  
**Modelo:** `ProductoUnidadHistorialViewModel`

**Estructura:**
```
1. Back link → Producto/Unidades
2. Header: código interno unidad + código/nombre producto + serie
3. Badge estado actual
4. Tabla: Fecha, De, A, Motivo, Origen, Usuario
```

**Objetivo:** Auditoría completa de ciclo de vida de una unidad. Vista simple, lectura pura.

### F.4. MovimientoStock/Index — Movimientos SKU

No es inventario físico, sino movimientos de stock agregado. Separado por diseño. Tiene `data-oc-scroll` y es visualmente coherente con el sistema.

### F.5. MovimientoStock/Kardex — Kardex por producto

Vista de sólo lectura por producto. Sin problemas graves.

### F.6. AlertaStock (4 vistas)

Módulo bien diferenciado del inventario físico. Opera sobre alertas de stock agregado, no sobre unidades físicas. Visualmente más sólido que Producto/Unidades:
- Usa `data-oc-scroll` con scroll affordance.
- Usa `badge-erp` components del design system.
- Tiene paginación en Index.
- Tiene breadcrumb en Criticos y Estadisticas.

---

## G. Flujo actual

```
Catálogo (Index_tw)
  └─ [Link "Unidades" por producto] ──► Producto/Unidades
         ├─ [Ver historial de unidad] ──► Producto/UnidadHistorial
         └─ [Ver kardex SKU] ──► MovimientoStock/Kardex

Catálogo (Index_tw)
  └─ [Link "Inventario físico global"] ──► Producto/UnidadesGlobal
         └─ [Ver historial] ──► Producto/UnidadHistorial
         └─ [Ver producto] ──► Producto/Unidades

MovimientoStock/Kardex
  └─ [Registrar ajuste] ──► MovimientoStock/Create

AlertaStock/Index
  └─ [Críticos] ──► AlertaStock/Criticos
  └─ [Estadísticas] ──► AlertaStock/Estadisticas
  └─ [Por producto] ──► AlertaStock/PorProducto
```

---

## H. Problemas de comprensión

1. **Dualidad stock agregado / unidades físicas no explicada al ingreso.** El usuario llega a Unidades y ve un panel de conciliación antes de entender qué son las unidades físicas. El primer contacto con la vista es una sección técnica compleja.

2. **"Trazabilidad individual" no es terminología intuitiva** para un encargado de inventario. El sistema la usa de forma consistente, pero un usuario nuevo necesitaría capacitación para entender qué activa o desactiva ese toggle.

3. **El panel de conciliación mezcla lectura y escritura** sin separación visual clara. Los KPIs de conciliación (¿cuánto hay de diferencia?) conviven con las acciones de ajuste (botones, inputs, formularios). Esto hace difícil entender qué es informativo y qué es accionable.

4. **Las acciones por fila son condicionales** y aparecen o desaparecen según el estado de la unidad. Un usuario no sabe de antemano qué va a ver cuando llegue a una fila. En una fila con EnReparacion, ve un select de destino + input de motivo + button. En una fila Faltante, ve input + button de reintegrar + input + button de dar baja. La variabilidad es confusa.

5. **"El stock agregado no fue modificado"** aparece en múltiples mensajes (TempData, notas de formulario). Es una advertencia correcta pero repetitiva. El usuario puede no entender por qué esto es importante si no conoce el sistema.

6. **UnidadesGlobal no explica su relación con Producto/Unidades.** El subtítulo dice "No reemplaza el stock agregado ni el Kardex SKU" pero no explica qué relación tiene con la vista por producto.

---

## I. Hallazgos UX

### I.1. Densidad extrema en Producto/Unidades

La vista tiene 7 secciones funcionales distintas en una sola página sin separación visual jerárquica fuerte. El usuario debe hacer scroll largo para llegar a la tabla de listado (la sección más frecuente en uso diario).

**Orden actual de secciones:**
1. Info producto (frecuente: ver)
2. Toggle trazabilidad (poco frecuente: configurar)
3. Conciliación (periódico: auditar)
4. Resumen estados (frecuente: ver)
5. Agregar unidad (periódico: crear)
6. Carga masiva (raro: importar)
7. Filtros + listado (frecuente: buscar/gestionar)

El listado (acción más frecuente) está al fondo. Los formularios de alta y carga masiva (acciones poco frecuentes) bloquean el camino hacia la tabla.

### I.2. Acciones de fila con demasiado peso vertical

La columna "Acciones" de la tabla de Unidades contiene, por fila:
- 1 link (Ver historial)
- Hasta 4 forms con input de motivo + button cada uno

Con `min-w-[18rem]` y `flex-col items-stretch`, cada fila con acciones disponibles puede tener 150-200px de alto. En una tabla con 20 unidades, el scroll se vuelve impracticable.

### I.3. Carga masiva visible aunque sea inusual

El formulario de carga masiva está siempre expandido en la página, aunque es una acción de configuración inicial (se usa una vez, no diariamente). Ocupa espacio valioso entre los formularios de alta y el listado.

### I.4. Duplicación de KPIs de conciliación

Los KPIs primarios (Stock agregado, Unidades disponibles, Diferencia, Registradas) y los secundarios (Vendidas, Faltantes, Baja, Devueltas, Reservadas, En reparación) están en grillas separadas pero visualmente del mismo nivel. El usuario no sabe cuál es más importante.

### I.5. Navegación sin breadcrumb en Unidades

La vista Producto/Unidades solo tiene un link de "Volver al Catálogo". No hay breadcrumb que muestre: Catálogo > Producto X > Unidades físicas. En AlertaStock/Criticos y Estadisticas sí hay breadcrumb.

---

## J. Hallazgos visuales

### J.1. Coherencia con dark theme

En general coherente. Uso consistente de `slate-950/slate-900/slate-800` para fondos, `border-slate-800` para separadores, `text-white` para titulares y `text-slate-400` para texto secundario.

### J.2. Textos de bajo contraste en notas críticas

- Las notas de conciliación (`text-xs text-slate-500`) contienen información operativa clave pero tienen bajo contraste en pantallas sin buena calibración.
  - Ejemplo: `"Unidades registradas incluye todas las unidades no eliminadas..."` — crítico para entender el panel pero se pierde visualmente.
- Las notas amber (`text-amber-200`) en secciones de formulario comunican correctamente, pero hay 3 notas amber distintas en la misma vista, diluye la urgencia.

### J.3. Jerarquía de secciones inconsistente

- `h2` y `h1` tienen el mismo tamaño (`text-base font-black text-white` para h2 vs `text-2xl font-black text-white` para h1).
- Las secciones son `rounded-lg border border-slate-800 bg-slate-950/60 p-5` todas iguales, sin diferenciación visual por importancia.
- No hay separadores de "zona de lectura" vs "zona de escritura".

### J.4. Badge partial es sólido

`_EstadoUnidadBadge.cshtml` es correcto: usa border + background + texto por cada estado, con colores semánticos apropiados. No hay que modificarlo.

### J.5. Botones de acción por fila con colores semánticos correctos

- Marcar faltante: amber (⚠)
- Reintegrar: emerald (✓)
- Dar de baja: red (✗)
- Finalizar reparación: blue

El sistema de colores es correcto, pero el volumen de botones por fila es el problema.

### J.6. Duplicación de botón "Ver kardex SKU"

El mismo botón aparece en el header de la vista y en la sección de conciliación. Uno es redundante.

---

## K. Hallazgos mobile

### K.1. Tabla de Unidades inutilizable en mobile

La tabla tiene 9 columnas + columna de acciones con forms (mínimo 18rem). Con `overflow-x-auto` el usuario debe hacer scroll horizontal extenso, pero:
- No hay `data-oc-scroll` ni scroll affordance (AlertaStock/Index sí lo tiene).
- No hay indicación visual de que la tabla tiene más contenido a la derecha.
- Los forms inline de la columna Acciones hacen que cada fila sea muy alta, agravando el scroll vertical.

### K.2. Formularios de Unidades en mobile

- El formulario de "Agregar unidad" (`lg:grid-cols-[1fr_1fr_1.4fr_auto] lg:items-start`) se colapsa a 1 columna en mobile, aceptable.
- El formulario de "Carga masiva" (`lg:grid-cols-[0.7fr_1.3fr_1fr_1.2fr]`) también, aceptable, pero el textarea de series (5 rows) ocupa mucho espacio.
- Los botones de submit tienen tamaño adecuado para touch.

### K.3. Sección de conciliación en mobile

Las grillas de KPIs (`sm:grid-cols-2`, `sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6`) se colapsan a 2 columnas en mobile. Aceptable en lectura, pero el panel de acciones de ajuste con sus propios grids y formularios se vuelve confuso.

### K.4. UnidadesGlobal en mobile

Tabla con `overflow-x-auto` pero sin scroll affordance. 9 columnas. Inutilizable en mobile sin scroll horizontal, que no es obvio.

### K.5. AlertaStock mobile

Correcto: usa `data-oc-scroll` con affordance y fade. Es el modelo a seguir para las otras tablas.

---

## L. Hallazgos accesibilidad / baja visión

### L.1. Tablas sin scope="col" en headers

Ninguna de las tablas auditadas en Producto/Unidades, Producto/UnidadesGlobal, Producto/UnidadHistorial, MovimientoStock/Index o MovimientoStock/Kardex tiene `scope="col"` en sus `<th>`. Esto perjudica a lectores de pantalla que no pueden asociar celdas de datos con sus encabezados.

Afectado:
- `Unidades.cshtml` líneas 537–546 (9 th)
- `UnidadesGlobal.cshtml` líneas 199–208 (9 th)
- `UnidadHistorial.cshtml` líneas 34–39 (6 th)
- `MovimientoStock/Index_tw.cshtml` líneas 173–184 (11 th)
- `MovimientoStock/Kardex_tw.cshtml` líneas 163–173 (10 th)

### L.2. Inputs de motivo sin label en acciones de fila

Los inputs de "Motivo obligatorio" dentro de las acciones por fila en Unidades.cshtml son `<input placeholder="Motivo obligatorio" required />` sin `<label for="">`. Los lectores de pantalla solo leerán el placeholder, que desaparece cuando el usuario comienza a escribir.

Afectado: acciones MarcarFaltante, ReintegrarAStock, DarBaja, FinalizarReparacion — cada una con `<input name="Motivo">` sin label (líneas 593, 607, 622, 645).

### L.3. Botón "Desactivar" bloqueado sin aria-disabled

El botón desactivado de trazabilidad (cuando hay unidades activas) usa `cursor-not-allowed` y `title` para explicar el bloqueo, pero no tiene `aria-disabled="true"` ni `role="button"` explícito. El `title` no es accesible en touch ni lectura de pantalla confiable.

```
Unidades.cshtml línea 109-113
```

### L.4. Labels en UnidadesGlobal sin for

Los labels en la sección de filtros de UnidadesGlobal usan `<label class="mb-1 block ...">Producto</label>` sin atributo `for`. El campo de texto que sigue no tiene `id` coincidente. La asociación entre label y control es implícita solo visualmente.

Afectado: líneas 101–126 de UnidadesGlobal.cshtml.

### L.5. Contraste de texto slate-400/500

- `text-slate-400` en texto de body (~4.5:1 en slate-950): pasa WCAG AA para texto normal.
- `text-slate-500` en notas de texto pequeño (xs) sobre slate-950: ~3:1, puede no pasar WCAG AA para texto pequeño (requiere 4.5:1).

Afectado: notas de conciliación, timestamps de última actualización.

### L.6. Foco visible

No se detectaron problemas graves de foco. Los inputs tienen `focus:border-primary` y `focus:ring-1 focus:ring-primary`. Los botones tienen estilos hover. Sin embargo, no se verificó `focus-visible` explícito para no confundir click con keyboard.

### L.7. Acciones solo con icono

En UnidadesGlobal, las acciones tienen `title` y texto explícito ("Historial", "Producto"). En AlertaStock/Index, las acciones tienen `aria-label` explícito. Unidades.cshtml tiene texto en los botones de acción de fila. No se detectó acción-solo-con-icono sin texto o aria-label.

---

## M. Hallazgos seguridad frontend

- No se detectaron interpolaciones peligrosas en las vistas auditadas. Razor escapa automáticamente con `@valor`.
- Los motivos y observaciones del servidor se renderizan con `@unidad.Observaciones`, `@movimiento.Motivo`, etc. — escapados por Razor.
- `alerta-stock-index.js` usa `dataset.alertaConfirm` que se lee como texto plano, no se interpola en HTML.
- No hay `innerHTML` en el JS auditado.
- No hay XSS detectado. Zona sin deuda de seguridad conocida.

---

## N. Recomendación principal de rediseño

### Opción recomendada: B+F — Dividir en secciones con jerarquía visual clara + acciones avanzadas colapsables

**Mantener la vista Producto/Unidades como vista única** (no crear tabs ni vistas separadas). Reorganizar internamente:

1. **Header:** mantener. Eliminar el botón duplicado "Ver kardex SKU" (queda solo en conciliación).

2. **Toggle de trazabilidad:** mover abajo del header pero hacer la sección más compacta y discreta (no es la acción principal en uso diario).

3. **Sección de conciliación:** separar lectura de escritura.
   - Panel superior (siempre visible): diferencia en grande, estado conciliado/diferencia, link "Ver acciones" si hay diferencia.
   - Panel de acciones de ajuste: colapsable (expandir solo cuando hay diferencia o el usuario lo pide). Reduce el ruido en el 80% de los casos donde está conciliado.

4. **Resumen de estados:** mover arriba, justo después de header. Es la info más frecuente.

5. **Tabla de listado:** llevar arriba del fold (debería ser la primera cosa que el usuario ve después del resumen).

6. **Formularios Agregar / Carga masiva:** colapsar ambos por defecto. Expandir con botón "Agregar unidad +" y "Carga masiva +" en la parte superior de la sección de tabla.

7. **Acciones de fila:** consolidar en menú expandible por fila en lugar de mostrar todos los forms inline. El usuario clickea "Acciones" en la fila → se despliega el panel de esa unidad con el form correspondiente a su estado.

**Beneficios:**
- No cambia endpoints, payloads, IDs, names, antiforgery.
- Reduce el scroll significativamente.
- La acción más frecuente (ver listado) pasa a ser lo primero visible.
- Las acciones avanzadas (conciliación, carga masiva) se ocultan hasta que se necesitan.
- Compatible con el dark theme actual.

---

## O. Alternativas descartadas

### Opción A — Solo accesibilidad y responsive, sin reorden

Descartada como opción única: el problema de densidad es real y va más allá de accesibilidad. Agregar `scope="col"` y labels mejora accesibilidad pero no resuelve el UX.

### Opción C — Tabs internas en Unidades

Descartada: las secciones están muy relacionadas entre sí. Separar "Conciliación" en un tab y "Listado" en otro haría difícil actuar sobre una unidad después de ver la conciliación.

### Opción D — Cards en mobile, tabla en desktop

No descartada, es compatible con la Opción B+F. Se puede implementar en MISA-INVENTARIO-FISICO-UX-1C si la Opción B+F no resuelve suficientemente el problema mobile.

### Opción E — Tabla desktop + cards mobile

Similar a D. Viable como siguiente paso después de la reorganización base.

### Crear vista separada para acciones avanzadas

Descartada: el usuario necesita ver el contexto (conciliación, estado) mientras actúa. Separarlos en URLs distintas rompe el flujo.

---

## P. Qué mantener separado

- **Producto/UnidadesGlobal** debe mantenerse como vista separada. Es un reporte transversal y su función es distinta a la gestión por producto.
- **Producto/UnidadHistorial** debe mantenerse como vista separada. Es una vista de solo lectura enfocada en una unidad. Lanzarla desde la tabla (link externo) es correcto.
- **MovimientoStock** (Index + Kardex + Create) debe mantenerse separado. Opera sobre stock agregado (SKU), no sobre unidades físicas. La relación entre los sistemas es la que hay que comunicar mejor, no fusionarlos.
- **AlertaStock** debe mantenerse separado. Es un módulo de gestión de alertas con su propio flujo.

---

## Q. Qué integrar visualmente

- **AlertaStock/Index → link a UnidadesGlobal** cuando una alerta está resuelta o en proceso: "Ver unidades físicas del producto" desde Details. Actualmente no hay navegación directa.
- **MovimientoStock/Kardex → link a Producto/Unidades** del mismo producto: "Ver unidades físicas". Actualmente no hay ese link.
- **Producto/Unidades → link a AlertaStock** del mismo producto: "Ver alertas de stock". Actualmente no hay ese link. Sería útil cuando StockActual está bajo.

---

## R. Riesgos funcionales

### R.1. No tocar

- `MovimientoStockService` — gestiona el stock agregado. Cualquier ajuste de stock pasa por aquí.
- `IProductoUnidadService` — gestiona las unidades físicas y sus transiciones de estado.
- Los endpoints de acción: `CrearUnidad`, `CrearUnidadesMasivas`, `MarcarUnidadFaltante`, `DarUnidadBaja`, `ReintegrarUnidadAStock`, `FinalizarReparacionUnidad`, `AjustarStockAgregadoAUnidadesFisicas`, `AjustarStockAgregadoHaciaAbajo`.
- La lógica de conciliación: el backend recalcula antes de aplicar ajustes. No simular en frontend.
- Los antiforgery tokens en todos los forms POST.
- El `id="form-carga-masiva-unidades"` (usado como anchor link desde el panel de conciliación `href="#form-carga-masiva-unidades"`).
- El `id="ajuste-asistido"` y `id="listado-unidades"` (usados como anchor links internos).
- El `id="alerta-producto"`, `id="alerta-tipo"`, `id="alerta-prioridad"`, `id="alerta-estado"` en AlertaStock (pueden estar usados por JS o tests).

### R.2. Riesgo de colapsado de formularios

Si se colapsan los formularios de "Agregar unidad" y "Carga masiva" con JavaScript, hay que preservar el anchor link `#form-carga-masiva-unidades` que el panel de conciliación usa. El colapso debe auto-expandir el form si se accede por ese anchor.

### R.3. Riesgo de acciones de fila en modo colapsado

Si se consolidan las acciones de fila en un panel expandible por row, hay que preservar el comportamiento de submit (POST con antiforgery). No usar fetch/XHR sin instrucción explícita.

### R.4. Tests de contratos UI

El proyecto tiene tests de `LayoutUiContractTests` y `UiContract`. No se auditaron los specs de Playwright de Inventario en esta fase, pero es necesario verificarlos antes de modificar selectores o IDs en fases siguientes.

---

## S. Roadmap propuesto

| Fase | Nombre | Objetivo | Alcance | Prioridad |
|---|---|---|---|---|
| **MISA-INVENTARIO-FISICO-UX-1A** | Accesibilidad semántica mínima | `scope="col"` en todas las tablas del módulo + labels con `for` en inputs de motivo de fila + `aria-disabled` en botón bloqueado de trazabilidad | Razor puro, sin JS | Alta |
| **MISA-INVENTARIO-FISICO-UX-1B** | Reorden visual y jerarquía | Mover el listado arriba del fold. Separar lectura de escritura en conciliación. Eliminar botón kardex duplicado. Aumentar contraste de notas críticas. Agregar breadcrumb | Razor + CSS mínimo | Alta |
| **MISA-INVENTARIO-FISICO-UX-1C** | Mobile / scroll affordance | Agregar `data-oc-scroll` a tablas de Unidades y UnidadesGlobal. Evaluar cards en mobile para tabla de Unidades | Razor + JS mínimo | Media |
| **MISA-INVENTARIO-FISICO-UX-1D** | Acciones de fila colapsables | Consolidar forms inline de acción por fila en un panel expandible por row. Colapsado de secciones Agregar/Carga masiva por defecto | Razor + JS | Media |
| **MISA-INVENTARIO-FISICO-UX-2** | Integraciones de navegación | Links cruzados: Kardex → Unidades, Unidades → AlertaStock, AlertaStock → Unidades | Razor puro | Baja |

---

## T. Fase siguiente recomendada

**MISA-INVENTARIO-FISICO-UX-1A — Accesibilidad semántica mínima.**

Es la fase de menor riesgo, mayor impacto en accesibilidad y completamente Razor-only sin tocar JS ni backend. Agrega `scope="col"` en todas las tablas afectadas, `aria-disabled` en el botón bloqueado, y labels con `for` en inputs de motivo de fila.

Estimación: 5 archivos modificados, cambios pequeños y reversibles, sin impacto funcional.

---

## U. Validaciones ejecutadas

Esta fase es audit-only.

```powershell
git diff --check
# → sin whitespace errors

git status --short
# → M .claude/settings.local.json
# → M AGENTS.md
# → M CLAUDE.md
# → D skills-lock.json
# → A docs/misa-inventario-fisico-ux-0-auditoria.md
```

No se ejecutó build, dotnet test ni Playwright.  
Motivo: la fase es doc-only. El diff confirma que solo se creó el documento.

---

## V. Procesos

Esta fase no inició ningún proceso externo (sin build, sin dotnet run, sin Playwright, sin Node de test).

Procesos preexistentes documentados (no iniciados por esta tarea, no se cierran):
- VS Code (Code.exe)
- C# DevKit / Language Server
- Playwright MCP
- Context7 MCP

---

## W. Deudas abiertas

1. No se auditaron los specs Playwright de Inventario (si existen). Verificar antes de modificar selectores en fases 1A–1D.
2. No se auditó `ViewModels/ProductoUnidadesViewModel.cs` ni `IProductoUnidadService` en profundidad. Solo se inspeccionaron desde las vistas y el controller.
3. El contraste exacto de `text-slate-500` sobre `slate-950` debería medirse con herramienta (no se midió en esta fase).
4. No se verificó si `#form-carga-masiva-unidades` ni `#ajuste-asistido` ni `#listado-unidades` están referenciados en tests de contrato UI o Playwright.
5. AlertaStock/Details y AlertaStock/PorProducto/Details no fueron auditados (están fuera del alcance principal de inventario físico).
