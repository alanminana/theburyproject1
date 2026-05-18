# Cierre de integracion - Conciliacion stock unidades

## A. Rama integrada

- `juan/conciliacion-stock-unidades-diseno`

## B. Que se integro

- Acciones asistidas separadas por signo para conciliar `Producto.StockActual` contra `ProductoUnidad.EnStock`.
- Ajuste hacia arriba mediante `AjustarStockAgregadoAUnidadesFisicas` cuando hay mas unidades fisicas disponibles que stock agregado.
- Ajuste hacia abajo mediante `AjustarStockAgregadoHaciaAbajo` cuando el stock agregado supera las unidades fisicas disponibles.
- Endpoint legacy `ConciliarStockUnidades` neutralizado: redirige con error y no ajusta stock.
- Registro de `MovimientoStock` con `TipoMovimiento.Ajuste`, referencia `ConciliacionUnidad:{productoId}`, motivo obligatorio y delta correcto.
- Kardex consistente al pasar el cambio de stock por `MovimientoStockService.RegistrarAjusteAsync`.
- UI de `Producto/Unidades` sin boton generico ambiguo, con acciones explicitas por signo.
- Tests de regresion para ajuste hacia arriba, ajuste hacia abajo, signo incorrecto, diferencia cero, endpoint legacy y contrato UI.

## C. Validaciones ejecutadas

- `dotnet build` - OK.
- `dotnet build --configuration Release` - OK.
- `dotnet test --filter "AjustarStockAgregado"` - OK, 5/5.
- `dotnet test --filter "Conciliacion|ProductoUnidad|MovimientoStock|Venta"` - OK, 979/979.
- `dotnet test --filter "VentaCreateUiContractTests"` - OK, 49/49.
- `dotnet test --filter "Venta|VentaApiController|VentaController|ConfiguracionPago|ProductoUnidad|Conciliacion"` - OK, 1011/1011.
- `dotnet test --filter "Caja"` - OK, 175/175.
- `dotnet test --filter "Cotizacion"` - OK, 162/162.
- `dotnet ef migrations list` - OK.
- `dotnet ef database update` - OK, base ya actualizada.
- `git diff --check` - OK, sin errores.
- `dotnet test` - primer intento con timeout transitorio en `CambiosPreciosAplicarRapidoTest.Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada`; el test paso aislado luego.
- `dotnet test` - OK final, 2966/2966, 0 omitidos.

## D. Que NO se toco

- `VentaService`.
- `ValidarUnidadesTrazablesAsync`.
- `ProductoUnidadService`.
- `MovimientoStockService`, salvo uso existente desde la rama integrada.
- Caja.
- Factura.
- Cotizacion.
- Migraciones.
- Reglas de venta.

## E. Deuda remanente

- Evaluar `ConciliacionStockUnidadesService` si las reglas de conciliacion crecen.
- Evaluar origen estructurado para ajustes de conciliacion si se necesitan reportes o auditoria mas granular.
