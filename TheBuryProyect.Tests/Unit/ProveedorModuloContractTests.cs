using System.Reflection;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contratos del módulo Proveedor (Index, Details y drawers de alta/edición). Protegen defectos reales
/// encontrados en el cierre del módulo: acciones de escritura sin permiso propio, y edición que borraba
/// categorías, marcas o productos del proveedor porque el formulario no los enviaba.
/// </summary>
public class ProveedorModuloContractTests
{
    [Theory]
    [InlineData(nameof(ProveedorController.CreateAjax), "create")]
    [InlineData(nameof(ProveedorController.EditAjax), "update")]
    [InlineData(nameof(ProveedorController.GetEditData), "update")]
    [InlineData(nameof(ProveedorController.DeleteConfirmed), "delete")]
    public void AccionesDeEscrituraExigenSuPermisoPropio(string accion, string permisoEsperado)
    {
        var metodo = typeof(ProveedorController).GetMethod(accion);
        Assert.NotNull(metodo);

        var permisos = metodo!.GetCustomAttributes<PermisoRequeridoAttribute>()
            .Where(a => a.Modulo == "proveedores")
            .Select(a => a.Accion)
            .ToList();

        Assert.Contains(permisoEsperado, permisos);
    }

    [Fact]
    public void ElPermisoDeClaseSigueSiendoView()
    {
        var permisosClase = typeof(ProveedorController).GetCustomAttributes<PermisoRequeridoAttribute>()
            .Select(a => (a.Modulo, a.Accion))
            .ToList();

        Assert.Contains(("proveedores", "view"), permisosClase);
    }

    [Fact]
    public void ElDrawerEnviaProductosCategoriasYMarcas()
    {
        // ProveedorService.UpdateAsync reemplaza TODAS las asociaciones con lo que llega: si el formulario no
        // envía alguna de las tres, se borra al guardar (antes pasaba con categorías y marcas en el Index).
        var campos = Leer("Views", "Proveedor", "_ProveedorFormFields.cshtml");

        Assert.Contains("data-form-name=\"ProductosSeleccionados\"", campos);
        Assert.Contains("name=\"CategoriasSeleccionadas\"", campos);
        Assert.Contains("name=\"MarcasSeleccionadas\"", campos);

        var editarJs = Leer("wwwroot", "js", "proveedor-editar-modal.js");
        Assert.Contains("checkGroup('CategoriasSeleccionadas'", editarJs);
        Assert.Contains("checkGroup('MarcasSeleccionadas'", editarJs);
        Assert.Contains("_picker.preload(", editarJs);
    }

    [Fact]
    public void IndexYDetailsUsanElMismoDrawerDeEdicion()
    {
        // Details tenía una copia propia del modal (sin el picker de productos): editar desde ahí dejaba al
        // proveedor sin productos. Un único markup evita que vuelvan a divergir.
        var index = Leer("Views", "Proveedor", "Index_tw.cshtml");
        var details = Leer("Views", "Proveedor", "Details_tw.cshtml");

        Assert.Contains("<partial name=\"_ProveedorModales\"", index);
        Assert.Contains("<partial name=\"_ProveedorModales\"", details);
        Assert.DoesNotContain("id=\"modal-editar-proveedor\"", details);
        Assert.DoesNotContain("id=\"modal-editar-proveedor\"", index);

        // Details inicializa el picker; sin él el drawer no puede precargar los productos.
        var modulo = Leer("wwwroot", "js", "proveedor-module.js");
        var initDetails = modulo[modulo.IndexOf("function initDetails", StringComparison.Ordinal)..];
        Assert.Contains("ProveedorProductPicker.init()", initDetails[..initDetails.IndexOf("window.TheBury.ProveedorModule", StringComparison.Ordinal)]);
    }

    [Fact]
    public void LasAccionesDeEscrituraDeLasVistasRespetanLosPermisos()
    {
        var index = Leer("Views", "Proveedor", "Index_tw.cshtml");
        var details = Leer("Views", "Proveedor", "Details_tw.cshtml");

        foreach (var permiso in new[] { "create", "update", "delete" })
        {
            Assert.Contains($"User.TienePermiso(\"proveedores\", \"{permiso}\")", index);
        }
        Assert.Contains("User.TienePermiso(\"proveedores\", \"update\")", details);

        Assert.True(index.IndexOf("@if (puedeCrear", StringComparison.Ordinal)
                    < index.IndexOf("data-proveedor-modal=\"create\"", StringComparison.Ordinal));
        Assert.True(index.IndexOf("@if (puedeEditar)", StringComparison.Ordinal)
                    < index.IndexOf("data-proveedor-modal=\"edit\"", StringComparison.Ordinal));
        Assert.True(index.IndexOf("@if (puedeEliminar)", StringComparison.Ordinal)
                    < index.IndexOf("data-proveedor-delete-form", StringComparison.Ordinal));
        Assert.True(details.IndexOf("@if (puedeEditar)", StringComparison.Ordinal)
                    < details.IndexOf("data-proveedor-modal=\"edit\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LosCamposDelFormularioTienenLabelAsociadoPorFor()
    {
        var campos = Leer("Views", "Proveedor", "_ProveedorFormFields.cshtml");

        foreach (var campo in new[] { "Cuit", "RazonSocial", "NombreFantasia", "Contacto", "Email", "Telefono", "Direccion", "Ciudad", "Provincia", "CodigoPostal", "Aclaraciones" })
        {
            Assert.Contains($"<label for=\"@(p)-{campo}\"", campos);
        }
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
