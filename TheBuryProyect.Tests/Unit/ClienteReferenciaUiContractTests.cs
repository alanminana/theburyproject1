namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-02: contrato de la solapa «Referencias» de Cliente/Details (vista + parcial + componente + script).
/// </summary>
public class ClienteReferenciaUiContractTests
{
    private static string Leer(params string[] partes) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(partes).ToArray()));

    [Fact]
    public void Details_UbicaLaSeccionReferenciasDentroDeLaSolapaDatosSinAgregarSolapas()
    {
        var view = Leer("Views", "Cliente", "Details_tw.cshtml").Replace("\r\n", "\n");

        var panelDatos = view.IndexOf("id=\"panel-datos\"", StringComparison.Ordinal);
        var card = view.IndexOf("id=\"referencias-card\"", StringComparison.Ordinal);
        Assert.True(panelDatos > 0 && card > panelDatos, "La sección Referencias debe estar dentro del panel Datos.");
        Assert.DoesNotContain("data-cliente-tab=\"referencias\"", view);
        Assert.Contains("Component.InvokeAsync(\"ClienteReferencias\"", view);
        Assert.Contains("~/js/cliente-referencias.js", view);
        Assert.Contains("id=\"referenciasMensaje\"", view);
        Assert.Contains("aria-live=\"polite\"", view);
    }

    [Fact]
    public void Details_LosModalesSonDialogosConEtiquetaYSoloExistenConPermisoDeEdicion()
    {
        var view = Leer("Views", "Cliente", "Details_tw.cshtml");

        foreach (var id in new[] { "referenciaModal", "referenciaVerifModal", "referenciaBajaModal" })
        {
            Assert.Contains($"id=\"{id}\" class=\"overlay hidden\" role=\"dialog\" aria-modal=\"true\"", view);
        }

        // Cada campo del formulario tiene su label asociado.
        foreach (var campo in new[] { "refTipo", "refNombre", "refRelacion", "refTelefono", "refEmail", "refEmpresa", "refObservaciones", "refVerifEstado", "refVerifObs" })
        {
            Assert.Contains($"for=\"{campo}\"", view);
            Assert.Contains($"id=\"{campo}\"", view);
        }

        // Los modales están dentro del bloque protegido por clientes.edit.
        var bloqueEdicion = view.IndexOf("@if (puedeEditarCliente)\n    {\n        <div id=\"referenciaModal\"", StringComparison.Ordinal);
        var bloqueEdicionCrLf = view.IndexOf("@if (puedeEditarCliente)\r\n    {\r\n        <div id=\"referenciaModal\"", StringComparison.Ordinal);
        Assert.True(bloqueEdicion >= 0 || bloqueEdicionCrLf >= 0, "Los modales de referencias deben renderizarse solo con clientes.edit.");
    }

    [Fact]
    public void Listado_OfreceAccionesSoloConPermisoYTieneEstadoVacio()
    {
        var lista = Leer("Views", "ClienteReferencia", "_ClienteReferenciasLista.cshtml");

        Assert.Contains("User.TienePermiso(\"clientes\", \"edit\")", lista);
        Assert.Contains("data-referencias-vacio", lista);
        Assert.Contains("data-ref-editar", lista);
        Assert.Contains("data-ref-verificar", lista);
        Assert.Contains("data-ref-baja", lista);
        Assert.Contains("aria-label=", lista);
    }

    [Fact]
    public void Controller_ExigeAntiforgeryYPermisoDeEdicionEnLasEscrituras()
    {
        var controller = Leer("Controllers", "ClienteReferenciaController.cs");

        foreach (var accion in new[] { "Crear", "Editar", "Verificar", "Baja" })
        {
            var inicio = controller.IndexOf($"Task<IActionResult> {accion}(", StringComparison.Ordinal);
            Assert.True(inicio > 0, accion);
            var atributos = controller.Substring(Math.Max(0, inicio - 260), Math.Min(260, inicio));
            Assert.Contains("[HttpPost]", atributos);
            Assert.Contains("[ValidateAntiForgeryToken]", atributos);
            Assert.Contains("Accion = \"edit\"", atributos);
        }

        Assert.Contains("[PermisoRequerido(Modulo = \"clientes\", Accion = \"view\")]", controller);
    }

    [Fact]
    public void Script_ManejaEscapeYDevuelveElFoco()
    {
        var js = Leer("wwwroot", "js", "cliente-referencias.js");

        Assert.Contains("'Escape'", js);
        Assert.Contains("origenFoco", js);
        Assert.Contains("RequestVerificationToken", js);
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

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
