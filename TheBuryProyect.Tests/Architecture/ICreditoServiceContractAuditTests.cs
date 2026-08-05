using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// PUN-ML9-D.1 (riesgo 1): <c>ICreditoService</c> debe ser un contrato obligatorio de interfaz —
/// ningún miembro puede tener implementación por defecto (<c>=> ...;</c> en la declaración de la
/// interfaz). Un método default cómodo para no tocar los dobles viejos (como existió para
/// <c>ObtenerContextoPagoCuotaAsync</c>/<c>PrevisualizarPagoCuotaAsync</c>/
/// <c>RegistrarPagoCuotaIndividualAsync</c>, que lanzaban <see cref="NotSupportedException"/>)
/// falla en runtime recién cuando algo lo invoca, no en tiempo de compilación — cualquier
/// implementación real que se olvide de sobrescribirlo compila igual y explota más tarde. Este
/// test impide que vuelva a ocurrir: si se reintroduce un default en la interfaz, este test es
/// rojo en compilación de código fuente (no depende de que algo lo ejecute).
/// </summary>
public sealed partial class ICreditoServiceContractAuditTests
{
    [Fact]
    public void ICreditoService_NingunMiembroTieneImplementacionPorDefecto()
    {
        var source = LeerFuenteSinComentariosNiStrings();

        // Firma de miembro de interfaz seguida de cuerpo expresión (=>) en vez de terminar en ';'.
        // Los miembros reales de la interfaz siempre cierran su lista de parámetros con ')' y
        // continúan con ';' (posiblemente tras salto de línea/espacios); un '=>' entre el ')' y el
        // ';' es exactamente un default interface method.
        var match = DefaultBodyRegex().Match(source);

        Assert.False(match.Success,
            $"ICreditoService tiene un miembro con implementación por defecto (default interface " +
            $"method) cerca de: \"{Truncar(match.Value)}\". Todos los métodos deben ser contratos " +
            "obligatorios — si una implementación no lo soporta, debe declararlo explícitamente " +
            "(NotImplementedException/NotSupportedException) en SU PROPIA clase, no heredarlo del " +
            "default de la interfaz.");

        Assert.DoesNotContain("NotSupportedException", source, StringComparison.Ordinal);
    }

    private static string Truncar(string value) => value.Length <= 80 ? value : value[..80] + "…";

    private static string LeerFuenteSinComentariosNiStrings()
    {
        var path = FindSourcePath();
        var source = File.ReadAllText(path);
        return CommentsAndStringsRegex().Replace(source, string.Empty);
    }

    private static string FindSourcePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Services", "Interfaces", "ICreditoService.cs");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException(
            "No se encontró Services/Interfaces/ICreditoService.cs a partir de AppContext.BaseDirectory.");
    }

    [GeneratedRegex("""
        //.*?$|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"
        """, RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex CommentsAndStringsRegex();

    [GeneratedRegex(@"\)\s*=>", RegexOptions.Singleline)]
    private static partial Regex DefaultBodyRegex();
}
