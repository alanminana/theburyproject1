using System.ComponentModel.DataAnnotations;
using TheBuryProject.Services;
using TheBuryProject.ViewModels.PagoCuota;
using TheBuryProject.ViewModels.Punitorio;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L14 — fecha de la primera cuota, importe de pago y motivos de punitorio.</summary>
public class CreditoCamposValidacionTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 29);

    [Theory]
    [InlineData(0, true)]      // hoy: se cobra al confirmar
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(400, true)]
    [InlineData(-1, false)]    // nacería vencida
    [InlineData(-400, false)]
    [InlineData(401, false)]
    [InlineData(3000, false)]
    public void PrimeraCuota_EntreHoyY400Dias(int dias, bool valida)
    {
        var fecha = Hoy.AddDays(dias).ToDateTime(TimeOnly.MinValue);

        Assert.Equal(valida, CreditoConfiguracionVentaService.ErrorFechaPrimeraCuota(fecha, Hoy) == null);
    }

    [Fact]
    public void PrimeraCuota_Vacia_EsInvalida()
        => Assert.NotNull(CreditoConfiguracionVentaService.ErrorFechaPrimeraCuota(null, Hoy));

    private static bool Valido(object modelo)
        => Validator.TryValidateObject(modelo, new ValidationContext(modelo), new List<ValidationResult>(), validateAllProperties: true);

    private static PagarCuotaInputModel Pago(decimal monto) => new()
    {
        MontoIngresado = monto,
        MedioPago = "Efectivo",
        CuotaRowVersionBase64 = "AAAA"
    };

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(150000, true)]
    [InlineData(20000000, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(20000001, false)]
    public void PagoCuota_Importe0_01a20Millones(double monto, bool valido)
        => Assert.Equal(valido, Valido(Pago((decimal)monto)));

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Punitorio_MotivoTope500(int largo, bool valido)
    {
        var aplicar = new AplicarPunitorioHttpViewModel { Motivo = new string('m', largo), CuotaRowVersionBase64 = "AAAA" };
        var anular = new AnularPunitorioHttpViewModel { Motivo = new string('m', largo), PunitorioAplicadoRowVersionBase64 = "AAAA" };

        Assert.Equal(valido, Valido(aplicar));
        Assert.Equal(valido, Valido(anular));
    }
}
