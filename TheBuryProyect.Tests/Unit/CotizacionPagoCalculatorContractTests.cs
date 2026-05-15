using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionPagoCalculatorContractTests
{
    [Fact]
    public void CotizacionRequest_PermiteClienteOpcionalYNombreLibre()
    {
        var request = new CotizacionSimulacionRequest
        {
            ClienteId = null,
            NombreClienteLibre = "Cliente mostrador",
            Productos =
            {
                new CotizacionProductoRequest
                {
                    ProductoId = 10,
                    Cantidad = 1
                }
            }
        };

        Assert.Null(request.ClienteId);
        Assert.Equal("Cliente mostrador", request.NombreClienteLibre);
        Assert.True(request.IncluirEfectivo);
        Assert.True(request.IncluirCreditoPersonal);
    }

    [Fact]
    public void CotizacionResultado_RepresentaOpcionDisponible()
    {
        var resultado = new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.Efectivo,
            NombreMedioPago = "Efectivo",
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes =
            {
                new CotizacionPlanPagoResultado
                {
                    Plan = "1 pago",
                    CantidadCuotas = 1,
                    Total = 100_000m,
                    ValorCuota = 100_000m,
                    Recomendado = true
                }
            }
        };

        Assert.True(resultado.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.Disponible, resultado.Estado);
        Assert.Single(resultado.Planes);
    }

    [Fact]
    public void CotizacionResultado_RepresentaOpcionNoDisponibleConMotivo()
    {
        var resultado = new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
            NombreMedioPago = "Credito personal",
            Disponible = false,
            Estado = CotizacionOpcionPagoEstado.BloqueadoPorProducto,
            MotivoNoDisponible = "Producto bloquea credito personal."
        };

        Assert.False(resultado.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.BloqueadoPorProducto, resultado.Estado);
        Assert.Contains("bloquea", resultado.MotivoNoDisponible);
    }

    [Fact]
    public void CotizacionResultado_RepresentaAdvertenciasGeneralesYPorProducto()
    {
        var resultado = new CotizacionSimulacionResultado
        {
            Exitoso = true,
            Advertencias = { "Credito personal requiere evaluacion." },
            Productos =
            {
                new CotizacionProductoResultado
                {
                    ProductoId = 7,
                    Codigo = "P-7",
                    Nombre = "Producto",
                    Cantidad = 2,
                    PrecioUnitario = 50_000m,
                    Subtotal = 100_000m,
                    TieneRestricciones = true,
                    Advertencias = { "Limita cuotas." }
                }
            }
        };

        Assert.Contains("requiere evaluacion", resultado.Advertencias[0]);
        Assert.True(resultado.Productos[0].TieneRestricciones);
        Assert.Contains("Limita", resultado.Productos[0].Advertencias[0]);
    }

    [Fact]
    public void CotizacionEnums_RepresentanMediosYEstadosEsperados()
    {
        var medios = Enum.GetValues<CotizacionMedioPagoTipo>();
        var estados = Enum.GetValues<CotizacionOpcionPagoEstado>();

        Assert.Contains(CotizacionMedioPagoTipo.Efectivo, medios);
        Assert.Contains(CotizacionMedioPagoTipo.Transferencia, medios);
        Assert.Contains(CotizacionMedioPagoTipo.TarjetaCredito, medios);
        Assert.Contains(CotizacionMedioPagoTipo.TarjetaDebito, medios);
        Assert.Contains(CotizacionMedioPagoTipo.MercadoPago, medios);
        Assert.Contains(CotizacionMedioPagoTipo.CreditoPersonal, medios);
        Assert.Contains(CotizacionOpcionPagoEstado.RequiereEvaluacion, estados);
        Assert.Contains(CotizacionOpcionPagoEstado.CuotaInactiva, estados);
    }

    [Fact]
    public void CotizacionCalculator_ImplementaContrato()
    {
        ICotizacionPagoCalculator calculator = new CotizacionPagoCalculator();

        Assert.IsType<CotizacionPagoCalculator>(calculator);
    }

    [Fact]
    public async Task CotizacionCalculator_SinProductos_DevuelveErrorControlado()
    {
        var calculator = new CotizacionPagoCalculator();

        var resultado = await calculator.SimularAsync(new CotizacionSimulacionRequest());

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("al menos un producto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CotizacionCalculator_ConProductoValido_NoRequiereCliente()
    {
        var calculator = new CotizacionPagoCalculator();

        var resultado = await calculator.SimularAsync(new CotizacionSimulacionRequest
        {
            ClienteId = null,
            Productos =
            {
                new CotizacionProductoRequest
                {
                    ProductoId = 1,
                    Cantidad = 1
                }
            }
        });

        Assert.True(resultado.Exitoso);
        Assert.Empty(resultado.Errores);
        Assert.Contains(resultado.Advertencias, a => a.Contains("V1A", StringComparison.OrdinalIgnoreCase));
    }
}
