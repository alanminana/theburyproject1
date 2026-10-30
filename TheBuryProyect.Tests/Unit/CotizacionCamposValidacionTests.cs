using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L13 — campos libres de la cotización: antes se recortaban en silencio o no se controlaban.</summary>
public class CotizacionCamposValidacionTests
{
    private static readonly DateTime Ahora = new(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc);

    private static CotizacionCrearRequest Req(
        string? observaciones = null, string? nombre = null, string? telefono = null, DateTime? vence = null)
        => new()
        {
            Observaciones = observaciones,
            NombreClienteLibre = nombre,
            TelefonoClienteLibre = telefono,
            FechaVencimiento = vence
        };

    private static void Valido(CotizacionCrearRequest r) => CotizacionService.ValidarCamposLibres(r, Ahora);

    private static void Invalido(CotizacionCrearRequest r)
        => Assert.Throws<InvalidOperationException>(() => CotizacionService.ValidarCamposLibres(r, Ahora));

    [Fact]
    public void SinCamposLibres_EsValido() => Valido(Req());

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Observaciones_Tope1000(int largo, bool ok)
    {
        var r = Req(observaciones: new string('o', largo));
        if (ok) Valido(r); else Invalido(r);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Nombre_Tope200(int largo, bool ok)
    {
        var r = Req(nombre: new string('n', largo));
        if (ok) Valido(r); else Invalido(r);
    }

    [Theory]
    [InlineData("11 5555-1234", true)]
    [InlineData("+54 (11) 5555-1234", true)]
    [InlineData("0221 4567890", true)]
    [InlineData("llamar a Juan", false)]
    [InlineData("---", false)]
    [InlineData("1234567890123456789012345678901", false)]
    public void Telefono_SoloNumerosYSeparadores(string tel, bool ok)
    {
        var r = Req(telefono: tel);
        if (ok) Valido(r); else Invalido(r);
    }

    [Theory]
    [InlineData(0, true)]      // hoy
    [InlineData(-1, true)]     // ayer: tolerancia por huso (la fecha cargada es local)
    [InlineData(30, true)]
    [InlineData(731, true)]
    [InlineData(-2, false)]
    [InlineData(-400, false)]
    [InlineData(732, false)]
    public void FechaVencimiento_EntreHoyYDosAnios(int dias, bool ok)
    {
        var r = Req(vence: Ahora.Date.AddDays(dias));
        if (ok) Valido(r); else Invalido(r);
    }

    [Fact]
    public void FechaVencimiento_AnioExtremo_Invalida()
        => Invalido(Req(vence: new DateTime(9999, 12, 31)));
}
