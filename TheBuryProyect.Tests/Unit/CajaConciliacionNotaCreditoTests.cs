using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>H4-E2: el canje de nota de crédito tiene su propio medio en la conciliación de caja (no se mezcla con crédito personal) y no es físico.</summary>
public class CajaConciliacionNotaCreditoTests
{
    [Fact]
    public void ResumenPorMedio_NotaCredito_TieneSuPropioMedioYNoImpactaCajaFisica()
    {
        var venta = new Venta
        {
            Id = 1, Numero = "V1", Estado = EstadoVenta.Facturada, TipoPago = TipoPago.NotaCredito, Total = 100m,
            FechaVenta = DateTime.UtcNow, Detalles = new List<VentaDetalle>()
        };
        var mov = new MovimientoCaja
        {
            Id = 1, VentaId = 1, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaNotaCredito,
            TipoPago = TipoPago.NotaCredito, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.NoAplica, Monto = 100m,
            FechaMovimiento = DateTime.UtcNow
        };
        var detalle = new DetallesAperturaViewModel
        {
            Apertura = new AperturaCaja { Id = 1, MontoInicial = 0m },
            Movimientos = new List<MovimientoCaja> { mov },
            VentasDelTurno = new List<Venta> { venta },
            ResumenRealPorMedioPago = new List<ResumenMedioPagoCajaViewModel>
            {
                new() { MedioPago = "Nota de crédito", TotalIngresos = 100m, CantidadMovimientos = 1 }
            }
        };

        var vm = CajaConciliacionBuilder.Build(detalle, null, puedeOperar: true);

        var fila = Assert.Single(vm.ResumenPorMedio);
        Assert.Equal("notacredito", fila.MedioKey);
        Assert.Equal("Nota de crédito", fila.MedioPago);
        Assert.False(fila.ImpactaCajaFisica);
        Assert.Equal(100m, fila.TotalCobrado);
        Assert.False(CajaConciliacionBuilder.EsMovimientoFisico(mov));
    }
}
