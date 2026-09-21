namespace TheBuryProject.Tests.Unit;

/// <summary>
/// COTIZACION-MIVENTA-02 (§27 del pedido): Venta/Create no tenía protección contra
/// doble-submit del envío final (auditado antes de implementar — confirmado en
/// wwwroot/js/venta-create.js: el listener de "submit" sólo validaba, sin ningún guard de
/// reentrancia ni disabled-on-click). Estos tests son un contrato mínimo sobre el JS real
/// (no ejecutan el navegador) para que una futura edición no borre el guard por accidente;
/// el comportamiento en runtime ya se verificó con Playwright (doble-click → una sola Venta).
/// </summary>
public class VentaCreateDobleSubmitContractTests
{
    [Fact]
    public void VentaCreateJs_DefineFlagDeReentranciaParaElSubmitFinal()
    {
        var js = ReadJs();

        Assert.Contains("let submitFinalEnCurso = false;", js);
    }

    [Fact]
    public void VentaCreateJs_ElGuardCorreAntesQueCualquierValidacion()
    {
        var js = ReadJs();

        var indexListener = js.IndexOf("ventaForm.addEventListener('submit'", StringComparison.Ordinal);
        var indexGuard = js.IndexOf("if (submitFinalEnCurso)", StringComparison.Ordinal);
        var indexPrimeraValidacion = js.IndexOf("trazableSinUnidad", StringComparison.Ordinal);

        Assert.True(indexListener >= 0 && indexGuard >= 0 && indexPrimeraValidacion >= 0);
        // El guard de reentrancia es lo primero que corre dentro del handler — antes que
        // cualquier validación de negocio (así un segundo submit no revalida nada, sólo corta).
        Assert.True(indexListener < indexGuard);
        Assert.True(indexGuard < indexPrimeraValidacion);
    }

    [Fact]
    public void VentaCreateJs_ElFlagSeFijaSoloDespuesDeQueTodasLasGuardasPasen()
    {
        var js = ReadJs();

        var indexComentarioGuardasPasaron = js.IndexOf("Todas las guardas pasaron", StringComparison.Ordinal);
        var indexFlagTrue = js.IndexOf("submitFinalEnCurso = true;", StringComparison.Ordinal);

        Assert.True(indexComentarioGuardasPasaron >= 0 && indexFlagTrue >= 0);
        // El flag se fija DESPUÉS del punto donde ya se dejó pasar el submit real — nunca antes,
        // porque un submit legítimamente bloqueado por una validación (p. ej. falta motivo de
        // excepción) debe poder reintentarse sin quedar trabado por su propio guard.
        Assert.True(indexComentarioGuardasPasaron < indexFlagTrue);
    }

    private static string ReadJs() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
