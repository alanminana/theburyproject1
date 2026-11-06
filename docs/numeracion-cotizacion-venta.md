# Numeracion COT / VTA de ventas y cotizaciones

Documenta el comportamiento actual (decision del propietario, opcion A: documentar, no cambiar). Los prefijos son
configurables en Configuracion -> Prefijos (`TipoPrefijoNumeracion`); los valores por defecto son `COT` y `VTA`.

## Dos series distintas con el mismo prefijo `COT`

| Entidad | Formato | Ejemplo | Generador |
|---|---|---|---|
| `Cotizacion` (modulo Cotizaciones) | `COT-yyyyMMdd-NNNN` | `COT-20261028-0001` | `CotizacionService.GenerarNumeroAsync` |
| `Venta` (tabla Ventas) | `<prefijo>-yyyyMM-NNNNNN` | `COT-202610-000123` / `VTA-202610-000124` | `VentaNumberGenerator.GenerarNumeroAsync` |

La entidad `Cotizacion` tiene su propia serie, independiente de la de ventas: no comparten contador ni tabla. Un
`COT-20261028-0001` es una cotizacion; un `COT-202610-000123` es una venta.

## Que prefijo recibe una venta

`VentaNumberGenerator` elige el prefijo segun el estado con el que nace la venta: `Cotizacion` -> prefijo `COT`
(`TipoPrefijoNumeracion.Cotizacion`), cualquier otro estado -> prefijo `VTA` (`TipoPrefijoNumeracion.Venta`).

- **Ventas creadas desde Venta/Create** (`VentaService.CreateAsync`): el servidor las crea siempre en estado
  `Cotizacion`, por lo que nacen con prefijo **COT** (numero de origen).
- **H-01 (regla del propietario, 2026-11):** la venta nacida en Create **se renumera a `VTA-yyyyMM-NNNNNN`** en el primer
  punto de emision de un documento que copia el numero: al **generar el contrato/pagare** de credito personal
  (`ContratoVentaCreditoService.GenerarAsync`) o, si no hay contrato, al **confirmar** (`ValidarUnidadesYDescontarStockAsync`,
  antes de descontar stock y registrar caja). El `COT-...` original queda en `Ventas.NumeroOrigen` (columna nueva, nullable) y
  se muestra como «Origen COT-...» en el detalle. La renumeracion es idempotente (no se repite) y **no toca ventas ya confirmadas
  ni documentos ya emitidos**: las ventas anteriores a H-01 conservan su numero. No renumera si los prefijos configurados de
  cotizacion y venta son iguales. La facturacion tiene su propia numeracion (`FA-B-...`) y no cambia.
- **Ventas convertidas desde una `Cotizacion`** (`CotizacionConversionService`) y ventas importadas de Mercado Libre
  nacen ya como `Confirmada` y reciben prefijo **VTA**.

Por lo tanto: una venta **`COT-` es una operacion aun no confirmada ni documentada** (o historica, anterior a H-01); una
`VTA-` es una venta en curso/confirmada. La cotizacion del modulo Cotizaciones conserva su propio numero (`COT-yyyyMMdd-NNNN`).

## Cobertura de tests

- `VentaServiceConfirmarEfectivoTests.CreateAsync_VentaNaceConPrefijoCot_YConfirmarLaRenumeraAVta_ConservandoElOrigen`: `COT-`
  al crear, `VTA-` al confirmar y `NumeroOrigen` con el `COT-`.
- `VentaNumberGeneratorTests.AsegurarNumeroDeVenta_*`: renumera, idempotente, no toca ventas `VTA-`, prefijos iguales, prefijo configurado.
- `ContratoVentaCreditoServiceTests.GenerarAsync_VentaNacidaComoCotizacion_SeRenumeraAVtaAntesDeEmitirElContrato_H01`: el snapshot del contrato lleva `VTA-`.
- `CotizacionConversionServiceTests.Convertir_LaVentaLlevaNumeracionDeVenta_NoDeCotizacion`: la venta convertida recibe `VTA-`.
