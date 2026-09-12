# UI Refactor Status

Mapa rápido de qué pantallas ya siguen [ERP-UI-STANDARD.md](./ERP-UI-STANDARD.md) y cuáles
faltan. Actualizar esta tabla al cerrar cada pantalla.

| Área | Pantalla | Estado | Referencia |
|---|---|---|---|
| Global | `_Layout` / Foundation | ✅ Cerrado | `ERP-UI-STANDARD.md` |
| Venta | `Index` | ✅ Cerrado | `79b4c91`, `e1de859`, `3225b95` |
| Venta | `Create` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad |
| Venta | `Edit` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad |
| Venta | `Details` | ✅ Cerrado | serie VENTA-DETAILS (ver resumen abajo) |
| Cotización | `Simular` (`_CotizadorForm.cshtml`) | ✅ Cerrado | serie COTIZACION-SIMULAR-REDESIGN (ver resumen abajo) |
| ConfiguracionPago | `MediosPago` | ✅ Cerrado | ver resumen abajo |
| Caja | Abrir / Cerrar / RegistrarMovimiento / DetallesApertura / DetallesCierre / Historial / Conciliación | ✅ Cerrado | ver resumen abajo |

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

- reapertura VENTA-INDEX-FILTRO-FANTASMA-01: auditoría de 4 capas en vivo (Playwright
  contra la app real, no solo lectura de código) detectó un hallazgo no documentado en
  cierres previos. `pendientesAutorizacion`, `cotizacionesPresupuestos` y
  `devolucionesElegibles` se calculan sobre `ventas` (el conjunto ya filtrado por el
  controller vía `Buscar venta`/filtros avanzados — decisión intencional de VENTA-UI-04B,
  documentada en el comentario de `Index_tw.cshtml`), pero el indicador "Filtros activos"
  y el control para quitarlos vivían solo dentro de `panel-operaciones`. Resultado
  reproducido en vivo: con un filtro que no matchea nada, la pestaña Pendientes pasaba de
  "20" a mostrar "Sin pendientes de autorización" (ícono verde de estado resuelto) sin
  ninguna señal de que un filtro residual, no la ausencia real de pendientes, explicaba el
  vacío — mismo mecanismo en Cotizaciones y presupuestos y en Devoluciones. Se descartó
  hacer que esas 3 pestañas ignoren el filtro (revertiría una decisión de diseño ya
  documentada) a favor de comunicar el estado real: se agrega el mismo indicador ya usado
  en Operaciones (`pill pill-blue` "Filtros activos" + link "Limpiar filtros" a
  `asp-action="Index"`, mismo componente `panel-heading__meta` que ya usaba Devoluciones
  para "Ver historial") a los 3 `panel-heading` de Pendientes/Cotizaciones/Devoluciones,
  condicionado a `filtroActivo` (variable ya existente, sin nueva lógica). Sin cambios de
  cálculo, reglas de negocio, ids, `data-*` ni contratos backend/JS; 0 CSS nuevo (reusa
  `.pill`, `.pill-blue`, `.panel-heading__meta`, `.btn-ghost`, ícono `clear_all`, ya
  definidos). Validado en vivo con Playwright (segunda pasada sobre la instancia :18787
  del usuario, solo navegación de lectura) en 1440×900 y 390×844: filtro sin resultados
  aplicado desde Operaciones → Pendientes/Cotizaciones/Devoluciones muestran el indicador y
  "Limpiar filtros" restaura el badge "20" y el listado completo; sin overflow horizontal,
  0 errores/warnings de consola; build 0 errores; 7/7 tests focalizados
  (`VentaControllerIndexPaginacionTests`) verdes.

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

- reapertura VENTA-CREDITO-CTA-SIN-COLOR-01, a partir de reporte directo del usuario sobre
  `Venta/Edit`: el botón "Continuar a revisión" (`_ConfigurarVentaEmbebida.cshtml`, sólo
  visible cuando `Model.CreditoEstaConfigurado`) "no tiene color y no se nota". Causa raíz
  verificada en código: el botón sólo llevaba `class="btn btn-block btn-sm"` — `.btn`
  (`credito-module.css`) es únicamente estructura (padding, radio, tipografía); el color de
  fondo/texto lo aporta siempre una clase variante (`.btn-primary`/`.btn-success`/
  `.btn-ghost`/`.btn-soft`/`.btn-amber`/`.btn-danger`), que acá faltaba — el botón quedaba
  sin fondo, ilegible contra el fondo oscuro del panel. Fix: se agrega `btn-soft` (variante
  secundaria neutra ya definida, usada para CTAs no-primarios en el mismo archivo) — queda
  visible sin competir con el verde de "Guardar configuración" (`btn-success`), que sigue
  siendo el único CTA primario del bloque. Sin cambios de cálculo, reglas de negocio, ids,
  `data-*` ni contratos backend/JS. Validado: build 0 errores, 335/335 tests focalizados
  (incluye `VentaCreditoRedesignVisual*`, que ya cubren `data-credito-embebido-continuar-
  revision`) verdes; validado en vivo con Playwright sobre VTA-202608-000080 (paso Crédito,
  crédito ya configurado) — "Continuar a revisión" queda visible (fondo slate, borde) sin
  competir con el verde de "Guardar configuración", 0 errores de consola.

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

- reapertura VENTA-DETAILS-OPTIMIZACION-01, a partir de una auditoría de 4 capas completa
  pedida por el usuario (no un reporte de bug puntual). 5 hallazgos nuevos no cubiertos por
  H11-H13 (que se habían enfocado en Total/N° de factura/Crédito Personal/N° de contrato):
  - el campo "Cliente" (nombre + documento) de la grilla "Información de la venta" repetía
    exactamente el mismo dato, con el mismo peso tipográfico, que la card "Cliente" del hero
    inmediatamente arriba sin scroll de por medio (en mobile, layout de 1 columna, quedan uno
    debajo del otro) — a diferencia de Total/N° de factura (H11-H13, mantenidos porque cada
    aparición cumple un rol distinto), acá no había cálculo ni contexto adicional agregado.
    Se retira el campo de la grilla (pasa de 4 a 3 columnas); el hero ya lo muestra con más
    jerarquía. Como el botón "Imprimir" usa `window.print()` genérico sin hoja de estilos de
    impresión propia (deuda ya documentada), la duplicación se trasladaba también a cualquier
    impresión de la pantalla;
  - el badge "Usa pago principal" se repetía idéntico en cada línea de la tabla de productos
    que no tuviera una excepción de forma de pago — el caso común, sin aportar nada a decidir
    (§14: "mismo estado repetido"). Ahora solo se muestra badge cuando la línea realmente
    tiene una excepción;
  - reordenamiento: con autorización pendiente, la card "Acciones" (Autorizar/Rechazar) ya
    era visible sin scroll en la columna derecha en desktop (`lg:items-start`, columnas en
    paralelo), mientras que el motivo/las razones para decidir quedaban al final de la
    columna izquierda, después de Información/Detalle de productos/Crédito/Documentación/
    Facturación si existían. La card "Autorización" se extrajo a
    `_VentaAutorizacionPanel.cshtml` (mismo patrón que `_VentaRazonesAutorizacion.cshtml`)
    para poder llamarla desde 2 posiciones sin duplicar el markup: al principio de la
    columna izquierda cuando `Model.DebeAlertarAutorizacionPendiente` (a la misma altura que
    "Acciones" también en desktop), o en su posición original al final cuando ya está
    resuelta (solo registro histórico). En mobile las columnas ya se apilaban en este mismo
    orden relativo, así que no cambia nada ahí;
  - accesibilidad: los botones "cerrar" (ícono `close` solo, sin texto) de los modales
    "Emitir factura" y "Anular factura" no tenían `aria-label` ni `title` — viola §11 del
    estándar ("nombres accesibles en botones con solo ícono"). Se agrega
    `aria-label="Cerrar"` a ambos;
  - `venta-page-wizard.css` (1056 líneas) se cargaba también en Details/Autorizar/Rechazar/
    Cancelar/Delete vía `_VentaModuleStyles.cshtml` aunque ninguna de esas 5 vistas usa
    clases de wizard (verificado con grep, 0 matches) — solo Create/Edit lo necesitan. Se
    separa a `_VentaWizardStyles.cshtml`, incluido explícitamente solo en Create_tw/Edit_tw;
    las otras 5 vistas dejan de pagar ese payload sin cambio visual.
  El resto de las duplicaciones ya evaluadas en H11-H13 (Total en 7 lugares, N° de factura,
  N° de contrato, chips de Fecha/Forma de pago) se revisaron de nuevo y se reconfirmaron sin
  evidencia nueva para reabrirlas — no se tocan. Sin cambios de cálculo, reglas de negocio,
  ids ni contratos backend. Comparación de referencia contra `Venta/Index` (§16): confirmó
  buen uso compartido del lenguaje visual (`hero-erp`, `card-erp-panel`, `badge-erp-*`); el
  mapeo estado→color queda documentado como duplicado de forma independiente entre ambas
  vistas (Index vía clases `.pill-*` scopeadas + helper `@functions`, Details vía switch de
  Tailwind inline) — no bloqueante, no se unifica en este lote (crearía una autoridad visual
  nueva sin pedido explícito). No se pudo hacer una pasada de Playwright en vivo en este
  lote: el navegador MCP quedó tomado por otra sesión activa del usuario sobre el mismo
  proyecto durante todo el desarrollo; validado en cambio con 1198/1198 tests de contrato UI
  relacionados con Venta verdes (incluye los 4 tests actualizados/agregados para reflejar el
  nuevo contrato: badge sin excepción, y las 2 posiciones de `_VentaAutorizacionPanel`) y
  build 0 errores (vía `bin/valrun`, sin tocar la instancia :18787 del usuario). Pendiente
  para el próximo cierre: repetir la validación visual en vivo con Playwright cuando el
  navegador quede libre, especialmente el caso de autorización pendiente en 1440×900/390×844.

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

- reapertura COTIZACION-SIMULAR-OVERLAP-01, a partir de reporte directo del usuario (con
  capturas) de íconos superpuestos con el texto tipeado en varios campos del cotizador
  (`_CotizadorForm.cshtml`, embebido en la pestaña Cotizar de `Venta/Create`/`Edit` y en
  `Cotizacion/Index_tw`). Causa raíz verificada en código, no visual únicamente:
  `cotizacion-simulador.css` no usa `@layer` — por reglas de CSS Cascade Layers, cualquier
  regla fuera de un `@layer` le gana a las utilities de Tailwind (`@layer utilities`) sin
  importar el orden de las hojas ni la especificidad, mismo mecanismo ya documentado para
  `.hidden` en VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01. El padding shorthand de
  `.cotz-app .field { padding: .5rem .65rem }` anulaba por completo `.pl-8`/`.pl-6`/`.pr-6`
  (usadas para dejar hueco a un ícono absoluto: lupa de "Buscar nombre, código o ID…" y de
  "Nombre, DNI o CUIT…", "$" de Anticipo e Importe de descuento general, "%" de Porcentaje
  de descuento general), dejando el padding lateral real en `.65rem` — el texto quedaba
  tapado por el ícono en los 5 campos. Fix: 3 reglas nuevas (`.field.pl-8`/`.field.pl-6`/
  `.field.pr-6`, mismos valores que ya define Tailwind con `--spacing: .25rem`) que
  restauran el padding sólo del lado que trae el ícono, sin tocar `.field` en general ni
  crear una nueva autoridad de espaciado. Sin cambios de cálculo, reglas de negocio, ids
  ni contratos backend/JS. Validado: build 0 errores, 335/335 tests focalizados
  (`VentaCreditoRedesignVisual*`, `VentaCreateUiContractTests`, `Cotizacion*`) verdes.

  Validación en vivo (Playwright, una vez liberado el navegador MCP) encontró que el fix
  de arriba no alcanzaba dentro de `Venta/Create`/`Edit`: el cotizador se embebe dentro de
  `#venta-create-page`/`#venta-edit-page`, y `venta-page-wizard.css` trae un reset genérico
  de inputs con selector `#venta-create-page input[type="text"], ... input[type="number"],
  ...` — un ID en la cadena de selectores le gana en especificidad a `.cotz-app
  .field.pl-8` (3 clases) sin importar cuántas clases se sumen ahí, así que el padding
  seguía cayendo a `.75rem`/`.8rem` (el reset del wizard) en vez de a los 2rem/1.5rem del
  fix. Es el mismo patrón que ya resolvió `.cotz-app .total-display` más arriba en este
  mismo archivo (línea ~39, borde ámbar heredado de `venta-page-wizard.css` por el mismo
  mecanismo). Fix: `!important` en las 3 reglas — no hay forma de ganarle a un selector con
  ID sólo con clases, y el archivo ya documenta y usa este mismo escape para el caso
  gemelo. No aplica en `Cotizacion/Index_tw` standalone (sin ese ancestro), donde el fix
  sin `!important` ya alcanzaba, pero el `!important` ahí es inocuo (mismo valor, sin
  competencia). Validado en vivo con Playwright, logueado como usuario real: los 5 campos
  (buscador de productos, buscador de cliente, "$" de Anticipo, "$" de Importe y "%" de
  Porcentaje) muestran padding-left/right correcto (32px/24px/24px medidos por
  `getComputedStyle`, antes 12px) tanto en `Venta/Create` (1440×900, embebido en el
  wizard) como en 390×844 (mobile real); captura visual confirma texto e íconos sin
  superposición; sin overflow horizontal; 0 errores/warnings de consola.

  Segunda pasada sobre el mismo reporte: el hallazgo "card de producto agregado (ej.
  heladera) con espacio desperdiciado / poco prolija" sí tenía causa raíz verificable en
  código, no encontrada en la primera pasada. `.qty-step input` y `.mini` (Cantidad y
  Dto. %/Dto. $ de cada fila del carrito) son `type="number"` sin ningún reset de las
  flechas nativas del navegador — a diferencia de `.stepper` en `credito-module.css`, que
  ya suprime esas flechas para el mismo tipo de componente (custom −/+ + input). El
  spinner nativo se sumaba al ancho fijo de `.qty-step`/`.mini` dentro de una fila ya
  angosta (rail de Productos, 14rem mínimo), angostando el dígito visible y dejando huecos
  irregulares; en los `.field` con sufijo "%" (`pr-6`, Descuento general) el spinner además
  caía debajo del signo "%" posicionado en absoluto. Fix: mismo patrón ya usado en
  `credito-module.css` (`appearance: textfield` / `::-webkit-*-spin-button { appearance:
  none }`), aplicado una sola vez a `.cotz-app input[type="number"]` (cubre `.qty-step`,
  `.mini` y los `.field` numéricos con "$"/"%"). Sin cambios de cálculo, reglas de
  negocio, ids ni contratos backend/JS. Validado: build 0 errores, 226/226 tests
  focalizados (`Cotizacion*`, `VentaWizardAccessibility*`) verdes; validado en vivo con
  Playwright agregando "heladera" al carrito en `Venta/Create` — fila del carrito sin
  flechas nativas visibles, espaciado prolijo entre stepper y Dto. %/Dto. $, sin huecos ni
  recortes; 0 errores de consola.

## ConfiguracionPago / MediosPago — cerrado

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

## Caja — cerrado

Auditoría de 4 capas pedida por el usuario sobre las 8 pantallas full-page del módulo
(`Abrir`, `Cerrar`, `Create`/`Edit`, `DetallesApertura`, `DetallesCierre`, `Historial`,
`RegistrarMovimiento`) más los 5 parciales de Conciliación (`_ConciliacionCards`,
`_ConciliacionTabs`, `_ConciliacionResumenTab`, `_ConciliacionVentasTab`,
`_ConciliacionMovimientosTab`, `_ConciliacionConciliacionTab`, `_ConciliacionAuditoriaTab`),
sin trabajo previo de este estándar (el módulo venía de un rework visual propio ML9/ML10,
`docs/ui-caja-handoff-ml10.md`, no auditado contra `ERP-UI-STANDARD.md`). Diagnóstico +
implementación pedidos explícitamente en el mismo lote:

- **Movimientos ⊂ Libro mayor (redundancia real, no interpretación)**: verificado en
  `CajaConciliacionBuilder.cs` que el tab "Movimientos" y el libro mayor del tab
  "Conciliación" partían del mismo array `movimientos` — el libro mayor solo sumaba la fila
  de apertura y el saldo corriente. Se fusionó "Movimientos" dentro de "Conciliación" (pasa
  de 5 a 4 tabs); sus filtros propios (tipo/dirección, medio, usuario) se absorbieron en el
  libro mayor sin perder capacidad de filtrado — medio y usuario ahora se derivan de los
  datos reales (`ConciliacionLineaViewModel.Usuario`, campo nuevo, puramente aditivo, sin
  tocar ninguna regla de cálculo) en vez de listas hardcodeadas, mismo patrón que ya usa el
  filtro "Estado" de `_ConciliacionVentasTab`;
- **ARIA de tabs incompleto** (§5): `_ConciliacionTabs` tenía `role="tab"`/`aria-selected`
  pero sin `id`/`aria-controls`/`aria-labelledby`/`role="tabpanel"` ni roving tabindex ni
  navegación por flechas — comparado directamente contra la referencia ya cerrada
  `Venta/Index` (`venta-index-rework.js`), que sí los tiene. Se portó el mismo patrón
  completo (ids `cc-tab-*`/`cc-panel-*`, `aria-controls`/`aria-labelledby` bidireccional,
  `tabindex` 0/-1 con roving, `ArrowLeft`/`ArrowRight`/`Home`/`End` en `caja-conciliacion.js`);
- **"Caja física esperada" repetida 3 veces en el mismo tab sin aportar nada la 3ª vez**: card
  fija arriba de los tabs + fórmula del "Resumen de conciliación" + pie del libro mayor, las
  3 dentro de la misma pantalla y sin scroll entre las 2 últimas. Se retiró la 3ª repetición;
  el pie del libro mayor ahora muestra la Diferencia final solo cuando el turno está cerrado
  (el único dato de esa sección que no se veía en ningún otro lado), y no se muestra nada
  cuando el turno sigue abierto (no hay "diferencia final" todavía);
- **Concepto de `RegistrarMovimiento` sin fallback server-side**: el `<select>` no tenía
  ningún `<option>` server-renderizado — un fallo de `caja-registrar-movimiento.js` dejaba un
  campo obligatorio vacío sin ninguna opción elegible. Las 2 `<optgroup>` (Ingresos/Egresos,
  mismos 12 conceptos que ya usaba el JS, sin agregar los conceptos de `ConceptoMovimientoCaja`
  reservados a movimientos generados por el sistema — venta transferencia/Mercado Pago/
  liquidación ML/reversión — que este formulario nunca ofreció) quedan siempre en el HTML; el
  JS ahora oculta la que no corresponde al tipo activo en vez de reconstruir el `<select>`
  desde cero, y lee los sets Ingreso/Egreso del propio DOM en vez de duplicarlos en un array;
- **Trabajo JS muerto en `RegistrarMovimiento`**: 5 `<span hidden>` (`tipo-card-label`,
  `tipo-card-copy`, `tipo-panel-badge`, `tipo-panel-title`, `tipo-panel-copy`) recibían
  texto/clases en cada cambio de tipo pero estaban permanentemente ocultos (atributo `hidden`
  más una regla CSS `!important` que los forzaba); el HTML de referencia que los originó
  (`referencia/Caja/`) ya no existe en el repo para "terminar" un panel visual con ellos, y el
  copy que escribían ya es visible por otro lado (`#tipo-help`). Se eliminaron junto con el
  código JS y CSS que los sostenía;
- **`caja-detalles-apertura.js` huérfano** (128 líneas): ninguna vista actual lo carga
  (`DetallesApertura_tw`/`DetallesCierre_tw` cargan `caja-conciliacion.js`) y sus selectores
  (`data-sale-payment-filter`, `data-merch-filter`, `#tabla-movimientos`, etc.) no existen en
  ningún `.cshtml` vigente — es JS de una versión anterior a la Conciliación actual. Eliminado;
- **`Create_tw`/`Edit_tw` full-page huérfanas**: sin ningún link de entrada real (verificado en
  `_Layout`, `Index_tw`, `Dashboard`, `Venta`), ya documentadas como "legacy, coexiste con
  modal AJAX" desde el handoff ML10, sin tests que las cubrieran. El modal (`_CreateModal_tw`/
  `_EditModal_tw`, cargado vía `CreatePartial`/`EditPartial`) es la única vía real desde
  `Index_tw`, y su submit (`caja-index.js`) siempre postea con `X-Requested-With` — se
  confirmó que el fallback no-AJAX del controller nunca se ejercita en el flujo real. Se
  eliminaron ambas vistas y `caja-form.js` (su único consumidor); `CajaController` pierde las
  2 acciones GET y sus 5 ramas de fallback a esas vistas pasan a redirigir a `Index` con
  `TempData["Error"]` (mismo criterio que ya usa `Delete`), sin tocar la lógica de negocio de
  `CrearCajaAsync`/`ActualizarCajaAsync` ni el contrato AJAX existente;
- **`Abrir_tw` sin estado explícito para "no hay cajas para abrir"**: el `<select>` quedaba
  con el placeholder únicamente cuando el padrón del usuario estaba vacío. Se agregó un aviso
  explícito + `disabled` en el select y el submit cuando no hay ninguna opción real. De paso
  se corrigió una discrepancia real encontrada al implementar esto: `SetCajasActivasSelectListAsync`
  no excluía cajas ya abiertas (`Estado == EstadoCaja.Abierta`) de su propio `<select>`,
  contradiciendo el hint de la pantalla ("Solo se listan cajas disponibles (sin turno
  abierto)") — `AbrirCajaAsync` ya lo rechazaba igual del lado del service, pero el listado
  podía mostrar una caja no operable. Ahora el filtro incluye `Estado != Abierta`.

Sin cambios de reglas de negocio, cálculo, ids/`data-*` de contrato existentes ni rutas.
Validado: build 0 errores (proyecto principal + `TheBuryProyect.Tests`, `dotnet build`),
229/229 tests focalizados de Caja verdes (`--filter FullyQualifiedName~Caja`, incluye 3 tests
de contrato nuevos/actualizados en `CajaDetallesAperturaContractTests` que fijan el nuevo
contrato de 4 tabs + ARIA + Concepto server-renderizado), sintaxis de los 2 JS reescritos
verificada con `node --check`. **Pendiente para el próximo cierre**: no se pudo validar en
vivo con Playwright — el navegador MCP quedó tomado por otra sesión activa del usuario
durante todo el lote (mismo escenario ya documentado en VENTA-DETAILS-OPTIMIZACION-01);
repetir cuando el navegador quede libre, en particular el roving tabindex/flechas de los
tabs, el filtro fusionado del libro mayor y el `disabled` condicional de `Abrir_tw` en los
viewports obligatorios.

- reapertura CAJA-VENTAS-AGRUPACION-01 (2026-09-12): hallazgo del usuario sobre
  `/Caja/DetallesApertura/{id}` — el tab "Ventas del turno" (`_ConciliacionVentasTab.cshtml`)
  ya excluía `VentaTurnoCategoria.Registro` (cotizaciones/presupuestos/canceladas), pero
  mezclaba `Efectiva` (Confirmada/Facturada/Entregada) y `Pendiente` (PendienteRequisitos/
  PendienteFinanciacion) en una sola lista ordenada solo por fecha, sin separación visual —
  regresión funcional respecto al diseño original de 3 bloques de
  `docs/fase-kira-caja-detalle-ventas-ux.md`, que esa fase había resuelto explícitamente por
  el mismo motivo ("confusión operativa" al mezclar estados). Una venta `Confirmada` o
  `Pendiente financiación` (todavía sin facturar, sin ingreso real en caja) terminaba
  compitiendo visualmente en pie de igualdad con una `Facturada`. Fix: la tabla ahora agrupa
  por categoría — "Ventas efectivas" primero, "Operaciones pendientes" después (con leyenda
  "Sin ingreso inmediato — todavía no impactan la caja") — vía una fila de encabezado de
  grupo por sección (`.tbl-group-row`, CSS nuevo en `caja-module.css`), conservando el orden
  por fecha dentro de cada grupo. Categorización de negocio (`VentaTurnoCategoria`,
  `CajaConciliacionBuilder.CategoriaVenta`) sin cambios — el fix es puramente de jerarquía
  visual, no de qué ventas se muestran. Los 4 filtros existentes (medio/estado/impacta/
  cliente) y sus `data-venta-*` no cambiaron; `caja-conciliacion.js` se extendió para ocultar
  el encabezado de un grupo si el filtro deja 0 filas visibles en él (evita un título de
  grupo huérfano sobre una tabla vacía). Nuevo test de contrato
  `Ventas_AgrupaEfectivasYPendientesConEncabezadoPropio`. Sin cambios de ids/`data-*`
  existentes, rutas ni reglas de negocio. Validado: build 0 errores, 230/230 tests
  focalizados de Caja verdes (`--filter FullyQualifiedName~Caja`, incluye el test nuevo),
  sintaxis de `caja-conciliacion.js` verificada con `node --check`. **No validado en vivo con
  Playwright** — mismo bloqueo ya documentado arriba (navegador MCP tomado por otra sesión
  activa del usuario durante todo el lote); pendiente repetir cuando el navegador quede
  libre, en los 5 viewports obligatorios y con los 3 filtros aplicados sobre datos reales.

## Backlog transversal

**P2 — horizontal-scroll-affordance**

El fade derecho del scroll horizontal (componente compartido, `data-oc-scroll-fade`)
puede quedar levemente visible al alcanzar el final del scroll en anchos intermedios
(~768px / 1024px). No bloquea `Venta/Index`. No resuelto todavía.

## Regla para mantener estos documentos

Al cerrar una pantalla:

1. actualizar la tabla y el resumen de esta página;
2. actualizar `ERP-UI-STANDARD.md` únicamente si apareció una regla reusable nueva;
3. no agregar detalles específicos que no generalicen (datos de una corrida de QA,
   cantidades, usuarios de prueba);
4. una pantalla cerrada no se rediseña de nuevo salvo regresión demostrada.
