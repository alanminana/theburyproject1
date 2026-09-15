# UI Refactor Status

Mapa rápido de qué pantallas ya siguen [ERP-UI-STANDARD.md](./ERP-UI-STANDARD.md) y cuáles
faltan. Actualizar esta tabla al cerrar cada pantalla.

| Área | Pantalla | Estado | Referencia |
|---|---|---|---|
| Global | `_Layout` / Foundation | ✅ Cerrado | `ERP-UI-STANDARD.md` |
| Venta | `Index` | ✅ Cerrado | `79b4c91`, `e1de859`, `3225b95` + ENVIO-ML (ver resumen abajo) |
| Venta | `Create` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad + ENVIO-ML (ver resumen abajo) |
| Venta | `Edit` | ✅ Cerrado | `_VentaWizardForm.cshtml` + tests de paridad + ENVIO-ML (ver resumen abajo) |
| Venta | `Details` | ✅ Cerrado | serie VENTA-DETAILS (ver resumen abajo) + ENVIO-ML (ver resumen abajo) |
| Cotización | `Simular` (`_CotizadorForm.cshtml`) | ✅ Cerrado | serie COTIZACION-SIMULAR-REDESIGN (ver resumen abajo) + ENVIO-ML (ver resumen abajo) |
| ConfiguracionPago | `MediosPago` | ✅ Cerrado | ver resumen abajo |
| Dashboard | `Index` | ◐ Foundation aplicada | header en `.hero-erp` + tabs Vencidas/Próximas con patrón ARIA completo (ver resumen abajo); resto sin auditoría de 4 capas |
| Cliente | `Nuevo cliente` (drawer Create) | ✅ Cerrado | serie wizard Cliente (ver resumen abajo) |
| Cliente | `Details` | ✅ Cerrado | serie CLIENTE-DETAILS-TABS (ver resumen abajo) |

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

## Regla para mantener estos documentos

Al cerrar una pantalla:

1. actualizar la tabla y el resumen de esta página;
2. actualizar `ERP-UI-STANDARD.md` únicamente si apareció una regla reusable nueva;
3. no agregar detalles específicos que no generalicen (datos de una corrida de QA,
   cantidades, usuarios de prueba);
4. una pantalla cerrada no se rediseña de nuevo salvo regresión demostrada.
