using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Unit;

public class FacturaComprobanteBuilderTests
{
    [Fact]
    public void Build_EfectivoConDescuentoDeMedio_ExplicaElAjusteEnLosTotales()
    {
        var factura = new Factura
        {
            Numero = "FC-EF", Tipo = TipoFactura.B, FechaEmision = new DateTime(2026, 10, 26), Total = 950m,
            Venta = new Venta
            {
                Numero = "V-EF", TipoPago = TipoPago.Efectivo, Total = 950m,
                AjusteMedioPagoPorcentaje = -5m, AjusteMedioPagoMonto = -50m,
                Cliente = new Cliente { Nombre = "Ana", Apellido = "C", NumeroDocumento = "1" },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "P1", Nombre = "P1" }, Cantidad = 1, PrecioUnitario = 1_000m,
                        Subtotal = 1_000m, SubtotalNeto = 826.45m, SubtotalIVA = 173.55m, PorcentajeIVA = 21m
                    }
                }
            }
        };
        factura.Venta.Facturas.Add(factura);

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(-50m, comprobante.Totales.MontoAjustePlanAplicado);
        Assert.Equal(-5m, comprobante.Totales.PorcentajeAjustePlanAplicado);
        Assert.Equal("Efectivo", comprobante.Totales.NombrePlanPagoSnapshot);
        Assert.Equal(1_000m, comprobante.Totales.TotalProductos);
        Assert.Equal(950m, comprobante.Totales.Total);
    }

    [Fact]
    public void Build_EfectivoSinAjusteDeMedio_NoInventaAjuste()
    {
        var factura = new Factura
        {
            Numero = "FC-EF2", Tipo = TipoFactura.B, FechaEmision = new DateTime(2026, 10, 26), Total = 1_000m,
            Venta = new Venta
            {
                Numero = "V-EF2", TipoPago = TipoPago.Efectivo, Total = 1_000m,
                Cliente = new Cliente { Nombre = "Ana", Apellido = "C", NumeroDocumento = "1" },
                Detalles = new List<VentaDetalle>
                {
                    new() { Producto = new Producto { Codigo = "P1", Nombre = "P1" }, Cantidad = 1, PrecioUnitario = 1_000m, Subtotal = 1_000m }
                }
            }
        };
        factura.Venta.Facturas.Add(factura);

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Null(comprobante.Totales.MontoAjustePlanAplicado);
    }

    [Fact]
    public void Build_ConDescuentoGeneralProrrateado_UsaSubtotalFinal()
    {
        var factura = new Factura
        {
            Numero = "FC-TEST",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 4, 30),
            Venta = new Venta
            {
                Numero = "V-TEST",
                Cliente = new Cliente
                {
                    Nombre = "Ana",
                    Apellido = "Cliente",
                    NumeroDocumento = "123",
                    Telefono = "111",
                    Domicilio = "Calle 1"
                },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "P1", Nombre = "Producto 1" },
                        Cantidad = 1,
                        PrecioUnitario = 121m,
                        Subtotal = 121m,
                        PorcentajeIVA = 21m,
                        AlicuotaIVANombre = "IVA 21%",
                        DescuentoGeneralProrrateado = 31m,
                        SubtotalFinalNeto = 74.38m,
                        SubtotalFinalIVA = 15.62m,
                        SubtotalFinal = 90m
                    },
                    new()
                    {
                        Producto = new Producto { Codigo = "P2", Nombre = "Producto 2" },
                        Cantidad = 1,
                        PrecioUnitario = 110.5m,
                        Subtotal = 110.5m,
                        PorcentajeIVA = 10.5m,
                        AlicuotaIVANombre = "IVA 10.5%",
                        SubtotalFinalNeto = 100m,
                        SubtotalFinalIVA = 10.5m,
                        SubtotalFinal = 110.5m
                    }
                }
            }
        };
        factura.Venta.Facturas.Add(factura);

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(174.38m, comprobante.Totales.SubtotalNeto);
        Assert.Equal(26.12m, comprobante.Totales.IVA);
        Assert.Equal(200.5m, comprobante.Totales.Total);
        Assert.Contains(comprobante.ResumenAlicuotas, r =>
            r.AlicuotaIVANombre == "IVA 21%" &&
            r.BaseImponible == 74.38m &&
            r.IVA == 15.62m &&
            r.Total == 90m);
    }

    [Fact]
    public void Build_DebitoConRecargo_UsaTotalPersistidoYDiscriminaElRecargoEnElResumen()
    {
        var factura = new Factura
        {
            Numero = "FC-REC",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 6),
            Total = 133.10m,
            Venta = new Venta
            {
                Numero = "V-REC",
                TipoPago = TipoPago.TarjetaDebito,
                Total = 133.10m,
                DatosTarjeta = new DatosTarjeta
                {
                    TipoTarjeta = TipoTarjeta.Debito,
                    NombreTarjeta = "Maestro",
                    RecargoAplicado = 12.10m
                },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "P1", Nombre = "Producto 1" },
                        Cantidad = 1,
                        PrecioUnitario = 121m,
                        Subtotal = 121m,
                        PorcentajeIVA = 21m,
                        AlicuotaIVANombre = "IVA 21%",
                        SubtotalFinalNeto = 100m,
                        SubtotalFinalIVA = 21m,
                        SubtotalFinal = 121m
                    }
                }
            }
        };

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(121m, comprobante.Totales.TotalProductos);
        Assert.Equal(12.10m, comprobante.Totales.RecargoDebitoAplicado);
        Assert.Equal(133.10m, comprobante.Totales.Total);
        // H5-I: el recargo se discrimina con IVA a la alícuota de los ítems: Neto + IVA == Total.
        var resumen = Assert.Single(comprobante.ResumenAlicuotas);
        Assert.Equal(133.10m, resumen.Total);
        Assert.Equal(133.10m, resumen.BaseImponible + resumen.IVA);
        Assert.Equal(133.10m, comprobante.Totales.SubtotalNeto + comprobante.Totales.IVA);
    }

    [Fact]
    public void Build_SinRecargo_NoExponeRecargo()
    {
        var factura = new Factura
        {
            Numero = "FC-SIN",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 6),
            Venta = new Venta
            {
                Numero = "V-SIN",
                TipoPago = TipoPago.Efectivo,
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "P1", Nombre = "Producto 1" },
                        Cantidad = 1,
                        PrecioUnitario = 121m,
                        Subtotal = 121m,
                        PorcentajeIVA = 21m,
                        AlicuotaIVANombre = "IVA 21%",
                        SubtotalFinalNeto = 100m,
                        SubtotalFinalIVA = 21m,
                        SubtotalFinal = 121m
                    }
                }
            }
        };

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(0m, comprobante.Totales.RecargoDebitoAplicado);
        Assert.Equal(121m, comprobante.Totales.Total);
    }

    [Fact]
    public void Build_PlanGlobal_PropagaSnapshotGlobalYTotalPersistido()
    {
        var factura = new Factura
        {
            Numero = "FC-GLOBAL",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 13),
            Total = 1100m,
            Venta = new Venta
            {
                Numero = "V-GLOBAL",
                TipoPago = TipoPago.MercadoPago,
                Total = 1100m,
                DatosTarjeta = new DatosTarjeta
                {
                    TipoTarjeta = TipoTarjeta.Credito,
                    NombreTarjeta = "Mercado Pago",
                    ConfiguracionPagoPlanId = 10,
                    CantidadCuotas = 3,
                    PorcentajeAjustePagoAplicado = 10m,
                    MontoAjustePagoAplicado = 100m,
                    NombrePlanPagoSnapshot = "MP 3 cuotas +10",
                    ProductoCondicionPagoPlanId = null
                },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "P1", Nombre = "Producto 1" },
                        Cantidad = 1,
                        PrecioUnitario = 1000m,
                        Subtotal = 1000m,
                        SubtotalFinal = 1000m
                    }
                }
            }
        };

        var comprobante = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(10m, comprobante.Totales.PorcentajeAjustePlanAplicado);
        Assert.Equal(100m, comprobante.Totales.MontoAjustePlanAplicado);
        Assert.Equal("MP 3 cuotas +10", comprobante.Totales.NombrePlanPagoSnapshot);
        Assert.Equal(3, comprobante.Totales.CantidadCuotas);
        Assert.Equal(1100m, comprobante.Totales.Total);
        Assert.Empty(comprobante.GruposPagoPorItem);
    }
}
