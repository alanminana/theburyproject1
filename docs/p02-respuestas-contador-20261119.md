# P-02: respuestas del contador C1–C15 (2026-11-19), clasificación y estado

Fecha: 2026-11-19. Base: `origin/main` 1e210e34. Fuente de las respuestas: transmitidas por el propietario (registradas en Notion). **Son respuestas del contador, no una validación legal definitiva ni una autorización de implementación.** Complementa `docs/iva-decisiones-contador.md` §5, `docs/p02-escenarios-y-alternativas.md` y `docs/p02-versionado-fiscal-diseno.md`.

**Estado de P-02: EN REVISIÓN (no finalizada).** Qué impide cerrarla está en §7; lo implementado desde esta tabla, en §9.

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
| C5 | Descuento en efectivo solo sobre productos, no sobre envío/armado | **A (comercial)** | **Implementado en `CriterioFiscalV2`** (§9): venta directa, conversión de cotización, cotizador y vista previa | V1 conserva el descuento sobre toda la base. Efectivo/Transferencia con descuento (porcentaje negativo); el recargo sigue sobre toda la base. |
| C6 | Envío y armado llevan IVA | **B** | Ya así: 21 % por snapshot (`AlicuotaServiciosSnapshot`) | La alícuota «aplicable según la operación» no se respondió; se mantiene 21 %. |
| C7 | Recargo de tarjeta con IVA del producto y parte del precio gravado | **B/C** | Ya así en V1; **no se toca** | El propio contador dudó. Ver §3.1: el recargo en un solo pago puede estar prohibido. |
| C8 | Recargos de tarjeta/transferencia se documentan por factura | **C** | Sin cambios | No dijo qué recargos son legalmente permitidos por tarjeta, cuotas o transferencia. |
| C9 | Cuota $618 = capital 500 + interés 100 + recargo 18; documentar el total con IVA del producto | **C** | Sin cambios (Caja 618, sin factura) | Facturar el total duplicaría el capital ya incluido en la factura de la venta. Ver §3.3. |
| C10 | Punitorios sin IVA «porque el capital ya lo tiene», pero «al facturarlos llevarían 21 %» | **C** (contradictoria) | Sin cambios (sin IVA ni comprobante) | Requiere aclaración. |
| C11 | Interés de financiación propia con IVA, documentado al vender en el documento firmado | **B** | Sin cambios | Contradice la respuesta preliminar anterior («no alcanzado por IVA», `iva-matriz-diferencias.md` 2.1). Alícuota, devengo y comprobante externo sin definir. |
| C12 | Sin respuesta (no sabe si el IVA de compras es crédito fiscal) | **D** | Sin cambios: ganancia sobre costo con IVA | No se asume el régimen fiscal del negocio. |
| C13 | Devolución en 72 h: se cancela la deuda y se emite NC, normalmente el mismo día | **B** | Sin cambios | No define período de cómputo ni la relación NC interna / NC fiscal externa. Ver §3.2 y §4 (P3). |
| C14 | 72 h presencial, 30 días Mercado Libre; dinero solo dentro de 24 h con producto perfecto; después NC | **B** | Sin cambios (`DiasLimiteDevolucion` = 30, reembolso o NC a elección) | Describe la práctica comercial, no los derechos legales. Ver §3.2. |
| C15 | Los informes usan la fecha argentina (31/10 22:30 ART es octubre) | **A** | **Implementado** (§9): `FechaVenta` se guarda siempre como instante UTC y todo informe/pantalla usa el día comercial argentino | Sin migración ni cambio de datos históricos. |

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

## 9. Tandas A y B (2026-11-19): importes únicos, C5 y día comercial (C15)

**Defecto de Codex (§7-bis) — reproducido y corregido.** Con V2, producto 1.000,51 al 21 % y transferencia +3 %: venta directa 1.031,03 (neto 851,69 + IVA 179,34); la conversión de cotización cobraba 1.030,53 (ajuste 30,02 sobre la base con centavos) y el comprobante/Reporte de IVA reconstruían neto 852,09 + IVA 178,94. Orden único (solo `CriterioFiscalV2`; V1 intacta, byte a byte): **la base (productos con descuentos + servicios) se redondea a pesos con el criterio fiscal y recién después se aplica el ajuste del medio de pago** (`ICriterioFiscal.RedondearBaseAntesDeAjuste`). Lo usan venta directa (ya lo hacía), cotizador, vista previa de la conversión y conversión real. El comprobante, el Reporte de IVA y la conversión reconstruyen el desglose con el monto exacto del ajuste persistido (`Venta.AjusteMedioPagoMonto`, `Discriminar(…, ajusteMedioPersistido)`), no por heurística de centavos.

**C5.** `ICriterioFiscal.DescuentoDeMedioSoloSobreProductos` (V2 = true): el descuento de Efectivo/Transferencia (porcentaje negativo) se calcula sobre `base − (armados + envío)`; el total es siempre `base + ajuste`. Ejemplo (1.000 + armado 450 + envío 200, −5 %): V1 1.567,50; V2 1.600,00. Limitación conocida: con alícuotas mixtas el descuento se prorratea entre todas las alícuotas (también servicios) en el desglose informativo de IVA; el total cobrado es exacto.

**C15.** `Services/FechaComercial.cs`: `Venta.FechaVenta` se guarda como instante UTC (fecha de formulario = día comercial elegido + hora comercial actual; edición del mismo día conserva el instante). Los períodos de Reporte de ventas/IVA/por producto/comisiones/agrupados, el Dashboard (hoy/mes/año/7 días/12 meses y devoluciones netas), el filtro de la lista de ventas, la validación de fecha de facturación, el comprobante, los contratos/documentos y la conciliación de Caja convierten día comercial ⇄ UTC. 31/10 22:30 ART (01/11 01:30 UTC) se contabiliza en octubre. Sin cambios en datos históricos (la base nueva no los tiene; una base con ventas de formulario de la versión anterior las tiene como «medianoche argentina» guardada como si fuera UTC: antes de usar este código sobre una base histórica hay que decidir su tratamiento).

**Pruebas nuevas/actualizadas:** `Convertir_V2_TransferenciaConCentavos_CobraLoMismoQueLaVentaDirecta`, `CriterioV2_AjusteMedioPago_VentaComprobanteYReporteIvaCoinciden` (Transferencia +3 % y Efectivo −5 %), `DescuentoDeEfectivo_C5_*` (venta y conversión), `Simular_V2_*` y `Simular_DescuentoDeEfectivo_SoloSobreProductosEnV2`, `C15_VentaDeLas2230…`, bordes de mes por día comercial, agrupación por día/mes y `ResolverFechaVenta_C15_*`. Se actualizaron las pruebas que sembraban fechas con la semántica anterior (día UTC).

**No se cambió `VersionVigente` (sigue en 1) ni se desplegó ninguna regla monetaria en producción.**

**Revisión independiente de Codex (adversarial, N2) sobre este lote.** (1) *Alto, confirmado y corregido*: con C5 el descuento seguía repartiéndose entre servicios en el desglose de IVA y la venta directa inferia los servicios de la base redondeada; ahora el reparto excluye armados/envío (`AjusteMedioContexto`) y todos los circuitos usan los servicios exactos (regresión con alícuota 10,5 % + 21 % y centavos: 1.150 = neto 1.025,46 + IVA 124,54 en venta directa, conversión, comprobante y reporte). (2) *Alto, escalado a decisión del propietario*: las ventas de formulario guardadas antes de este cambio están como «medianoche argentina» sin conversión; con este código se leerían como UTC y cambiarían de día. **Precondición de despliegue: este código es solo para la instalación nueva con base limpia; no apuntarlo a la base histórica** sin una transición verificada por procedencia (no se aplica ninguna).

## 10. Tanda C (2026-11-19): devoluciones, política comercial y vínculo con la NC fiscal externa (P3 preparado, SIN efecto fiscal)

- **Política comercial C13/C14** (`Helpers/DevolucionPolitica.cs`): 72 h venta presencial, 30 días Mercado Libre (venta con orden ML), reintegro en dinero solo en las primeras 24 h (presencial; después, nota de crédito). Es **informativa y nunca bloquea**: al crear una devolución fuera de política se agrega una constancia en `ObservacionesInternas` y al log; los motivos de garantía/defecto/faltante/error de despacho/producto incorrecto jamás quedan «fuera de política» (derechos del consumidor). No cambia el tope técnico existente de 30 días de `PuedeDevolverVentaAsync`.
- **Reintegro con C5**: con `CriterioFiscalV2` el descuento de efectivo se calculó solo sobre productos, así que devolver todo reintegra exactamente lo cobrado por los productos (antes del ajuste se prorrateaba el descuento también a servicios y se reintegraba de más). V1 intacta.
- **Vínculo con la NC fiscal externa** (`NotaCredito.FiscalExterna*`, `INotaCreditoFiscalExternaService`, migración aditiva `P02NotaCreditoFiscalExterna`: 8 columnas nullables + índice único filtrado `IX_NotasCredito_FiscalExterna`; **NO aplicada a ninguna base**): punto de venta (1–5 díg.), tipo (NCA/NCB/NCC/NCM/NCE), número (1–8 díg.), fecha (no futura ni anterior a la venta), importe (> 0, ≤ NC interna, ≤ venta y suma de NC fiscales ≤ venta), CAE opcional (14 díg.). Rechaza duplicados (validación + índice único), un segundo vínculo sobre la misma NC (inmutable) y ventas sin factura vigente. Exige el permiso `devoluciones.approve` (existente; un permiso propio `linkfiscal` requiere sembrar permisos y queda como decisión) y audita en el mismo guardado (`devoluciones / vincular_nc_fiscal_externa`).
- **Sin neteo de IVA**: ni el Reporte de IVA ni ventas netas leen estos campos. Activarlo exige tu autorización P3 y la respuesta del contador sobre el período (§5, pregunta 3). Sin pantalla nueva (no se rediseña la interfaz): hoy el vínculo se carga por servicio.

## 11. Tanda D (2026-11-20): validación en ambiente de ensayo, pantalla de NC fiscal externa y cierre técnico

**Activación de ensayo de V2 (no productiva).** `Ensayo:CriterioFiscalVersion` (clave de configuración, `Helpers/EnsayoCriterioFiscal`) solo se admite en el entorno `Development`; en `Production`, `Staging` o `Testing` **aborta el arranque** (falla cerrada, nunca se ignora). Sin la clave la versión vigente sigue siendo 1. Se usó únicamente en un stack descartable (`tbp-p02-ensayo`, SQL Server y red propios, datos sintéticos) en la VM; se destruye con `docker compose -p tbp-p02-ensayo down -v`.

**Permiso `notascredito.linkfiscal`** (propio; ya no se reutiliza `devoluciones.approve`). Asignación inicial mínima: **SuperAdmin y Administrador** (grant «todo salvo la lista»). Gerente (excluido explícitamente del grant en bloque del módulo), Contador, Técnico, Vendedor, Cajero y Repositor **no** lo reciben; se asigna a mano desde Seguridad > Roles. Se verificó por rol (prueba del seeder) y en navegador: el Gerente ve el vínculo en solo lectura, sin formulario, y un POST directo es rechazado (`AccessDenied`).

**Pantalla.** Detalle de la devolución (`Devolucion/Detalles`), dentro de la tarjeta «Nota de crédito»: tipo, punto de venta, número, fecha, importe y CAE opcional; muestra la venta y la NC interna vinculadas; el vínculo ya cargado es de solo lectura. Acción `POST Devolucion/VincularNcFiscal` (antiforgery + permiso). Auditoría (`notascredito`): alta (`vincular_nc_fiscal_externa`, con tipo, punto de venta, número y venta; **sin CAE ni importe**), intento rechazado por validación (`..._rechazado`, solo la cantidad de errores) e intento sin permiso. **No netea IVA ni cambia ningún importe.**

**Migración `P02NotaCreditoFiscalExterna`** probada en un SQL Server descartable (contenedor `bury-p02-throwaway-sql`, ya eliminado): aplica (8 columnas nullable + índice único filtrado `IX_NotasCredito_FiscalExterna`), una fila previa queda intacta, el índice rechaza un comprobante repetido y admite varias NC sin vínculo, **revierte** (columnas e índice desaparecen, las filas se conservan, `__EFMigrationsHistory` vuelve a D03) y se reaplica. No se aplicó a ninguna base real; la base de preprod de la VM conserva 144 migraciones.

**Validación en navegador real (Chrome, instancia de ensayo con V2; evidencia en `D:\git\QA-AUDITORIA\p02-ensayo-20261120`).** Importes mostrados contra lo persistido en SQL Server:

| Recorrido | Pantalla | SQL | Resultado |
|---|---|---|---|
| Cotizador: 3 alícuotas (21 %, 10,5 %, exento) + armado 450 | base 2.501,00; Efectivo −5 % = 2.398,45; Transferencia +3 % = 2.576,03 | `TotalBase` 2.501,00; `TotalSeleccionado` 2.398,45 / 2.576,03 | coincide (el descuento excluye el armado) |
| Conversión cotización → venta (Efectivo) | Total 2.398,45; descuento −102,55 | `Ventas`: neto 2.083,52 + IVA 314,93 = 2.398,45; ajuste −102,55; versión 2 | coincide |
| Confirmar y facturar | factura FA-B-…0001 | `Facturas` 2.398,45 = `MovimientosCaja` 2.398,45 | coincide |
| Transferencia con recargo | 2.576,03 | venta, factura y Caja 2.576,03; ajuste +75,03 | coincide |
| Comprobante interno | neto 2.083,52, IVA 314,93, total 2.398,45, descuento repartido solo entre productos | = venta | coincide |
| Reporte de IVA (2 ventas) | neto 4.322,31, IVA 652,17, total 4.974,48 (y por alícuota) | suma de `Ventas` | coincide |
| Reporte de ventas / ganancias | bruto 2.398,45; ganancia 1.283,52 (neto 2.083,52 − costo 800) | = | coincide |
| Devolución parcial (producto 21 %) | 950,32 | = lo cobrado por ese producto (descuento prorrateado solo entre productos) | exacto |
| NC fiscal externa: importe inválido / válido | 3 errores de validación / «vinculada» | `NotasCredito.FiscalExterna*` y 2 eventos de auditoría | correcto |
| C15: venta a 01/11 01:30 UTC | «31/10/2026 22:30 hs»; Reporte de IVA y de ventas de **octubre** la incluyen, el de noviembre no | `FechaVenta` 2026-11-01 01:30 UTC | correcto |

**No validado en navegador en esa tanda (RESUELTO en §12, tanda E):** venta directa por el asistente de Create (comparte el cotizador y se cubre por pruebas automatizadas: matriz de alícuotas mixtas, FiscalVersionado), envío a domicilio (mi guion no logró completar el modal de envío; cubierto por pruebas), créditos/cuotas/recibos y devolución total.

**Hallazgos de esta tanda.** (1) V1 con ajuste del medio de pago y alícuotas mixtas deja una diferencia histórica de hasta ~$0,7 entre el neto guardado en la venta y el reconstruido en comprobante/reporte (el total y neto + IVA = total sí cierran): V1 es inmutable y no se corrige; V2 no la tiene (matriz `FiscalAlicuotasMixtasP02Tests`: 40 casos). (2) Corregido: la hora de emisión de la factura en el detalle de la venta y la de registro del vínculo fiscal se mostraban en UTC (03:45 en vez de 00:45); ahora usan la hora comercial. (3) Resuelto en §12 (antes pendiente): `Cotizacion.Fecha` y otras marcas de tiempo informativas se muestran en UTC (la cotización guardada a las 00:43 argentinas muestra 03:43).

## 12. Tanda E (2026-11-20): validación completa en navegador, fechas, P3/P6 preparados y clasificación final

**Estado de P-02: EN REVISIÓN (no finalizada).** Todo lo técnicamente ejecutable está hecho y probado; lo que queda depende de autorizaciones del propietario (P3, P6) y de respuestas fiscales del contador. Procedimientos exactos de activación: `docs/p02-procedimiento-activacion.md`.

**Validación en navegador real (Chrome, instancia de ensayo con V2 contra un SQL Server descartable con datos sintéticos; capturas en `D:\git\QA-AUDITORIA\p02-ensayo-20261120\tanda-e-20261120`).** Complementa §11 (que no se repite). Lo que §11 declaraba «no validado» se validó así:

| Recorrido | Esperado (cálculo independiente) | Pantalla | SQL | Resultado |
|---|---|---|---|---|
| Venta directa (asistente `Venta/Create`): exento 1.000,99 + 10,5 % 1.000,51 + 21 % 1.000,51; Efectivo −5 % | base 3.002,01 → V2 3.002 → ×0,95 = **2.851,90** | Revisión 2.851,90; Detalle: neto 2.596,63 + IVA 255,27 | `Ventas` y `Facturas` 2.851,90 (versión fiscal 2); Caja 2.851,90 | PASS |
| Venta con armado 450 + envío 200 + Transferencia +3 % (modal de envío) | base 2.651,50 → 2.651 → ×1,03 = **2.730,53**; Efectivo −5 % solo sobre productos (C5) = 2.550,95 | 2.730,53; IVA por alícuota: 21 % 537,19 + 112,81; 10,5 % 905,44 + 95,07; exento 1.000,99 | venta y factura 2.516,92 + 213,61 = 2.730,53; Caja 2.730,53 | PASS |
| Envío a domicilio: causa de la falla anterior | — | El modal exige elegir el tipo y **Guardar datos de envío**; con los precios globales de envío/armado en $0 (base nueva) el costo es $0 | `ServiciosVentaPrecios` vacía | **Era el guion y los datos, no un defecto funcional** |
| Costo real y ganancia | venta 2: neto 2.516,92 − costo (productos 2.000 + envío 120 + armado 300) = **96,92** | Reporte de ventas: 96,92 (venta 1: −403,37; venta 3: −173,13) | — | PASS |
| Crédito: venta a 3 cuotas (plan +5 %) | 1.001 × 1,05 = 1.051,05; cuota 350,35 | 3 cuotas de 350,35; contrato generado | `Creditos` 1.051,05; `Cuotas` capital 1.001,00 + interés 50,05; sin movimiento de Caja al confirmar | PASS |
| Cobro de la cuota 1 por Transferencia | 350,35 × 1,03 = **360,86** (recargo 10,51) | resultado y previsualización idénticos | Caja 360,86 (base 350,35 + recargo 10,51); saldo 667,33 (capital restante, sin duplicar) | PASS |
| Pago parcial cuota 2 (100 por Transferencia) y adelanto cuota 3 (Efectivo −5 %) | 103,00 y 332,83 (descuento 17,52) | Recibos REC-…01 (100,00 + 3,00 = 103,00) y REC-…02 (350,35 − 17,52 = 332,83) | Caja 103,00 y 332,83; saldo 238,43 | PASS |
| Punitorio calculado | informativo 50,00; **no** integra el total cobrable hasta que lo aplica un usuario con permiso | así se muestra | — | PASS (comportamiento existente; no se tocó el tratamiento fiscal) |
| Devolución total con Nota de Crédito (venta con armado, envío y recargo) | reintegro ≤ lo cobrado por los productos devueltos | 2.061,16 (parte proporcional de lo cobrado; los servicios no se reintegran) | `Devoluciones` y `NotasCredito` 2.061,16; stock repuesto; sin egreso de Caja | PASS |
| Devolución total con Reembolso (venta con descuento de efectivo) | exactamente lo cobrado: **2.851,90** | 950,94 + 950,48 + 950,48 = 2.851,90 | egreso de Caja 2.851,90 | PASS |
| Reporte de IVA (ventas facturadas) | suma de las facturas | neto 5.113,55 + IVA 468,88 = 5.582,43; la venta 3 (confirmada, sin facturar) no discrimina IVA | = suma de `Ventas` 1 y 2 | PASS |

**Defectos encontrados en esta tanda y corregidos:**

1. *La hora visible dependía de la zona del servidor.* 63 usos en vistas y 15 en C# usaban `ToLocalTime()` (el contenedor corre en UTC: una cotización de las 00:43 se veía 03:43; también Caja, Kardex, documentos, Mercado Libre). Ahora toda hora de presentación pasa por `AHoraComercial()` (zona `America/Argentina/Buenos_Aires` explícita, sin conversión doble). Se distinguió entre instantes UTC (se convierten) y fechas comerciales sin hora, como el vencimiento de la cotización (se muestran tal cual). Se convirtieron además horas que se mostraban sin convertir (detalle y turno de Caja, línea de tiempo de la venta, autorización, punitorios). Prueba guardián: ninguna vista ni servicio puede volver a usar `ToLocalTime()`; pruebas de medianoche, cambio de día, mes, año y bisiesto. **Sin conversión masiva de datos históricos** (lo guardado ya era UTC).
2. *El desglose de la Revisión no cerraba.* Con V2 la base se redondea a pesos antes del ajuste: «productos + armados + envío − descuento» podía diferir del Total hasta 0,50 (2.001,50 + 450 + 200 − 100,05 = 2.551,45 ≠ 2.550,95). Se agregó la línea explícita «Redondeo a pesos». Verificado a 1366 px y 390 px.
3. *Concurrencia del vínculo fiscal:* prueba de doble envío sobre la misma NC (el segundo no pisa al primero).

**Observaciones (no son defectos de importe):** (a) la vista previa del modal «Crear devolución» muestra el total de lista (3.002,01) mientras lo persistido es lo cobrado (2.851,90); (b) el descuento de Efectivo en el cobro de cuotas se guarda en la cuota (−17,52) pero la columna `RecargoMedioPago` del movimiento de Caja queda vacía (el `Monto` es correcto); (c) el reintegro de una devolución con recargo incluye la parte proporcional del recargo, sin superar lo cobrado por los conceptos devueltos.

### Clasificación final de criterios

FINALIZADO = implementado y validado · PREPARADO = implementado, falta activación o aceptación · BLOQUEADO = requiere decisión externa · DIFERIDO = fuera del alcance aprobado.

| # | Criterio | Categoría | Evidencia |
|---|---|---|---|
| C1/C2 | Redondeo comercial (≤ 0,50 abajo; > 0,50 arriba) | **PREPARADO** | `CriterioFiscalV2` implementada y validada en ensayo (3.002,01 → 3.002); la activación es P6 (`docs/p02-procedimiento-activacion.md` §3) |
| C3 | Residuo de redondeo | **BLOQUEADO** | pregunta 1; hoy se absorbe en el IVA (`Neto + IVA = Total`) |
| C4 | Descuento en efectivo reduce base e IVA | **PREPARADO** (técnico) / **BLOQUEADO** (licitud) | implementado; licitud frente al art. 37 inc. c) Ley 25.065 y Res. 51/2017 sin confirmar |
| C5 | Descuento solo sobre productos | **PREPARADO** | V2; validado en navegador (2.550,95) y por la matriz de 40 casos; activación = P6 |
| C6 | Envío y armado con IVA | **FINALIZADO** (21 %) / **BLOQUEADO** (excepciones) | 21 % por snapshot validado; excepción por operación sin respuesta (pregunta 6) |
| C7/C8 | Recargos por medio de pago | **BLOQUEADO** | V1 sin cambios; licitud sin respuesta (pregunta 2) |
| C9 | Documentar capital, interés y recargo de la cuota | **BLOQUEADO** | hoy Caja = cuota + recargo y recibo exacto (validado); comprobante fiscal sin definir (pregunta 4) |
| C10 | IVA de punitorios | **BLOQUEADO** | respuesta contradictoria; ver evidencia; no se implementa |
| C11 | IVA y documentación del interés de financiación propia | **BLOQUEADO** | evidencia a favor de «gravado» (unicidad, art. 10); alícuota, devengo y comprobante sin definir |
| C12 | Crédito fiscal de compras / margen | **BLOQUEADO** | sin respuesta; ganancia sobre costo con IVA, sin cambio |
| C13 | Devolución, NC y período del débito fiscal | **PREPARADO** (P3) / **BLOQUEADO** (período) | vínculo con NC fiscal externa implementado y probado (permiso, CSRF, auditoría, duplicados, importes, fechas, concurrencia, navegador); migración **no aplicada**; neteo de IVA diseñado y **no activo** |
| C14 | 72 h presencial / 30 días ML / 24 h para dinero | **FINALIZADO** (política informativa) | nunca bloquea; garantías y derechos del consumidor intactos |
| C15 | Fecha argentina | **FINALIZADO** | día comercial en períodos y reportes; hora de presentación corregida en esta tanda; sin cambio de datos históricos |
| P3 | Neteo de IVA por NC fiscal | **PREPARADO** / **BLOQUEADO** | falta autorización (aplicar migración) y período (pregunta 3) |
| P4 | Leyenda «no válido como factura» | **FINALIZADO** | implementada |
| P5 | Versionado por operación | **FINALIZADO** | cada venta guarda su versión |
| P6 | Activación de V2 | **PREPARADO** | procedimiento, scripts y verificación listos; falta autorización y fecha de puesta en marcha |

### Evidencia de fuentes para el contador (no concluyente; no reemplaza su criterio)

- **Intereses de financiación (C11):** según la doctrina consultada, el art. 10 de la Ley 23.349 integra al precio neto los intereses por pago diferido o fuera de término y rige un criterio de **unicidad**: la financiación del vendedor sigue el tratamiento de la operación principal, incluso si esta fuera exenta o no gravada (fallo CNACAF Sala V, 2007). Esto contradice la respuesta preliminar «no alcanzado» y coincide con C11 (con IVA). Hay crítica doctrinaria a la reglamentación ([El Cronista](https://www.cronista.com/fiscal/el-principio-de-unicidad-de-los-intereses-en-el-iva/)).
- **Punitorios (C10):** el decreto reglamentario trata aparte los intereses resarcitorios y punitorios por incumplimiento de una operación gravada y ubica el perfeccionamiento del hecho imponible en el devengo o cobro, lo que sugiere que **sí** estarían alcanzados («al facturarlos llevarían 21 %»). No se halló una fuente que lo confirme sin ambigüedad ni se leyeron en texto completo los dictámenes DAT 23/2002 y 65/2002. **No se verificó** contra el texto ordenado vigente (con la Ley 27.743).
- Se consultó solo por búsqueda web, sin acceso a la base oficial de ARCA: verificar siempre contra el texto vigente.

### Consulta única al contador (mínimo indispensable, con el ejemplo del ensayo)

1. **Redondeo (C3).** Neto 2.516,92 + IVA 213,61 = 2.730,53: el centavo o peso de redondeo se absorbe en el IVA. ¿Es aceptable?
2. **Descuento y recargos por medio de pago (C4/C7/C8).** ¿Son lícitos el descuento por efectivo (−5 %) y el recargo de transferencia, débito o tarjeta en un pago (+3 %) frente al art. 37 inc. c) de la Ley 25.065 y la Res. 51/2017? ¿Cuáles se mantienen y con qué documentación?
3. **Nota de crédito (C13).** ¿El débito fiscal de una devolución se resta en el período de la **NC fiscal** o en el de la operación original? ¿Confirma que la NC fiscal la emite siempre el sistema externo y el ERP solo registra número, fecha e importe?
4. **Cuota con interés y recargo (C9/C10/C11).** Cuota 350,35 = capital 333,67 + interés 16,68 (venta de 1.001 a 3 cuotas, +5 %), cobrada por transferencia con recargo del 3 % (10,51 → 360,86). ¿Qué comprobante externo corresponde a cada componente, con qué alícuota y en qué momento (en la venta o en cada cuota)? Si hay mora, ¿el punitorio lleva IVA (sí o no, sin «sí y no»)?
5. **Crédito fiscal (C12).** ¿El IVA de compras es crédito fiscal recuperable para el negocio (margen sobre costo sin IVA)?
6. **Servicios (C6).** ¿Envío y armado van siempre al 21 % o hay casos con otra alícuota?

### Decisiones del propietario que siguen pendientes

P3 (aplicar la migración a la base real), P6 (activar V2, con fecha = puesta en marcha real), neteo de IVA (tras la respuesta 3) y la política de C4/C7/C8 (tras la respuesta 2). Nada de esto se aplicó.

## 13. Tanda F (2026-11-20): P3 y P6 autorizadas por el propietario — actualización de estados

El propietario **aprobó expresamente P3 y P6**. No se vuelve a consultar. Cambios respecto de §12:

| # | Estado anterior | Estado actual | Evidencia |
|---|---|---|---|
| P6 / C1 / C2 / C5 | PREPARADO (falta autorización) | **PREPARADO: autorizado, implementado y ensayado; se aplica en la puesta en marcha real** | clave `FISCAL_CRITERIO_V2_DESDE` (versión resuelta al arrancar el proceso, sin cambio en caliente; inválida = no arranca; preflight); ensayo general en la VM: V1 antes, V2 desde el redeploy de puesta en marcha, S3 = 1.001,00 (`docs/p02-procedimiento-activacion.md` §3 y §6) |
| P3 / C13 (vínculo) | PREPARADO (falta autorización) | **PREPARADO: autorizado, migración ensayada con el despliegue real (144 → 145), scripts aplicar/revertir validados; se aplica en la puesta en marcha real** | ensayo §6; `p02-nc-fiscal-externa-up/down.sql` |
| Concurrencia entre NC de una misma venta | residuo documentado | **FINALIZADO** | el vínculo toca la venta en el mismo guardado (token de concurrencia); prueba que falla sin el fix; revisión de Codex |
| Neteo de IVA por NC fiscal | BLOQUEADO (período) | **BLOQUEADO (período)** — **no se activa** | pendiente de la respuesta 3 del contador |
| Aplicación real a la base de producción | — | **BLOQUEADO (externo)**: base nueva con credenciales reales, fecha real de puesta en marcha y offsite de respaldos | `docs/p02-procedimiento-activacion.md` §7; la base de la VM es de preproducción con datos sintéticos y no se usa |

Los demás criterios (C3, C4/C7/C8, C9–C12 y el período de C13) siguen BLOQUEADOS por el contador (consulta única de §12).
