using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Accesibilidad de las superficies tocadas en los tickets QA 27 y 29 (críticas A y B de Impeccable):
/// el modal "Editar producto" asocia cada etiqueta con su control, el modal de confirmación compartido
/// retiene y devuelve el foco, y el buscador de productos del proveedor se expone como combobox.
/// </summary>
public class ProductoEditarModalAccesibilidadContractTests
{
    [Fact]
    public void ElModalDeEdicionAsociaCadaEtiquetaConSuControl()
    {
        var vista = Leer("Views", "Catalogo", "Index_tw.cshtml");
        var inicio = vista.IndexOf("<form id=\"form-editar-producto\"", StringComparison.Ordinal);
        var fin = vista.IndexOf("</form>", inicio, StringComparison.Ordinal);
        Assert.True(inicio > 0 && fin > inicio);
        var modal = vista[inicio..fin];

        var conFor = Regex.Matches(modal, "<label for=\"prod-edit-[A-Za-z-]+\"").Count;
        Assert.True(conFor >= 20, $"Se esperaban al menos 20 etiquetas asociadas con for=, hay {conFor}.");

        // Controles sin etiqueta visible propia: nombre accesible explícito.
        Assert.Contains("id=\"prod-edit-activo\" name=\"Activo\" type=\"checkbox\" value=\"true\" aria-label=\"Producto activo\"", modal);
        Assert.Contains("aria-label=\"Nueva característica\"", modal);
        Assert.Contains("aria-label=\"Agregar característica\"", modal);
    }

    [Fact]
    public void ElModalDeConfirmacionCompartidoRetieneYDevuelveElFoco()
    {
        var js = Leer("wwwroot", "js", "shared-ui.js");

        Assert.Contains("lastFocused", js);          // devuelve el foco a quien lo abrió
        Assert.Contains("e.key !== 'Tab'", js);      // Tab queda dentro del diálogo
        Assert.Contains("options.tone === 'primary'", js); // confirmaciones que no son de peligro
        Assert.Contains("options.confirmLabel", js);
    }

    [Fact]
    public void ElBuscadorDeProductosDelProveedorEsUnComboboxConTeclado()
    {
        var js = Leer("wwwroot", "js", "proveedor-product-picker.js");

        Assert.Contains("'role', 'combobox'", js);
        Assert.Contains("'role', 'listbox'", js);
        Assert.Contains("'role', 'option'", js);
        Assert.Contains("aria-activedescendant", js);
        Assert.Contains("e.key === 'ArrowDown'", js);
        Assert.Contains("e.key === 'Enter'", js);
    }

    [Fact]
    public void LosImportesVisiblesDeLaOrdenYDelDesgloseUsanFormatoArgentino()
    {
        var orden = Leer("wwwroot", "js", "ordencompra-form.js");
        var desglose = Leer("wwwroot", "js", "producto-precio-calculo.js");

        Assert.Contains("toLocaleString('es-AR'", orden);
        Assert.Contains("toLocaleString('es-AR'", desglose);
        // Lo que viaja al servidor sigue con punto decimal.
        Assert.Contains("function numForPost", orden);
        Assert.Contains("function setHidden", desglose);
    }

    private static string Leer(params string[] segmentos)
    {
        var ruta = Path.Combine(new[] { FindRepoRoot() }.Concat(segmentos).ToArray());
        return File.ReadAllText(ruta);
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
