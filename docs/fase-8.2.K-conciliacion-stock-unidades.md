# Fase 8.2.K - Conciliacion stock agregado vs unidades fisicas

**Estado:** Diagnostico y decision funcional V1
**Fecha:** 2026-05-14
**Agente:** Fase 8.2.K - Conciliacion stock agregado unidades
**Alcance:** documentacion solamente. Sin cambios de codigo, migraciones, Caja, Seguridad, pagos, `VentaService`, `MovimientoStockService` ni reglas de transicion.

---

## A. Diagnostico tecnico actual

### Componentes revisados y clasificacion

| Componente | Clasificacion | Evidencia | Decision |
|---|---|---|---|
| `Producto.StockActual` | Canonico para stock agregado por SKU | `MovimientoStockService.RegistrarAjusteAsync`, `RegistrarEntradasAsync` y `RegistrarSalidasAsync` actualizan este campo. Ventas usa `RegistrarSalidasAsync` al confirmar y `RegistrarEntradasAsync` al cancelar. | Mantener como autoridad agregada V1. |
| `MovimientoStock` | Canonico para kardex agregado por SKU | Entidad con `ProductoId`, `Tipo`, `Cantidad`, `StockAnterior`, `StockNuevo`, costo, `Referencia`, `OrdenCompraId` y `Motivo`. `MovimientoStockController` y `Kardex_tw` lo consumen. | Mantener separado de unidades fisicas. |
| `MovimientoStockService` | Canonico para mutar stock agregado | Centraliza entradas, salidas y ajustes. Los tests caracterizan incremento, decremento, ajuste absoluto, batch y que `CreateAsync` no modifica stock. | No modificar en esta fase. |
| `ProductoUnidad.Estado` | Canonico para disponibilidad fisica individual | `ProductoUnidadService.ObtenerDisponiblesPorProductoAsync` filtra `Estado == EnStock`; ventas valida que la unidad este `EnStock` antes de confirmar. | Mantener como autoridad fisica por unidad. |
| `ProductoUnidadMovimiento` | Canonico para historial individual | Registra `EstadoAnterior`, `EstadoNuevo`, `Motivo`, `OrigenReferencia`, `UsuarioResponsable`, `FechaCambio`. | Mantener separado de `MovimientoStock`. |
| `ProductoUnidadService` | Canonico para ciclo de vida individual | Crea unidades, marca `Vendida`, `Faltante`, `Baja`, `Devuelta`, reintegra y revierte venta. No inyecta `IMovimientoStockService`. | No expandirlo con ajustes automaticos V1. |
| `VentaService` | Canonico para venta confirmada/cancelada | En confirmacion llama `DescontarStockYRegistrarMovimientos` y luego `MarcarUnidadesVendidasAsync`; en cancelacion llama `DevolverStock` y `RevertirUnidadesVentaAsync`. | Riesgo alto de doble ajuste si unidades generan kardex automaticamente. |
| `/Producto/Unidades/{productoId}` | Canonico para operacion de unidades | Muestra `StockActual`, resumen por estado y mensajes explicitos: crear/cargar/ajustar unidad no modifica stock agregado. | Mejor lugar V1 para panel de conciliacion. |
| `Producto.RequiereNumeroSerie` | Canonico actual para trazabilidad obligatoria en ventas | `VentaService.ValidarUnidadesTrazablesAsync` exige `ProductoUnidadId` solo cuando este flag es true y rechaza unidad en producto no trazable. | Usarlo para incluir en conciliacion obligatoria V1. |

### Significado actual

**`Producto.StockActual`**
Representa la existencia agregada operativa por SKU. Es la cifra que usa el sistema para validar stock de venta, alertas y kardex. Se actualiza por:

- ajustes manuales desde `MovimientoStockController.Create` via `MovimientoStockService.RegistrarAjusteAsync`;
- entradas batch via `RegistrarEntradasAsync`;
- salidas batch via `RegistrarSalidasAsync`;
- confirmacion de venta via `VentaService.DescontarStockYRegistrarMovimientos`;
- cancelacion de venta confirmada/facturada via `VentaService.DevolverStock`.

**`MovimientoStock`**
Representa el kardex agregado por SKU. Cada movimiento conserva tipo (`Entrada`, `Salida`, `Ajuste`), cantidad, stock anterior, stock nuevo, motivo, referencia y costo snapshot. Hoy alcanza para registrar una conciliacion futura usando `TipoMovimiento.Ajuste`, `Motivo` y `Referencia`, aunque no tiene un campo estructurado de origen/tipo de operacion.

**`ProductoUnidad.Estado`**
Representa el estado fisico individual de una unidad. `EnStock` es la unica disponibilidad que el selector de ventas debe ofrecer. `Vendida`, `Faltante`, `Baja`, `Devuelta`, `Reservada`, `Entregada`, `EnReparacion` y `Anulada` describen el ciclo de vida fisico, no el stock agregado.

**`ProductoUnidadMovimiento`**
Representa auditoria individual. Es el historial de cambios de estado de cada unidad fisica con motivo, usuario y referencia operativa. No expresa stock agregado ni reemplaza el kardex SKU.

### Inconsistencias posibles

- `StockActual` mayor que unidades `EnStock`: hay stock agregado que no tiene unidad fisica disponible asociada.
- `StockActual` menor que unidades `EnStock`: hay mas unidades fisicas disponibles que stock agregado vendible.
- Kardex agregado consistente consigo mismo pero divergente del recuento fisico individual.
- Historial de unidad consistente consigo mismo pero sin correlato agregado.
- Cancelacion de venta ya devuelve stock agregado y revierte unidad; si se agregara sincronizacion automatica en `ProductoUnidadService`, podria duplicar movimientos.
- Productos no trazables pueden tener unidades opcionales cargadas; si se mezclan en conciliacion obligatoria podrian generar falsas alarmas.

### Deteccion actual

No existe una consulta o reporte de conciliacion. La diferencia se puede inferir manualmente comparando:

- `Producto.StockActual`;
- resumen por estados en `/Producto/Unidades/{productoId}`;
- Kardex SKU en `/MovimientoStock/Kardex/{productoId}`;
- historial de cada unidad.

La UI ya avisa que crear, cargar o ajustar unidades no modifica el stock agregado, pero no calcula una diferencia explicita.

---

## B. Escenarios de divergencia

### Caso A - Se crean unidades manuales o masivas

Hoy no cambia `StockActual` ni crea `MovimientoStock`. Es correcto para V1 si la accion significa "documentar unidades fisicas existentes" y no "recibir mercaderia". Crear unidades como entrada automatica mezclaria alta de trazabilidad con ingreso contable/operativo de stock.

Decision V1: mantener sin movimiento agregado. Si se necesita ingresar stock, hacerlo por Kardex/ajuste/recepcion y luego cargar unidades o conciliar.

### Caso B - Unidad `EnStock` pasa a `Faltante`

Hoy no cambia `StockActual`. Es tecnicamente consistente con separacion estricta, pero deja una diferencia visible: la unidad deja de ser vendible por selector fisico, mientras el stock agregado sigue disponible.

Decision V1: no ajustar automaticamente. Mostrar diferencia y permitir ajuste agregado controlado desde conciliacion.

### Caso C - Unidad `Faltante` vuelve a `EnStock`

Hoy no cambia `StockActual`. Si antes el usuario habia ajustado el stock agregado a la baja, el reintegro generara diferencia inversa.

Decision V1: no ajustar automaticamente. La conciliacion debe mostrar el nuevo desvio y permitir ajuste positivo si corresponde.

### Caso D - Unidad `EnStock` pasa a `Baja`

Hoy no cambia `StockActual`. Es correcto si la baja fisica primero documenta el evento y el ajuste agregado queda como decision posterior.

Decision V1: no ajustar automaticamente. La baja debe alimentar el reporte de conciliacion.

### Caso E - Unidad `Vendida` se cancela y vuelve a `EnStock`

La venta confirmada ya hizo salida agregada por `RegistrarSalidasAsync`. La cancelacion ya hace entrada agregada por `RegistrarEntradasAsync` y ademas revierte la unidad con `RevertirVentaAsync`.

Decision V1: `ProductoUnidad` no debe generar otro `MovimientoStock` en cancelacion. Hay riesgo concreto de doble ajuste si se sincroniza automaticamente.

### Caso F - Producto trazable con `StockActual = 10` y 7 unidades `EnStock`

Debe mostrarse como diferencia positiva `StockActual - UnidadesEnStock = 3`. Operativamente significa: "el sistema agregado cree que hay 3 unidades vendibles mas que las unidades fisicas disponibles".

Decision V1: advertencia en panel/reporte, con link a Kardex SKU e historial de unidades. Accion futura: ajuste controlado para igualar stock agregado a unidades fisicas.

### Caso G - Producto no trazable con unidades opcionales

Como ventas rechaza `ProductoUnidadId` para productos con `RequiereNumeroSerie = false`, esas unidades no son obligatorias para disponibilidad de venta. Incluirlas en conciliacion obligatoria generaria ruido.

Decision V1: conciliacion obligatoria solo para `RequiereNumeroSerie = true`. Para no trazables, mostrar como "trazabilidad operativa opcional" si hay unidades cargadas, sin estado de desvio critico.

---

## C. Opciones comparadas

| Opcion | Descripcion | Ventajas | Riesgos | Compatibilidad con codigo actual |
|---|---|---|---|---|
| A - Separacion estricta | Unidades no tocan stock agregado; usuario ajusta stock por separado. | Simple, bajo riesgo, respeta arquitectura actual. | La divergencia queda poco visible si no hay reporte. | Muy alta. Es el comportamiento actual. |
| B - Sincronizacion automatica | Cargas/faltantes/bajas/reintegros generan `MovimientoStock`. | Reduce pasos manuales. | Doble ajuste, movimientos silenciosos, mezcla alta fisica con stock agregado, conflictos con cancelacion de venta. | Baja/media. Requiere acoplar `ProductoUnidadService` a stock. |
| C - Sincronizacion controlada | La accion de unidad pregunta si ajusta stock agregado. | Explicita la decision al usuario. | UI mas compleja, riesgo de confirmaciones repetitivas, necesita relacionar dos auditorias. | Media. Requiere cambios en UI, service y contrato. |
| D - Conciliacion asistida | Unidades no mutan stock; reporte/panel muestra diferencias; ajuste agregado se genera desde conciliacion. | Evita ajustes automaticos, evita doble movimiento, mantiene trazabilidades separadas, vuelve visible el problema. | Requiere nueva query/pantalla y disciplina operativa. | Alta. Encaja con mensajes y servicios actuales. |

---

## D. Decision recomendada

Recomendada para V1: **Opcion D - Conciliacion asistida**.

Motivos:

- Es compatible con la separacion ya validada tecnicamente.
- Evita movimientos automaticos silenciosos.
- Evita doble ajuste en ventas canceladas.
- Mantiene `MovimientoStock` como kardex agregado canonico y `ProductoUnidadMovimiento` como auditoria fisica individual.
- Hace visible la diferencia sin decidir por el usuario.
- Permite construir una accion futura de ajuste agregado con preview, motivo y referencia clara.

Regla V1: una transicion de unidad fisica nunca modifica `Producto.StockActual` ni crea `MovimientoStock` por si sola. La unica excepcion futura debe ser una accion explicita de conciliacion agregada.

---

## E. Diseno de conciliacion

### Metricas por producto trazable

Para productos `RequiereNumeroSerie = true`:

- `ProductoId`;
- codigo y nombre;
- `StockActual`;
- cantidad de unidades `EnStock`;
- cantidad de unidades `Vendida`;
- cantidad de unidades `Faltante`;
- cantidad de unidades `Baja`;
- cantidad de unidades `Devuelta`;
- cantidad de unidades `Reservada`, `Entregada`, `EnReparacion`, `Anulada` si existen;
- `Diferencia = StockActual - UnidadesEnStock`;
- ultimo `MovimientoStock` agregado;
- ultimo `ProductoUnidadMovimiento`;
- estado visual: `OK` si diferencia = 0; `Advertencia` si diferencia != 0.

Para productos no trazables:

- excluir del reporte obligatorio, o mostrar en seccion opcional si tienen unidades cargadas;
- no marcar diferencia como problema critico por defecto.

### Query/endpoint sugerido

V1 incremental:

- agregar query read-only en un service o metodo privado de controlador dedicado a conciliacion;
- agrupar `ProductoUnidades` por `ProductoId` y `Estado`;
- consultar productos activos con `RequiereNumeroSerie = true`;
- traer ultimo movimiento agregado por `ProductoId`;
- traer ultimo movimiento de unidad por producto via `ProductoUnidadMovimiento` + `ProductoUnidad`.

Endpoint futuro posible:

```text
GET /Producto/ConciliacionUnidades
GET /Producto/ConciliacionUnidades/{productoId}
```

Para primer micro-lote UI, alcanza con enriquecer:

```text
GET /Producto/Unidades/{productoId}
```

### Diferencias a calcular

```text
UnidadesEnStock = count(ProductoUnidad where ProductoId = id and Estado = EnStock and !IsDeleted)
Diferencia = Producto.StockActual - UnidadesEnStock
```

Interpretacion:

- `0`: stock agregado y unidades fisicas disponibles coinciden.
- `> 0`: hay mas stock agregado vendible que unidades fisicas disponibles.
- `< 0`: hay mas unidades fisicas disponibles que stock agregado vendible.

---

## F. Ajuste controlado futuro

Accion: **Generar ajuste de stock para igualar unidades fisicas**.

Ejemplo:

```text
StockActual = 5
UnidadesEnStock = 3
Diferencia = 2
Nuevo stock agregado = 3
```

Debe:

- mostrar preview antes de aplicar;
- crear `MovimientoStock` de tipo `Ajuste`;
- actualizar `Producto.StockActual` usando `MovimientoStockService.RegistrarAjusteAsync`;
- usar como `cantidad` el stock absoluto destino (`UnidadesEnStock`), porque el ajuste actual del service interpreta `TipoMovimiento.Ajuste` como valor absoluto;
- registrar motivo obligatorio;
- no tocar estados de `ProductoUnidad`;
- usar referencia clara, por ejemplo `ConciliacionUnidad:{productoId}`;
- registrar usuario desde usuario actual;
- mostrar link posterior al Kardex SKU.

Preguntas/decisiones:

- El ajuste puede ser positivo o negativo: si `UnidadesEnStock` es mayor que `StockActual`, el delta del movimiento sera positivo; si es menor, sera negativo.
- Debe requerir permiso especial: recomendado `productos/edit` como minimo; ideal futuro `stock/conciliar` o permiso equivalente.
- Debe mostrar preview antes de aplicar: si.
- Debe bloquear si hay ventas pendientes: recomendado no bloquear V1, pero advertir si existen ventas no confirmadas con unidades seleccionadas o productos trazables. Si se implementa `Reservada`, recalcular la regla.
- Debe registrar usuario: si, usando el mismo criterio que `MovimientoStockService`.
- No debe ejecutarse desde `ProductoUnidadService`, para evitar acoplar transiciones individuales con kardex agregado.

---

## G. UI propuesta

Primer punto recomendado: `/Producto/Unidades/{productoId}`.

Panel superior de conciliacion:

- stock agregado;
- unidades `EnStock`;
- diferencia;
- badge `OK` si diferencia = 0;
- badge `Advertencia` si diferencia != 0;
- link a `MovimientoStock/Kardex/{productoId}`;
- link/anchor al historial/listado de unidades;
- boton futuro `Conciliar stock`, visible solo si hay diferencia y permiso suficiente.

Texto operativo sugerido:

```text
Stock agregado: 5
Unidades fisicas disponibles: 3
Diferencia: +2
El stock agregado indica mas unidades vendibles que las unidades fisicas disponibles.
```

Para productos no trazables:

```text
Este producto no exige unidad fisica en venta. Las unidades cargadas son trazabilidad operativa opcional.
```

No implementar aun en esta fase.

---

## H. Tests futuros

### Reporte/query de conciliacion

- producto trazable con stock igual a unidades `EnStock` devuelve diferencia 0;
- stock mayor que unidades `EnStock` devuelve diferencia positiva;
- stock menor que unidades `EnStock` devuelve diferencia negativa;
- productos no trazables quedan excluidos del reporte obligatorio;
- productos no trazables con unidades cargadas quedan marcados como opcionales si se muestran;
- reporte incluye conteos por estado;
- reporte incluye ultimo movimiento agregado y ultimo movimiento individual cuando existen.

### Garantias de separacion

- crear unidad no modifica `Producto.StockActual`;
- carga masiva no modifica `Producto.StockActual`;
- marcar faltante no modifica `Producto.StockActual`;
- marcar baja no modifica `Producto.StockActual`;
- reintegrar no modifica `Producto.StockActual`;
- revertir venta no genera movimiento agregado propio desde `ProductoUnidadService`.

### Ajuste controlado

- generar ajuste de conciliacion crea `MovimientoStock` tipo `Ajuste`;
- generar ajuste actualiza `Producto.StockActual` al numero de unidades `EnStock`;
- ajuste positivo registra delta positivo;
- ajuste negativo registra delta negativo;
- referencia contiene `ConciliacionUnidad:{productoId}`;
- motivo obligatorio;
- usuario queda registrado;
- no modifica estados de `ProductoUnidad`;
- cancelacion de venta no duplica movimientos de stock.

---

## I. Plan de fases sugerido

### Fase 8.2.L - Query/read model de conciliacion

- Crear modelo read-only de conciliacion por producto.
- Agregar query para productos trazables.
- Tests de diferencias y conteos por estado.
- Sin accion de ajuste todavia.

### Fase 8.2.M - Panel en `/Producto/Unidades/{productoId}`

- Mostrar metricas y diferencia.
- Links a Kardex SKU e historial/listado.
- Estado visual OK/Advertencia.
- Tests de controller/view model si aplica.

### Fase 8.2.N - Reporte global de conciliacion

- Pantalla con todos los productos trazables y diferencias.
- Filtros: solo con diferencia, categoria, marca, busqueda.
- Export futuro si hace falta.

### Fase 8.2.O - Ajuste controlado de conciliacion

- Preview.
- Permiso.
- Motivo obligatorio.
- `MovimientoStockService.RegistrarAjusteAsync(productoId, TipoMovimiento.Ajuste, unidadesEnStock, referencia, motivo, usuario)`.
- Tests de no duplicacion y no modificacion de unidades.

---

## J. Riesgos y deuda

| Riesgo/deuda | Impacto | Mitigacion |
|---|---|---|
| No hay reporte actual de diferencias | Usuario puede no detectar divergencia a tiempo. | Implementar panel/query en proximo micro-lote. |
| `MovimientoStock.Referencia` es string libre | Dificulta trazabilidad estructurada de origen. | Usar convencion `ConciliacionUnidad:{productoId}` V1; evaluar campo origen estructurado V2. |
| `TipoMovimiento.Ajuste` usa cantidad como stock absoluto en `RegistrarAjusteAsync` | Puede confundirse con delta. | Documentar y testear que conciliacion pasa `UnidadesEnStock` como valor absoluto. |
| Productos no trazables con unidades opcionales | Pueden crear falsas diferencias. | Excluir de conciliacion obligatoria V1. |
| Ventas pendientes con unidades seleccionadas | Pueden cambiar disponibilidad entre preview y ajuste. | Recalcular al aplicar; advertir si hay ventas pendientes. |
| Sin permiso especifico de conciliacion | `productos/edit` puede ser demasiado amplio. | Crear permiso especifico en fase futura si el modelo de seguridad lo permite. |
| Doble ajuste en cancelaciones | Alto si se sincroniza automaticamente desde unidad. | Mantener acciones de unidad sin kardex automatico. |

---

## K. Checklist actualizado

### Completado en esta fase

- [x] Revisar flujo actual de `Producto.StockActual`.
- [x] Revisar `MovimientoStockService` y tipos de movimiento.
- [x] Revisar integracion venta/stock agregado.
- [x] Revisar integracion venta/unidad fisica.
- [x] Revisar UI actual de unidades y Kardex.
- [x] Revisar tests relevantes de stock, unidades y ventas.
- [x] Comparar opciones A/B/C/D.
- [x] Confirmar compatibilidad de Opcion D con el codigo actual.
- [x] Documentar decision recomendada V1.

### Pendiente para proximo micro-lote

- [ ] Implementar read model/query de conciliacion.
- [ ] Agregar tests de diferencia por producto trazable.
- [ ] Agregar panel de conciliacion en `/Producto/Unidades/{productoId}`.
- [ ] Agregar reporte global si se confirma necesidad operativa.
- [ ] Disenar permiso especifico para conciliacion si Seguridad lo permite.
- [ ] Implementar ajuste controlado desde conciliacion en fase posterior.

### Siguiente micro-lote recomendado

**Fase 8.2.L - Read model de conciliacion sin UI de ajuste.**

Objetivo: calcular y testear `StockActual`, unidades por estado y `Diferencia` para productos trazables, sin modificar stock ni estados. Es el paso de menor riesgo y habilita tanto el panel por producto como el reporte global.
