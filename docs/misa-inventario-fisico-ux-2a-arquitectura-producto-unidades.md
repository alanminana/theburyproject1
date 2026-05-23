# MISA-INVENTARIO-FISICO-UX-2A - Arquitectura UX de Producto/Unidades

Fase: audit-only / documentacion.
Rama: `misa/inventario-fisico-ux-2a-arquitectura-unidades`.
Base: `main` actualizado con `git pull --ff-only`, HEAD `fa059bf`.
Fecha: 2026-05-23.

---

## A. Objetivo

Auditar la arquitectura UX de `Producto/Unidades` despues de las fases 1A-1D y QA, incorporando el feedback del usuario: la pantalla sigue mezclando demasiadas funciones y resulta confusa.

La auditoria busca decidir si conviene reorganizar la pantalla con tabs internos, modales y mejor jerarquia, sin modificar Razor productivo, JS, CSS, backend, tests, endpoints ni payloads.

---

## B. Estado actual de la pantalla

`Views/Producto/Unidades.cshtml` es hoy una pantalla unica de administracion completa de unidades fisicas de un producto. Despues de las fases 1A-1D quedo mas ordenada que la version original:

1. Header del producto con stock agregado, codigo, badge de trazabilidad y link a Kardex.
2. Resumen de unidades fisicas.
3. Filtros.
4. Listado principal de unidades.
5. Alta individual de unidad.
6. Bloque de configuracion y herramientas avanzadas.
7. Trazabilidad individual.
8. Conciliacion stock vs unidades fisicas.
9. Carga masiva.

La mejora anterior fue real: el listado ya no esta enterrado al final y las acciones por fila estan agrupadas en `<details>`. Sin embargo, la pantalla sigue siendo conceptualmente una "consola total": consulta diaria, alta, configuracion, conciliacion y carga masiva conviven en la misma ruta y con peso visual parecido.

La conclusion del QA anterior fue "no abrir UX-2 ahora". Esa conclusion queda superada por feedback real del usuario. La nueva lectura es: la pantalla es funcional y sus contratos estan preservados, pero su arquitectura de informacion todavia no separa suficientemente los modos mentales del operador.

Archivos auditados:

- `Views/Producto/Unidades.cshtml`
- `Views/Producto/UnidadesGlobal.cshtml`
- `Views/Producto/UnidadHistorial.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`
- `docs/misa-inventario-fisico-ux-0-auditoria.md`
- `docs/misa-inventario-fisico-ux-1a-accesibilidad-semantica.md`
- `docs/misa-inventario-fisico-ux-1b-jerarquia-unidades.md`
- `docs/misa-inventario-fisico-ux-1c-mobile-scroll-unidades.md`
- `docs/misa-inventario-fisico-ux-1d-acciones-fila-unidades.md`
- `docs/misa-inventario-fisico-ux-qa-producto-unidades.md`

Lectura cruzada:

- `Controllers/ProductoController.cs`, acciones de unidades, trazabilidad, carga masiva, conciliacion y transiciones.
- Referencias a `ProductoUnidadService`, `IProductoUnidadService`, tests de integracion y contratos de vista.

---

## C. Mapa de funciones actuales

Funciones que conviven hoy en `/Producto/Unidades/{productoId}`:

| Funcion | Tipo | Ubicacion actual | Frecuencia esperada |
|---|---|---|---|
| Identificar producto, codigo y stock agregado | Lectura contextual | Header | Alta |
| Ir a Kardex SKU | Navegacion cruzada | Header | Media |
| Entender stock agregado vs unidades fisicas | Explicacion | Header + conciliacion | Alta para usuarios nuevos |
| Ver resumen por estado | Lectura operativa | KPIs superiores | Alta |
| Filtrar unidades por texto/estado/checks | Operacion diaria | Antes del listado | Alta |
| Ver listado de unidades | Operacion diaria | Tabla principal | Alta |
| Ver historial de una unidad | Auditoria puntual | Accion de fila | Media |
| Marcar faltante | Transicion operativa | Details por fila | Media |
| Reintegrar a stock | Transicion operativa | Details por fila | Media |
| Dar de baja | Transicion destructiva | Details por fila | Baja / sensible |
| Finalizar reparacion | Transicion operativa sensible | Details por fila | Baja / sensible |
| Crear una unidad | Alta individual | Seccion inline | Media |
| Activar/desactivar trazabilidad | Configuracion | Bloque avanzado | Baja / sensible |
| Comparar StockActual vs unidades EnStock | Conciliacion | Bloque avanzado | Media en auditoria, baja en consulta diaria |
| Ajustar stock agregado hacia unidades fisicas | Conciliacion con efecto real | Bloque `#ajuste-asistido` | Baja / muy sensible |
| Crear unidades faltantes desde diferencia | Atajo a alta/carga | Link a carga masiva | Baja |
| Carga masiva con preview/confirmacion | Importacion | Seccion inline final | Baja / eventual |

Vistas relacionadas:

- `UnidadesGlobal.cshtml`: reporte transversal, lectura y navegacion. No compite con la vista por producto.
- `UnidadHistorial.cshtml`: historial individual, lectura enfocada. Correctamente separada.
- `Kardex_tw.cshtml`: movimientos de stock agregado SKU, lectura separada. Correctamente separada por dominio.

---

## D. Funciones principales vs avanzadas

Funciones principales:

- Ver producto, codigo, stock agregado y estado de trazabilidad.
- Ver resumen por estado.
- Buscar/filtrar unidades.
- Revisar listado de unidades.
- Entrar al historial de una unidad.
- Ejecutar una accion por unidad cuando el operador ya sabe que la necesita.
- Agregar una unidad individual.

Funciones avanzadas:

- Activar/desactivar trazabilidad individual.
- Conciliar stock agregado vs unidades fisicas.
- Ajustar StockActual desde conciliacion.
- Carga masiva.
- Interpretar diferencias por signo y decidir entre crear unidades, dejar stock sin identificar o ajustar stock agregado.
- Revisar desglose tecnico de conciliacion: vendidas, faltantes, baja, devueltas, reservadas, en reparacion, ultimos movimientos.

Lectura clave: el listado debe ser el foco principal. Alta individual debe estar cerca del foco, pero no necesariamente expandida siempre. Conciliacion, trazabilidad y carga masiva son herramientas avanzadas y deben quedar accesibles sin dominar la pantalla inicial.

---

## E. Problemas de mezcla funcional

La pantalla mezcla tres modos de trabajo:

1. **Operacion diaria:** buscar unidades, revisar estado, ver historial, marcar/reintegrar.
2. **Configuracion del producto:** activar o desactivar trazabilidad individual.
3. **Conciliacion/auditoria:** comparar StockActual contra unidades EnStock y ejecutar ajustes con impacto en Kardex.

El problema no es que estas funciones existan en una misma ruta. El problema es que la pantalla no obliga al usuario a elegir un modo. Todo queda disponible en el mismo scroll, con cards similares y encabezados similares.

Impactos:

- El operador nuevo no sabe si "stock agregado", "unidades fisicas", "trazabilidad", "conciliacion" y "carga masiva" son pasos de un mismo flujo o herramientas independientes.
- La seccion de conciliacion, aunque esta abajo, sigue siendo muy tecnica y extensa.
- La carga masiva al final parece una continuacion natural del flujo diario, cuando en realidad es una herramienta de importacion/normalizacion.
- Trazabilidad individual vive en la misma pantalla que operaciones por unidad, pero su efecto pertenece a reglas de venta y configuracion del producto.

---

## F. Problemas de jerarquia visual

Hallazgos principales:

- La pantalla usa muchos contenedores visualmente parecidos: `rounded-lg border border-slate-800 bg-slate-950/60`. La separacion por frecuencia de uso existe en el orden, pero no alcanza como arquitectura.
- Los KPIs de resumen, los KPIs de conciliacion y el formulario de carga masiva comparten lenguaje visual de cards. Esto hace que lectura diaria y diagnostico avanzado parezcan equivalentes.
- "Agregar unidad" esta expandido, aunque el usuario que solo vino a revisar listado debe atravesarlo despues de la tabla.
- El bloque "Configuracion y herramientas avanzadas" ayuda, pero agrupa tres cosas muy distintas: regla de venta, conciliacion contable/stock y carga masiva.
- En acciones de fila, `<details>` redujo densidad, pero la tabla sigue alojando acciones sensibles dentro de una celda, no en un espacio deliberado de confirmacion.

Conclusion: la pantalla necesita arquitectura por modos, no solo reorden visual.

---

## G. Problemas mobile

Mobile sigue siendo funcional pero no comodo:

- Tabla de 9 columnas + acciones: requiere scroll horizontal largo. El hint "Desliza para ver mas columnas" ayuda, pero no convierte la tabla en experiencia mobile nativa.
- Acciones operativas dentro de `<details>`: cuando se expanden, la fila crece mucho y el usuario queda dentro de una tabla horizontal, con formularios estrechos.
- Conciliacion puede superar ampliamente el alto de un viewport movil cuando hay diferencia y aparece `#ajuste-asistido`.
- Carga masiva con textarea de series es usable, pero no deberia competir con el listado en una pantalla chica.
- La navegacion por scroll obliga a recordar donde estaba cada herramienta. Tabs o anclas persistentes reducirian carga cognitiva.

Recomendacion mobile: no intentar resolver todo con mas scroll affordance. La arquitectura recomendada debe permitir que el primer viewport mobile sea "producto + resumen + busqueda/listado", y que las herramientas avanzadas se abran bajo demanda.

---

## H. Problemas baja vision

Mejoras ya presentes:

- `scope="col"` en tablas.
- Labels `sr-only` para motivos por fila.
- `aria-disabled="true"` en trazabilidad bloqueada.
- Contraste principal correcto en textos blancos y `text-slate-400`.

Problemas persistentes:

- Hay muchos textos pequeños (`text-xs`) con informacion critica: notas de stock, advertencias, labels KPI, timestamps. Para baja vision, el problema no es solo contraste sino cantidad de microcopy distribuido.
- `text-slate-500` en etiquetas pequeñas puede ser insuficiente si el texto es funcional, no decorativo.
- La seccion de conciliacion exige comparar varios numeros y mensajes interpretativos. En baja vision, esto deberia presentarse como un estado principal muy claro y un detalle expandible.
- La tabla ancha obliga a scroll horizontal. Usuarios con zoom alto pierden contexto de columnas y acciones.

Recomendacion: cualquier rediseño debe convertir el estado de conciliacion en una lectura primaria simple, y mover el desglose tecnico a un panel/tab/modal con jerarquia fuerte.

---

## I. Que deberia quedar visible siempre

Visible siempre en el primer modo de la pantalla:

- Nombre del producto.
- Codigo del producto.
- Stock agregado actual.
- Estado de trazabilidad en forma de badge.
- Link a Kardex SKU.
- Resumen compacto de unidades por estado.
- Estado de conciliacion en formato compacto: "Conciliado" o "Diferencia detectada", con diferencia numerica.
- Filtros principales.
- Listado de unidades como foco principal.
- Accion primaria "Agregar unidad".

No deberia quedar siempre expandido:

- Carga masiva.
- Ajuste asistido de stock agregado.
- Desglose completo de conciliacion.
- Activar/desactivar trazabilidad.
- Formularios sensibles de cambio de estado dentro de todas las filas.

---

## J. Que podria ir a tabs

Tabs internos recomendados, dentro de la misma URL:

1. **Unidades**
   - Resumen compacto.
   - Filtros.
   - Listado.
   - Accion "Agregar unidad".
   - Acciones por unidad.

2. **Alta y carga**
   - Alta individual.
   - Carga masiva.
   - Preview de carga.
   - Advertencia clara: no ajusta StockActual.

3. **Conciliacion**
   - Estado compacto de diferencia.
   - KPIs primarios.
   - Interpretacion por signo.
   - Acciones asistidas.
   - Desglose secundario.

4. **Configuracion**
   - Trazabilidad individual.
   - Explicacion de impacto en venta.
   - Acciones activar/desactivar.

Alternativa mas conservadora: solo dos tabs.

- **Unidades**: listado + alta individual.
- **Herramientas**: trazabilidad + conciliacion + carga masiva.

La alternativa de cuatro tabs es mas clara conceptualmente, pero requiere mas cuidado para no esconder acciones que el usuario necesita encontrar. Para este ERP, la opcion recomendada es tres tabs:

- **Unidades**
- **Carga**
- **Conciliacion**

Y dejar trazabilidad como panel compacto en el header o en un bloque "Configuracion" dentro de Conciliacion/Herramientas. Si el equipo quiere maxima claridad semantica, usar cuatro tabs.

---

## K. Que podria ir a modal

Buenos candidatos a modal:

- **Alta individual de unidad.** Modal corto, foco claro, campos simples, POST tradicional. Debe poder abrirse desde boton "Agregar unidad".
- **Carga masiva.** Modal o drawer amplio. Mejor que inline porque tiene preview, textarea y confirmacion. Si se usa modal, debe soportar estado de preview sin perder ModelState.
- **Acciones por unidad** como marcar faltante, reintegrar, baja y finalizar reparacion. Un modal por accion permite confirmar mejor acciones sensibles, mostrar unidad/codigo/estado y pedir motivo sin inflar la fila.
- **Ajuste asistido de stock agregado.** Modal de confirmacion fuerte, especialmente porque modifica StockActual y Kardex. Debe mostrar antes/despues, diferencia, motivo y advertencia de que no modifica unidades fisicas.

No todos deben convertirse a modal en una misma fase. Prioridad recomendada:

1. Modal de accion por unidad.
2. Modal de alta individual.
3. Modal/drawer de carga masiva.
4. Modal de confirmacion para ajuste de conciliacion.

Riesgo modal: los forms actuales son POST server-rendered con antiforgery y ModelState. Si se modaliza sin JS complejo, conviene mantener forms reales y permitir que errores vuelvan a la misma vista con el modal reabierto mediante estado Razor o hash. No introducir fetch/XHR en esta etapa.

---

## L. Que no conviene separar

No conviene separar en nuevas URLs:

- Listado y filtros del producto: son el nucleo de `/Producto/Unidades/{productoId}`.
- Historial individual: ya esta separado correctamente en `UnidadHistorial`.
- Kardex SKU: ya esta separado correctamente en `MovimientoStock/Kardex`.
- Reporte global: ya esta separado correctamente en `UnidadesGlobal`.

No conviene separar del todo la conciliacion a otra ruta si el usuario necesita revisar el listado antes de ajustar. Mejor tab interno o seccion avanzada, preservando contexto del producto.

No conviene ocultar completamente trazabilidad: debe permanecer visible como estado/badge, aunque la accion de cambiarla sea avanzada.

No conviene convertir la tabla completa a cards desktop. Desktop necesita densidad operativa, escaneo y comparacion.

---

## M. Contratos criticos a preservar

Contratos de ruta y endpoints:

- `GET Producto/Unidades/{productoId:int}`
- `POST Producto/CrearUnidad`
- `POST Producto/CrearUnidadesMasivas`
- `POST Producto/ActivarTrazabilidad/{productoId:int}`
- `POST Producto/DesactivarTrazabilidad/{productoId:int}`
- `POST Producto/MarcarUnidadFaltante`
- `POST Producto/ReintegrarUnidadAStock`
- `POST Producto/DarUnidadBaja`
- `POST Producto/FinalizarReparacionUnidad`
- `POST Producto/AjustarStockAgregadoAUnidadesFisicas`
- `POST Producto/AjustarStockAgregadoHaciaAbajo`
- `GET Producto/UnidadHistorial/{unidadId:int}`
- `GET MovimientoStock/Kardex/{id}`

Contratos HTML/Razor:

- `id="listado-unidades"`
- `id="form-carga-masiva-unidades"`
- `id="ajuste-asistido"`
- `href="#listado-unidades"`
- `href="#form-carga-masiva-unidades"`
- `href="#ajuste-asistido"`
- `name="texto"`, `name="estado"`, `name="soloDisponibles"`, `name="soloVendidas"`, `name="soloSinNumeroSerie"`
- `asp-for="CrearUnidad.ProductoId"`, `CrearUnidad.NumeroSerie`, `CrearUnidad.UbicacionActual`, `CrearUnidad.Observaciones`
- `asp-for="CargaMasiva.ProductoId"`, `CargaMasiva.CantidadSinSerie`, `CargaMasiva.NumerosSerieTexto`, `CargaMasiva.UbicacionActual`, `CargaMasiva.Observaciones`
- `name="CargaMasiva.Confirmar"` con valores `false` y `true`
- `name="ProductoUnidadId"`, `name="Motivo"`, `name="EstadoDestino"`
- IDs dinamicos de motivos: `motivo-faltante-@unidad.Id`, `motivo-reintegrar-@unidad.Id`, `motivo-baja-@unidad.Id`, `motivo-reparacion-@unidad.Id`
- `@Html.AntiForgeryToken()` en todos los POST.
- Partial `_EstadoUnidadBadge`.
- `scope="col"` en tablas.
- `aria-disabled="true"` para trazabilidad bloqueada.

Contratos funcionales:

- Crear unidad no modifica StockActual.
- Carga masiva no modifica StockActual.
- Ajustes de unidad no modifican StockActual.
- Ajuste de conciliacion modifica StockActual mediante `MovimientoStockService` y referencia `ConciliacionUnidad:{productoId}`.
- Backend recalcula conciliacion antes de aplicar ajuste.
- Frontend no decide reglas finales.

Contratos de tests detectados:

- Existen tests de integracion sobre `ProductoControllerPrecioTests` que verifican presencia de acciones, `ajuste-asistido`, conciliacion, filtros, alta, carga masiva y redirecciones a Unidades.
- No se detecto spec Playwright especifico para Producto/Unidades.

---

## N. Propuesta de arquitectura UX recomendada

Recomendacion: rediseñar la pantalla como una **vista por modos con tabs internos + modales/drawers para acciones de escritura**.

Arquitectura propuesta:

### 1. Header persistente compacto

Debe mostrar:

- Producto, codigo, stock agregado.
- Badge trazabilidad.
- Estado compacto de conciliacion.
- Boton Kardex.
- Acciones principales: "Agregar unidad" y "Carga masiva".

El header debe explicar stock agregado vs unidades fisicas en una linea corta o tooltip/help text, no con parrafos repetidos.

### 2. Tab "Unidades"

Pantalla inicial.

Contenido:

- Resumen por estado.
- Filtros.
- Listado.
- Accion de fila "Historial".
- Accion de fila "Gestionar" que abre modal/drawer con acciones disponibles.

Este tab debe ser el foco diario.

### 3. Tab "Carga"

Contenido:

- Alta individual.
- Carga masiva.
- Preview y confirmacion.
- Advertencia: "No modifica StockActual".

Tambien puede reemplazarse por modales accionados desde el header si se quiere una pantalla aun mas enfocada. Si se preservan errores de ModelState de forma simple, tab de carga es mas seguro que modal.

### 4. Tab "Conciliacion"

Contenido:

- Estado principal: Conciliado / Diferencia.
- KPIs primarios.
- Interpretacion por signo.
- Acciones asistidas con motivo obligatorio.
- Desglose secundario colapsable.
- Links a listado y Kardex.

Debe tener copy de decision, no solo numeros. Ejemplo conceptual: "Hay mas stock agregado que unidades disponibles. Puede representar stock sin identificar o stock inflado."

### 5. Configuracion de trazabilidad

Dos opciones viables:

- Panel compacto dentro del header con accion "Configurar".
- Tab "Configuracion" si el equipo prefiere separar claramente reglas de venta.

Recomendacion: panel/modal "Configurar trazabilidad" desde el header. Es baja frecuencia y no necesita tab propio salvo que crezca.

---

## O. Alternativas evaluadas

### Alternativa A - Mantener pantalla actual y solo mejorar copy

Descartada. El feedback del usuario indica confusion estructural, no solo textual.

### Alternativa B - Solo colapsar secciones avanzadas con `<details>`

Mejora parcial, pero insuficiente. La pantalla seguiria siendo una lista larga de herramientas. Reduce scroll, no separa modos mentales.

### Alternativa C - Tabs internos sin modales

Viable y de menor riesgo que modales. Ordena arquitectura sin cambiar endpoints ni payloads. Puede ser la primera microfase de implementacion.

Riesgo: alta individual y carga masiva siguen ocupando espacio dentro de un tab, aunque ya no bloquean el listado.

### Alternativa D - Modales sin tabs

Viable para acciones de escritura, pero no resuelve por completo la conciliacion extensa ni la diferencia entre operacion/configuracion/auditoria.

### Alternativa E - Nuevas URLs separadas

No recomendada ahora. Aumenta navegacion, rompe contexto y puede duplicar contratos. Solo considerar si conciliacion crece mucho o requiere permisos/flujo propio.

### Alternativa F - Cards mobile + tabla desktop

Deseable a futuro para mobile, pero no deberia ser la primera fase. Antes hay que resolver arquitectura de modos.

---

## P. Roadmap de implementacion por microfases

### UX-2B - Tabs internos sin cambiar comportamiento

Objetivo:

- Agregar tabs internos en `Unidades.cshtml`.
- Tab inicial: Unidades.
- Mover alta/carga/conciliacion/configuracion a tabs o paneles dedicados.
- Sin JS complejo si es posible: tabs con anchors/hash o radio/HTML progresivo. Si se usa JS, mantenerlo minimo y sin alterar forms.

Validacion sugerida si se implementa:

- `dotnet build --configuration Release`
- tests de contrato relevantes `ProductoControllerPrecioTests` o filtro `ProductoUnidad|Conciliacion`
- Playwright solo si se agrega spec o si el equipo decide validar visualmente.

### UX-2C - Modal/drawer de acciones por unidad

Objetivo:

- Sacar formularios sensibles de la celda de tabla.
- Mantener "Ver historial" visible.
- "Gestionar" abre modal/drawer con acciones disponibles y motivo.
- Preservar POST tradicional y antiforgery.

### UX-2D - Alta individual y carga masiva como flujo dedicado

Objetivo:

- Decidir si alta individual queda como modal y carga masiva como tab/drawer.
- Preservar preview/confirmacion de carga masiva.
- Reabrir modal/tab correcto si vuelve ModelState con errores.

### UX-2E - Conciliacion enfocada

Objetivo:

- Convertir conciliacion en tab/panel de decision.
- Estado principal claro.
- Desglose secundario colapsable.
- Ajustes con confirmacion fuerte.

### UX-2F - QA visual y contratos

Objetivo:

- Verificar desktop/mobile.
- Revisar baja vision.
- Confirmar anchors, forms, antiforgery, payloads y tests.
- Decidir si hace falta Playwright especifico.

---

## Q. Riesgos

Riesgos principales:

- **ModelState y modales:** si un POST invalido vuelve a la vista, el modal o tab correcto debe quedar visible. Si no, el usuario no vera el error.
- **Anclas existentes:** `#listado-unidades`, `#form-carga-masiva-unidades` y `#ajuste-asistido` pueden estar referenciadas por links internos o tests.
- **Contratos de tests:** tests de integracion buscan strings como `CrearUnidadesMasivas`, `MarcarUnidadFaltante`, `ajuste-asistido`, `AjustarStockAgregadoAUnidadesFisicas`.
- **Acciones sensibles en modal:** el modal no debe bajar la friccion de acciones destructivas. Debe aumentar claridad y confirmacion.
- **Mobile con tabs:** tabs horizontales pueden generar overflow si tienen textos largos. Usar labels cortos y accesibles.
- **Permisos:** no cambiar atributos `[PermisoRequerido]`, endpoints ni visibilidad funcional por rol.
- **Backend como autoridad:** no mover reglas de conciliacion ni validacion al frontend.
- **No mezclar fases:** tabs, modales, conciliacion y mobile cards deben ir por microfases separadas.

---

## R. Proximo prompt recomendado

```text
PROMPT - MISA-INVENTARIO-FISICO-UX-2B - Tabs internos de Producto/Unidades

Actua como Misa y segui AGENTS.md / CLAUDE.md.

Base: main actualizado. Crear rama misa/inventario-fisico-ux-2b-tabs-unidades.

Tipo de fase: Razor/HTML UX, sin backend, sin JS complejo salvo imprescindible.

Objetivo:
Implementar tabs internos en Views/Producto/Unidades.cshtml para separar:
- Unidades
- Carga
- Conciliacion
- Configuracion o trazabilidad

Preservar endpoints, payloads, antiforgery, ids, names, asp-*, partials, anchors y tests.
No cambiar reglas de negocio.

Validar con build y tests relevantes de ProductoUnidad/Conciliacion.
No tocar Ventas/Kira, Catalogo, MovimientoStock ni backend.
```

---

## S. Validaciones de esta fase

No se ejecuto build.
No se ejecutaron tests.
No se ejecuto Playwright.

Motivo: fase audit-only / documentacion. El diff debe contener solo este documento.
