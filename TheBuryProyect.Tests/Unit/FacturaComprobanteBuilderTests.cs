using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Unit;

public class FacturaComprobanteBuilderTests
{
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
}
