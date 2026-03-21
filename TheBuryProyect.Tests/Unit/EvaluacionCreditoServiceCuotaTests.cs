using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para EvaluacionCreditoService.CalcularRelacionCuotaIngreso
/// y la heurística de cuota estimada (RatioCuotaEstimada).
///
/// Documenta el contrato de las reglas de scoring de capacidad de pago:
/// - relación <= 0.25 (UmbralCuotaIngresoBajo) → excelente
/// - relación > 0.25 y <= RelacionCuotaIngresoMax (configurable) → aceptable
/// - relación > RelacionCuotaIngresoMax y <= 0.45 (UmbralCuotaIngresoAlto) → ajustada
/// - relación > 0.45 → insuficiente
///
/// CalcularRelacionCuotaIngreso es una función pura.
/// RatioCuotaEstimada = 0.10m es la heurística de scoring (no un cálculo financiero).
///
/// No requiere DB ni infraestructura.
/// </summary>
public class EvaluacionCreditoServiceCuotaTests
{
    // ---------------------------------------------------------------------------
    // CalcularRelacionCuotaIngreso — casos base
    // ---------------------------------------------------------------------------

    [Fact]
    public void Relacion_CuotaBajaSueldo_MenorQueUmbralBajo()
    {
        // cuota=200, sueldo=2000 → relación=0.10 < 0.25
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 200m, sueldo: 2_000m);

        Assert.True(relacion < 0.25m, $"Relación esperada < 0.25, obtenida: {relacion}");
        Assert.Equal(0.10m, relacion);
    }

    [Fact]
    public void Relacion_CuotaIntermedia_EntreUmbrales()
    {
        // cuota=700, sueldo=2000 → relación=0.35 (entre 0.25 y 0.45)
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 700m, sueldo: 2_000m);

        Assert.True(relacion > 0.25m && relacion < 0.45m,
            $"Relación esperada entre 0.25 y 0.45, obtenida: {relacion}");
        Assert.Equal(0.35m, relacion);
    }

    [Fact]
    public void Relacion_CuotaAltaSueldo_MayorQueUmbralAlto()
    {
        // cuota=1000, sueldo=2000 → relación=0.50 > 0.45
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 1_000m, sueldo: 2_000m);

        Assert.True(relacion > 0.45m, $"Relación esperada > 0.45, obtenida: {relacion}");
        Assert.Equal(0.5m, relacion);
    }

    // ---------------------------------------------------------------------------
    // CalcularRelacionCuotaIngreso — edge case: sueldo = 0
    // ---------------------------------------------------------------------------

    [Fact]
    public void Relacion_SueldoCero_DevuelveCeroSinExcepcion()
    {
        // No debe lanzar DivideByZeroException
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 500m, sueldo: 0m);

        Assert.Equal(0m, relacion);
    }

    // ---------------------------------------------------------------------------
    // Heurística RatioCuotaEstimada = 0.10m
    // Documenta que la cuota estimada es 10% del monto solicitado
    // ---------------------------------------------------------------------------

    [Fact]
    public void CuotaEstimada_Es10PorCientoDelMonto()
    {
        // RatioCuotaEstimada no es public, pero su efecto es observable:
        // cuotaEstimada = monto * 0.10 → para monto=10000, cuota=1000
        // relacion = 1000 / sueldo
        const decimal monto = 10_000m;
        const decimal sueldo = 40_000m;

        // cuotaEstimada = 10000 * 0.10 = 1000
        // relacion = 1000 / 40000 = 0.025
        var cuotaEsperada = monto * 0.10m;
        var relacionEsperada = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuotaEsperada, sueldo);

        Assert.Equal(0.025m, relacionEsperada);
    }

    // ---------------------------------------------------------------------------
    // Límite exacto en UmbralCuotaIngresoBajo (0.25)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Relacion_ExactamenteEnUmbralBajo_NoSuperaUmbral()
    {
        // relación == 0.25 → debe clasificar como <= 0.25 (Excelente en EvaluarIngresos)
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 250m, sueldo: 1_000m);

        Assert.Equal(0.25m, relacion);
        Assert.True(relacion <= 0.25m);
    }

    // ---------------------------------------------------------------------------
    // Límite exacto en UmbralCuotaIngresoAlto (0.45)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Relacion_ExactamenteEnUmbralAlto_NoSuperaUmbral()
    {
        // relación == 0.45 → debe clasificar como <= 0.45 (Ajustada en EvaluarIngresos)
        var relacion = EvaluacionCreditoService.CalcularRelacionCuotaIngreso(cuota: 450m, sueldo: 1_000m);

        Assert.Equal(0.45m, relacion);
        Assert.True(relacion <= 0.45m);
    }
}
