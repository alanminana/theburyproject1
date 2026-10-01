using System.Reflection;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// GET /Producto/GetJson devuelve costos, precios y configuración de crédito del producto. Antes lo leía
/// cualquier usuario autenticado (hallazgo de la QA de los tickets 27 y 29): ahora exige productos.view y
/// el Catálogo no ofrece el botón Editar —que depende de ese endpoint— a quien no tiene ese permiso.
/// </summary>
public class ProductoGetJsonPermisoContractTests
{
    [Fact]
    public void GetJsonExigeProductosView()
    {
        var metodo = typeof(ProductoController).GetMethod(nameof(ProductoController.GetJson));
        Assert.NotNull(metodo);

        var permisos = metodo!.GetCustomAttributes<PermisoRequeridoAttribute>()
            .Where(a => a.Modulo == "productos")
            .Select(a => a.Accion)
            .ToList();

        Assert.Contains("view", permisos);
    }

    [Fact]
    public void ElCatalogoSoloOfreceEditarConProductosView()
    {
        var catalogo = Leer("Views", "Catalogo", "Index_tw.cshtml");
        var detalleProveedor = Leer("Views", "Proveedor", "Details_tw.cshtml");

        var botonEditar = catalogo.IndexOf("data-prod-edit-id=\"@p.ProductoId\"", StringComparison.Ordinal);
        Assert.True(botonEditar > 0);
        var guarda = catalogo.LastIndexOf("@if (puedeVerProducto)", botonEditar, StringComparison.Ordinal);
        Assert.True(guarda > 0 && botonEditar - guarda < 200, "El botón Editar debe estar dentro de @if (puedeVerProducto).");

        Assert.Contains("User.TienePermiso(\"productos\", \"view\") && User.TienePermiso(\"productos\", \"edit\")", detalleProveedor);
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
