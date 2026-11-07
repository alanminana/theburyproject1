using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H-19 (regla aprobada): las observaciones son obligatorias cuando existe promesa, compromiso o acuerdo, y NO en contactos
/// sin respuesta.
/// </summary>
public class MoraObservacionesObligatoriasTests
{
    private static bool Falla(object vm, string propiedad)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);
        return resultados.Any(r => r.MemberNames.Contains(propiedad));
    }

    private static RegistrarContactoViewModel Contacto(ResultadoContacto resultado, string? obs = null,
        DateTime? fechaPromesa = null, decimal? montoPromesa = null) => new()
    {
        ClienteId = 1, TipoContacto = TipoContacto.LlamadaTelefonica, Resultado = resultado, Observaciones = obs,
        FechaPromesaPago = fechaPromesa, MontoPromesaPago = montoPromesa
    };

    [Theory]
    [InlineData(ResultadoContacto.PromesaPago)]
    [InlineData(ResultadoContacto.SolicitaAcuerdo)]
    public void Contacto_ConCompromiso_ExigeObservaciones(ResultadoContacto resultado)
    {
        Assert.True(Falla(Contacto(resultado), nameof(RegistrarContactoViewModel.Observaciones)));
        Assert.True(Falla(Contacto(resultado, "   "), nameof(RegistrarContactoViewModel.Observaciones)));
        Assert.False(Falla(Contacto(resultado, "Se compromete a pagar el viernes"), nameof(RegistrarContactoViewModel.Observaciones)));
    }

    [Theory]
    [InlineData(ResultadoContacto.NoContesta)]
    [InlineData(ResultadoContacto.NumeroEquivocado)]
    [InlineData(ResultadoContacto.ContactoExitoso)]
    [InlineData(ResultadoContacto.RechazaPagar)]
    public void Contacto_SinCompromiso_NoExigeObservaciones(ResultadoContacto resultado)
    {
        Assert.False(Falla(Contacto(resultado), nameof(RegistrarContactoViewModel.Observaciones)));
    }

    [Fact]
    public void Contacto_ConFechaOMontoPrometidos_ExigeObservacionesAunqueElResultadoSeaOtro()
    {
        Assert.True(Falla(Contacto(ResultadoContacto.ContactoExitoso, null, fechaPromesa: DateTime.UtcNow.Date.AddDays(3)),
            nameof(RegistrarContactoViewModel.Observaciones)));
        Assert.True(Falla(Contacto(ResultadoContacto.ContactoExitoso, null, montoPromesa: 500m),
            nameof(RegistrarContactoViewModel.Observaciones)));
    }

    [Fact]
    public void Promesa_YAcuerdo_ExigenObservaciones()
    {
        var promesa = new RegistrarPromesaViewModel { AlertaId = 1, ClienteId = 1, FechaPromesa = DateTime.UtcNow.Date.AddDays(2), MontoPromesa = 100m };
        Assert.True(Falla(promesa, nameof(promesa.Observaciones)));
        promesa.Observaciones = "Cobra el viernes";
        Assert.False(Falla(promesa, nameof(promesa.Observaciones)));

        var acuerdo = new CrearAcuerdoViewModel { AlertaId = 1, ClienteId = 1, CreditoId = 1, CantidadCuotas = 3, FechaPrimeraCuota = DateTime.UtcNow.Date.AddDays(5), MontoEntregaInicial = 0m };
        Assert.True(Falla(acuerdo, nameof(acuerdo.Observaciones)));
        acuerdo.Observaciones = "Acordado en 3 cuotas";
        Assert.False(Falla(acuerdo, nameof(acuerdo.Observaciones)));
    }
}
