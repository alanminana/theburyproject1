using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>H-12: el ticket promedio por cliente del reporte de ventas descuenta lo devuelto de sus ventas.</summary>
public class ReporteClientesTopTicketNetoTests
{
    private static Venta V(int id, Cliente c, decimal total) =>
        new() { Id = id, ClienteId = c.Id, Cliente = c, Total = total };

    [Fact]
    public void TicketPorCliente_DescuentaLoDevueltoDeSusVentas()
    {
        var a = new Cliente { Id = 1, Nombre = "Ana", Apellido = "A" };
        var b = new Cliente { Id = 2, Nombre = "Beto", Apellido = "B" };
        var ventas = new[] { V(10, a, 1000m), V(11, a, 3000m), V(20, b, 2000m) };
        var devoluciones = new List<VentasNetas.DevolucionNeta>
        {
            new(1, 11, DateTime.UtcNow, Bruto: 1000m, Neto: 826m, Costo: 400m)
        };

        var top = ReporteService.ObtenerClientesTop(ventas, devoluciones);

        var ana = top.Single(c => c.ClienteId == 1);
        Assert.Equal(4000m, ana.MontoTotal);                 // ranking por lo facturado
        Assert.Equal((4000m - 1000m) / 2m, ana.TicketPromedio); // 1.500: neto de devoluciones, no 2.000
        Assert.Equal(2000m, top.Single(c => c.ClienteId == 2).TicketPromedio); // sin devoluciones, igual
    }
}
