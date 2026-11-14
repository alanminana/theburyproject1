using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// D-05 (recorridos de escritura bajo CSP): los escenarios <c>scripts/qa/csp-roles/escenarios/zz-escritura-d05.js</c> asumen una matriz
/// de autorización concreta (quién puede y quién NO puede escribir). Si alguien cambia el permiso de una acción, la prueba de navegador
/// (que no corre en CI: exige un stack QA aislado) quedaría mintiendo sin avisar. Estos tests fijan el contrato en CI:
/// cada acción de escritura recorrida conserva su <c>[PermisoRequerido]</c> y el escenario sigue cubriendo la ruta.
/// </summary>
public class CspEscrituraD05ContractTests
{
    private static readonly string Escenarios = Path.Combine("scripts", "qa", "csp-roles", "escenarios", "zz-escritura-d05.js");

    [Theory]
    [InlineData("Controllers/PlantillaContratoCreditoController.cs", "Create", "configuracion", "update")]
    [InlineData("Controllers/ContratoVentaCreditoController.cs", "Generar", "creditos", "configure")]
    [InlineData("Controllers/VentaController.cs", "Facturar", "ModuloVentas", "AccionFacturar")]
    [InlineData("Controllers/DevolucionController.cs", "QuickCreate", "devoluciones", "create")]
    [InlineData("Controllers/DevolucionController.cs", "Aprobar", "devoluciones", "approve")]
    [InlineData("Controllers/DevolucionController.cs", "Completar", "devoluciones", "complete")]
    [InlineData("Controllers/DevolucionController.cs", "Rechazar", "devoluciones", "reject")]
    [InlineData("Controllers/DevolucionController.cs", "Cancelar", "devoluciones", "reject")]
    public void AccionDeEscritura_ConservaElPermisoQueAsumenLosEscenariosD05(string controller, string accion, string modulo, string permiso)
    {
        var fuente = File.ReadAllText(Path.Combine(FindRepoRoot(), controller));
        // Los atributos y comentarios de la acción están entre el "}" del miembro anterior y su firma: se toma esa ventana. La sobrecarga
        // GET de Create/Facturar no cuenta como POST porque no lleva [HttpPost].
        var firma = new Regex(@"public async Task<IActionResult> " + accion + @"\(");
        var posts = new List<string>();
        foreach (Match m in firma.Matches(fuente))
        {
            var ventana = fuente.Substring(Math.Max(0, m.Index - 1200), Math.Min(1200, m.Index));
            var bloque = ventana[(ventana.LastIndexOf('}') + 1)..];
            if (bloque.Contains("[HttpPost]", StringComparison.Ordinal)) posts.Add(bloque);
        }

        Assert.True(posts.Count == 1, $"{controller}: se esperaba exactamente una acción POST {accion}, hay {posts.Count}.");
        var attrs = posts[0];
        Assert.Contains("[ValidateAntiForgeryToken]", attrs, StringComparison.Ordinal);
        Assert.Matches(@"PermisoRequerido\(Modulo\s*=\s*""?" + Regex.Escape(modulo) + @"""?,\s*Accion\s*=\s*""?" + Regex.Escape(permiso) + @"""?\)", attrs);
    }

    [Fact]
    public void LosEscenariosD05_CubrenLasRutasDeEscrituraYLaMatrizDeRoles()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), Escenarios));

        foreach (var ruta in new[]
                 {
                     "/PlantillaContratoCredito/Create", "/Venta/Facturar", "/ContratoVentaCredito/Preparar", "/ContratoVentaCredito/Generar",
                     "/Devolucion/Detalles/", "/Devolucion/${acc}", "[data-contrato-generar-btn]", "#form-facturar",
                     "toggle-hide", "QuickCreate",
                 })
            Assert.True(js.Contains(ruta, StringComparison.Ordinal), $"Falta en el escenario D-05: {ruta}");

        // SuperAdmin escribe; Administrador, Cajero y Repositor se prueban en NEGATIVO.
        foreach (var usuario in new[] { "usuario: 'admin'", "usuario: 'administrador'", "usuario: 'cajero'", "usuario: 'repositor'" })
            Assert.Contains(usuario, js, StringComparison.Ordinal);
        // Control negativo: el medidor debe detectar un handler inline inyectado en estas pantallas.
        Assert.Contains("violacionesEsperadas", js, StringComparison.Ordinal);
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
