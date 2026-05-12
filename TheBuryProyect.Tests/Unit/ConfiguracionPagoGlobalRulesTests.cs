using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public class ConfiguracionPagoGlobalRulesTests
{
    [Fact]
    public void Calcular_SinAjusteUnPago_MantieneTotal()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            PorcentajeAjuste = 0m,
            CantidadCuotas = 1
        });

        Assert.True(resultado.EsValido);
        Assert.Equal(0m, resultado.MontoAjuste);
        Assert.Equal(100_000m, resultado.TotalFinal);
        Assert.Equal(100_000m, resultado.ValorCuota);
        Assert.False(resultado.EsPagoEnCuotas);
    }

    [Fact]
    public void Calcular_RecargoDiezPorCiento_AumentaTotal()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            PorcentajeAjuste = 10m
        });

        Assert.True(resultado.EsValido);
        Assert.Equal(10_000m, resultado.MontoAjuste);
        Assert.Equal(110_000m, resultado.TotalFinal);
    }

    [Fact]
    public void Calcular_DescuentoDiezPorCiento_ReduceTotal()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            PorcentajeAjuste = -10m
        });

        Assert.True(resultado.EsValido);
        Assert.Equal(-10_000m, resultado.MontoAjuste);
        Assert.Equal(90_000m, resultado.TotalFinal);
    }

    [Fact]
    public void Calcular_SeisCuotasConRecargo_CalculaValorCuota()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            PorcentajeAjuste = 10m,
            CantidadCuotas = 6
        });

        Assert.True(resultado.EsValido);
        Assert.Equal(110_000m, resultado.TotalFinal);
        Assert.Equal(18_333.33m, resultado.ValorCuota);
        Assert.True(resultado.EsPagoEnCuotas);
    }

    [Fact]
    public void Calcular_CuotasCero_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            CantidadCuotas = 0
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.CuotasInvalidas, resultado.Estado);
    }

    [Fact]
    public void Calcular_BaseNegativa_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = -1m
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.BaseNegativa, resultado.Estado);
    }

    [Fact]
    public void Calcular_DescuentoMayorAlCienPorCiento_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            PorcentajeAjuste = -101m
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.DescuentoMayorAlTotal, resultado.Estado);
    }

    [Fact]
    public void Calcular_PlanInactivo_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(100_000m, new PlanPagoGlobalDto
        {
            Activo = false,
            CantidadCuotas = 3,
            AjustePorcentaje = 10m
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.PlanInactivo, resultado.Estado);
    }

    [Fact]
    public void Calcular_TarjetaInactiva_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            TarjetaActiva = false
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.TarjetaInactiva, resultado.Estado);
    }

    [Fact]
    public void Calcular_MedioInactivo_RetornaInvalido()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100_000m,
            MedioActivo = false
        });

        Assert.False(resultado.EsValido);
        Assert.Equal(EstadoValidacionPagoGlobal.MedioInactivo, resultado.Estado);
    }

    [Fact]
    public void Calcular_RedondeaAjusteADosDecimales()
    {
        var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = 100.005m,
            PorcentajeAjuste = 10m,
            CantidadCuotas = 1
        });

        Assert.True(resultado.EsValido);
        Assert.Equal(10.00m, resultado.MontoAjuste);
        Assert.Equal(110.01m, resultado.TotalFinal);
        Assert.Equal(110.01m, resultado.ValorCuota);
    }
}
