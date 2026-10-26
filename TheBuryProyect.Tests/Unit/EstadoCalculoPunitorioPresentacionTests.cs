using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels.PagoCuota;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// QA manual (2026-10-26): Pagar/Adelantar cuota mostraba al operador el nombre técnico del enum ("EntradaInvalida") y
/// su mensaje interno ("FechaCalculo no puede ser anterior a FechaVencimiento") para una cuota que todavía no venció.
/// </summary>
public class EstadoCalculoPunitorioPresentacionTests
{
    [Theory]
    [MemberData(nameof(Estados))]
    public void Etiqueta_NuncaMuestraElNombreTecnicoDelEnum(EstadoCalculoPunitorioDetalle estado)
    {
        var etiqueta = estado.Etiqueta();

        Assert.False(string.IsNullOrWhiteSpace(etiqueta));
        if (estado != EstadoCalculoPunitorioDetalle.Calculado)
            Assert.NotEqual(estado.ToString(), etiqueta);
    }

    [Fact]
    public void Etiqueta_EntradaInvalida_ExplicaQueLaCuotaNoVencio()
    {
        Assert.Contains("no venció", EstadoCalculoPunitorioDetalle.EntradaInvalida.Etiqueta(cuotaNoVencida: true));
    }

    [Fact]
    public void Etiqueta_EntradaInvalidaConCuotaVencida_NoDiceQueNoVencio()
    {
        // Configuración o pagos inconsistentes en una cuota ya vencida: es un error real, no "todavía no venció".
        var etiqueta = EstadoCalculoPunitorioDetalle.EntradaInvalida.Etiqueta(cuotaNoVencida: false);

        Assert.DoesNotContain("no venció", etiqueta);
        Assert.Contains("inconsistentes", etiqueta);
    }

    public static IEnumerable<object[]> Estados() =>
        Enum.GetValues<EstadoCalculoPunitorioDetalle>().Select(e => new object[] { e });
}
