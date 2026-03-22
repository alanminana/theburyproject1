using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para FinancialCalculationService.
/// Todas las funciones son puras (sin dependencias).
/// </summary>
public class FinancialCalculationServiceTests
{
    private readonly FinancialCalculationService _sut = new();

    // ---------------------------------------------------------------
    // CalcularCuotaSistemaFrances
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCuotaFrances_TasaCero_DivideMontoEnCuotas()
    {
        var resultado = _sut.CalcularCuotaSistemaFrances(12000m, 0m, 12);
        Assert.Equal(1000m, resultado);
    }

    [Fact]
    public void CalcularCuotaFrances_ConTasa_CalculaCorrectamente()
    {
        // 10.000 a 5% mensual en 12 cuotas → cuota ≈ 1128.25
        var resultado = _sut.CalcularCuotaSistemaFrances(10000m, 0.05m, 12);
        Assert.InRange(resultado, 1128m, 1129m);
    }

    [Fact]
    public void CalcularCuotaFrances_UnaCuota_DevuelveMontoPlusInteres()
    {
        var resultado = _sut.CalcularCuotaSistemaFrances(1000m, 0.10m, 1);
        Assert.Equal(1100m, resultado);
    }

    [Fact]
    public void CalcularCuotaFrances_MontoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCuotaSistemaFrances(-100m, 0.05m, 12));
    }

    [Fact]
    public void CalcularCuotaFrances_CuotasCero_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCuotaSistemaFrances(1000m, 0.05m, 0));
    }

    // ---------------------------------------------------------------
    // CalcularTotalConInteres
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularTotalConInteres_TasaCero_DevuelveMonto()
    {
        var resultado = _sut.CalcularTotalConInteres(5000m, 0m, 6);
        Assert.Equal(5000m, resultado);
    }

    [Fact]
    public void CalcularTotalConInteres_ConTasa_MayorQueMonto()
    {
        var resultado = _sut.CalcularTotalConInteres(10000m, 0.05m, 12);
        Assert.True(resultado > 10000m);
    }

    // ---------------------------------------------------------------
    // CalcularInteresTotal
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularInteresTotal_TasaCero_DevuelveCero()
    {
        var resultado = _sut.CalcularInteresTotal(5000m, 0m, 6);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularInteresTotal_ConTasa_DevuelveDiferenciaPositiva()
    {
        var resultado = _sut.CalcularInteresTotal(10000m, 0.05m, 12);
        Assert.True(resultado > 0m);
    }

    // ---------------------------------------------------------------
    // CalcularCFTEA
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCFTEA_CuotasCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEA(12000m, 10000m, 0);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEA_MontoInicialCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEA(12000m, 0m, 12);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEA_ConDatos_DevuelvePositivo()
    {
        var resultado = _sut.CalcularCFTEA(13539m, 10000m, 12);
        Assert.True(resultado > 0m);
    }

    // ---------------------------------------------------------------
    // CalcularCFTEADesdeTasa
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEADesdeTasa(0m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaNegativa_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCFTEADesdeTasa(-0.05m));
    }

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaPositiva_DevuelveAnualizado()
    {
        // 5% mensual → CFTEA ≈ 79.59%
        var resultado = _sut.CalcularCFTEADesdeTasa(0.05m);
        Assert.InRange(resultado, 79m, 80m);
    }

    // ---------------------------------------------------------------
    // ComputePmt
    // ---------------------------------------------------------------

    [Fact]
    public void ComputePmt_TasaCero_DivideRedondeado()
    {
        var resultado = _sut.ComputePmt(0m, 3, 1000m);
        Assert.Equal(333.33m, resultado);
    }

    [Fact]
    public void ComputePmt_MontoCero_DevuelveCero()
    {
        var resultado = _sut.ComputePmt(0.05m, 12, 0m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void ComputePmt_MontoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(0.05m, 12, -100m));
    }

    [Fact]
    public void ComputePmt_TasaNegativa_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(-0.01m, 12, 1000m));
    }

    [Fact]
    public void ComputePmt_CuotasMenorAUno_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(0.05m, 0, 1000m));
    }

    [Fact]
    public void ComputePmt_ConTasa_DevuelveRedondeadoA2Decimales()
    {
        var resultado = _sut.ComputePmt(0.05m, 12, 10000m);
        Assert.Equal(Math.Round(resultado, 2), resultado);
    }

    // ---------------------------------------------------------------
    // ComputeFinancedAmount
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeFinancedAmount_SinAnticipo_DevuelveTotal()
    {
        var resultado = _sut.ComputeFinancedAmount(10000m, 0m);
        Assert.Equal(10000m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_ConAnticipo_RestaDiferencia()
    {
        var resultado = _sut.ComputeFinancedAmount(10000m, 3000m);
        Assert.Equal(7000m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoIgualTotal_DevuelveCero()
    {
        var resultado = _sut.ComputeFinancedAmount(5000m, 5000m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_TotalNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(-100m, 0m));
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(1000m, -100m));
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoMayorQueTotal_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(1000m, 2000m));
    }
}
