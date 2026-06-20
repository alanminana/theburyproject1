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

    // ---------- DNI ----------

    [Theory]
    [InlineData("12345678")]
    [InlineData("00123456")]
    public void Dni_OchoDigitos_Valido(string value)
    {
        Assert.Null(Run(new DniArgentinoAttribute(), value));
    }

    [Theory]
    [InlineData("1234567")]   // 7 dígitos
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
    [InlineData("DNI", "1234567", false)]
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
