using System.Reflection;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contratos de la UI de Órdenes de compra (tickets QA 27 y 29): el avance de estado desde el listado
/// pide confirmación y exige su permiso en el servidor, y el formulario explica por qué el buscador
/// no ofrece un producto en vez de quedar vacío.
/// </summary>
public class OrdenCompraUiContractTests
{
    [Theory]
    [InlineData(nameof(OrdenCompraController.CambiarEstado), "update")]
    [InlineData(nameof(OrdenCompraController.Create), "create")]
    public void LasAccionesQueEscribenExigenSuPermisoEnElServidor(string accion, string permisoEsperado)
    {
        var metodos = typeof(OrdenCompraController).GetMethods().Where(m => m.Name == accion);

        var permisos = metodos
            .SelectMany(m => m.GetCustomAttributes<PermisoRequeridoAttribute>())
            .Where(a => a.Modulo == "ordenescompra")
            .Select(a => a.Accion)
            .ToList();

        Assert.Contains(permisoEsperado, permisos);
    }

    [Fact]
    public void AvanzarElEnvioDesdeElListadoPideConfirmacion()
    {
        var vista = Leer("Views", "OrdenCompra", "Index_tw.cshtml");
        var js = Leer("wwwroot", "js", "ordencompra-index.js");

        Assert.Contains("data-oc-confirm", vista);
        Assert.Contains("data-confirm-message", vista);
        Assert.Contains("form[data-oc-confirm]", js);
        Assert.Contains("confirmAction", js);
        // No destruye nada: confirmación en tono primario, con título y rótulo propios y el cambio de estado dicho.
        Assert.Contains("tone: 'primary'", js);
        Assert.Contains("data-confirm-title", vista);
        Assert.Contains("data-confirm-label", vista);
        Assert.Contains("pasa de @OrdenCompraUiHelper.EstadoNombre(o.Estado) a @OrdenCompraUiHelper.EstadoNombre(sig)", vista);
    }

    [Fact]
    public void ElFormularioExplicaLosBloqueosEntreProveedorYProducto()
    {
        var js = Leer("wwwroot", "js", "ordencompra-form.js");

        // Aviso fijo cuando ningún proveedor tiene el producto, y detalle de los no asociados en el buscador.
        Assert.Contains("sticky: true", js);
        Assert.Contains("Asociarlos desde el proveedor", js);
        Assert.Contains("noAsociados", js);
        // El aviso fijo ofrece un enlace para resolver el bloqueo y elegir el producto lleva a Cantidad.
        Assert.Contains("hrefLabel: 'Ir a Proveedores'", js);
        Assert.Contains("inpCantidad.focus()", js);
    }

    [Fact]
    public void AgregarProductoSeBloqueaCuandoNingunProveedorLoTieneYElListadoMarcaEntregasVencidas()
    {
        var js = Leer("wwwroot", "js", "ordencompra-form.js");
        var vista = Leer("Views", "OrdenCompra", "Index_tw.cshtml");

        Assert.Contains("sinProveedorParaProducto", js);
        Assert.Contains("aria-disabled", js);
        Assert.Contains("entregaVencida", vista);
        Assert.Contains("Vencida", vista);
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
