# Fase Juan - Diagnostico ConciliacionStockUnidadesService y origen estructurado

## A. Diagnostico actual

La conciliacion asistida entre `Producto.StockActual` y `ProductoUnidad.EnStock` quedo integrada como un flujo chico y localizado en `ProductoController`.

Componentes revisados:

- `Controllers/ProductoController.cs`
- `Views/Producto/Unidades.cshtml`
- `Services/ProductoUnidadService.cs`
- `Services/Interfaces/IProductoUnidadService.cs`
- `Services/MovimientoStockService.cs`
- `Services/Interfaces/IMovimientoStockService.cs`
- `Models/Entities/MovimientoStock.cs`
- `Models/Entities/ProductoUnidad.cs`
- `Models/Entities/ProductoUnidadMovimiento.cs`
- `Models/Enums/TipoMovimiento.cs`
- `ViewModels/ProductoUnidadesViewModels.cs`
- `TheBuryProyect.Tests/Integration/ProductoControllerPrecioTests.cs`
- `docs/fase-juan-conciliacion-stock-unidades-asistida.md`
- `docs/cierre-integracion-conciliacion-stock-unidades.md`

Clasificacion:

- Canonico: `Producto.StockActual` como stock agregado del SKU.
- Canonico: `ProductoUnidadService.ObtenerConciliacionPorProductoAsync`, porque concentra la lectura del estado de conciliacion actual.
- Canonico: `MovimientoStockService.RegistrarAjusteAsync`, porque es la autoridad actual para modificar `StockActual` y registrar kardex.
- Canonico: `MovimientoStock` con `Tipo`, `Referencia`, `Motivo`, `StockAnterior`, `StockNuevo`, `CreatedBy` y `CreatedAt`.
- Canonico: acciones explicitas `AjustarStockAgregadoAUnidadesFisicas` y `AjustarStockAgregadoHaciaAbajo`, cubiertas por tests de regresion.
- Legacy neutralizado: `ConciliarStockUnidades`, que ya no modifica stock y solo redirige con error operativo.
- Incierto/no necesario ahora: `ConciliacionStockUnidadesService`, porque no existe todavia y no hay multiples callers ni reglas suficientes para justificarlo.
- Duplicado/paralelo evitado: no se encontro otro camino activo que ajuste esta conciliacion por fuera de `MovimientoStockService`.

La cantidad de logica de conciliacion que quedo en `ProductoController` es acotada: dos endpoints publicos de una linea delegan en un helper privado; el helper valida motivo, recalcula la conciliacion en servidor, verifica diferencia cero, valida el signo permitido, arma la referencia textual y llama a `MovimientoStockService.RegistrarAjusteAsync`.

## B. Responsabilidades actuales

Responsabilidades que hoy tiene `ProductoController`:

- Recibir el POST desde la UI.
- Validar motivo obligatorio.
- Validar longitud maxima de motivo.
- Recalcular la conciliacion en servidor mediante `IProductoUnidadService`.
- Calcular/leer la diferencia ya provista por el read model.
- Decidir si el signo corresponde a la accion invocada.
- Evitar movimiento cuando la diferencia es cero.
- Construir la referencia `ConciliacionUnidad:{productoId}`.
- Llamar a `IMovimientoStockService.RegistrarAjusteAsync`.
- Armar `TempData` de exito/error.
- Redirigir a `Unidades`.
- Mantener neutralizado el endpoint legacy `ConciliarStockUnidades`.

Responsabilidades que ya pertenecen a servicios canonicos:

- `ProductoUnidadService`: conteos por estado, `UnidadesEnStock`, diferencia contra `StockActual`, ultimas fechas de movimiento.
- `MovimientoStockService`: validacion de stock absoluto para ajuste, transaccion, update de `Producto.StockActual`, delta del movimiento, `StockAnterior`, `StockNuevo`, costo, usuario, persistencia y kardex.

Si se extrajera un servicio dedicado, deberia absorber:

- Calcular/obtener la conciliacion actual.
- Validar diferencia cero.
- Validar direccion/signo del ajuste.
- Calcular `nuevoStockAbsoluto`.
- Construir la referencia de origen.
- Ejecutar el ajuste mediante `MovimientoStockService`.
- Devolver un resultado semantico para que el controller solo traduzca a `TempData` y redirect.

No deberia absorber:

- Mensajes UI especificos.
- `TempData`.
- Redirecciones.
- Antiforgery/permisos.
- Cambios de estado de `ProductoUnidad`.
- Creacion de `ProductoUnidadMovimiento`.

## C. Riesgos de dejarlo en controller

Riesgos reales:

- La regla de signo queda expresada como `Func<decimal, bool>` en el controller, no como lenguaje de dominio.
- La referencia textual `ConciliacionUnidad:{productoId}` es una convencion string y podria duplicarse mal si aparece otro caller.
- Si se agregan nuevas acciones, por ejemplo previsualizacion, aprobacion, auditoria separada o conciliacion batch, el controller podria empezar a concentrar reglas.

Riesgos mitigados hoy:

- No hay duplicacion grave entre las acciones hacia arriba y hacia abajo; ambas comparten `AplicarAjusteStockAgregadoDesdeConciliacionAsync`.
- El backend recalcula la conciliacion; no confia en valores del cliente.
- El ajuste efectivo no vive en el controller; vive en `MovimientoStockService`.
- Los tests cubren signo correcto, signo incorrecto, diferencia cero, referencia, delta, `StockAnterior`, `StockNuevo`, motivo, no modificacion de unidades y endpoint legacy neutralizado.

## D. Riesgos de extraer servicio ahora

Extraer `ConciliacionStockUnidadesService` ahora agregaria:

- Nueva interfaz.
- Nueva implementacion.
- Registro DI.
- Nuevo modelo de resultado.
- Cambios en tests o nuevos tests del service.
- Mas superficie para mantener sin un segundo caller real.

El riesgo principal es crear una abstraccion prematura sobre un flujo que hoy tiene una sola entrada UI, reglas simples y cobertura existente en el controller. Tambien podria dar una falsa sensacion de dominio consolidado cuando la regla actual sigue siendo deliberadamente asistida y operativa, no una conciliacion automatica integral.

La extraccion seria mas valiosa si aparece alguna de estas condiciones:

- Mas de un caller necesita ejecutar la misma conciliacion.
- Se agrega un flujo batch.
- Se agregan estados de aprobacion o auditoria especifica.
- Se agregan reglas distintas segun tipo de producto, sucursal, deposito o trazabilidad.
- Se necesita devolver un resultado semantico reutilizable fuera de MVC.
- Se quiere testear reglas de conciliacion sin levantar controller.

## E. Evaluacion de origen estructurado

`MovimientoStock` ya conserva datos suficientes para auditoria operativa del ajuste actual:

- `Tipo = TipoMovimiento.Ajuste`
- `Referencia = ConciliacionUnidad:{productoId}`
- `Motivo`
- `StockAnterior`
- `StockNuevo`
- `Cantidad` como delta
- `CreatedBy`
- `CreatedAt`
- `ProductoId`

La referencia textual alcanza hoy para identificar que el ajuste provino de conciliacion de unidades del producto. Ademas, el producto ya esta vinculado por `ProductoId`, por lo que el `{productoId}` dentro de la referencia es redundante pero util como convencion visible en kardex/reportes.

Patrones encontrados:

- `MovimientoStock` usa `Referencia` textual y, para compras, `OrdenCompraId` como vinculo estructurado especifico.
- `ProductoUnidadMovimiento` tiene `OrigenReferencia` textual para eventos como devolucion, cancelacion o ajustes de unidad.
- No se encontro un patron general actual de `OrigenTipo`/`OrigenId` para `MovimientoStock`.
- Si se agregan columnas formales como `OrigenTipo`, `OrigenId` u `OrigenReferencia`, hace falta migracion y actualizar mapeos/reportes/tests.

Agregar un enum/origen formal requeriria migracion si se persiste en `MovimientoStock`. Esa migracion no vale la pena ahora porque no hay evidencia de consultas, reportes, integraciones o auditorias que necesiten filtrar por origen formal mas alla de `Tipo`, `Referencia`, `Motivo`, usuario y fechas.

## F. Opciones evaluadas

### Opcion A - No tocar

Mantener la logica actual en `ProductoController`.

Ventajas:

- Cero cambio funcional.
- Cero migracion.
- Cero nueva abstraccion.
- Mantiene verde la cobertura existente.
- La logica sigue chica y compartida por un helper privado.
- Todavia no hay multiples callers.

Costo/riesgo:

- Sigue existiendo una convencion string local.
- El lenguaje de dominio de direccion de ajuste no queda encapsulado en un service.

Evaluacion: recomendada ahora.

### Opcion B - Extraer servicio sin migracion

Crear `IConciliacionStockUnidadesService` y `ConciliacionStockUnidadesService`.

Ventajas:

- Controller mas delgado.
- Reglas de signo y resultado semantico en una clase de dominio.
- Mejor seam para tests si crece el flujo.

Costo/riesgo:

- Mas archivos e interfaz sin segundo caller.
- Mayor mantenimiento.
- Puede ser sobrearquitectura para el estado actual.

Evaluacion: no recomendada ahora; recomendable solo si crecen reglas o callers.

### Opcion C - Origen estructurado sin migracion

Agregar helper/constante para `ConciliacionUnidad:{productoId}`.

Ventajas:

- Evita string magico si la referencia se reutiliza.
- No requiere migracion.
- Mantiene el contrato actual.

Costo/riesgo:

- Hoy hay una sola ocurrencia productiva directa; el helper agregaria indireccion con valor marginal.

Evaluacion: no necesaria en este micro-lote. Buena primera mejora si aparece otra ocurrencia productiva o si se toca de nuevo este flujo.

### Opcion D - Origen estructurado con migracion

Agregar campos formales a `MovimientoStock`, por ejemplo `OrigenTipo`, `OrigenId`, `OrigenReferencia`.

Ventajas:

- Auditoria consultable por origen formal.
- Reportes mas robustos.
- Menos dependencia de convenciones string.

Costo/riesgo:

- Requiere migracion.
- Requiere actualizar entidad, DbContext, vistas/reportes, posiblemente viewmodels y tests.
- Afecta una tabla central de kardex sin necesidad funcional actual.

Evaluacion: no recomendada ahora.

## G. Recomendacion final

Recomendacion: Opcion A, no tocar codigo ahora.

La implementacion actual debe seguir en `ProductoController` por el momento porque:

- La logica es pequena y esta localizada.
- Las dos acciones explicitas no duplican el cuerpo critico; comparten helper.
- El ajuste real ya esta en el service canonico `MovimientoStockService`.
- La lectura de conciliacion ya esta en `ProductoUnidadService`.
- No hay multiples callers.
- No hay reglas de negocio adicionales que justifiquen un service nuevo.
- La cobertura actual valida los contratos sensibles.
- Crear `ConciliacionStockUnidadesService` hoy seria mas costo estructural que valor.
- Agregar origen formal persistido requeriria migracion y no hay evidencia suficiente.

## H. Si conviene implementar o no

No conviene implementar cambios de codigo ahora.

Tampoco conviene crear migraciones, tocar `MovimientoStockService`, tocar `ProductoUnidadService`, tocar `VentaService`, tocar `ValidarUnidadesTrazablesAsync` ni modificar UI.

Implementacion futura minima sugerida, solo si aparece un segundo uso de la referencia:

```csharp
public static class MovimientoStockReferencias
{
    public static string ConciliacionUnidad(int productoId)
        => $"ConciliacionUnidad:{productoId}";
}
```

Ese helper deberia entrar sin migracion y con tests ajustados solo si reduce duplicacion real.

## I. Tests afectados

Tests revisados/afectados conceptualmente:

- `AjustarStockAgregadoAUnidadesFisicas_DiferenciaNegativa_AjustaHaciaArribaYCreaMovimientoStock`
- `AjustarStockAgregadoAUnidadesFisicas_DiferenciaPositiva_NoAplicaAccionDeSignoIncorrecto`
- `AjustarStockAgregadoHaciaAbajo_DiferenciaPositiva_AjustaSoloConAccionExplicita`
- `AjustarStockAgregadoHaciaAbajo_DiferenciaNegativa_NoAplicaAccionDeSignoIncorrecto`
- `ConciliarStockUnidades_EndpointLegacy_NoAjustaStock`
- `AjustarStockAgregadoHaciaAbajo_DiferenciaCero_NoCreaMovimientoStock`

Como este cierre es documental, no se agregaron ni modificaron tests.

Validaciones base ejecutadas antes del diagnostico:

```powershell
dotnet build --configuration Release
dotnet test --filter "AjustarStockAgregado"
dotnet test --filter "Conciliacion|ProductoUnidad|MovimientoStock|Venta"
```

Resultado:

- Build Release OK.
- `AjustarStockAgregado`: 5/5 OK.
- `Conciliacion|ProductoUnidad|MovimientoStock|Venta`: 979/979 OK.

Nota operativa: un primer intento de ejecutar las tres validaciones .NET en paralelo vencio por timeout y dejo procesos `dotnet` vivos de esa tanda. Se cerraron solo esos procesos y se repitieron las validaciones secuencialmente con resultado OK.

## J. Deuda remanente real

- Mantener `ConciliacionUnidad:{productoId}` como convencion documentada mientras exista un solo caller.
- Crear helper de referencia si aparece una segunda ocurrencia productiva.
- Extraer `ConciliacionStockUnidadesService` si la conciliacion deja de ser una accion UI puntual y pasa a tener reglas reutilizables.
- Evaluar origen persistido formal solo si hay necesidad real de reporting/auditoria por origen transversal.
- Si se crea origen formal en el futuro, tratarlo como cambio de modelo de kardex con migracion, backfill opcional y pruebas de reportes.

## Checklist actualizado

- [x] Se evaluo si hace falta servicio dedicado.
- [x] Se evaluo si hace falta origen estructurado.
- [x] Se reviso la logica actual del controller.
- [x] Se reviso `ProductoUnidadService` como origen de conciliacion.
- [x] Se reviso `MovimientoStockService` como autoridad de ajuste.
- [x] Se reviso `MovimientoStock` y campos de auditoria.
- [x] Se revisaron tests criticos.
- [x] No se cambio comportamiento funcional.
- [x] No se tocaron `VentaService` ni `ValidarUnidadesTrazablesAsync`.
- [x] No se tocaron `ProductoUnidadService` ni `MovimientoStockService`.
- [x] No se modifico UI.
- [x] No se crearon migraciones.
- [x] Documento creado.
- [x] Validaciones finales post-documentacion.
- [ ] Commit y push.

Siguiente micro-lote recomendado: no abrir codigo por esta deuda ahora. Volver a evaluarla cuando aparezca un segundo caller, un reporte de auditoria que no pueda resolverse con `Referencia`, o nuevas reglas de conciliacion.
