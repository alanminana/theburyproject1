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
- **El numero no cambia** al confirmar ni al facturar: `ConfirmarVentaAsync` y `FacturarVentaAsync` conservan el numero
  original. Una venta creada en Create y ya facturada sigue llamandose `COT-yyyyMM-NNNNNN`.
- **Ventas convertidas desde una `Cotizacion`** (`CotizacionConversionService`) y ventas importadas de Mercado Libre
  nacen ya como `Confirmada` y reciben prefijo **VTA**.

Por lo tanto el prefijo de una venta indica su origen, no su estado actual: `COT` = creada en Venta/Create, `VTA` =
convertida desde una cotizacion (o importada).

## Cobertura de tests

- `VentaServiceConfirmarEfectivoTests.CreateAsync_VentaNaceConPrefijoCot_YConfirmarConservaElNumero`: prefijo `COT-yyyyMM-`
  al crear y numero intacto tras Confirmar.
- `CotizacionConversionServiceTests.Convertir_LaVentaLlevaNumeracionDeVenta_NoDeCotizacion`: la venta convertida recibe `VTA-`.
