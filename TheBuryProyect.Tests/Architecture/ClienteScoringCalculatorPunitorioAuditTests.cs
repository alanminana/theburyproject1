using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// PUN-ML10-E: auditoría estructural de que <c>ClienteScoringCalculator</c> sigue siendo puro
/// respecto de punitorios — el punitorio NO agrega su propia penalización de scoring (política
/// congelada), sólo se refleja indirectamente vía <see cref="TheBuryProject.Models.Enums.EstadoCuota"/>
/// ya resuelto. Si alguna vez se reintroduce una lectura directa de
/// <c>Cuota.MontoPunitorio</c> (legacy, congelado) o una dependencia de
/// <c>IPunitorioService</c>/<c>PunitorioAplicado</c> en el calculador, este test es rojo en
/// compilación de código fuente, sin depender de que algo lo ejecute.
/// </summary>
public sealed partial class ClienteScoringCalculatorPunitorioAuditTests
{
    [Fact]
    public void ClienteScoringCalculator_NuncaLeeMontoPunitorioNiDependeDePunitorioService()
    {
        var source = LeerFuenteSinComentariosNiStrings();

        Assert.DoesNotContain("MontoPunitorio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPunitorioService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PunitorioAplicado", source, StringComparison.Ordinal);
    }

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
            var candidate = Path.Combine(current.FullName, "Services", "ClienteScoringCalculator.cs");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("No se encontró Services/ClienteScoringCalculator.cs a partir de AppContext.BaseDirectory.");
    }

    [GeneratedRegex("""
        //.*?$|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"
        """, RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex CommentsAndStringsRegex();
}
