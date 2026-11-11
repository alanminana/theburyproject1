using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-04 [IDX-4/5]: los artefactos de la ventana (script de migración, reversión y runner) deben conservar sus guardas. No requieren base alguna:
/// verifican el texto de los scripts para que nadie las quite sin darse cuenta. El comportamiento real se ensaya contra un clon (ver docs/d04-aplicacion-controlada.md).
/// </summary>
public class D04VentanaScriptsContractTests
{
    private static string Leer(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray()));
    }

    [Fact(DisplayName = "D-04 [IDX-4] el script de reversión es transaccional, con DryRun, base esperada y exige que D-04 sea la última migración")]
    public void Rollback_TieneGuardas()
    {
        var sql = Leer("scripts", "sql", "d04-rollback-apertura-unica-caja.sql");
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("SET QUOTED_IDENTIFIER ON", sql);
        Assert.Contains("DB_NAME() <> @BaseEsperada", sql);
        Assert.Contains("MigrationId > @Migracion", sql);
        Assert.Contains("DryRun=1: se revierte", sql);
        Assert.Contains("DROP INDEX [UX_AperturasCaja_CajaId_Abierta]", sql);
        Assert.Contains("CREATE INDEX [IX_AperturasCaja_CajaId]", sql);
        // Solo toca índices e historial: ninguna fila de negocio.
        var sinComentarios = Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline), @"--[^\r\n]*", "");
        Assert.DoesNotMatch(@"(?i)\b(UPDATE|TRUNCATE|INSERT)\b", sinComentarios);
        Assert.Single(Regex.Matches(sinComentarios, @"(?i)\bDELETE\b"));
    }

    [Fact(DisplayName = "D-04 [IDX-5] el runner exige token con fecha, -ConfirmarBaseReal para la base real, respaldo verificado y -Base explícito")]
    public void Runner_NoAplicaSinGuardas()
    {
        var ps = Leer("scripts", "ops", "d04-ventana.ps1");
        Assert.Matches(@"\[Parameter\(Mandatory = \$true\)\]\[string\]\$Base", ps);
        Assert.Contains("'D04-' + (Get-Date -Format 'yyyyMMdd')", ps);
        Assert.Contains("ConfirmarBaseReal", ps);
        Assert.Contains("RespaldoVerificado", ps);
        Assert.Contains("HuellaVigente", ps);
        Assert.Contains("[string]$Modo = 'Preflight'", ps);
        Assert.Contains("se niega a escribir", ps);
        Assert.DoesNotContain("dotnet ef", ps);
        Assert.DoesNotContain("AllowMigrations", ps);
    }
}
