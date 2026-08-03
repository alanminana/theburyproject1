using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// PUN-ML7 (cierre): auditoría estructural final de fuentes de fecha en MoraService. Ninguna
/// decisión comercial (vencimiento, vigencia de promesas/acuerdos, clasificación de KPIs) puede
/// depender de DateTime.Today/DateTime.Now — varían con la zona del proceso, no con Argentina — toda
/// fecha "hoy" debe pasar por IRelojComercial (HoyComercial/InicioDiaComercial).
/// DateTime.UtcNow sigue permitido: en este archivo se usa exclusivamente para timestamps técnicos
/// de auditoría (CreatedAt/UpdatedAt/FechaAlerta/FechaContacto/FechaResolucion, duración de
/// ejecución del job, generación de NumeroAcuerdo), nunca para decidir vencimiento o vigencia — ver
/// <see cref="TheBuryProject.Services.MoraService"/>.
/// </summary>
public sealed partial class MoraServiceDateAuditTests
{
    [Fact]
    public void MoraService_NoUsaDateTimeTodayNiDateTimeNow()
    {
        var source = LeerFuenteMoraServiceSinComentariosNiStrings();

        Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
        // No matchea "DateTime.UtcNow": tras "DateTime." el siguiente caracter ahí es 'U', no 'N'.
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
    }

    private static string LeerFuenteMoraServiceSinComentariosNiStrings()
    {
        var path = FindMoraServiceSourcePath();
        var source = File.ReadAllText(path);
        return CommentsAndStringsRegex().Replace(source, string.Empty);
    }

    private static string FindMoraServiceSourcePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Services", "MoraService.cs");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("No se encontró Services/MoraService.cs a partir de AppContext.BaseDirectory.");
    }

    [GeneratedRegex("""
        //.*?$|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"
        """, RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex CommentsAndStringsRegex();
}
