# P-02: cuadro de situación, escenarios numéricos y alternativas

Fecha: 2026-11-12. Base: `origin/main` 56ddfe55. **Solo documentación: no cambia alícuotas, criterios de IVA, intereses, notas de crédito ni reglas comerciales.** Complementa `docs/iva-decisiones-contador.md` (§5), `docs/facturacion-fiscal-externa.md` y `docs/iva-preservacion-historica.md`. P-02 sigue ABIERTA. Los números de los escenarios son los que ya fija `FiscalCalculosF4Tests` / `FiscalConciliacionF4Tests` (52 tests Fiscal*, verdes al 2026-11-12) salvo donde se indica «calculado a mano».

## 0. Conteo verificado

- Aclaraciones del contador: **15** (C1–C15, `iva-decisiones-contador.md` §5-a). Coincide.
- Decisiones del propietario: **6** (P1–P6, §5-b). Coincide.
- Además hay 8 pendientes fiscales «por fila» (§2.1–2.8) y 9 puntos de validación en `facturacion-fiscal-externa.md` §5; los C/P son la versión mínima de esos.
- Inconsistencias menores de los docs (no cambian código): `iva-decisiones-contador.md` salta del §3 al §5 (no hay §4) y P3 cita «fila 2.5a», que no existe como subtítulo (es la parte «netear devoluciones» del §2.5). C14 habla de «7 días»; el ERP usa 30 días (ver abajo). El PR #280 (F4) figura MERGED (2026-11-11).

## 1. Cuadro: resuelto / falta / qué toca

Estado: RESUELTO = respuesta del contador + implementación; PARCIAL = criterio provisional implementado sin validar; ABIERTO = sin criterio.

| # | Tema | Estado | Información que falta | Código afectado si cambia (ruta:línea) |
|---|---|---|---|---|
| C1/C2 | Redondeo del total (hoy `Math.Floor` a pesos) | PARCIAL (el contador describió redondeo distinto) | Regla exacta: ¿comercial a pesos, a centavos, o truncado? | `Services/VentaService.cs:3768` (`TruncarAPesos`), `Services/CotizacionPagoCalculator.cs:729`, `Helpers/RecargoMedioPagoIva.cs:27,114` (`CalcularRecargo`, `Discriminar`), `Services/DevolucionService.cs:367-385` (`RecargoPorLinea`) |
| C3 | Dónde se absorbe el centavo (IVA, luego neto) | PARCIAL | Idem | `Helpers/RecargoMedioPagoIva.cs:AbsorberTruncado` (fin del archivo), `Helpers/FacturaComprobanteBuilder.cs:396` |
| C4 | Descuento efectivo reduce base | PARCIAL | Comercial vs financiero | `Helpers/RecargoMedioPagoIva.cs:Discriminar` (signo negativo), `Services/VentaService.cs:4041-4065` |
| C5 | Descuento alcanza envío/armado | PARCIAL | Idem | `Services/VentaService.cs:4021-4031` |
| C6 | Envío/armado al 21 % | PARCIAL | Alícuota y si va aparte | `Helpers/ServiciosVentaIva.cs:10-30`; usos: `Helpers/FacturaComprobanteBuilder.cs:112-137`, `Services/ReporteService.cs:323`, `Services/CotizacionConversionService.cs:420,575`, `Services/VentaService.cs:4092,4423`, `ViewModels/FacturaViewModel.cs:88` |
| C7/C8 | Recargo de tarjeta/transferencia a la alícuota de los ítems | PARCIAL | Si es precio gravado o concepto financiero; qué planes admiten recargo | `Helpers/RecargoMedioPagoIva.cs:Repartir`, `Services/ReporteService.cs:330`, `Services/VentasNetas.cs:152,176` |
| C9 | Recargo 3 % y capital/interés de cuotas: ¿qué se factura? | ABIERTO | Qué comprobante, si alguno | `Services/CreditoService.cs:122,700,748,850` (cobro con recargo), recibo `RECIBO_COBRANZA` |
| C10 | Punitorios con IVA | ABIERTO | Sí/no | `Services/PunitorioCalculator.cs:38` (hoy sin IVA ni comprobante) |
| C11 | Interés del crédito en factura/contrato | ABIERTO | Exento/gravado/no alcanzado; documento | `Services/VentaService.cs:1458,3167-3168` (`TotalAPagar`, `InteresTotal`), contrato de crédito |
| C12 | IVA de compra como crédito fiscal | ABIERTO | Condición del negocio | `Services/ReporteService.cs:519` (margen por producto), `:784`; `Services/VentasNetas.cs:109,227` (costo); `Services/PrecioService.cs:498-503`; `Services/MercadoLibrePricingService.cs` (precio mínimo) |
| C13 | Netear devoluciones en Reporte de IVA | ABIERTO (lista para autorizar, P3) | Período de la NC | `Services/ReporteService.cs:281-345` (no netea), `Services/VentasNetas.cs:140-160` (sí netea en Ventas) |
| C14 | Plazo de devolución / reembolso vs NC | ABIERTO | Plazo legal aplicable (el doc dice 7 días; la norma de consumo para ventas a distancia suele citarse como 10 días corridos: **a confirmar por el contador**) y desde cuándo corre | `Services/DevolucionService.cs:18` (30 días), `:841-860` (`PuedeDevolverVentaAsync`, usa `.Days` truncado), `:463-500` (aprobar y NC) |
| C15 | Período fiscal = día argentino vs UTC | ABIERTO | Husos/criterio | `Services/ReporteService.cs:281-290,709,721` (`FechaVenta` UTC), `Services/DevolucionService.cs:274,463` |
| P1/P2 | Facturación fiscal externa / interna | DECIDIDO parcialmente (2026-11-11: externa, sin ARCA/CAE por ahora) | Confirmación final y si P2 es proyecto aparte | `Helpers/ComprobanteInternoLeyenda.cs`, `Views/Venta/ComprobanteFactura_tw.cshtml` |
| P3 | Netear NC en Reporte IVA | ABIERTO | Autorización | idem C13 |
| P4 | Leyenda «no válido como factura» | RESUELTO en código (texto legal pendiente del contador) | Redacción legal | `Helpers/ComprobanteInternoLeyenda.cs:9` |
| P5 | Versionar criterio de cálculo por operación | ABIERTO, **bloquea todo cambio fiscal** | Autorización | `Services/ReporteService.cs:281-345` y `Helpers/FacturaComprobanteBuilder.cs` recalculan en lectura (`iva-preservacion-historica.md` §2-3) |
| P6 | Autorización por fila y fecha de corte | ABIERTO | Lista de filas | n/a |

## 2. Escenarios de prueba (casos numéricos)

Valores con IVA incluido salvo indicación. `A` = 21 %, `B` = 10,5 %.

| Esc. | Pregunta | Entrada | Resultado hoy (verificado por test) | Resultado alternativo (calculado a mano) |
|---|---|---|---|---|
| E1 (C1) | Redondeo en .50 | ítems 1.000,50 | total 1.000 | comercial: 1.001 (+1,00) |
| E2 (C2) | Redondeo en .51/.99 | 1.000,51 / 1.000,99 | 1.000 / 1.000 | 1.001 / 1.001 (+1,00 en ambos) |
| E3 (C3) | Dónde va el centavo | 1.000,49 | 1.000 = neto 826,85 + IVA 173,15 | sin truncar (centavos exactos): 1.000,49 = 826,85 + 173,64 |
| E4 (C4) | Descuento efectivo | 3.000, −5 % | 2.850 = 2.355,37 + 494,63 | descuento financiero (base sin cambio): IVA 520,66 sobre neto 2.479,34, y el descuento 150 se informa aparte |
| E5 (C5) | Descuento sobre servicios | producto 1.000 + envío 200, −5 % | descuento alcanza ambos (1.140) | solo producto: 1.000×0,95 + 200 = 1.150 |
| E6 (C6) | Alícuota de servicios | envío 200, armado 450 | 165,29 + 34,71; 371,90 + 78,10 | si fueran 10,5 %: 180,99 + 19,01; 407,24 + 42,76 |
| E7 (C7) | Recargo mixto | A 1.000 + B 500, +3 % | 1.545 = 1.317,30 + 227,70 | concepto financiero no gravado: los 45 de recargo salen de la base de IVA (cifra a definir según C7) |
| E8 (C8) | Tarjeta +10 % | 1.000 | 1.100 = 909,09 + 190,91 | idem E7 |
| E9 (C9) | Cuota por transferencia | cuota 600 (capital 500 + interés 100), recargo 3 % = 18 | Caja 618, sin factura | facturar solo interés: 100; o recargo: 18; o nada |
| E10 (C10) | Punitorio | punitorio P | sin IVA ni comprobante | con IVA 21 %: neto P/1,21 e IVA P−neto |
| E11 (C11) | Interés crédito | venta 2.000, anticipo 500, 3×600; interés 300 | factura/IVA sobre 2.000 (IVA 347,11); el cliente paga 2.300 | interés gravado 21 %: IVA adicional 52,07 (300/1,21×0,21) |
| E12 (C12) | Crédito fiscal de compras | venta 1.000 (neto 826,45), costo 400 (con IVA) | ganancia 826,45−400 = 426,45 | costo sin IVA 330,58: ganancia 495,87 |
| E13 (C13) | Devolución en IVA | V1: 3×1.000 −5 %, IVA 494,63; se devuelve 1 (950, IVA 164,88) | reporte IVA 1.260,35 | netea por fecha de la devolución: 1.095,47 |
| E14 (C14) | Plazo | venta hace 29 d 23 h / 30 d 1 h | 29 d 23 h: permitida (`.Days` = 29); 30 d 1 h: `.Days` = 30, también permitida | plazo de 7 o 10 días corridos desde la compra; pasado el plazo solo NC |
| E15 (C15) | Período | venta 31/10 22:30 ART (01/11 01:30 UTC) | cae en noviembre | día argentino: octubre |

Nota E7: la columna «alternativo» queda sin cifra cerrada a propósito; depende de la respuesta C7 (alícuota o exención del concepto financiero) y no se inventa un criterio.

## 3. Alternativas por decisión (con efecto y riesgo)

**P1/P2 Facturación.** (a) Fuera del ERP, IVA del ERP informativo (estado actual): sin migración, la conciliación mensual es manual (`facturacion-fiscal-externa.md` §2). (b) Campo de vínculo al comprobante externo (opción 1 del doc): migración aditiva, auditoría, permiso propio. (c) Facturación electrónica propia: proyecto aparte (WSAA/WSFE, CAE, numeración). Riesgo común: el impreso interno puede circular como fiscal (mitigado por la leyenda).

**P3 Netear NC en Reporte de IVA.** (a) No netear (hoy; diferencia esperable en E13). (b) Netear por fecha de la devolución: cambia solo un reporte, no importes persistidos. (c) Netear por fecha de la NC fiscal externa: requiere el vínculo de P1(b).

**P4 Leyenda.** (a) Texto actual. (b) Redacción legal del contador: cambio en un solo lugar (`Helpers/ComprobanteInternoLeyenda.cs`).

**P5 Versionado del criterio.** (a) Sin versión (hoy): un cambio fiscal futuro reescribe en lectura el IVA de ventas históricas. (b) Versión por operación (columna nueva + fecha de corte): preserva históricos; requiere migración. (c) Congelar neto/IVA al facturar: más simple, pero distinto de lo que hoy se recalcula. Debe decidirse antes de cualquier fila de P6.

**P6 Orden sugerido de implementación (cuando haya autorización):** 1) P5, 2) C13/P3 (reporte), 3) C1–C3 (redondeo, afecta todas las facturas), 4) C4–C8, 5) C9–C11, 6) C12 (reportes de ganancia), 7) C14/C15.

**Redondeo (C1–C3).** (a) Truncar a pesos (hoy). (b) Redondeo comercial a pesos. (c) Centavos exactos, sin truncar. Cambia importes cobrados de toda venta: requiere fecha de corte y avisar a Caja.

**Recargo (C7/C8).** (a) Gravado a la alícuota de los ítems (hoy). (b) Línea propia al 21 %. (c) Concepto financiero no gravado/exento. Cambia el IVA del Reporte y de la factura, no el total cobrado.

**Descuento en efectivo (C4/C5).** (a) Comercial, reduce la base (hoy). (b) Financiero, la base queda y se informa el descuento aparte. Cambia neto/IVA, no el total.

**Cuotas (C9–C11).** (a) No facturar nada (hoy). (b) Facturar solo interés. (c) Facturar interés y recargo. (d) Incluir el interés en la factura de venta a crédito. Cualquiera exige P1(b) o (c).

## 4. Defectos técnicos revisados

Se ejecutó `dotnet test --filter Fiscal` (52/52 verdes) y se revisaron `RecargoMedioPagoIva`, `ServiciosVentaIva`, `RecargoPorLinea` y la sección de IVA de `ReporteService`. **No se encontró un defecto técnico comprobable (test que falle) que se pueda corregir sin una decisión fiscal.** Observaciones sin cambio de código (todas dependen de decisiones fiscales o comerciales):

1. `Services/DevolucionService.cs:850,860` usa `(UtcNow - FechaVenta).Days`, que trunca: una venta de 30 días y 23 horas sigue dentro del plazo de 30. Es consistente con «30 días», pero cambia de significado si C14 fija otro plazo.
2. `Services/ReporteService.cs:709,721` agrupa por `FechaVenta.Date` en UTC (C15): comportamiento caracterizado por test, no es un defecto hasta que el contador decida el criterio.
3. `Helpers/RecargoMedioPagoIva.cs` `Discriminar` absorbe residuos de hasta 0,05 por línea en el IVA: es una tolerancia deliberada; revisar junto con C3.

## 5. No validado

- Ninguna cifra «alternativa» fue validada por un contador; son cálculos a mano para ilustrar el efecto.
- Los E7, E10 y E12 dependen de datos del propietario/contador que no están en los docs.
- No se ejecutó la suite completa ni pruebas contra SQL Server; solo `Fiscal*` en SQLite en memoria.
