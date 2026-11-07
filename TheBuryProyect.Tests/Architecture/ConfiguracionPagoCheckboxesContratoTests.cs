using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// H-32: en Configuración de pagos los checkboxes «Activo/Activa» son HTML crudo (sin tag helper) y los modelos de comando
/// asumen <c>true</c> por defecto. Un checkbox desmarcado no envía nada, así que sin un hidden «false» detrás (el binder usa el PRIMER valor) nunca se podía
/// desactivar un medio, una tarjeta ni un plan desde estos formularios.
/// </summary>
public class ConfiguracionPagoCheckboxesContratoTests
{
    private static string Raiz()
    {
        var actual = new DirectoryInfo(AppContext.BaseDirectory);
        while (actual != null)
        {
            if (File.Exists(Path.Combine(actual.FullName, "TheBuryProyect.csproj")))
                return actual.FullName;
            actual = actual.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    [Fact]
    public void MediosPago_CadaCheckboxActivoOActiva_LlevaUnHiddenFalseInmediatamenteDespues()
    {
        var lineas = File.ReadAllLines(Path.Combine(Raiz(), "Views", "ConfiguracionPago", "MediosPago_tw.cshtml"));
        var checkboxes = new List<(int Linea, string Nombre)>();
        for (var i = 0; i < lineas.Length; i++)
        {
            var m = Regex.Match(lineas[i], "<input name=\"(Activo|Activa)\" type=\"checkbox\"");
            if (m.Success) checkboxes.Add((i, m.Groups[1].Value));
        }

        Assert.NotEmpty(checkboxes);
        foreach (var (linea, nombre) in checkboxes)
        {
            var siguiente = lineas[linea + 1];
            Assert.True(
                Regex.IsMatch(siguiente, $"<input type=\"hidden\" name=\"{nombre}\" value=\"false\" */?>"),
                $"MediosPago_tw.cshtml:{linea + 1}: el checkbox «{nombre}» necesita un hidden con value=\"false\" justo DESPUÉS (el binder toma el primer valor: si el hidden va antes, marcar el checkbox nunca activa).");
        }
    }

    [Fact]
    public void MediosPago_LosHiddenDeEstado_NoRenderizanBooleanosComoAtributo()
    {
        // H-33: Razor omite un atributo booleano en false y escribe value="value" en true; el hidden de «Quitar/Reactivar»
        // llegaba como "value" o vacío y el binder nunca interpretaba «true» (reactivar no hacía nada).
        var texto = File.ReadAllText(Path.Combine(Raiz(), "Views", "ConfiguracionPago", "MediosPago_tw.cshtml"));
        Assert.DoesNotContain("name=\"activo\" value=\"@(!", texto);
        Assert.DoesNotContain("name=\"activa\" value=\"@(!", texto);
        Assert.Contains("name=\"activo\" value=\"@(plan.Activo ? \"false\" : \"true\")\"", texto);
        Assert.Contains("name=\"activa\" value=\"@(tarjeta.Activa ? \"false\" : \"true\")\"", texto);
    }

    [Theory]
    [InlineData("nuevoPerfilActivo")]
    [InlineData("nuevaCuotaActivo")]
    public void CreditoPersonal_LosCheckboxesDeAltaRapida_LlevanHiddenFalseDespues(string nombre)
    {
        // El POST declara estos parámetros con valor por defecto true: desmarcar no enviaba nada y el alta quedaba activa.
        var lineas = File.ReadAllLines(Path.Combine(Raiz(), "Views", "ConfiguracionPago", "CreditoPersonal_tw.cshtml"));
        var i = Array.FindIndex(lineas, l => l.Contains($"name=\"{nombre}\"") && l.Contains("type=\"checkbox\""));
        Assert.True(i >= 0);
        Assert.Contains($"<input type=\"hidden\" name=\"{nombre}\" value=\"false\"", lineas[i + 1]);
    }
}
