# UI-1 - Design system dark accesible

Fecha: 2026-05-18
Responsable: Kira
Alcance: documentacion operativa para el rework visual progresivo del ERP. No se modifico codigo productivo.

## Resumen ejecutivo

Este documento define el sistema visual base para el rework progresivo de TheBuryProject ERP. Su objetivo es consolidar el camino dark-first ya existente, mejorar accesibilidad operativa y reducir inconsistencias sin romper flujos funcionales.

UI-1 no implementa codigo. El documento ordena criterios, tokens conceptuales, componentes, reglas responsive, accesibilidad, checklists y roadmap para que UI-2 en adelante trabajen por micro-lotes seguros sobre pantallas canonicas existentes.

La base actual ya incluye `_Layout.cshtml`, Tailwind v4, `layout.css`, `shared-components.css`, `shared-ui.js`, `layout.js`, `horizontal-scroll-affordance` y componentes parciales como `btn-erp-*`, `card-erp`, `badge-erp`, `row-action`, `filter-input-erp`, `filter-select-erp` y `table-erp-wrapper`. La tarea no es reemplazar todo, sino converger esos patrones y dejar de expandir variantes inline o por modulo cuando exista un camino compartido.

## Objetivos

- Mantener un dark theme solido, sobrio y operacional.
- Aumentar legibilidad en usuarios con baja vision.
- Preservar mobile-first, notebooks y monitores chicos como escenarios reales.
- Mejorar coherencia visual entre modulos.
- Reducir ruido, transparencias, blur y decoracion sin funcion.
- Definir estados claros con color mas texto y, cuando aplique, borde o icono.
- Evitar que el polish visual cambie reglas de negocio, endpoints o contratos HTML/JS.
- Guiar UI-2 a UI-18 con criterios repetibles, validables y reversibles.

## Diagnostico base

### A. Reglas visuales ya definidas en UI-0D

- Dark theme solido, sin negro puro como superficie principal.
- Alto contraste real para baja vision.
- Menos transparencias y menos `backdrop-blur`.
- Texto secundario mas legible.
- Focus visible en botones, links, inputs, tabs, row actions y regiones scrolleables.
- Estados con color mas texto; en estados criticos tambien borde o icono.
- Tablas responsive con affordance clara.
- Modales normalizados en tamanos, overlay, header, footer, acciones y scroll interno.
- Botones con variantes primaria, secundaria, ghost, warning, success y danger.
- Evitar `transition-all`; transicionar propiedades concretas.
- Respetar `prefers-reduced-motion`.
- Evitar estetica decorativa o de landing SaaS en pantallas operativas.

### B. Problemas visuales detectados en UI-0A

- Bajo contraste potencial en `text-slate-500`, `text-slate-600`, placeholders y labels chicos.
- Exceso de superficies translucidas con opacidades bajas.
- Uso frecuente de `bg-slate-950/40`, `bg-black/50`, `backdrop-blur`, `shadow-2xl` y gradientes puntuales.
- Modales heterogeneos en ancho, overlay, footer y comportamiento mobile.
- Botones, badges, empty states, loading states y tablas responsive con criterios mixtos.
- Acciones destructivas y row actions no siempre tienen jerarquia consistente.
- Pantallas densas donde KPIs, filtros, tabs, badges y acciones compiten visualmente.

### C. Riesgos funcionales detectados en UI-0B

- La unificacion visual no implica fusion funcional.
- Venta y Caja deben seguir separadas.
- Venta y Cotizacion comparten lenguaje visual, pero no flujo unico todavia.
- Catalogo, Producto, ProductoUnidad y MovimientoStock tienen responsabilidades distintas.
- DocumentoCliente puede integrarse visualmente con Cliente, pero debe mantener cola documental.
- Credito, Seguridad, Caja, Venta, Devolucion y DocumentoCliente tienen permisos, auditoria o impacto transaccional.
- Home debe ser operativo y Reportes debe mantener analitica pesada.

### D. Riesgos tecnicos detectados en UI-0C

- Muchas pantallas dependen de IDs, `data-*`, `name`, `asp-action`, `asp-controller`, antiforgery, hidden inputs y scripts dedicados.
- Venta/Create y `_VentaCrearModal` concentran el mayor riesgo tecnico.
- Cotizacion/Detalles, Layout global, Caja/Index, Catalogo/Index, Cliente/Details, Ticket, Producto/Unidades y Caja/Cerrar requieren cautela.
- Crear vistas paralelas aumenta deuda y puede dejar tests verdes sobre una vista que ya no representa el flujo real.
- Pantallas con JS fuerte necesitan tests de contrato antes del polish visual significativo.

### E. Componentes visuales ya existentes parcialmente

- Botones: `.btn-erp-primary`, `.btn-erp-secondary`, `.btn-erp-ghost`, `.btn-erp-warning`, `.btn-erp-success`, `.btn-erp-danger`, `.btn-sm`, `.btn-erp-block`.
- Cards y paneles: `.card-erp`, `.card-erp-metric`, `.card-erp-panel`, `.card-erp-panel-padded`, `.hero-erp`, `.filter-panel-erp`.
- Tablas: `.table-erp-wrapper` y `data-oc-scroll*`.
- Badges: `.badge-erp` y variantes primary, neutral, success, warning, danger, info.
- Row actions: `.row-action`, `.row-action--primary`, `.row-action--warning`, `.row-action--danger`.
- Formularios de filtros: `.filter-input-erp`, `.filter-select-erp`.
- Texto accesible sobre dark: `.text-primary-on-dark`.
- Scripts globales: `shared-ui.js`, `layout.js`, `horizontal-scroll-affordance.js`.

### F. Componentes que necesitan normalizacion

- Inputs/selects por modulo: `venta-input`, `vm-input`, `credito-input`, `caja-input`, `catalogo-input` y equivalentes.
- Modales por modulo y compartidos.
- Alerts, toasts, empty states, loading states y skeletons.
- Tabs, filtros colapsables y headers de pantalla.
- Acciones destructivas y confirmaciones.
- Tablas mobile y transformacion a cards cuando el scroll horizontal no sea suficiente.
- Badges inline por estado.
- Cards de KPI, resumen y alerta.

### G. Pantallas que deben esperar por riesgo tecnico

- Venta/Create y `_VentaCrearModal`.
- Venta/Index, Details, Edit y Facturar.
- Caja/Index, Cerrar y DetallesApertura.
- Credito y configuracion de venta a credito.
- Cotizacion/Detalles e Index por conversion/simulador.
- Catalogo/Index por modales/AJAX.
- Cliente/Details cuando involucra documentos, BCRA o credito.
- DocumentoCliente/Upload.
- Producto/Unidades y conciliacion.
- Seguridad/Usuarios/Roles.
- Layout global completo.

### H. Pantallas candidatas para aplicar primero el sistema

- UI-2 Login, por estar aislada y permitir validar tono, inputs, errores, foco y mobile.
- UI-3 Home/Dashboard, por valor operativo y menor riesgo que Layout.
- Luego UI-4 Layout global con cautela.
- Luego UI-5 componentes base.
- Como pilotos posteriores: Clientes, Proveedores, Reportes, MovimientoStock/Kardex o listados read-only.

## Principios visuales

- Claridad antes que decoracion.
- Contraste antes que sutileza.
- Texto legible antes que estetica.
- Patrones repetibles antes que disenos unicos.
- Color mas texto para estados.
- Foco visible siempre.
- Mobile usable sin perder informacion critica.
- Dark solido, no vidrio ni transparencias excesivas.
- Jerarquia operativa: accion principal clara, acciones secundarias presentes, acciones destructivas contenidas.
- Densidad controlada: el ERP debe permitir trabajar, no parecer una landing page.
- Movimiento minimo, funcional y respetuoso de `prefers-reduced-motion`.

## Paleta recomendada

Tokens conceptuales para futura implementacion. No obligan a cambiar codigo en UI-1.

| Token | Uso | Criterio recomendado |
|---|---|---|
| `background-app` | Fondo general del layout | Off-black similar a `#101622`; evitar negro puro. |
| `background-surface` | Cards, paneles, filtros | Superficie solida similar a `#161c28`. |
| `background-surface-raised` | Modales, dropdowns, headers de panel | Similar a `#1a202c`. |
| `background-muted` | Hover, filas alternas, bloques secundarios | Similar a `#242a37` o slate oscuro solido. |
| `border-default` | Bordes generales | Similar a `#1e293b`, visible sin competir. |
| `border-strong` | Separadores fuertes, focus contextual | Similar a `#334155` o semantico. |
| `text-primary` | Texto principal | `#dde2f4` o slate-100/200. |
| `text-secondary` | Texto secundario operativo | Subir a slate-300 aproximado; no usar gris tenue para datos. |
| `text-muted` | Ayudas no criticas | Reservar slate-400; evitar slate-500/600 en informacion importante. |
| `accent-primary` | Accion principal | Azul actual `#135bec` para fondos; texto sobre dark debe usar variante clara. |
| `accent-primary-hover` | Hover principal | Azul mas profundo, sin brillo neon. |
| `accent-success` | Confirmado, aprobado, positivo | Emerald con texto claro y borde. |
| `accent-warning` | Pendiente, atencion, reversible | Amber con texto claro y borde. |
| `accent-danger` | Error, rechazo, eliminar, cancelar | Red/rose con texto claro y confirmacion. |
| `accent-info` | Informativo, sistema, ayuda | Sky/blue claro con contraste suficiente. |

Reglas:

- Evitar negro puro salvo zonas puntuales de overlay controlado.
- Evitar `slate-500` y `slate-600` para texto operativo.
- Subir contraste de textos secundarios.
- No usar transparencia como base principal de cards, tablas o formularios.
- Usar superficies solidas por defecto; reservar opacidades bajas para acentos de estado.
- No usar neones, glows fuertes ni gradientes genericos como identidad.

## Tipografia

El sistema actual usa Inter. No cambiar fuente en UI-1 ni introducir una familia nueva sin fase dedicada.

- Texto base operativo: 14px minimo recomendado, 15/16px cuando el contenido sea lectura sostenida.
- Texto principal de cards y formularios: 14px o 16px segun densidad.
- Labels: 13/14px minimo, peso 600/700, sin tracking excesivo.
- Ayudas: 13px minimo cuando explican decisiones operativas; no apagarlas con gris debil.
- Errores: 13/14px minimo, texto claro mas borde/icono o bloque semantico.
- Badges: 11/12px minimo si son cortos; no deben reemplazar texto explicativo cuando el estado sea critico.
- Tablas: 13/14px minimo; numeros con `tabular-nums` o monospace consistente.
- Titulos de pantalla: sobrios, 20/24px usualmente suficiente.
- Titulos de seccion: 16/18px, con jerarquia por peso y separacion, no solo tamano.
- No bajar de 14px en textos operativos.
- Evitar labels de 10px en mayusculas para informacion importante.

## Espaciado y layout

- Padding minimo de panel: 16px en mobile, 20/24px en tablet y desktop.
- Separacion entre secciones: 16/24px segun densidad; no saturar pantallas con cards pegadas.
- Grids responsive: una columna en mobile, dos columnas solo cuando el contenido pueda leerse sin compresion.
- Ancho maximo: mantener `max-w-7xl` o equivalente para pantallas operativas; no expandir texto indefinidamente en wide.
- Cards: usarlas para agrupar unidades funcionales, no para envolver todo dentro de todo.
- Tablas: permitir densidad operativa, pero con filas legibles y acciones cercanas al contexto.
- Notebook: reducir altura de filtros, permitir collapse y mantener tabla/resumen visible.
- Mobile: priorizar accion principal, filtros colapsables, scroll indicado y botones tactiles.

## Botones

### Primary

Uso: accion principal de la pantalla o modal: guardar, crear, confirmar, aplicar.

No usar para acciones secundarias repetidas ni para acciones destructivas. Debe tener contraste alto, estado hover visible, focus claro, disabled reconocible y loading con texto.

### Secondary

Uso: accion relevante no principal: volver, limpiar filtros, abrir detalle secundario.

No debe competir con primary. Mantener borde visible y texto claro.

### Ghost

Uso: acciones contextuales de baja jerarquia, cancelar modal no destructivo, abrir opciones.

No usar para operaciones criticas que requieran atencion.

### Danger

Uso: eliminar, anular, rechazar, cancelar con impacto.

Debe tener texto explicito, color mas borde, confirmacion cuando corresponda y no depender solo del rojo. En mobile debe ser facil no tocarlo por error.

### Warning

Uso: pendiente, configurar, accion reversible o que requiere atencion.

No usar como estilo decorativo. Debe explicar el motivo del warning.

### Success

Uso: aprobar, recepcionar, confirmar positivo.

No usar para accion primaria generica si el resultado no significa exito operacional.

### Icon-only

Uso: barras compactas o acciones repetidas donde el contexto sea obvio.

Requisitos: `aria-label` o `title`, focus visible, target minimo 40px y preferible 44px, iconografia Material Symbols consistente.

### Row-action

Uso: acciones por fila en tablas/listados.

Requisitos: en mobile mostrar icono mas texto; en desktop puede compactarse. Las acciones destructivas deben diferenciarse por texto, color, title/label y confirmacion si hay impacto.

Estados obligatorios para todas las variantes:

- Hover: cambio de color/fondo/borde sin shift de layout.
- Focus: anillo visible de 2px o equivalente.
- Disabled: contraste suficiente para reconocer estado, sin parecer accion disponible.
- Loading: conservar ancho estable cuando sea posible y mostrar texto como "Guardando..." o "Procesando...".
- Mobile: altura minima 44px para acciones principales.

## Inputs, selects y formularios

- Input base: superficie solida, borde visible, texto `text-primary`, placeholder legible pero no dominante.
- Select base: mismas reglas del input, opciones legibles en dark theme.
- Checkbox/radio: target amplio, label visible y estado checked claro.
- Label: siempre visible, arriba del control en formularios operativos.
- No usar placeholder como label.
- Help text: util, corto y con contraste suficiente.
- Error text: debajo del control, color semantico mas texto claro; asociar visualmente al campo.
- Validacion: mensajes especificos, no genericos, sin depender solo del borde rojo.
- Disabled: explicar cuando el motivo no sea evidente.
- Readonly: diferenciar de disabled; debe ser legible.
- Focus visible: borde/acento y ring suave.
- Grupos de campos: separar por significado, no solo por grilla.
- Formularios largos: dividir en secciones, mantener accion principal visible al final y evitar scroll confuso.
- Formularios en modal: header claro, contenido con scroll interno, footer de acciones estable.

## Tablas

### Desktop

- Usar tabla cuando haya comparacion, orden, totales o lectura por columnas.
- Header con contraste alto y labels claros.
- Filas con altura suficiente y separacion sutil.
- Numeros alineados a la derecha y con `tabular-nums`.
- Acciones por fila cerca del dato que afectan.

### Mobile

- Evitar tablas imposibles de operar.
- Usar `data-oc-scroll*` cuando el scroll horizontal sea necesario.
- Agregar hint/fade y region focusable cuando aplique.
- Considerar transformacion a cards/lista compacta si el usuario necesita actuar por fila.
- Mostrar acciones con texto, no solo iconos.

### Reglas

- Scroll horizontal controlado, no accidental.
- Sticky header solo si la tabla es larga y no tapa contenido critico.
- Badges de estado con texto claro.
- Totales con jerarquia y separacion.
- Empty state dentro de la tabla o panel, con titulo, descripcion y accion opcional.
- Densidad permitida: compacta, pero nunca a costa de 13/14px minimo en texto operativo.
- Transformar en cards cuando hay muchas columnas no comparables o acciones por item dominan la tarea.

## Cards y paneles

- Card base: superficie solida, borde visible, radio moderado y padding suficiente.
- Card elevada: solo cuando hay capa superior como modal, dropdown o panel contextual.
- Card de alerta: color semantico suave mas borde y texto claro.
- Card de KPI: dato principal, etiqueta legible, contexto o periodo; evitar grilla ruidosa si no hay datos utiles.
- Card de resumen: agrupa lectura operativa y links relacionados.
- Card clickeable: debe tener cursor, hover/focus visible y target claro.
- Evitar glassmorphism fuerte, blur, fondos muy translucidos y sombras pesadas.
- No poner cards dentro de cards salvo estructura funcional inevitable.

## Modales

- Tamanos conceptuales: `sm` confirmacion, `md` formulario simple, `lg` formulario o tabla, `xl/operativo` solo con necesidad real.
- Altura maxima: no exceder viewport; contenido largo con scroll interno.
- Header fijo cuando el contenido sea largo.
- Footer fijo si las acciones pueden quedar fuera de vista.
- Orden de acciones: secundaria/cancelar y primaria claras; danger separada cuando corresponda.
- Mobile: modal casi full-width, padding contenido, acciones full-width o apiladas.
- Cierre: boton visible, Escape/backdrop solo si no hay perdida de datos o se confirma.
- Foco: mover foco al modal al abrir y devolverlo al trigger al cerrar cuando se implemente.
- Contenido largo: dividir por secciones; no esconder errores debajo del fold.
- Formularios en modal: preservar `form`, `name`, antiforgery, IDs y ownership.

## Badges y estados

Estados base:

- `success`: aprobado, confirmado, completado, disponible.
- `warning`: pendiente, requiere atencion, diferencia, revision.
- `danger`: rechazado, error, vencido, bloqueado, anulacion.
- `info`: informativo, nuevo, en proceso.
- `neutral`: historico, sin cambios, referencia.
- `pending`: esperando accion o verificacion.
- `cancelled`: cancelado/anulado, con texto explicito.
- `disabled`: no disponible, inactivo o sin permiso.

Regla obligatoria: cada estado debe tener texto claro. No depender solo del color. Evitar colores de bajo contraste y badges tan tenues que parezcan decorativos.

## Alerts y mensajes

- Success: confirmar resultado y siguiente accion si aplica.
- Error: explicar que fallo y que puede hacer el usuario.
- Warning: indicar riesgo o dato pendiente.
- Info: aportar contexto sin bloquear.
- Empty: titulo, descripcion y accion primaria opcional.
- Loading: texto breve que indique que se esta cargando/procesando.
- Skeleton: usar cuando el layout final es conocido y la espera afecta lectura.
- Validacion: cerca del campo y tambien resumen arriba si hay multiples errores.
- Confirmacion: explicita para acciones destructivas o irreversibles.

## Navegacion y layout global

Criterios para UI-4, sin tocar implementacion en UI-1:

- Sidebar: conservar permisos visibles y estados activos claros.
- Header: reducir ruido y mantener informacion util.
- Breadcrumbs: usar si ayudan a ubicacion en flujos profundos.
- Menu mobile: off-canvas claro, overlay solido, foco y cierre predecible.
- Colapso: preservar `sidebar`, `sidebarOverlay`, `toggleSidebar`, `collapseSidebar` y almacenamiento actual.
- Foco: todos los triggers de nav deben ser alcanzables por teclado.
- Acciones principales: no esconderlas en menus ambiguos.
- Permisos visibles: no mostrar acciones no permitidas salvo que sea readonly explicado.
- No redisenar Layout completo sin plan y tests de contrato.

## Home/Dashboard

Criterios para UI-3:

- Menos enfasis en "lo ganado" como protagonista.
- Evitar ruido si hay pocas ventas o datos incompletos.
- Priorizar caja abierta, ventas pendientes, documentos pendientes, stock critico, creditos a revisar, tickets o alertas importantes.
- Incluir accesos rapidos a tareas frecuentes.
- Incluir notas rapidas si aportan operacion diaria.
- KPIs utiles, con periodo y significado claro.
- Separar analitica pesada hacia Reportes.
- Mantener Home como entrada operativa, no como tablero decorativo.

## Login

Criterios para UI-2:

- Pantalla aislada, sobria y de alto contraste.
- Inputs claros, labels visibles, errores legibles y foco evidente.
- Mobile usable sin zoom ni controles pequenos.
- Estetica alineada al ERP: profesional, dark solido, sin exceso decorativo.
- Preservar validacion, `name`, antiforgery y flujo de autenticacion.

## Clientes y Proveedores

Criterios para fases tempranas:

- Tablas legibles y filtros claros.
- Formularios ordenados por datos principales, contacto, estado y relaciones.
- Acciones por fila consistentes.
- Relacion visual clara con documentos, creditos, ordenes y operaciones asociadas.
- No fusionar Cliente con DocumentoCliente ni Proveedor con OrdenCompra sin analisis funcional.
- En Cliente, cuidar documentos, BCRA, creditos y permisos.

## Modulos transaccionales de alto riesgo

Deben esperar o hacerse con tests/contratos previos:

- Venta.
- Caja.
- Credito.
- Cotizacion.
- DocumentoCliente.
- Seguridad.
- Producto/Unidades con conciliacion.
- Catalogo con modales/AJAX.

## Accesibilidad y baja vision

- Contraste fuerte en texto, bordes funcionales y controles.
- Foco visible siempre.
- Estados con texto, no solo color.
- No usar placeholder como label.
- No usar texto demasiado chico.
- No usar gris apagado para informacion importante.
- Targets clickeables comodos: 44px ideal, 40px minimo en row actions compactos.
- Navegacion por teclado preservada.
- Mobile usable con una mano cuando sea razonable.
- Respetar `prefers-reduced-motion`.
- Icon-only siempre con nombre accesible.
- Formularios con labels asociados y mensajes de error claros.
- Scroll horizontal con affordance, foco y etiqueta cuando aplique.

## Responsive

Breakpoints conceptuales:

- Mobile: menor a 640px.
- Tablet: 640px a 1023px.
- Notebook: 1024px a 1439px, con especial cuidado por 1366px y escalado.
- Desktop: 1440px a 1919px.
- Wide: 1920px o mas.

Reglas:

- Evitar tablas imposibles en mobile.
- Scroll horizontal solo si esta indicado y es operable.
- Filtros colapsables en mobile/notebook cuando ocupan demasiado alto.
- Cards para datos resumidos, no para reemplazar tablas comparativas sin criterio.
- Modales full-width o casi full-width en mobile.
- Botones con tamano tactil correcto.
- Sidebar colapsado/overlay debe preservar lectura y foco.
- No depender de hover para acciones esenciales.

## Reglas de implementacion futura

- No tocar logica de negocio para polish visual.
- Preservar IDs, `data-*`, `name`, `asp-action`, `asp-controller`, `method`, antiforgery y hidden inputs usados por JS o model binding.
- No cambiar endpoints.
- No crear vistas paralelas sin justificacion explicita.
- Agregar tests de contrato antes de tocar pantallas con JS fuerte.
- Trabajar por micro-lotes.
- Validar build y tests por fase.
- Usar evidencia visual cuando aplique: capturas desktop, notebook y mobile.
- No expandir componentes legacy o inciertos.
- Revisar controller, ViewModel, JS y tests antes de cambiar estructura HTML.
- Mantener `data-oc-scroll*` en tablas anchas.
- Mantener modales en la relacion DOM esperada por scripts.

## Checklist de aceptacion visual

Para cada pantalla:

- [ ] Contraste correcto en texto principal, secundario y estados.
- [ ] Texto legible, sin labels operativos demasiado chicos.
- [ ] Mobile revisado.
- [ ] Acciones principales y secundarias claras.
- [ ] Estados visibles con color mas texto.
- [ ] Errores visibles y cerca del contexto.
- [ ] Focus visible en controles interactivos.
- [ ] Tabla usable o alternativa mobile justificada.
- [ ] Modal usable en mobile/notebook si aplica.
- [ ] Sin transparencias excesivas.
- [ ] Sin clases duplicadas innecesarias cuando exista componente canonico.
- [ ] Sin romper tests ni contratos HTML/JS.

## Checklist tecnico por pantalla

Antes de implementar:

- [ ] Revisar controller.
- [ ] Revisar ViewModel.
- [ ] Revisar JS asociado.
- [ ] Revisar IDs usados por JS.
- [ ] Revisar `data-*`.
- [ ] Revisar `name` y model binding.
- [ ] Revisar tests existentes.
- [ ] Revisar permisos.
- [ ] Revisar formularios POST y antiforgery.
- [ ] Revisar endpoints/API consumidos.
- [ ] Validar build.
- [ ] Validar tests relevantes.

## Roadmap de aplicacion

Orden confirmado:

1. UI-2 Login.
2. UI-3 Home/Dashboard.
3. UI-4 Layout global.
4. UI-5 componentes base.
5. UI-6 Clientes.
6. UI-7 Proveedores.
7. UI-8 Catalogo/Producto/Inventario.
8. UI-9 MovimientoStock/Kardex.
9. UI-10 Caja.
10. UI-11 Cotizacion.
11. UI-12 Venta.
12. UI-13 Credito.
13. UI-14 DocumentoCliente.
14. UI-15 Devolucion/Garantia/RMA.
15. UI-16 OrdenCompra.
16. UI-17 Reportes.
17. UI-18 Seguridad/Usuarios/Roles.

## Que NO hacer

- No redisenar todo de golpe.
- No empezar por Venta.
- No tocar Layout completo sin plan.
- No fusionar flujos transaccionales todavia.
- No usar glassmorphism fuerte.
- No usar texto gris debil.
- No meter CSS nuevo gigante sin migracion controlada.
- No cambiar logica por estetica.
- No introducir frameworks nuevos.
- No tocar controllers, services, entidades, migraciones ni tests productivos en fases visuales.
- No eliminar legacy o placeholders sin evidencia de referencias, rutas, DI, vistas, scripts y tests.

## Deuda y riesgos remanentes

- Convivencia de clases compartidas, clases por modulo y Tailwind inline.
- `text-slate-500/600` y placeholders tenues siguen presentes en muchas vistas.
- Modales aun no tienen contrato global completo.
- Empty/loading/error states necesitan normalizacion.
- Layout global sigue siendo transversal y riesgoso.
- Pantallas con JS fuerte requieren tests de contrato antes de polish relevante.
- `site.css`, `dark-theme.css` y vistas legacy/paralelas no deben tocarse sin evidencia.
- La consolidacion de `shared-components.css` puede afectar muchas vistas; debe hacerse con validacion representativa.

## Cierre UI-1

- [x] Documento UI-1 creado.
- [x] No se toco codigo productivo.
- [x] No se tocaron vistas.
- [x] No se toco CSS/JS.
- [x] Tokens visuales definidos.
- [x] Componentes base definidos.
- [x] Reglas de accesibilidad definidas.
- [x] Reglas responsive definidas.
- [x] Roadmap confirmado.

## Proximo paso recomendado

Ejecutar UI-2 Login como micro-lote aislado. Validar tono visual, inputs, labels, errores, foco, mobile y baja vision preservando autenticacion, validacion, `name`, antiforgery y flujo existente.
