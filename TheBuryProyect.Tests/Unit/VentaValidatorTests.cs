using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Validators;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para VentaValidator.
/// Función pura de dominio — no requiere base de datos ni infraestructura.
/// Cubre los 8 métodos de validación de estado y stock.
/// </summary>
public class VentaValidatorTests
{
    private readonly VentaValidator _validator = new();

    // =========================================================================
    // ValidarEstadoParaEdicion
    // =========================================================================

    [Theory]
    [InlineData(EstadoVenta.Cotizacion)]
    [InlineData(EstadoVenta.Presupuesto)]
    [InlineData(EstadoVenta.PendienteRequisitos)]
    [InlineData(EstadoVenta.PendienteFinanciacion)]
    public void ValidarEstadoParaEdicion_EstadoPermitido_NoLanzaExcepcion(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        var ex = Record.Exception(() => _validator.ValidarEstadoParaEdicion(venta));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Facturada)]
    [InlineData(EstadoVenta.Cancelada)]
    public void ValidarEstadoParaEdicion_EstadoNoPermitido_LanzaInvalidOperation(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarEstadoParaEdicion(venta));
    }

    // =========================================================================
    // ValidarEstadoParaEliminacion
    // =========================================================================

    [Theory]
    [InlineData(EstadoVenta.Cotizacion)]
    [InlineData(EstadoVenta.Presupuesto)]
    [InlineData(EstadoVenta.PendienteRequisitos)]
    [InlineData(EstadoVenta.PendienteFinanciacion)]
    public void ValidarEstadoParaEliminacion_EstadoPermitido_NoLanzaExcepcion(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        var ex = Record.Exception(() => _validator.ValidarEstadoParaEliminacion(venta));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Cancelada)]
    public void ValidarEstadoParaEliminacion_EstadoNoPermitido_LanzaInvalidOperation(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarEstadoParaEliminacion(venta));
    }

    // =========================================================================
    // ValidarEstadoParaConfirmacion
    // =========================================================================

    [Theory]
    [InlineData(EstadoVenta.Presupuesto)]
    [InlineData(EstadoVenta.PendienteRequisitos)]
    public void ValidarEstadoParaConfirmacion_EstadoPermitido_NoLanzaExcepcion(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        var ex = Record.Exception(() => _validator.ValidarEstadoParaConfirmacion(venta));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(EstadoVenta.Cotizacion)]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Cancelada)]
    public void ValidarEstadoParaConfirmacion_EstadoNoPermitido_LanzaInvalidOperation(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarEstadoParaConfirmacion(venta));
    }

    // =========================================================================
    // ValidarEstadoParaFacturacion
    // =========================================================================

    [Fact]
    public void ValidarEstadoParaFacturacion_Confirmada_NoLanzaExcepcion()
    {
        var venta = new Venta { Estado = EstadoVenta.Confirmada };
        var ex = Record.Exception(() => _validator.ValidarEstadoParaFacturacion(venta));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(EstadoVenta.Cotizacion)]
    [InlineData(EstadoVenta.Presupuesto)]
    [InlineData(EstadoVenta.Cancelada)]
    public void ValidarEstadoParaFacturacion_NoConfirmada_LanzaInvalidOperation(EstadoVenta estado)
    {
        var venta = new Venta { Estado = estado };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarEstadoParaFacturacion(venta));
    }

    // =========================================================================
    // ValidarAutorizacion
    // =========================================================================

    [Fact]
    public void ValidarAutorizacion_NoRequiereAutorizacion_NoLanzaExcepcion()
    {
        var venta = new Venta
        {
            RequiereAutorizacion = false,
            EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion
        };
        var ex = Record.Exception(() => _validator.ValidarAutorizacion(venta));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidarAutorizacion_RequiereYEstaAutorizada_NoLanzaExcepcion()
    {
        var venta = new Venta
        {
            RequiereAutorizacion = true,
            EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada
        };
        var ex = Record.Exception(() => _validator.ValidarAutorizacion(venta));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidarAutorizacion_RequiereYNoPendiente_LanzaInvalidOperation()
    {
        var venta = new Venta
        {
            RequiereAutorizacion = true,
            EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion
        };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarAutorizacion(venta));
    }

    // =========================================================================
    // ValidarStock
    // =========================================================================

    [Fact]
    public void ValidarStock_StockSuficiente_NoLanzaExcepcion()
    {
        var producto = new Producto { Nombre = "Prod", StockActual = 10m };
        var venta = new Venta
        {
            Detalles = new List<VentaDetalle>
            {
                new() { Producto = producto, Cantidad = 5 }
            }
        };
        var ex = Record.Exception(() => _validator.ValidarStock(venta));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidarStock_StockInsuficiente_LanzaInvalidOperation()
    {
        var producto = new Producto { Nombre = "Prod", StockActual = 3m };
        var venta = new Venta
        {
            Detalles = new List<VentaDetalle>
            {
                new() { Producto = producto, Cantidad = 5 }
            }
        };
        var ex = Assert.Throws<InvalidOperationException>(() => _validator.ValidarStock(venta));
        Assert.Contains("Stock insuficiente", ex.Message);
    }

    [Fact]
    public void ValidarStock_DetalleEliminado_NoSeValida()
    {
        var producto = new Producto { Nombre = "Prod", StockActual = 1m };
        var venta = new Venta
        {
            Detalles = new List<VentaDetalle>
            {
                new() { Producto = producto, Cantidad = 5, IsDeleted = true }
            }
        };
        var ex = Record.Exception(() => _validator.ValidarStock(venta));
        Assert.Null(ex);
    }

    // =========================================================================
    // ValidarNoEstaCancelada
    // =========================================================================

    [Fact]
    public void ValidarNoEstaCancelada_Cancelada_LanzaInvalidOperation()
    {
        var venta = new Venta { Estado = EstadoVenta.Cancelada };
        Assert.Throws<InvalidOperationException>(() => _validator.ValidarNoEstaCancelada(venta));
    }

    [Fact]
    public void ValidarNoEstaCancelada_NoCancelada_NoLanzaExcepcion()
    {
        var venta = new Venta { Estado = EstadoVenta.Confirmada };
        var ex = Record.Exception(() => _validator.ValidarNoEstaCancelada(venta));
        Assert.Null(ex);
    }

    // =========================================================================
    // ValidarEstadoAutorizacion
    // =========================================================================

    [Fact]
    public void ValidarEstadoAutorizacion_EstadoCorrecto_NoLanzaExcepcion()
    {
        var venta = new Venta { EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada };
        var ex = Record.Exception(() =>
            _validator.ValidarEstadoAutorizacion(venta, EstadoAutorizacionVenta.Autorizada));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidarEstadoAutorizacion_EstadoDistinto_LanzaInvalidOperation()
    {
        var venta = new Venta { EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion };
        Assert.Throws<InvalidOperationException>(() =>
            _validator.ValidarEstadoAutorizacion(venta, EstadoAutorizacionVenta.Autorizada));
    }
}
