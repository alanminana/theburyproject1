using System.Reflection;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contratos de la gestión de Categorías dentro de Catálogo/Inventario (pestaña Categorías + modales
/// Nueva/Editar). Protegen regresiones reales encontradas en el cierre del módulo:
/// permisos por acción sin enforcement, categoría imposible de desactivar y pérdida de la pestaña
/// activa tras guardar o eliminar.
/// </summary>
public class CategoriaCatalogoContractTests
{
    [Theory]
    [InlineData(nameof(CategoriaController.CreateAjax), "create")]
    [InlineData(nameof(CategoriaController.EditAjax), "update")]
    [InlineData(nameof(CategoriaController.GetJson), "update")]
    [InlineData(nameof(CategoriaController.DeleteConfirmed), "delete")]
    public void AccionesDeEscrituraExigenSuPermisoPropio(string accion, string permisoEsperado)
    {
        var metodo = typeof(CategoriaController).GetMethod(accion);
        Assert.NotNull(metodo);

        var permisos = metodo!.GetCustomAttributes<PermisoRequeridoAttribute>()
            .Where(a => a.Modulo == "categorias")
            .Select(a => a.Accion)
            .ToList();

        Assert.Contains(permisoEsperado, permisos);
    }

    [Fact]
    public void ElPermisoDeClaseSigueSiendoView()
    {
        var permisosClase = typeof(CategoriaController).GetCustomAttributes<PermisoRequeridoAttribute>()
            .Select(a => (a.Modulo, a.Accion))
            .ToList();

        Assert.Contains(("categorias", "view"), permisosClase);
    }

    [Fact]
    public void ElSwitchActivoEnviaFalseExplicitoDespuesDelCheckbox()
    {
        // Un checkbox desmarcado no viaja en el form y CategoriaViewModel.Activo vale true por defecto:
        // sin el hidden "false" (siempre después del checkbox) una categoría no puede desactivarse.
        var partial = Leer("Views", "Catalogo", "_CategoriaModalFields.cshtml");

        var iCheckbox = partial.IndexOf("name=\"Activo\" id=\"@(p)-activo\" type=\"checkbox\"", StringComparison.Ordinal);
        var iHidden = partial.IndexOf("<input type=\"hidden\" name=\"Activo\" value=\"false\"", StringComparison.Ordinal);

        Assert.True(iCheckbox >= 0, "No se encontró el checkbox Activo del partial.");
        Assert.True(iHidden > iCheckbox, "El hidden Activo=false debe existir y estar después del checkbox.");
    }

    [Fact]
    public void LosCamposDelModalTienenLabelAsociadoPorFor()
    {
        var partial = Leer("Views", "Catalogo", "_CategoriaModalFields.cshtml");

        foreach (var campo in new[] { "codigo", "nombre", "descripcion", "parentId", "alicuotaIVAId" })
        {
            Assert.Contains($"<label for=\"@(p)-{campo}\"", partial);
        }
    }

    [Fact]
    public void ElAltaYLaEdicionVuelvenAlCatalogoEnLaPestanaCategorias()
    {
        // Sin esto, tras guardar/eliminar el usuario caía en la pestaña Productos.
        foreach (var archivo in new[] { "categoria-crear-modal.js", "categoria-editar-modal.js" })
        {
            var js = Leer("wwwroot", "js", archivo);
            Assert.Contains("/Catalogo?tab=categorias", js);
        }

        var editar = Leer("wwwroot", "js", "categoria-editar-modal.js");
        Assert.Contains("returnUrl=", editar);
        Assert.DoesNotContain("returnUrl=/Catalogo'", editar);
    }

    [Fact]
    public void LosBotonesDeCategoriaRespetanLosPermisosDeLaVista()
    {
        var vista = Leer("Views", "Catalogo", "Index_tw.cshtml");

        Assert.Contains("User.TienePermiso(\"categorias\", \"create\")", vista);
        Assert.Contains("User.TienePermiso(\"categorias\", \"update\")", vista);
        Assert.Contains("User.TienePermiso(\"categorias\", \"delete\")", vista);

        // Nueva categoría / Editar / Eliminar solo se renderizan bajo su permiso.
        Assert.True(vista.IndexOf("@if (puedeCrearCategoria)", StringComparison.Ordinal)
                    < vista.IndexOf("id=\"btn-crear-categoria\"", StringComparison.Ordinal));
        Assert.True(vista.IndexOf("@if (puedeEditarCategoria)", StringComparison.Ordinal)
                    < vista.IndexOf("data-cat-edit-id=\"@cat.Id\"", StringComparison.Ordinal));
        Assert.True(vista.IndexOf("@if (puedeEliminarCategoria)", StringComparison.Ordinal)
                    < vista.IndexOf("data-cat-delete-id=\"@cat.Id\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LasPestanasExponenSuEstadoActivoConAriaSelected()
    {
        // El estado activo lo define aria-selected (server-side y JS): antes el JS y Razor usaban juegos de
        // clases distintos y, al cambiar de pestaña con un clic, Productos quedaba resaltada junto a la nueva.
        var vista = Leer("Views", "Catalogo", "Index_tw.cshtml");
        var js = Leer("wwwroot", "js", "catalogo-index.js");

        Assert.Contains("role=\"tab\"", vista);
        Assert.Contains("aria-selected=\"@TabSelected(\"categorias\")\"", vista);
        Assert.Contains("setAttribute('aria-selected'", js);
        Assert.DoesNotContain("'bg-primary/10'", js);
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
