using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H11 D-1: el script que da de alta ventas.discount y creditos.configure en la base real debe seguir siendo acotado,
/// transaccional e idempotente. Estas verificaciones fallan si alguien lo amplía (otros permisos) o le quita las guardas.
/// </summary>
public class H11D1PermisosScriptContractTests
{
    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "scripts", "sql", "h11-d1-permisos-ventas-discount-creditos-configure.sql"));
    }

    private static string SinComentarios(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(sql, @"--[^\r\n]*", string.Empty);
    }

    [Fact]
    public void EsTransaccionalConEnsayoObligatorioYVerificacion()
    {
        var sql = Script();
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("$(DryRun)", sql);          // sin -v DryRun=... sqlcmd falla: no se aplica por accidente
        Assert.Contains("ROLLBACK TRANSACTION", sql);
        Assert.Contains("THROW 51002", sql);        // verificación previa al commit
    }

    [Fact]
    public void SoloInsertaYSoloLasDosAccionesAutorizadas()
    {
        var sql = SinComentarios(Script());

        Assert.DoesNotMatch(@"(?i)\b(DELETE|UPDATE|DROP|TRUNCATE|ALTER|MERGE)\b", sql);
        // El unico EXEC permitido es el bloqueo de aplicacion que serializa las ejecuciones.
        Assert.Single(Regex.Matches(sql, @"(?i)\bEXEC\b"));
        Assert.Contains("sp_getapplock", sql);
        Assert.Contains("THROW 51004", sql);        // roles homonimos: aborta
        Assert.Matches(@"N'discount'", sql);
        Assert.Matches(@"N'configure'", sql);

        // Todas las claves de acción mencionadas son exactamente esas dos.
        var claves = Regex.Matches(sql, @"N'([a-z]+)'\s*[,)]")
            .Select(m => m.Groups[1].Value)
            .Where(c => c is not ("ventas" or "creditos"))
            .Distinct()
            .OrderBy(c => c)
            .ToArray();
        Assert.Equal(new[] { "configure", "discount" }, claves);
    }

    [Fact]
    public void Asignaciones_SonLasDelSembrador_SinOtorgarDescuentosAGerenteNiVendedor()
    {
        var sql = SinComentarios(Script());

        // ventas.discount: solo Administrador y SuperAdmin (decisión vigente del propietario).
        var descuentos = Regex.Matches(sql, @"\(N'(\w+)',\s*@ModVentas,\s*N'discount'\)").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Administrador", "SuperAdmin" }, descuentos);

        // creditos.configure: Administrador, SuperAdmin, Gerente, Vendedor.
        var configurar = Regex.Matches(sql, @"\(N'(\w+)',\s*@ModCreditos,\s*N'configure'\)").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Administrador", "Gerente", "SuperAdmin", "Vendedor" }, configurar);
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
