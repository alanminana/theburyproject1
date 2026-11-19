# P-02: respuestas del contador C1–C15 (2026-11-19), clasificación y estado

Fecha: 2026-11-19. Base: `origin/main` 1e210e34. Fuente de las respuestas: transmitidas por el propietario (registradas en Notion). **Son respuestas del contador, no una validación legal definitiva ni una autorización de implementación.** Complementa `docs/iva-decisiones-contador.md` §5, `docs/p02-escenarios-y-alternativas.md` y `docs/p02-versionado-fiscal-diseno.md`.

**Estado de P-02: EN REVISIÓN (no finalizada).** Qué impide cerrarla está en §6.

## 1. Decisiones ya tomadas (no se vuelven a consultar)

Facturación fiscal en sistema externo (el ERP no emite CAE; sus comprobantes son internos y llevan la leyenda P4). Versionado fiscal y snapshots (P5) integrados. Instalación nueva con base SQL Server limpia, sin históricos; la VM Ubuntu es el servidor provisional. No se toca ninguna base histórica.

## 2. Clasificación

Niveles: **A** = respuesta clara y técnicamente implementable; **B** = decisión comercial clara pero con duda legal/fiscal; **C** = incompleta o contradictoria; **D** = sin respuesta.

| # | Respuesta (resumen) | Nivel | Estado en el código | Observación |
|---|---|---|---|---|
| C1 | ≤ $0,50 → hacia abajo (1.000,50 → 1.000) | **A** | **`CriterioFiscalV2`** (registrada, no vigente) | Coincide con V1 en este caso. |
| C2 | > $0,50 → hacia arriba (1.000,51 y 1.000,99 → 1.001) | **A** | **`CriterioFiscalV2`** (registrada, no vigente) | V1 trunca siempre. Cambia el importe cobrado: exige fecha de corte (P6). |
| C3 | La diferencia de redondeo «no se registra, calcula ni modifica nada» | **C** | V2 mantiene `Neto + IVA = Total` absorbiendo el residuo en el IVA (igual que V1); no crea ningún concepto | Una factura no puede dejar una diferencia sin explicar. Se pregunta al contador si absorber en IVA es aceptable (§5, pregunta 1). |
| C4 | Descuento en efectivo reduce el importe final y el IVA se ajusta | **B** | Ya así en V1 (reduce base e IVA); sin cambios | Riesgo legal: ver §3.1. |
| C5 | Descuento en efectivo solo sobre productos, no sobre envío/armado | **A (comercial)** | **No implementado** | Toca 7 puntos de cálculo (plan de tarjeta, plan global, ajuste 1 pago, conversión de cotización, cotizador, vista previa). Cambia el importe cobrado y debe cotizador/venta/caja/factura de forma idéntica: requiere su propia tanda con autorización (P6). |
| C6 | Envío y armado llevan IVA | **B** | Ya así: 21 % por snapshot (`AlicuotaServiciosSnapshot`) | La alícuota «aplicable según la operación» no se respondió; se mantiene 21 %. |
| C7 | Recargo de tarjeta con IVA del producto y parte del precio gravado | **B/C** | Ya así en V1; **no se toca** | El propio contador dudó. Ver §3.1: el recargo en un solo pago puede estar prohibido. |
| C8 | Recargos de tarjeta/transferencia se documentan por factura | **C** | Sin cambios | No dijo qué recargos son legalmente permitidos por tarjeta, cuotas o transferencia. |
| C9 | Cuota $618 = capital 500 + interés 100 + recargo 18; documentar el total con IVA del producto | **C** | Sin cambios (Caja 618, sin factura) | Facturar el total duplicaría el capital ya incluido en la factura de la venta. Ver §3.3. |
| C10 | Punitorios sin IVA «porque el capital ya lo tiene», pero «al facturarlos llevarían 21 %» | **C** (contradictoria) | Sin cambios (sin IVA ni comprobante) | Requiere aclaración. |
| C11 | Interés de financiación propia con IVA, documentado al vender en el documento firmado | **B** | Sin cambios | Contradice la respuesta preliminar anterior («no alcanzado por IVA», `iva-matriz-diferencias.md` 2.1). Alícuota, devengo y comprobante externo sin definir. |
| C12 | Sin respuesta (no sabe si el IVA de compras es crédito fiscal) | **D** | Sin cambios: ganancia sobre costo con IVA | No se asume el régimen fiscal del negocio. |
| C13 | Devolución en 72 h: se cancela la deuda y se emite NC, normalmente el mismo día | **B** | Sin cambios | No define período de cómputo ni la relación NC interna / NC fiscal externa. Ver §3.2 y §4 (P3). |
| C14 | 72 h presencial, 30 días Mercado Libre; dinero solo dentro de 24 h con producto perfecto; después NC | **B** | Sin cambios (`DiasLimiteDevolucion` = 30, reembolso o NC a elección) | Describe la práctica comercial, no los derechos legales. Ver §3.2. |
| C15 | Los informes usan la fecha argentina (31/10 22:30 ART es octubre) | **A** | **No implementado**: hallazgo en §3.4 | Hace falta normalizar primero cómo se guarda `FechaVenta`. |

## 3. Contraste con fuentes oficiales

Las verificaciones se hicieron por búsqueda de fuentes oficiales; los puntos marcados «no verificado» deben confirmarse en el texto consolidado de InfoLEG / Boletín Oficial antes de actuar. **No es una opinión jurídica ni reemplaza al contador.**

### 3.1 Recargos y diferencias de precio por medio de pago (C4, C7, C8) — hallazgo de riesgo legal
- Ley 25.065, art. 37 inc. c): el proveedor no puede efectuar diferencias de precio entre operaciones al contado y con tarjeta ([texto](https://www.argentina.gob.ar/normativa/nacional/ley-25065-55556/texto)).
- Resolución E 51/2017: la prohibición alcanza a las operaciones **en un solo pago** y dice que no se pueden hacer diferencias de precio entre operaciones al contado/efectivo **o** en un solo pago con tarjeta de débito, compra, crédito «u otros medios electrónicos de pago»; en cuotas se presume que la financiación implica otro precio y debe exhibirse el costo financiero total ([texto](https://www.argentina.gob.ar/normativa/nacional/resoluci%C3%B3n-51-2017-271185/texto)).
- **Consecuencia para el ERP** (lectura literal, a confirmar con el contador/abogado): el recargo de **transferencia**, de **débito** y de **tarjeta en 1 pago** (hoy configurable, p. ej. +3 % transferencia) y quizá también el **descuento exclusivo por efectivo** (C4) podrían constituir una diferencia de precio entre medios en un solo pago. Los recargos en cuotas se presumen financiación y requieren exhibir el CFT.
- No verificado: que la Res. 51/2017 siga vigente tras normas posteriores (p. ej. DNU 70/2023). Una fuente provincial de 2023 la cita como vigente.
- Por eso C7/C8 **no se activan ni se versionan**: no hay respuesta sobre qué recargos son lícitos.

### 3.2 Devoluciones, arrepentimiento y garantías (C13, C14)
- Compras a distancia: art. 34 de la Ley 24.240 (revocación) con plazo de 10 días corridos, y botón de arrepentimiento vigente por la Disposición 954/2025 del Ministerio de Economía, que derogó la Res. 424/2020 ([Res. 424/2020, histórica](https://www.argentina.gob.ar/normativa/nacional/resoluci%C3%B3n-424-2020-342869/texto); [guía oficial](https://www.argentina.gob.ar/justicia/derechofacil/leysimple/boton-arrepentimiento); [aviso Boletín Oficial 4/9/2025](https://www.boletinoficial.gob.ar/detalleAviso/primera/330827/20250904)). El plazo exacto del art. 34 no apareció en el texto consultado: **no verificado literalmente**, pero las fuentes oficiales coinciden en 10 días corridos.
- Para Mercado Libre (venta a distancia), 30 días supera los 10. Pero «habitualmente no devuelve dinero» **choca** con el derecho de revocación: ejercido en término, el consumidor recupera lo pagado, sin que una nota de crédito lo sustituya (a confirmar con el contador/abogado). Ese reintegro lo condiciona además el circuito de la propia plataforma.
- Venta presencial: la revocación del art. 34 no es el régimen general; las 72 h y el «dinero solo en 24 h» son política comercial válida **sin afectar** la garantía legal por producto defectuoso (Ley 24.240 arts. 11 y ss.). Duración exacta de la garantía legal: no verificado en esta tanda (no se cita número).
- Notas de crédito fiscales: ARCA/RG 4540/2019 pide emitirlas dentro de 15 días corridos del hecho que las origina, las emite quien emitió la factura original y el período ajustado debe constar en el comprobante; en el Libro IVA Digital las NC de ventas se registran con signo negativo ([fuente ARCA](https://biblioteca.afip.gob.ar/search/query/norma.aspx?p=t%3ARAG%7Cn%3A4540%7Co%3A3%7Ca%3A2019%7Cf%3A31%2F07%2F2019)). **No encontré** una fuente que fije si el débito fiscal se resta en el mes de emisión de la NC o en el de la operación: pregunta al contador (§5, pregunta 3).

### 3.3 Cuotas, intereses y recargo del 3 % (C9–C11)
Hoy la factura de la venta incluye el capital; la cuota cobrada se compone de capital (ya facturado), interés (C11: gravado según el contador) y recargo por medio de pago. Facturar «el total de la cuota» repetiría el capital. Sin respuesta sobre qué comprobante externo corresponde a cada componente no se implementa nada; además el recargo del 3 % por transferencia cae bajo la duda de §3.1.

### 3.4 Período por fecha argentina (C15) — hallazgo técnico
`Venta.FechaVenta` **mezcla dos semánticas**: la venta nacida de cotización, Mercado Libre y el valor por defecto guardan un instante UTC (`DateTime.UtcNow`), pero el formulario de venta envía solo una fecha (`<input type="date">`) y `ResolverFechaVenta` la conserva como «medianoche del día», sin hora. Los reportes (`ReporteService`), `DashboardService` y `VentasNetas` comparan y agrupan por el día UTC.

Convertir las lecturas a hora argentina sin normalizar la escritura desplazaría al día anterior las ventas cargadas desde el formulario (00:00 → 21:00 del día previo). Por eso **no se tocó**: C15 requiere una tanda propia (guardar siempre el instante UTC —el día elegido combinado con la hora comercial actual—, convertir los límites de los reportes y mostrar la hora en hora comercial). Es seguro hacerlo en la base nueva (sin históricos). Queda caracterizado hoy por `Caracteriza_VentaDeLas2230ArgentinaDel31DeOctubre_…`.

## 4. P3 y P6

### P3 — netear devoluciones en el Reporte de IVA (recomendación, **no implementada**)
Opción recomendada: restar el IVA informativo de una devolución **solo cuando exista una NC fiscal externa válida registrada en el ERP**, en el período fiscal de esa NC. No se netea automáticamente por la fecha de aprobación de la devolución.
Requisitos previos (todos ausentes hoy): (a) un vínculo del ERP con el comprobante fiscal externo (campos para número/fecha/importe de la NC externa, permiso y auditoría — opción 1 de `docs/facturacion-fiscal-externa.md`), que implica una migración aditiva; (b) la respuesta del contador sobre el período (§5, pregunta 3). Mientras tanto el Reporte de IVA seguirá sin netear y su nota lo advierte.

### P6 — activación de `CriterioFiscalV2`
`CriterioFiscalV2` está **registrada pero no vigente** (`CriterioFiscalRegistry.VersionVigente` = 1): ninguna operación nueva la usa y ninguna regla cambia en producción. Contiene solo C1/C2. Activarla = cambiar `VersionVigente` a 2 con fecha de corte autorizada, sin migración de datos (las operaciones V1 se conservan). Recomendación: activarla desde la puesta en marcha de la base nueva, **después** de que el contador responda las preguntas 1 y 2 de §5 y de decidir qué hacer con C5 (para que el cambio de importes ocurra una sola vez).

## 5. Consulta consolidada al contador (solo lo que no se puede resolver investigando)

1. **Redondeo (C3):** para que Neto + IVA = Total, el ERP absorbe el centavo de redondeo en el IVA (hoy y en V2). ¿Es aceptable, o prefiere otro tratamiento?
2. **Descuento por efectivo y recargos (C4/C7/C8):** ¿el descuento exclusivo por efectivo y los recargos de transferencia, débito y tarjeta en un solo pago son lícitos a la luz del art. 37 inc. c) de la Ley 25.065 y la Res. 51/2017? ¿Qué recargos se mantienen y con qué justificación (financiación en cuotas con CFT exhibido)?
3. **Notas de crédito (C13):** ¿el débito fiscal de una devolución se resta en el mes de emisión de la NC o en el de la operación original? ¿La NC fiscal la emite siempre el sistema externo y el ERP solo registra su número e importe?
4. **Cuotas (C9–C11):** ¿qué comprobante externo corresponde a cada componente de la cuota (capital ya facturado, interés, recargo del 3 %)? ¿El interés se factura en la venta, o en cada cuota? Contradicción a resolver: C11 (interés con IVA) vs. la respuesta preliminar anterior (no alcanzado).
5. **Punitorios (C10):** ¿llevan IVA sí o no (la respuesta fue «no… pero al facturarlos 21 %»)?
6. **Crédito fiscal (C12):** ¿el negocio puede computar el IVA de compras como crédito fiscal? ¿El margen se informa sobre costo sin IVA?
7. **Servicios (C6):** ¿envío y armado van siempre al 21 % (o al de la operación, p. ej. exportación)?

## 6. Decisiones del propietario pendientes

1. **P6 — activación:** ¿activar `CriterioFiscalV2` (redondeo comercial) desde la puesta en marcha, y con qué fecha de corte? (Recomendado: sí, tras las respuestas 1–2.)
2. **P3:** ¿autorizar el vínculo con la NC fiscal externa (migración aditiva) para netear el IVA solo con NC registrada?
3. **C5:** ¿autorizar la tanda que aplica el descuento solo a productos (cambia importes cobrados)?
4. **C14:** ¿se parametrizan 72 h presencial / 30 días Mercado Libre / 24 h de reembolso en el ERP? Y, en ventas a distancia dentro de los 10 días, ¿se permite elegir NC en lugar de reintegro (hoy el operador elige)?
5. **C15:** ¿autorizar la tanda que normaliza `FechaVenta` y pasa los reportes a día argentino?

## 7. Qué impide cerrar P-02

- Respuestas del contador pendientes o contradictorias (§5): C3, C4/C7/C8 (legalidad), C9–C11, C12, C13.
- Inconsistencia de orden de redondeo/ajuste entre caminos (§7-bis), a resolver con C5 antes de activar V2.
- Decisiones del propietario (§6): activación de V2 y fecha de corte, P3, C5, C14, C15.
- Pendiente heredado: política de backfill de `AlicuotaServiciosSnapshot` (`docs/p02-versionado-fiscal-diseno.md` §8.2); no aplica a la base nueva (sin históricos).

## 7-bis. Hallazgo de la revisión independiente de Codex (condición para activar V2)

Con ajuste porcentual del medio de pago (Efectivo/Transferencia) el orden de operaciones difiere entre caminos: la venta directa redondea la base **antes** de aplicar el ajuste (`VentaService.CalcularTotales` → `AplicarAjusteMedioUnPagoAsync`), mientras que el cotizador y la conversión de cotización calculan el ajuste sobre la base con centavos, y el comprobante/Reporte de IVA reconstruyen el desglose desde los brutos originales. Ejemplo con V2 (cálculo de Codex, **no reproducido por mí con una prueba**): producto 1.000,51 al 21 %, transferencia +3 %: venta directa 1.031,03 (neto 851,69 + IVA 179,34); comprobante y Reporte de IVA, mismo total pero neto 852,09 + IVA 178,94; cotización→venta cobra 1.030,53. Cada capa cierra `Neto + IVA = Total`, pero el IVA difiere entre capas y el importe entre caminos. **Es una inconsistencia previa** (con V1 el orden «truncar y luego ajustar» ya existe); V2 la hereda. Corregirla cambia importes de ventas con ajuste, por lo que no se tocó: debe resolverse junto con C5 (misma zona de código) y **antes** de activar V2.

## 8. Qué se implementó y probó en esta tanda

- `Helpers/CriterioFiscal.cs`: `CriterioFiscalV2` (redondeo comercial C1/C2), registrada; `VersionVigente` sigue en 1.
- `Helpers/RecargoMedioPagoIva.cs`: variantes parametrizables por la regla de redondeo (`Discriminar`, `ResumenPorAlicuota`, `CalcularRecargo`) y `AbsorberRedondeo` con residuo con signo; con `Math.Floor` son idénticas a V1.
- `Helpers/FacturaComprobanteBuilder.cs`, `Services/VentaService.cs`: manejan residuos negativos (total redondeado hacia arriba); en V1 nunca ocurre.
- `Services/CotizacionPagoCalculator.cs`: el truncado del cotizador pasa por el criterio vigente (idéntico en V1), para que cotización y venta redondeen igual.
- Pruebas (`FiscalCriterioV2P02Tests`, `FiscalVersionadoP02Tests`): bordes C1/C2 (0,49 / 0,50 / 0,51 / 0,99); los 100 valores de centavos cierran `Neto + IVA = Total`; venta exenta redondeada hacia arriba sin IVA; recargo no inventado; devolver todo reintegra exactamente lo cobrado; cadena venta nueva V2 → comprobante → Reporte de IVA; V1 sin cambios; producción sin V2 vigente. La prueba de «versión inexistente» pasó de 2 a 3 porque la 2 ya existe.
