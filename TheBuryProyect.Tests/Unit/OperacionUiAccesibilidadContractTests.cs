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

    [Theory]
    [InlineData("modal-precioCompraSinIva")]
    [InlineData("modal-ivaCompraImporte")]
    [InlineData("modal-costoRealFinal")]
    [InlineData("modal-precioFinal")]
    [InlineData("modal-ventaSinIva")]
    [InlineData("modal-ivaVentaImporte")]
    [InlineData("precio-valor")]
    public void Catalogo_CamposCalculadosYFiltroDePrecios_TienenLabelConFor(string id)
    {
        var view = Leer("Views", "Catalogo", "Index_tw.cshtml");

        Assert.Contains($"<label for=\"{id}\"", view);
    }

    [Theory]
    [InlineData("modal-caract-nombre")]
    [InlineData("modal-caract-valor")]
    [InlineData("precio-categoria-id")]
    [InlineData("precio-marca-id")]
    [InlineData("mov-fecha-desde")]
    [InlineData("mov-fecha-hasta")]
    [InlineData("mov-page-size")]
    public void Catalogo_ControlesSinLabelVisible_TienenAriaLabel(string id)
    {
        var view = Leer("Views", "Catalogo", "Index_tw.cshtml");

        Assert.Matches($"id=\"{id}\" aria-label=\"[^\"]+\"", view);
    }

    [Fact]
    public void MarcaModalCodigoNombre_LabelsApuntanAlIdGeneradoPorElPartial()
    {
        var view = Leer("Views", "Catalogo", "_MarcaModalCodigoNombre.cshtml");

        Assert.Contains("<label for=\"@(p)-codigo\"", view);
        Assert.Contains("id=\"@(p)-codigo\"", view);
        Assert.Contains("<label for=\"@(p)-nombre\"", view);
        Assert.Contains("id=\"@(p)-nombre\"", view);
    }

    [Fact]
    public void ClienteDetails_InputDeArchivoOcultoTieneNombreAccesible()
    {
        var view = Leer("Views", "Cliente", "Details_tw.cshtml");

        Assert.Contains("<label class=\"label\" for=\"modalArchivoInput\">", view);
        Assert.Matches("id=\"modalArchivoInput\" aria-label=\"[^\"]+\"", view);
    }

    [Fact]
    public void DevolucionIndex_EstadoYMontoNuncaSeOcultan_ColumnasSecundariasPorBreakpoint()
    {
        var view = Leer("Views", "Devolucion", "Index.cshtml");
        var css = Leer("wwwroot", "css", "devolucion-module.css");

        Assert.Contains("devolucion-module.css", view);
        Assert.Contains("dev-col-motivo", view);
        Assert.Contains("dev-col-venta", view);
        Assert.Contains("dev-col-impacto", view);
        // Estado y Monto son las columnas que no pueden quedar fuera de vista: sin clase de ocultamiento.
        Assert.DoesNotContain("dev-col-venta px-4 py-2.5 text-center sm:px-5\">Estado", view);
        Assert.Matches(@"<th class=""px-4 py-2.5 text-center sm:px-5"">Estado</th>\s*<th class=""px-4 py-2.5 text-right sm:px-5"">Monto</th>", view);
        // El panel Radar se apila debajo de la tabla hasta 1536px.
        Assert.Contains("@media (min-width: 1536px)", css);
        Assert.DoesNotContain("xl:grid-cols-[minmax(0,1.7fr)", view);
    }

    [Fact]
    public void MoraFichaCliente_OpcionDeAlertaMuestraElMontoPrimero()
    {
        var view = Leer("Views", "Mora", "FichaCliente.cshtml");

        // Un <option> nativo se trunca a la derecha en mobile: el monto va primero para que siempre se lea.
        Assert.DoesNotContain("@NombreTipo(a.Tipo) · @a.FechaAlerta.ToString(\"dd/MM/yyyy\") · @a.MontoVencido", view);
        Assert.Equal(2, Regex.Matches(view, @"<option value=""@a\.Id"">@a\.MontoVencido\.ToString\(""C2""\) · @NombreTipo").Count);
    }

    [Fact]
    public void VentaDetails_AvisoDeScrollHorizontalVisibleComoEnOrdenCompra()
    {
        var view = Leer("Views", "Venta", "Details_tw.cshtml");

        Assert.Matches(@"data-oc-scroll-hint hidden\s+class=""[^""]*border-slate-700[^""]*""", view);
        Assert.Contains("Scroll horizontal", view);
    }

    [Fact]
    public void DevolucionModal_EncabezadoCompactoEnMobile()
    {
        var view = Leer("Views", "Shared", "_VentaDevolucionModal.cshtml");
        var css = Leer("wwwroot", "css", "shared-components.css");

        Assert.Contains("devolucion-quick-modal__header", view);
        Assert.Contains("devolucion-quick-modal__chips", view);
        Assert.Contains("#modal-devolucion-venta .devolucion-quick-modal__chips", css);
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
