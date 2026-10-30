using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// H10 (decisiones D3 y D5 de docs/iva-recargos-verificacion.md): fuente única de las devoluciones que restan de las
    /// ventas en Reportes y Dashboard, y de la base neta (sin IVA) de la ganancia. Fórmulas y casos borde en
    /// docs/reportes-ganancia-devoluciones.md. No modifica registros: solo lee.
    /// </summary>
    internal static class VentasNetas
    {
        /// <summary>
        /// Devoluciones que cuentan: Aprobada (genera la nota de crédito o el reintegro) y Completada (stock/caja
        /// procesados). Pendiente, En revisión, Rechazada y Cancelada no restan. Un mismo registro no se cuenta dos veces:
        /// pasa de Aprobada a Completada sin duplicarse.
        /// </summary>
        public static readonly EstadoDevolucion[] EstadosDevolucionQueRestan =
        {
            EstadoDevolucion.Aprobada,
            EstadoDevolucion.Completada
        };

        /// <summary>Misma definición de venta comercial que los reportes y el Dashboard.</summary>
        public static readonly EstadoVenta[] EstadosVentaComerciales =
        {
            EstadoVenta.Confirmada,
            EstadoVenta.Facturada,
            EstadoVenta.Entregada
        };

        /// <summary>
        /// Una devolución valuada: <see cref="Bruto"/> = lo realmente cobrado por lo devuelto (con IVA, incluida su parte del
        /// recargo/descuento del medio de pago; es <c>Devolucion.TotalDevolucion</c>), <see cref="Neto"/> = esa misma
        /// porción sin IVA y <see cref="Costo"/> = costo de la venta original de las unidades devueltas.
        /// </summary>
        public sealed record DevolucionNeta(int DevolucionId, int VentaId, DateTime Fecha, decimal Bruto, decimal Neto, decimal Costo);

        /// <summary>
        /// Devoluciones que restan cuya FECHA DE DEVOLUCIÓN cae en [<paramref name="desde"/>, <paramref name="hastaExclusivo"/>),
        /// de ventas comerciales vigentes (una venta cancelada no suma, así que su devolución tampoco resta).
        /// <paramref name="ventaIds"/> restringe a las ventas que pasaron los filtros del reporte (cliente, vendedor, medio de pago…).
        /// </summary>
        public static async Task<List<DevolucionNeta>> ObtenerDevolucionesAsync(
            AppDbContext context, DateTime desde, DateTime? hastaExclusivo, IQueryable<int>? ventaIds = null)
        {
            var query = context.Devoluciones
                .AsNoTracking()
                .Where(d => !d.IsDeleted &&
                            EstadosDevolucionQueRestan.Contains(d.Estado) &&
                            d.FechaDevolucion >= desde &&
                            d.Venta != null && !d.Venta.IsDeleted &&
                            EstadosVentaComerciales.Contains(d.Venta.Estado));

            if (hastaExclusivo.HasValue)
                query = query.Where(d => d.FechaDevolucion < hastaExclusivo.Value);

            if (ventaIds != null)
                query = query.Where(d => ventaIds.Contains(d.VentaId));

            var devoluciones = await query
                .Select(d => new
                {
                    d.Id,
                    d.VentaId,
                    d.FechaDevolucion,
                    d.TotalDevolucion,
                    Detalles = d.Detalles
                        .Where(x => !x.IsDeleted)
                        .Select(x => new { x.ProductoId, x.Cantidad, x.Subtotal })
                        .ToList()
                })
                .ToListAsync();

            if (devoluciones.Count == 0)
                return new List<DevolucionNeta>();

            var ventaIdsConDevolucion = devoluciones.Select(d => d.VentaId).Distinct().ToList();
            var lineas = await context.VentaDetalles
                .AsNoTracking()
                .Where(vd => !vd.IsDeleted && ventaIdsConDevolucion.Contains(vd.VentaId))
                .Select(vd => new LineaVenta(
                    vd.VentaId, vd.ProductoId, vd.Cantidad, vd.PorcentajeIVA, vd.SubtotalFinal, vd.SubtotalFinalNeto,
                    vd.CostoTotalAlMomento, vd.CostoUnitarioAlMomento,
                    vd.Producto != null ? vd.Producto.PrecioCompra : 0m))
                .ToListAsync();
            var lineasPorVentaProducto = lineas
                .GroupBy(l => (l.VentaId, l.ProductoId))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Fallback si la devolución no trae detalles (no debería ocurrir): proporción neto/bruto de toda la venta.
            var ventas = await context.Ventas
                .AsNoTracking()
                .Where(v => ventaIdsConDevolucion.Contains(v.Id))
                .Select(v => new { v.Id, v.Total, v.IVA })
                .ToDictionaryAsync(v => v.Id);

            var resultado = new List<DevolucionNeta>(devoluciones.Count);
            foreach (var d in devoluciones)
            {
                decimal neto = 0m, costo = 0m;
                foreach (var det in d.Detalles)
                {
                    lineasPorVentaProducto.TryGetValue((d.VentaId, det.ProductoId), out var lineasProducto);
                    neto += NetoDeImporte(det.Subtotal, lineasProducto);
                    costo += CostoUnitarioPromedio(lineasProducto) * det.Cantidad;
                }

                if (d.Detalles.Count == 0 && ventas.TryGetValue(d.VentaId, out var venta) && venta.Total > 0m)
                    neto = Math.Round(d.TotalDevolucion * Math.Max(0m, venta.Total - Math.Max(0m, venta.IVA)) / venta.Total, 2, MidpointRounding.AwayFromZero);

                resultado.Add(new DevolucionNeta(d.Id, d.VentaId, d.FechaDevolucion, d.TotalDevolucion, neto, costo));
            }

            return resultado;
        }

        /// <summary>
        /// Ingreso neto sin IVA de una venta, sin el recargo de débito (que se informa aparte): <c>Total - IVA - neto del
        /// recargo de débito</c>. Incluye envío y armados (servicios) y el ajuste (recargo/descuento) de los demás medios de
        /// pago, igual que antes de H10 pero sin IVA. Con IVA no persistido (venta legacy) el neto es el total.
        /// </summary>
        public static decimal CalcularIngresoNeto(Venta venta, decimal recargoDebitoAplicado, IEnumerable<VentaDetalle> detallesValidos)
        {
            var neto = Math.Max(0m, venta.Total - Math.Max(0m, venta.IVA));
            if (recargoDebitoAplicado <= 0m)
                return neto;

            var netoRecargo = RecargoMedioPagoIva
                .Repartir(detallesValidos.Select(d => (d.PorcentajeIVA, d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal)), recargoDebitoAplicado)
                .Sum(p => p.Neto);
            if (netoRecargo <= 0m)
                netoRecargo = recargoDebitoAplicado;
            return neto - netoRecargo;
        }

        /// <summary>Neto (sin IVA) de una línea ya valuada: <c>SubtotalFinalNeto</c> o, sin snapshot, bruto / (1 + alícuota).</summary>
        public static decimal NetoDeLinea(VentaDetalle detalle)
        {
            if (detalle.SubtotalFinalNeto > 0m)
                return detalle.SubtotalFinalNeto;
            var bruto = detalle.SubtotalFinal > 0m ? detalle.SubtotalFinal : detalle.Subtotal;
            return SinIva(bruto, detalle.PorcentajeIVA);
        }

        public static decimal SinIva(decimal bruto, decimal porcentajeIva) =>
            porcentajeIva > 0m
                ? Math.Round(bruto / (1m + porcentajeIva / 100m), 2, MidpointRounding.AwayFromZero)
                : bruto;

        private sealed record LineaVenta(
            int VentaId, int ProductoId, int Cantidad, decimal PorcentajeIVA, decimal SubtotalFinal, decimal SubtotalFinalNeto,
            decimal CostoTotalAlMomento, decimal CostoUnitarioAlMomento, decimal PrecioCompra);

        /// <summary>Neto de un importe devuelto con la proporción neto/bruto de las líneas de ese producto en la venta original.</summary>
        private static decimal NetoDeImporte(decimal importe, List<LineaVenta>? lineasProducto)
        {
            if (lineasProducto == null || lineasProducto.Count == 0)
                return importe;

            var bruto = lineasProducto.Sum(l => l.SubtotalFinal);
            var neto = lineasProducto.Sum(l => l.SubtotalFinalNeto);
            if (bruto > 0m && neto > 0m)
                return Math.Round(importe * neto / bruto, 2, MidpointRounding.AwayFromZero);

            return SinIva(importe, lineasProducto[0].PorcentajeIVA);
        }

        private static decimal CostoUnitarioPromedio(List<LineaVenta>? lineasProducto)
        {
            if (lineasProducto == null || lineasProducto.Count == 0)
                return 0m;

            var unidades = lineasProducto.Sum(l => l.Cantidad);
            if (unidades <= 0)
                return 0m;

            var costo = lineasProducto.Sum(l => l.CostoTotalAlMomento > 0m
                ? l.CostoTotalAlMomento
                : (l.CostoUnitarioAlMomento > 0m ? l.CostoUnitarioAlMomento : l.PrecioCompra) * l.Cantidad);
            return costo / unidades;
        }
    }
}
