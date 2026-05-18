# Fase Juan - Conciliacion asistida StockActual vs ProductoUnidad

## Diagnostico

La fase cierra la primera implementacion vertical de conciliacion asistida entre:

- `Producto.StockActual`: stock agregado canonico del SKU.
- `ProductoUnidad`: unidades fisicas individuales en estado `EnStock`.
- `MovimientoStock`: kardex agregado canonico.

El problema corregido es la diferencia operativa entre el stock agregado y el recuento de unidades fisicas disponibles. Antes la accion podia leerse como una conciliacion generica; ahora queda separada por signo para evitar ajustes ambiguos sobre inventario real.

## Acciones implementadas

- `AjustarStockAgregadoAUnidadesFisicas`: aplica solo con diferencia negativa (`StockActual < UnidadesEnStock`). Ajusta `StockActual` hacia arriba hasta igualarlo a las unidades fisicas disponibles.
- `AjustarStockAgregadoHaciaAbajo`: aplica solo con diferencia positiva (`StockActual > UnidadesEnStock`). Ajusta `StockActual` hacia abajo hasta igualarlo a las unidades fisicas disponibles.
- `ConciliarStockUnidades`: endpoint legacy neutralizado. Redirige a `Unidades` con error y no modifica stock.

Ambas acciones explicitas recalculan la conciliacion en servidor, requieren motivo, usan `MovimientoStockService.RegistrarAjusteAsync` y registran referencia `ConciliacionUnidad:{productoId}`.

## Reglas por signo

| Diferencia | Interpretacion | Accion permitida | Resultado |
| --- | --- | --- | --- |
| Positiva (`StockActual > UnidadesEnStock`) | Hay stock agregado mayor al recuento fisico individual. Puede ser stock sin identificar o stock inflado. | `AjustarStockAgregadoHaciaAbajo` | Baja `StockActual` hasta `UnidadesEnStock` y genera `MovimientoStock` con delta negativo. |
| Negativa (`StockActual < UnidadesEnStock`) | Hay unidades fisicas disponibles sin stock agregado suficiente. | `AjustarStockAgregadoAUnidadesFisicas` | Sube `StockActual` hasta `UnidadesEnStock` y genera `MovimientoStock` con delta positivo. |
| Cero | El stock agregado coincide con unidades fisicas disponibles. | Ninguna | No crea `MovimientoStock`; muestra error operativo. |

Si se invoca una accion con signo incorrecto, no se modifica `StockActual`, no se crea `MovimientoStock` y se informa error.

## Seguridad funcional

- Ningun ajuste se ejecuta automaticamente desde la vista ni desde la carga de unidades.
- Ambas acciones requieren POST, antiforgery, permiso `productos/edit` y motivo obligatorio.
- El backend ignora cualquier numero enviado por el cliente y recalcula la diferencia antes de ajustar.
- No se toca `ProductoUnidad.Estado`.
- No se crea `ProductoUnidadMovimiento`.
- Todo cambio efectivo de `StockActual` pasa por `MovimientoStockService.RegistrarAjusteAsync`.
- Kardex queda consistente porque `MovimientoStock` registra `StockAnterior`, `StockNuevo`, `Cantidad`, `Motivo`, `Referencia` y usuario.
- El endpoint legacy ya no ejecuta conciliacion generica.

## UI

`Views/Producto/Unidades.cshtml` muestra acciones separadas por signo:

- diferencia negativa: accion para ajustar stock agregado a unidades fisicas;
- diferencia positiva: opciones para dejar stock sin identificar, crear unidades faltantes o ajustar stock agregado hacia abajo;
- diferencia cero: estado conciliado sin accion de ajuste.

No queda un boton generico de "Conciliar" sin contexto.

## Que NO se toco

- No se modifico `ProductoUnidadService`.
- No se modificaron reglas de transicion de unidades.
- No se modifico `MovimientoStockService`.
- No se agregaron migraciones.
- No se cambio venta, cancelacion, orden de compra, caja ni facturacion.
- No se automatizo sincronizacion entre unidades fisicas y stock agregado.

## Validaciones

Validaciones requeridas para el cierre:

```powershell
dotnet build --configuration Release
dotnet test --filter "AjustarStockAgregado"
dotnet test --filter "Conciliacion|ProductoUnidad|MovimientoStock|Venta"
git diff --check
```

## Riesgos

- La accion de bajar stock puede reducir inventario agregado valido si el operador interpreta mal stock sin identificar. La UI deja una opcion explicita de "Dejar stock sin identificar" para no forzar el ajuste.
- La conciliacion iguala `StockActual` a `UnidadesEnStock`; no resuelve causas historicas de la diferencia.
- La referencia `ConciliacionUnidad:{productoId}` es una convencion string, no un origen estructurado.

## Deuda remanente

- Evaluar un `ConciliacionStockUnidadesService` si aparecen mas reglas o mas acciones.
- Evaluar una entidad/origen estructurado para movimientos de conciliacion.
- Agregar auditoria visual mas detallada en Kardex para distinguir ajustes de conciliacion.
- Revisar reportes operativos para exponer stock sin identificar sin inducir a ajustes innecesarios.
