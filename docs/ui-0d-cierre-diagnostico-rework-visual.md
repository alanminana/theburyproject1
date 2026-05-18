# UI-0D - Cierre diagnostico rework visual

Fecha: 2026-05-18
Responsable: Integrador UI-0D
Alcance: consolidacion documental de UI-0A, UI-0B y UI-0C. No se modifico codigo productivo.

## Resumen ejecutivo

La fase UI-0 confirma que TheBuryProject ya tiene una base visual dark-first y un camino canonico inicial: layout global, Tailwind v4, CSS compartido, affordance de scroll horizontal, scripts globales y varios CSS/JS por modulo. El problema principal no es la falta total de sistema, sino la convivencia de componentes compartidos, estilos por modulo, utilidades inline y contratos HTML/JS sensibles.

La decision final es no empezar por Venta ni por un redisenio completo del Layout. El rework debe comenzar con UI-1 Design System documental, seguir con UI-2 Login, UI-3 Home/Dashboard y recien despues UI-4 Layout global con maxima cautela. Luego conviene avanzar por modulos administrativos y de menor riesgo relativo antes de entrar en pantallas transaccionales complejas.

El objetivo del roadmap no es fusionar flujos funcionales. Es unificar lenguaje visual, accesibilidad, contraste, responsive, estados y patrones operativos preservando rutas, permisos, formularios, IDs, `data-*`, `name`, scripts y tests existentes.

## Que hizo Kira - UI-0A

Kira realizo la auditoria visual global del frontend Razor/CSS/JS. Inventario 135 vistas Razor, 18 archivos CSS, 74 archivos JS y 5 archivos de tests `*UiContractTests.cs`. Identifico una base canonica existente en `_Layout.cshtml`, `tailwind-input.css`, `layout.css`, `shared-components.css`, `horizontal-scroll-affordance.css`, `shared-ui.js` y `layout.js`.

El diagnostico visual detecto bajo contraste potencial en texto secundario, exceso de transparencias, blur/backdrop innecesario, heterogeneidad en modales, botones, badges, empty states, loading states y tablas responsive. Tambien marco que `Venta/Create_tw.cshtml`, `Catalogo/Index_tw.cshtml`, Caja, Credito, Seguridad y Dashboard concentran alto riesgo visual u operativo.

Kira propuso consolidar componentes compartidos y priorizar accesibilidad operativa: superficies mas solidas, texto mas legible, focus visible, botones consistentes, tablas mas usables y modales con contrato uniforme.

## Que hizo Juan - UI-0B

Juan realizo el analisis funcional de pantallas, flujos y modulos combinables. Clasifico modulos criticos como Venta, Caja, Catalogo/Inventario, Cliente/DocumentoCliente, Credito, MovimientoStock/Kardex, Devolucion, Reportes, Cotizacion, Proveedor, OrdenCompra, Seguridad y Dashboard/Home.

El aporte central fue separar unificacion visual de unificacion funcional. Juan identifico combinaciones visuales seguras, como Venta + Cotizacion, Cliente + DocumentoCliente, Catalogo + Producto/Unidades + MovimientoStock/Kardex, Caja + Venta, Dashboard/Home + Reportes, OrdenCompra + Proveedor, Devolucion + Venta + ProductoUnidad, y Credito + Cliente + Venta.

Tambien definio que varias zonas deben seguir separadas funcionalmente: Venta y Caja, Venta y Cotizacion, Producto maestro y MovimientoStock, ProductoUnidad e inventario agregado, DocumentoCliente y Cliente maestro, Credito y Venta, Seguridad y modulos operativos, Reportes y Home, Devolucion/RMA/NotaCredito y Venta.

## Que hizo Carlos - UI-0C

Carlos realizo el mapa tecnico frontend-backend para el rework visual. El hallazgo principal es que muchas pantallas tienen contratos HTML/JS fuertes: IDs, `data-*`, `name`, formularios, endpoints JSON, antiforgery, scripts por modulo y tests de contrato. Un cambio "solo visual" puede romper flujos si modifica esos contratos.

El mayor riesgo tecnico esta en Venta/Create y `_VentaCrearModal`, Cotizacion/Detalles, Layout global, Caja/Index, Catalogo/Index, Cliente/Details, Ticket panel/modal, Caja/DetallesApertura, Producto/Unidades y Caja/Cerrar.

Carlos recomendo no crear vistas paralelas para el rework, preservar contratos activos, crear tests antes de pantallas con JS fuerte y evitar modificar controllers/services/ViewModels para polish visual.

## Consensos entre diagnosticos

- Existe una base dark-first reutilizable y debe consolidarse, no reemplazarse masivamente.
- Los componentes canonicos compartidos son el camino correcto para el rework.
- Venta/Create no debe ser la primera pantalla piloto.
- Layout global es transversal y riesgoso; no debe redisenarse completo de golpe.
- Hay que preservar contratos HTML/JS: IDs, `data-*`, `name`, actions, formularios y antiforgery.
- El rework debe ser incremental, por micro-lotes, con validaciones.
- Las pantallas con JS fuerte requieren tests o contrato documentado antes del polish.
- La unificacion visual no implica fusion funcional.
- La prioridad visual es accesibilidad operativa: contraste, baja vision, legibilidad, foco, estados claros y responsive.

## Diferencias de criterio entre agentes

- Kira sugirio como piloto visual `Credito/Index_tw.cshtml` o `Ticket/Index_tw.cshtml`, por ser representativas sin llegar al riesgo de Venta/Create.
- Juan sugirio `Reporte/Index_tw` o `MovimientoStock/Index_tw`, priorizando bajo riesgo funcional y pantallas read-only o semi-operativas.
- Carlos sugirio `MovimientoStock/Kardex_tw.cshtml`, por ser mayormente read-only y permitir probar tablas, badges, resumen y responsive sin JS complejo.

La integracion final toma esos criterios como evidencia de riesgo, pero ajusta el orden de fase: antes de elegir una pantalla operativa piloto se debe cerrar un design system documental, luego validar tono visual en Login, luego ordenar Home/Dashboard y recien despues intervenir Layout global. Esto reduce el riesgo de que cada piloto invente su propio lenguaje visual.

## Decision final de roadmap

No empezar por Venta.

No empezar por redisenar Layout completo de golpe.

Si empezar con UI-1 Design System documental.

Luego UI-2 Login.

Luego UI-3 Home/Dashboard.

Luego UI-4 Layout global.

Luego modulos administrativos como Clientes y Proveedores.

Luego modulos operativos y transaccionales complejos.

## Por que no empezar por Venta

Venta concentra pagos, stock, carrito, unidad fisica, caja, factura, credito, documentacion, autorizaciones, modales internos, APIs y contratos JS densos. `Venta/Create_tw.cshtml` y `_VentaCrearModal.cshtml` dependen de muchos IDs, hidden inputs, `data-*`, endpoints y tests. Empezar por ahi aumenta el riesgo de romper comportamiento observable por un cambio visual.

Venta debe esperar hasta que existan tokens, componentes, reglas de tablas, modales, botones, estados y pruebas suficientes en pantallas menos riesgosas.

## Por que si empezar por Login/Home/Layout

Login es una pantalla aislada, con bajo acoplamiento funcional y buena capacidad para validar tono visual, inputs, botones, errores, foco, mobile y baja vision sin tocar flujos transaccionales.

Home/Dashboard es la entrada operativa del sistema. Permite ajustar jerarquia y prioridad de informacion: menos enfasis en "lo ganado", mas utilidad diaria, notas rapidas, KPIs razonables, alertas accionables y links a tareas frecuentes.

Layout global debe abordarse temprano porque condiciona todas las pantallas, pero despues de Login y Home para llegar con criterio visual probado. Incluye sidebar, header, navegacion, contenedor, mobile, overlay, dropdowns, tickets, confirm modal y scripts globales.

## Por que Layout es mas delicado que Login/Home

Layout afecta todas las vistas. Cambios en `_Layout.cshtml`, `layout.css`, `layout.js`, `shared-ui.js`, modales compartidos o scripts globales pueden romper navegacion, sidebar mobile, dropdowns, confirmaciones, tickets, notificaciones y carga de scripts por seccion.

Login esta mas aislado. Home/Dashboard es visible y relevante, pero su impacto funcional es menor que el layout. Por eso Layout debe ser una fase propia, con pruebas de contrato y validacion responsive.

## Por que Clientes y Proveedores entran temprano despues de base visual

Clientes y Proveedores son modulos administrativos con alto valor operativo y menor complejidad transaccional que Venta, Caja, Credito o Devolucion. Permiten aplicar el sistema visual a listados, fichas, filtros, formularios, documentos relacionados y relaciones con otros modulos sin empezar por reglas financieras o de stock criticas.

Clientes requiere cuidado por documentos, creditos y BCRA, pero es un buen puente hacia DocumentoCliente y Credito si se trabaja por micro-lotes. Proveedores es buen puente hacia OrdenCompra.

## Pantallas de alto riesgo

- `Views/Venta/Create_tw.cshtml` y `Views/Venta/_VentaCrearModal.cshtml`.
- `Views/Venta/Index_tw.cshtml`, `Details_tw.cshtml`, `Edit_tw.cshtml`, `Facturar_tw.cshtml`.
- `Views/Cotizacion/Detalles_tw.cshtml` por conversion a venta.
- `Views/Caja/Index_tw.cshtml`, `Cerrar_tw.cshtml`, `DetallesApertura_tw.cshtml`.
- `Views/Catalogo/Index_tw.cshtml`.
- `Views/Cliente/Details_tw.cshtml` cuando involucra documentos o BCRA.
- `Views/DocumentoCliente/Upload_tw.cshtml`.
- `Views/Producto/Unidades.cshtml`.
- `Views/Credito/*_tw.cshtml`.
- `Views/Seguridad/Index.cshtml` y partials de permisos/roles.
- `_Layout.cshtml` y scripts globales.

## Pantallas de bajo riesgo relativo

- Login y paginas standalone de acceso, preservando validacion.
- Home/Dashboard si el cambio es de jerarquia visual y no de metricas/backend.
- Listados read-only o de navegacion simple.
- `Views/Reporte/*_tw.cshtml` si se preservan filtros GET y tablas.
- `Views/MovimientoStock/Kardex_tw.cshtml` si se mantiene read-only.
- `Views/Producto/UnidadesGlobal.cshtml` si no se tocan acciones.
- `Views/Cotizacion/Listado_tw.cshtml`.
- Fichas o detalles sin formularios POST ni JS fuerte.

## Modulos combinables solo visualmente

- Venta + Cotizacion: lenguaje visual comun de productos, totales, estados y acciones, sin fusion de flujo.
- Cliente + DocumentoCliente: documentos visibles en ficha, manteniendo cola documental.
- Catalogo + Producto + Inventario + MovimientoStock/Kardex: experiencia visual comun, reglas separadas.
- Caja + Venta: enlaces y lectura cruzada, sin mezclar cierre/apertura con venta.
- Dashboard/Home + Reportes: KPIs compatibles, reportes pesados separados.
- OrdenCompra + Proveedor: resumen y navegacion cruzada, recepcion separada.
- Devolucion + Venta + ProductoUnidad: timeline o lenguaje comun, acciones separadas.
- Credito + Cliente + Venta: paneles consistentes, permisos y auditoria separados.

## Modulos que no deben fusionarse funcionalmente todavia

- Venta y Caja.
- Venta y Cotizacion.
- Producto maestro y MovimientoStock.
- ProductoUnidad e inventario agregado.
- DocumentoCliente y Cliente maestro.
- Credito y Venta.
- Seguridad y cualquier modulo operativo.
- Reportes y Home.
- Devolucion/RMA/NotaCredito y Venta.
- Proveedor y OrdenCompra como maestro/recepcion unica.

## Reglas visuales base

- Mantener dark theme solido.
- Usar alto contraste real para baja vision.
- Reducir transparencias.
- Reducir blur/backdrop.
- Evitar negro puro como superficie principal.
- Hacer texto secundario mas legible.
- Garantizar focus visible.
- Mostrar estados con color + texto, y cuando sea critico tambien borde o icono.
- Usar tablas responsive con affordance clara, foco y lectura en mobile/notebook.
- Normalizar modales: tamanios, overlay, header, footer, orden de acciones y scroll interno.
- Normalizar botones y acciones: primaria, secundaria, ghost, warning, success, danger.
- Evitar `transition-all`; transicionar propiedades concretas.
- Respetar `prefers-reduced-motion`.
- Evitar estetica decorativa o de landing page SaaS en pantallas operativas.

## Reglas tecnicas

- Preservar IDs, `data-*`, `name`, `asp-action`, `asp-controller`, `method`, antiforgery y hidden inputs usados por JS o model binding.
- No tocar controllers/services/ViewModels para polish visual.
- No tocar migraciones ni entidades en tareas visuales.
- No crear vistas paralelas salvo justificacion explicita o prototipo descartable.
- Modificar vistas canonicas existentes en micro-lotes.
- Revisar JS asociado antes de mover HTML.
- Crear tests antes de pantallas con JS fuerte o contratos fragiles.
- Mantener `data-oc-scroll*` en tablas anchas.
- Mantener modales en la relacion DOM que esperan los scripts.
- Validar build, tests aplicables y `git diff --check` por lote.
- Documentar componentes legacy, duplicados o inciertos detectados.

## Roadmap recomendado completo

### Fase UI-1 - Design system operativo dark accesible

Documento de tokens y componentes base. No tocar pantallas todavia. Definir color, superficies, texto, borde, foco, estados, botones, inputs, selects, tabs, tablas, modales, alerts, badges, empty/loading/error states y reglas responsive.

### Fase UI-2 - Login visual

Pantalla aislada para validar tono visual, inputs, botones, errores, foco, mobile, baja vision y tokens standalone.

### Fase UI-3 - Home/Dashboard

Reordenar prioridad: menos enfasis en "lo ganado", mas utilidad operativa, notas rapidas, KPIs razonables, alertas accionables y links de accion.

### Fase UI-4 - Layout global

Sidebar, header, navegacion, contenedor, mobile, alto contraste, dropdowns, overlay, scripts globales y modales compartidos. Ejecutar con maxima cautela.

### Fase UI-5 - Componentes base compartidos

Botones, cards, inputs, selects, tablas, badges, alerts, empty/loading/error states. Consolidar aliases o wrappers sin romper clases por modulo.

### Fase UI-6 - Clientes

Listado, ficha, formularios y documentos relacionados visualmente. Mantener cola documental separada.

### Fase UI-7 - Proveedores

Listado, filtros, formularios y relacion visual con OrdenCompra.

### Fase UI-8 - Catalogo / Producto / Inventario

Unificar experiencia visual sin fusionar reglas funcionales. Preservar Catalogo como hub, Producto como maestro/formulario e Inventario como operacion de unidades.

### Fase UI-9 - MovimientoStock / Kardex

Pantalla operativa piloto segura para tablas, historial, signos, badges y responsive. Mantener auditoria separada.

### Fase UI-10 - Caja

Con cautela por saldos, turnos, ventas vinculadas, acreditaciones, cierre, arqueo y movimientos.

### Fase UI-11 - Cotizacion

Unificar visualmente con Venta, sin fusionar flujo funcional. Mantener conversion como puente explicito y auditable.

### Fase UI-12 - Venta

Alta complejidad: pagos, stock, carrito, unidad fisica, caja, factura, credito, documentacion, autorizaciones y modales.

### Fase UI-13 - Credito

Alta sensibilidad por documentos, cuotas, scoring, cupos, contrato, venta y permisos.

### Fase UI-14 - DocumentoCliente

Integracion visual con ficha cliente, manteniendo cola documental, upload, verificacion, rechazo, batch y retorno contextual.

### Fase UI-15 - Devolucion / Garantia / RMA

Integracion visual con Venta y Unidad fisica. Mantener impactos de stock, caja, cliente y nota de credito separados.

### Fase UI-16 - OrdenCompra

Integracion visual con Proveedor. Mantener recepcion y estados propios.

### Fase UI-17 - Reportes

Read-only, claridad visual, filtros, tablas, exportaciones y lectura analitica.

### Fase UI-18 - Seguridad / Usuarios / Roles

Cautela por permisos, roles, auditoria y administracion.

## Que puede hacerse en paralelo despues de UI-0D

- UI-1 Design System documental puede avanzar en paralelo con inventario de tokens/componentes, siempre sin tocar pantallas.
- Preparar tests de contrato faltantes para Layout, Login, CajaIndex, CajaCerrar, ProductoUnidades, MovimientoStock/Kardex, CatalogoIndex y TicketShared puede hacerse en paralelo por archivos de test disjuntos.
- Auditoria puntual de accesibilidad de Login/Home/Clientes/Proveedores puede hacerse en paralelo como documentacion.
- Relevar capturas o baseline visual por pantalla puede hacerse en paralelo si no modifica codigo.
- Preparar checklist de criterios de aceptacion por modulo puede hacerse en paralelo.

## Que debe hacerse secuencialmente

- UI-1 debe preceder cambios visuales amplios.
- UI-2 Login debe validar tono visual antes de llevarlo a Home/Layout.
- UI-3 Home/Dashboard debe preceder UI-4 Layout para evitar cambiar la entrada operativa con layout inestable.
- UI-4 Layout debe cerrarse antes de reworks masivos de modulos.
- UI-5 componentes base debe estabilizarse antes de aplicar cambios repetidos en Clientes, Proveedores, Catalogo, Caja, Cotizacion o Venta.
- Venta debe esperar a Cotizacion, Caja y componentes compartidos mas maduros.
- Credito, DocumentoCliente, Devolucion/RMA y Seguridad requieren tests o contratos antes del rework visual fuerte.

## Checklist de cierre UI-0

- [x] UI-0A integrado.
- [x] UI-0B integrado.
- [x] UI-0C integrado.
- [x] UI UX Pro Max revisado antes de cierre.
- [x] Sin `pycache` ni `*.pyc` trackeados.
- [x] Documento UI-0D creado.
- [x] Roadmap final definido.
- [x] Decision final documentada: no empezar por Venta ni por Layout completo.
- [x] Reglas visuales base documentadas.
- [x] Reglas tecnicas de preservacion de contratos documentadas.
- [x] Build Release final ejecutado.
- [x] `git diff --check` final ejecutado.
- [ ] Push de `main` ejecutado.

## Proximos prompts recomendados

### UI-1

Actua como Arquitecto Frontend / UX accesible. Crear `docs/ui-1-design-system-dark-accesible.md` con tokens, componentes base, reglas responsive, accesibilidad, baja vision, estados, tablas, modales y criterios de aceptacion. No tocar codigo productivo.

### UI-2

Actua como desarrollador frontend ASP.NET MVC. Aplicar el design system aprobado a Login en un micro-lote aislado. No tocar controllers/services. Preservar validacion, names, antiforgery y flujo de autenticacion. Ejecutar build y validaciones UI aplicables.

### UI-3

Actua como UX/UI engineer de ERP. Reordenar Home/Dashboard para utilidad operativa, notas rapidas, KPIs razonables y links accionables. No cambiar calculos ni origen de datos sin decision explicita.

### UI-4

Actua como integrador frontend. Preparar y ejecutar rework incremental del Layout global con tests de contrato previos para sidebar, header, dropdowns, confirm modal, tickets, scripts globales y responsive.

## Deuda y riesgos remanentes

- Faltan tests de contrato para varias pantallas con JS fuerte.
- `Create_tw_legacy.cshtml` existe y no debe usarse como referencia visual.
- Hay vistas `_tw` y no `_tw` conviviendo; cada modulo requiere verificacion de ruta/controller antes de tocar.
- `site.css` y `dark-theme.css` parecen residuales o legacy, pero no deben eliminarse sin evidencia.
- Home y Dashboard tienen solapamiento conceptual que debe resolverse por utilidad operativa, no por estetica.
- La consolidacion visual puede afectar muchas vistas si se toca `shared-components.css`; requiere validacion representativa.
- Layout global sigue siendo el frente transversal mas delicado.
