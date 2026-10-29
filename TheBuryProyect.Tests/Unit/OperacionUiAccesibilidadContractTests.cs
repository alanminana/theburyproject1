using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Hito 7 K-UX: contratos de las correcciones de accesibilidad/operación de baja complejidad
/// (h1 de pantalla, labels asociados, teclado de los buscadores, error visible del modal de devolución).
/// </summary>
public class OperacionUiAccesibilidadContractTests
{
    [Theory]
    [InlineData("Cliente", "Index_tw.cshtml")]
    [InlineData("OrdenCompra", "Index_tw.cshtml")]
    [InlineData("Proveedor", "Index_tw.cshtml")]
    public void IndexView_TieneUnH1(string carpeta, string archivo)
    {
        var view = Leer("Views", carpeta, archivo);

        Assert.Single(Regex.Matches(view, "<h1\\b"));
    }

    [Fact]
    public void CajaRegistrarMovimiento_LabelsAsociadosAlControl()
    {
        var view = Leer("Views", "Caja", "RegistrarMovimiento_tw.cshtml");

        // El select tiene id propio (select-concepto): el label no puede apuntar a "Concepto".
        Assert.Contains("<label for=\"select-concepto\"", view);
        Assert.Contains("id=\"select-concepto\"", view);
        // El grupo de botones se nombra con aria-labelledby, no con un <label> sin control.
        Assert.Contains("aria-labelledby=\"tipo-toggle-label\"", view);
    }

    [Fact]
    public void CatalogoCrearProducto_LabelsConFor()
    {
        var view = Leer("Views", "Catalogo", "Index_tw.cshtml");

        Assert.Contains("<label for=\"prod-create-Codigo\"", view);
        Assert.Contains("id=\"prod-create-Codigo\"", view);
        Assert.Contains("<label for=\"modal-precioCompra\"", view);
    }

    [Fact]
    public void CotizadorBuscadores_EnterNoDisparaElSubmitDelWizard()
    {
        var cotizador = Leer("wwwroot", "js", "cotizacion-simulador.js");
        var wizard = Leer("wwwroot", "js", "venta-page-wizard.js");

        Assert.Contains("bindTecladoBuscador(els.productoBuscar, els.productosDropdown)", cotizador);
        Assert.Contains("bindTecladoBuscador(els.clienteBuscar, els.clientesDropdown)", cotizador);
        // Enter sobre un <input> del cotizador embebido se neutraliza en el handler del wizard.
        Assert.Contains("if (event.target instanceof HTMLInputElement) event.preventDefault();", wizard);
    }

    [Fact]
    public void VentaCreateBuscadores_TienenTeclado()
    {
        var js = Leer("wwwroot", "js", "venta-create.js");

        Assert.Contains("bindTecladoBuscador(inputBuscarCliente, dropdownClientes)", js);
        Assert.Contains("bindTecladoBuscador(inputBuscarProducto, dropdownProductos)", js);
    }

    [Fact]
    public void DevolucionModal_ErrorVisibleYControlesEtiquetados()
    {
        var js = Leer("wwwroot", "js", "venta-devolucion-modal.js");
        var view = Leer("Views", "Shared", "_VentaDevolucionModal.cshtml");

        Assert.Contains("errorBox.scrollIntoView", js);
        Assert.Contains("aria-label=\"Cantidad a devolver de", js);
        Assert.Contains("id=\"devolucion-modal-error\" role=\"alert\"", view);
    }

    private static string Leer(params string[] partes)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            current = current.Parent;
        Assert.NotNull(current);
        return File.ReadAllText(Path.Combine(new[] { current!.FullName }.Concat(partes).ToArray()));
    }
}
