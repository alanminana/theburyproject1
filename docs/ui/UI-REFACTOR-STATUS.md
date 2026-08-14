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
