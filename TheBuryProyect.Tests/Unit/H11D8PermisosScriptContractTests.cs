using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H11 D-8: el script que asigna a Gerente, Cajero y Contador los permisos de Precios y Mora que faltan en la base real debe
/// seguir siendo acotado (solo esas 10 asignaciones, solo INSERT en RolPermisos), transaccional e idempotente.
/// </summary>
public class H11D8PermisosScriptContractTests
{
    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "scripts", "sql", "h11-d8-permisos-precios-mora.sql"));
    }

    private static string SinComentarios(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(sql, @"--[^\r\n]*", string.Empty);
    }

    [Fact]
    public void EsTransaccionalSerializadoConEnsayoObligatorioYVerificacion()
    {
        var sql = Script();
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("$(DryRun)", sql);
        Assert.Contains("ROLLBACK TRANSACTION", sql);
        Assert.Contains("sp_getapplock", sql);
        Assert.Contains("THROW 51015", sql);   // verificación previa al commit
        Assert.Contains("THROW 51014", sql);   // roles homónimos
        Assert.Contains("THROW 51011", sql);   // no crea acciones: aborta si faltan
    }

    [Fact]
    public void SoloInsertaEnRolPermisos_NoCreaAccionesNiTocaOtrasTablas()
    {
        var sql = SinComentarios(Script());

        Assert.DoesNotMatch(@"(?i)\b(DELETE|UPDATE|DROP|TRUNCATE|ALTER|MERGE)\b", sql);
        Assert.Single(Regex.Matches(sql, @"(?i)\bEXEC\b"));
        var inserts = Regex.Matches(sql, @"(?i)\bINSERT\s+(\S+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("dbo.RolPermisos", inserts);
        Assert.DoesNotContain(inserts, t => t.StartsWith("dbo.AccionesModulo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Asignaciones_SonExactamenteLas10DelSembrador()
    {
        var sql = SinComentarios(Script());

        var asignaciones = Regex.Matches(sql, @"\(N'(\w+)',\s*@Mod(Precios|Mora),\s*N'(\w+)'\)")
            .Select(m => $"{m.Groups[1].Value}:{m.Groups[2].Value.ToLowerInvariant()}.{m.Groups[3].Value}")
            .OrderBy(x => x)
            .ToArray();

        var esperadas = new[]
        {
            "Cajero:mora.view", "Contador:mora.view",
            "Gerente:mora.manage", "Gerente:mora.view",
            "Gerente:precios.apply", "Gerente:precios.approve", "Gerente:precios.history",
            "Gerente:precios.revert", "Gerente:precios.simulate", "Gerente:precios.view"
        }.OrderBy(x => x).ToArray();

        Assert.Equal(esperadas, asignaciones);
        // Nada administrativo: ni alta/edición/baja de listas ni configuración de mora.
        Assert.DoesNotContain(asignaciones, a => a.EndsWith(".create") || a.EndsWith(".update") || a.EndsWith(".delete") || a.EndsWith(".config"));
    }

    [Fact]
    public void LosRolesSeResuelvenUnaSolaVezBajoBloqueoYSeUsanSoloEsosId()
    {
        var sql = SinComentarios(Script());

        // Una única lectura de AspNetRoles (la resolución bajo UPDLOCK/HOLDLOCK); el resto usa la variable @Roles.
        Assert.Single(Regex.Matches(sql, @"dbo\.AspNetRoles"));
        Assert.Contains("DECLARE @Roles TABLE", sql);
        Assert.Matches(@"HAVING COUNT\(\*\) = 1", sql);
        // Los esperados son siempre los de la política, nunca "los que se pudieron resolver".
        Assert.Contains("SELECT COUNT(*) FROM @Asignaciones)", sql);
        Assert.DoesNotMatch(@"JOIN\s+dbo\.AspNetRoles", sql);
    }
}
