# COTIZ-QA-2 — Validación de guardado, persistencia y conversión de Cotización con descuentos

## A. Objetivo

Validar funcionalmente que el flujo de Cotización con descuentos generales y por producto:
- Se guarda correctamente en base de datos.
- Persiste los snapshots esperados en `CotizacionDetalle`.
- No rompe la conversión a Venta.
- Tiene comportamiento documentado respecto a qué descuentos se propagan en la conversión.

## B. Contexto COTIZ-1A/1B

**COTIZ-1A** integró en el formulario de cotización:
- `FechaVencimiento`, `DescuentoGeneralPorcentaje`, `DescuentoGeneralImporte`
- `NombreClienteLibre`, `TelefonoClienteLibre`

**COTIZ-1B** integró el descuento por producto:
- Inputs de descuento porcentaje e importe por producto en la tabla de simulación
- `state.productos` con `descuentoPorcentaje`/`descuentoImporte`
- `buildRequest()` envía los campos al backend
- Backend ya soportaba `CotizacionProductoRequest.DescuentoPorcentaje/DescuentoImporte`
- `CotizacionDetalle.DescuentoPorcentajeSnapshot/DescuentoImporteSnapshot` ya existían

## C. Tests agregados

### Integration — `CotizacionServicePersistenceTests.cs` (+4 tests)

| Test | Qué valida |
|---|---|
| `CrearCotizacion_ConDescuentoPorcentaje_PersistSnapshotEnDetalle` | `DescuentoPorcentajeSnapshot` se guarda correctamente desde el request |
| `CrearCotizacion_ConDescuentoImporte_PersistSnapshotEnDetalle` | `DescuentoImporteSnapshot` se guarda correctamente desde el request |
| `CrearCotizacion_SinDescuento_SnapshotsNulos` | Sin descuento, ambos snapshots son null |
| `CrearCotizacion_ConDescuentoGeneral_DescuentoTotalReflejaCalculatorResult` | `DescuentoGeneral` se aplica vía calculator y persiste en `Cotizacion.DescuentoTotal` |

### Integration — `CotizacionConversionServiceTests.cs` (+4 tests)

| Test | Qué valida |
|---|---|
| `Convertir_ConDescuentoImporteSnapshot_AplicaDescuentoEnVentaDetalle` | `DescuentoImporteSnapshot` se aplica como `VentaDetalle.Descuento` en la conversión |
| `Convertir_ConSoloDescuentoPorcentajeSnapshot_DescuentoDetalleEsCero` | Documenta que solo porcentaje no genera descuento en VentaDetalle |
| `Convertir_ConAmbosDescuentosSnapshot_UsaImporte` | Cuando ambos están presentes, importe tiene prioridad |
| `Convertir_DescuentoGeneralNoPropagaAVentaDescuento` | `Venta.Descuento = 0` siempre; descuento general no se propaga |

### E2E — `e2e/cotizacion-simulador.spec.js` (+2 tests)

| Test | Qué valida |
|---|---|
| `T7: Guardar cotización con descuento por producto — navega a detalles` | Guardar con 10% por producto completa el flujo y redirige a /Cotizacion/Detalles/{id} |
| `T8: Guardar cotización con descuento general — navega a detalles` | Guardar con 5% descuento general completa el flujo y redirige a /Cotizacion/Detalles/{id} |

## D. Escenarios cubiertos

1. Guardar cotización con descuento por producto porcentaje — E2E T7 + integración
2. Guardar cotización con descuento por producto importe — integración
3. Guardar cotización con descuento general porcentaje — E2E T8 + integración
4. Sin descuento: snapshots nulos — integración
5. Simulación sigue generando cards — T2/T5 pre-existentes
6. Selección de opción sigue funcionando — T3 pre-existente
7. Conversión con descuento importe: aplicado correctamente
8. Conversión con solo porcentaje: descuento = 0 (comportamiento documentado)
9. Conversión con ambos: importe tiene prioridad
10. Descuento general no propagado a `Venta.Descuento`

## E. Escenarios no cubiertos y motivo

| Escenario | Motivo |
|---|---|
| Edge case: porcentaje negativo o > 100 | UI previene con `min="0"` `max="100"`; el calculator los rechazaría. Requiere test del calculator, fuera de alcance de esta fase. |
| Edge case: importe > subtotal → subtotal negativo | El calculator decide si rechaza o clampa. Sin evidencia de bug en producción; se registra como deuda. |
| E2E conversión Cotización → Venta completa | Requiere crear la cotización, navegar a detalles y disparar la conversión. Flujo más largo; reservado para fase futura. |
| Validación de DescuentoGeneral campo-a-campo en entidad | El campo no existe: `DescuentoGeneral` no se persiste como campo separado (ver sección F). |

## F. Persistencia de descuentos

### Descuento por producto

- `CotizacionProductoRequest.DescuentoPorcentaje` → `CotizacionDetalle.DescuentoPorcentajeSnapshot`
- `CotizacionProductoRequest.DescuentoImporte` → `CotizacionDetalle.DescuentoImporteSnapshot`
- Se copian directamente en `CotizacionService.CrearAsync` (líneas 89-91), sin intervención del calculator.
- El calculator recibe estos valores y los usa para calcular `CotizacionProductoResultado.Subtotal` (subtotal ya descontado).

### Descuento general

- `CotizacionSimulacionRequest.DescuentoGeneralPorcentaje/Importe` → procesado por el calculator.
- El efecto se refleja en `CotizacionSimulacionResultado.DescuentoTotal` y `TotalBase`.
- La entidad `Cotizacion` persiste el resultado en `Cotizacion.DescuentoTotal` y `Cotizacion.TotalBase`.
- **No existe campo separado** para `DescuentoGeneralPorcentaje` o `DescuentoGeneralImporte` en la entidad. Solo el efecto calculado se persiste.

## G. Conversión a Venta: comportamiento real

### Qué SÍ se propaga

`CotizacionDetalle.DescuentoImporteSnapshot` → `VentaDetalle.Descuento`

```csharp
// CotizacionConversionService.cs línea 384
decimal descuento = detalle.DescuentoImporteSnapshot ?? 0m;
decimal subtotal = Redondear(precioUnitario * cantidad - descuento);
```

El subtotal de la venta se calcula descontando el importe snapshot.

### Qué NO se propaga

| Campo | Motivo |
|---|---|
| `DescuentoPorcentajeSnapshot` | No se usa en `ConstruirDetalles`. Es solo snapshot/auditoría. |
| `Cotizacion.DescuentoTotal` (descuento general) | `venta.Descuento = 0m` siempre (línea 247). El descuento general no se re-aplica. |

### Consecuencia de diseño

Si el usuario asignó descuento **solo como porcentaje** (sin importe) a un producto:
- La cotización muestra el subtotal correcto (el calculator aplicó el porcentaje).
- El detalle guarda `DescuentoPorcentajeSnapshot = 10%` pero `DescuentoImporteSnapshot = null`.
- La venta generada tendrá `Descuento = 0` para ese producto.
- El precio en la venta será `PrecioUnitarioSnapshot * cantidad` (precio full).

Esto puede sorprender al usuario que esperaba que el descuento porcentual se propagara a la venta.

**Recomendación futura**: documentar en la UI del preview de conversión si hay descuentos por porcentaje que no serán propagados.

## H. Edge cases validados

- Descuento porcentaje > 0: snapshot correcto
- Descuento importe > 0: snapshot correcto
- Sin descuento: snapshots null
- Ambos descuentos presentes: importe usado en conversión
- Descuento general: reflejado en DescuentoTotal (no campo separado)
- Conversión no rompe con o sin descuentos

## I. Archivos modificados

| Archivo | Cambio |
|---|---|
| `TheBuryProyect.Tests/Integration/CotizacionServicePersistenceTests.cs` | +4 tests, +1 helper, +1 stub |
| `TheBuryProyect.Tests/Integration/CotizacionConversionServiceTests.cs` | +4 tests, +1 helper `CotizacionEmitidaConDescuento` |
| `e2e/cotizacion-simulador.spec.js` | +T7, +T8 |
| `docs/cotiz-qa-2-guardar-conversion-descuentos.md` | Creado |

## J. Contratos preservados

- No se modificó ningún archivo productivo (controllers, services, models, views, migrations).
- Payloads existentes intactos.
- Endpoints sin cambio.
- Comportamiento del simulador, selección y guardado sin modificación.

## K. Validaciones

- Build Release: ejecutado
- `LayoutUiContractTests`: ejecutado
- Tests filtro `Cotizacion`: ejecutado
- `cotizacion-simulador.spec.js`: ejecutado
- `ui-4e-layout-visual.spec.js`: ejecutado

## L. Playwright

| Spec | Tests pre-existentes | Tests nuevos | Total esperado |
|---|---|---|---|
| `cotizacion-simulador.spec.js` | T1, T2, T3, T5, T6, T4 (43) | T7, T8 | 43 + 2 = 45 |
| `ui-4e-layout-visual.spec.js` | — | — | 169/169 |

## M. Procesos

Ver informe final del agente.

## N. Riesgos y deudas

- **Deuda**: Si el usuario aplica descuento solo como porcentaje, la conversión a venta no propaga ese descuento. No es un bug confirmado (puede ser diseño intencional) pero debe comunicarse al usuario en el preview de conversión. Registrado para fase VENTAS-UX-1 o posterior.
- **Deuda edge case**: importe > subtotal → subtotal negativo. Sin test por no haber evidencia de bug en producción.
- **No cubierto**: E2E de conversión completa (flujo largo: crear → detalles → convertir).

## O. Próximo paso recomendado

**VENTAS-UX-1** — Mejoras UX en el flujo de ventas, o bien una fase **COTIZ-QA-3** para:
- Cubrir E2E de conversión Cotización → Venta.
- Agregar advertencia en preview de conversión si hay descuentos por porcentaje que no se propagarán.
- Validar edge case importe > subtotal en el calculator.
