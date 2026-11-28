using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Las confirmaciones declarativas (data-inv-confirm) pueden mostrar el valor de un campo del formulario
/// (<c>campo = "..."</c>). inventario-ui.js lo busca con <c>form.elements.namedItem</c>, que distingue mayúsculas:
/// si el nombre no coincide, la confirmación muestra el texto por defecto mientras se envía otro valor.
/// </summary>
public class InventarioConfirmacionCamposContractTests
{
    [Fact]
    public void CadaCampoQueMuestraUnaConfirmacionExisteEnLaVistaConElMismoNombre()
    {
        var raiz = Raiz();
        var revisados = 0;
        foreach (var vista in Directory.EnumerateFiles(Path.Combine(raiz, "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(vista);
            if (!texto.Contains("data-inv-confirm", StringComparison.Ordinal)) continue;

            foreach (Match campo in Regex.Matches(texto, "\\bcampo = \"(?<nombre>[^\"]+)\""))
            {
                var nombre = Regex.Escape(campo.Groups["nombre"].Value);
                Assert.True(
                    Regex.IsMatch(texto, $"\\b(name|id|asp-for)=\"{nombre}\""),
                    $"{Path.GetRelativePath(raiz, vista)}: la confirmación lee «{campo.Groups["nombre"].Value}» y ningún control se llama así.");
                revisados++;
            }
        }

        Assert.True(revisados >= 4, $"Se esperaban al menos 4 campos en confirmaciones; se revisaron {revisados}.");
    }

    private static string Raiz()
    {
        var actual = new DirectoryInfo(AppContext.BaseDirectory);
        while (actual != null && !File.Exists(Path.Combine(actual.FullName, "TheBuryProyect.csproj")))
            actual = actual.Parent;
        return actual?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
