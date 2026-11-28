namespace TheBuryProject.Tests.Infrastructure;

/// <summary>Código fuente de Catalogo/Index_tw compuesto con los parciales de cada sección, para los contratos de UI.</summary>
public static class CatalogoVista
{
    private static readonly string[] Archivos =
    {
        "Index_tw.cshtml",
        "_Productos.cshtml", "_ProductosResultados.cshtml", "_ProductoFila.cshtml",
        "_ProductoDialogos.cshtml", "_ProductoPaneles.cshtml",
        "_Categorias.cshtml", "_Marcas.cshtml", "_EntidadDialogo.cshtml"
    };

    public static string Leer()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz != null && !File.Exists(Path.Combine(raiz.FullName, "TheBuryProyect.csproj")))
            raiz = raiz.Parent;
        if (raiz == null)
            throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
        return string.Join("\n", Archivos.Select(a => File.ReadAllText(Path.Combine(raiz.FullName, "Views", "Catalogo", a))));
    }
}
