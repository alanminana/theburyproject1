using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
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

            // Armados y envío (nuevo modelo) forman parte de Venta.Total: se facturan como líneas propias.
            var envioIncluido = venta.Envio is { IncluidoEnTotal: true, IsDeleted: false }
                ? VentaMontos.NormalizarImporteEnvio(venta.Envio.CostoEnvio)
                : 0m;
            lineas.AddRange(ToLineasServicios(detalleViewModels, venta.Envio, envioIncluido));

            var recargoDebitoAplicado = ResolverRecargoDebitoAplicado(venta);

            // Fase 16.6: cuando hay TipoPago por ítem, el ajuste global de DatosTarjeta queda subordinado.
            var hasPagoPorItem = detalleViewModels.Any(d => d.TipoPago.HasValue);
            // Efectivo/Transferencia no tienen DatosTarjeta: su descuento/recargo vive en la propia venta.
            var ajusteMedio = !hasPagoPorItem && venta.DatosTarjeta == null && venta.AjusteMedioPagoMonto.HasValue;
            var ajustePlanMonto = hasPagoPorItem
                ? null
                : venta.DatosTarjeta?.MontoAjustePagoAplicado ?? venta.DatosTarjeta?.MontoAjustePlanAplicado
                    ?? (ajusteMedio ? venta.AjusteMedioPagoMonto : null);
            var ajustePlanPct = hasPagoPorItem
                ? null
                : venta.DatosTarjeta?.PorcentajeAjustePagoAplicado ?? venta.DatosTarjeta?.PorcentajeAjustePlanAplicado
                    ?? (ajusteMedio ? venta.AjusteMedioPagoPorcentaje : null);
            var nombrePlanPagoSnapshot = hasPagoPorItem
                ? null
                : venta.DatosTarjeta?.NombrePlanPagoSnapshot
                    ?? (ajusteMedio ? ResolverLabelTipoPago(venta.TipoPago) : null);
            var cantidadCuotas = hasPagoPorItem ? null : venta.DatosTarjeta?.CantidadCuotas;
            var gruposPago = hasPagoPorItem
                ? BuildGruposPagoPorItem(detalleViewModels, venta.TipoPago)
                : new List<FacturaComprobanteGrupoPagoViewModel>();

            // H5-I: el recargo del medio de pago se discrimina con IVA (prorrateado entre alícuotas) para que Neto + IVA == Total.
            var resumen = FacturaAlicuotaResumenBuilder.Build(detalleViewModels, envioIncluido);
            var recargoIva = ResolverRecargoDiscriminado(factura, venta, lineas);

            var totales = BuildTotales(
                factura,
                venta,
                lineas,
                recargoDebitoAplicado,
                ajustePlanMonto,
                ajustePlanPct,
                nombrePlanPagoSnapshot,
                cantidadCuotas,
                recargoIva);

            return new FacturaComprobanteViewModel
            {
                Factura = new FacturaComprobanteFacturaViewModel
                {
                    Id = factura.Id,
                    Numero = factura.Numero,
                    Tipo = factura.Tipo,
                    PuntoVenta = factura.PuntoVenta,
                    FechaEmision = factura.FechaEmision,
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
                    Id = venta.ClienteId ?? 0,
                    Nombre = venta.Cliente?.ToDisplayName() ?? venta.NombreClienteLibre ?? string.Empty,
                    Documento = venta.Cliente?.NumeroDocumento ?? venta.DniClienteLibre ?? string.Empty,
                    Telefono = venta.Cliente?.Telefono ?? venta.TelefonoClienteLibre,
                    Domicilio = venta.Cliente?.Domicilio
                },
                Lineas = lineas,
                ResumenAlicuotas = AplicarRecargoAlResumen(resumen, recargoIva),
                Totales = totales,
                GruposPagoPorItem = gruposPago
            };
        }

        private static IEnumerable<FacturaComprobanteLineaViewModel> ToLineasServicios(
            IEnumerable<VentaDetalleViewModel> detalles,
            VentaEnvio? envio,
            decimal envioIncluido)
        {
            foreach (var d in detalles.Where(d => d.ArmadoSubtotal > 0m))
            {
                var (neto, iva) = ServiciosVentaIva.Separar(d.ArmadoSubtotal);
                yield return new FacturaComprobanteLineaViewModel
                {
                    ProductoCodigo = "ARMADO",
                    ProductoNombre = $"{d.ArmadoDisplay} — {d.ProductoNombre}",
                    Cantidad = d.Cantidad,
                    PrecioUnitario = d.ArmadoPrecioUnitario,
                    PorcentajeIVA = ServiciosVentaIva.Porcentaje,
                    AlicuotaIVANombre = ServiciosVentaIva.NombreAlicuota,
                    SubtotalNeto = neto,
                    IVA = iva,
                    Total = d.ArmadoSubtotal
                };
            }

            if (envioIncluido > 0m)
            {
                var (neto, iva) = ServiciosVentaIva.Separar(envioIncluido);
                yield return new FacturaComprobanteLineaViewModel
                {
                    ProductoCodigo = "ENVIO",
                    ProductoNombre = envio?.TipoEnvio?.NombreVisible() ?? "Envío",
                    Cantidad = 1,
                    PrecioUnitario = envioIncluido,
                    PorcentajeIVA = ServiciosVentaIva.Porcentaje,
                    AlicuotaIVANombre = ServiciosVentaIva.NombreAlicuota,
                    SubtotalNeto = neto,
                    IVA = iva,
                    Total = envioIncluido
                };
            }
        }

        private static VentaDetalleViewModel ToDetalleViewModel(VentaDetalle detalle)
        {
            return new VentaDetalleViewModel
            {
                Id = detalle.Id,
                VentaId = detalle.VentaId,
                ProductoId = detalle.ProductoId,
                // Identidad histórica del producto (snapshot → relación viva legacy → Producto #id). Micro-lote 5.
                ProductoCodigo = VentaDetalleProductoSnapshot.ResolverCodigo(detalle) ?? string.Empty,
                ProductoNombre = VentaDetalleProductoSnapshot.ResolverNombre(detalle),
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
                Observaciones = detalle.Observaciones,
                TipoPago = detalle.TipoPago,
                ProductoCondicionPagoPlanId = detalle.ProductoCondicionPagoPlanId,
                PorcentajeAjustePlanAplicado = detalle.PorcentajeAjustePlanAplicado,
                MontoAjustePlanAplicado = detalle.MontoAjustePlanAplicado,
                TipoArmado = detalle.TipoArmado,
                EntregaCajaCerrada = detalle.EntregaCajaCerrada,
                ArmadoPrecioUnitario = detalle.ArmadoPrecioUnitario,
                ArmadoSubtotal = detalle.ArmadoSubtotal
            };
        }

        private static List<FacturaComprobanteGrupoPagoViewModel> BuildGruposPagoPorItem(
            IReadOnlyCollection<VentaDetalleViewModel> detalles,
            TipoPago tipoPagoGlobal)
        {
            return detalles
                .Where(d => d.ProductoCondicionPagoPlanId.HasValue
                    && d.MontoAjustePlanAplicado.HasValue
                    && (d.TipoPago ?? tipoPagoGlobal) != TipoPago.CreditoPersonal)
                .GroupBy(d => new
                {
                    TipoPago = d.TipoPago ?? tipoPagoGlobal,
                    d.ProductoCondicionPagoPlanId,
                    d.PorcentajeAjustePlanAplicado
                })
                .Select(g =>
                {
                    var subtotal = g.Sum(d => d.SubtotalFinal);
                    var ajuste = g.Sum(d => d.MontoAjustePlanAplicado ?? 0m);
                    return new FacturaComprobanteGrupoPagoViewModel
                    {
                        TipoPagoLabel = ResolverLabelTipoPago(g.Key.TipoPago),
                        PorcentajeAjuste = g.Key.PorcentajeAjustePlanAplicado,
                        Subtotal = subtotal,
                        AjusteMonto = ajuste,
                        Total = subtotal + ajuste
                    };
                })
                .ToList();
        }

        private static string ResolverLabelTipoPago(TipoPago tipoPago) => tipoPago switch
        {
            TipoPago.Efectivo => "Efectivo",
            TipoPago.Transferencia => "Transferencia",
            TipoPago.TarjetaDebito => "Tarjeta Débito",
            TipoPago.TarjetaCredito => "Tarjeta Crédito",
            TipoPago.Cheque => "Cheque",
            TipoPago.MercadoPago => "Mercado Pago",
            TipoPago.CuentaCorriente => "Cuenta Corriente",
            TipoPago.NotaCredito => "Nota de crédito",
            _ => tipoPago.ToString()
        };

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
            decimal recargoDebitoAplicado,
            decimal? ajustePlanMonto,
            decimal? ajustePlanPct,
            string? nombrePlanPagoSnapshot,
            int? cantidadCuotas,
            RecargoDiscriminado recargoIva)
        {
            var totalProductos = lineas.Sum(l => l.Total);

            if (lineas.Count > 0)
            {
                // El cargo financiero de debito no se prorratea ni integra IVA.
                // Si productos + recargo difiere por redondeo, el total persistido de Factura/Venta es la autoridad.
                return new FacturaComprobanteTotalesViewModel
                {
                    SubtotalNeto = recargoIva.HayAjuste ? recargoIva.Neto : lineas.Sum(l => l.SubtotalNeto),
                    IVA = recargoIva.HayAjuste ? recargoIva.Iva : lineas.Sum(l => l.IVA),
                    RecargoIncluidoNeto = recargoIva.RecargoNeto,
                    RecargoIncluidoIVA = recargoIva.RecargoIva,
                    TotalProductos = totalProductos,
                    RecargoDebitoAplicado = recargoDebitoAplicado,
                    PorcentajeAjustePlanAplicado = ajustePlanPct,
                    MontoAjustePlanAplicado = ajustePlanMonto,
                    NombrePlanPagoSnapshot = nombrePlanPagoSnapshot,
                    CantidadCuotas = cantidadCuotas,
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
                    PorcentajeAjustePlanAplicado = ajustePlanPct,
                    MontoAjustePlanAplicado = ajustePlanMonto,
                    NombrePlanPagoSnapshot = nombrePlanPagoSnapshot,
                    CantidadCuotas = cantidadCuotas,
                    Total = factura.Total
                };
            }

            return new FacturaComprobanteTotalesViewModel
            {
                SubtotalNeto = venta.Subtotal,
                IVA = venta.IVA,
                TotalProductos = Math.Max(0m, venta.Total - recargoDebitoAplicado),
                RecargoDebitoAplicado = recargoDebitoAplicado,
                PorcentajeAjustePlanAplicado = ajustePlanPct,
                MontoAjustePlanAplicado = ajustePlanMonto,
                NombrePlanPagoSnapshot = nombrePlanPagoSnapshot,
                CantidadCuotas = cantidadCuotas,
                Total = venta.Total
            };
        }


        private readonly record struct RecargoDiscriminado(
            IReadOnlyList<RecargoMedioPagoIva.ParteAlicuota> Partes,
            decimal Truncado,
            // Neto/IVA de la suma de líneas + recargo, con el truncado a pesos absorbido (IVA primero), para que cierre contra Total.
            decimal Neto,
            decimal Iva)
        {
            // Hay ajuste del medio de pago o, al menos, centavos truncados a pesos que el total ya no incluye.
            public bool HayAjuste => Partes.Count > 0 || Truncado > 0m;
            public decimal RecargoNeto => Partes.Sum(p => p.Neto);
            public decimal RecargoIva => Partes.Sum(p => p.Iva);
        }

        private static RecargoDiscriminado ResolverRecargoDiscriminado(
            Factura factura,
            Venta venta,
            IReadOnlyCollection<FacturaComprobanteLineaViewModel> lineas)
        {
            var vacio = new RecargoDiscriminado(Array.Empty<RecargoMedioPagoIva.ParteAlicuota>(), 0m, 0m, 0m);
            if (lineas.Count == 0) return vacio;

            var total = factura.Total != 0m ? factura.Total : venta.Total;
            var discriminacion = RecargoMedioPagoIva.Discriminar(
                total,
                lineas.Select(l => (l.PorcentajeIVA, l.Total, l.SubtotalNeto, l.IVA)).ToList());
            // Sin ajuste del medio de pago ni centavos truncados el comprobante es la suma de sus líneas.
            if (discriminacion.Partes.Count == 0 && discriminacion.Truncado <= 0m) return vacio;

            return new RecargoDiscriminado(discriminacion.Partes, discriminacion.Truncado, discriminacion.Neto, discriminacion.Iva);
        }

        private static List<FacturaAlicuotaResumenViewModel> AplicarRecargoAlResumen(
            List<FacturaAlicuotaResumenViewModel> resumen,
            RecargoDiscriminado recargo)
        {
            foreach (var parte in recargo.Partes)
            {
                var item = resumen.FirstOrDefault(r => r.PorcentajeIVA == parte.Porcentaje);
                if (item == null)
                {
                    resumen.Add(new FacturaAlicuotaResumenViewModel
                    {
                        PorcentajeIVA = parte.Porcentaje,
                        AlicuotaIVANombre = parte.Porcentaje > 0m ? $"IVA {parte.Porcentaje:0.##}%" : "Sin IVA",
                        BaseImponible = parte.Neto,
                        IVA = parte.Iva,
                        Total = parte.Bruto
                    });
                    continue;
                }

                item.BaseImponible += parte.Neto;
                item.IVA += parte.Iva;
                item.Total += parte.Bruto;
            }

            if (recargo.Truncado > 0m && resumen.Count > 0)
            {
                // El truncado a pesos se absorbe en la alícuota de mayor IVA, igual que en los totales.
                var destino = resumen.OrderByDescending(r => r.IVA).First();
                var (neto, iva) = RecargoMedioPagoIva.AbsorberTruncado(destino.BaseImponible, destino.IVA, recargo.Truncado);
                destino.Total -= (destino.BaseImponible - neto) + (destino.IVA - iva);
                destino.BaseImponible = neto;
                destino.IVA = iva;
            }

            return resumen.OrderByDescending(r => r.PorcentajeIVA).ThenBy(r => r.AlicuotaIVANombre).ToList();
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
