# UI-3 - Home / Dashboard dark accesible

Fecha: 2026-05-18
Responsable: Kira
Alcance: rework visual del Dashboard principal (Views/Dashboard/Index.cshtml). No se tocó lógica de negocio ni módulos productivos.

## A. Objetivo

Aplicar el Design System dark accesible (UI-1) al Home/Dashboard operativo del ERP.
Mejorar jerarquía UX, contraste, legibilidad, accesibilidad y mobile.
Bajar protagonismo de los KPIs financieros y priorizar alertas y pendientes operativos.
Preparar el bloque de Notas rápidas (ya existente, funcional con localStorage).

## B. Archivos revisados

- docs/ui-1-design-system-dark-accesible.md
- docs/ui-2-login-dark-accesible.md
- docs/ui-rework-guia-operativa.md
- Views/Home/Index.cshtml
- Views/Dashboard/Index.cshtml
- Controllers/HomeController.cs
- Controllers/DashboardController.cs
- ViewModels/DashboardViewModel.cs
- wwwroot/css/dashboard-module.css
- wwwroot/css/shared-components.css
- wwwroot/js/dashboard-index.js
- wwwroot/js/horizontal-scroll-affordance.js

## C. Archivos modificados

- Views/Dashboard/Index.cshtml — único archivo de código modificado.
- docs/ui-3-home-dashboard-dark-accesible.md — este documento (creado).

No se modificó: dashboard-module.css, dashboard-index.js, DashboardController.cs, DashboardViewModel.cs, HomeController.cs.

## D. Diagnóstico Home/Dashboard

### D.1 Vista canónica
- Views/Home/Index.cshtml es un stub. HomeController.Index() hace RedirectToAction("Index", "Dashboard"). No tiene contenido relevante.
- Views/Dashboard/Index.cshtml es la vista canónica del Home/Dashboard. Es la pantalla principal post-login.

### D.2 No hay duplicación funcional
Existe un único Dashboard real. Views/Home/Index.cshtml no es una vista paralela; es un redirector de entrada.

### D.3 Controlador y servicio
DashboardController.cs carga datos vía IDashboardService.GetDashboardDataAsync() y pasa DashboardViewModel a la vista. Sin lógica visual.

### D.4 CSS
dashboard-module.css define solo layout responsive (grid de main + sidebar, sticky sidebar, hero metrics en 4 columnas en desktop). Correcto y mínimo.
shared-components.css provee las clases canónicas: card-erp-metric, card-erp-metric-success/warning/danger/info, hero-erp, btn-erp-ghost, btn-erp-sm, badge-erp-*, section-header-erp.

### D.5 JS
dashboard-index.js maneja:
- Tabs de cuotas (vencidas / próximas) via data-dashboard-tab y data-dashboard-tab-panel.
- Notas rápidas via data-dashboard-note-input, data-dashboard-note-list, data-dashboard-note-status, localStorage (NOTAS_KEY).
- Scroll affordance via TheBury.initHorizontalScrollAffordance.
Todos los contratos data-* se preservaron sin cambios.

### D.6 Problemas visuales detectados antes del rework

| Problema | Impacto |
|---|---|
| "Ventas totales" en primer KPI con verde brillante | Protagonismo excesivo en pantalla con pocas ventas |
| text-[11px] en labels KPI del hero | Por debajo del mínimo recomendado de 12px |
| text-[10px] en IDs de cliente en tabla cuotas | Por debajo del mínimo recomendado |
| text-slate-500 en badge de fecha de cuotas próximas | Contraste insuficiente para dato operativo |
| Botones "Reponer" con clases inline | No usa btn-erp-ghost canónico |
| group-hover:scale-110 transition-transform en íconos accesos rápidos | Animación decorativa, viola regla de movimiento mínimo |
| Sin focus-visible en accesos rápidos (links) | Accesibilidad de teclado incompleta |
| Sin focus-visible en tabs de cuotas | Accesibilidad de teclado incompleta |
| Sin aria-label en botones de acción de filas | Accesibilidad de lectores de pantalla |
| Inline style con radial-gradient en hero-erp | Decorativo innecesario; hero-erp ya define el fondo |

### D.7 Datos hardcodeados (deuda funcional, no visual)
- Tabla de alertas de inventario: 3 filas con SKUs hardcodeados (BP-1022, BP-5044, BP-9012). No vienen del backend.
- Actividad reciente: 3 ítems hardcodeados. No vienen del backend.
Estos son deudas funcionales pendientes de una fase de conectividad de datos.

## E. Cambios visuales aplicados

### E.1 Jerarquía KPIs en el hero — reordenamiento
**Antes:** Ventas totales (verde) → Operaciones → Créditos activos → Cuotas en vista
**Después:** Cuotas vencidas (danger/rojo) → Créditos activos (info/azul) → Operaciones (warning/amber) → Ventas del mes (neutral/slate)

Resultado operativo:
- Lo urgente (cuotas vencidas) es lo primero visible.
- Los créditos activos y operaciones dan contexto operativo inmediato.
- Las ventas monetarias están disponibles pero no dominan la pantalla.

### E.2 Tipografía labels KPI
text-[11px] → text-xs (12px) en los 4 labels de KPIs del hero.
tracking-[0.18em] → tracking-[0.14em] (menos espaciado agresivo).

### E.3 Color KPI "Ventas del mes"
card-erp-metric-success (verde) → card-erp-metric (neutral).
text-emerald-300 → text-slate-100 en el valor numérico.
Resultado: el valor de ventas monetario sigue visible pero no llama más la atención que las alertas operativas.

### E.4 Inline style decorativo del hero — eliminado
Se quitó `style="background:radial-gradient(...)"`. hero-erp ya define `background-color: #1a202c` en shared-components.css.

### E.5 Botones "Reponer" — canonizados
3 instancias de clases inline sin coherencia → btn-erp-ghost btn-erp-sm.
btn-erp-ghost ya incluye: focus-visible con outline 2px solid #135bec, hover, disabled, y padding correcto.

### E.6 IDs de cliente en tabla de cuotas
text-[10px] → text-[11px] en las 2 filas de ID de cliente (vencidas y próximas).

### E.7 Badge de fecha en cuotas próximas
text-slate-500 → text-slate-400 en el badge de fecha de vencimiento.

### E.8 Accesos rápidos — foco y animación
- Eliminado: class group, group-hover:scale-110 y transition-transform de los 6 links.
- Agregado: focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50 en los 6 links.

### E.9 Tabs de cuotas — foco
Agregado focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50 en ambos tabs (Vencidas / Próximas).

### E.10 Botones de acción en filas de cuotas — foco y accesibilidad
- Botón de pago (cuotas vencidas): agregado focus-visible ring + aria-label="Registrar pago".
- Botón de detalle (cuotas próximas): agregado focus-visible ring + aria-label="Ver detalle de cuota".

## F. Jerarquía UX final

Orden de lectura en desktop:

**Hero (siempre visible):**
1. Cuotas vencidas — urgente operativo
2. Créditos activos — contexto de cobranza
3. Operaciones del mes — actividad comercial
4. Ventas del mes — KPI financiero en segundo plano

**Columna principal:**
5. Alertas de stock crítico — acción inmediata (inventario)
6. Cuotas y pagos (tabs) — cobranza operativa

**Sidebar derecho:**
7. Accesos rápidos — navegación operativa frecuente
8. Actividad reciente — feed de eventos (datos hardcodeados, deuda funcional)
9. Notas rápidas — bloc de trabajo diario

## G. Notas rápidas

El bloque ya existía con estructura visual completa y funcional.

Implementación: localStorage via NOTAS_KEY = 'dashboard_notas'.
- Máximo 20 notas.
- Guardado al hacer click en "Guardar" o presionar el botón del header.
- Eliminación por índice.
- Renderizado dinámico sin recarga.
- Timestamp en es-AR.

**Deuda documentada:** el uso de localStorage es temporal. No persiste entre dispositivos ni usuarios distintos. La migración a persistencia de backend requiere una entidad, endpoint, servicio y migración. Esto NO se hace en UI-3.

No se tocó: ni la entidad, ni el servicio, ni la migración, ni el contrato data-* del JS.

## H. Accesibilidad aplicada

| Check | Estado |
|---|---|
| Texto mínimo operativo 14px en contenido clave | OK — text-sm (14px) en nombres y montos |
| Labels KPI subidos a text-xs (12px) mínimo | OK — antes era 11px |
| IDs de cliente subidos a 11px | OK — antes era 10px |
| Focus visible en links de accesos rápidos | OK — agregado focus-visible ring |
| Focus visible en tabs de cuotas | OK — agregado focus-visible ring |
| Focus visible en botones de acción por fila | OK — btn-erp-ghost ya lo tenía; action buttons de cuotas, agregado |
| aria-label en botones icon-only | OK — agregado en "Registrar pago" y "Ver detalle de cuota" |
| aria-pressed en tabs | OK — ya existía, no se tocó |
| data-oc-scroll-region tabindex="0" | OK — ya existía para tablas horizontales |
| Estados con texto, no solo color | OK — todos los badges tienen texto |
| Contraste slate-500 eliminado en datos operativos | OK — subido a slate-400 |

## I. Mobile / Responsive

dashboard-module.css gestiona el grid responsivo:
- Mobile / tablet: 1 columna (main + sidebar apilados), hero metrics en 2 columnas.
- Desktop (≥1024px): main + sidebar de 22rem, hero metrics en 4 columnas.

No se modificó dashboard-module.css. El layout responsive se preserva sin cambios.

Los accesos rápidos mantienen grid de 2 columnas con py-4 (targets táctiles cómodos).
Los tabs tienen min-h implícito por padding; operables en touch sin problemas.

## J. Reglas funcionales preservadas

- No se tocó autenticación.
- No se tocó lógica de negocio.
- No se tocaron controllers, services, viewmodels ni migraciones.
- No se tocó dashboard-index.js.
- Todos los contratos data-* preservados sin cambios:
  - data-dashboard-tab, data-dashboard-tab-panel, data-dashboard-tab-tone
  - data-dashboard-note-input, data-dashboard-note-list, data-dashboard-note-status
  - data-dashboard-action, data-dashboard-notes
  - data-oc-scroll, data-oc-scroll-hint, data-oc-scroll-fade, data-oc-scroll-region
- asp-controller, asp-action, asp-route-* de todos los links preservados.
- IDs preservados: tabVencidas, tabProximas, tbodyVencidas, tbodyProximas, notaTextarea, notaStatus, notasLista.
- No se crearon vistas paralelas.
- No se crearon entidades, migraciones ni servicios nuevos.

## K. Validaciones

- dotnet build --configuration Release → Compilación correcta. 0 Advertencias, 0 Errores.
- git diff --check → OK (sin whitespace errors).
- git status: solo Views/Dashboard/Index.cshtml modificado + docs/ui-3-home-dashboard-dark-accesible.md creado.

## L. Tests

Ejecutado: `dotnet test --configuration Release --filter "Home|Dashboard|Reporte|UiContract"`

Resultado: **227/227 pasados, 0 fallados, 0 omitidos.**

No existen tests específicos de markup visual del Dashboard. Los contratos data-* no tienen test de contrato dedicado. Documentado como deuda para UI-4 o UI-5 si se agrega JS significativo.

## M. Riesgos y deudas remanentes

| Item | Tipo | Prioridad |
|---|---|---|
| Tabla de alertas de stock con datos hardcodeados | Deuda funcional | Media — conectar a AlertaStockService en fase de datos |
| Actividad reciente con datos hardcodeados | Deuda funcional | Media — conectar a log de actividad en fase de datos |
| Notas rápidas con localStorage | Deuda funcional | Baja — migrar a backend en fase futura si el usuario lo necesita |
| dark:bg-slate-950/40 en items de notas (en JS) | Deuda visual menor | Baja — el JS renderiza con bg con transparencia; no es crítico |
| Tabla de inventario aún sin datos reales del backend | Deuda funcional | Media |
| OrdenesCompraPendientes del ViewModel no renderizados | Deuda funcional | Baja — el ViewModel ya tiene el dato; falta una sección visual en el Dashboard |
| text-[10px] en timestamps de "Actividad reciente" | Deuda visual menor | Baja — datos hardcodeados; se resolverá al conectar el feed real |
| No hay test de contrato para markup del Dashboard | Deuda técnica | Baja — agregar si se toca JS en fases posteriores |

## N. Próximo paso recomendado

UI-4: Layout global dark accesible.

Alcance recomendado:
- _Layout.cshtml: dark sólido, contraste, legibilidad.
- Sidebar: estado activo claro, contraste, permisos visibles, colapso mobile.
- Header: reducir ruido, información útil visible.
- Menú mobile: overlay sólido, foco, cierre predecible.

Reglas para UI-4:
- Revisar layout.css, shared-components.css, layout.js, shared-ui.js antes de tocar.
- No cambiar colapso del sidebar sin entender TheBury.toggleSidebar y almacenamiento actual.
- Agregar tests de contrato antes de cambiar estructura global si hay JS fuerte.
- Micro-lote acotado: no rediseñar todo el Layout en una sola fase.

## Cierre UI-3

- [x] Vista canónica identificada: Views/Dashboard/Index.cshtml.
- [x] Jerarquía KPIs reordenada: urgente primero, financiero al final.
- [x] Tipografía labels subida a text-xs mínimo.
- [x] Ventas del mes bajada a card neutral (sin verde protagonista).
- [x] Botones "Reponer" canonizados a btn-erp-ghost btn-erp-sm.
- [x] Contraste text-slate-500 corregido a text-slate-400 en datos operativos.
- [x] Animación decorativa eliminada de accesos rápidos.
- [x] Focus visible agregado en links de accesos rápidos.
- [x] Focus visible agregado en tabs de cuotas.
- [x] aria-label agregado en botones icon-only de cuotas.
- [x] Notas rápidas: bloque existente preservado, localStorage documentado como deuda temporal.
- [x] No se tocó Layout global.
- [x] No se tocó sidebar/nav/header global.
- [x] No se tocó lógica de negocio.
- [x] No se tocaron controllers, services, entities, migrations.
- [x] No se crearon vistas paralelas.
- [x] Build Release OK.
- [x] git diff --check OK.
- [x] Tests 227/227 OK.
- [x] Documento UI-3 creado.
