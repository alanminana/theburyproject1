# P-02 / P5: versionado fiscal por operación (diseño y parte preparatoria implementada)

Fecha: 2026-11-12. Base: `origin/main` 5cc059d5. Rama `task/p02-versionado-fiscal-20261112`. **P-02 sigue ABIERTA**: esto implementa solo la infraestructura de versionado (P5); no decide ni cambia ninguna regla fiscal.

## 0. Decisión del propietario (tomada)

Los cálculos fiscales de operaciones históricas deben **conservarse** cuando cambien las reglas de IVA, redondeo u otros criterios fiscales. Las operaciones nuevas usarán las reglas vigentes. (Punto P5 de `docs/iva-decisiones-contador.md` §5-b.)

Lo que NO cambia en este trabajo: alícuotas, redondeos (`Math.Floor` a pesos), criterios de IVA, importes persistidos, reglas tributarias. No existe ninguna «versión 2» en producción.

## 1. Problema

Las lecturas fiscales **recalculan** con las reglas del código de hoy a partir de datos persistidos:

| Lectura | Dónde | Qué recalcula |
|---|---|---|
| Reporte de IVA de ventas | `Services/ReporteService.cs` (~281-345) | servicios (armado/envío) al `PorcentajeDefault`, recargo/descuento del medio de pago prorrateado por alícuota, truncado absorbido |
| Comprobante de factura | `Helpers/FacturaComprobanteBuilder.cs`, `FacturaAlicuotaResumenBuilder` (`ViewModels/FacturaViewModel.cs`) | ídem sobre el impreso |
| Ingreso neto / devoluciones netas | `Services/VentasNetas.cs` | neto del recargo de débito (`Repartir`) |
| Reintegro por línea de una devolución | `Services/DevolucionService.cs` (`RecargoPorLinea`) | parte del recargo por línea (`Prorratear`) |
| Cálculo al crear/editar | `Services/VentaService.cs` (`CalcularTotales`, `DiscriminarRecargoEnNetoEIva`, `NormalizarNetoEIvaDesdeTotal`), `CotizacionConversionService` | truncado a pesos, servicios, discriminación |

Los importes principales de la venta (`Subtotal`, `IVA`, `Total`, `VentaDetalle.SubtotalFinalNeto/IVA`) sí están persistidos al calcularse. Pero todo lo que se **releé** (alícuota de servicios `ProductoIvaResolver.PorcentajeDefault`, reparto del recargo, truncado) se reevalúa con el código vigente: si mañana cambia una regla, el Reporte de IVA y el comprobante de una venta de 2026 cambiarían retroactivamente. `docs/iva-preservacion-historica.md` §2-3 describe el riesgo.

## 2. Diseño elegido (el más chico y seguro)

### 2.1 Qué se versiona

Un **criterio fiscal** = el conjunto de reglas de cálculo que hoy están en tres lugares estáticos:

- `Helpers/RecargoMedioPagoIva` (reparto/discriminación del ajuste del medio de pago, truncado absorbido),
- `Helpers/ServiciosVentaIva` (alícuota y separación neto/IVA de armados y envío),
- el truncado a pesos del total (`Math.Floor`, antes `VentaService.TruncarAPesos`).

### 2.2 Piezas

- `Helpers/CriterioFiscal.cs`:
  - `ICriterioFiscal`: `Version`, `AlicuotaServicios`, `NombreAlicuotaServicios`, `SepararServicios`, `TruncarAPesos`, `CalcularRecargo`, `Prorratear`, `Repartir`, `Discriminar`, `ResumenPorAlicuota`, `AbsorberTruncado`.
  - `CriterioFiscalV1`: **solo delega** en las clases estáticas actuales y en `Math.Floor`. Cero reglas propias: la versión 1 es, por construcción, el comportamiento actual byte a byte (los helpers estáticos no se tocaron).
  - `CriterioFiscalRegistry`: `VersionInicial = 1`, `VersionVigente` (hoy 1), `Vigente`, `Resolver(int)`. Una versión no registrada **lanza** `InvalidOperationException` (nunca se recalcula con otra regla «por las dudas»). Para tests existe `UsarParaPruebas(...)` (internal, `AsyncLocal`, sin ningún camino de producción) que permite registrar una versión de prueba.
- `Venta.CriterioFiscalVersion` (int, NOT NULL, default 1). `VentaViewModel.CriterioFiscalVersion` es `[BindNever]` y el mapeo `VentaViewModel → Venta` lo ignora: **solo el servidor** fija la versión (no se acepta del formulario ni se pisa en una edición).
- Migración `20261112160856_P02CriterioFiscalVersion`: un único `AddColumn` `Ventas.CriterioFiscalVersion int NOT NULL DEFAULT 1`.

Por qué un registro estático y no DI: los consumidores son helpers/view models estáticos (`FacturaComprobanteBuilder`, `FacturaAlicuotaResumenBuilder`, `VentasNetas`) sin acceso a un contenedor; un `Resolver(version)` estático puro evita reescribir firmas de toda la cadena. La inyección no aporta nada con una sola versión.

### 2.3 Cuándo se estampa la versión

Al **crear** la operación: `VentaService.CreateAsync`, `CotizacionConversionService.ConvertirAVentaAsync` (venta nacida de cotización) y `MercadoLibreOrderService` asignan `CriterioFiscalRegistry.VersionVigente`. Se estampa al crear (no al confirmar) porque los importes se calculan y persisten en la creación y en cada edición del borrador; si la versión se cambiara recién al confirmar, importes calculados con la versión A quedarían rotulados B. **La edición de una venta existente conserva su versión** (se calcula con el criterio de la propia venta). Cambiar la versión de un borrador ya calculado al confirmarlo implicaría recalcular todo: queda como decisión pendiente (ver §6), no se hace.

### 2.4 Cableado de lectura (sin cambiar resultados)

Todo cálculo de una operación existente obtiene `CriterioFiscalRegistry.Resolver(venta.CriterioFiscalVersion)`:

- `ReporteService.GenerarReporteIvaAsync`: por venta (servicios y `ResumenPorAlicuota`).
- `FacturaComprobanteBuilder.Build` + `FacturaAlicuotaResumenBuilder.Build(…, criterio)`: líneas de servicios, discriminación, absorción del truncado. Los llamadores sin operación (vista previa de cotización) usan el vigente.
- `VentasNetas.CalcularIngresoNeto` y `ObtenerDevolucionesAsync` (la versión de la venta original).
- `DevolucionService.RecargoPorLinea` (criterio de la venta devuelta).
- `VentaService` (`CalcularTotales`, `DiscriminarRecargoEnNetoEIva`, `NormalizarNetoEIvaDesdeTotal`, `TruncarAPesos(…, venta)`): con el criterio de la venta. Las vistas previas sin venta usan el vigente.

**Devoluciones y notas de crédito**: no llevan columna propia. Una devolución/NC deriva de una venta y siempre se calcula con la versión de la venta original (es lo que pide «conservar el cálculo histórico»: devolver una venta v1 se reintegra con las reglas v1 aunque la vigente cambie). Una columna propia solo haría falta si una decisión futura (p. ej. C13/C14) exigiera que la NC use reglas distintas de las de la venta; ver §6. Cuotas de crédito, punitorios y recibos no discriminan IVA hoy (C9–C11), por lo que no requieren versión.

## 3. Cambios por capa

| Capa | Cambio |
|---|---|
| Modelo | `Venta.CriterioFiscalVersion`; `VentaViewModel.CriterioFiscalVersion` (BindNever) |
| EF | `HasDefaultValue(1)` en `AppDbContext`; snapshot + migración `P02CriterioFiscalVersion` |
| Servicios | `VentaService`, `CotizacionConversionService`, `MercadoLibreOrderService` (estampa), `ReporteService`, `VentasNetas`, `DevolucionService` (lectura/cálculo por versión) |
| Helpers/VM | `CriterioFiscal.cs` (nuevo), `FacturaComprobanteBuilder`, `FacturaAlicuotaResumenBuilder` (parámetro opcional `criterio`), `VentaController.CrearFacturaViewModel` |
| Reportes | mismos importes en v1; el reporte es agnóstico de la versión: cada venta usa la suya |
| Tests | `Integration/FiscalVersionadoP02Tests.cs` (7 tests) |

## 4. Migración y backfill

- La migración es **aditiva**: una sola `ADD COLUMN ... NOT NULL DEFAULT 1`. SQL Server rellena todas las filas existentes con 1 como parte del `ALTER TABLE` (operación de metadatos en 2012+: no reescribe ni toca `Subtotal`, `IVA`, `Total` ni ninguna otra columna). **El backfill es el propio default**: no hay `UPDATE`.
- Operaciones existentes = v1 = reglas actuales, así que ningún importe, reporte ni comprobante cambia.
- Rollback: `Down` elimina la columna (verificado en base aislada).
- **Aplicación a `TheBuryProjectDb`: NO realizada** (fuera de alcance; requiere respaldo y autorización expresa, como toda migración).
- Evidencia en base aislada (SQL Server 2022 en contenedor descartable, ya eliminado): se aplicaron todas las migraciones hasta `D04AperturaCajaUnicaAbierta`, se insertaron 2 ventas sintéticas (totales 2.000,00 y 1.797,00), se aplicó P02 y se verificó `CriterioFiscalVersion = 1` en ambas con `Subtotal/IVA/Total` idénticos; constraint `DF__Ventas__Criterio__… ((1))`; luego `Down` revirtió la columna sin alterar importes.

## 5. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Alguien registra una v2 y la «vigente» cambia sin validar con el contador | `UsarParaPruebas` es `internal` y solo para tests; en producción el registro tiene solo v1. Registrar una versión nueva exige cambio de código revisado (PR) y decisión del contador |
| `ServiciosVentaIva.Porcentaje` lee `Iva:PorcentajeDefault` de configuración | v1 delega en él tal cual hoy. **Cambiar esa configuración altera retroactivamente la lectura de ventas v1** (ya ocurría antes). Para blindarlo, una versión futura debe congelar la alícuota dentro de la propia versión y la configuración pasar a gobernar solo la versión vigente. Queda anotado como pendiente |
| La alícuota por línea (`PorcentajeIVA`, snapshot) y los importes persistidos ya son históricos | Sin cambios |
| Versión desconocida en una fila | Falla explícita (reporte/comprobante lanzan) en lugar de importes silenciosamente distintos |
| Reglas de lectura fuera de este alcance (p. ej. `CajaConciliacionBuilder`, exportaciones) | No usan estas reglas fiscales; revisar al introducir una v2 |
| `ProductoIvaResolver` en `VentaService` (líneas) | Es el snapshot por línea al crear; no se versiona aquí |

## 6. Decisiones que SIGUEN pendientes (no se decidieron)

Del contador (`iva-decisiones-contador.md` §5-a): **C1–C15** — redondeo (C1/C2), absorción del redondeo (C3), descuento efectivo sobre base imponible (C4) y sobre envío/armado (C5), servicios al 21 % (C6), recargo a la alícuota del ítem (C7), recargos de tarjetas/planes (C8), cuotas cobradas (C9), IVA en punitorios (C10), interés en factura/contrato (C11), IVA de compra recuperable (C12), neteo de devoluciones por fecha (C13), plazo de 7 días (C14) y período fiscal UTC−3 (C15).

Del propietario (§5-b): **P1–P4 y P6** (P5 está decidido y es lo que se prepara aquí): facturación fuera del ERP (P1), facturación electrónica propia (P2), neteo de devoluciones en el Reporte de IVA (P3), leyenda en el impreso (P4) y autorización/fecha de corte por fila (P6).

Nuevas, derivadas de este diseño (a validar, no decididas):

1. **Fecha de corte y versión vigente**: cuando el contador defina un criterio nuevo, ¿desde qué fecha rige (venta, confirmación, facturación)? Hoy se estampa al crear.
2. **Borradores/cotizaciones**: ¿un borrador creado con la versión N y confirmado después de un cambio pasa a la vigente (recalculando) o conserva N?
3. **NC/devoluciones**: ¿usan siempre la versión de la venta original (implementado así) o la vigente cuando difieren (p. ej. C13)?
4. **Congelar la alícuota de servicios** dentro de la versión (hoy sale de `Iva:PorcentajeDefault`).
5. **Reglas todavía fuera del versionado** (p. ej. neteo de devoluciones, `RecargoDiscriminado` del comprobante en cuotas): se añaden al contrato `ICriterioFiscal` junto con cada decisión concreta.

## 7. Cómo agregar una versión 2 (cuando exista decisión del contador y autorización)

1. Implementar `CriterioFiscalV2 : ICriterioFiscal` (nuevo archivo; v1 no se edita jamás).
2. Registrarla en `CriterioFiscalRegistry` y subir `VersionVigente` con fecha de corte autorizada.
3. Tests: histórico v1 intacto, nuevo v2 correcto (modelo: `FiscalVersionadoP02Tests`).
4. Sin migración de datos: las operaciones v1 se conservan.
