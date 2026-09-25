# UI Refactor Status

Mapa rápido de qué pantallas ya siguen [ERP-UI-STANDARD.md](./ERP-UI-STANDARD.md) y cuáles
faltan. Actualizar esta tabla al cerrar cada pantalla.

| Área | Pantalla | Estado | Referencia |
|---|---|---|---|
| Global | `_Layout` / Foundation | ✅ Cerrado | `ERP-UI-STANDARD.md` |
| Venta | `Index` | ✅ Cerrado | `79b4c91`, `e1de859`, `3225b95` + ENVIO-ML + VENTA-INDEX-CLIENTE-PARIDAD-01 + VENTA-INDEX-SIN-HERO-01 (ver resumen abajo) |
| Venta | `Create` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad + ENVIO-ML (ver resumen abajo) |
| Venta | `Edit` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad + ENVIO-ML (ver resumen abajo) |
| Venta | `Details` | ✅ Cerrado | serie VENTA-DETAILS (ver resumen abajo) + ENVIO-ML (ver resumen abajo) |
| Cotización | `Simular` (`_CotizadorForm.cshtml`) | ✅ Cerrado | serie COTIZACION-SIMULAR-REDESIGN (ver resumen abajo) + ENVIO-ML (ver resumen abajo) |
| ConfiguracionPago | `MediosPago` + `CreditoPersonal` | ✅ Unificado en un solo módulo con pestañas (2026-09-25) | CONFIGPAGO-UNIFICACION-01 (ver resumen abajo) |
| Dashboard | `Index` | ◐ Auditoría 4 capas aplicada (2026-09-24) | estado del día en lugar de hero, tabs con contador, permisos en accesos/acciones, paneles planos, composición por ancho de columna; filas reales de cuotas verificadas en el saneamiento pre-PR; validado con roles restringidos (2026-09-24, cierre de módulo); datos financieros gateados por permiso de módulo |
| Cliente | `Index` | ✅ Cerrado | CLIENTE-INDEX-SIN-HERO-01 (ver resumen abajo) |
| Cliente | `Nuevo cliente` (drawer Create) | ✅ Cerrado | serie wizard Cliente (ver resumen abajo) |
| Cliente | `Details` | ✅ Cerrado | serie CLIENTE-DETAILS-TABS (ver resumen abajo) |
| Catálogo | `Inventario` (tab Productos) | ✅ Cerrado | ver resumen abajo |
| Catálogo | `Inventario` (tab Categorías + modales Nueva/Editar + eliminar) | ✅ Cerrado (2026-09-25) | CATEGORIA-CIERRE-01 (ver resumen abajo) |
| Proveedor | `Index`, `Details`, drawers Nuevo/Editar, eliminar | ✅ Cerrado (2026-09-25) | cierre de módulo `/ui-module proveedores` (ver resumen abajo) |
| Caja | `Index`, `Create`/`Edit` (página + panel lateral), `Abrir`, `RegistrarMovimiento`, `Cerrar`, `DetallesApertura`, `DetallesCierre`, `Historial` | ✅ Cerrado (2026-09-25) | cierre de módulo `/ui-module caja` (ver resumen abajo) |

Leyenda:

- ✅ Cerrado — auditado y validado contra el estándar completo.
- ◐ Foundation aplicada / refactor pendiente — solo recibió fixes estructurales globales
  (ej. scoping de `.hidden`, `main` único), sin auditoría ni refactor completo de la pantalla.
- ⏳ Pendiente — sin trabajo de este estándar todavía.

## Venta / Index — cerrado

Resumen no cronológico de lo que quedó implementado:

- responsive desktop/mobile con lógica funcional paritaria (tabla + cards);
- tabs accesibles con teclado (roving tabindex, Arrow/Home/End) y scroll affordance;
- paginación server-rendered, filtros preservados en la URL;
- acciones normalizadas y compartidas entre tabla y cards vía helper único;
- barra sticky mobile sin CTAs duplicados;
- cleanup de CSS/JS muerto;
- toast con una sola autoridad de inicialización;
- validación integrada de responsive y accesibilidad;
- cierre reproducible desde Git (ver commits en la tabla arriba).

- reapertura VENTA-INDEX-CLIENTE-PARIDAD-01, a pedido explícito del usuario de
  converger la composición visual con `Cliente/Index` (referencia interna, no
  rediseño inventado). Causa raíz confirmada en código antes de tocar nada:
  `#venta-index-rework.venta-index-shell` tenía `max-width:1440px; margin-inline:auto`
  propio — `.shell.cliente-index` (`cliente-module.css`) ya había convergido a ancho
  fluido sin cap (el gutter real lo aporta `_Layout.cshtml`) en la serie
  CLIENTE-INDEX-REDESIGN, dejando ~130px de margen muerto por lado en Venta/Index a
  1920px que Cliente/Index no tiene. Fix: `.venta-index-shell` pasa a fluido con el
  mismo criterio (`max-width:none`); el id `#venta-index-rework` es exclusivo de esta
  vista (no lo comparten Create/Edit/Details de Venta), así que no requirió scope
  adicional. Efecto colateral corregido en el mismo lote: al liberar el shell, el
  campo único de la fila base de filtros ("Buscar venta") se estiraba a >1400px de
  ancho — se acota su pista de grid a un máximo útil (28rem), mismo criterio de "no
  convertir el filtro en una card gigante" que ya sigue Cliente/Index. Acciones por
  fila de la tabla desktop (Ver/Devolver/Anular) convergen de botones llenos
  (`btn btn-xs btn-soft/btn-amber/btn-danger`) al componente compartido `.row-action`
  (`shared-components.css`, mismo patrón ya usado por `Cliente/Index`) — ícono-only
  (la tabla desktop de Venta sólo se muestra ≥768px, igual que la de Cliente ≥900px),
  bajando el peso visual de 3 controles y sumando `aria-label`/`title` reales que
  antes no tenían (dependían del texto visible). Tabs, hero, filtros avanzados,
  paginación y las cards mobile no se tocaron — ya usan un patrón de tabs con ARIA
  completo equivalente al resto del ERP y Cliente/Index no tiene tabs con qué
  compararlos; las cards mobile mantienen sus botones locales, mismo criterio ya
  documentado en Cliente/Index (`.row-action` pasa a ícono-only desde 640px, antes
  del breakpoint donde se muestran las cards). Sin cambios de reglas de negocio,
  permisos, contratos backend, paginación funcional, ids ni `data-*` (`data-open-devolucion-modal`/
  `data-venta-id` intactos). Validado en vivo con Playwright (instancia propia en
  :5199, sin tocar la instancia :18787 del usuario) comparando contra `Cliente/Index`
  en 1920×1080, 1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800: mismo
  ancho útil y mismo borde derecho de contenido que Cliente/Index a ≥1440px, sin
  overflow horizontal en ningún viewport, 0 errores de consola; modal de devolución
  verificado end-to-end contra una venta real desde el nuevo botón ícono; build 0/0;
  105/105 tests focalizados de Venta (`VentaController*`, `VentaApiController*`,
  `VentaDetailsUiContractTests`, `VentaEnvio*`) verdes.

- reapertura VENTA-INDEX-SIN-HERO-01, a pedido explícito del usuario tras comparar
  capturas reales de Inventario/Cliente/Ventas y preferir la composición de Catálogo
  (sin card de hero propio, acciones integradas a la barra de tabs). Convergencia
  literal con `Catálogo/Index_tw.cshtml` (ver también CLIENTE-INDEX-SIN-HERO-01, misma
  sesión): se retira la `<section class="venta-hero card">` (eyebrow "Centro operativo",
  breadcrumb "Ventas › Centro de ventas", `<h1>Centro de Ventas</h1>`) — el título ya lo
  muestra la barra superior global, y el breadcrumb no aportaba navegación real (un solo
  nivel, sin jerarquía). El único botón que vivía en el hero ("Medios de pago") se mueve
  a la barra de tabs, junto a "Ver cotizaciones"/"Nueva Venta", pero **fuera** del `<div
  class="venta-tabs__actions">` que ya se ocultaba a <768px por duplicar la sticky mobile
  bar — "Medios de pago" no tiene equivalente en esa barra (único punto de entrada real a
  `ConfiguracionPago/MediosPago` desde Ventas, verificado por grep) y hubiera quedado sin
  acceso en mobile si se agrupaba con esos dos. CSS muerto retirado de
  `ventas-index.css` (`.venta-hero`, `::before`, `__content`, `__eyebrow`, `.dot`/
  `.dot-green`, `.venta-breadcrumb*` y sus 2 variantes en media queries) — `id="venta-
  index-rework"` es exclusivo de esta vista, confirmado sin otros consumidores. Sin
  cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend; ERP-UI-
  STANDARD.md §4 actualizado para documentar este patrón (sin hero, acciones integradas
  al card de contenido) como canónico, con `Catálogo/Index_tw.cshtml` como referencia.
  Validado en vivo con Playwright (instancia del usuario en :18787, solo lectura, sin
  tocarla) en 1920×1080, 1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800: sin
  overflow horizontal en ningún viewport, 0 errores de consola; tab switching y "Medios de
  pago" (visible y navegable en los 7 viewports, incluido mobile real) verificados
  funcionalmente; build 0/0; 501/501 tests focalizados (`Cliente*` + `VentaEnvioUiContractTests`)
  verdes.
- reapertura VENTA-INDEX-MOBILE-P0 (<1024px, hallazgo de la auditoría mobile): con la
  barra de tabs en `flex-direction: column` (≤1023px), el `flex: 1 1 44rem` de
  `.tabs-scroll-shell` — un ancho base pensado para la fila — pasaba a ser la **altura**:
  ~700px de bloque vacío entre las tabs y las acciones a 390 y 768, empujando filtros y
  listado una pantalla hacia abajo. Además `flex-wrap: wrap` volvía la columna multi-línea
  y el shell medía 613px dentro de una tarjeta de 358, así que "Cotizaciones/Devoluciones/
  Envíos" quedaban recortadas por `.tabs-card{overflow:hidden}` sin poder deslizarse. Fix
  aditivo dentro del `@media (max-width: 1023px)` existente (`flex: 0 0 auto` +
  `flex-wrap: nowrap`), sin tocar las reglas base. Scroll total 1790→1122px (390) y
  1407→960px (768); el shell pasa a 329px y las tabs se deslizan; ≥1024px idéntico (capturas
  1440/1280/1024 iguales byte a byte); 70/70 tests focalizados verdes.
- reapertura `/ui-module ventas` (2026-09-24): la tabla desktop de Venta/Index cortaba la
  columna "Acciones" a 1440px y 1280px (con el aviso "Deslizá la tabla" visible en desktop).
  Causa raíz: `--oc-scroll-min-width: 72rem` (1152px) superaba el ancho útil (1077px a
  1440) y, a 1280, el contenido (961px) el de 909px. Fix sólo CSS en `ventas-index.css`:
  min-width 72→52rem, padding lateral/letter-spacing/gap de acciones reducidos <1440px y
  número de venta en una sola línea. Medido en vivo: 1440 y 1280 sin scroll ni aviso;
  768/1024 conservan el scroll con aviso (comportamiento previsto). Details
  (VENTA-DETAILS-DUPLICACION-04, a pedido explícito): se retiran los chips de forma de
  pago/fecha del hero y el campo "Cliente" de Resumen (ambos ya visibles en la misma
  pantalla); Resumen pasa a 3 columnas y Acciones/Total suben ~60px. 74/74 tests
  focalizados (VentaDetails*, VentaEnvioUi*) verdes; 1440/390 sin overflow. Fuera de
  alcance: título duplicado topbar+h1 (patrón global); `.venta-info-pill` en
  `venta-module.css` quedó sin uso.
- `/ui-module ventas`, recorrido completo del módulo (2026-09-24; 360 cargas = 12 pantallas
  × 10 ventas × 5 viewports, 0 errores HTTP/consola/overflow; gates de estado por
  redirección verificados). Correcciones: (1) Facturar: `z-80` no existe en el Tailwind
  precompilado, el botón flotante de tickets tapaba el extremo de "Emitir factura" →
  `.venta-aux-page[data-facturar-panel]{z-index:80}`; (2) `border-sky-500/25` tampoco
  existe (borde blanco en "Resumen comercial") → `/20` en Facturar_tw y
  `_FacturaCamposEmision`; (3) cotizador: el acento pasó a menta con texto blanco
  (~1.5:1) y el hover del acento a violeta por la migración de paleta → texto
  `--pal-neutral-950` y hover `--pal-info-500`; (4) terminología: "Anular venta" (Index)
  → "Cancelar venta", como su pantalla destino; (5) fecha "00:00" ya no se muestra
  cuando la venta no tiene hora (Details/Cancelar/Delete/Autorizar/Rechazar);
  (6) pestañas secundarias de Index avisan "Hay filtros activos…" + Limpiar cuando el
  filtro deja vacía la lista; (7) CTA del sidebar del wizard usa flecha salvo en el paso
  que confirma; (8) tilde en "Configuración global activa cargada". 1494/1494 tests de
  `Venta*`/`Cotizacion*` verdes. Sin cubrir: Autorizar/Rechazar renderizados (no hay ventas
  pendientes de autorización en la base), Edit de ventas con crédito.
- `/ui-module ventas`, ronda con QA sobre copia de la base (backup/restore a `_qa`, instancia :5199,
  ya eliminadas; la base real no se tocó): ventas reales creadas, confirmadas, facturadas, con
  devolución, canceladas y a crédito personal con excepción documental. Correcciones: (1) el botón
  de Create decía "Confirmar Transacción" y "se generará la factura" pero sólo crea la venta
  (queda en Presupuesto) → "Crear venta" + nota real (contrato de paridad actualizado); (2) tras
  aplicar la excepción documental el badge seguía "NO VIABLE" → "VIABLE CON EXCEPCIÓN" (se
  restaura al retirarla); (3) "ReciboSueldo" crudo → "Recibo de sueldo" (sólo presentación);
  (4) modal de Devolución: Motivo truncado y "Reflejar en caja" comprimido a ≥1280px, y la
  selección de producto quedaba bajo el pliegue sin indicar (ahora scroll + marca al fallar);
  (5) CTA duplicado del header vs sidebar a ≥1280px → sólo el del sidebar (Crédito conserva el
  del header); (6) "Ver cotizaciones" → "Cotizaciones guardadas", "Añadir" → "Agregar",
  "Elegibilidad" → "Viabilidad del crédito"; (7) Details: badge "Autorización: …", copy de
  Facturación para canceladas/ya facturadas, Autorizar/Rechazar apilados; (8) texto <12px de las
  vistas de Venta llevado a `--erp-fs-min` con una regla; (9) z-index global `.z-80`. Crítica
  Impeccable (dual-agent, snapshot en `.impeccable/critique/`): 25/40, detector 0 hallazgos.
  Suite completa 4996/4996 (4 omitidos: seeders E2E). Sin resolver: cotizador con textos de
  10–11.5px propios; Details de Cotización sin "Confirmar" es decisión previa del usuario;
  aplicar la excepción crea un borrador de venta (CreateAjax) que queda huérfano si se abandona.
- `/ui-module ventas`, cierre del módulo (2026-09-25; instancias propias :5290 sobre la base real y :5199 sobre
  una copia `_qa`, ya eliminadas). Recorrido: Index, Create (Cotizar → "Continuar con wizard"), Edit (5 pasos y
  paso Crédito con excepción documental), Details, Facturar (emisión real), Devolución (creada), Cancelar
  (ejecutada), Autorizar, Rechazar, Cotización y Listado; 5 viewports, sin overflow ni errores HTTP/consola.
  Fases formales ejecutadas: `ux-heuristics`, Impeccable critique dual-agent (26/40, detector 0 hallazgos),
  polish y QA final. Correcciones: (1) P1 flujo: con un turno de caja abierto de un día anterior el aviso
  decía "Sin caja abierta" y "Abrir caja" llevaba a un formulario sin cajas (callejón sin salida) — Index y
  Details distinguen "Turno de caja vencido" (caja y fecha) y su CTA lleva a Cajas; Details ya no dice "Sin
  acciones disponibles" cuando el bloqueo es la caja; (2) el texto de ayuda bajo el CTA del wizard sólo se
  muestra en el paso que confirma; (3) "Continuar con wizard" sin cliente avisa antes de guardar (ya no deja
  una cotización escrita); (4) cotizador: textos de 10–11.5px llevados a 12px, con cabeceras/etiquetas acortadas
  ("Subtotal", "Cuotas", "Cuota") donde el tamaño nuevo las truncaba; (5) Listado de Cotización: labels
  asociados a sus campos (a11y), tildes en título/cabeceras; (6) tabs de Index en <1024px con degradé que indica
  que se deslizan; (7) "1 días" → "1 día" (modal de devolución y mora del cotizador); (8) "Próximo paso…" en
  Details de una Cotización → "Esperando confirmación". 2034/2034 tests `Venta*`/`Cotizacion*`/`Devolucion*`/`Ui*`
  (2 nuevos). Cabeceras alineadas a ERP-UI-STANDARD §4 (sin hero, título en la barra global): Details
  pasa a "Detalle de venta" en la barra y la miga ("Ventas / Detalle"), así el número de venta aparece una
  sola vez (H1 con su estado); el Listado de Cotización pierde su hero (la descripción y "Nueva cotización"
  pasan a la cabecera del card de filtros, H1 sólo para lectores de pantalla); el cotizador standalone deja
  de repetir "Cotización" bajo la barra global (embebido en Venta/Create sigue como título del paso).
  En la copia de la base también se ejecutaron anulación de factura y Delete de venta (desktop y mobile).
  Segunda crítica Impeccable dual-agent sobre las cabeceras (26/40, detector 0 hallazgos; QA en 1440/1280/768/390
  sin overflow ni errores). Corregido: "Ventas" queda activo en el sidebar en Cotización y su Listado; h1 sólo
  para lectores de pantalla en Venta/Index y Cotización; selects/buscador del panel de tickets con aria-label;
  copy del Listado. Decisiones/deuda: el acento menta del cotizador es la decisión de COTIZACION-MOCKUP (no se
  unifica con el lima de Ventas); filtros del Listado apilados en mobile y "Cotizaciones" con tres accesos/nombres
  quedan como deuda de diseño.

## Venta / Create + Edit — cerrados conjuntamente

Resumen no cronológico de lo que quedó implementado:

- vistas finas (`Create_tw.cshtml`, `Edit_tw.cshtml`) sin lógica propia relevante;
- `_VentaWizardForm.cshtml` como núcleo único compartido entre ambos modos;
- misma estructura visual entre Crear y Editar;
- mismo JS y CSS funcional entre ambos modos;
- diferencias visuales existentes justificadas por diferencias funcionales reales por modo;
- paridad Crear/Editar protegida por tests;
- responsive validado;
- accesibilidad compartida vía el mismo parcial;
- configurador de crédito embebido (`_ConfigurarVentaEmbebida.cshtml`) usa container
  queries propias (`credito-module.css`, scopeadas a `[data-credito-config-embedded]`)
  en vez de breakpoints de Tailwind atados al viewport, porque el ancho real del
  contenedor no es monótono con el ancho de pantalla dentro del wizard; validación
  integral final sin regresiones en el rango completo de viewports soportados;
- reapertura CREDITO-VISUAL del paso Crédito cerrada (padding monetario, señales/motivos
  no duplicados, densidad/detalle por cuota colapsable, excepción documental contextual
  junto al bloqueante, labels asociados a su input real) sin cambios de cálculo ni de
  reglas de negocio;
- reapertura VENTA-CREDITO-ARQUITECTURA-VISUAL-01 del paso Crédito — micro-lote 1 de 2:
  banner de verificación automática se oculta una vez hay resultado; Resultado SCORE
  compactado (Cupo Disponible + barra siempre visibles, Límite/Utilizado a detalle
  expandible); excepción documental aplicada colapsa a un teaser de una línea ("Ver
  motivo ▸") en vez del formulario completo con textarea permanente; "Cantidad de
  cuotas" y "Valores del crédito" unificados en una única sección "Configurar plan";
  mini-resumen vivo reducido a cuota/total financiado/cantidad/primer vencimiento, con
  el resto del detalle financiero colapsado; evaluación preliminar (semáforo) compacta
  a badge siempre visible + detalle expandible; CTA "Confirmar crédito" renombrado a
  "Guardar configuración" (no aprobaba elegibilidad, persistía la configuración del
  plan). Sin cambios de cálculo, reglas de negocio, ids ni contratos backend. Paso
  Revisión sin cambios en este lote — la transferencia del resumen financiero completo,
  evaluación y contrato a Revisión queda para VENTA-CREDITO-ARQUITECTURA-VISUAL-02.
  Hallazgo conocido no corregido en este lote: Enter no alterna los `<details>` del
  paso Crédito (interceptado por el handler global de avance del wizard en
  `venta-page-wizard.js`, preexistente — Espacio sí funciona correctamente).
- reapertura VENTA-CREDITO-ARQUITECTURA-VISUAL-02 del paso Crédito — micro-lote 2 de 2,
  completa la arquitectura: Revisión pasa a ser la fase real de revisión financiera/
  asesoria/contractual, Crédito queda enfocado en configurar. Resumen financiero completo,
  evaluación preliminar completa y estado final (elegibilidad/pendientes/excepción
  documental) se muestran en Revisión como un espejo de la misma autoridad que ya los
  calcula/pinta en Crédito (`actualizarPlanResumen`/`actualizarSemaforo` en
  `configurar-venta-credito.js`) — mismo `data` de `/Credito/SimularPlanVenta`, sin
  segundo cálculo ni segundo fetch; la documentación contractual se reubica en Revisión
  moviendo el nodo real del DOM (mismos ids/listeners) en vez de duplicarlo. "Documentación
  — exceptuada" reemplaza el rojo de bloqueante real en "Otros motivos" una vez aplicada la
  excepción documental (antes volvía a aparecer como pendiente sin resolver, contradiciendo
  la excepción ya aplicada). Sidebar compactada (Vendedor a meta-línea, Observaciones a
  `<details>` colapsado por defecto). Breakpoint del split sidebar/contenido del wizard
  pasa de 1024 a 1280 (antes `lg:` de Tailwind): a 1024px la sidebar se apila y el
  contenido recupera el ancho completo — altura de Step 4 medida con Playwright bajó de
  ~2924px a ~1777px en 1024×720 (y mejoras equivalentes en el resto de los viewports
  obligatorios), sin overflow horizontal en ningún caso. Enter ahora sí alterna los
  `<details>` (excluido del handler global de avance sólo cuando el foco está en un
  `<summary>`; Espacio seguía funcionando). "Continuar a revisión" aparece tras guardar la
  configuración del crédito. Sin cambios de cálculo, reglas de negocio, ids ni contratos
  backend. Validado en vivo con Playwright (Create y Edit, cliente con documentación+cupo+
  mora bloqueantes, excepción aplicada, plan configurado, "no viable" con plan guardado)
  y 4706/4706 tests (2 skipped ajenos), 0 rojos propios.
  Deuda conocida no bloqueante: no se agregó spec E2E versionado nuevo (la validación fue
  interactiva vía Playwright MCP); estados "viable sin bloqueantes" y "contrato ya
  generado" no se ejercitaron en esta corrida; colisión preexistente de `id="plan-total"`
  entre `_ConfigurarVentaEmbebida.cshtml` y `_CotizadorForm.cshtml` (ajena a este lote,
  sin efecto funcional porque el JS de Crédito siempre consulta con `root` scopeado).
- reapertura VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01 del paso Crédito: reduce el
  paso a 2 superficies principales. "Verificación crediticia" se renombra a "Estado del
  crédito" y fusiona en una sola superficie (filas `.credito-estado-row`, separador
  superior liviano, color sólo en ícono/texto) lo que antes eran hasta 6 cards con fondo/
  borde propio — Resultado SCORE, Cupo suficiente (eliminado, redundante), Documentación,
  Excepción documental aplicada, Alerta de Mora, Otros motivos — más una fila nueva
  "Riesgo" espejada del mismo semáforo que ya calcula "Configurar plan"
  (`actualizarSemaforo`, sin segundo cálculo ni fetch; la card "Evaluación preliminar" se
  retira de Configurar plan). "Ver detalle financiero" y "Detalle por cuota" se fusionan
  en un único `<details>` "Ver desglose ▸" (antes dos expansiones separadas). El motivo
  completo de la excepción documental se reubica de un `<details>Ver motivo▸` en Crédito a
  texto plano en Revisión (mismo id/gate de permiso, sin duplicarse). CTA: nuevo estado
  `scoreDisponible` (SCORE ya corrió, independiente de si el plan quedó guardado) permite
  que el CTA contextual pase por Verificar crédito → Guardar configuración → Continuar a
  revisión sin inventar "Reverificar"; el submit persistente del sidebar (`#btn-confirmar`)
  se oculta por completo durante todo el paso Crédito porque nunca ejecutaba esa acción
  (sólo avanzar(), bloqueado en ese paso) — corrige la incoherencia de 2/3 CTAs "Verificar
  crédito" simultáneos detectada en el audit previo. Sin cambios de cálculo, reglas de
  negocio, ids ni contratos backend; standalone `ConfigurarVenta_tw.cshtml` con 0 diff.
  2 bugs propios de este lote encontrados y corregidos en vivo con Playwright antes de
  cerrar: colisión de CSS Cascade Layers (una regla sin `@layer` le ganaba a `.hidden` de
  Tailwind, dejando filas vacías visibles antes de tener datos) y un CTA "Guardar
  configuración" en no-op silencioso cuando el configurador embebido queda bloqueado
  (fallback agregado con el mismo helper `mostrarRequisitoCredito` ya usado en otros
  bloqueos del wizard). Validado en vivo (cliente con documentación+cupo+mora
  bloqueantes, excepción aplicada, plan guardado, Revisión con motivo completo) en
  1440/1280/1024/390px sin overflow horizontal; 4718/4720 tests (2 skipped ajenos), 0
  rojos propios. Deuda no bloqueante: no se aisló con precisión el instante "SCORE aún no
  corrió" en pruebas scripteadas (el fetch resuelve demasiado rápido en el entorno local);
  la garantía de "sin CTA duplicado" en ese sub-estado se apoya en que el sidebar queda
  oculto incondicionalmente durante todo el paso, no en una captura puntual de ese
  instante. El apilado de Vendedor/Observaciones/Volver/Imprimir debajo del contenido
  (explícitamente opcional en la spec) no se implementó.
- reapertura VENTA-CREDITO-REDESIGN-VISUAL-CIERRE-01 del paso Crédito: cierra la mitad
  inferior en exactamente 2 superficies principales para todo el paso (Estado del
  crédito + Configurar plan). Antes de este lote esa mitad seguía leyéndose como hasta 4
  bloques con chrome propio consecutivos: título externo "Configuración de Crédito
  Personal" (retirado — "Configurar plan" queda como única identidad de sección) + card
  "Configurar plan" + `<aside>` "Resumen del plan" (retirado — el resultado vivo se
  integra al cierre de la misma card, sin su propio wrapper ni la caja verde
  `.result-card.is-ok`) + card "Ver desglose" (retirado — el mismo `<details>` fusionado
  en VISUAL-IMPLEMENTACION-01 pasa a vivir sin wrapper de card propio). "Cantidad de
  cuotas" se suma a la grilla de Anticipo/Gastos/Vencimiento (antes ocupaba una fila
  propia a ancho completo sin razón funcional) con un umbral nuevo de contenedor a 4
  columnas. CTA: el hero global del wizard (`[data-wizard-primary]`, header + barra
  sticky mobile) se oculta durante Crédito en cuanto existe un botón equivalente dentro
  del Plan (`guardar-configuracion`/`continuar-revision`) — sólo `verify-credit` (sin
  equivalente en el Plan) lo deja visible; nunca dos "Guardar configuración" a la vez.
  Sidebar: sólo durante el paso Crédito (todos los viewports, incluido desktop ≥1280px)
  el grid principal del wizard pasa a una columna — Vendedor/Observaciones/Volver/
  Imprimir fluyen debajo del contenido en vez de reservar una columna propia (~30% del
  ancho, casi vacía durante todo el scroll); el resto de los pasos no cambia. Sin cambios
  de cálculo, reglas de negocio, ids ni contratos backend; standalone
  `ConfigurarVenta_tw.cshtml` sin diff. 1 regresión propia encontrada y corregida antes
  de cerrar: retirar el wrapper `data-plan-cuotas-detalle` de la card de "Ver desglose"
  dejaba sin estilo (cursor/marcador/foco visible) al `<summary>` embebido — la regla CSS
  scopeada a ese atributo sumó la variante `data-plan-cuotas-detalle-details` (que ya
  llevaba el `<details>`) como bloque propio, sin tocar el selector original que protege
  la página standalone. Validado en vivo (Playwright, Create y Edit) en 1440/1280/1024/
  768/390/360px sin overflow horizontal, teclado (Enter/Espacio en "Ver desglose"),
  guardar → continuar a revisión → volver a Crédito sin pérdida de valores, y standalone
  intacto; 4731/4733 tests (2 skipped ajenos), 0 rojos propios.
- reapertura VENTA-CREDITO-REDESIGN-VISUAL-POLISH-01 del paso Crédito: pulido final de
  densidad dentro de "Configurar plan", sin tocar "Estado del crédito" ni reabrir las 2
  superficies ya cerradas. Grilla de inputs (Cuotas/Anticipo/Gastos/Vencimiento) más
  compacta (`gap-3`); separadores internos (recargo, primera cuota, resumen, CTA)
  reducidos de `margin-top:1.1rem;padding-top:1rem` a `.85rem`/`.75rem`. Resultado
  financiero deja de ser un bloque vertical (cuota + `<dl>` con "Cantidad de cuotas" +
  franja amarilla de ancho completo para el vencimiento) y pasa a una grilla de 3
  columnas en desktop ("Cuota" / "Total financiado" / "Vencimiento", container query
  nueva `[data-plan-resumen-grid]` a 26.25rem, mismo mecanismo que la grilla de inputs)
  con stack en mobile; el acento verde queda exclusivo de la cuota (Total financiado pasa
  a texto neutro, nueva clase `.result-value-sm`). Fila "Cantidad de cuotas" retirada del
  resumen (redundante con el input de arriba y con "N cuotas de $X ▸" del `<summary>` de
  "Ver desglose" bajo ella) — mismo id `plan-cuotas-label` sigue actualizándose vía JS
  (`if` guard) para no tocar la página standalone, que conserva la fila. Hallazgo
  adicional detectado en vivo durante la validación: la etiqueta "Cuota" del resumen
  también se sobrescribía dinámicamente a "N cuotas de $X"/"Ver detalle por cuota"
  (`formatearDetalleCuotas`), duplicando (y en el caso "Ver detalle por cuota" hasta
  simulando un segundo CTA no interactivo) lo que ya dice el `<summary>` de "Ver
  desglose" — se desactivó esa sobrescritura sólo para el fragmento embebido (`!embebido`
  en `configurar-venta-credito.js`); el standalone conserva el comportamiento dinámico
  sin cambios, ahí no es redundante. Recargo del plan con sufijo `%` visual (nueva clase
  `.field-percent`, mismo patrón que `.field-money`) sin tocar el valor que pinta el JS.
  Primera cuota (caso "no aplica") pasa de párrafo a nota `.hint` de una línea. CTA
  "Guardar configuración"/"Continuar a revisión" deja de ser `btn-block` en desktop
  (nueva clase `.plan-cta-row`, container query a 26.25rem: `align-items:flex-end` +
  ancho automático con mínimo `15rem`); mobile conserva ancho completo. Microcopy de
  "Gastos administrativos" y "Recargo del plan" abreviada; el texto protegido por test
  ("El backend valida los importes finales…") se mantiene literal, sólo con menos margen.
  Sin cambios de cálculo, reglas de negocio, ids, contratos backend ni arquitectura de 2
  superficies; standalone `ConfigurarVenta_tw.cshtml` sin diff. Validado en vivo
  (Playwright, Edit) en 1440/1024/390/360px sin overflow horizontal, edición de
  cuotas/anticipo/gastos/fecha con recálculo en vivo, primera cuota "aplica"/"no aplica",
  teclado (Enter en "Ver desglose"), guardar → continuar a revisión, standalone intacto;
  4731/4733 tests (2 skipped ajenos), 0 rojos propios. Hallazgo fuera de alcance (no
  corregido, es backend): al reconfigurar un crédito ya guardado, Anticipo y Gastos
  administrativos no persisten el nuevo valor en el POST de confirmación (vuelven al
  valor previamente guardado), mientras que Fecha de primera cuota sí persiste — pre-
  existente, ajeno a este lote (no se tocó `CreditoController`/`venta-credito-embebido.js`),
  reproducido en vivo sobre la venta VTA-202608-000080.
- reapertura VENTA-WIZARD-MOBILE-01 (mobile ≤767px, a pedido explícito: "que se vea
  especialmente bien en mobile"): medido en vivo a 390×844, antes de este lote el paso
  Productos **desbordaba 408px** horizontalmente (regresión ya commiteada, no mobile-only:
  `grid-template-columns: 1fr` en los resets ≤1279px de `venta-page-wizard.css` equivale a
  `minmax(auto, 1fr)`, así que el min-content de la tabla de productos ensanchaba la
  columna; también afectaba 1024–1279px) — corregido a `minmax(0, 1fr)` + `min-width: 0`
  (el test `VentaPageWizardCss_SidebarSinColumnaDedicadaDuranteCredito` se actualizó: el
  valor `1fr` era el bug, la intención "una columna" se conserva). Shell mobile: hero
  compacto (título + Cancelar en una fila; sin breadcrumb, sin descripción, sin el CTA del
  hero, que duplicaba el de la barra), stepper con los N pasos visibles a la vez (inactivos
  = número, activo = número + nombre; el nombre accesible se conserva; 40×40px, 34px a
  ≤380px; `venta-page-wizard.js` centra el activo si con Crédito no entran), barra de
  resumen fija abajo en una fila (Total + CTA; antes sticky arriba y apilada, 100px,
  tapando el campo en edición), sin el tercer CTA de la tarjeta amarilla del sidebar (sólo
  Revisión conserva recordatorio/"Facturar al confirmar"/"Guardar sin confirmar", neutra y
  sin botón principal: el de la barra dispara el mismo `#btn-confirmar`, verificado con el
  submit interceptado), encabezado de paso sólo con título (el círculo numerado se
  desincronizaba del stepper), tabla de productos → tarjetas (cantidad, precio, descuento
  y subtotal ahora visibles sin scroll horizontal; mismo DOM y `data-*`), filtros de
  búsqueda en `<details data-collapse-mobile>` cerrado en mobile (abierto y sin summary en
  ≥768px) y campos a 16px (iOS no hace zoom). Altura de los pasos Cliente/Productos/Pago/
  Envío 1547–1777px → 892px; el contenido empieza en el primer tercio de la pantalla.
  Sin cambios de reglas de negocio, ids ni contratos backend; desktop/tablet sin cambios
  (verificado 768 y 1440). Tablet y horizontal, que quedaron fuera de este lote, se
  resolvieron en VENTA-WIZARD-TABLET-01 (entrada siguiente).
- VENTA-WIZARD-TABLET-01 (tablet 768–1279px y teléfono en horizontal, continuación de
  VENTA-WIZARD-MOBILE-01): medido en vivo, **768–1279px es un solo rango** — el wizard se
  apila por debajo de 1280 y la columna útil queda en ~630–880px según la sidebar del ERP
  (16rem / 4.5rem / drawer), así que a 1024 con sidebar abierta fallaba igual que a 768:
  stepper cortado en "Envío" con "Revisión" oculta, tabla de productos con Subtotal y
  Eliminar detrás de scroll horizontal, y el CTA repetido en el hero y en la tarjeta
  amarilla. El bloque mobile se reorganizó en capas: (1) `@media (max-width: 1279px)`
  — hero de una fila sin breadcrumb/descripción/CTA duplicado, tarjeta amarilla sólo en
  Revisión, encabezado de paso sin círculo numerado y barra de resumen **sticky** al pie
  de la columna (no `fixed`: la sidebar del ERP cambia de ancho y la taparía; por eso la
  barra pasó a ser lo último del `<form>`); (2) **container queries** para lo que depende
  del ancho real y no del viewport — el stepper pasa a números + paso activo con nombre
  bajo 47rem de fila (`vmsteps`) y la tabla de productos pasa a tarjetas bajo 46rem de
  contenedor (`vmdetalle`); (3) `@media (max-width: 767px)` sólo teléfono (barra `fixed`,
  descripciones ocultas, filtros colapsados, campos a 16px); (4) viewport bajo (≤500px de
  alto): hero y barra compactos. Verificado en vivo en 768×1024, 900×720, 1024×720,
  1279×720, 844×390 y 667×375: 0 overflow horizontal y 0 errores de consola en
  Create/Edit; el CTA de la barra avanza el paso en Create y en Edit/Revisión dispara
  `#btn-confirmar` (incluido el modal "Confirmar y facturar"); crédito de 7 pasos entra
  completo en 768; nombres accesibles del stepper intactos. Regresión: las 26 capturas de
  teléfono 390/360 son idénticas byte a byte a las de antes de reorganizar el bloque. Dos
  hallazgos propios corregidos en el camino: un `sticky bottom` se ancla a la caja de
  contenido del scroller, y `_Layout` fija `padding-bottom: 6rem` inline, así que la barra
  flotaba 6rem sobre el borde visible (se compensa con `calc(.75rem - 6rem)`); y una regla
  de 16px para pantallas táctiles cortaba la fecha del cotizador en 768 (se retiró, queda
  sólo ≤767px). **Efecto colateral en escritorio, a confirmar:** por ser container query,
  la tabla de productos también pasa a tarjetas en ≥1280px cuando la columna izquierda
  (2fr) mide <736px (1280–1440 con sidebar abierta) — ahí la tabla ya cortaba Subtotal y
  Eliminar; sólo cambia el paso Productos (el resto de las capturas de 1280/1440 son
  idénticas). Si se prefiere conservar la tabla en escritorio, alcanza con envolver el
  `@container vmdetalle` en `@media (max-width: 1279px)`. Fuera de alcance: el paso Cotizar
  en 768–1023px conserva sus 2 columnas del cotizador (angostas, sin cortes).

## Venta / Details — cerrado

Resumen no cronológico de lo que quedó implementado:

- vista fina (`Details_tw.cshtml`) con card "Acciones" como única autoridad de qué mostrar,
  derivada de los mismos predicados `Puede*`/permisos que ya controlan cada botón (sin
  duplicar lógica de negocio);
- estado vacío explícito cuando ninguna acción está disponible para el estado actual;
- ningún CTA se ofrece si otra condición previa lo va a bloquear en el backend (ej. una
  venta con autorización pendiente ya no muestra "Configurar Crédito" — el backend la
  rechaza igual — y "Autorizar"/"Rechazar" queda como la única acción real disponible);
- foco contenido dentro de los modales (Cancelar, Anular Factura, Facturar);
- copy de acciones alineado con lo que realmente hace el endpoint;
- datos de crédito vigentes hidratados desde la misma fuente que gobierna cuotas y pagos
  (sin tabla legacy paralela);
- responsive validado.

Backlog transversal conocido (no bloquea el cierre): sin cobertura E2E propia en `e2e/`
todavía; el botón de impresión del header usa `window.print()` genérico sin hoja de
estilos de impresión dedicada para el módulo.

- reapertura VENTA-DETAILS-DUPLICACION-01 (H11), a partir de reporte directo del usuario
  sobre duplicación excesiva: causa raíz real del DNI repetido no era Razor — el mapeo
  compartido `Venta→VentaViewModel` (`AutoMapperProfile.cs`) armaba `ClienteNombre` con
  `ToDisplayName()`, que incrusta "- DNI: ..." en el nombre (pensado para desambiguar en
  listas sin columna de documento propia). `ClienteDocumento` ya es un campo separado en
  todas las vistas que consumen `VentaViewModel` (Details, Index), así que el sufijo
  quedaba duplicado contra ese campo; mismo criterio de anti-patrón ya corregido antes en
  `cotizacion-simulador.js`. Fix acotado al mapeo (no se tocó `ToDisplayName()`, que sigue
  vigente para Cuota/Garante y otros contextos que sí lo necesitan) — valida en vivo tanto
  en Details como en Index (pantalla cerrada, mismo campo, sin columna de DNI propia; sin
  pérdida de legibilidad observada). Además, sólo dentro de `Details_tw.cshtml`: la card
  hero "Detalle" (conteo de ítems/unidades) se retira — duplicaba el badge ya visible en
  "Detalle de productos" sin aportar valor de decisión (grilla hero pasa de 4 a 3
  columnas); columnas "Descuento"/"Desc. gral." de la tabla de productos se ocultan juntas
  cuando ninguna línea tiene descuento real (evita dos columnas vacías repetidas por fila
  sin perder el dato cuando sí existe). Sin cambios de cálculo, reglas de negocio, ids ni
  contratos backend. Hallazgos documentados sin implementar en este lote (duplicación
  legítima entre superficies con propósito distinto, no se tocan): Total de la venta
  repetido en 7 superficies (hero/línea/resumen/crédito/factura/alícuota); número de
  factura en 4 superficies; número de contrato en 2. Formato de moneda sin ceros de
  centavos cuando el importe es entero: evaluado y descartado por ahora (crearía
  inconsistencia de formato sólo en esta pantalla frente al resto del ERP, que mantiene 2
  decimales fijos) — candidata a regla reusable si se decide adoptarla ERP-wide.
  Validado en vivo con Playwright (segunda instancia en :5199 sobre el mismo build, sin
  tocar la instancia :18787 del usuario) en 1440×900 y 390×844 sin overflow horizontal, 0
  errores de consola; 1220/1220 tests focalizados (Venta + MappingProfile) verdes.

- reapertura VENTA-DETAILS-DUPLICACION-02 (H12), a partir de reporte directo del usuario
  sobre una segunda pasada de duplicación en la misma pantalla. De los ítems reportados,
  la mayoría ya estaba evaluada y aceptada en H11 (Total, número de factura, número de
  contrato, "Crédito Personal" repetidos entre superficies con propósito distinto) y no se
  tocó. Un ítem sí era nuevo: el CTA "Ver contrato" aparecía dos veces — como botón en la
  card "Acciones" y como botón propio dentro de la card "Contrato generado" (mismo
  `asp-controller/asp-action/asp-route-ventaId`, mismo target). A diferencia del resto
  (dato repetido en contextos distintos), acá era el mismo control interactivo duplicado,
  y contradecía el principio ya documentado de que "Acciones" es la única autoridad de qué
  mostrar (mismo patrón que ya respeta "Facturación", que no repite "Imprimir Factura"
  fuera de Acciones). Causa raíz verificada en código (no asumida): `hayAccionVerContrato`
  en Acciones estaba condicionado a `puedeOperarVentas` (caja abierta), pero
  `ContratoVentaCreditoController.Ver` es de solo lectura y no tiene ninguna verificación
  de caja — el mismo criterio que ya aplica `hayAccionImprimirFactura`, que nunca tuvo ese
  gate. Ese gate de más ocultaba el único acceso al contrato cuando no había caja abierta,
  lo que había forzado a duplicar el botón sin condición dentro de "Contrato generado" para
  no perder esa vía. Fix: se retira `puedeOperarVentas` de `hayAccionVerContrato` (deja de
  bloquear una acción de solo lectura que el backend siempre permite) y se retira el botón
  redundante de "Contrato generado", que queda como card informativa (mismo patrón que
  Facturación). Sin cambios de cálculo, reglas de negocio, ids ni contratos backend.
  Validado en vivo con Playwright (segunda instancia en :5199, sin tocar :18787) sobre la
  venta real reportada por el usuario, en 1440×900 y 390×844 sin overflow horizontal, 0
  errores de consola; 66/66 tests focalizados (VentaDetails + ContratoVentaCredito) verdes,
  build 0/0.

- reapertura VENTA-DETAILS-DUPLICACION-03 (H13): el usuario confirmó que la duplicación
  seguía sintiéndose real después de H11/H12, sobre los 4 grupos que H11 había aceptado
  como "duplicación legítima" (Total, N° de factura, "Crédito Personal", N° de contrato).
  Se re-evaluó cada uno mirando la pantalla renderizada (no solo el código) en vez de
  repetir la conclusión anterior sin evidencia fresca. Resultado: 2 de los 4 eran
  redundancia cognitiva real (mismo string, mismo bloque visual, sin scroll de por medio,
  sin aportar nada que el vecino no dijera ya) y se corrigieron; los otros 2 se
  reconfirmaron como duplicación útil con evidencia visual directa, no solo repitiendo el
  argumento de H11:
  - el chip de factura del hero (fila de pills superior) repetía el mismo número que la
    tarjeta "Facturación" de la grilla hero, un instante después sin scroll — se retira el
    chip, la tarjeta ya tiene más contexto (fecha de emisión) y el badge "Facturada" ya
    cubre la señal de "hay factura" a nivel chip;
  - el badge "Crédito Personal" en el header de la card "Información de la venta" repetía
    el mismo ícono+texto que el campo "Forma de Pago" dos renglones abajo, dentro de la
    misma tarjeta — se retira el badge, el campo ya lo dice con más contexto;
  - Total de la venta en el hero vs. la fila "Total" al pie de "Detalle de productos": se
    mantienen ambos — vistos en pantalla no compiten por la misma atención (estilo y
    tamaño distintos) y cumplen roles distintos (vistazo rápido vs. derivación visible del
    cálculo Subtotal→IVA→Total); "Monto Financiado" (Crédito Personal), factura.Total
    (Facturación) y el total del resumen por alícuotas son campos propios que hoy
    coinciden numéricamente en este caso de prueba pero pueden diferir en anticipo o
    facturación parcial — no se tocan;
  - "Crédito Personal" como chip superior del hero, campo "Forma de Pago" (tras retirar el
    badge redundante) y título de su propia sección: se mantienen los 3 — cada uno cumple
    un rol distinto (tag de escaneo rápido, campo de dato, identidad de sección) y ninguno
    queda pegado sin contexto contra otro que diga lo mismo;
  - N° de contrato (campo "Contrato N°") vs. nombre del PDF al pie de la misma tarjeta: se
    mantienen ambos — el nombre de archivo confirma qué documento se va a abrir, patrón
    estándar (mismo criterio que el nombre de archivo de una factura).
  Sin cambios de cálculo, reglas de negocio, ids ni contratos backend. Validado en vivo con
  Playwright (segunda instancia en :5199, sin tocar :18787) sobre la misma venta real del
  reporte, en 1440×900 y 390×844 sin overflow horizontal, 0 errores de consola; 66/66 tests
  focalizados (VentaDetails + ContratoVentaCredito) verdes, build 0/0.

- reapertura VENTA-DETAILS-CONFIRMAR-DUPLICADO-01, a partir de reporte directo del usuario
  con capturas ("me aparece 2 veces el botón confirmar/facturar, está mal que aparezca en
  detalles, borralo"): "Confirmar Venta" (+ checkbox "Facturar al confirmar" + modal
  "Confirmar y facturar") vivía duplicado en 2 pantallas para la misma venta — el mismo
  botón/modal ya existe en el paso Revisión del wizard de `Venta/Edit` (agregado el mismo
  día por `c4c23c6`/`93c7f4e`, que además unificó el patrón visual con Details sin notar la
  duplicación de superficie). Se retira por completo de Details: bloque `@if
  (hayAccionConfirmar)` (form, checkbox, modal `#modal-confirmar-facturar`) eliminado de
  `Details_tw.cshtml`; declaraciones `hayAccionConfirmar`/`puedeFacturarAlConfirmar`
  retiradas (sólo se usaban ahí); JS muerto correspondiente (`formConfirmar`,
  `chkFacturarDetails`, `btnConfirmarFacturarDetails`, `modalConfirmarFacturarDetails` y sus
  listeners) retirado de `details-venta.js`. "Editar Venta" queda como única vía desde
  Details hacia esa acción; `hayAccionFacturar` (venta ya confirmada, sólo falta
  facturarla — estado distinto, sin equivalente en el wizard) no se toca. Test
  `DetailsView_OfreceConfirmarYFacturarEnUnPaso` (`VentaDetailsUiContractTests.cs`), que
  protegía la presencia de ese bloque, reemplazado por
  `DetailsView_NoDuplicaConfirmarVentaDelWizardDeEdicion` (verifica ausencia del bloque
  retirado). Deuda no bloqueante, fuera de alcance de este lote: `ViewBag
  .PrimeraCuotaVenceHoy`/`PrimeraCuotaDecisionCobrar`/`PrimeraCuotaDecisionMedio` sigue
  poblándose en el controller (`VentaController.Details`) pero ya no lo renderiza ninguna
  vista — no se tocó el controller para no mezclar con lógica de negocio de cobro de 1ª
  cuota, cubierta por `VentaControllerConfirmarCreditoPersonalTests`. Sin cambios de
  cálculo, reglas de negocio, ids (`Confirmar`/`ConfirmarYFacturar` siguen existiendo como
  actions, sólo se retira el botón que los invocaba desde Details) ni contratos backend.
  Validado con build 0/0 y 11/11 tests focalizados (`VentaDetailsUiContractTests`).
  Hallazgo aparte durante la misma investigación (no es un bug de código, no se tocó
  nada por esto): el proceso `TheBuryProyect.exe` corriendo en esta máquina llevaba
  arrancado desde ~4h antes de los commits `c4c23c6`/`93c7f4e` que agregaron "Confirmar
  venta + Facturar" — el controller compilado en memoria todavía no tenía ese `switch`
  (las vistas sí se ven actualizadas por recompilación en caliente de Razor). Reproducido
  con una réplica exacta del POST real (`accionConfirmacion=confirmar-facturar`
  confirmado en el FormData enviado) que igual cayó en el `default` del código viejo.
  Requiere rebuild + restart del proceso para validar cualquier cambio de controller
  de esta sesión.

- reapertura VENTA-DETAILS-GEOMETRIA-01, a pedido explícito del usuario de converger la
  geometría de la pantalla con `Venta/Index` (ancho fluido) y `Cliente/Details` (ficha
  ejecutiva), sin agregar tabs ni tocar reglas de negocio. Causa raíz confirmada con
  geometría real medida en vivo (Playwright, instancia propia, 1920×1080), no asumida:
  `#venta-details-page` tenía `mx-auto w-full max-w-7xl px-4 py-4 sm:px-6 lg:px-8` propio
  — un `max-width` de 1280px centrado más un gutter horizontal duplicado sobre el que ya
  aporta `_Layout.cshtml` — mientras `#venta-index-rework` (`ventas-index.css`) y
  `.shell.cliente-details` (`cliente-module.css`) ya habían convergido a ancho fluido sin
  cap (mismo criterio que `VENTA-INDEX-CLIENTE-PARIDAD-01`). Medido: shell real de 1280px
  centrado con ~160px de margen muerto por lado a 1920px (vs. 1600px fluido de Index y
  Cliente/Details) y, como efecto colateral no evidente sin medir, la tabla de productos
  ya scrolleaba internamente incluso a 1920px (wrapper de 802px contra el
  `min-width:56rem` de la tabla). Fix: `#venta-details-page` pasa a `w-full py-4` (sin
  `max-w-7xl`/`mx-auto`/gutter propio); el grid main/aside pasa de `grid-cols-3` +
  `col-span-2` fijo (66/33 sin piso) a `lg:grid-cols-[minmax(0,1.65fr)_minmax(18rem,1fr)]`
  junto con `min-w-0` en main — mismo patrón ya usado en `Venta/Autorizar_tw.cshtml` y
  `Venta/Cancelar_tw.cshtml` (consistencia dentro del propio módulo), con piso de 18rem en
  el aside y `minmax(0,…)` para que la tabla no fuerce un blowout del grid. Resultado
  verificado: shell de Details pasa a 1600px, igual que Index y Cliente/Details; la tabla
  deja de scrollear internamente a 1920px (972px de contenido en un wrapper de 982px).
  Se evaluó explícitamente forzar además el layout a una sola columna en el rango
  1024–1536px para eliminar también ahí el scroll interno de la tabla, y se descartó: se
  comprobó en vivo que sin el piso de `min-width:56rem` los importes envuelven a 2 líneas
  ("$" / "120.000,00") y la columna final llega a recortarse contra el aside a 1280px —
  el piso de 56rem no es cosmético, protege exactamente la regla "evitar wrap innecesario
  en importes"; con un aside real de ≥18rem y un main+aside lado a lado, no hay ancho
  suficiente para 8 columnas financieras por debajo de ~1780px viewport. El scroll interno
  de fallback en ese rango (1024–1440px) es, por lo tanto, una decisión evidenciada, no
  una regresión: ya existía antes de este lote (el cap de 1280px tampoco daba espacio
  suficiente ahí) y el estándar admite explícitamente scroll "como fallback cuando
  realmente haga falta". Tarjetas con grids dependientes de breakpoints de viewport
  (Información, Crédito Personal, Contrato, Envío) se revisaron en vivo con datos reales
  tras el cambio de proporciones y no mostraron cramping en ningún viewport — no se
  tocaron. Sin cambios de cálculo, reglas de negocio, permisos, ids ni contratos backend;
  ninguna otra vista comparte `#venta-details-page` (verificado por grep). Validado en
  vivo con Playwright (instancia propia en :5199, build a `bin/valrun` para no chocar con
  el proceso `TheBuryProyect.exe` del usuario en :18787, detenida al cerrar) sobre 3
  ventas reales distintas (sin crédito/envío, con envío, con Crédito Personal + contrato
  generado) en 1920×1080, 1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800: sin
  overflow horizontal de página en ningún viewport, 0 errores/warnings de consola; build
  0/0; 11/11 tests focalizados (`VentaDetailsUiContractTests`) verdes. 3 tests de
  `VentaDetailsAjustePlanUiContractTests` fallan buscando contenido
  (`Model.TieneExcepcionDocumentalRegistrada`, `_VentaRazonesAutorizacion`) que no existe
  en `Details_tw.cshtml` ni en `HEAD` antes de este lote — confirmado pre-existente y
  ajeno a este cambio, no corregido (fuera de alcance). Working tree con WIP externo
  concurrente detectado (`Controllers/VentaController.cs` modificado,
  `VentaControllerEditEnvioModelStateTests.cs` nuevo) — no tocado.

## Cotización / Simular — cerrado

Parcial compartido `_CotizadorForm.cshtml` (pantalla completa en `Cotizacion/Index_tw` y
embebido en la pestaña Cotizar de `Venta/Create`). Serie COTIZACION-SIMULAR-REDESIGN,
desktop/tablet cerrado en IMPLEMENTACION-01 (7a0d3fd) + TOAST-OVERLAP-FIX-01 (6f41a08) +
POLISH-01 (f722a23); mobile real cerrado en CIERRE-01 (este lote):

- banda angosta (<40rem de contenedor, viewports 390/360px reales): Resultados pasa a
  aparecer primero también acá — antes sólo se reordenaba desde 40rem, por debajo de eso
  el panel completo de Productos se mostraba primero y empujaba Resultados fuera del
  primer viewport (mismo mecanismo de `grid-template-areas` vía container query ya usado
  en las bandas más anchas, sin duplicar DOM);
- Productos y Configuración pasan a paneles colapsables (arrancan cerrados, un `<button>`
  real con `aria-expanded`/`aria-controls` los expande) — reutilizan el DOM real de cada
  zona, no hay una segunda representación ni un drawer nuevo; el botón vive siempre en el
  documento pero es `display:none` (fuera del árbol de accesibilidad) fuera de esa banda,
  mismo patrón que el toggler de un navbar responsive;
  el CTA de Configuración (`.panel-foot`, Total + Simular/Guardar) nunca se colapsa;
- tabla de Resultados: mismos `<td>` y mismos hooks (`data-cotizacion-row-key`,
  `data-cotizacion-opcion-key`, `data-g`, `aria-expanded`) que desktop, reflow a filas
  compactas de 2 líneas vía flexbox + `nth-child` (Medio/plan + Total en la línea 1;
  Plan/Cuota + Estado en la línea 2) sin ningún cambio de JS — el click delegado en
  `#cotizacion-resultados-tbody` sigue funcionando igual. Recargo se oculta de la fila
  compacta; sigue disponible al tocar la fila (abre el mismo drawer "Detalle del plan" que
  ya usa desktop). Antes la tabla desbordaba ~266px con scroll horizontal interno;
- patrón elegido: A (Resultados primero + paneles secundarios colapsables) sobre B
  (drawers laterales) — reutiliza el DOM real sin gestión de foco nueva que mantener;
- sin barra sticky: descartada explícitamente — con Productos/Config colapsados el CTA ya
  queda a 1-2 scrolls de Resultados (antes ~2200px de scroll total en 390px, ahora la
  página completa entra sin scrollear en el estado vacío) y evita competir con el botón
  flotante global "Reportar incidencia" (`_Layout.cshtml`, fuera de alcance de este
  módulo);
- sin cambios de cálculo, reglas de negocio, `state.opcionSeleccionada`, `bestKey`, payload
  Guardar, semántica "Mejor precio"/"Pago único"/"Seleccionado" ni tabla desktop; desktop/
  tablet (1440/1280/1024/768) revalidado sin cambios perceptibles.
  Validado en vivo con Playwright en 390×844 y 360×800 (toggles, agregar producto, editar
  cantidad, cambiar cliente, expandir/colapsar grupo Tarjeta/Crédito personal, seleccionar
  opción, Simular, Guardar) sin overflow horizontal de página ni de tabla; test E2E nuevo
  T9 en `e2e/cotizacion-simulador.spec.js` (10/10 verdes en esa spec).
  Hallazgo fuera de alcance encontrado durante la validación (no corregido, ajeno a este
  lote): `e2e/cotizacion-conversion.spec.js` (T9-T12) espera que Guardar navegue a
  `/Cotizacion/Detalles/{id}` (`page.waitForURL`) — comportamiento desactualizado desde
  IMPLEMENTACION-01, que dejó a Guardar en la misma página con un link "Ver cotización"
  (reproducido en vivo en navegador real, sin relación con CIERRE-01: ocurre a 1366px,
  fuera de cualquier banda tocada por este lote).

- reapertura COTIZACION-SIMULAR-DENSIDAD-01, a partir de reporte directo del usuario
  ("lo veo todo demasiado amontonado, y poco legible") sobre la misma pantalla ya cerrada.
  Causa raíz verificada en vivo con datos reales (no asumida): el umbral de 68rem que activa
  el workspace de "comparación completa" (3 columnas: Productos | Resultados | Config) y el
  piso `minmax()` de esas dos columnas laterales (14rem/19% Productos, 15.5rem/21% Config)
  estaban desalineados — a 68rem el componente porcentual todavía no le ganaba al piso, así
  que cruzar a "modo comparación ancho" daba MENOS aire a Productos/Config que la banda de 2
  columnas anterior (ahí ocupan la mitad del contenedor completo, ~450-490px medido en vivo a
  1280px), en vez de más. Reproducido con datos reales (2 productos, comparativa completa de
  6 medios con Tarjeta crédito expandida a 3 planes) tanto en `Cotizacion/Index_tw` a 1440px
  como embebido en la pestaña Cotizar de `Venta/Create` hasta 1920px de viewport (contenedor
  real más angosto que standalone por la sidebar del wizard, ~1336px a 1920px): nombres de
  producto truncados en el carrito y los inputs "Dto. %"/"Dto. $" de cada línea prácticamente
  sin ancho usable — justo el rango de resoluciones de escritorio más común (1366-1536px
  reales standalone, y hasta 1920px embebido). Fix acotado a 3 valores en
  `wwwroot/css/cotizacion-simulador.css` (piso subido a 17rem/18rem, umbral corrido de 68rem
  a 84rem, aplicado a los 2 bloques `@container` que comparten ese umbral — el de la grilla/
  alto de panel y el del padding reservado para el botón flotante global "Reportar
  incidencia"): la banda de 2 columnas, más generosa, pasa a cubrir standalone hasta
  ~1536px reales y embebido hasta 1920px reales; el modo de 3 columnas queda para standalone
  ≥~1600px o embebido en monitores aún más anchos, donde el piso nuevo ya entra holgado (no
  se tocó la banda ≥90rem en la práctica porque a esos anchos el componente porcentual ya
  superaba ambos pisos, viejo y nuevo, sin cambio perceptible). Sin cambios de cálculo,
  reglas de negocio, ids, `data-*` ni contratos backend — sólo 2 números (`min-width` de los
  `@container`) y 2 valores de piso (`minmax()`) más los comentarios que documentan la
  decisión. Validado en vivo con Playwright, con datos mockeados a nivel `fetch` (base local
  sin productos utilizables en el momento de la validación, ajeno a este módulo) porque el
  hallazgo sólo es observable con el workspace de Resultados poblado, no en el estado vacío:
  1440×900 y 1920×1000 en `Cotizacion/Index_tw` (pasan de 3 a 2 columnas / se mantienen en 3
  columnas con piso más generoso, respectivamente) y en la pestaña Cotizar de `Venta/Create`
  (pasan de 3 a 2 columnas en ambos casos), más regresión en 1280×800, 1024×720, 768×1024 y
  390×844 sin cambios perceptibles y sin overflow horizontal ni errores de consola. Deuda no
  bloqueante: no se re-ejecutó `e2e/cotizacion-simulador.spec.js` (requiere una segunda
  instancia de la app en el puerto que usa `playwright.config.js`, no se levantó para este
  lote) — el cambio es sólo CSS de `@container`/`minmax()` sobre ids y estructura ya
  existentes, sin tocar nada que esas specs excluyan; sus aserciones son de visibilidad, no
  de posición de columnas.

- reapertura COTIZACION-SIMULAR-ORDEN-01, misma sesión, a partir de reporte directo del
  usuario ("está ordenado de forma horrible") sobre 3 aspectos distintos, confirmados los 3
  con evidencia real:
  - **orden de paneles**: en la banda de 2 columnas (44rem-84rem, ampliada por
    DENSIDAD-01 a cubrir la mayoría del escritorio real) Resultados ocupaba la fila
    superior completa, por delante de Productos — un simulador vacío ("Sin simulación
    todavía") recibía al operador antes que el punto de partida real (buscar un
    producto), y antes que Configuración (Cliente/Descuentos/Anticipo). Se invirtieron
    las 2 filas (Productos + Config primero, Resultados después a todo el ancho); se
    evaluó y descartó un layout con Resultados como columna lateral angosta porque
    ninguna proporción de columnas deja ancho suficiente para la tabla de 6 columnas Y
    un riel de Productos usable a la vez en este rango (medido en vivo a 1024px:
    introducía scroll horizontal interno en la tabla que no existía antes). La banda de
    3 columnas y el mobile real (<40rem, ya cerrado en CIERRE-01 con "Resultados
    primero" deliberado) no se tocan;
  - **orden de las filas de Resultados**: los medios de pago se pintaban en el orden fijo
    en que los devuelve el backend (enum), no por conveniencia — un medio "Sin
    planes"/"Req. cliente" en el medio de la lista interrumpía la comparación de precios
    entre los medios sí disponibles. `cotizacion-simulador.js`: los grupos con planes
    disponibles ahora se ordenan por su plan más barato (mismo criterio que ya usa la
    pill "Mejor precio"), los grupos sin planes quedan al final en su orden relativo
    original (sort estable); no cambia `bestKey`, `opcionSeleccionada` ni ningún cálculo,
    sólo el orden de pintado — verificado en vivo con datos mockeados que fuerzan un
    medio "off" intercalado en el medio del enum;
  - **orden interno de Configuración**: "Anticipo (Crédito personal)" — condicional, sólo
    relevante con ese medio incluido, y ya tratado como secundario por el propio JS
    (`data-anticipo-activo` lo atenúa cuando no aplica) — aparecía antes que "Descuentos
    generales", universal. Se invierte el orden en `_CotizadorForm.cshtml` (mismos ids,
    sin cambios de JS).
  Sin cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend. Validado
  en vivo con Playwright (datos mockeados, mismo motivo que DENSIDAD-01) en 1024×900,
  1280×800, 1440×1000 y 768×1024 standalone (sin overflow horizontal ni de tabla en
  ninguno) y en la pestaña Cotizar de `Venta/Create` a 1440px; regresión en mobile real
  390×844 confirmando que el orden "Resultados primero" de CIERRE-01 sigue intacto.

- reapertura COTIZACION-SIMULAR-GUARDAR-DIRECTO-01, misma sesión, a pedido explícito del
  usuario: "Guardar" y "Pasar a venta" pasan a ser una sola acción, sin el modal de
  confirmación intermedio ("Guardar cotización": resumen + advertencia + botón
  confirmar). Un click en "Guardar" guarda la cotización y, si hay un cliente de sistema
  seleccionado (requisito real preexistente de `pasarAVenta()`, no nuevo), continúa solo
  a convertirla en venta y navega — sin pausa para revisar un resumen que ya se ve
  completo en el propio formulario antes de guardar (Cliente/Resultados/Total ya están a
  la vista). Guarda primero con su propio try/catch; si guarda bien, la UI queda siempre
  en el estado "guardado" real (`mostrarAccionesPostGuardado`) antes de intentar pasar a
  venta — si esa segunda llamada falla, o no hay cliente de sistema en ese momento, el
  operador ve la cotización guardada con "Pasar a venta" disponible para reintentar a
  mano, en vez de quedar en un estado ambiguo. Decisión explícita del usuario (con el
  trade-off planteado y confirmado): esto vuelve irrepetible por UI, desde el simulador,
  el escenario de fondo que usaba `e2e/cotizacion-conversion.spec.js` (T9-T12) — guardar
  con cliente de sistema ya no deja una cotización "Emitida" sin convertir en
  `Cotizacion/Detalles` para ejercitar ahí el modal de conversión manual
  (`#cotizacion-btn-convertir`), porque ese modal comparte la misma precondición
  (ClienteId) que ahora dispara la conversión automática desde el simulador. Esa pantalla
  de conversión manual en Detalles no se tocó y sigue funcionando (p. ej. para una
  cotización guardada sin cliente de sistema en su momento); los 4 tests afectados ahora
  skipean con el motivo real documentado en el propio archivo — reescribirlos para llegar
  a ese estado por API directa (sin conducir el simulador) queda pendiente como
  seguimiento, fuera de este lote. Modal "Guardar cotización" completo (`#modal-guardar`,
  `#cotizacion-guardar-confirm`, resumen `mg*`) retirado de `_CotizadorForm.cshtml`,
  `cotizacion-simulador.js` y `cotizacion-simulador-ui.js` (listener de Escape); sin
  cambios de cálculo, reglas de negocio, ids ni contratos backend en el resto del flujo.
  Validado en vivo con Playwright: click en Guardar sin cliente de sistema → guarda,
  sin modal, feedback explica por qué no continuó, "Pasar a venta" queda disponible;
  click en Guardar con cliente de sistema → guarda y encadena sola a la conversión,
  navega a `/Venta/Edit/{id}` sin ningún click intermedio (verificado contra endpoints
  mockeados, dado que la base local no tiene productos utilizables ahora mismo — ver
  hallazgo de DENSIDAD-01). Tests focalizados: `CotizacionControllerUiTests` (43/43,
  incluye el reemplazo de `Modal_Guardar_MuestraOpcionSeleccionadaNoMejorOpcion` por
  `CotizadorForm_NoDeclaraModalGuardarPropio`) y build 0/0.

- reapertura COTIZACION-WORKSTATION-01, a pedido explícito del usuario con mockup como
  referencia visual autorizada, tras el rework funcional de la etapa (Cotización agnóstica
  al medio de pago). Diagnóstico del usuario, confirmado en vivo: la pantalla se leía como
  "formulario + formulario + tabla cruda" — carditis (card dentro de card dentro de card),
  Cliente y Condiciones cargados como una lista plana de campos, Crédito personal dominando
  todo el panel Cliente con una banda roja, Guardar con peso de CTA primario, y una
  transición débil entre configurar → simular → comparar → elegir → continuar. Alcance
  visual/UX: no se tocaron reglas de negocio, cálculos, recargos, cuotas, permisos,
  aptitud, autorización, stock, caja, endpoints ni ViewModels.
  - **Superficies (§3/§24)**: quedan 3 con borde (Productos, Cliente+Condiciones,
    Resultados) más la franja de Totales. `.card-sub` se restringe al drawer de plan; la
    ficha de Cliente, la aptitud crediticia y los avisos de descuento pasan a acento
    lateral/tipografía. Una escala de spacing única (.45/.55/.7/.9rem).
  - **Cliente (§5/§6)**: identidad (avatar + nombre + documento) con el estado de Crédito
    personal contiguo y subordinado — acento lateral de 2px (verde/ámbar/rojo) con eyebrow
    "Crédito personal" que nombra su alcance real, en vez de una banda que teñía todo el
    panel como si la operación entera estuviera rechazada. El detalle (cupo, motivos) sigue
    detrás de "Ver situación".
  - **Totales + CTA (§8/§9)**: la franja Subtotal/Descuento/Total base pasa a ser el puente
    entre configuración y resultados y aloja la única acción primaria de la etapa, que
    alterna "Simular cotización" ↔ "Actualizar cotización". El CTA del header del wizard se
    oculta durante Cotizar (`ocultarCtaGlobal` en `venta-page-wizard.js`, mismo mecanismo ya
    usado en el paso Crédito): antes la misma intención existía duplicada a media pantalla
    de distancia, con el botón local escondido por CSS.
  - **Comparador (§11-§15)**: columna ACCIÓN con "Elegir" explícito por alternativa —
    antes la única forma de elegir era clickear cualquier parte de la fila, sin affordance
    visible, y eso además abría siempre el drawer. Ahora "Elegir" selecciona sin abrir el
    detalle y el click en el resto de la fila abre el detalle; el drawer cierra el circuito
    con "Elegir esta opción". Jerarquía padre/hijo real (la fila del medio lleva ícono,
    peso tipográfico y separador de grupo; las de plan van indentadas, más livianas y **sin
    repetir el Estado**, que es el mismo para todo el grupo). Bajo el nombre de Crédito
    personal va el dato que explica su estado (cupo vs. monto solicitado), no una segunda
    copia del veredicto que ya está en la columna Estado.
  - **Cierre (§16/§17)**: barra de selección al pie del comparador con la opción elegida y
    dos acciones de jerarquía explícita. **Cambio de intención pedido por el usuario**:
    "Guardar" deja de ser `.btn-success` a ancho completo y de hacer dos cosas a la vez
    (persistir + convertir); ahora es secundaria (`.btn-soft`) y sólo persiste, y
    "Continuar con esta opción" es la única primaria y encadena la conversión. Mismos
    endpoints, mismo payload y misma secuencia interna (`guardarCotizacion()` +
    `continuarConOpcion()` en `cotizacion-simulador.js`).
  - **Filtros de medios (§3)**: la banda permanente de 6 chips sobre el comparador pasa a
    un desplegable en el encabezado. Mismos checkboxes y mismos `data-cotizacion-medio`.
  - **Responsive (§27/§28)**: el orden del DOM pasa a ser el orden del flujo (Productos →
    Cliente/Condiciones → Totales+Simular → Resultados), que es el que el usuario fijó para
    mobile. Eso **revierte a propósito** el "Resultados primero + Productos/Config
    colapsables" de CIERRE-01: con Productos ya primero no hay nada que plegar, así que los
    toggles y su handler se retiraron. Umbral de 2 columnas bajado de 44rem a 38rem de
    contenedor: a 1024×720 y 768×1024 el comparador sube ~400px (de `top:1291` a `top:891`).
  - Hallazgos propios encontrados y corregidos durante el QA en vivo, no asumidos: (a) el
    leak de `venta-page-wizard.css` ya documentado para `.total-display`/`.field` también le
    gana a los utilitarios `pl-8`/`pl-6`/`pr-6` — dentro del wizard el ícono de búsqueda
    quedaba encima de la primera letra del placeholder y el "$" pegado al valor del Anticipo
    (sólo reproducible embebido, no en standalone); (b) `.hidden` de Tailwind (una clase)
    perdía contra cualquier `.cotz-app .x { display:… }` propio, así que `show()/hide()`
    dejaban de funcionar en los componentes nuevos — blindado una vez con
    `.cotz-app .hidden { display:none !important }`; (c) `.rt-accion { width:1% }` es un
    truco de layout de tabla que como flex-item se resolvía a ~4px reales y desbordaba la
    fila: eran los 6px de scroll horizontal dentro del panel de Resultados a 390px; (d) en
    el rail angosto de Productos (≤52rem de contenedor, que incluye la banda de 2 columnas
    de 1024/768) el stepper dejaba ~60px por descuento y las etiquetas "Dto. %"/"Dto. $"
    partían en dos líneas.
  - **Validación**: build 0 errores / 0 advertencias. Tests .NET focalizados
    (`Cotizacion|Venta|Credito|Ui`): 2384/2394 — los 10 rojos restantes son pre-existentes
    y ajenos (`Views/Producto/Edit_tw.cshtml` no existe; `Create_tw.cshtml`,
    `_VentaWizardForm.cshtml` y `venta-create.js` están en HEAD sin modificar;
    `Details_tw.cshtml` es WIP externo concurrente). E2E: `cotizacion-simulador.spec.js` +
    `venta-cotizar-step.spec.js` + `cotizacion-conversion.spec.js` = 26 passed, 4 skipped
    (skips conocidos y documentados en el propio spec), 0 rojos. QA visual en vivo con
    Playwright (instancia propia contra el proceso del usuario en :18787) en 1920×1080,
    1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800: sin overflow horizontal en
    ninguno, 0 errores de consola, y a 1920×1080 se mantiene el above-the-fold pedido
    (header, Productos, Cliente+Condiciones, franja de Totales, encabezado de Resultados y
    la primera alternativa real cierran en y=904). Verificado además el host standalone
    (`Cotizacion/Index_tw`), que comparte el parcial.
  - Contratos de markup actualizados, no borrados, donde el pedido del usuario cambió la
    intención que protegían: `CtaSimularGuardar_SimuladaDegradaSimularASecundaria` (Guardar
    ya no es `.btn-success`), `ClienteSeleccionado_NombreSinDniDuplicado` (el botón-ícono
    "Quitar cliente" pasó a un botón con texto "Cambiar"),
    `EstadoDeSimulacion_NoCuadruplicaSenales`, `Descuento_NeutralPorDefecto…`,
    `VentaPageWizardJs_OcultaCtaGlobalCuandoElPlanTieneBotonEquivalente` y el T9 mobile de
    `cotizacion-simulador.spec.js`.
  - Working tree con WIP externo concurrente (Caja/Conciliación, `VentaController.cs`,
    `Views/Venta/Details_tw.cshtml`, `VentaControllerEditEnvioModelStateTests.cs`) — no
    tocado. Sin commit ni push.

- COTIZACION-WORKSTATION-02 — pasada de fidelidad visual y contraste, a pedido explícito
  ("los productos seleccionados y el cliente seleccionado no se distinguen del fondo; la
  pantalla sigue demasiado plana"). No es un rediseño: corrige la jerarquía de superficies
  que WORKSTATION-01 dejó pendiente. Sin tocar endpoints, servicios, ViewModels, reglas de
  negocio, cálculos, recargos, cuotas, aptitud, autorización, stock, caja ni conversiones.

  **Causa raíz (medida en vivo, no supuesta): la escala de elevación estaba invertida.**
  El fondo de la app es `#0b0e14` (`--ml-bg`), el panel `#111827`, y todo lo que vivía
  DENTRO del panel era **más oscuro** que él: `.cart-row` y `.field` en `#0f172a` (a 3
  puntos de RGB entre sí y del propio panel) y las filas padre del comparador en `#0e1726`.
  Un bloque más oscuro que su contenedor se lee como un hueco, no como contenido elevado —
  de ahí que el producto agregado y el cliente elegido "se mezclaran con el fondo" y que el
  producto compitiera en peso con los inputs del buscador.

  La escala correcta ya existía en el design system: `theme-ml.css` define
  `--ml-bg < --ml-panel < --ml-panel-2 < --ml-elev`, **ascendente**. Se introduce ese mismo
  contrato como tokens locales (`--cz-s1/--cz-s2/--cz-s3/--cz-sunk`) expresado en la familia
  navy que ya usa el cotizador — mismo rol y mismos saltos de luminancia, no una paleta
  nueva (§19 del pedido) — más un nivel *hundido* para los controles de entrada, que sí
  deben quedar por debajo de la superficie que los contiene:

  | nivel | uso | color |
  | --- | --- | --- |
  | fondo app | shell ERP (no lo pinta el cotizador) | `#0b0e14` |
  | superficie workstation | `.panel`, `thead`, fila de plan hija | `#111827` |
  | bloque | producto agregado, cliente elegido, fila de medio, franja de totales | `#1b2539` |
  | hover de bloque | `.cart-row:hover`, fila de medio hover | `#223050` |
  | hundido | `.field`, zona Condiciones, pie de selección | `#0c1220` / velos |
  | elegida | alternativa seleccionada (acento, no superficie) | `rgba(59,130,246,.16)` + barra de 3px |

  Cambios por zona: producto agregado y cliente elegido pasan a bloque elevado con borde y
  hover (comparten lenguaje visual: son ambos "lo que ya cargué"); Condiciones pasa a una
  zona **recesada** dentro del mismo panel — Cliente elevado / Condiciones hundido se
  distinguen sin agregar otra card (§9); la franja de Totales sube a nivel bloque con
  degradado propio, borde reforzado, valores de `.95rem` a `1.02rem` y divisor vertical
  antes del CTA (es la única superficie con degradado de la pantalla, que es justamente lo
  que la marca como transición, §13); en el comparador el nivel de elevación de la fila
  **es** su nivel jerárquico — toda alternativa de primer nivel (con planes o sin ellos)
  sube a bloque y los planes hijos vuelven al nivel del panel (antes las filas de medio sin
  planes no tenían fondo alguno y pesaban igual que un plan hijo); la aptitud de Crédito
  personal conserva el acento lateral y suma un velo semántico del 7% **sobre su propio
  bloque**, nunca sobre el panel Cliente (§8).

  Cuatro defectos propios encontrados midiendo en vivo durante esta pasada:
  - `wwwroot/css/layout.css` declara `header { min-height: 4rem }` con selector de
    **elemento** — pensado para el header global del ERP pero aplicado a cualquier
    `<header>` de cualquier página: la barra contextual de Cotización medía **64px para una
    línea de 30px**. Neutralizado con `min-height: 0` scopeado a `.cotz-app` (gana por
    especificidad, sin `!important`, sin tocar `layout.css`). Devuelve ~24px de alto a la
    workstation (§16). *Deuda ajena: cualquier otro `<header>` anidado del ERP paga lo
    mismo — no se toca acá.*
  - El leak ya documentado de `venta-page-wizard.css` (`#venta-create-page input[type=…]`,
    id + atributo) también fija `background-color` y borde de los inputs: el nivel hundido
    no llegaba a aplicarse **justo en el host embebido**, el que se estaba corrigiendo. Se
    agregan esas dos propiedades a la neutralización existente; la geometría se deja como
    estaba para no alterar densidad ya validada.
  - Mismo leak vía `#venta-create-page .btn { min-height: 2.55rem }`: todo `.btn-xs` del
    cotizador se renderizaba a 41px dentro del wizard y a 30px en standalone.
  - A 1024×720 y 768×1024 el comparador necesitaba 706px en 620px disponibles: el
    `overflow-x: auto` del panel salvaba el layout pero obligaba a scrollear horizontalmente
    la herramienta de decisión de la pantalla. Banda intermedia nueva
    (`@container (min-width: 38rem) and (max-width: 46rem)`) que ajusta sólo aire horizontal
    — ninguna columna se oculta ni se recorta. Medido después: `scrollWidth == clientWidth`.

  Validación: 7 viewports (1920×1080, 1440×900, 1280×720, 1024×720, 768×1024, 390×844,
  360×800) sin overflow horizontal —ni de documento ni dentro del comparador— y 0 errores de
  consola; above-the-fold a 1920×1080 preservado con la primera alternativa real visible;
  host standalone `Cotizacion/Index_tw` verificado (mismo parcial, misma escala, topbar 44px);
  estados cubiertos: vacío, 1 producto, 2 productos, cliente no apto colapsado y con "Ver
  situación" desplegado, Crédito personal expandido, alternativa elegida y drawer de plan.
  Sin tests nuevos: el lote es cosmético y no introduce contratos de markup nuevos (los
  contratos existentes —`.cart-row`, `<header class="cotz-topbar">`, ids y `data-*`— se
  preservan intactos).

- reapertura puntual, a partir de reporte directo del usuario sobre el panel Productos del
  paso Cotizar de `Venta/Create` ("los productos agregados no se distinguen del fondo").
  QA en vivo (Playwright, sesión propia sobre el proceso del usuario en :18787, sin
  reiniciarlo) confirmó que WORKSTATION-02 ya resolvía el pedido — `.cart-row` elevado con
  borde/hover, jerarquía nombre/meta/precio, subtotal anclado — en 1920×1080, 1440×900,
  1024×720, 390×844 y 360×800 con 1 y 2 productos. A 1280×720 apareció un defecto propio no
  documentado hasta ahora: los inputs Dto. %/Dto. $ del producto agregado se renderizaban
  rotos (spinner nativo del navegador cortado, se leía como un "(" en vez de "0"). Causa
  raíz medida en vivo (no asumida): el mismo leak cruzado de `venta-page-wizard.css`
  (`#venta-create-page input[type="number"]`, ID+atributo) ya documentado arriba para
  `.field`/`.total-display`/`.btn-xs` también le gana a `.cotz-app .qty-step input` (dos
  clases) y fuerza `width:100%` + padding/borde/fondo propios en el input de cantidad — a
  886px de contenedor real (banda "auto 1fr 1fr" de `.cart-row-inputs`), el stepper pasaba
  de ~86px a 285px, dejando sólo 44px para cada input de descuento. El mismo leak también
  vaciaba visualmente el dígito de Cantidad (padding heredado de 24px horizontales sobre un
  input de 34px). Fix: `.cotz-app .qty-step input` neutraliza ancho/padding/borde/fondo con
  `!important`, mismo patrón ya usado para `.field`. Sin cambios de cálculo, reglas de
  negocio, ids ni contratos backend. Revalidado en vivo (1440×900, 1280×720, 1024×720,
  390×844, 360×800, 1 y 2 productos) sin overflow horizontal, 0 errores de consola.

Auditoría de 4 capas (sin trabajo previo de este estándar). Micro-lote 1 (bloqueante+alto)
más micro-lote 2 (deuda de paleta/tokens, a pedido explícito de ampliar alcance):

- breadcrumb real ("Inicio › Configuracion global de pagos") reemplaza un eyebrow que
  repetía casi literal el H1 sin aportar navegación (§4 exigía breadcrumb, la pantalla no
  tenía);
- aviso "Credito personal se administra en una fase separada" retirado del header genérico
  (se mostraba siempre, para cualquier medio seleccionado) — queda solo una vez, dentro del
  panel del medio Crédito personal, que ya lo explica con más contexto y su propio CTA;
- badges "N tarjetas activas" / "N planes" del panel del medio ahora se condicionan a
  `permiteTarjetas`/`permitePlanesGlobales`: para Crédito personal (que el propio backend
  bloquea para ambos conceptos) ya no se muestra "0 tarjetas activas" / "0 planes", que
  sugería un estado vacío completable en vez de un bloqueo real por diseño;
- breakpoint nuevo en 1024px para `.payments-topbar` y `.method-panel-header`: sin él, el
  H1 "Configuracion de pagos" y el H2 del medio seleccionado se partían en 2 líneas de
  forma antiestética en el rango 701–1024px (grid de 2 columnas con poco espacio para el
  texto) — confirmado en vivo con Playwright en 1024×720/900, resuelto apilando antes.
  Sin cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend.
  Validado en vivo con Playwright en 1440×900, 1024×900/720 y 390×844 (Efectivo,
  Transferencia, Tarjeta crédito con Visa expandida, Crédito personal, form "Editar
  método" en mobile) sin overflow horizontal, 0 errores/warnings de consola; build 0/0.

Micro-lote 2 — tokens de paleta (`--pay-*`, ~40 usos) dejan de hardcodear valores propios
y pasan a derivar de los tokens de tema reales (`--erp-bg`, `--erp-surface`,
`--erp-surface-raised`, `--erp-border`, `--erp-text`, `--erp-text-muted`,
`--erp-text-faint`, `--erp-primary`, definidos en `theme-ml.css`): si el tema del ERP
cambia, la pantalla cambia con el resto en vez de divergir en silencio. `--pay-ok`/
`--pay-warn`/`--pay-danger` se alinean a los mismos tonos que usa
`.badge-erp-success/-warning/-danger` (`shared-components.css`) para que el significado
cromático (descuento/recargo/inactivo) sea idéntico al resto del ERP. Los hardcodes sueltos
que no pasaban por variable (`#f5c518`, `#0f172a`, `#fff`, `#0a1322`/`#f8fafc` en
`.pay-field`) se reemplazan por sus tokens equivalentes. Se retira además el hack
`margin: -1rem` de `.payments-page` (cancelaba el padding del `_Layout` solo en mobile,
dejando un residuo inconsistente en tablet/desktop) — la pantalla ahora vive dentro del
padding estándar del layout, igual que el resto del ERP (`Venta/Index` no usa este hack).

Se mantiene como variante local deliberada (no deuda pendiente, decisión de diseño con
justificación real): las clases `.pay-badge-*`/`.pay-mini-button` en sí (no se renombran a
`.badge-erp-*`/`.btn-erp-*`) porque el sistema compartido está dimensionado para listados/
tablas (botones de 36-44px de alto) y esta pantalla necesita mayor densidad — acordeones
anidados con varias filas de tarjetas/planes por medio — que un botón de ese tamaño
degradaría; forzar el reemplazo cambiaría la densidad visual ya validada sin resolver
ningún riesgo real (el color ya deriva del mismo origen desde este mismo lote).
Sin cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend. Validado en
vivo con Playwright en 1440×900, 1280×720, 1024×900/720, 390×844 y 360×800 (Efectivo,
Crédito personal, Tarjeta crédito con Visa expandida) sin overflow horizontal, 0 errores/
warnings de consola; build 0/0.

Micro-lote 3 — a pedido explícito de cerrar también los hallazgos de prioridad baja del
diagnóstico original:

- **V3 (forma)** — `.payments-shell-card` pasa de radio propio (22px) + sombra grande
  (`0 28px 90px`) a radio alineado a `.hero-erp` (`shared-components.css`, 1.5rem) sin
  box-shadow — el resto del ERP distingue una superficie elevada con borde + fondo, no con
  sombra grande; la diferencia de luminosidad entre `--pay-shell`/`--pay-bg` (ya
  retokenizados en el micro-lote 2) alcanza para distinguir el shell del fondo;
- **U2** — cada fila de plan (`.plan-summary`) tenía un `<span class="pay-mini-button">
  Editar</span>` con apariencia de botón aislado cuando en realidad toda la fila (el
  `<summary>` completo) es el único control que abre el formulario — visualmente sugería
  que solo el botón reaccionaba. Se reemplaza por un chevron (`.plan-chevron`, mismo patrón
  que `.payment-card-chevron` ya usa el acordeón de tarjeta) que comunica "expandible" en
  vez de "acción aislada", sin cambiar el comportamiento real (clic en cualquier punto de
  la fila sigue abriendo el mismo form). Aplicado a los 3 lugares donde se repite el patrón
  (planes por tarjeta, planes generales en "opciones avanzadas", planes generales de medio
  simple). `.plan-actions` (CSS y su regla responsive) quedó sin consumidor y se eliminó;
- **U3** — la descripción de cada método, cuando `Descripcion` está vacía en la base
  (los 6 medios de seed), mostraba el mismo texto fijo idéntico
  ("Metodo global con historial preservado..."). Pasa a depender de qué admite
  realmente el medio (`permiteTarjetas`/`permitePlanesGlobales`, ya calculados): "administra
  tarjetas propias..." / "usa un plan general..." / "tiene configuracion propia — ver
  detalle mas abajo" para Crédito personal. Sin inventar dato de negocio nuevo, solo
  reflejando una condición que el propio código ya calculaba;
- **sidebar "METODOS"** — mismo hallazgo que U1 (micro-lote 1) pero en el nav lateral, no
  tocado en su momento: el link de Crédito personal mostraba "0 tarjetas" en el meta y "0"
  en el badge de conteo de planes, igual que un medio vacío completable. Ahora, solo para
  el caso donde el medio no admite ni tarjetas ni planes propios (hoy exclusivo de Crédito
  personal), el meta dice "Config. propia" y el badge muestra "—" con
  `title="Se administra en su propia pantalla"`; el resto de medios (que sí tienen "0
  tarjetas" como dato real, no engañoso) no cambia.

Sin cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend. Validado en
vivo con Playwright en 1440×900, 1024×900/720, 390×844 y 360×800 (Efectivo con plan
general expandido, Crédito personal, Tarjeta crédito con Visa y un plan expandido para
confirmar que el form de edición sigue abriendo igual) sin overflow horizontal, 0
errores/warnings de consola; build 0/0.

Con esto, ningún hallazgo del diagnóstico original (4 capas) queda sin cerrar o sin
justificación de diseño explícita.

Micro-lote 4 — bug reportado en vivo por el usuario tras el cierre: cambiar de medio en el
sidebar "METODOS" reiniciaba el scroll al tope en cada click. Causa raíz: el scroll real de
la pantalla vive en el contenedor `.overflow-y-auto` de `_Layout` (no en el `body`/
`window`), y cada link del sidebar navegaba a una URL nueva (`?medioId=X`) con recarga
completa de documento — cualquier navegación de documento reinicia ese contenedor, sin
relación con el resto de los cambios de esta pantalla (reproducido también contra la
versión previa a este lote). Fix (a pedido explícito del usuario, opción "sin recargar la
página" sobre la alternativa de solo aterrizar en el panel): los links del sidebar de
métodos pasan a interceptarse por JS — `fetch` de la misma URL, reemplazo del `<nav>` de
métodos y de `.payments-main` vía `DOMParser`, `history.pushState`/`popstate` para que
atrás/adelante sigan funcionando, foco movido al link activo tras el reemplazo. Como el
documento nunca se recarga, el contenedor de scroll no se toca. Progressive enhancement:
los `<a href>` siguen siendo reales — si el `fetch` falla, cae a navegación normal
(`window.location.href`). El spinner de envío de formularios (`@section Scripts`) pasa de
bindearse por-formulario a delegación de eventos en el contenedor raíz, para seguir
funcionando en los forms que trae cada reemplazo AJAX sin necesitar re-bind. Los ~10
formularios de guardar/editar/cambiar-estado siguen siendo POST + redirect real (no se
tocaron, fuera del alcance reportado) — sí reinician scroll al guardar, pero eso ya
volvía al mismo medio en el que se estaba, no "a otra sección" como el bug reportado.
Validado en vivo con Playwright en 1440×900 y 390×844: cambio de sección preserva
scrollTop exacto (probado en ambos anchos), botón atrás del navegador re-sincroniza el
panel y el sidebar correctamente, un submit real (Editar método → Guardar) sigue
funcionando end-to-end con toast de éxito tras el reemplazo AJAX, sin overflow horizontal,
0 errores/warnings de consola; build 0/0.

- COTIZACION-MOBILE-01 (banda angosta, <38rem de contenedor: 390/360px, standalone y
  embebido en Venta/Create — a pedido explícito): scroll total de 2951px → ~1920px con
  producto + cliente + simulación (390×844). El `.workspace` pasa de grid a flex en una
  columna (mismo apilado) para que la franja Totales + "Simular/Actualizar cotización"
  quede **sticky abajo** (compacta: sólo "Total base" + el botón; Subtotal y Descuento ya
  están en Productos y en los propios campos); Descuento %/$ y Anticipo/Válida hasta
  vuelven a ir de a dos (bloque ~450px → ~300px); productos agregados con objetivos
  táctiles de 40px (eran 24–28px) y descuentos por ítem como campos con borde; comparador:
  los medios con planes **arrancan colapsados** en banda angosta (~1400px de filas de plan;
  nunca el grupo con la opción elegida o la de mejor precio; se abren al tocar, mismo
  handler), las filas de plan pierden la celda "Plan" (repetía "N cuotas") y dejan
  nombre+total / cuota+Elegir en dos líneas ordenadas, y la fila padre pierde el "—" y el
  botón "Ver planes" (el chevron ya lo comunica); textos `text-[10px]` generados por JS a
  12px y campos a 16px. JS: sólo clases semánticas (`rt-plan`, `rt-cuota`, `is-empty`),
  `esBandaAngosta()` y el colapso inicial en `appendGroup`. Sin cambios de cálculo,
  endpoints, ids ni contratos; banda de 2 columnas y desktop sin cambios.
  Corrección posterior (VENTA-WIZARD-TABLET-01): embebido en Venta/Create la barra sticky
  flotaba ~104px sobre el borde inferior — `_Layout` trae `padding-bottom: 6rem` inline en
  el scroller y un sticky se ancla a la caja de contenido —, mientras que standalone
  (`/Cotizacion`, sin ese padding) quedaba bien a 8px. Se compensa sólo para el host
  embebido, en `venta-page-wizard.css`.

- reapertura COTIZACION-MOCKUP-01 (Venta/Create paso Cotizar + Cotizacion/Simular), a pedido
  explícito del usuario con un mockup como referencia visual autorizada
  (`templates/Nueva Venta — Cotizar (completa)-html/Main.dc.html`, viewport 1440 sin sidebar):
  "readaptarlo para que sea exacto". Alcance visual: no se tocaron reglas de negocio, cálculos,
  permisos, endpoints ni contratos backend; ids y `data-*` intactos.
  - **Sistema visual**: paleta grafito del mockup como tokens `--cz-*` locales de `.cotz-app`
    (card `#10151b`, bloque `#171e26`, líneas `#232b34/#2c3644`, texto `#f1f4f8/#b8c2cc/#8993a1/
    #62707e`, acento `#5b8def`), radios 16/14/12, paddings 22-24px, escala tipográfica 10–15px,
    importes proporcionales con cifras tabulares (antes monoespaciados) y pegados al símbolo
    (`$180.758,90`, sólo presentación en el cotizador — `TheBury.formatCurrency` global intacto).
    Los inputs toman el tono opuesto al de su contenedor (`--cz-field-bg`).
  - **Estructura**: Productos | Cliente en 2 columnas iguales; línea de producto en una fila
    (nombre, CANT., DESCUENTO con selector %/$, precio, ×) + Subtotal; ficha de cliente con avatar
    de acento; **aptitud crediticia como card propia** (eyebrow + pill de estado + "Ver
    situación"); Condiciones como card elevada de grilla 2×2 igual; franja de totales con los
    **cinco datos siempre** (Subtotal · Descuento · Total productos · Envío · Total a cobrar) y
    CTA primario "Actualizar cotización"; comparador con **cada medio de pago como bloque
    redondeado** (cabecera con chevron + tile de color por medio, planes colgando, barra lateral
    verde en el más barato/seleccionado, "Seleccionado" sólido de acento); **barra "Opción
    seleccionada" como card propia** con Guardar / Continuar con wizard / Confirmar Mi Venta.
  - **Chrome del wizard (sólo paso Cotizar, ≥1280px)**: breadcrumb + Cancelar en una fila, título
    22px, stepper de pastillas numeradas con conectores y "Incompleta", todo sin card y cerrado
    por un divisor. Se anula el `px-8` propio de la página en ese paso para llegar al gutter de 32px
    del mockup.
  - **Responsive**: la grilla de 7 columnas y las 2 columnas de Productos|Cliente rigen desde
    60rem de contenedor (medido: por debajo la cifra del Total final y "Solicitar excepción"
    se encimaban); entre 38 y 60rem las filas de la tabla pasan al reflow de dos líneas (con
    Recargo visible); por debajo de 38rem se conserva el reflow y la barra sticky de mobile.
    La línea de producto se apila por debajo de 78rem de contenedor.
  - **Decisiones de adaptación (no copia literal)**: (1) acento del wizard = oro del ERP y del
    cotizador = azul del mockup (`--cz-accent`; el stepper es del wizard completo y no debe
    cambiar de color entre pasos); (2) tipografía Inter (el mockup usa Manrope, que el ERP no
    carga — no se agregó dependencia); (3) "Datos de contacto libres" del mockup dibuja
    Teléfono/Email/Dirección con cliente seleccionado, pero `Cotizacion` sólo persiste nombre y
    teléfono libres y sólo aplican sin cliente de sistema: se conservan esos dos campos, visibles
    únicamente sin cliente; (4) el descuento por línea del mockup es un único campo con selector
    %/$: los dos `<input>` reales siguen en el DOM y alternar sólo decide cuál se ve (un descuento
    cargado en el otro modo no se pierde; queda un punto ámbar en su botón).
  - **Criterios previos superados por la referencia** (tests de contrato actualizados):
    "Simular" ya no se degrada a `.btn-soft` con simulación vigente; recargo 0% pasa de neutro a
    verde; Envío/Total a cobrar dejan de esconderse sin envío; la compactación del hero a 1080p
    de VENTA-COTIZACION-REWORK-03 rige sólo por debajo de 1280px (con la geometría del mockup la
    primera alternativa de pago queda bajo el pliegue a 1080p).

- COTIZACION-MOCKUP-02 (resto de las pantallas de Venta al formato del mockup), a pedido explícito
  del usuario ("readaptá el resto de las ventanas de venta para que se acoplen al nuevo formato").
  El mockup sólo dibuja el paso Cotizar; el resto se **deriva de su lenguaje visual** (no hay
  referencia por pantalla). Sin cambios de reglas de negocio, permisos, endpoints ni ids/`data-*`.
  - **Pendientes de MOCKUP-01 resueltos**: el drawer "Detalle del plan" salía a ancho completo
    porque `sm:w-105` no está en el Tailwind precompilado (ahora 420px desde 640px, superficies
    grafito) y los nombres de medio de pago que sólo difieren del canónico en mayúsculas/tildes
    ("tarjeta credito") muestran el canónico ("Tarjeta crédito"); cualquier otro nombre
    configurado se respeta.
  - **Wizard Create + Edit, los seis pasos** (`venta-page-wizard.css`): tokens `--venta-wizard-*`
    a la escala grafito; el hero del mockup (breadcrumb + acciones, título 22px, stepper con
    conectores, "Incompleta", divisor) deja de ser exclusivo de Cotizar y rige en todos los pasos
    y en Edit desde 1280px — el stepper no cambia de aspecto ni de ancho al navegar (el tope de
    1400px se libera para los seis pasos por el mismo motivo); cards de 16px/20px de aire sin
    sombra, títulos de 15px, chips neutros, campos de 40px con foco azul, resumen lateral
    grafito con CTA de 44px (oro del ERP), ficha de cliente neutra con "Cambiar" en azul (no rojo:
    no es destructivo). El número estático de cada card (1…5) no seguía al stepper (Cliente es el
    2 en Create; Revisión repetía el 5): se retira (`.vm-step-bubble`), el número real lo lleva el
    stepper. Filtros de Productos con `auto-fit` (Min/Max quedaban en ~50px a 1280–1440px).
  - **Details y pantallas auxiliares** (`venta-module.css`): tokens `--vm-*` a grafito, remap de
    utilitarios slate de Tailwind a la escala grafito (alcance: wizard, Details, Cancelar,
    Autorizar, Rechazar, Eliminar y el panel Facturar), cards/métricas/botones/badges de los
    componentes globales (`.card-erp-*`, `.btn-erp-*`, `.badge-erp-neutral`) pintados con las
    superficies del mockup **sólo dentro de estas pantallas**; el hero de Details adopta el mismo
    patrón (breadcrumb + Volver/Imprimir en la primera fila, título + estado, descripción, chips,
    KPI, divisor) desde 1280px con hooks `venta-hero-*`. Facturar carga ahora el partial de
    estilos del módulo.
  - **Index** (`ventas-index.css`): tokens y literales azulados a grafito; sin cambios de layout.
  - **Cierre de pendientes (segunda ronda)**: la tabla "Detalle de productos" de Details cortaba
    "Subtotal final" a 1440–1688px (región de 832px contra 896px de `--oc-scroll-min-width`). La
    columna lateral pasa a ancho casi fijo (`clamp(18rem, 20vw, 24rem)`, hook `venta-details-grid`),
    los importes no se parten en dos líneas, las celdas se compactan, el piso baja a 36rem y se
    anula la reserva de 10px de `scrollbar-gutter` del componente en esta tabla: entera de 1280 a
    1688px sin scroll; a 1024px conserva el scroll horizontal, ahora con su aviso visible también
    en desktop (antes `lg:hidden`). Los nombres de "Forma de pago" del wizard (Create y Edit)
    salían de la configuración global en minúscula ("credito personal"): `aplicarMediosGlobales
    AlSelector` (`venta-create.js`) conserva el nombre canónico que Razor ya renderiza por tipo de
    pago cuando el configurado sólo difiere en mayúsculas/tildes; un nombre realmente distinto
    ("debito", "medio digital") se respeta.
  - **Fuera de alcance**: `ComprobanteFactura` (documento imprimible, no una ventana de trabajo) y
    los componentes globales del ERP fuera de Venta (siguen con su paleta).
  - **Verificación**: capturas reales en 1688 (1440 de contenido), 1280, 1024, 768 y 390 de
    Create (seis pasos, con y sin Crédito personal), Edit, Details ×3 estados, Cancelar, Eliminar,
    Facturar e Index: 0 overflow horizontal, 0 errores de consola. E2E `venta-wizard-accessibility`
    (9 viewports), `venta-cotizar-step`, `cotizacion-simulador`, `cotizacion-continuar-wizard`
    en verde. `venta-pago-por-item` no es ejecutable: prueba el pago por ítem
    (`.btn-configurar-pago-item`, `#modal-pago-item`), que ya no existe en ninguna vista ni script
    (retirado en "Aislar pago por producto y restaurar pago general en ventas"); spec obsoleta,
    candidata a eliminarse. Tests C# de Venta/Cotización: 1481/1489, los 8 rojos son anteriores a
    esta tarea (los fragmentos que buscan tampoco están en HEAD): "Guardar cambios" de Edit,
    panel de diagnóstico y razones de autorización/justificación en Details.

## Dashboard / Index — foundation aplicada (header + tabs accesibles)

A partir de un pedido del usuario de adoptar como estándar visual ERP-wide una imagen de
referencia (sidebar oscuro + KPIs + filtros + tabla), se auditó Dashboard como candidato:
ya usa el sistema canónico compartido en su mayoría (`card-erp-metric` para el row de 4
KPIs, `card-erp-panel` para las secciones) — el único gap real contra `ERP-UI-STANDARD.md`
§4 era la ausencia de header de página. Se agregó `<h1>Dashboard</h1>` + subtítulo, sin
breadcrumb (Dashboard es la raíz de navegación — `Home/Index` redirige acá, un breadcrumb
de un solo nivel no aporta) ni acciones primarias nuevas (ya las cubre "Accesos rápidos"
del sidebar, sin duplicar autoridad). Validado en vivo con Playwright (instancia propia)
en 1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800, sin overflow horizontal en
ningún viewport, 0 errores de consola; build 0/0.

**Segunda pasada (misma sesión, auditoría de 4 capas contra `ERP-UI-STANDARD.md` con
gate previo al usuario):**

- *Header* (§4, bajo riesgo): el `<h1>`/subtítulo existente se envolvió en `.hero-erp`, el
  contenedor canónico que ya usan ~12 pantallas cerradas (referencia:
  `Views/AlertaStock/Index_tw.cshtml`) — las clases de texto ya coincidían, solo faltaba el
  contenedor boxed. Sin breadcrumb ni acciones nuevas, sin cambio de contenido.
- *Tabs "Vencidas"/"Próximas"* (§5/§11, antes solo `aria-pressed` sin semántica de tabs):
  se llevó al patrón ARIA completo tomando como referencia la implementación ya resuelta en
  `Views/Venta/Index_tw.cshtml` / `wwwroot/js/venta-index-rework.js` — contenedor
  `role="tablist"` con `aria-label`, botones `role="tab"` + `aria-selected` +
  `aria-controls` apuntando al `<tbody>` correspondiente, los `<tbody id="tbodyVencidas"/
  "tbodyProximas">` pasan a `role="tabpanel"` + `aria-labelledby`, roving `tabindex`
  (0 en la tab activa, -1 en la inactiva) y navegación por teclado
  (`ArrowLeft`/`ArrowRight` circular, `Home`, `End`) agregada en `wwwroot/js/dashboard-index.js`.
  Mismo contenido, misma lógica de datos y mismo click-handler conceptual — solo se
  reemplazó `aria-pressed` por `aria-selected` y se sumó el manejo de teclado; sin cambios
  visuales.
- *Fuera de alcance, documentado como hallazgo transversal, no corregido*: los wrappers
  `data-oc-scroll-region` de las tablas (Dashboard y otras pantallas) no tienen
  `role="region"` + `aria-label` propio (exigido por §6) — verificado que esto también
  falta en pantallas ya cerradas como `AlertaStock/Index_tw.cshtml`, por lo que es un gap
  del patrón compartido `horizontal-scroll-affordance`, no algo propio de Dashboard;
  corregirlo solo acá generaría inconsistencia con el resto del ERP. Candidato a una tarea
  aparte sobre el patrón compartido.

Validado en vivo con Playwright (instancia propia, puerto aislado): los 6 viewports
(1440/1280/1024/768/390/360) sin overflow horizontal; QA de teclado específico sobre las
tabs (foco inicial, `ArrowRight` mueve foco y activa el panel, `Home`/`End` van al primer/
último tab) confirmado vía inspección de `aria-selected`/`tabIndex`/panel oculto en cada
paso; 0 errores/warnings de consola en toda la sesión; build 0 warnings/0 errores;
57/57 tests de `LayoutUiContractTests` (no hay contrato UI dedicado a `Dashboard/Index`
todavía). Resto de la pantalla (contenido de la tabla de cobranzas, alertas de stock,
productos destacados, sidebar de accesos/notas/actividad) sigue sin auditoría de 4 capas
completa — no se marca "✅ Cerrado". Sin commit, sin push (pendiente autorización).

### Dashboard / Index — `/ui-module dashboard` (2026-09-24, sin commit)

Pipeline completo (ux-heuristics + Impeccable critique/polish + `modulo-ui-refactor`) sobre el
WIP previo del working tree (header con estado del día, estados vacíos, KPIs con container
queries), sin revertirlo. Impeccable critique corrió en modo degradado (un solo contexto).

- *Flujo/UX:* "Accesos rápidos", "Reponer", "Ver crédito" y "Ver catálogo" ahora respetan los
  permisos del destino (`ventas.create`, `clientes.create`, `reportes.view`, `productos.view`,
  `caja.view`, `creditos.view`, `ordenescompra.create`); antes llevaban a un 403. Bug del
  vacío de "Próximas" (evaluaba `cuotasVencidas.Count`) corregido. Las tabs muestran su
  contador (se quitó el texto "N vencidas · M próximas" duplicado) y una nota "Mostrando las 5
  más antiguas de N" + "Ver créditos" avisa que la lista está acotada. Notas rápidas aclaran
  "Solo en este navegador" (localStorage).
- *Visual:* el `<h1>` queda `sr-only` (la barra global ya muestra el título, §4); se eliminó
  el panel dentro de panel (`.dashboard-table-wrap`) y la franja vacía del hint; sin
  encabezados de tabla sobre un estado vacío; pill de stock sin salto de línea.
- *Responsive:* la columna lateral pasó de ≥1024 a ≥1280px (a 1024 dejaba ~320px de contenido y
  recortaba KPIs/tabs/botones — el chequeo de overflow no lo detectaba); KPIs por container
  query (1/2/4 columnas según el ancho real); en teléfono los KPIs son filas compactas
  (~296px en vez de ~500px); accesos 3×2 entre 640 y 1279px.
- *A11y:* regiones de scroll con `role="region"` + `aria-label`.
- *Validado en vivo* (Playwright, app propia en :18787) a 1920/1440/1280/1024/768/390/360: sin
  overflow, sin recortes, 0 errores/requests fallidos; tabs con mouse y teclado. Build 0 errores.
- *Sin verificar:* la BD dev no tiene cuotas, así que el estado con filas (tabla llena, nota de
  truncado renderizada por servidor, "Ver crédito") no se vio real; el toggle JS de la nota se
  probó con markup simulado. Tampoco se probó con un rol de permisos restringidos.
- *Deuda documentada:* Accesos rápidos queda al final en mobile (reordenar por CSS rompería el
  orden de foco; el menú cubre las rutas); el chip de sección (COBRANZAS/INVENTARIO/DESTACADOS)
  es identidad transversal (25 vistas), no se tocó; el gating de "Ver alertas" conserva su
  lógica previa (`cotizaciones.view`, distinta de `productos.view` del Catálogo).

### Saneamiento pre-PR (2026-09-24, rama `saneamiento-cambios-pendientes-20260924`)

Re-validación con datos reales en un clon de la LocalDB (`_qa`, con un crédito de 12 cuotas y una
venta pendiente de autorización sembrados solo en el clon) y Chrome/Playwright propio, a
1440/1366/1024/768/390/360. Impeccable `critique` corrió con dos sub-agentes aislados (A: diseño,
B: detector + navegador) y luego `polish`; el detector devolvió 0 hallazgos sobre Dashboard y Venta.

- *Corregido (contratos descubiertos):*
  - Dashboard: la tabla "Cuotas por cobrar" (mín. 47,25 rem) recortaba la columna Acciones a 1440px
    con sidebar; ahora 40 rem con `px-4`. El DNI ya no viaja incrustado en `ClienteNombre`
    (`CuotaVencidaDto`/`CuotaProximaVencerDto.ClienteDocumento`), misma causa raíz que H11.
  - Venta wizard, mobile: la regla que oculta las descripciones del encabezado de paso
    (`.venta-section > .mb-6.flex p`) también alcanzaba al dropdown del buscador de productos y
    ocultaba nombre/código/descripción (`display:none`); ahora se limita a `.mb-6.flex.items-start`
    (test de contrato en `VentaCreateUiContractTests`).
  - Venta/Details, Historial: `size-4.5` y `before:left-2.25` no existen en el Tailwind
    precompilado (punto de 4px y línea sobre el texto); pasan a `.venta-timeline` en `venta-module.css`.
  - Venta/Index: las acciones de la tabla se alinean al inicio (2 vs 3 acciones desalineaban el primer ícono).
  - Etiquetas accesibles en los buscadores del cotizador y en Observaciones del wizard.
- *Validado:* Dashboard (KPIs, tabs Vencidas/Próximas con mouse y teclado, stock bajo KPI=tabla,
  accesos, notas, actividad); Venta Index/Details (cotización y confirmada)/Create hasta el paso
  Revisión con el CTA "Crear venta"/Edit/Facturar/Cancelar/Autorizar/Rechazar (hasta el envío del
  formulario, sin ejecutar acciones destructivas); panel de filtros compartido en OrdenCompra y
  Caja/Historial (mouse, Enter, Espacio; Escape no está implementado); smoke de paleta en Caja,
  Crédito, Catálogo, Cliente, Orden de Compra, MercadoLibre. 0 errores de consola / requests fallidos.
- *NO validado:* Dashboard con un rol de permisos restringidos; Details de una venta Facturada con
  crédito; Delete de Venta (destructivo); Login/Identity con sesión cerrada solo se capturó.
- *Deuda conocida (no introducida por esta rama):* contraste de textos `text-slate-500` pequeños
  (~3,3:1) por el mapeo de paleta; tap targets <44px en `btn-xs` de tarjetas mobile de Venta/Index;
  el POST de "Autorizar" sin motivo muestra el error antes de que la persona escriba nada;
  `Credito/Index` tiene el título alineado a la derecha (`.credito-index-head {justify-content:flex-end}`);
  los `h1` del layout ("TheBuryProject") coexisten con el título de página.

### Dashboard / Index — cierre de módulo `/ui-module dashboard` (2026-09-24, sin commit)

Pipeline completo sobre clon de la LocalDB (`_qa`, crédito de 12 cuotas sembrado solo en el clon) y
Chrome/Playwright propio; roles admin, contador y repositor. Impeccable `critique` con dos
sub-agentes aislados (A diseño 25/40, B detector: 0 hallazgos) y `polish`.

- *Corregido:* a 1440px el importe de la cuota se partía en "$ / 10.000,00" y las filas medían ~112px;
  a 1280px la tabla recortaba Acciones. La columna Estado se fusionó en Vencimiento (Vencidas:
  "Hace N d" con "Vencida" para lectores de pantalla; Próximas: fecha + "Por vencer/Programado"),
  celdas `px-3 py-3`, importe y nº de crédito sin salto (filas ~65px, sin scroll horizontal a 1280).
- *Permisos:* la columna Acciones ya no se dibuja si el rol no tiene ninguna acción; en Vencidas, sin
  `cobranzas.payinstallment` pero con `creditos.view`, se ofrece "Ver crédito". "Ver créditos" con área táctil ≥40px.
- *Validado:* 1440/1280/1024/768/390/360 (sin overflow, 0 errores de consola), tabs con mouse y teclado,
  estado vacío/lleno, roles admin/contador/repositor, 488/488 tests de Dashboard/UiContract.
- *Cierre de pendientes (2026-09-25, por pedido del usuario):* cada bloque financiero exige el permiso de su
  módulo (Ventas del mes y actividad de ventas → `ventas.view`; Cobranza, Monto vencido, "Cuotas por cobrar"
  y estado de cuotas → `creditos.view`; actividad de clientes → `clientes.view`); Repositor ya no ve montos ni
  datos de clientes. KPIs con `auto-fit` (los visibles reparten el ancho) y el de cuotas vencidas encabeza la fila;
  columna Acciones de "Alertas de stock" solo con `ordenescompra.create`; notas más bajas; `h1` de marca del
  layout → `<p>` (un único `h1` por página); DNI/ID sin salto a 1280px sin scroll. Gating solo de vista: el
  servicio sigue calculando todo (el gate del controller sigue siendo `dashboard.view`).
- *Deuda menor restante:* ninguna conocida del módulo.

## Backlog transversal

**P2 — horizontal-scroll-affordance**

El fade derecho del scroll horizontal (componente compartido, `data-oc-scroll-fade`)
puede quedar levemente visible al alcanzar el final del scroll en anchos intermedios
(~768px / 1024px). No bloquea `Venta/Index`. No resuelto todavía.

## Venta + Cotización / Envío a domicilio — cerrado (serie ENVIO-ML)

Extensión aditiva sobre las 5 pantallas ya cerradas de la tabla (Venta Index/Create/Edit/
Details, Cotización Simular): no se rediseñó nada existente, se agregó el concepto de
envío que antes no existía en el módulo de Venta (solo existía para MercadoLibre, dominio
no reutilizable).

- Modelo: `VentaEnvio` 1:1 con `Venta` (mismo patrón que `DatosCheque`), 7 estados
  (Pendiente→Preparando→Despachado→EnCamino→Entregado, +Fallido/Cancelado) con máquina de
  transición fail-closed en `VentaEnvioService`. El costo de envío es puramente
  informativo — no toca Total/IVA/caja/crédito/factura.
- Simular (`_CotizadorForm.cshtml`): checkbox "Esta venta tiene envío a domicilio". Al
  guardar y convertir a venta, si estaba tildado se crea el `VentaEnvio` Pendiente
  precargado con el domicilio del cliente.
- Wizard (`Create`/`Edit`): paso "Envío" siempre visible entre Pago y Revisión (a
  diferencia de Crédito, que se oculta condicionalmente), con checkbox interno que
  muestra/oculta el formulario de datos de entrega y botón "Usar domicilio del cliente".
- Details: card "Envío" + acción "Actualizar Estado de Envío" (modal con `<select>` que
  solo ofrece las transiciones válidas desde el estado actual).
- Index: quinto tab "Envíos pendientes" con badge de conteo, derivado en memoria del
  conjunto ya filtrado (mismo patrón que "Cotizaciones y presupuestos"/"Devoluciones"),
  sin acción de controller nueva.

**Validación (4 capas):**

- *Técnica*: `dotnet build` 0 warnings/0 errores; suite completa 4798 tests, 0 regresiones
  propias (10 rojos preexistentes sin relación); tests nuevos dedicados
  (`VentaEnvioServiceTests` sobre la máquina de estados y "no toca totales",
  `VentaEnvioUiContractTests` sobre los hooks DOM/JS, casos en
  `CotizacionConversionServiceTests`).
- *Visual y flujo UX*: recorrido real end-to-end contra una instancia propia recién
  compilada (build aislado, sin tocar la app del usuario) — Simular con envío → Guardar
  (convierte directo a `Venta/Edit`) → paso Envío prellenado con destinatario/domicilio/
  teléfono del cliente → Details muestra la card y el botón de acción → modal cambia el
  estado (Pendiente→Preparando) → aparece en "Envíos pendientes" del Index → al pasar a un
  estado terminal (Cancelado) desaparece de esa lista. Sin errores de consola nuevos.
- *Estados reales*: verificado sobre una venta real creada en el flujo (no datos
  mockeados), con el `<select>` del modal ofreciendo únicamente las transiciones válidas
  para cada estado real de la fila.
- *Responsive*: barrido en los 7 viewports de la matriz mínima (1440×900, 1280×720,
  1024×720, 900×720, 768×1024, 390×844, 360×800) sobre las 3 pantallas nuevas/tocadas
  (wizard paso Envío, Details, Index tab Envíos pendientes) — sin overflow horizontal de
  página en ninguno. La barra de tabs del wizard y del Index reutiliza el scroll
  horizontal de pills ya existente en el resto de la app (no es una regresión de esta
  serie).

`EstadoVenta` no se sincroniza automáticamente con el estado del envío (decisión
deliberada: son dos máquinas de estado independientes; queda como posible extensión
futura a validar con el usuario, no como pendiente de esta serie).

### Envío cobrable: Total a cobrar ≠ Total facturable (VENTA-ENVIO-TOTAL-01)

Reemplaza la decisión "el costo de envío es sólo informativo" de esta serie: el importe de
envío (`VentaEnvio.CostoEnvio`, campo único, sin duplicar) es un concepto **separado** que el
cliente paga. `Venta.Total` sigue siendo el total de productos; el envío no entra en IVA,
precio unitario, recargos del medio de pago, crédito ni comprobante. Fórmula única en backend
(`Helpers/VentaMontos`): `TotalACobrar = Total + ImporteEnvio`; `TotalFacturable = Total`
(el envío no está en ningún cálculo fiscal del sistema — sin línea, alícuota ni configuración —
y no se cambió sin evidencia; la diferencia queda explícita en UI).

- Cotizador (`Simular`): la franja de totales pasa a Subtotal · Descuento · Total productos ·
  Envío · **TOTAL A COBRAR** cuando hay envío guardado (sin envío queda idéntica); el resumen de
  la opción elegida y el modal de "Confirmar Mi Venta" separan Productos / Envío / Total a
  cobrar; el importe viaja con la cotización (`Cotizacion.CostoEnvio`, migración
  `AddCotizacionCostoEnvio`) y sobrevive a "Pasar a venta". Cotización/Detalles muestra Envío y
  Total a cobrar.
- Wizard (`Create`/`Edit`): paso Envío sin "informativo"; Revisión muestra Total productos /
  Envío / **Total a cobrar** (dominante) y la barra sticky mobile dice "A cobrar"; Crédito
  Personal aclara que el envío no se financia.
- `Venta/Details`: el resumen superior separa Productos / Envío / Total a cobrar; la card Envío
  dice "Costo de envío"; la card Facturación explica que el comprobante no incluye el envío.
- Facturar (modal de Details, página `Facturar_tw` y partial compartido con el Cotizador):
  bloque "Resumen comercial" (Productos · Envío · Total a cobrar) por encima del comprobante.
- Caja: `ConfirmarVentaAsync` registra **un** ingreso por `TotalACobrar` (la reversión por
  cancelación espeja ese ingreso e incluye el envío); Ventas del turno muestra Productos y
  Envío dentro de la celda Total; Vendido/Cobrado/Pendiente/Caja esperada usan lo realmente
  cobrado. Crédito Personal/Cuenta corriente: sin cambios (el envío no se financia; queda como
  pendiente de cobro explícito).

**Validación (4 capas):** *Técnica*: tests nuevos `VentaEnvioTotalACobrarTests` (fórmula,
persistencia tras recargar, Caja Efectivo/Transferencia/Tarjeta/MP, pago parcial, sin envío, envío
cero, crédito personal, cancelación, guard de edición), casos de Cotización/Conversión/Confirmar y
contrato UI. *Visual y flujo UX*: recorrido real Cotizador → Confirmar Mi Venta → Details → Caja →
Facturar, más Cotizador → wizard (Revisión) con instancia propia. *Estados reales*: venta
confirmada real ($180.758,90 + $1.000,00 → Caja +$181.758,90) y venta legacy con envío
"informativo" que ahora figura con $1.000,00 pendiente. *Responsive*: 7 viewports (1920×1080 a
360×800) × Cotizador, Revisión, Details, Caja Resumen/Ventas y modal Facturar, 0 overflow.

## Cliente / Nuevo cliente (drawer Create) — cerrado

Alcance estrictamente acotado al drawer de alta (`data-cliente-wizard="create"`, dentro
de `_ClienteModal.cshtml` dentro de `Cliente/Index_tw.cshtml`). Implementado en 3 lotes
(navegación real → UX contextual → responsive y acabado final), auditado y autorizado
por el usuario entre cada lote. No tocado: drawer Edit (sigue con su comportamiento
legacy de formulario con tabs, sin progresión ni bloqueo de pasos), `/Cliente/Create`
página completa (`Create_tw.cshtml`), `/Cliente/Edit/{id}` página completa
(`Edit_tw.cshtml`), reglas de negocio, modelos, validadores, permisos.

Resumen no cronológico de lo que quedó implementado:

- wizard real de 5 pasos navegables — Personales, Cónyuge, Contacto, Laboral,
  Crédito — con progresión, `Anterior`/`Siguiente` y `Crear cliente` disponible
  únicamente en el paso final (nunca se envía el form desde un paso intermedio);
- Cónyuge condicional: se marca "No aplica" (estado propio `skip`, no reutiliza
  pending/complete/error) según el Estado civil elegido en Personales, en vez de
  forzar completarlo siempre;
- Referencias queda fuera del flujo navegable de 5 pasos (no es un paso del wizard,
  texto técnico limpiado);
- validación por paso: `Siguiente` no avanza con campos obligatorios del paso actual
  incompletos, reutilizando las reglas `[Required]` del ViewModel (`asp-validation-for`
  junto con `_ValidationScriptsPartial`) en vez de duplicarlas a mano en JS; el primer
  campo con error recibe foco y `scrollIntoView`;
- estados de paso claramente distinguibles vía `data-step-state`: `pending`, `current`,
  `complete`, `error`, `skip` (Cónyuge no aplicable) — cada uno con su propio color de
  ícono/texto, sin reutilizar el semáforo de otro estado para significar algo distinto;
- bloqueo de pasos futuros inválidos: solo se puede saltar por click directo a un paso
  ya alcanzado/permitido, nunca a uno posterior sin completar los anteriores;
- resumen lateral dinámico (desktop: columna lateral; tablet/mobile: se apila debajo del
  formulario vía el mismo breakpoint `lg:` de Tailwind que ya gobierna el grid, sin
  breakpoint nuevo) — iniciales, nombre, documento, contacto (oculto por completo si no
  hay teléfono ni email cargados, sin fila vacía), estado, aptitud, límite; sin datos
  ficticios ni duplicados;
- accesibilidad: `aria-current="step"` en el tab activo, `aria-invalid`/
  `aria-describedby` en campos con error, anuncio de "Paso X de 5" vía `aria-live`,
  navegación con teclado completa, foco visible; el texto de cada tab vive en un
  `<span class="tab-label">` propio para poder ocultarlo visualmente sin sacarlo del
  árbol de accesibilidad (ver punto de mobile abajo);
- barra de pasos sin truncar nombres ("Crédito" completo) en desktop/tablet; el tab
  activo se mantiene visible dentro de la barra horizontalmente scrolleable vía
  `scrollIntoView({ block: 'nearest', inline: 'nearest' })` en cada cambio de paso —
  antes, en anchos como 1024px, "Crédito" podía quedar totalmente fuera de vista sin
  aviso al llegar al último paso con `Siguiente`;
- mobile (≤640px): se suma un stepper compacto ("Paso X de 5" + ícono/nombre del paso
  actual + barra de progreso) **arriba** de la barra de 5 tabs, en vez de comprimir el
  texto hasta cortar palabras a la mitad. La barra de tabs no se oculta ni se reemplaza:
  su texto pasa a `sr-only` (mismo patrón ya usado en `cliente-module.css`, no
  `display:none`) y los tabs quedan ícono-only, pero siguen siendo del mismo tamaño y
  totalmente clickeables — "abrir manualmente un paso permitido" con un click directo
  en un tab funciona igual que en desktop. Decisión explícita: el mockup original del
  usuario sugería reemplazar la barra por completo; se corrigió a este diseño aditivo
  porque la sustitución total rompía 2 tests ya existentes que protegen esa capacidad;
- footer con las 3 combinaciones de botones según la posición del paso (inicial:
  `Cancelar` + `Siguiente`; intermedio: `Anterior` + `Siguiente`; último: `Anterior` +
  `Crear cliente`), sin `Cancelar` duplicado, targets táctiles adecuados en mobile;
- scroll: header fijo, contenido central scrolleable, footer fijo — sin doble scrollbar,
  sin inputs ocultos tras el footer;
- validado en los 7 viewports de la matriz mínima (1920/1440/1280/1024/768/390/360 —
  vía los 7 proyectos configurados en `playwright.config.js`): sin overflow horizontal
  inesperado, sin pasos truncados, footer usable, resumen bien posicionado en cada
  banda, navegación `Anterior`/`Siguiente` correcta, `Crear cliente` solo al final,
  consola limpia.

**Validación:** `dotnet build` 0 warnings/0 errores; `ClienteWizardUiContractTests.cs`
(16 tests, contrato sobre el markup real vía `File.ReadAllText`, sin renderizar Razor);
`e2e/cliente-wizard-nuevo.spec.js` (10 tests) verde en los 7 proyectos de Playwright
configurados (71/71 corridas); suite completa filtrada por `Cliente` 462/462 tests
(2 skipped ajenos), 0 rojos propios.

Deuda no bloqueante: el stepper compacto de mobile es aditivo (ícono-only + resumen
arriba) en vez de reemplazar la barra por completo, por la razón de compatibilidad
explicada arriba — es una decisión de diseño ya tomada, no una tarea pendiente. No se
tocó `/Cliente/Create` ni `/Cliente/Edit/{id}` página completa: si se quiere el mismo
wizard fuera del drawer, es un alcance nuevo a autorizar aparte.

## Cliente / Details — cerrado (serie CLIENTE-DETAILS-TABS)

Reorganización estructural/visual a pedido explícito del usuario (imagen de referencia +
spec funcional detallada, 2026-09-14): la ficha pasa de scroll único muy largo a
estructura híbrida — resumen ejecutivo y alerta principal siempre visibles arriba, resto
de la pantalla organizado en 5 solapas fijas. Sin cambios de backend, ViewModel, reglas
de negocio, cálculo de crédito, BCRA, permisos ni rutas.

- arriba de las solapas (siempre visible): identidad del cliente (avatar, nombre,
  chips de aptitud/estado, DNI/CUIL/edad, Imprimir/Editar perfil), resumen ejecutivo de
  4 KPIs (Crédito disponible, Estado de aptitud, Documentos, Situación BCRA) y la alerta
  principal de aptitud (única, con motivo condensado + `Recalcular aptitud`) — todo
  contenido ya existente, sin nuevas métricas ni reglas;
- solapas `[ Resumen | Crédito | Documentación | Datos | Historial ]` con patrón ARIA
  completo (`role="tablist/tab/tabpanel"`, `aria-selected`, `aria-controls`,
  `aria-labelledby`, roving tabindex, `ArrowLeft/ArrowRight/Home/End`) — mismo enfoque que
  `Views/Venta/Index_tw.cshtml` (`.tabs-scroll-shell`/`.tab-btn`) y el roving tabindex de
  `wwwroot/js/dashboard-index.js`; paneles 100% server-renderizados, mostrados/ocultados
  por JS (`cliente-details.js`, sin fetch adicional, sin SPA); Resumen es la solapa activa
  por defecto;
- **Persistencia de solapa (lote 10/10, 2026-09-14)**: la solapa activa se refleja en el
  hash de la URL (`#credito`, `#documentacion`, ...) vía `history.replaceState` (nunca
  `pushState`, para no ensuciar "Atrás"); al cargar, un hash inválido o ausente cae a
  Resumen; un `hashchange` (link a otra solapa sin recarga completa) también resincroniza.
  Las acciones que redirigen directo al `returnUrl` recibido (Verificar/Rechazar/Subir
  documento — `DocumentoClienteController` hace `LocalRedirect` literal a esa URL) reciben
  el hash de la solapa activa agregado al campo oculto antes de enviar el form, sin tocar
  ningún controller. Las acciones que redirigen vía `RedirectToAction(Details, ...)`
  (Recalcular aptitud/scoring, asignar/limpiar puntaje manual) arman una URL nueva por
  routing y no preservan el fragmento — quedan en Resumen tras usarse (deuda documentada,
  requeriría tocar esos controllers);
- **Resumen**: Ventas pendientes de autorización (movida acá desde su posición anterior
  como banner siempre visible — evita competir con la alerta principal), bloque "Crédito"
  condensado a chips (lote 10/10: ya no repite "Crédito disponible" como card grande
  idéntica al KPI superior — solo lo que el KPI no muestra: puntaje, mora, capital en
  mora), card BCRA completa (movida tal cual), Motivos de (no) aptitud (movida tal cual),
  Documentación condensada (solo ítems que requieren atención — faltantes/pendientes — sin
  las acciones de carga/revisión, que quedan en la solapa Documentación, con estado vacío
  honesto cuando no hay nada pendiente) y Garante (movido tal cual, con su modal);
- **Crédito**: card "Información crediticia" completa (desglose, puntaje, acciones
  Asignar puntaje manual/Límites, punitorio aplicado pendiente — movida tal cual),
  Scoring de comportamiento + Historial de puntaje (colapsables, sin cambios), Últimos
  créditos del cliente (tabla ya acotada a 5 + "Ver todos", sin cambios);
  ambas antes compartían fila de grid 50/50 con BCRA — al perder su acompañante pasan a
  ancho completo dentro de su solapa;
- **Documentación**: card completa (carga, verificar, rechazar, reemplazar) movida tal
  cual, sin recortes de funcionalidad;
- **Datos**: Datos personales + Contacto (movidas tal cual) y Zona sensible (movida al
  final, visualmente aislada con su borde rojo existente — decisión explícita del usuario
  de ubicarla acá en vez de al final de Resumen);
- **Historial**: nueva card mínima que reutiliza el mismo link ya existente a
  `Credito/Index` filtrado por cliente ("Ver todos los créditos (N)") — no se inventó una
  bitácora nueva; el historial de puntaje, al ya vivir como colapsable dentro de la card
  de Crédito, se mantuvo ahí (mismo criterio que la spec del usuario: "si ya existe
  colapsable, mantener ese comportamiento");
- mobile: barra de tabs con scroll horizontal interno + affordance de fade lateral
  (`data-oc-scroll`/`horizontal-scroll-affordance.js`, reutilizado del resto del ERP, no
  cargado antes en esta vista) — las 5 secciones siguen alcanzables y clickeables sin
  truncar texto ni ocultar funcionalidad; identificador de crédito (`CRE-202609-000123`)
  ya no se parte en varias líneas en la tabla de últimos créditos (lote 10/10: la celda
  mono queda `white-space:nowrap`, resuelto por el mismo scroll horizontal de la tabla);
- densidad (lote 10/10): `dl.kv` dentro de las solapas queda con `max-width:34rem` — en
  1920/2560px un par label/valor de una sola columna ya no se estira de punta a punta del
  monitor (shell sigue fluido, sin volver a `max-width:1120px`);
- BCRA (lote 10/10): un error de red/servidor al refrescar ya no pisa el último dato real
  conocido (`bcra-desc`) con un texto genérico — mensaje propio (`#bcra-error`) cerca del
  botón, reintentable con el mismo botón (queda habilitado de nuevo al terminar);
- CSS nuevo acotado a `.shell.cliente-details .tab-btn`/`.tabs-scroll` (scopeado a la
  ficha, mismo patrón de convergencia que ya usa Venta) — `.tab-panel` se reutilizó tal
  cual (ya existía en `cliente-module.css`, compartido con el wizard de Create/Edit).

Sin cambios de cálculo, reglas de negocio, ids, `data-*` ni contratos backend/ViewModel.
Riesgo real encontrado y corregido (lote inicial): `e2e/cliente-aptitud-punitorio.spec.js`
verificaba visibilidad del bloque de mora/punitorio asumiendo que vivía siempre visible;
se agregó un paso explícito de navegación a la solapa Crédito en los 4 tests que lo
requerían (los que solo verifican ausencia con `toHaveCount(0)` no lo necesitaban). Ese
spec requiere IDs de clientes sembrados por variables de entorno no disponibles en esta
sesión — se verificó sintácticamente (`node --check`) pero no se re-ejecutó contra los 9
escenarios sembrados.

Se validó en vivo con Playwright (instancia propia en :18787 y luego :5199, detenidas al
cerrar) sobre 2 clientes reales de la base de desarrollo en ambos lotes, incluida
navegación por teclado completa (`ArrowRight`/`Home`/`End`), persistencia de solapa por
reload real (`#credito` sobrevive a F5), fallback a Resumen ante hash inválido, y consola
limpia en 1920/1440/1280/1024/768/390/360. `ClienteDetailsTabsUiContractTests.cs` (17
tests) y `e2e/cliente-details-tabs.spec.js` (6 tests, no requiere seed especial), ambos
verdes; suite focalizada por `Cliente` 484/486 tests (2 skipped ajenos), 0 rojos propios;
build 0/0.

Deuda no resuelta en este lote (evaluada y descartada deliberadamente, no pendiente por
olvido): formato de DNI con puntos de miles — se dejó tal cual (dígitos sin separador) por
ser el mismo criterio usado sin excepción en `Cliente/Index`, `Cliente/Edit` y
`Cliente/Delete`; agregar puntos solo en Details introduciría la inconsistencia que se
buscaba evitar, no resolverla.

### Cliente / Details — auditoría `/ui-module` (2026-09-24)

Reabierta para este alcance (Index se trabajó antes; Create/Edit/drawer/Delete quedan
aparte). 4 capas con `ux-heuristics` + critique (un solo contexto), sobre la ficha real de
un cliente No apto sin cupo usado y BCRA en error, en las 5 solapas. Solo Razor/CSS/JS de
presentación; sin cambios de backend, ViewModel, reglas, permisos ni ids/`data-*`:

- **Jerga técnica al usuario (P1)**: el KPI y la card BCRA mostraban `Error API (400)`
  (texto interno del servicio, que además lo detecta por ese prefijo). Se muestra "No se
  pudo consultar" (vista y refresco JS de `cliente-details.js`); el dato persistido y el
  servicio no se tocan. La solapa Historial exponía una nota de desarrollo ("mismo acceso que
  ya ofrecía esta ficha"): reescrita.
- **Señales de color contradictorias (P2)**: el cupo disponible ($ 1.000.000.000, 100% libre)
  y su barra salían en rojo por la aptitud del cliente, mientras el KPI de arriba estaba en
  acento. El tono de esa card/barra sigue ahora al cupo (rojo solo si está agotado); la
  aptitud ya la comunican el chip del encabezado, el KPI y la alerta principal.
- **Microcopy (P2)**: ~30 textos visibles sin tilde ("Crédito disponible", "Situación BCRA",
  "Última consulta", "Requiere autorización", "Al día", "Teléfono", "Automático", etc.);
  4 aserciones de tests de contrato + 1 spec e2e actualizadas al texto nuevo. Placeholders
  `ph-label` que repetían el título del estado vacío, retirados. Sigue sin tilde
  "Monto maximo personalizado" (texto que arma `CreditoDisponibleService`, backend, fuera de
  alcance).
- **Equilibrio del layout (P2, desktop)**: la columna izquierda de Resumen quedaba ~750px
  vacía bajo Motivos. Garante pasa al final de esa columna. En <1024px las columnas se
  aplanan (`display:contents`) y Garante, la card menos urgente, cierra la lista (BCRA y
  Documentación antes).
- **Mobile (P2)**: los KPI de texto (Estado de aptitud, Situación BCRA) pueden bajar de línea
  (`.kpi-value--text`); con `nowrap` "No se pudo consultar" se cortaba.
- **Accesibilidad (P2)**: los botones de subir archivo por documento faltante eran solo un
  ícono cuyo nombre accesible era la palabra "upload"; ahora `aria-label`/`title` "Subir
  archivo de <documento>".

Validado en vivo (Playwright, :18787, solo lectura) en 1440, 1280, 768, 390 y 360 × 5 solapas:
sin overflow de página ni textos cortados dentro de las solapas, 0 errores de consola. 503/503
tests `Cliente*` verdes (2 omitidos por diseño). No re-ejecutados: los specs Playwright
`e2e/cliente-details-tabs.spec.js` y `cliente-aptitud-punitorio.spec.js` (este último exige
seed). Técnicamente: ✅ · Visualmente: ✅ · Flujo UX: ✅ (ver seguimiento abajo).

**Seguimiento (mismo día, a pedido "soluciona los pendientes")** — los tres puntos que habían
quedado abiertos por decisión de producto:
- **Redundancia de "No apto"**: se retira el chip de aptitud del encabezado (repetía el KPI
  "Estado de aptitud" a 40px de distancia). Quedan el KPI (estado), la alerta principal (motivo +
  acción, pedido explícito del usuario) y la card "Motivos", que detalla la lista. La fila "Estado
  de aptitud" del aviso de punitorio en Crédito **se conserva**: `cliente-aptitud-punitorio.spec.js`
  la exige como contrato (se había quitado y el CI lo detectó; restaurada).
- **Solapa Historial fusionada en Crédito**: era una card con un único link a
  `Credito/Index?clienteId=` que "Últimos créditos del cliente" ya tiene ("Ver todos (N)").
  La ficha pasa de 5 a **4 solapas** (Resumen/Crédito/Documentación/Datos). Un `#historial`
  guardado abre Crédito (alias en `cliente-details.js`) en vez de caer a Resumen. Tests de
  contrato y `e2e/cliente-details-tabs.spec.js` actualizados (+1 test del alias): 43/43 en los
  5 viewports del proyecto Playwright; 503/503 `Cliente*`.
- **Navegación duplicada**: se retira el breadcrumb "Clientes / Detalle" y queda "Volver a
  clientes" (target más grande; respeta `returnUrl`); la ubicación la dan la barra global y el
  ítem activo del menú.

Validado de nuevo en vivo en 1440/1280/768/390/360 × 4 solapas: sin overflow, sin textos
cortados, 0 errores de consola. Flujo UX: ✅.

### Cliente / Create y Edit (páginas completas) — auditoría `/ui-module` (2026-09-24)

Alcance: `/Cliente/Create` y `/Cliente/Edit/{id}` (`Create_tw`, `Edit_tw`) y el parcial de campos
`_ClienteFormCampos`, que **comparte** con el drawer. No se convirtió la página a wizard (el
estado del drawer ya lo dejaba como alcance aparte y la auditoría no lo justificó: el formulario
es corto, los obligatorios están todos en la primera solapa y los errores se ven). Sin cambios de
backend, ViewModel, validaciones, permisos ni ids/`name`:

- **Jerga y contradicción en Edit (P1)**: mostraba el enum crudo `AprobadoCondicional` dos veces
  (chip del encabezado y "Aptitud" del resumen) — no es la aptitud del cliente (Details dice "No
  apto") sino su nivel de riesgo. Ahora "Nivel de riesgo: Medio" con la misma clasificación y textos
  que Cliente/Index (`ClienteHelper.ClasificarRiesgo`); se retira el chip duplicado del encabezado;
  "Score 6,00" pasa a "Puntaje de riesgo 60%" (misma escala que Index y su tooltip).
- **Solapa oculta (P1)**: a 1280px la sexta solapa ("Crediticio", con los montos
  mínimo/máximo personalizados) quedaba fuera de la barra sin ninguna pista. 1024–1439px: la
  barra pierde los íconos (el texto basta) y, entre 1024 y 1279px, la tarjeta lateral baja debajo
  del formulario; ≤640px la barra scrollea con fade en el borde derecho. Medido: las 6 solapas
  entran completas de 768 a 1920.
- **Accesibilidad (P2)**: 3 campos sin etiqueta asociada (Estado civil, Monto mínimo y máximo
  personalizados: el `<label asp-for>` apuntaba a un id distinto del `id` fijado en el input). Corrige
  también el drawer (parcial compartido).
- Sin cambio deliberado: el `<h1>` repite el título de la barra global — se conserva como encabezado
  del formulario (título + subtítulo con la advertencia de aptitud); nombre en minúsculas
  ("cotizacion, cotizacion") es dato cargado, no presentación.

Validado en vivo 1440/1280/768/390/360 en ambas páginas (sin overflow, 0 errores de consola);
503/503 `Cliente*`; e2e `cliente-wizard-nuevo` + `cliente-details-tabs` 113/113 en los 5 proyectos
Playwright. Técnicamente: ✅ · Visualmente: ✅ · Flujo UX: ✅.

**Seguimiento — errores en solapas ocultas y Escape (P1, solo `cliente-form.js`, páginas
Create/Edit; el drawer y su wizard usan `cliente-modal.js` y no cambian)**: Teléfono y Domicilio
son obligatorios pero viven en la solapa Contacto. jQuery Validation ignora los campos ocultos, así
que "Crear cliente" desde Personales se enviaba, el servidor lo rechazaba y la página volvía a
Personales **sin ningún error visible** (los mensajes quedaban en Contacto). Ahora se validan todas
las solapas (`ignore` = solo `input[type=hidden]`); si algo falla se abre la primera solapa con error,
se enfoca el campo y las solapas con errores llevan un punto rojo (+ "(con errores)" para lectores de
pantalla); lo mismo tras un rechazo del servidor. "Recordá" de Create avisa que teléfono y domicilio
son obligatorios. Además `Escape` (volver a la pantalla anterior) solo actúa con el formulario
intacto: con datos escritos —o al cerrar un desplegable con Escape— descartaba todo sin aviso.
Verificado en vivo: 0 POST al servidor con obligatorios de Contacto vacíos, foco en Teléfono, Escape
sin navegar; 113/113 e2e del wizard/solapas siguen verdes.

**Corrección posterior (regresión propia, detectada al auditar el drawer)**: la validación entre
solapas de `cliente-form.js` validaba también las reglas `number` en solapas ocultas y los decimales
llegan con coma (es-AR: `Sueldo="5000000,00"`), así que guardar un cliente existente en `/Cliente/Edit/{id}`
quedaba bloqueado ("The field Sueldo must be a number"). Ahora, en solapas inactivas solo se exigen los
`[Required]` (la activa se valida completa; el servidor sigue siendo la autoridad). Nuevo
`e2e/cliente-form-validacion.spec.js` (29 corridas): obligatorio de Contacto abre y marca la solapa sin
enviar, Escape con/sin datos, y "editar un cliente existente sin cambios es válido en el navegador".

### Cliente / Drawer de edición — auditoría `/ui-module` (2026-09-24)

Drawer del botón "Editar" del listado (`_ClienteModal` + `_ClienteFormPartial`, `cliente-modal.js`); el
wizard de alta no cambia. 4 capas, sin overflow ni errores de consola en 1440/1280/768/390/360:
- **P1 (técnica/estados)**: el drawer de edición no corría jQuery Validation: con Teléfono vacío en otra
  solapa el guardado iba al servidor y volvía como cartel arriba, sin solapa marcada, sin campo enfocado ni
  error junto al campo. Ahora valida `[Required]` entre solapas, abre y marca la primera con error y enfoca
  el campo (solo `[Required]`: mismo motivo de la coma decimal que arriba).
- **P1 (flujo)**: Escape, clic en el fondo y Cancelar descartaban cambios sin avisar. En edición con cambios
  sin guardar ahora piden confirmación ("Hay cambios sin guardar…"); sin cambios cierran directo.
- Cambios: `cliente-modal.js` (solo edición; `requestClose`, flags de sucio con listeners solo en la rama de
  edición) y `cliente-module.css` (punto de solapa con error también en `#cliente-modal-form`). Sin C#.
- Sin resolver (P3 / requiere C#): errores de servidor de `EditAjax` llegan como lista sin campo ni solapa
  (siguen en el cartel superior); el foco no se mueve al abrir/cerrar el drawer; doble "Cancelar"
  (cabecera y pie); sin confirmación al cerrar el wizard de alta (para no arriesgar sus e2e).
- QA: 503 tests `Cliente*` y `cliente-wizard-nuevo` verdes; sin guardar datos reales.

### Cliente / Delete ("Dar de baja") — auditoría `/ui-module` (2026-09-24)

`DeleteAsync` es un **soft-delete** (`IsDeleted = true`, se conserva el historial) y en Details el
mismo botón se llama "Dar de baja cliente… Se conserva el historial", pero la pantalla de destino
decía "Eliminar cliente / Esta acción es permanente / Eliminar definitivamente" y el menú de Index
"Eliminar cliente" con un diálogo genérico "no se puede deshacer": tres nombres y una promesa falsa
para la misma acción (P1, confianza/error de comprensión). Se unifica en **"Dar de baja"** con lo que
el código realmente hace:
- Delete: título, alerta ("El cliente dejará de aparecer en el listado… se conserva su historial…
  Hoy el sistema no ofrece reactivarlo" — verificado: no existe reactivación en servicio/controller/
  vistas de Cliente), confirmación y botón ("Dar de baja al cliente"); se retiran del detalle los
  datos que repetían el encabezado (nombre, documento, estado), "Qué se va a eliminar" pasa a
  "Créditos y deuda del cliente" (no lista lo que se borra) y "Créditos activos" deja de ir en verde.
- Index: el ítem del menú (tabla y tarjetas) pasa a "Dar de baja cliente" y es un link directo a esa
  pantalla, que ya es la confirmación (checkbox): se elimina el diálogo previo redundante que
  además afirmaba "no se puede deshacer" (`module-index.js` compartido con otros módulos, no se toca).
- Mensaje de éxito: "Cliente dado de baja" (única línea de C# tocada, `ClienteController.DeleteConfirmed`).
Sin cambios de permisos (`clientes.delete`), rutas ni del servicio. Validado en 5 viewports, sin
overflow ni errores de consola; 503/503 `Cliente*`. Técnicamente: ✅ · Visualmente: ✅ · Flujo UX: ✅.

### Cliente / cierre de módulo — barrido `/ui-module` (2026-09-25)

Barrido de todas las superficies del módulo (Index con datos y sin resultados, menú "Más", modal
"Límites por puntaje", drawer de alta, Details, Edit y Delete) en 1440×900 y 390×844, sin overflow ni
errores de consola. Lo ya auditado el 2026-09-24 no se reabrió; la única superficie sin auditar era el
modal de límites. Cambios (solo Razor/CSS, sin negocio, permisos ni contratos):
- **Menú "Más" en mobile (P2, visual)**: Documentos y Créditos salían centrados y desalineados de
  "Límites por puntaje". Causa: la regla global de `erp-responsive-system.css` para links de "solo ícono"
  (`a[href]:has(> .material-symbols-outlined:only-child)`) los centraba. Se restituye la alineación
  izquierda con un selector local en `cliente-module.css`.
- **Modal "Límites por puntaje" (P2, microcopy/señal falsa)**: sin tildes ("Limites", "configuracion",
  "maximo"), voseo inconsistente ("No tenes permiso") y los puntajes 3 y 5 resaltados en verde sin
  significado alguno. Se corrige el texto, todos los puntajes quedan neutros y en modo lectura el botón
  dice "Cerrar" en vez de "Cancelar".

Validación técnica: 503/503 tests `Cliente*` (2 omitidos por diseño). Lo que en el primer barrido quedó sin validar por falta de datos (paginación multi-página, roles sin permisos, guardado real) se validó después en una copia QA de la base; ver abajo. La decisión de producto sobre mostrar la aptitud en el listado ("Disponible $1.000.000.000" en un cliente que Details marca "No apto") se resolvió mostrando el badge.
**Aptitud en el listado (implementada y validada en base QA)**: `ClienteViewModel.EstadoCrediticio` (ignorada en el mapeo inverso) y un badge "No apto" / "Requiere autorización" bajo "Disponible" en tabla y tarjetas de `Cliente/Index` (Apto/No evaluado no muestran nada; "Requiere autorización" baja a 2 líneas en 1024–1440px, sin recorte). **Hallazgo P1 corregido**: un rol solo-lectura (Cajero) veía "Nuevo cliente" (barra y estado vacío) aunque `Create` le da AccessDenied; ahora se oculta con `ViewBag.PuedeCrearClientes` (`clientes.create`). Validado en una copia QA de la base (30 clientes sembrados, luego eliminada) en 1440/1280/1024/768/390/360: sin overflow ni errores de consola; paginación 25/31 y página 2, clamp de página 99, filtros conservados al paginar; Cajero: sin Nuevo/Editar/Baja, Edit/Delete/Create → AccessDenied, modal de límites en solo lectura (inputs deshabilitados, "Cerrar"); Vendedor: Editar y Nuevo sí, Baja no; alta real por el drawer y guardado real de límites OK; e2e wizard/validación/solapas 41/41 en 1440 y 390; 503/503 `Cliente*`. Contador validado (su "login fallido" era el paso de Términos y Condiciones del primer ingreso, que pide además nombre y apellido; no es un defecto): sin Nuevo/Editar/Baja, Create/Edit/Delete → AccessDenied, límites en solo lectura.

**Crítica `impeccable critique` (dual-agent: A revisión de diseño 26/40 · B detector + evidencia de navegador)**. Detector estático sobre `Views/Cliente`: exit 0, 0 hallazgos; el overlay vivo (headless, sin pestaña [Human]) dio ruido heredado del shell compartido (`layout-transition`, `clipped-overflow-container`, `dark-glow` del token primario). Corregido en esta pasada: (1) Details: la card "Crédito disponible" seguía en lima junto a "No apto" (contradicción; ahora neutra y con "· no habilita operar a crédito" / "· requiere autorización"); (2) Index: el nombre del cliente era lo más liviano de la fila (ahora 14px/600); (3) el menú "Más" quedaba abierto detrás del modal de límites (se cierra al abrirlo). No se tocó, con motivo: filtros `tp-filter-*` sin etiqueta (pertenecen al panel de tickets del layout, no a Clientes); contraste 4.0:1 de `#15110a` sobre `#607e16` (token compartido de gradiente lima, fuera del módulo); targets táctiles de 40px en mobile (el estándar del ERP no fija 44px); nombre en minúsculas (dato cargado); "Crediticio" vs "Crédito" (contenidos distintos: montos personalizados vs resumen); filtro rápido por aptitud y compactar importes (funcionalidad nueva, no pedida).


## Cliente / Index — cerrado (CLIENTE-INDEX-SIN-HERO-01)

La búsqueda/filtros/paginación/permisos de esta pantalla se implementaron el 2026-09-14
(ver historial de memoria del proyecto) sin actualizar esta tabla en su momento. Este
lote (mismo día que VENTA-INDEX-SIN-HERO-01) cierra específicamente el patrón de header,
a pedido explícito del usuario tras comparar capturas reales de Inventario/Cliente/Ventas
y preferir la composición sin hero de Catálogo:

- se retira el `<header class="hero-erp cliente-page-head">` (título `<h1>Gestión de
  Clientes</h1>` + subtítulo "Buscá y administrá tus clientes." + acciones) — el título ya
  lo muestra la barra superior global (`_Layout.cshtml`), y el subtítulo era descriptivo
  sin valor de decisión, redundante con el propio nombre de la pantalla;
- "Nuevo cliente" y el menú "Más" (Documentos/Créditos/Límites por puntaje) se mueven a
  una toolbar dentro del mismo card de contenido (`.cliente-section`), alineada a la
  derecha por encima de "Listado de clientes" — mismo criterio que la fila de
  tabs+acciones de `Catálogo/Index_tw.cshtml` y `Venta/Index_tw.cshtml`
  (VENTA-INDEX-SIN-HERO-01), adaptado a que Cliente no tiene tabs. `.hero-erp` (componente
  compartido de `shared-components.css`, usado por Dashboard/AlertaStock/Proveedor/
  ConfiguracionPago/Ticket/CambiosPrecios) no se toca, sólo se deja de usar en esta vista;
- doble espaciado retirado: `.cliente-section` tenía `margin-top:1.5rem` (y `1.25rem` en
  mobile) pensado como separación bajo el header que se retira — sumado al
  `padding-top` propio de `.cliente-index`, dejaba ~48px de hueco vacío bajo la barra
  global en vez de los ~32px de Catálogo; se retira el margin duplicado (ambas reglas,
  `cliente-module.css`) y `.cliente-index` sigue aportando el espaciado real;
- `data-cliente-modal-open="create"` y `data-cliente-row-menu` (selectores por atributo,
  no por posición en el DOM) verificados sin cambios de comportamiento: el modal "Nuevo
  cliente" y el menú "Más" abren igual desde la nueva ubicación;
- `aria-labelledby="cliente-page-title"` retirado del contenedor raíz junto con el `id`
  que referenciaba (removido con el header); ningún test ni JS lo consumía (verificado por
  grep). CSS muerto retirado de `cliente-module.css` (`.page-head__copy`, única regla,
  único consumidor). Sin cambios de reglas de negocio, permisos, contratos backend, ids ni
  `data-*` funcionales.
  Validado en vivo con Playwright (instancia del usuario en :18787, solo lectura, sin
  tocarla) en 1440×900 y 390×844: sin overflow horizontal, 0 errores de consola; modal
  "Nuevo cliente" (wizard completo) y menú "Más" (Documentos/Créditos/Límites) verificados
  funcionalmente end-to-end; build 0/0; 501/501 tests focalizados (`Cliente*` +
  `VentaEnvioUiContractTests`) verdes.

Deuda conocida, no iniciada en este lote: la búsqueda/filtros/paginación/permisos del
2026-09-14 no recibieron la auditoría de 4 capas de §14 — este cierre cubre
específicamente el patrón de header (CLIENTE-INDEX-SIN-HERO-01), no una auditoría
integral de la pantalla. (Retomada por `/ui-module cliente`, ver abajo.)

### Cliente / Index — auditoría `/ui-module` (2026-09-24)

Reabierta para este alcance (pantalla por pantalla; Details/Create/Edit no se tocaron).
Auditoría de 4 capas con `ux-heuristics` + critique (en un solo contexto, sin sub-agentes)
sobre la pantalla real (1 cliente en la DB local + estado vacío por búsqueda sin
resultados). Hallazgos y cambios, todo en `Index_tw.cshtml` y `cliente-module.css`, sin
tocar reglas de negocio, permisos, contratos ni `data-*`:

- **Mobile ≤640px (P1, flujo/visual)**: los 5 filtros apilados ocupaban una pantalla
  entera antes del primer cliente (y empujaban el estado vacío bajo el pliegue). Búsqueda a
  ancho completo + Estado/Riesgo y Tipo/Mostrar de a pares; "Nuevo cliente" y "Más" en una
  sola fila. El primer cliente y el estado vacío completo entran ahora en 390×844.
- **Desktop (P2, microcopy/datos)**: el CUIT/CUIL se truncaba con "…" (identificador
  ilegible sin abrir el legajo). El prefijo baja de línea; el número nunca se corta. El
  porcentaje de riesgo ("60%") sin etiqueta gana `title="Puntaje de riesgo: N%"`.
- **Tarjetas mobile/tablet (P2)**: el "$" del importe se separaba del número (nbsp);
  Crédito toma el ancho de su importe y Riesgo el resto (apilados ≤380px, donde el chip
  desbordaba); "Editar"/"Más" pierden la etiqueta bajo 390px de viewport útil → siempre
  visible (ancho de sobra); entre 421 y 899px "Más" caía en una fila aparte (bug previo:
  `.cliente-card-menu{grid-column:1/-1}` dentro de la grilla interna de 2 columnas); el
  botón "Editar" no tenía borde y "Más" sí → mismo peso.
- **Microcopy (P3)**: la opción "Todos los tipos" del filtro Tipo de documento pasa a
  "Todos" (igual que Estado/Riesgo; el label ya dice qué se filtra; se cortaba a 360px).
- Sin cambio deliberado: "Limpiar filtros" en chips + estado vacío es duplicación útil
  (recuperación en el punto del fallo).

Validado en vivo (Playwright, instancia del usuario :18787, solo lectura): 1440×900,
768×1024, 390×844 y 360×800, con datos y estado vacío; sin overflow horizontal, 0
errores/warnings de consola; menú "Más" y autosubmit de filtros verificados. Build OK,
503/503 tests `Cliente*` (2 omitidos por diseño, seeders E2E).
- **Laptops 900–1439px (P2, visual/datos)**: a 1280×720 (contenedor ≈948px) la tabla
  scrolleaba en horizontal y Cliente/Contacto quedaban en ~124px c/u ("cotizacion, co…",
  "asdf.123…"): las columnas fijas sumaban 712px. En esa banda la columna Estado se oculta
  y su pill (misma lógica) pasa bajo el nombre; el piso de tabla baja a 854px y el reparto
  es Cliente 160 / Documento 140 / Crédito 150 (Contacto recibe el resto, ≈212px). Efecto
  aceptado: en la banda no se puede ordenar por Estado (el filtro Estado sí). Medido en
  vivo: sin scroll ni truncado de 1280 a 1920; a 900/1024 (contenedor < 854px) se mantiene
  el scroll interno con aviso, diseño previo.
- **Progreso del wizard "Nuevo cliente"**: la barra deja de animar `width` (layout) y avanza
  con `transform: scaleX` (`cliente-modal.js`), con `prefers-reduced-motion`.

Técnicamente: ✅ · Visualmente: ✅ · Flujo UX: ✅ (sobre el volumen real de datos local: 1
cliente; no se evaluó la tabla con paginación multi-página).

## Catálogo / Inventario (tab Productos) — cerrado

A pedido explícito del usuario, con una captura de referencia como fuente visual
autorizada (fidelidad literal confirmada por el usuario), para converger geometría y
composición con `Cliente/Index` y `Venta/Index` sin tocar reglas de negocio, permisos,
rutas ni ViewModels.

- **Shell**: `erp-page-shell-xl` (cap `--erp-shell-xl: 1480px`, centrado) reemplazado por
  `erp-page-shell` fluido (sin modificador de ancho máximo) — mismo criterio de
  convergencia ya aplicado en `Cliente/Index` (`.shell.cliente-index`) y `Venta/Index`
  (`#venta-index-rework.venta-index-shell`): el gutter real ya lo aporta `_Layout.cshtml`.
  Medido en vivo (Playwright, 1920×1080): 1480px → 1774px, igual que Venta (1774px) y
  Cliente (1784px), mismo gutter izquierdo (104px). `erp-page-shell-xl` sigue vigente sin
  cambios para las otras 2 vistas que la comparten (`DocumentoCliente/Index_tw`,
  `Producto/UnidadesGlobal`), no tocadas.
- **Tabs**: ícono + fondo pill dorado en el tab activo (antes texto con subrayado
  inferior); aplica a los 5 tabs (Productos/Categorías/Marcas/Alertas/Movimientos), scroll
  horizontal interno sin cambios cuando no entran a lo ancho.
- **Acciones superiores**: "Nuevo producto" se reubica junto a "Inventario"/"Ajuste
  Masivo" (antes vivía en una cabecera propia dentro del tab Productos); se retira el
  bloque "Productos del catálogo / Filtrá, seleccioná..." — el contador de productos
  visibles (`#productos-visible-count`, mismo id que actualiza `catalogo-index.js`) y
  "Limpiar filtros" pasan a la fila de filtros.
- **Tabla de Productos**: `table-layout: fixed` con anchos por intención (Producto 42% >
  Precio 18% > Stock 12% > Acciones 15% > Comisión 10%), scopeado a
  `#tab-productos .erp-table` en `catalogo-module.css` para no afectar las tablas de
  Categorías/Marcas/Alertas/Movimientos, que comparten la clase `.erp-table` con otras
  columnas.
- **Badge "Inactivo"**: se reduce peso visual (de `font-bold` rojo saturado a
  `font-semibold` más sutil) sin eliminarlo ni moverlo a columna propia (confirmado con el
  usuario que la referencia lo mostraba junto al nombre del producto, no en columna
  separada).
- **Acciones por fila**: "Inventario" y "···" (más acciones) convergen al componente
  compartido `.row-action` (`shared-components.css`, mismo patrón ya usado en
  `Cliente/Index`/`Venta/Index`) — ghost, ícono-only; "Editar" conserva texto.
- Regresión propia encontrada y corregida en el mismo lote: al ensanchar la columna
  Producto, el `flex-1` del nombre estiraba el badge "Inactivo" lejos del texto visible
  (hasta ~700px de distancia). Corregido reemplazando `flex-1` por `shrink` + `max-w-xl`
  en el nombre.
- Sin cambios de cálculo, reglas de negocio, permisos, ids, rutas ni `data-*`.
  Validado en vivo con Playwright (instancia del usuario en :18787, solo lectura, sin
  tocarla) en 1920×1080, 1440×900, 1280×720, 1024×720, 768×1024, 390×844 y 360×800: sin
  overflow horizontal de página en ningún viewport, 0 errores/warnings de consola; tab
  switching, modal "Nuevo producto", selección múltiple (chip + badge + barra sticky) y
  atributos/hrefs de acciones verificados funcionalmente sin cambios; build 0/0; 73/73
  tests focalizados de Catálogo verdes.

Hallazgos fuera de alcance detectados durante la auditoría, no corregidos (preexistentes,
ajenos a este lote): el botón "···" (más acciones) no tiene ningún listener JS que lo abra
en `catalogo-index.js` ni `catalogo-module.js` (botón inerte); a exactamente 1024px de
viewport el input de búsqueda del formulario de filtros se comprime a ~92px y su label
pasa a 2 líneas (breakpoint `lg:` del formulario de filtros, sin relación con el shell ni
la tabla).

Deuda conocida, no iniciada en este lote: Categorías/Marcas/Alertas/Movimientos se
benefician del shell fluido y del rediseño de tabs, pero no recibieron auditoría completa
de 4 capas — quedan fuera del alcance visual pedido (limitado a "Inventario"/Productos).
Categorías quedó cerrada después en CATEGORIA-CIERRE-01 (ver más abajo).

## Catálogo / Inventario (tab Categorías) — CATEGORIA-CIERRE-01

Cierre de módulo pedido por el usuario (`/ui-module`): tab Categorías, modales Nueva/Editar,
confirmación de eliminación y la barra de pestañas compartida con el resto de Inventario.
Reabre lo que quedaba como deuda en el cierre de Productos ("Categorías ... no recibieron
auditoría completa de 4 capas").

Defectos reales encontrados y corregidos (los dos primeros solo se vieron ejercitando el flujo,
no con build/tests):

- **Cambio de pestaña con un clic dejaba dos pestañas resaltadas** (Productos seguía en verde
  con texto ilegible): el JS y Razor usaban juegos de clases distintos. El estado activo ahora
  es `aria-selected` (server y JS), pintado por `.catalogo-tab` en `catalogo-module.css`;
  `role=tablist/tab/tabpanel`, roving tabindex y flechas/Home/End. `?tab=` se mantiene en la
  URL con `replaceState` (recargar/volver conserva la pestaña).
- **No se podía desactivar una categoría**: el checkbox desmarcado no viaja y
  `CategoriaViewModel.Activo` vale `true` por defecto. Se agrega el hidden `Activo=false`
  después del checkbox (patrón de `Html.CheckBox`).
- **Permisos por acción sin enforcement**: `create/update/delete` existían en el seeder pero el
  controller solo exigía `view`. Ahora `CreateAjax` (create), `EditAjax`/`GetJson` (update) y
  `Delete` (delete) los exigen; la vista oculta Nueva/Editar/Eliminar (y la columna Acciones
  si no queda ninguna). Con solo `view` el rol Vendedor lista sin acciones y recibe 403.
- **Editar solo ofrecía categorías raíz como padre** (un padre no raíz o inactivo se perdía en
  silencio al guardar). Alta y edición usan la misma lista (todas las vigentes, en orden
  jerárquico, "(inactiva)" marcada); la edición oculta la opción propia; el servidor sigue
  validando ciclos.
- **La fila se armaba en JS con HTML duplicado** (clases del tema claro, sin descripción, sin
  contador ni sangría). Tras crear/editar se recarga `/Catalogo?tab=categorias` y el aviso viaja
  por TempData; eliminar también vuelve a la pestaña (antes caía en Productos).
- Barra superior: la última pestaña ("Movimientos") quedaba recortada a 1440px porque el bloque
  de acciones le quitaba el ancho; "Ajuste masivo" y "Nuevo producto" (solo aplican a Productos)
  se ocultan fuera de esa pestaña con `[hidden]` (antes el toggle por clase no ganaba a
  `.btn-erp-*` y en el render server seguían visibles). El contador de la pestaña activa pasa a
  contraste legible.

UX/UI: lista en orden jerárquico con sangría y conector (padre → hijas), columna Estado como
única señal (se retira el "Inactivo" duplicado bajo el nombre), acciones con `.row-action`
(icono en ≥sm, texto en mobile), estado vacío con siguiente paso, copy en voseo sin relleno.
Modales: mismo partial para campos e interruptores (`.catalogo-switch`, foco visible), `<label for>`,
obligatorios marcados, validación por campo con foco en el primero, mensaje claro si falta permiso
o venció la sesión, `novalidate`, cuerpo scrolleable con `.catalogo-modal-form` (cabecera y pie
siempre completos, verificado hasta 844×390).

Sin cambios de reglas de negocio, contratos de servicio, rutas ni datos. Tests: 10 nuevos de
contrato (`CategoriaCatalogoContractTests`).

Validado en vivo (Playwright propio, Chrome del sistema, contra clon de la LocalDB en :5199):
1920/1536/1440/1280/1024/768/390/360 (sin overflow, 0 errores de consola/requests); alta, edición
(incluye desactivar), eliminar con hijas (error del servidor), eliminar OK, código duplicado,
validación de campos, estado vacío (admin, mobile y solo lectura), roles SuperAdmin,
Administrador (permisos completos) y Vendedor (solo `view`: sin botones y 403 en POST).

Fuera de alcance, sin corregir ni auditar: la pestaña Marcas comparte la barra de pestañas (ya
corregida) pero su tabla y modales no se revisaron; `MarcaController` solo declara `marcas.view` a
nivel de clase (verificado) y podría tener los mismos defectos que tenía Categorías. El modal de
confirmación genérico (`TheBury.confirmAction`, "Confirmar acción / Confirmar") es compartido por
todo el ERP.

## Caja — cierre de módulo `/ui-module caja` (2026-09-25, sin commit)

Barrido de todas las superficies (Index con turnos vencidos, panel lateral Nueva/Editar, Create/Edit de página completa, eliminar con confirmación, Abrir, Registrar movimiento, Cerrar, Detalle de turno con sus 5 pestañas, Detalle de cierre, Historial con datos, filtros abiertos y estado vacío) en 1440×900, 1280×720, 768×1024, 390×844 y 360×800: 0 overflow, 0 errores de consola, 0 requests fallidos. QA en una copia de la base (`TheBuryProjectDb_caja_qa`, instancia :5199, ya eliminadas) con flujos de escritura reales: abrir turno, ingreso, egreso, cierre con sobrante y justificación obligatoria, alta y baja de caja.

Hallazgos corregidos (sin cambiar reglas de negocio, permisos ni contratos):
- **P1 funcional — `Create` nacía inactiva**: el GET no pasaba modelo y "Caja activa" se renderizaba destildada ("Estado inicial: Inactiva" en rojo); una caja nueva quedaba sin poder abrirse sin que nadie lo decidiera. Ahora `View("Create_tw", new CajaViewModel())`.
- **P1 flujo — callejón sin salida en turno vencido**: el Detalle mostraba "Turno abierto" y "Nuevo movimiento" en un turno de día anterior; el formulario se completaba y el servicio lo rechazaba al guardar. El Detalle ahora dice "Turno vencido" (chip + aviso, también en Información del turno), oculta "Nuevo movimiento" y deja solo "Cerrar caja"; `RegistrarMovimiento` (GET) redirige al detalle si la apertura está vencida. El servicio sigue siendo la autoridad.
- **P1 contrato de formulario — "Descripción (opcional)" era obligatoria** en Registrar movimiento (`[Required]` + `required`): la etiqueta ahora marca `*`. Inversamente, "Sucursal *" en Create/Edit de página no es obligatoria en el modelo (verificado creando una caja solo con código y nombre): pasa a "(opcional)".
- **P2 — arqueo alarmista**: Cerrar precargaba `0` y mostraba de entrada "Faltante −$691.982,80" con justificación obligatoria. Ahora el campo arranca vacío, la diferencia es neutra ("—") hasta ingresar el conteo y el estado (Exacto/Sobrante/Faltante) se muestra como chip de texto, no solo color; se retiró un ícono suelto que se colaba bajo el texto. Registrar movimiento: Monto también vacío al abrir.
- **P2 — botón principal cortado**: el panel lateral de alta/edición (`100vh-4rem`) dejaba "Cancelar/Guardar" fuera del viewport a 1440×900 (parte 32px más abajo del header); ahora `100dvh-8rem` a ≥1024px.
- **P2 — primer click perdido en validación**: al corregir el último campo y pulsar el botón, el blur retiraba el mensaje, el botón subía y el click se perdía; los mensajes de validación de los formularios de Caja reservan su línea.
- **P2 — Historial a 1280px**: la tabla (min 66rem) escondía la columna "Ver detalle" tras un scroll horizontal sin pista; baja a 54rem y entra completa desde 1280px (por debajo sigue con scroll y su aviso). El respiro bajo el título de "Filtros" colapsado dejaba ~16px vacíos.
- **P2 — paridad mobile**: las tarjetas de "Tus cajas" no ofrecían Eliminar (sí el desktop); ahora aparece con la misma condición (admin, caja sin turno) y la misma confirmación; el aviso "Quedó abierta del día anterior…" se repetía en cada tarjeta además del banner y el chip "Vencida" y se retiró.
- **P2 — panel lateral desactualizaba la pantalla**: tras crear/editar una caja el JS parchaba el DOM (filas y tarjetas sin "Eliminar", RowVersion viejo, KPIs y chips "Todas/Disponibles/En uso" sin actualizar); ahora `caja-index.js` avisa y recarga (se eliminó el código de armado de filas del cliente). Verificado en 1440 y 390: la caja nueva aparece con Eliminar y los contadores suben.
- **P3**: número de venta en la pestaña Ventas ya no se parte en dos líneas; buscador del libro mayor sin recorte del placeholder; placeholder de código "CAJA-01" en los paneles (antes "C01-HQ", contradecía la ayuda).

Validación técnica: 257/257 tests `Caja*` (2 nuevos: `Create_Get_EntregaModeloConCajaActivaPorDefecto`, `RegistrarMovimiento_Get_AperturaVencida_RedirigeAlDetalleSinMostrarElFormulario`); build 0 errores. `ux-heuristics` ejecutada; `impeccable critique` formal dual-agent (A revisión de diseño 31/40 · B detector: `impeccable detect Views/Caja` = 0 hallazgos, sin errores de consola ni de a11y programática) y `polish` ejecutados. Sin pendientes propios; los datos "test"/"cotizacion" que se ven en las capturas son de la base local.

**Segunda pasada (crítica formal + roles)**: (1) Index: el banner de turno vencido enlaza a las tarjetas ("Ver turnos vencidos"); (2) KPIs "Egresos efectivo" (Cerrar) y "Sobrantes/Faltantes" (Historial) solo se pintan de rojo/ámbar si el valor es mayor a 0; (3) Cerrar: "Movimientos: N · ver detalle" abre el detalle en pestaña nueva sin perder el conteo cargado; (4) rol Cajero sin cajas asignadas (validado con el usuario real `cajero`): el estado vacío decía "No hay cajas activas configuradas" y "Seleccione una caja disponible" (falso y sin salida); ahora "No tenés cajas asignadas. Pedile a un administrador que te habilite…" y voseo consistente. Cajero: Create/Edit → AccessDenied, sin Nueva/Editar/Eliminar, detalle/cierre de turnos ajenos redirigen; solo Historial disponible.
**Tercera pasada (todo lo que había quedado abierto)**: (1) Cerrar con diferencia pide una segunda confirmación con el monto ("Vas a cerrar la caja con un faltante de $X…"), sin inventar umbral: se dispara ante cualquier diferencia; (2) Historial muestra "% del esperado" bajo cada diferencia (escala sin definir tolerancia de negocio); (3) Abrir pierde los 4 KPI redundantes (Responsable y hora pasan a una línea bajo el título); (4) en Index las filas/tarjetas "En uso" ya no repiten "Ver turno": apuntan a la tarjeta de "Cajas abiertas ahora" ("Ver arriba"); (5) Cerrar enlaza a la calculadora por denominación del detalle (`#conciliacion`, ahora las pestañas aceptan hash); (6) el filtro activo de Historial ya se veía (el panel se expande solo con filtros); (7) layout: el título del topbar deja de ser `<h2>` (antes precedía al `<h1>` en todas las pantallas); (8) hallazgos nuevos de la validación por roles: los GET de Abrir/Registrar movimiento/Cerrar no exigían `caja.open/movements/close` (solo el POST) y Registrar/Cerrar mostraban su formulario sobre un turno ya cerrado; ahora exigen el permiso y redirigen al detalle; Index y Detalle solo ofrecen Abrir/Movimiento/Cerrar si el usuario tiene ese permiso.
Últimos retoques: la pestaña Movimientos ya no repite la referencia cuando la descripción la nombra ("Venta COT-…" + enlace) y las tarjetas Top sobrantes/faltantes de Historial no se estiran entre sí.
**Roles validados con cuentas reales en copia de la base**: Cajero (sin acceso a Create/Edit, sin cajas asignadas → mensaje guiado), Vendedor (Historial y turno en solo lectura sin botones de operar, Create/Edit → AccessDenied) y Contador (sin `caja.view` → AccessDenied en todo el módulo). 258/258 tests `Caja*` (+1 nuevo por turno cerrado) y 712/712 con Layout/UiContract/Sidebar.

## Mobile transversal — MOBILE-DEBT-01 (deuda de la auditoría mobile 2026-09-20)

Lote transversal que cierra la deuda restante de la auditoría mobile (101 rutas, 390/360/768).
No es una pantalla: toca `_Layout`, un componente compartido nuevo y CSS por módulo; cada
pantalla afectada sigue con su estado de cierre propio.

- **Nombre de módulo en el header <lg.** `_Layout` ocultaba el `<h2>` (`hidden lg:block`)
  aunque en <lg la sidebar es un drawer y el header es lo único que nombra la pantalla: 20 de
  101 rutas quedaban sin título visible en mobile/tablet (Venta, Cliente, Catálogo, Seguridad,
  Proveedor, Órdenes de compra, …). Ahora se muestra siempre, truncado con `min-w-0`; costo
  vertical cero (el header ya medía 64px). 20 → 2 rutas sin nombre visible (las 2 son
  `Cotizacion/Imprimir`, vista de impresión sin layout).
- **ERP-TABLE-CARDS-01 (componente compartido, opt-in).** `wwwroot/css/erp-table-cards.css` +
  `wwwroot/js/erp-table-cards.js`, cargados desde `_Layout`. Solo actúan sobre
  `<table data-erp-cards>` y solo bajo 640px: la fila pasa a tarjeta y el resto de anchos
  conserva la tabla. Cada `<th>` declara `data-card="title|half|actions|select|hide"`; el JS
  copia a cada `<td>` la etiqueta (`data-label`, leída del `<th>`) y el rol, sin mover nodos,
  y restituye los roles ARIA de tabla mientras el navegador los pierde por el `display`.
  El `<thead>` no se pierde: los `<th>` con controles (botones de orden, "seleccionar todos")
  quedan como franja sobre las tarjetas. Roles de columna: `title` (sube al tope de la tarjeta),
  `half`, `third` (tres cantidades cortas por renglón), `actions`, `select`, `hide`; las filas con
  `colspan` (totales, "cargando", vacío) mantienen las etiquetas del resto de las columnas.
  Aplicado a: Catálogo (Productos, Categorías, Marcas, Historial de precios y Movimientos),
  Proveedor (Index y Details), Órdenes de compra (Index y Create, editable), Cotización/Listado
  y Cotización/Detalles (líneas y medios de pago), Venta/Details (líneas), Seguridad (Usuarios,
  Roles, Auditoría), Tickets, Kardex, Movimientos de stock, Reporte de márgenes, Unidades
  globales y los dos listados del Dashboard. **No** se aplicó a las que ya tienen alternativa
  mobile propia (Venta/Index `mobile-sales-list`, Caja/Historial `hide-mobile`), al desglose de
  IVA de Venta/Details (3 filas de 4 columnas que entran) ni a los editores de propiedades y
  la simulación de precios de Catálogo. Costo asumido: las tarjetas son más altas que las filas
  (Catálogo +1090px con 5 productos en 390); en Kardex el wrapper con scroll interno
  (`max-height`) se anula en teléfono para que el scroll sea el de la página.
- **Hallazgo propio, corregido:** el ordenamiento por columna de Catálogo no funcionaba en
  ningún ancho (el JS buscaba `th[data-sort]` pero el atributo vive en el `<button>` del `<th>`);
  se corrigió en `catalogo-index.js` porque la franja "Ordenar por" de mobile lo expone.
- **Overflow puntual (0 en 390/360/768 al cierre):** `Credito/CuotasVencidas` (3 KPIs con
  `repeat(3,1fr)` inline → `.grid-kpi--3`, apilados en teléfono), `MercadoLibre/Dashboard`
  (`.kgrid` con `1fr` → `minmax(0,1fr)` y 1 columna bajo 560px), `Cotizacion/Detalles` (acciones
  del header sin `flex-wrap`), `Reporte/Morosidad` (header sin wrap a 360), `Seguridad` (header
  full-bleed con `md:-mx-10` sobre un padding de 24/32px: desbordaba 16px a 768 y 8px en
  escritorio; ahora compensa el padding real del layout) y el filtro de Auditoría (5 columnas
  desde `lg`, desbordaba 38px a 1024 → desde `xl`). Tablas de Mercado Libre (`.tbl` dentro de
  `.card{overflow:hidden}` sin wrapper): la card ahora scrollea en X.
- **Fuente de íconos 3.86 MB → 0.77 MB.** `material-symbols-outlined-fill.woff2`: misma fuente
  con los ejes `GRAD=0` y `opsz=24` fijados (los únicos valores que usa el sitio) y `wght`
  acotado a 400–700 (hay un `::after` con `font-weight:900` en Venta/Index), conservando `FILL`
  (las estrellas) y los 6570 glifos/ligaduras. Comparación de 12 pantallas en 1440 y 390:
  idénticas salvo un chevron 1–2px; las demás diferencias también aparecen entre dos cargas de
  la fuente original (contenido dinámico). El original queda en `wwwroot/fonts/` como fuente
  para regenerar (receta en `local-fonts.css`); si se regenera hay que cambiarle el nombre
  (la URL no lleva versión). Caché y compresión: entrada siguiente.
- **Compresión y caché de estáticos.** `Program.cs`: `Cache-Control` explícito (`immutable` con
  `?v=`, 30 días para `/fonts`) y compresión sólo de `text/css`, `text/javascript`,
  `application/javascript` e `image/svg+xml` (`tailwind.css` pesa 230 KB). HTML y JSON quedan
  fuera a propósito: pueden reflejar tokens antiforgery y comprimirlos sobre HTTPS abre BREACH.
  Cubierto por `StaticAssetsCacheHttpTests` (incluye "el HTML no se comprime"). Toma efecto al
  reiniciar la app.
- **Paso Cotizar del wizard en 768–1023px.** Embebido, el cotizador dispone de ~630–750px y su
  banda de 2 columnas (≥38rem) dejaba columnas de ~310–350px: etiquetas partidas ("DESCUENTO %"),
  placeholders cortados y una columna de Productos con 250px contra 500px de Cliente/Condiciones.
  Bajo 46rem de contenedor el host embebido (`#venta-create-page`) apila en 1 columna
  (Productos → Cliente y condiciones → Totales → Resultados); a 900px/1280px conserva las 2
  columnas y `/Cotizacion` standalone no cambia. Costo: +187–240px de scroll a 768/1024.
- **Otros:** `#erp-app-shell` usa `100dvh` (el `h-screen` de `_Layout` era `100vh` y dejaba el pie
  bajo la barra de URL móvil); en teléfono todos los campos a 16px con `!important` (iOS hace
  zoom con <16px; 354 → 0, incluidos los de CSS de módulo con mayor especificidad); en
  pantallas táctiles checkboxes/radios ≥24px, botones/chips chicos y links de una sola línea o
  de solo ícono ≥40px.
- **Validación:** barrido de las mismas 101 rutas en 390×844, 360×800 y 768×1024 antes/después
  (instancia propia en :5199 con el build actual, porque la del usuario tenía la vista de
  Details apuntando a un modelo aún sin compilar): rutas con overflow 4/5/1 → 0/0/0; rutas con
  tabla en scroll 23 → 2 (390; las 2 restantes son las tablas de Caja/DetallesApertura que no
  eran una lista con datos al medir, luego convertidas y verificadas por pestaña); inputs <16px
  354 → 0; controles <36px 500 → 228 y <24px 108 → 26; rutas sin nombre de página 20 → 2;
  0 errores de consola; 0 rutas con status distinto de 200. Filtros de fila (Caja) y ordenamiento
  y "seleccionar todos" (Catálogo) verificados con las filas como tarjetas. 2865/2873 tests
  focalizados; los 8 rojos son los mismos de antes (Venta wizard/Details, trabajo en curso del
  usuario). Pantallas ≥640px sin cambios visuales salvo el header con título (<lg), el filtro de
  Auditoría a 1024–1279 y el paso Cotizar a 768–1023.
- **Deuda que queda:** `/Mora` (500: `Views/Mora/Index` y el resto de las vistas del controller
  no existen; bug funcional ajeno a mobile, excluido a pedido); tablas de editores (propiedades
  y simulación de precios de Catálogo) sin reflow; ~26 controles <24px, casi todos links de
  breadcrumb de una línea; la tabla de productos del wizard pasa a tarjetas también en ≥1280px
  con la columna <736px (decisión pendiente, se dejó como está).

## Proveedor — cierre de módulo `/ui-module proveedores` (2026-09-25, sin commit)

Pantallas: `Views/Proveedor/Index_tw`, `Details_tw`, drawers Nuevo/Editar (ahora `_ProveedorModales` +
`_ProveedorFormFields`, compartidos por Index y Details), eliminar. JS: `proveedor-modal-base.js` (nuevo),
`proveedor-crear/editar-modal.js`, `proveedor-module.js`, `proveedor-index.js`, `proveedor-product-picker.js`.
Método: ux-heuristics (Nielsen/Krug) + Impeccable critique y polish aplicados leyendo sus archivos, **en un
solo contexto** (no se lanzaron sub-agentes: la sesión no los pidió; el detector `impeccable detect` dio 0
hallazgos y no sustituye la inspección visual). QA con Chrome propio (playwright-core) sobre un clon de la
LocalDB en :5199; la base real y la app del usuario no se tocaron.

Defectos reales (build y tests no los mostraban):

- **Pérdida de datos al editar**: `ProveedorService.UpdateAsync` reemplaza TODAS las asociaciones con lo que
  llega, y el drawer del Index no enviaba categorías ni marcas (se borraban en cada edición; el clon tenía 0
  asociaciones). El modal de Details era una copia legacy sin el picker de productos (los borraba) y con fondo
  blanco/títulos invisibles. Ahora hay un único drawer que carga y devuelve productos, categorías y marcas, y
  el editor se niega a abrirse si el picker no está listo.
- **Permisos**: `create/update/delete` de `proveedores` sin enforcement (solo `view` a nivel clase): un
  Contador (solo lectura) veía y podía usar Nuevo/Editar/Eliminar. Ahora `CreateAjax`, `GetEditData`, `EditAjax`
  y `DeleteConfirmed` exigen su permiso y la vista solo ofrece lo permitido (verificado con rol Contador: 0
  botones, llamadas directas 403).
- Copy inventado en el drawer ("el contacto recibe el envío automático de órdenes de compra") y guía de carga
  eliminados; columna "Productos" en realidad mostraba categorías → "Cobertura"; KPI "Deuda" (en rojo aun en $0)
  era la suma de órdenes no canceladas → "Total en órdenes", en neutro; mapa falso en Details retirado.
- Guardar recarga la página y el aviso lo dibuja el servidor (TempData); se eliminó el HTML de filas duplicado
  en JS. Endpoints POST `Create`/`Edit` clásicos sin llamadores retirados.
- UX: filtros automáticos (sin botón Filtrar), foco de la búsqueda restituido, estados vacíos distintos (sin
  proveedores / sin resultados), errores del servidor junto a cada campo con `aria-invalid`, foco atrapado y
  devuelto, aviso de cambios sin guardar (ESC/fondo/Cancelar), guardado con botón bloqueado, mensajes claros para
  403/sesión vencida, error de productos ya no se muestra como "sin productos", el buscador de productos ya no
  se cierra al hacer scroll.
- Tablet/laptop chica: la tabla entra sin scroll hasta 1024px (Editar/Eliminar quedaban fuera de vista).

Matriz de cobertura: VALIDADA (desktop y mobile, 1440/1280/1024/900/768/390/360, overflow 0, 0 errores de
consola/red): Index con datos, vacío sin proveedores, vacío por filtro, filtros automáticos, drawer Nuevo
(errores de servidor, CUIT duplicado, dirty-guard, alta), drawer Editar desde Index y desde Details (persistencia
de productos/categorías/marcas), Details, confirmación y baja (en clon), vista solo lectura (Contador).
NO VALIDADA: baja bloqueada por órdenes/cheques (el clon no tiene órdenes), conflicto de concurrencia
(RowVersion), estado de error de carga del Index (catch del controller), baja de proveedores con datos reales.

Tests: `ProveedorModuloContractTests` (6 casos: permisos, cobertura completa en el drawer, drawer único,
labels). Suite completa 5020/5020 (4 omitidos previos), build 0 errores. Riesgo pendiente: la app corriendo
del usuario necesita reinicio para tomar los cambios de C# (permisos, TempData); Razor/JS/CSS se refrescan solos.

## Stock — cierre de módulo `/ui-module` (Movimientos, Kardex, Inventario global; 2026-09-25, sin commit)

Pantallas: `MovimientoStock/Index`, `MovimientoStock/Kardex/{id}`, `Producto/UnidadesGlobal` (con su pestaña
Movimientos), `MovimientoStock/Create` y el modal de ajuste; el parcial del listado también alimenta la pestaña
Movimientos de Catálogo.

Hallazgos corregidos (todos verificados en navegador):
- **Bug de datos:** Index, la pestaña Movimientos y `ListJson` mostraban la hora en UTC (3 h de diferencia con
  Kardex); ahora todo en hora local. El filtro por fecha del Kardex usa la fecha local.
- **Control falso:** el selector "Promedio pond. / PEPS (FIFO)" del Kardex no calculaba nada; retirado.
  "Exportar" solo imprimía (duplicaba "Imprimir"): retirado; queda "Imprimir / PDF".
- **Copy honesto:** KPIs del Kardex dicen "Total histórico" (no reaccionan a filtros); "Costo promedio" era el
  último costo → "Último costo". Los totales del pie se recalculan con las filas visibles y aparece un
  estado vacío por filtros con "Limpiar filtros". Stats de Index con unidad explícita (movimientos / unidades).
- Chip "Salida" del Kardex pasa a rojo (coherente con su columna y con Ficha); banner de Index al tono info.
- Index: columna "Fuente costo" integrada bajo "Costo total" (la acción "Ver kardex" quedaba fuera de vista
  a 1440px); importes/fechas sin partirse, cifras tabulares.
- Inventario global: subtítulo ya no contradice las pestañas; el vacío sin filtros dice "Todavía no hay
  unidades físicas registradas" (antes "con los filtros actuales"); Estado y los atajos "Solo …" se excluyen
  entre sí (combinarlos daba 0 sin explicación); dropdown de estados con etiquetas legibles; el link de
  fallback del header solo se ve sin JS; la pestaña Movimientos limita a 50 filas, muestra signo y unidades;
  tabs con `aria-controls`, `tabpanel` y flechas de teclado.

Matriz de cobertura: VALIDADA en 1440/1280/768/390/360 (overflow 0, 0 errores de consola/red): Index con datos,
vacío por filtro, modal de ajuste (abre/Esc), Kardex con datos, filtrado, vacío por filtro y limpiar, modal de
ajuste, envío real del ajuste (en clon de DB: 3→5 u, aviso de éxito y fila nueva), Create standalone, Inventario global con datos/vacío/vacío por filtro, pestaña Movimientos (desktop y
mobile), pestaña Movimientos de Catálogo (overflow/consola).
NO VALIDADA: Unidades globales con
unidades reales (la base no tiene ninguna), paginación de Index (12 movimientos = 1 página).

Tests: 331/331 (MovimientoStock, Catálogo, ProductoUnidad), build 0 errores. Riesgo: la app corriendo del usuario
necesita reinicio para ver los cambios de C# (`ListJson`, etiquetas del dropdown); Razor/JS se refrescan solos.

## Tickets — cierre de módulo `/ui-module tickets` (2026-09-25, sin commit)

Pantallas: `Ticket/Index`, `Ticket/Details/{id}`, panel lateral de incidentes, modal de cambio de estado
(individual y masivo), confirmación de eliminar y modal "Reportar incidencia".

Hallazgos corregidos (verificados en navegador, en una clon de la DB):
- **Acciones inalcanzables:** en Index la columna `Acciones` quedaba fuera de vista por scroll horizontal
  (tabla de 1513px) y cada fila medía 253px por 5 botones apilados. Ahora: 6→4 columnas (Reportado y Origen
  pasan a la línea de contexto del ticket), botones solo-icono en una fila y columna fija a la derecha;
  filas de ~99px y sin scroll horizontal ≥1280px; en tablet el Tipo se integra a la fila.
- **Details:** el título quedaba aplastado en ~220px (14 líneas) por las acciones en la misma fila; ahora la
  cabecera va sin hero (§4) con las acciones en su propia fila. Descripción vacía con texto de fallback.
- **Hero + 6 tarjetas KPI** (ocupaban el primer viewport y "1 abiertos" bajo "En curso" confundía) →
  pestañas de estado con contador que además filtran (`?estado=`); chip "últimos 7 días" conserva el
  atajo. El select de Estado del filtro se retira (lo cubren las pestañas).
- Filtros en grilla (una fila ≥56rem del card, dos filas en tablet, 2 columnas en mobile); etiquetas
  "Creado desde/hasta"; búsqueda accesible.
- Barra de selección masiva `sticky` (antes quedaba fuera de vista al seleccionar filas del medio).
- Errores y copy: la transición inválida masiva ahora dice "El ticket #4 no puede pasar de Cancelado a En
  Curso. No se modificó ningún ticket." (antes `'Cancelado' → 'EnCurso'`); el modal de estado deshabilita los
  estados no permitidos y ya no usa eyebrow ni jerga ("El backend actual…"); etiquetas de acción unificadas
  entre Index/Details/panel (Marcar en curso / Marcar resuelto / Reabrir / Cancelar).
- "Reportar incidencia": validación con foco en el campo con error y `aria-invalid`, orden de errores igual al
  del formulario, foco/acento del sistema (antes azul legacy); el listado se recarga tras crear.
- Panel: al cerrarlo tras un cambio recarga Index/Details (mostraban el estado anterior); fechas 24 h;
  adjuntos < 1 KB en bytes (antes "0 KB").
- Toasts con `role="status"/"alert"`.

Matriz de cobertura: VALIDADA en 1440/1280/1024/768/390/360 (overflow de página 0, 0 errores de consola/red):
Index con datos, tabs, filtros, vacío por filtro, paginación (2 páginas), cambio de estado individual,
resolución sin descripción (error) y con descripción, cambio masivo (éxito y rechazo transaccional), selección
masiva sticky, eliminar real (clon), Details en Pendiente/En curso/Resuelto con checklist, adjunto y resolución,
panel (abrir/Esc/estado/checklist/adjunto/resolución) en desktop y mobile, modal de estado desktop/mobile,
modal Reportar incidencia (validación y alta real).
También VALIDADA: variante solo-lectura (usuario con solo `tickets.view` en la clon: sin casillas, barra masiva,
estados ni eliminar; Details y panel de solo lectura; POST directo a `CambiarEstado`/`Eliminar` sin efecto),
Details en Cancelado, estado vacío sin filtros (base real) y emulación táctil (`pointer: coarse`, objetivos de
44px, sin overflow). NO VALIDADA: botones solo-ícono en hardware táctil real (solo emulado).

Datos: la LocalDB real tenía un ticket con `Id = 0` (insertado a mano; la app descarta ids ≤ 0, así que no se
podía gestionar). Se marcó `IsDeleted = 1` (reversible). Decisión de diseño mantenida: acciones de fila como
íconos con `title` + nombre accesible (Eliminar separado al borde); la crítica los marcó como reconocimiento
débil y se aceptó por densidad, con etiqueta visible en tarjeta mobile y 44px en táctil.

Fases: contexto → ux-heuristics → Impeccable critique (dual-agent: A diseño 28/40 sin P0, B mecánico: 0 overflow,
0 contraste, 0 sin nombre accesible, 0 errores; 3 mejoras aplicadas: casillas 24px, pestañas 44px en táctil,
ficha sin Estado/Tipo duplicados) → implementación → QA visual → Impeccable polish → QA final. La primera
pasada de critique fue inline y posterior a la implementación; se repitió formalmente con sub-agentes.
Detector de Impeccable: 0 hallazgos. Tests: 2 nuevos
(`TicketServiceEstadoMasivoTests`) + `LayoutUiContractTests` (58/59; el rojo es `Layout_TieneNavItemDashboard`
por cambios ajenos en `_Layout.cshtml`), build 0 errores. Riesgo: la app corriendo del usuario necesita
reinicio para ver el mensaje nuevo de `TicketService`; las vistas no dependen de C# nuevo (verificado: :18787 responde 200) y Razor/JS/CSS se refrescan solos.

### OrdenCompra/Details + Recepcionar unificados (2026-09-25)

Recepcionar deja de ser una pantalla: vive en Details cuando la orden es Confirmada/En tránsito
(columnas Pendiente y A recepcionar, panel lateral con resumen, avance y "Confirmar recepción" encima de
"Cambiar estado"). `GET /OrdenCompra/Recepcionar/{id}` redirige a Details; los errores del POST vuelven a Details.
Los inputs de la tabla se asocian a un `<form>` aparte con `form="form-recepcionar"` (evita forms anidados
con el de Cambiar estado).

- *Validado (clon de la LocalDB):* recepción parcial (67 %, pasa a En tránsito) y completa (Recibida, sin
  panel ni inputs), tope por producto, modal de confirmación, redirect de la ruta vieja, error al
  recepcionar una orden Recibida; 1440/1024/768/390 sin overflow de página; 41 tests de OrdenCompra verdes.
- *NO validado:* rol sin permiso `receive` (el panel se muestra igual y el POST lo rechaza el servidor,
  mismo comportamiento que el botón previo); orden con varios productos.

## Regla para mantener estos documentos

Al cerrar una pantalla:

1. actualizar la tabla y el resumen de esta página;
2. actualizar `ERP-UI-STANDARD.md` únicamente si apareció una regla reusable nueva;
3. no agregar detalles específicos que no generalicen (datos de una corrida de QA,
   cantidades, usuarios de prueba);
4. una pantalla cerrada no se rediseña de nuevo salvo regresión demostrada.

## ConfiguracionPago / MediosPago + CreditoPersonal — unificación (CONFIGPAGO-UNIFICACION-01, 2026-09-25)

- **Una sola ventana:** ambas pantallas comparten título "Configuración de pagos" y la barra de pestañas
  `Views/ConfiguracionPago/_ConfiguracionPagoTabs.cshtml` (Medios de pago | Crédito personal). Cada una conserva
  su URL, su formulario y su contrato backend (fusionarlas en un solo formulario habría mezclado un POST global
  con ~20 acciones AJAX/POST independientes sin ganancia real).
- **MediosPago:** 4 cards de métricas → una fila de chips compactos (en mobile ocupaban una pantalla entera);
  se retira la leyenda de chips "-5% / 0% / +5%" (duplicaba el subtítulo); `confirm()` nativo de "Eliminar
  método" reemplazado por `TheBury.confirmAction` (`data-confirm`); el acceso a Crédito personal también se ve
  en modo solo lectura; nota de tarjeta quitada sin `border-left` de 3px.
- **CreditoPersonal:** 7 secciones → 5 (Resumen, Recargos y cuotas, Límites por puntaje, Perfiles, Punitorios);
  "Semáforo" pasó a sub-bloque de Recargos y cuotas (sin `<details>`: un input inválido oculto bloquea el submit);
  "Reglas canónicas" pasó a un `<details>` del Resumen en lenguaje llano; se eliminaron los rótulos "Sección N" y
  la numeración; texto técnico (`PuntajesCreditoLimite`, `ClienteAptitudService`) reemplazado por lenguaje llano
  (contrato `CreditoPersonalConfigUiContractTests` actualizado deliberadamente); "Guardar configuracion" →
  "Guardar cambios"; Escape ahora cierra también "Agregar cuota" (antes solo "Nuevo perfil"), foco inicial,
  foco atrapado y retorno de foco en ambos modales; el hash `#sN` ya no deja el encabezado con pestañas fuera de vista.
- **Ids preservados:** `s1..s4`, `s7`, `data-target`, formularios y `name` de campos (e2e `configuracion-punitorio` usa `#s7`).
- **Cierre de críticas (mismo día):** cabecera idéntica en ambas pestañas (`.cfgpago-crumb/title/sub` en el partial de
  pestañas; se retiró el CSS `payments-title/subtitle/breadcrumb`), tildes en títulos/nombres de medios y "Límites reales
  por puntaje", copy "canonical" reemplazado, "Volver" duplicado en solo lectura retirado, aviso `beforeunload` al salir de
  Crédito personal con cambios sin guardar, labels de MediosPago asociados por `for/id` (script, también tras el reemplazo
  AJAX del panel), targets táctiles ≥44px (pestañas y `.pay-*` en ≤700px), reset del scroll del hash `#sN`.
- **Validación:** 483/483 tests (`CreditoPersonal|ConfiguracionPago|ConfiguracionPunitorio`); QA Playwright propio en
  1440/1280/768/390/360, 0 overflow, 0 errores de consola/requests; guardado real de Crédito personal (gastos, semáforo,
  cuota nueva, perfil nuevo, validación nativa) y e2e `configuracion-punitorio` 14/14 en un clon de la BD (:5199, ya
  eliminado). Impeccable critique con dos evaluaciones aisladas (A: 25/40; B: detector 0 hallazgos, contraste y foco OK).
- **Deuda cerrada:** importes de "Límites por puntaje" con lectura `= $ 1.234.567` debajo del input (el valor enviado no cambia);
  Perfiles aclara que su rango de cuotas es propio del perfil (lo consume `CreditoConfiguracionHelper`) y no cambia los planes
  globales; `GuardarCreditoPersonalAsync` trata un perfil con Id 0 y nombre existente como edición (test nuevo) en vez de
  insertar un duplicado; cabecera y pestañas quedan en la misma posición en ambas pantallas.
- **Nota:** el e2e de punitorios crea versiones append-only y no es idempotente entre proyectos/viewports: correrlo siempre
  sobre un clon de la BD y de a un proyecto.

### CONFIGPAGO-CREDITO-VISTA-01 (2026-09-25) — refactor de vista de Crédito personal

- **Cabecera compartida compacta** (`_ConfiguracionPagoTabs` + ambas pantallas): sin breadcrumb (duplicaba título y sidebar), h1 1.35–1.75rem, subtítulo sin `min-height` reservado; Crédito personal pasó de ~230px a ~155px de cabecera y en mobile las acciones son una fila con scroll horizontal.
- **Navegación de secciones:** la columna lateral de 220px (y el `<select>` "Ir a" en mobile) se reemplazó por una sola barra de pills bajo la cabecera (wrap ≥640px, scroll con fade <640px); el contenido usa todo el ancho. `data-target`, ids `s1..s4/s7` y hash `#sN` intactos.
- **Sin tarjetas anidadas:** las secciones ya no son una tarjeta con icono grande; el encabezado es título + una línea + acción.
- **Recargos y cuotas:** los tres avisos redundantes (planes activos ×2, fallback) y el párrafo de 5 líneas pasaron a un único estado + dos `<details>` ("Cómo se aplican estos valores", "Cómo se calcula el recargo de un plan"); los planes son filas tipo tabla con una sola cabecera de columnas (Plan | Recargo total | Cuotas sin recargo) en vez de tarjetas con la etiqueta repetida; en <1024px se apilan.
- **Perfiles:** los acordeones `<details>` pasaron a filas tipo tabla (Nombre | Tasa | Gastos | Mín. | Máx. | Orden | Estado + descripción) con cabecera única en >=1280px, grilla de 6 columnas entre 640 y 1279px y apilado en mobile; todos los campos quedan editables sin expandir. Nombres de inputs y binding intactos (crear, editar y activar/desactivar perfil verificados con persistencia real en un clon de la BD, ya eliminado).
- **Límites por puntaje:** 3 columnas en xl, sin insignia numérica duplicada. **Perfiles/Resumen/Punitorios:** solo encabezado y tildes ("crédito", "descripción", "Límite").
- **Barra de guardado:** sólida, no se muestra en Resumen (solo lectura) ni en Punitorios (form propio), en <1024px baja al borde y en mobile solo botones.
- **Contrato de test actualizado deliberadamente:** `ConfiguracionPagoGlobalAdminViewTests` pedía el texto del breadcrumb "Configuracion global de pagos"; ahora pide el h1 "Configuración de pagos".
- **Medios de pago (misma tarea):** el desfase de ~20px entre pestañas era preexistente (`.payments-page` con `padding: 1.25rem` y shell de 1240px ya en HEAD); se alineó el shell de Medios de pago con el de Crédito personal (mismas x/y de cabecera en los 6 viewports, 0 overflow, 0 clipping).
- **Accesibilidad:** `aria-current` + subrayado/negrita en la sección activa (no solo color), anillo de foco en `.cp-field`, `scroll-margin` para que un control inválido enfocado y su mensaje queden sobre la barra sticky (verificado 1440/1024/768/390/360). Tab/Shift+Tab, Space en checkboxes, Enter/Space en `<details>`, modales (foco atrapado, Escape, retorno de foco) y labels verificados.
- **Validación:** 484/484 tests (`CreditoPersonal|ConfiguracionPago|ConfiguracionPunitorio`) y Release build 0 errores tras el último cambio; e2e `configuracion-punitorio` 14/14 (1 skip por diseño) en 1366x768, 768x1024 y 360x740, cada uno sobre un clon limpio de la BD; persistencia real en el clon (gastos, recargo explícito, recargo 0, herencia vacía, activo/inactivo, cuotas sin recargo, semáforo, límite por puntaje: POST 302 + recarga con los valores guardados); QA Playwright propio en 1440/1280/1024/768/390/360 × 5 secciones, 0 overflow, 0 errores de consola/requests. El clon se eliminó.
- **Deuda:** los importes de "Límites por puntaje" siguen siendo `type=number` (contrato de binding).
