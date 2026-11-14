# P-02: cobertura técnica del versionado fiscal (#298 / #304)

Fecha: 2026-11-14. Alcance: solo tests y documentación; no se cambió ningún importe, regla fiscal ni dato.
Complementa `docs/p02-versionado-fiscal-diseno.md` y `docs/p02-escenarios-y-alternativas.md`.

## Cobertura agregada

| Brecha | Test | Resultado |
|---|---|---|
| La conversión de cotización estampa `CriterioFiscalVersion` y `AlicuotaServiciosSnapshot`, y un cambio posterior de configuración no la altera | `CotizacionConversionServiceTests.Convertir_EstampaVersionFiscalYAlicuotaDeServicios_…` | OK (antes sin test) |
| La venta de Mercado Libre estampa ambos campos y un cambio posterior no la altera | `MercadoLibreOrderServiceTests.CrearVenta_OrdenPaga_EstampaVersionFiscalYAlicuota_…` | OK (antes sin test) |
| `VentasNetas` (ingreso neto, devoluciones netas) y el reporte de ventas no dependen de la alícuota configurada (con y sin snapshot) | `FiscalVentasNetasVersionP02Tests` (a) | OK |
| Versión fiscal no registrada en `VentasNetas`/devoluciones | `FiscalVentasNetasVersionP02Tests` (b) | Caracterizado, ver abajo |

Ya cubiertos antes (no duplicados): estampado en `VentaService.CreateAsync`, reporte de IVA y comprobante ante cambio de configuración
y ante versión inválida (`FiscalHistoricoConfigP02Tests`, `FiscalVersionadoP02Tests`).

## Comportamiento ante versión fiscal desconocida (caracterizado)

El registro no «adivina» otra versión: `CriterioFiscalRegistry.Resolver` lanza `CriterioFiscalDesconocidoException`.

- Reporte de IVA: la venta se excluye y se informa aparte (endurecido en #304).
- Comprobante: falla con la excepción tipada.
- `VentasNetas.CalcularIngresoNeto`, `VentasNetas.ObtenerDevolucionesAsync`: solo consultan el criterio al repartir el **recargo de
  débito**. Sin recargo de débito no fallan (usan total − IVA y los netos de línea). Con recargo de débito lanzan la excepción, y como
  el reporte de ventas y el Dashboard calculan todas las ventas del período en una sola pasada, **una única venta con versión
  corrupta y recargo de débito rompe el reporte completo** (a diferencia del reporte de IVA).
- La creación de una devolución (`DevolucionService`) también falla (fail-closed) para esa venta.

No se corrigió: excluir o avisar en `VentasNetas` cambiaría los totales de Reportes/Dashboard, es decir, define una regla (qué se
informa y qué no), no es un defecto técnico inequívoco. Además hoy es un estado inalcanzable por la UI: solo una escritura directa en
la base puede dejar una versión no registrada, y en producción solo existe la versión 1.

## Defectos técnicos inequívocos encontrados

Ninguno. Los tres puntos de alta (`VentaService`, `CotizacionConversionService`, `MercadoLibreOrderService`) estampan versión y
alícuota; las lecturas (`ReporteService`, `FacturaComprobanteBuilder`, `VentaController`, `DevolucionService`, `VentasNetas`) resuelven
por la versión y el snapshot de la operación.

## Requiere decisión del contador o propietario

1. **Ventas anteriores al snapshot (`AlicuotaServiciosSnapshot` null)**: se releen con la alícuota de configuración vigente (sigue
   siendo así, caracterizado en `FiscalHistoricoConfigP02Tests`). Opciones: backfill con 21 %, con la configuración vigente al
   migrar, o dejarlas. Requiere backfill → no aplicado.
2. **Versión fiscal desconocida en Reportes/Dashboard**: ¿fallar (hoy, con débito), o excluir la venta y mostrar un aviso como el
   reporte de IVA? Cambia qué importes aparecen en el reporte.
3. **Versiones fiscales nuevas** (alícuota de servicios, truncado a pesos, reparto del recargo): ninguna se implementa sin validación
   del contador; ver `docs/p02-escenarios-y-alternativas.md`.
4. **Mercado Libre**: la venta estampa una alícuota de servicios aunque ML no genera armados/envío; es inocuo hoy, pero si algún día
   ML incluyera servicios, la alícuota aplicable es una decisión fiscal.
