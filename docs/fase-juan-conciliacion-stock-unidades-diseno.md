# Fase Juan - Conciliacion asistida StockActual vs ProductoUnidad

Fecha: 2026-05-18  
Agente: Juan  
Alcance: diagnostico y diseno de correccion asistida. No se modifico backend de ventas ni reglas de confirmacion.

## A. Diagnostico actual

Hoy existe una implementacion parcial de conciliacion entre `Producto.StockActual` y las unidades fisicas `ProductoUnidad` en estado `EnStock`.

El calculo canonico esta en `ProductoUnidadService.ObtenerConciliacionPorProductoAsync(productoId)`.

Ese calculo:

- carga el producto activo
- cuenta unidades no eliminadas agrupadas por `EstadoUnidad`
- define `UnidadesEnStock` como `COUNT(ProductoUnidad WHERE Estado = EnStock AND !IsDeleted)`
- calcula `DiferenciaStockVsUnidadesEnStock = Producto.StockActual - UnidadesEnStock`
- informa conteos por estado
- informa fecha del ultimo `MovimientoStock`
- informa fecha del ultimo `ProductoUnidadMovimiento`

La pantalla actual no corrige automaticamente al cargar. Pero si existe una accion POST explicita que ajusta el stock agregado al numero de unidades fisicas disponibles.

## B. Flujos existentes

### Servicio de conciliacion

Archivo: `Services/ProductoUnidadService.cs`

Metodo canonico:

```csharp
ObtenerConciliacionPorProductoAsync(int productoId)
```

Read model:

```csharp
Services/Models/ProductoUnidadConciliacionReadModel.cs
```

View model:

```csharp
ViewModels/ProductoUnidadesViewModels.cs
```

### Endpoint / controller

Archivo: `Controllers/ProductoController.cs`

Pantalla:

```csharp
Unidades(int productoId, ...)
```

Accion correctiva existente:

```csharp
[HttpPost("Producto/ConciliarStockUnidades")]
ConciliarStockUnidades(ProductoUnidadConciliarStockViewModel vm)
```

Comportamiento actual de la accion:

1. exige motivo
2. recalcula conciliacion en servidor
3. si no hay diferencia, no ajusta
4. calcula `nuevoStockAbsoluto = conciliacion.UnidadesEnStock`
5. llama a `MovimientoStockService.RegistrarAjusteAsync(productoId, Ajuste, nuevoStockAbsoluto, "ConciliacionUnidad:{productoId}", motivo, usuario)`
6. no modifica `ProductoUnidad`
7. no crea `ProductoUnidadMovimiento`

### Vista

Archivo: `Views/Producto/Unidades.cshtml`

La vista muestra:

- stock agregado actual
- unidades disponibles `EnStock`
- diferencia
- unidades registradas
- conteos por estado
- ultimo movimiento de stock
- ultimo movimiento de unidad
- links a Kardex y listado de unidades
- preview de ajuste asistido

Condicion importante:

```csharp
var puedeAjustar = conciliacion.RequiereNumeroSerie && conciliacion.HayDiferencia;
```

La accion de conciliacion solo se ofrece visualmente para productos que requieren numero de serie y tienen diferencia.

### Kardex / MovimientoStock

Archivo: `Services/MovimientoStockService.cs`

`RegistrarAjusteAsync` trata `TipoMovimiento.Ajuste` como stock absoluto:

- `cantidad` representa el stock final deseado
- `Producto.StockActual = cantidad`
- `MovimientoStock.Cantidad = cantidad - stockAnterior`
- registra `StockAnterior`, `StockNuevo`, `Referencia`, `Motivo`, `CreatedBy`

Esto preserva Kardex para cambios de `StockActual`.

### Historial de unidades

Archivo: `Services/ProductoUnidadService.cs`

Las acciones que cambian unidades fisicas (`MarcarFaltanteAsync`, `MarcarBajaAsync`, `ReintegrarAStockAsync`, `FinalizarReparacionAsync`, etc.) crean `ProductoUnidadMovimiento`.

Crear una unidad tambien agrega un movimiento inicial con origen `AltaUnidad`.

Estas acciones no modifican stock agregado.

## C. Diferencias posibles

### Diferencia positiva

Formula:

```text
StockActual > UnidadesEnStock
DiferenciaStockVsUnidadesEnStock > 0
```

Interpretacion:

- hay mas stock agregado que unidades fisicas disponibles
- para producto no trazable puede representar stock sin identificar valido
- para producto trazable puede indicar unidades faltantes de carga o stock agregado inflado

Riesgo:

- si se ajusta automaticamente hacia abajo, se puede perder stock agregado valido de productos no trazables
- si se crean unidades fisicas faltantes automaticamente, se pueden inventar unidades sin respaldo fisico

### Diferencia negativa

Formula:

```text
StockActual < UnidadesEnStock
DiferenciaStockVsUnidadesEnStock < 0
```

Interpretacion:

- hay unidades fisicas disponibles `EnStock`, pero el stock agregado indica menos stock
- en ventas no trazables, `StockActual` puede bloquear una venta aunque existan unidades fisicas
- en productos trazables, la unidad fisica existe y puede estar disponible, pero el agregado queda inconsistente

Debe tratarse como error operativo a revisar, no como estado normal.

Riesgo:

- unidad atrapada: existe fisicamente y figura disponible, pero el agregado no acompana
- doble correccion si un operador ajusta stock y ademas marca unidades como faltantes/baja sin recalcular
- Kardex incompleto si alguien modifica `StockActual` por fuera de `MovimientoStockService`

## D. Riesgos

### Riesgo de doble correccion

Existe si se habilitan acciones multiples sin recalcular estado justo antes de aplicar.

Ejemplo:

1. operador ajusta `StockActual` hacia arriba
2. otro operador marca unidad como `Faltante`
3. ambos partieron de la misma diferencia

Mitigacion:

- recalcular conciliacion dentro de cada POST
- exigir motivo
- validar estado actual
- mostrar preview con delta
- no mezclar cambio de stock y cambio de unidades en una misma accion

### Riesgo de romper Kardex

El riesgo aparece si se modifica `Producto.StockActual` directamente.

La accion actual usa `MovimientoStockService.RegistrarAjusteAsync`, por lo tanto deja `MovimientoStock` y actualiza Kardex.

Regla propuesta:

```text
Nunca modificar StockActual sin MovimientoStock.
```

### Riesgo de romper trazabilidad de unidades

El riesgo aparece si se modifica `ProductoUnidad.Estado` directamente.

Las transiciones actuales usan `ProductoUnidadService` y crean `ProductoUnidadMovimiento`.

Regla propuesta:

```text
Nunca modificar ProductoUnidad sin ProductoUnidadMovimiento.
```

### Riesgo funcional en no trazables

Para productos que no requieren numero de serie, una diferencia positiva puede ser valida como stock sin identificar.

La pantalla actual no ofrece conciliacion para no trazables, lo cual es prudente.

## E. Opciones evaluadas

### Opcion A - Solo diagnostico

Mantener la conciliacion como reporte.

Ventaja:

- minimo riesgo
- no altera datos

Desventaja:

- no resuelve unidades atrapadas ni inconsistencias operativas

Estado actual:

- existe reporte
- pero tambien existe una accion correctiva limitada

### Opcion B - Ajustar StockActual a unidades fisicas

Para diferencia negativa:

```text
StockActual = UnidadesEnStock
MovimientoStock Ajuste positivo
```

Ventaja:

- libera unidad fisica atrapada
- preserva Kardex
- no altera historial de unidades

Riesgo:

- aumenta stock agregado por recuento/manual, no por operacion comercial

Mitigacion:

- motivo obligatorio
- referencia `ConciliacionUnidad:{productoId}`
- preview de delta
- permiso de edicion

La accion actual ya permite esto para productos trazables.

### Opcion C - Ajustar unidades fisicas a StockActual

Para diferencia negativa:

```text
marcar unidades sobrantes como Faltante/Baja/Anulada
ProductoUnidadMovimiento obligatorio
```

Ventaja:

- mantiene `StockActual` como fuente agregada

Riesgo:

- puede marcar como faltante/baja una unidad real
- requiere seleccion explicita de unidades afectadas

No debe hacerse automaticamente.

### Opcion D - Crear unidades fisicas faltantes

Para diferencia positiva:

```text
crear N ProductoUnidad
ProductoUnidadMovimiento inicial
```

Ventaja:

- ordena trazabilidad de productos serializados

Riesgo:

- crea unidades ficticias si el stock agregado estaba mal
- en productos con numero de serie obligatorio puede requerir serie real por unidad

Debe ser asistido, con carga individual o masiva y motivo/observacion.

### Opcion E - Flujo asistido por operador

Pantalla separa acciones por tipo de diferencia.

Para diferencia positiva:

- dejar como stock sin identificar, especialmente si no requiere serie
- crear unidades fisicas faltantes, si el recuento confirma stock fisico
- ajustar stock agregado hacia abajo, si el recuento confirma que el stock agregado estaba inflado

Para diferencia negativa:

- ajustar stock agregado hacia arriba, si la unidad fisica existe y esta disponible
- marcar unidad como faltante/baja, si la unidad fisica no existe o no debe venderse
- revisar manualmente, si no hay evidencia suficiente

Ventaja:

- evita automatismos destructivos
- conserva auditoria
- permite tratar distinto trazables y no trazables

Desventaja:

- requiere mas UI y reglas de validacion

## F. Recomendacion

Recomiendo evolucionar a Opcion E.

No conviene agregar correccion automatica. Conviene mantener flujo asistido y explicito, con acciones separadas y motivo obligatorio.

La accion actual `ConciliarStockUnidades` es util como base, pero esta demasiado generica: iguala siempre `StockActual` a `UnidadesEnStock`, tanto para diferencia positiva como negativa, y la UI no obliga al operador a elegir el tipo de correccion.

Recomendacion concreta:

1. Mantener `ProductoUnidadService.ObtenerConciliacionPorProductoAsync` como consulta canonica.
2. Crear un servicio de aplicacion especifico para conciliacion asistida solo si Fase 2 agrega varias acciones y validaciones cruzadas. Nombre sugerido: `ConciliacionStockUnidadesService`.
3. Separar acciones POST:
   - `AjustarStockAgregadoAUnidadesFisicas`
   - `AjustarStockAgregadoHaciaAbajo`
   - `CrearUnidadesFisicasFaltantes`
   - `MarcarUnidadesComoFaltantesOBaja`
4. Recalcular conciliacion en cada accion antes de aplicar.
5. No modificar `VentaService`.
6. No modificar `ValidarUnidadesTrazablesAsync`.

## G. Reglas de negocio propuestas

1. Toda correccion debe pedir motivo obligatorio.
2. Toda correccion de `StockActual` debe crear `MovimientoStock`.
3. Toda correccion de `ProductoUnidad` debe crear `ProductoUnidadMovimiento`.
4. No mezclar ajuste de stock agregado y cambio de estado de unidades en una sola accion.
5. Cada accion debe recalcular conciliacion en servidor antes de aplicar.
6. Cada accion debe validar que la diferencia actual coincide con el tipo de accion.
7. Para diferencia cero, no aplicar correccion.
8. Para producto que requiere numero de serie:
   - diferencia positiva no debe tratarse como stock sin identificar normal
   - recomendar cargar unidades fisicas faltantes o ajustar stock agregado hacia abajo
   - si se crean unidades, preferir exigir numero de serie cuando el negocio lo requiera
9. Para producto que no requiere numero de serie:
   - diferencia positiva puede quedar como stock sin identificar
   - no bloquear venta no trazable por falta de unidades fisicas
   - diferencia negativa debe advertirse como error operativo
10. El Kardex debe mostrar cualquier ajuste de stock agregado con referencia y motivo.
11. El historial de unidad debe mostrar cualquier cambio de estado con motivo y origen.

## H. Acciones UI propuestas

### Panel actual a conservar

Mantener:

- stock agregado
- unidades `EnStock`
- diferencia
- conteos por estado
- ultimo movimiento stock
- ultimo movimiento unidad
- link a Kardex
- link a listado/historial de unidades

### Mejoras propuestas

Cuando `DiferenciaStockVsUnidadesEnStock > 0`:

- mostrar interpretacion: "stock agregado mayor que unidades fisicas"
- si no requiere serie: mostrar "puede representar stock sin identificar"
- acciones:
  - dejar sin identificar / no accionar
  - crear unidades fisicas faltantes
  - ajustar stock agregado hacia abajo

Cuando `DiferenciaStockVsUnidadesEnStock < 0`:

- mostrar interpretacion: "hay unidades fisicas disponibles sin stock agregado suficiente"
- marcar como riesgo operativo
- acciones:
  - ajustar stock agregado hacia arriba
  - seleccionar unidades y marcar como faltante/baja
  - revisar manualmente

Cada accion debe tener:

- preview de antes/despues
- delta
- motivo obligatorio
- advertencia sobre que historial se va a generar

## I. Tests necesarios

Ya existen tests relevantes:

- `ProductoUnidadServiceTests.ObtenerConciliacion_StockIgualAUnidadesEnStock_DevuelveConciliado`
- `ProductoUnidadServiceTests.ObtenerConciliacion_StockMayorAUnidadesEnStock_DevuelveDiferenciaPositiva`
- `ProductoUnidadServiceTests.ObtenerConciliacion_StockMenorAUnidadesEnStock_DevuelveDiferenciaNegativa`
- `ProductoUnidadServiceTests.ObtenerConciliacion_CuentaBucketsPorEstado`
- `ProductoUnidadServiceTests.ObtenerConciliacion_ExcluyeUnidadesSoftDeleted`
- `ProductoUnidadServiceTests.ObtenerConciliacion_InformaUltimosMovimientos`
- `ConciliacionStockUnidadesTests` cubre ajuste de `StockActual` a `UnidadesEnStock`, movimiento de stock y no modificacion de unidades
- `VentaServiceProductoUnidadTrazabilidadTests` documenta que ventas trazables usan unidades fisicas `EnStock` como autoridad para trazabilidad

Tests recomendados para Fase 2:

- `Conciliacion_DiferenciaPositiva_ProductoNoTrazable_PermiteDejarStockSinIdentificar`
- `Conciliacion_DiferenciaPositiva_ProductoTrazable_OfreceCrearUnidadesOAjustarStock`
- `Conciliacion_DiferenciaNegativa_OfreceAjustarStockAgregadoHaciaArriba`
- `Conciliacion_DiferenciaNegativa_SeleccionUnidadYMarcaFaltante_CreaProductoUnidadMovimiento`
- `Conciliacion_AjustarStockAgregado_RecalculaAntesDeAplicar`
- `Conciliacion_AjustarStockAgregado_DiferenciaCero_NoCreaMovimientoStock`
- `Conciliacion_CrearUnidadesFaltantes_NoModificaStockActual`
- `Conciliacion_CrearUnidadesFaltantes_CreaHistorialInicial`
- `Conciliacion_NoPermiteAccionDeSignoIncorrecto`
- `Conciliacion_MotivoObligatorio_ParaTodasLasAcciones`
- `Conciliacion_NoTocaVentaService_NiReglasDeConfirmacion`

No se agregaron tests nuevos en esta fase porque el entregable pedido es de diagnostico/diseno y ya existen pruebas que documentan el comportamiento actual.

## J. Prompt de implementacion Fase 2

```text
JUAN - FASE 2 CONCILIACION STOCK/UNIDADES - Implementar flujo asistido seguro

Actua como Programador Senior ASP.NET MVC .NET 8.

Objetivo:
Evolucionar la pantalla Producto/Unidades para separar acciones de conciliacion StockActual vs ProductoUnidad sin automatismos destructivos.

Reglas criticas:
- No modificar VentaService.
- No modificar ValidarUnidadesTrazablesAsync.
- No modificar Caja, Factura, Cotizacion, Devolucion, migraciones ni Program.cs.
- No modificar StockActual sin MovimientoStock.
- No modificar ProductoUnidad sin ProductoUnidadMovimiento.
- No hacer correcciones silenciosas.
- Recalcular conciliacion en servidor antes de cada accion.
- Motivo obligatorio en toda accion.

Base actual:
- ProductoUnidadService.ObtenerConciliacionPorProductoAsync es la consulta canonica.
- ProductoController.Unidades muestra la conciliacion.
- ProductoController.ConciliarStockUnidades hoy ajusta StockActual a UnidadesEnStock via MovimientoStockService. Debe revisarse para no quedar como accion generica ambigua.

Implementar en micro-lote:
1. Refactorizar la UI de conciliacion para mostrar acciones separadas por signo de diferencia.
2. Mantener o renombrar la accion existente para que represente explicitamente "ajustar stock agregado a unidades fisicas".
3. Para diferencia negativa, permitir ajuste positivo de StockActual a UnidadesEnStock, con MovimientoStock tipo Ajuste, referencia ConciliacionUnidad:{productoId}, motivo obligatorio.
4. Para diferencia positiva, no ejecutar accion automatica. Mostrar opciones:
   - dejar como stock sin identificar si no requiere numero de serie
   - ajustar stock agregado hacia abajo
   - crear unidades faltantes mediante flujo existente de carga/alta
5. No crear en esta fase una accion que marque unidades como Baja/Faltante en lote salvo que se haga seleccion explicita de unidades y tests.

Tests minimos:
- accion diferencia negativa ajusta StockActual hacia arriba y crea MovimientoStock
- accion diferencia positiva ajusta StockActual hacia abajo solo si operador confirma esa accion
- diferencia cero no crea MovimientoStock
- motivo obligatorio
- no se crean ProductoUnidadMovimiento cuando solo se ajusta StockActual
- crear unidad faltante no modifica StockActual y crea historial inicial

Validaciones:
- dotnet build --configuration Release
- dotnet test --filter "Conciliacion|ProductoUnidad|MovimientoStock|Venta"
- git diff --check
```

## Checklist actualizado

Completado:

- baseline de rama verificado
- build Release ejecutado
- tests filtrados ejecutados
- busqueda obligatoria ejecutada
- Graphify consultado como apoyo de navegacion
- implementacion actual identificada
- riesgos documentados
- opciones evaluadas
- recomendacion de Fase 2 definida

Pendiente:

- implementar UI con acciones separadas por signo de diferencia
- revisar si conviene extraer `ConciliacionStockUnidadesService`
- agregar tests de acciones Fase 2
- decidir si se mantiene, renombra o reemplaza `ConciliarStockUnidades`

Siguiente micro-lote recomendado:

Separar la accion actual de conciliacion en acciones explicitas por intencion operativa, empezando por diferencia negativa: "Ajustar stock agregado a unidades fisicas" con motivo obligatorio, recalculo servidor y test de MovimientoStock.
