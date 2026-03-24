using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para las reglas de negocio puras de DevolucionService:
/// DeterminarAccionRecomendada, PermiteImpactoCaja, ConstruirObservacionesInternas.
/// No requiere DB ni infraestructura.
/// </summary>
public class DevolucionServiceBusinessRulesTests
{
    private readonly DevolucionService _sut;

    public DevolucionServiceBusinessRulesTests()
    {
        // DevolucionService requiere dependencias para operaciones de DB,
        // pero los métodos de reglas de negocio son puros y no las usan.
        _sut = new DevolucionService(null!, null!, null!, NullLogger<TheBuryProject.Services.DevolucionService>.Instance, null);
    }

    // ---------------------------------------------------------------
    // DeterminarAccionRecomendada
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(EstadoProductoDevuelto.Nuevo, AccionProducto.ReintegrarStock)]
    [InlineData(EstadoProductoDevuelto.NuevoSellado, AccionProducto.ReintegrarStock)]
    [InlineData(EstadoProductoDevuelto.UsadoBuenEstado, AccionProducto.ReintegrarStock)]
    [InlineData(EstadoProductoDevuelto.AbiertoSinUso, AccionProducto.Cuarentena)]
    [InlineData(EstadoProductoDevuelto.UsadoConDetalles, AccionProducto.Cuarentena)]
    [InlineData(EstadoProductoDevuelto.Marcado, AccionProducto.Cuarentena)]
    [InlineData(EstadoProductoDevuelto.Incompleto, AccionProducto.Cuarentena)]
    [InlineData(EstadoProductoDevuelto.Defectuoso, AccionProducto.DevolverProveedor)]
    [InlineData(EstadoProductoDevuelto.Danado, AccionProducto.Descarte)]
    public void DeterminarAccionRecomendada_DevuelveAccionCorrecta(
        EstadoProductoDevuelto estado, AccionProducto esperada)
    {
        var resultado = _sut.DeterminarAccionRecomendada(estado);
        Assert.Equal(esperada, resultado);
    }

    [Fact]
    public void DeterminarAccionRecomendada_EstadoDesconocido_DevuelveCuarentena()
    {
        var resultado = _sut.DeterminarAccionRecomendada((EstadoProductoDevuelto)999);
        Assert.Equal(AccionProducto.Cuarentena, resultado);
    }

    // ---------------------------------------------------------------
    // PermiteImpactoCaja
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(TipoPago.Efectivo, true)]
    [InlineData(TipoPago.Transferencia, true)]
    [InlineData(TipoPago.TarjetaDebito, true)]
    [InlineData(TipoPago.TarjetaCredito, true)]
    [InlineData(TipoPago.Cheque, true)]
    [InlineData(TipoPago.MercadoPago, true)]
    [InlineData(TipoPago.Tarjeta, true)]
    [InlineData(TipoPago.CreditoPersonal, false)]
    [InlineData(TipoPago.CuentaCorriente, false)]
    public void PermiteImpactoCaja_DevuelveValorCorrecto(TipoPago tipoPago, bool esperado)
    {
        var resultado = _sut.PermiteImpactoCaja(tipoPago);
        Assert.Equal(esperado, resultado);
    }

    // ---------------------------------------------------------------
    // ConstruirObservacionesInternas
    // ---------------------------------------------------------------

    [Fact]
    public void ConstruirObservacionesInternas_Reembolso_ConEgresoCaja()
    {
        var resultado = _sut.ConstruirObservacionesInternas(
            TipoResolucionDevolucion.ReembolsoDinero,
            registrarEgresoCaja: true,
            TipoPago.Efectivo);

        Assert.Contains("Resolución solicitada:", resultado);
        Assert.Contains("Pago original: Efectivo", resultado);
        Assert.Contains("Registrar egreso en caja al completar", resultado);
    }

    [Fact]
    public void ConstruirObservacionesInternas_Reembolso_SinEgresoCaja()
    {
        var resultado = _sut.ConstruirObservacionesInternas(
            TipoResolucionDevolucion.ReembolsoDinero,
            registrarEgresoCaja: false,
            TipoPago.CreditoPersonal);

        Assert.Contains("No registra egreso automático", resultado);
        Assert.Contains("Crédito Personal", resultado);
    }

    [Fact]
    public void ConstruirObservacionesInternas_Cambio_NoGeneraMovimientoCaja()
    {
        var resultado = _sut.ConstruirObservacionesInternas(
            TipoResolucionDevolucion.CambioMismoProducto,
            registrarEgresoCaja: false,
            TipoPago.Transferencia);

        Assert.Contains("No genera movimiento automático en caja", resultado);
        Assert.Contains("Pago original: Transferencia", resultado);
    }

    [Fact]
    public void ConstruirObservacionesInternas_NotaCredito_NoGeneraMovimientoCaja()
    {
        var resultado = _sut.ConstruirObservacionesInternas(
            TipoResolucionDevolucion.NotaCredito,
            registrarEgresoCaja: false,
            TipoPago.TarjetaDebito);

        Assert.Contains("No genera movimiento automático en caja", resultado);
    }
}
