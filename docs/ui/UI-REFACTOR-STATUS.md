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
