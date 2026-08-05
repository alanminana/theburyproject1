namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// PUN-ML10-G: auditoría estructural de que el recálculo global de scoring
/// (<c>IClienteScoringService.RecalcularTodosAsync</c>) nunca se dispara automáticamente — ni al
/// iniciar la app (<c>Program.cs</c>), ni desde una migración (<c>Migrations/</c>). Sólo debe
/// invocarse desde <c>MantenimientoController</c> (endpoint HTTP explícito, autenticado, con
/// permiso de máximo privilegio).
/// </summary>
public class RecalculoGlobalScoringAuditTests
{
    [Fact]
    public void ProgramCs_NuncaInvocaElRecalculoGlobal()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "Program.cs"));

        Assert.DoesNotContain("RecalcularTodosAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migraciones_NuncaInvocanElRecalculoGlobal()
    {
        var repoRoot = FindRepoRoot();
        var migrationsDir = Path.Combine(repoRoot, "Migrations");
        if (!Directory.Exists(migrationsDir))
            return; // Nada que auditar si el proyecto no tiene migraciones EF en esta carpeta.

        var archivosConLlamada = Directory
            .EnumerateFiles(migrationsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("RecalcularTodosAsync", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(archivosConLlamada);
    }

    [Fact]
    public void SoloMantenimientoControllerInvocaElRecalculoGlobal()
    {
        var repoRoot = FindRepoRoot();
        var controllersDir = Path.Combine(repoRoot, "Controllers");

        var callers = Directory
            .EnumerateFiles(controllersDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => File.ReadAllText(f).Contains("RecalcularTodosAsync", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        var caller = Assert.Single(callers);
        Assert.Equal("MantenimientoController.cs", caller);
    }

    [Fact]
    public void Seeder_DefineElPermisoYLoExcluyeDelGrantEnBloqueDeAdmin()
    {
        // PUN-ML10-G: máximo privilegio = sólo SuperAdmin. El seeder da a Admin TODAS las acciones
        // de cada módulo salvo las listadas explícitamente en exceptoAcciones (AsignarTodosLosPermisosAsync)
        // — si la acción nueva no está en esa lista, Admin la heredaría por el grant en bloque.
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "Data", "Seeds", "RolesPermisosSeeder.cs"));

        Assert.Contains("\"recalcularscoringglobal\"", source, StringComparison.Ordinal);
        Assert.Contains("\"configuracion.recalcularscoringglobal\"", source, StringComparison.Ordinal);

        // La cadena de exclusión de Admin debe contener la acción nueva en el mismo bloque que las
        // dos ya probadas por ConfiguracionPunitorioHttpTests (mismo mecanismo, mismo array).
        var indiceExcepciones = source.IndexOf("exceptoAcciones: new[]", StringComparison.Ordinal);
        Assert.True(indiceExcepciones > 0, "Debe existir el bloque exceptoAcciones del rol Admin.");
        var indiceCierre = source.IndexOf("});", indiceExcepciones, StringComparison.Ordinal);
        var bloqueExcepciones = source.Substring(indiceExcepciones, indiceCierre - indiceExcepciones);

        Assert.Contains("configuracion.managepunitorio", bloqueExcepciones, StringComparison.Ordinal);
        Assert.Contains("configuracion.recalcularscoringglobal", bloqueExcepciones, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio a partir de AppContext.BaseDirectory.");
    }
}
