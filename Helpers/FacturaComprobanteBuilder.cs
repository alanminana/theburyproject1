using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers
{
    public static class FacturaComprobanteBuilder
    {
        public static FacturaComprobanteViewModel Build(Factura factura)
        {
            ArgumentNullException.ThrowIfNull(factura);
            ArgumentNullException.ThrowIfNull(factura.Venta);

            var venta = factura.Venta;
            var detalles = venta.Detalles?
                .Where(d => !d.IsDeleted)
                .OrderBy(d => d.Id)
                .ToList() ?? new List<VentaDetalle>();

            var detalleViewModels = detalles.Select(ToDetalleViewModel).ToList();
            var lineas = detalleViewModels.Select(ToLineaViewModel).ToList();
            var recargoDebitoAplicado = ResolverRecargoDebitoAplicado(venta);
            var totales = BuildTotales(factura, venta, lineas, recargoDebitoAplicado);

            return new FacturaComprobanteViewModel
            {
                Factura = new FacturaComprobanteFacturaViewModel
                {
                    Id = factura.Id,
                    Numero = factura.Numero,
                    Tipo = factura.Tipo,
                    PuntoVenta = factura.PuntoVenta,
                    FechaEmision = factura.FechaEmision,
                    CAE = factura.CAE,
                    FechaVencimientoCAE = factura.FechaVencimientoCAE,
                    Anulada = factura.Anulada,
                    FechaAnulacion = factura.FechaAnulacion,
                    MotivoAnulacion = factura.MotivoAnulacion
                },
                Venta = new FacturaComprobanteVentaViewModel
                {
                    Id = venta.Id,
                    Numero = venta.Numero,
                    FechaVenta = venta.FechaVenta,
                    TipoPago = venta.TipoPago,
                    Estado = venta.Estado,
                    VendedorNombre = venta.VendedorNombre,
                    Observaciones = venta.Observaciones
                },
                Cliente = new FacturaComprobanteClienteViewModel
                {
                    Id = venta.ClienteId,
                    Nombre = venta.Cliente?.ToDisplayName() ?? string.Empty,
                    Documento = venta.Cliente?.NumeroDocumento ?? string.Empty,
                    Telefono = venta.Cliente?.Telefono,
                    Domicilio = venta.Cliente?.Domicilio
                },
                Lineas = lineas,
                ResumenAlicuotas = FacturaAlicuotaResumenBuilder.Build(detalleViewModels),
                Totales = totales
            };
        }

        private static VentaDetalleViewModel ToDetalleViewModel(VentaDetalle detalle)
        {
            return new VentaDetalleViewModel
            {
                Id = detalle.Id,
                VentaId = detalle.VentaId,
                ProductoId = detalle.ProductoId,
                ProductoCodigo = detalle.Producto?.Codigo ?? string.Empty,
                ProductoNombre = detalle.Producto?.Nombre ?? string.Empty,
                Cantidad = detalle.Cantidad,
                PrecioUnitario = detalle.PrecioUnitario,
                Descuento = detalle.Descuento,
                Subtotal = detalle.Subtotal,
                PorcentajeIVA = detalle.PorcentajeIVA,
                AlicuotaIVAId = detalle.AlicuotaIVAId,
                AlicuotaIVANombre = detalle.AlicuotaIVANombre,
                PrecioUnitarioNeto = detalle.PrecioUnitarioNeto,
                IVAUnitario = detalle.IVAUnitario,
                SubtotalNeto = detalle.SubtotalNeto,
                SubtotalIVA = detalle.SubtotalIVA,
                DescuentoGeneralProrrateado = detalle.DescuentoGeneralProrrateado,
                SubtotalFinalNeto = detalle.SubtotalFinalNeto,
                SubtotalFinalIVA = detalle.SubtotalFinalIVA,
                SubtotalFinal = detalle.SubtotalFinal,
                CostoUnitarioAlMomento = detalle.CostoUnitarioAlMomento,
                CostoTotalAlMomento = detalle.CostoTotalAlMomento,
                Observaciones = detalle.Observaciones
            };
        }

        private static FacturaComprobanteLineaViewModel ToLineaViewModel(VentaDetalleViewModel detalle)
        {
            var (subtotalNeto, iva, total) = ResolverImportesFinales(detalle);
            var alicuotaNombre = string.IsNullOrWhiteSpace(detalle.AlicuotaIVANombre)
                ? detalle.PorcentajeIVA > 0m ? $"IVA {detalle.PorcentajeIVA:0.##}%" : "Sin IVA"
                : detalle.AlicuotaIVANombre;

            return new FacturaComprobanteLineaViewModel
            {
                ProductoCodigo = detalle.ProductoCodigo ?? string.Empty,
                ProductoNombre = detalle.ProductoNombre ?? string.Empty,
                Cantidad = detalle.Cantidad,
                PrecioUnitario = detalle.PrecioUnitario,
                Descuento = detalle.Descuento + detalle.DescuentoGeneralProrrateado,
                PorcentajeIVA = detalle.PorcentajeIVA,
                AlicuotaIVANombre = alicuotaNombre,
                SubtotalNeto = subtotalNeto,
                IVA = iva,
                Total = total
            };
        }

        private static (decimal SubtotalNeto, decimal IVA, decimal Total) ResolverImportesFinales(VentaDetalleViewModel detalle)
        {
            var usaSnapshotsFinales = detalle.SubtotalFinal != 0m
                || detalle.SubtotalFinalNeto != 0m
                || detalle.SubtotalFinalIVA != 0m
                || detalle.DescuentoGeneralProrrateado != 0m;

            if (usaSnapshotsFinales)
            {
                return (detalle.SubtotalFinalNeto, detalle.SubtotalFinalIVA, detalle.SubtotalFinal);
            }

            var usaSnapshotsIva = detalle.SubtotalNeto != 0m
                || detalle.SubtotalIVA != 0m;

            if (usaSnapshotsIva)
            {
                return (detalle.SubtotalNeto, detalle.SubtotalIVA, detalle.Subtotal);
            }

            return (detalle.Subtotal, 0m, detalle.Subtotal);
        }

        private static FacturaComprobanteTotalesViewModel BuildTotales(
            Factura factura,
            Venta venta,
            IReadOnlyCollection<FacturaComprobanteLineaViewModel> lineas,
            decimal recargoDebitoAplicado)
        {
            var totalProductos = lineas.Sum(l => l.Total);

            if (lineas.Count > 0)
            {
                // El cargo financiero de debito no se prorratea ni integra IVA.
                // Si productos + recargo difiere por redondeo, el total persistido de Factura/Venta es la autoridad.
                return new FacturaComprobanteTotalesViewModel
                {
                    SubtotalNeto = lineas.Sum(l => l.SubtotalNeto),
                    IVA = lineas.Sum(l => l.IVA),
                    TotalProductos = totalProductos,
                    RecargoDebitoAplicado = recargoDebitoAplicado,
                    Total = ResolverTotalPersistido(factura, venta, totalProductos + recargoDebitoAplicado)
                };
            }

            if (factura.Subtotal != 0m || factura.IVA != 0m || factura.Total != 0m)
            {
                return new FacturaComprobanteTotalesViewModel
                {
                    SubtotalNeto = factura.Subtotal,
                    IVA = factura.IVA,
                    TotalProductos = Math.Max(0m, factura.Total - recargoDebitoAplicado),
                    RecargoDebitoAplicado = recargoDebitoAplicado,
                    Total = factura.Total
                };
            }

            return new FacturaComprobanteTotalesViewModel
            {
                SubtotalNeto = venta.Subtotal,
                IVA = venta.IVA,
                TotalProductos = Math.Max(0m, venta.Total - recargoDebitoAplicado),
                RecargoDebitoAplicado = recargoDebitoAplicado,
                Total = venta.Total
            };
        }

        private static decimal ResolverRecargoDebitoAplicado(Venta venta)
        {
            if (venta.DatosTarjeta?.TipoTarjeta != Models.Enums.TipoTarjeta.Debito)
            {
                return 0m;
            }

            var recargoAplicado = venta.DatosTarjeta.RecargoAplicado.GetValueOrDefault();
            return recargoAplicado > 0m
                ? recargoAplicado
                : 0m;
        }

        private static decimal ResolverTotalPersistido(Factura factura, Venta venta, decimal fallback)
        {
            if (factura.Total != 0m)
            {
                return factura.Total;
            }

            if (venta.Total != 0m)
            {
                return venta.Total;
            }

            return fallback;
        }
    }
}
