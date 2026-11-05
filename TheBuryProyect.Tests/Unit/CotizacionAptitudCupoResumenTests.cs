using TheBuryProject.Controllers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H11: la tarjeta de aptitud del cotizador no puede mostrar «Apto» con «Cupo $0» sin explicar nada: si la validación de
/// cupo está apagada el cupo es «no evaluado»; si se evalúa y el monto lo supera, se marca el exceso y el faltante.
/// </summary>
public class CotizacionAptitudCupoResumenTests
{
    [Fact]
    public void CupoEvaluadoYMontoMayor_MarcaExcesoYFaltante()
    {
        var r = CotizacionApiController.ResumirCupo(new AptitudCupoDetalle { Evaluado = true, CupoDisponible = 100_000m }, 150_000m);

        Assert.True(r.CupoEvaluado);
        Assert.True(r.ExcedeCupo);
        Assert.Equal(50_000m, r.Faltante);
    }

    [Fact]
    public void CupoEvaluadoEnCero_ConMontoPositivo_Excede()
    {
        var r = CotizacionApiController.ResumirCupo(new AptitudCupoDetalle { Evaluado = true, CupoDisponible = 0m }, 12_752m);

        Assert.True(r.ExcedeCupo);
        Assert.Equal(12_752m, r.Faltante);
    }

    [Theory]
    [InlineData(100_000, 100_000)]
    [InlineData(100_000, 40_000)]
    public void MontoDentroDelCupo_NoExcede(int cupo, int monto)
    {
        var r = CotizacionApiController.ResumirCupo(new AptitudCupoDetalle { Evaluado = true, CupoDisponible = cupo }, monto);

        Assert.False(r.ExcedeCupo);
        Assert.Equal(0m, r.Faltante);
    }

    [Fact]
    public void ValidacionDeCupoApagada_NoEvaluado_NoSeInformaComoExceso()
    {
        // Evaluado=false => CupoDisponible queda en 0 por defecto: no es "$0 disponibles".
        var r = CotizacionApiController.ResumirCupo(new AptitudCupoDetalle { Evaluado = false, CupoDisponible = 0m }, 12_752m);

        Assert.False(r.CupoEvaluado);
        Assert.False(r.ExcedeCupo);
        Assert.Equal(0m, r.Faltante);
    }
}
