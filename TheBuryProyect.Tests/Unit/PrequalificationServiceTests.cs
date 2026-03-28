using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Unit tests para PrequalificationService.Evaluate.
/// Cubren guard de cuota negativa, retorno Indeterminate sin ingreso,
/// flags de datos faltantes, política 30% (Red / Yellow / Green),
/// cálculo de ingreso efectivo con deuda declarada.
/// </summary>
public class PrequalificationServiceTests
{
    private readonly PrequalificationService _service = new();

    // -------------------------------------------------------------------------
    // Guard — cuota negativa
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_CuotaNegativa_LanzaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Evaluate(-1m, 10000m, null, 24));
    }

    // -------------------------------------------------------------------------
    // Sin ingreso declarado — retorna Indeterminate con flag
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_SinIngreso_RetornaIndeterminateConFlag()
    {
        var result = _service.Evaluate(1000m, null, null, null);

        Assert.Equal(PrequalificationStatus.Indeterminate, result.Status);
        Assert.Contains("falta_ingreso", result.Flags);
    }

    [Fact]
    public void Evaluate_SinIngreso_NoCumplePolitica()
    {
        var result = _service.Evaluate(1000m, null, null, null);

        Assert.Null(result.MeetsThirtyPercentPolicy);
    }

    // -------------------------------------------------------------------------
    // Sin antigüedad — flag agregado pero no bloquea si hay ingreso
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_SinAntiguedad_AgregaFlag()
    {
        // ingreso suficiente (cuota 1000 * 3.33 = 3330 ≤ 10000) pero sin antigüedad
        var result = _service.Evaluate(1000m, 10000m, null, null);

        Assert.Contains("falta_antiguedad", result.Flags);
    }

    [Fact]
    public void Evaluate_SinAntiguedad_IngresioSuficiente_RetornaYellow()
    {
        var result = _service.Evaluate(1000m, 10000m, null, null);

        Assert.Equal(PrequalificationStatus.Yellow, result.Status);
    }

    // -------------------------------------------------------------------------
    // Política 30% — no cumple → Red
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_IngresoInsuficiente_RetornaRed()
    {
        // cuota 1000, ingreso 2000 → efectivo 2000 < 1000 * 3.33 = 3330
        var result = _service.Evaluate(1000m, 2000m, null, 24);

        Assert.Equal(PrequalificationStatus.Red, result.Status);
        Assert.False(result.MeetsThirtyPercentPolicy);
    }

    [Fact]
    public void Evaluate_IngresoInsuficiente_RecomendacionIncrementarAnticipo()
    {
        var result = _service.Evaluate(1000m, 2000m, null, 24);

        Assert.Contains("anticipo", result.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Política 30% — cumple sin datos pendientes → Green
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_IngresioSuficiente_ConAntiguedad_RetornaGreen()
    {
        // cuota 1000, ingreso 10000 → 10000 >= 3330 → Green
        var result = _service.Evaluate(1000m, 10000m, null, 24);

        Assert.Equal(PrequalificationStatus.Green, result.Status);
        Assert.True(result.MeetsThirtyPercentPolicy);
    }

    [Fact]
    public void Evaluate_Green_RecomendacionOk30()
    {
        var result = _service.Evaluate(1000m, 10000m, null, 24);

        Assert.Equal("OK 30%", result.Recommendation);
    }

    // -------------------------------------------------------------------------
    // Deuda declarada reduce ingreso efectivo
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_ConDeuda_ReduceIngresoEfectivo_PuedeSerRed()
    {
        // ingreso 5000 - deuda 2500 = efectivo 2500 < 1000*3.33=3330 → Red
        var result = _service.Evaluate(1000m, 5000m, 2500m, 24);

        Assert.Equal(PrequalificationStatus.Red, result.Status);
        Assert.False(result.MeetsThirtyPercentPolicy);
    }

    [Fact]
    public void Evaluate_ConDeuda_IngresoEfectivoSuficiente_RetornaGreen()
    {
        // ingreso 10000 - deuda 1000 = efectivo 9000 >= 1000*3.33=3330 → Green
        var result = _service.Evaluate(1000m, 10000m, 1000m, 24);

        Assert.Equal(PrequalificationStatus.Green, result.Status);
        Assert.True(result.MeetsThirtyPercentPolicy);
    }

    // -------------------------------------------------------------------------
    // Borde exacto: ingreso efectivo == cuota * 3.33
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_IngresoEfectivoExactoLimite_CumplePolitica()
    {
        // cuota 100, ingreso 333 → 333 == 100 * 3.33 → cumple (>=)
        var result = _service.Evaluate(100m, 333m, null, 24);

        Assert.True(result.MeetsThirtyPercentPolicy);
        Assert.Equal(PrequalificationStatus.Green, result.Status);
    }

    [Fact]
    public void Evaluate_IngresoUnMenosDelLimite_NosCumplePolitica()
    {
        // cuota 100, ingreso 332 → 332 < 333 → no cumple
        var result = _service.Evaluate(100m, 332m, null, 24);

        Assert.False(result.MeetsThirtyPercentPolicy);
        Assert.Equal(PrequalificationStatus.Red, result.Status);
    }

    // -------------------------------------------------------------------------
    // Cuota cero — válida (no lanza) → todos los ingresos cumplen
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_CuotaCero_ConIngreso_RetornaGreen()
    {
        var result = _service.Evaluate(0m, 1000m, null, 24);

        Assert.Equal(PrequalificationStatus.Green, result.Status);
        Assert.True(result.MeetsThirtyPercentPolicy);
    }
}
