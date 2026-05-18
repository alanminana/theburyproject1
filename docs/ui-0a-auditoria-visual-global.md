# UI-0A - Auditoria visual global del ERP

Fecha: 2026-05-18
Responsable: Kira
Alcance: auditoria documental del frontend Razor/CSS/JS. No se modifico codigo productivo.

## Resumen ejecutivo

TheBuryProject ya tiene una base dark-first razonable y varios esfuerzos canonicos iniciados: Tailwind v4 compilado, tokens en `tailwind-input.css`, layout global en `_Layout.cshtml`, componentes compartidos en `shared-components.css`, affordance compartida para scroll horizontal y CSS/JS por modulo. El camino mas saludable es consolidar esos componentes compartidos, no redisenar pantalla por pantalla desde cero.

El principal problema visual no es ausencia total de sistema, sino convivencia de tres capas: clases compartidas canonicas, clases por modulo y utilidades Tailwind inline repetidas en vistas grandes. Esa mezcla genera contraste irregular, superficies demasiado transparentes, modales con tamanos y overlays distintos, tablas dependientes de scroll horizontal y acciones con jerarquia visual variable.

La prioridad del rework debe ser accesibilidad operativa: texto mas legible, superficies mas solidas, estados mas distinguibles, botones consistentes, tablas mas usables en mobile/notebook y un criterio unico para modales, filtros, empty/loading/error states.

## Inventario frontend

Comandos ejecutados:

- `Get-ChildItem Views -Recurse -Filter "*.cshtml" | Select-Object FullName`
- `Get-ChildItem wwwroot\css -Recurse -File | Select-Object FullName`
- `Get-ChildItem wwwroot\js -Recurse -File | Select-Object FullName`
- `Get-ChildItem TheBuryProyect.Tests\Unit -Recurse -Filter "*UiContractTests.cs" | Select-Object FullName`
- `Select-String -Path "Views/**/*.cshtml","wwwroot/css/*.css","wwwroot/js/*.js" -Pattern "bg-black|bg-slate|text-slate|opacity|backdrop|blur|gradient|overflow-x|modal|table|badge|btn|input|select|hidden|transition-all|shadow|rounded|dark|grid|flex" -CaseSensitive:$false`

Conteo detectado:

- Vistas Razor: 135 archivos `.cshtml`.
- CSS: 18 archivos en `wwwroot/css`.
- JavaScript: 74 archivos en `wwwroot/js`.
- Tests de contrato UI: 5 archivos `*UiContractTests.cs`.
- Archivos con `data-oc-scroll` u `overflow-x-auto`: 53.
- Archivos con `backdrop-blur`, `bg-black/`, `bg-slate-950/` o `shadow-2xl`: 55.
- Archivos usando clases canonicas `btn-erp`, `badge-erp`, `card-erp`, `table-erp-wrapper`, `filter-*` o `row-action`: 90.
- Archivos con texto/faint placeholders de bajo contraste potencial (`text-slate-500`, `text-slate-600`, `placeholder:text-slate-600`, `#475569`): 110.

### Layouts y partials compartidos detectados

Canonicidad estimada:

- Canonico: `Views/Shared/_Layout.cshtml`. Define layout dark, sidebar, topbar, carga `tailwind.css`, `layout.css`, `shared-components.css`, `shared-ui.js`, `layout.js`, tickets y modales compartidos.
- Canonico: `Views/Shared/_ConfirmModal.cshtml`, `_TicketModal.cshtml`, `_TicketPanel.cshtml`, `_VentaDevolucionModal.cshtml`, `_EstadoUnidadBadge.cshtml`.
- Canonico parcial: tabs y partials de Seguridad (`_SeguridadTabs`, `_SeguridadUsuariosTab`, `_SeguridadRolesTab`, `_SeguridadPermisosRolTab`, `_SeguridadAuditoriaTab`).
- Duplicado/paralelo controlado: partials `_CajaModuleStyles`, `_ClienteModuleStyles`, `_CreditoModuleStyles`, `_DocumentoClienteModuleStyles`, `_OrdenCompraModuleStyles`, `_ProveedorModuleStyles`, `_SeguridadModuleStyles`, `_TicketModuleStyles`, `_VentaModuleStyles` para cargar CSS por modulo y affordance horizontal.
- Legacy explicito: `Views/Venta/Create_tw_legacy.cshtml`; comentarios en Catalogo indican flujo legacy/admin de `ProductoCondicionPago`; `dark-theme.css` queda como placeholder legacy.
- Incierto: vistas sin sufijo `_tw` en modulos antiguos (`CambiosPrecios`, `Devolucion`, `PlantillaContratoCredito`, algunas de `Producto`) requieren validacion funcional antes de usarlas como referencia visual.

### CSS global y CSS por modulo

Global/canonico:

- `wwwroot/css/tailwind-input.css`: fuente de Tailwind v4, tokens globales dark-first, scanning de Razor.
- `wwwroot/css/tailwind.css`: salida compilada.
- `wwwroot/css/layout.css`: sidebar responsive, collapse desktop, mobile overlay, scrollbar y ajuste de escala 1400-1700 px.
- `wwwroot/css/shared-components.css`: sistema canonico de botones, cards, hero, filtros, tablas, badges, row actions, selects y reglas puntuales de accesibilidad.
- `wwwroot/css/horizontal-scroll-affordance.css`: affordance compartida para regiones con scroll horizontal.
- `wwwroot/css/standalone-tokens.css`: tokens para paginas standalone como login/access denied.
- `wwwroot/css/dark-theme.css`: placeholder legacy.
- `wwwroot/css/site.css`: scaffold/legacy Bootstrap residual.

Por modulo:

- `caja-module.css`, `catalogo-module.css`, `cliente-module.css`, `credito-module.css`, `dashboard-module.css`, `documentocliente-module.css`, `ordencompra-module.css`, `proveedor-module.css`, `seguridad-module.css`, `venta-module.css`.

Observacion: algunos modulos tienen input/select propios (`venta-input`, `vm-input`, `credito-input`, `caja-input`, `catalogo-input`) que repiten tokens y estados. Son utiles como puente, pero el rework deberia converger a un contrato global.

### JS global y JS por pantalla

Global/canonico:

- `wwwroot/js/shared-ui.js`: namespace `window.TheBury`, formateo ARS, auto-dismiss de toasts, confirm modal, normalizacion de texto.
- `wwwroot/js/layout.js`: sidebar mobile/desktop, persistencia de collapse, dropdowns.
- `wwwroot/js/horizontal-scroll-affordance.js`: helper reusable para hint/fades de scroll horizontal.
- `wwwroot/js/notificaciones.js`: global condicionado por permisos.

Por pantalla/modulo:

- Venta: `venta-index.js`, `venta-create.js`, `venta-crear-modal.js`, `venta-module.js`, `venta-devolucion-modal.js`, `venta-facturar.js`, `details-venta.js`.
- Credito: `credito-index.js`, `credito-details.js`, `credito-module.js`, `credito-pagar-cuota.js`, `configurar-venta-credito.js`.
- Caja: `caja-index.js`, `caja-form.js`, `caja-abrir.js`, `caja-cerrar.js`, `caja-historial.js`, `caja-detalles-apertura.js`, `caja-registrar-movimiento.js`.
- Catalogo/producto: `catalogo-index.js`, `catalogo-module.js`, modales de categoria/marca/producto, historial/precio/movimientos.
- Seguridad/tickets/documentos/proveedores/ordenes/reportes: scripts dedicados por modulo.

## Patrones actuales

Botones:

- Camino canonico: `.btn-erp-primary`, `.btn-erp-secondary`, `.btn-erp-ghost`, `.btn-erp-warning`, `.btn-erp-success`, `.btn-erp-danger`, `.btn-sm`, `.btn-erp-block`, `.row-action`.
- Problema: conviven con botones inline Tailwind (`rounded-lg`, `bg-slate-*`, `bg-red-*`, `transition-all`) y variantes especificas `vm-btn-*`, `tipo-btn`, acciones de tablas locales.

Inputs/selects:

- Camino canonico parcial: `.filter-input-erp`, `.filter-select-erp`.
- Por modulo: `.venta-input`, `.venta-select`, `.vm-input`, `.vm-select`, `.credito-input`, `.caja-input`, `.catalogo-input`.
- Problema: placeholders y labels usan mucho `#475569`, `text-slate-500/600`; en baja vision puede quedar demasiado tenue.

Tablas:

- Camino canonico: `.table-erp-wrapper` + `data-oc-scroll` + `horizontal-scroll-affordance`.
- Problema: muchas tablas siguen con `overflow-x-auto` directo y min-width manual. En mobile se puede operar, pero leer/comparar sigue siendo dificil.

Cards/superficies:

- Camino canonico: `.card-erp`, `.card-erp-metric`, `.card-erp-panel`, `.card-erp-panel-padded`, `.hero-erp`, `.filter-panel-erp`.
- Problema: abundan `bg-slate-900/40`, `bg-slate-950/40`, `bg-*/5`, `bg-*/10`; el tono es consistente pero a veces demasiado translucido.

Modales:

- Hay modales compartidos y por modulo. Se repiten overlays `bg-black/50`, `bg-black/60`, `bg-slate-950/70`, `backdrop-blur-sm`, `shadow-2xl`, paneles `max-w-lg`, `max-w-5xl`, `max-w-7xl`.
- Riesgo visual: profundidad excesiva, blur innecesario, tamanos grandes en notebook, footer/actions no siempre con mismo orden responsive.

Badges:

- Camino canonico: `.badge-erp` y variantes primary/neutral/success/warning/danger/info.
- Problema: tambien hay badges inline por estado (`bg-amber-500/10`, `bg-slate-800`, `text-slate-300`) que pueden diluir semantica.

Alerts:

- Patron recurrente: cajas con `bg-red-900/20`, `bg-emerald-900/20`, bordes semanticos y texto `*-400`.
- Problema: fondos muy translucidos y texto semantico usado como unica senal en algunos casos.

Empty states:

- Existen en listados (`No se encontraron...`, `Sin moras detectadas`, iconos Material Symbols), pero son heterogeneos.
- Falta contrato global de empty state con titulo, descripcion, accion primaria opcional y relacion con filtros.

Loading states:

- Existen en modales/paneles (`ticket-modal-loading`, `devolucion-modal-loading`), pero no hay patron global. Predominan loaders puntuales, no skeletons consistentes.

Headers/filtros:

- Headers usan `max-w-*`, cards/hero, breadcrumbs pequenos, acciones a la derecha en desktop.
- Filtros mezclan grids responsive, paneles colapsables y tabs horizontales.
- Problema: los filtros pueden ocupar mucho alto en mobile y notebook; la jerarquia entre filtros, resumen y tabla no siempre es estable.

## Problemas visuales

- Bajo contraste potencial en texto terciario: `text-slate-500`, `text-slate-600`, placeholders `#475569` y labels muy chicos aparecen en muchas vistas.
- Exceso de transparencias: muchas superficies usan opacidades `/5`, `/10`, `/20`, `/30`, `/40`, `/60`, con fondos oscuros cercanos.
- Fondos demasiado oscuros o planos: `#101622` es mejor que negro puro, pero se combina con `bg-slate-950` y `bg-black/60` en modales/overlays.
- Gradientes decorativos puntuales: aparecen en `postventa`, fades de scroll y algunos fondos. Los fades de scroll son funcionales; los fondos decorativos deberian limitarse.
- Blur/backdrop: usado en modales y paneles. Para baja vision conviene reducirlo y preferir overlays solidos.
- Jerarquia visual confusa: KPIs, filtros, tabs, badges y acciones compiten en vistas densas como Venta, Catalogo, Credito, Seguridad y Dashboard.
- Botones destructivos: existen `.btn-erp-danger`, pero hay acciones destructivas inline o dentro de tablas que no siempre tienen confirmacion/jerarquia visual igual.
- `transition-all`: aparece en vistas y modales; debe evitarse en el rework por rendimiento y previsibilidad.
- Iconografia: Material Symbols es consistente, pero hay usos de icon-only que dependen de title/aria y deben revisarse pantalla por pantalla.

## Problemas responsive

Mobile:

- Sidebar tiene off-canvas y overlay, bien encaminado.
- Tablas siguen dependiendo de scroll horizontal en muchas pantallas; el affordance ayuda pero no reemplaza una vista mobile operativa.
- Acciones en tabla pueden quedar lejos del contexto o exigir scroll lateral.
- Modales grandes (`max-w-5xl`, `max-w-7xl`) requieren revision de alto, footer sticky y orden de acciones.

Tablet:

- Grids `sm/md/lg` son frecuentes, pero algunos paneles pasan de una columna a estructuras densas demasiado pronto.
- Tabs horizontales con `overflow-x-auto` necesitan indicadores mas claros.

Notebook:

- Existe auto-collapse del sidebar entre 1024 y 1600 px y escala de `html` entre 1400 y 1700 px. Es una solucion pragmatica, pero puede compactar demasiado textos ya pequenos.
- Pantallas con tablas + filtros + KPIs pueden quedar visualmente pesadas en 1366-1536 px efectivos.

Monitor chico:

- El layout base `h-screen overflow-hidden` concentra el scroll en el main. Es estable, pero puede ocultar contexto si headers/filtros son altos.
- Modales y side panels con `max-height: 90vh/92vh` necesitan footer visible y scroll interno consistente.

Pantallas anchas:

- Muchas vistas usan `max-w-7xl`, correcto para ERP. El riesgo es que algunas tablas o dashboards expandan demasiado columnas sin mejorar lectura.

## Problemas de accesibilidad y baja vision

- Texto secundario/terciario demasiado tenue para usuarios con baja vision.
- Labels de `text-xs` y `text-[10px]` en mayusculas son dificiles de leer en modulos densos.
- Estados semanticos dependen demasiado del color; se deben sumar texto, icono, borde y peso visual.
- Focus visible existe en componentes canonicos, pero no esta garantizado en todos los botones inline y controles generados por JS.
- Target tactil: los botones canonicos usan 44 px, pero row actions compactos y controles inline no siempre llegan a ese objetivo.
- Motion: hay animaciones puntuales y `transition-all`; debe respetarse `prefers-reduced-motion` en futuros cambios.
- Modales con blur y sombras fuertes pueden reducir nitidez percibida.
- Scroll horizontal tiene hint/fades, pero necesita `role`, `aria-label` y foco consistente en todas las tablas.

## Pantallas prioritarias

Criticas por densidad operativa, riesgo visual o frecuencia esperada:

1. `Views/Venta/Create_tw.cshtml` y `Views/Venta/_VentaCrearModal.cshtml`: flujo de venta, formularios densos, productos, pagos, documentacion y modales.
2. `Views/Venta/Details_tw.cshtml` y `Views/Venta/Index_tw.cshtml`: acciones postventa, devolucion/facturacion/anulacion, tablas y paneles.
3. `Views/Credito/Index_tw.cshtml`, `Details_tw.cshtml`, `ConfigurarVenta_tw.cshtml`, `PagarCuota_tw.cshtml`: informacion financiera sensible y modales/side panels.
4. `Views/Catalogo/Index_tw.cshtml`: vista muy grande con modales, formularios, autocomplete y tablas.
5. `Views/Caja/Index_tw.cshtml`, `Abrir_tw.cshtml`, `Cerrar_tw.cshtml`, `DetallesApertura_tw.cshtml`, `Historial_tw.cshtml`: operacion diaria, conteos, errores y cierre.
6. `Views/Seguridad/Index.cshtml` y partials compartidos: tablas densas, permisos, usuarios, roles, auditoria.
7. `Views/Dashboard/Index.cshtml`: primera impresion y resumen operativo.
8. `Views/Reporte/*.cshtml`: tablas y lectura analitica.
9. `Views/DocumentoCliente/*.cshtml`, `Views/Ticket/*.cshtml`, `Views/OrdenCompra/*.cshtml`, `Views/Proveedor/*.cshtml`: pantallas medianas donde se puede consolidar rapido.

## Pantallas ideales para piloto

Piloto recomendado: `Credito/Index_tw.cshtml`.

Motivos:

- Ya usa componentes canonicos (`card-erp`, `badge-erp`, `filter-input-erp`, `table-erp-wrapper`, `data-oc-scroll`).
- Tiene tabs, filtros, tablas, empty state, side panel y estados semanticos.
- Es suficientemente representativa sin ser tan grande como `Catalogo/Index_tw.cshtml` o `Venta/Create_tw.cshtml`.
- Permite probar reglas de baja vision, scroll horizontal, jerarquia de acciones y superficies solidas.

Piloto alternativo de menor riesgo: `Ticket/Index_tw.cshtml`.

Motivos:

- Listado operativo mas acotado.
- Usa modales/panel y estados.
- Menor riesgo de tocar reglas financieras o venta.

Piloto no recomendado como primer paso: `Venta/Create_tw.cshtml` o `Catalogo/Index_tw.cshtml`, por tamano, mezcla de reglas y riesgo de regresion visual/funcional.

## Reglas visuales recomendadas

Base dark:

- Mantener fondo base off-black `#101622` o evolucion muy cercana.
- Superficies solidas por defecto: `#161c28`, `#1a202c`, `#242a37`; evitar opacidades por debajo de 0.75 en paneles de lectura.
- Evitar negro puro salvo overlay controlado; preferir `rgba(8, 14, 26, 0.82-0.92)`.
- Reducir `backdrop-blur`; usarlo solo si el contenido detras no compite.

Contraste/baja vision:

- Texto principal minimo `#dde2f4` o `slate-100/200`.
- Texto secundario minimo `#c3c5d8` o `slate-300` para datos importantes.
- Reservar `#94a3b8` para ayudas no criticas; evitar `#475569` como texto visible o placeholder principal.
- Labels de formularios: minimo 12 px real, preferible 13-14 px, peso 700, sin tracking excesivo.
- Numeros y dinero: usar `tabular-nums` o monospace consistente.

Componentes:

- Consolidar botones en `.btn-erp-*`; no crear variantes inline si existe una equivalente.
- Consolidar inputs/selects en un contrato global y dejar clases de modulo como alias o wrappers.
- Usar `.badge-erp` para estados; cada estado debe combinar color + texto + borde/icono si es critico.
- Tablas: adoptar `data-oc-scroll` como minimo; para mobile critico evaluar cards/lista compacta por fila.
- Modales: definir tamanos (`sm`, `md`, `lg`, `fullscreen/operativo`), overlay solido, header/footer sticky y orden de acciones uniforme.
- Empty/loading/error: crear componentes/partials o clases compartidas con estructura estable.

Interaccion:

- No usar `transition-all`; transicionar `color`, `background-color`, `border-color`, `box-shadow`, `opacity`, `transform`.
- Respetar `prefers-reduced-motion`.
- Focus visible obligatorio en botones, links, row actions, tabs, inputs y regiones scrolleables.
- Icon-only solo con `aria-label` y tooltip/title verificable.

Responsive:

- Mobile-first: acciones principales full-width cuando haya formulario o modal.
- En tablas mobile: hint visible, region foco/teclado, fades funcionales y columnas minimas razonables.
- En notebooks: evitar filtros de alto excesivo; permitir colapsar filtros y mantener resumen/tabla visibles.

## Riesgos

- Cambios visuales globales en `shared-components.css` pueden afectar 90 archivos que ya consumen clases canonicas.
- Tokens de Tailwind (`tailwind-input.css`) recompilan utilidades; cualquier cambio debe validar vistas representativas.
- `site.css` y `dark-theme.css` son residuales/legacy; eliminarlos o ignorarlos sin revisar carga real puede romper pantallas standalone o scaffolds.
- `Create_tw_legacy.cshtml` no debe usarse como referencia canonica.
- Modulos con reglas financieras o de venta no deben redisenarse sin tests/contratos visuales y smoke funcional.
- Hay cambios no relacionados ya presentes en el working tree antes de esta auditoria; no forman parte de este entregable.

## Roadmap visual preliminar

UI-0B - Baseline visual verificable:

- Definir lista corta de pantallas representativas.
- Tomar capturas desktop/notebook/mobile con Playwright o verificacion manual.
- Asociar tests UI existentes y detectar gaps.

UI-1 - Design system operativo dark accesible:

- Documentar tokens finales de color, texto, superficie, borde, estado y foco.
- Definir contrato de botones, inputs, selects, badges, alerts, tabs, cards, tablas, modales, empty/loading/error.
- No cambiar pantallas todavia salvo spike aislado.

UI-2 - Consolidacion de componentes compartidos:

- Fortalecer `shared-components.css` y `horizontal-scroll-affordance`.
- Crear aliases o migracion incremental para inputs/selects por modulo.
- Agregar reglas globales de focus, reduced motion y texto de baja vision.

UI-3 - Pantalla piloto:

- Rework de `Credito/Index_tw.cshtml` o `Ticket/Index_tw.cshtml`.
- Validar build, tests UI aplicables, mobile/notebook/desktop y regresion funcional basica.

UI-4 - Modulos criticos:

- Credito -> Caja -> Venta Details/Index -> Seguridad -> Catalogo -> Venta Create.

UI-5 - Limpieza de deuda visual:

- Reducir utilidades inline repetidas.
- Retirar placeholders legacy solo con evidencia.
- Normalizar modales y empty/loading states.

## Que NO se debe tocar todavia

- Controllers, services, entidades, migraciones o reglas de negocio.
- Calculos financieros o logica de autorizacion/venta/credito.
- `Venta/Create_tw.cshtml` como primer piloto.
- `Catalogo/Index_tw.cshtml` como primer piloto.
- Eliminacion de archivos legacy o placeholders sin comprobar referencias.
- Redisenos masivos globales sin baseline visual y capturas.
- Cambio de fuente, tokens o color primario sin evaluar contraste y alcance.
- Snapshots/contratos UI sin confirmar comportamiento esperado.
- `git add -A` en esta rama; el commit de esta fase debe limitarse a este documento.

## Checklist actualizado

- [x] Rama creada: `kira/ui-0a-auditoria-visual-global`.
- [x] `git pull` sobre `main`: sin cambios remotos.
- [x] `git status --short` inicial revisado.
- [x] `git log --oneline -5` revisado.
- [x] `dotnet build --configuration Release` inicial correcto.
- [x] Inventario de Views/CSS/JS/tests UI ejecutado.
- [x] Busqueda de patrones visuales ejecutada.
- [x] Diagnostico de layout, CSS, JS, patrones, responsive y accesibilidad documentado.
- [ ] UI-0B: baseline visual con capturas y seleccion final de pantalla piloto.
- [ ] UI-1: design system dark accesible documentado.
- [ ] UI-3: rework piloto acotado.

## Siguiente micro-lote recomendado

Realizar UI-0B: baseline visual verificable con capturas de `Credito/Index_tw.cshtml`, `Ticket/Index_tw.cshtml`, `Venta/Index_tw.cshtml`, `Caja/Index_tw.cshtml`, `Dashboard/Index.cshtml` y `Catalogo/Index_tw.cshtml` en mobile, notebook y desktop. El objetivo seria elegir oficialmente la pantalla piloto y dejar criterios de aceptacion antes de tocar CSS o Razor.
