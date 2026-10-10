using System.ComponentModel.DataAnnotations;
using TheBuryProject.Validation;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para los atributos de validación argentina (DNI, CUIL/CUIT,
/// nombre, teléfono, código postal). Funciones puras — no requieren base de datos.
/// </summary>
public class ArgentinaValidationAttributesTests
{
    private static ValidationResult? Run(ValidationAttribute attr, object? value, object? instance = null, string member = "Campo")
    {
        var ctx = new ValidationContext(instance ?? new object()) { MemberName = member, DisplayName = member };
        return attr.GetValidationResult(value, ctx);
    }

    // ---------- SoloLetras ----------

    [Theory]
    [InlineData("Ana")]
    [InlineData("Juan")]
    [InlineData("María José")]
    [InlineData("D'Angelo")]
    [InlineData("Ñandú")]
    [InlineData("De la Cruz")]
    [InlineData("Jo")] // mínimo 2
    public void SoloLetras_ValoresValidos(string value)
    {
        Assert.Null(Run(new SoloLetrasAttribute(), value));
    }

    [Theory]
    [InlineData("A")]        // menos de 2
    [InlineData("Juan123")]  // tiene números
    [InlineData("Juan_Perez")] // guion bajo no permitido
    [InlineData("123")]      // solo números
    [InlineData("@nombre")]  // símbolo
    public void SoloLetras_ValoresInvalidos(string value)
    {
        Assert.NotNull(Run(new SoloLetrasAttribute(), value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SoloLetras_VacioEsValido_DelegaEnRequired(string? value)
    {
        Assert.Null(Run(new SoloLetrasAttribute(), value));
    }

    [Theory]
    [InlineData("Maximilian")]        // 10 letras: tope por palabra
    [InlineData("Juan Carlos")]       // nombre compuesto
    [InlineData("Juan Carlos Maria")] // 3 palabras
    [InlineData("Pérez-Gómez")]       // guion: cada tramo cuenta aparte
    public void SoloLetras_LimitesPorPalabra_Validos(string value)
    {
        Assert.Null(Run(new SoloLetrasAttribute(), value));
    }

    [Theory]
    [InlineData("Maximiliano")]          // 11 letras en una palabra
    [InlineData("Juan Carlos Maria Jose")] // 4 palabras
    public void SoloLetras_ExcedeLimites_Invalido(string value)
    {
        Assert.NotNull(Run(new SoloLetrasAttribute(), value));
    }

    [Fact]
    public void SoloLetras_LimitesConfigurables()
    {
        var attr = new SoloLetrasAttribute { MaxWordLength = 20, MaxWords = 6 };
        Assert.Null(Run(attr, "Presidencia Roque Sáenz Peña"));
    }

    // ---------- Monto ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1500.50)]
    [InlineData(20000000)]
    public void Monto_EnRango_Valido(double value)
    {
        Assert.Null(Run(new MontoArgentinoAttribute(), (decimal)value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20000000.01)]
    [InlineData(1.234)] // más de 2 decimales
    public void Monto_FueraDeRango_Invalido(double value)
    {
        Assert.NotNull(Run(new MontoArgentinoAttribute(), (decimal)value));
    }

    [Fact]
    public void Monto_MinimoConfigurable_Y_NuloEsValido()
    {
        Assert.NotNull(Run(new MontoArgentinoAttribute { Minimo = 0.01 }, 0m));
        Assert.Null(Run(new MontoArgentinoAttribute(), null));
    }

    // ---------- DNI ----------

    [Theory]
    [InlineData("12345678")]
    [InlineData("00123456")]
    [InlineData("1234567")] // DNI antiguo de 7 dígitos
    public void Dni_SieteOOchoDigitos_Valido(string value)
    {
        Assert.Null(Run(new DniArgentinoAttribute(), value));
    }

    [Theory]
    [InlineData("123456")]    // 6 dígitos
    [InlineData("123456789")] // 9 dígitos
    [InlineData("1234567a")]  // letra
    [InlineData("12.345.678")] // con puntos
    public void Dni_Invalido(string value)
    {
        Assert.NotNull(Run(new DniArgentinoAttribute(), value));
    }

    // ---------- CUIL/CUIT (dígito verificador) ----------

    [Theory]
    [InlineData("20123456786")] // 20 - 12345678 - 6 (verificador correcto)
    public void CuilCuit_VerificadorCorrecto_Valido(string value)
    {
        Assert.True(CuilCuitArgentinoAttribute.EsValido(value));
        Assert.Null(Run(new CuilCuitArgentinoAttribute(), value));
    }

    [Theory]
    [InlineData("20123456787")] // verificador incorrecto
    [InlineData("12123456786")] // prefijo inválido (12)
    [InlineData("2012345678")]  // 10 dígitos
    [InlineData("201234567866")] // 12 dígitos
    [InlineData("2012345678a")] // letra
    public void CuilCuit_Invalido(string value)
    {
        Assert.False(CuilCuitArgentinoAttribute.EsValido(value));
        Assert.NotNull(Run(new CuilCuitArgentinoAttribute(), value));
    }

    [Fact]
    public void CuilCuit_AceptaGuionesYNormaliza()
    {
        // Mismo número con guiones debe ser válido (se normaliza a dígitos)
        Assert.Null(Run(new CuilCuitArgentinoAttribute(), "20-12345678-6"));
    }

    // ---------- CUIL coincide con DNI (XX-DNI-X) ----------

    private sealed class CuilDniModel
    {
        public string? NumeroDocumento { get; set; }
        public string? CuilCuit { get; set; }
    }

    [Fact]
    public void CuilCoincideConDni_MedioIgualAlDni_Valido()
    {
        var model = new CuilDniModel { NumeroDocumento = "12345678", CuilCuit = "20123456786" };
        var attr = new CuilCoincideConDniAttribute(nameof(CuilDniModel.NumeroDocumento));
        Assert.Null(Run(attr, model.CuilCuit, model, nameof(CuilDniModel.CuilCuit)));
    }

    [Fact]
    public void CuilCoincideConDni_MedioDistintoDelDni_Invalido()
    {
        var model = new CuilDniModel { NumeroDocumento = "99999999", CuilCuit = "20123456786" };
        var attr = new CuilCoincideConDniAttribute(nameof(CuilDniModel.NumeroDocumento));
        Assert.NotNull(Run(attr, model.CuilCuit, model, nameof(CuilDniModel.CuilCuit)));
    }

    [Fact]
    public void CuilCoincideConDni_SinDni_NoValida()
    {
        var model = new CuilDniModel { NumeroDocumento = null, CuilCuit = "20123456786" };
        var attr = new CuilCoincideConDniAttribute(nameof(CuilDniModel.NumeroDocumento));
        Assert.Null(Run(attr, model.CuilCuit, model, nameof(CuilDniModel.CuilCuit)));
    }

    // ---------- Documento según tipo ----------

    private sealed class DocModel
    {
        public string TipoDocumento { get; set; } = "DNI";
        public string? NumeroDocumento { get; set; }
    }

    [Theory]
    [InlineData("DNI", "12345678", true)]
    [InlineData("DNI", "1234567", true)]
    [InlineData("DNI", "123456", false)]
    [InlineData("DNI", "123456789", false)]
    [InlineData("CUIL", "20123456786", true)]
    [InlineData("CUIT", "20123456786", true)]
    [InlineData("CUIT", "20123456787", false)]
    [InlineData("PASAPORTE", "AB123456", true)] // tipo libre: no se valida estrictamente
    public void DocumentoArgentino_SegunTipo(string tipo, string numero, bool esValido)
    {
        var model = new DocModel { TipoDocumento = tipo, NumeroDocumento = numero };
        var attr = new DocumentoArgentinoAttribute(nameof(DocModel.TipoDocumento));
        var result = Run(attr, model.NumeroDocumento, model, nameof(DocModel.NumeroDocumento));
        if (esValido) Assert.Null(result); else Assert.NotNull(result);
    }

    // ---------- Teléfono ----------

    [Theory]
    [InlineData("1145678901")]
    [InlineData("+54 9 11 4567-8901")]
    [InlineData("(011) 4567-8901")]
    [InlineData("351 4567890")]
    public void Telefono_Valido(string value)
    {
        Assert.Null(Run(new TelefonoArgentinoAttribute(), value));
    }

    [Theory]
    [InlineData("123")]            // muy corto
    [InlineData("11abc45678")]     // letras
    [InlineData("11-4567-8901 ext")] // texto
    public void Telefono_Invalido(string value)
    {
        Assert.NotNull(Run(new TelefonoArgentinoAttribute(), value));
    }

    // ---------- Código Postal ----------

    [Theory]
    [InlineData("1414")]      // formato viejo
    [InlineData("C1414ABC")]  // CPA
    public void CodigoPostal_Valido(string value)
    {
        Assert.Null(Run(new CodigoPostalArgentinoAttribute(), value));
    }

    [Theory]
    [InlineData("141")]       // 3 dígitos
    [InlineData("14145")]     // 5 dígitos
    [InlineData("C1414AB")]   // CPA incompleto
    public void CodigoPostal_Invalido(string value)
    {
        Assert.NotNull(Run(new CodigoPostalArgentinoAttribute(), value));
    }
}
