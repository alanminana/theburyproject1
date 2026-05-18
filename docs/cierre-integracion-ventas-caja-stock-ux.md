# Cierre integracion Ventas + Caja + Stock UX

## Ramas integradas

- `origin/carlos/cleanup-ventas-create-modal`
- `origin/carlos/venta-stock-breakdown-ui`
- `origin/kira/venta-advertencia-stock-sin-identificar`
- `origin/kira/caja-totales-ventas-efectivas`
- `origin/kira/caja-detalle-ventas-ux`

## Conflictos encontrados

- `wwwroot/js/venta-create.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## Resolucion

- Se conservaron `renderStockInfo` y los data attributes de Carlos:
  - `data-unidades-en-stock`
  - `data-stock-sin-identificar`
- Se conservaron las advertencias no bloqueantes de Kira:
  - `actualizarAdvertenciaStockSinIdentificar`
  - estado de `productoActualUnidadesEnStock`
  - estado de `productoActualStockSinIdentificar`
  - uso de `textContent`
- Se combino el guard defensivo para `hdnProductoRequiereNumeroSerie` con la lectura de stock sin identificar.
- Se conservaron tests de contrato de ambas ramas sin elegir una sobre otra.

## Validaciones ejecutadas

- `dotnet build`: OK
- `dotnet build --configuration Release`: OK
- `dotnet test --filter "VentaCreateUiContractTests"`: OK, 49 tests
- `dotnet test --filter "Venta|VentaApiController|VentaController|ConfiguracionPago|ProductoUnidad|Conciliacion"`: OK, 1011 tests
- `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"`: OK, 1 test
- `dotnet test --filter "Caja"`: OK, 175 tests
- `dotnet test --filter "Caja|Venta|MovimientoCaja"`: OK, 920 tests
- `dotnet test --filter "Cotizacion"`: OK, 162 tests
- `dotnet ef migrations list`: OK
- `dotnet ef database update`: OK, base ya actualizada
- `git diff --check`: OK
- `dotnet test`: OK, 2960 tests, 0 fallos, 0 omitidos

## Estado final

- `main` integra el fix E2E de Ventas/Create, el cleanup defensivo, el breakdown de stock y la advertencia de stock sin identificar.
- `main` integra Caja totales de ventas efectivas y UX de detalle de ventas.
- No se tocaron `VentaService`, `ValidarUnidadesTrazablesAsync`, `ProductoUnidadService`, `MovimientoStockService`, payload backend, reglas de stock, Cotizacion, Factura ni migraciones.
- Queda listo para push a `origin/main`.
