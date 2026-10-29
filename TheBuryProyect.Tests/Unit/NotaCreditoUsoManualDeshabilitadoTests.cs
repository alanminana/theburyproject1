using TheBuryProject.Controllers;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H5-G1: la nota de crédito se canjea solo como medio de pago dentro de la venta (NotaCreditoCanje).
/// El uso manual (descontar saldo fuera de una venta) ya no existe ni en el servicio ni en el controller.
/// El canje por venta y su restitución al cancelar se prueban en NotaCreditoCanjeTests (NC-01..NC-15).
/// </summary>
public class NotaCreditoUsoManualDeshabilitadoTests
{
    [Fact]
    public void Servicio_NoExponeCaminoDeEscrituraManual()
    {
        Assert.Null(typeof(IDevolucionService).GetMethod("UtilizarNotaCreditoAsync"));
    }

    [Fact]
    public void Controller_NoExponeAccionDeUsoManual()
    {
        Assert.Null(typeof(DevolucionController).GetMethod("UtilizarNotaCredito"));
    }
}
