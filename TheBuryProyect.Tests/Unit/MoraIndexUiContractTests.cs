using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI de Views/Mora/Index.cshtml (MORA-VISTAS, micro-lote 1). Antes de este lote
/// MoraController.Index devolvía View() sin vista y /Mora respondía HTTP 500. Estos tests fallan
/// el build si la vista desaparece, si los enlaces que dependen de ella apuntan a acciones
/// inexistentes o si se pierde el gate de permisos de las acciones de escritura.
/// </summary>
public class MoraIndexUiContractTests
{
    private static string RutaVista(string nombre) =>
        Path.Combine(FindRepoRoot(), "Views", "Mora", nombre + ".cshtml");

    private static string LeerVista(string nombre) => File.ReadAllText(RutaVista(nombre));

    private static IEnumerable<MethodInfo> Acciones(string nombre) =>
        typeof(MoraController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == nombre);

    [Fact]
    public void Index_TieneVistaFisica()
    {
        Assert.True(File.Exists(RutaVista("Index")),
            "MoraController.Index devuelve View(MoraIndexViewModel): sin Views/Mora/Index.cshtml /Mora responde 500.");
    }

    [Fact]
    public void Index_UsaElViewModelQueElControllerEnvia()
    {
        var view = LeerVista("Index");

        Assert.Contains("@model TheBuryProject.ViewModels.MoraIndexViewModel", view);
        // KPIs e historial llegan por ViewBag (ver MoraController.Index).
        Assert.Contains("ViewBag.KPIs as DashboardMoraKPIs", view);
        Assert.Contains("ViewBag.HistorialEjecuciones", view);
    }

    [Fact]
    public void Index_ControllerMantieneGateDePermisoDeVista()
    {
        var index = Acciones("Index").Single();
        var permiso = index.GetCustomAttribute<PermisoRequeridoAttribute>();

        Assert.NotNull(permiso);
        Assert.Equal("mora", permiso!.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    [Fact]
    public void Index_LosEnlacesDeLaVistaApuntanAAccionesExistentesDelController()
    {
        var view = LeerVista("Index");
        var acciones = Regex.Matches(view, "asp-controller=\"Mora\"\\s+asp-action=\"(?<a>\\w+)\"")
            .Select(m => m.Groups["a"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(acciones);
        foreach (var accion in acciones)
            Assert.True(Acciones(accion).Any(), $"Index enlaza a Mora/{accion}, que no existe en MoraController.");
    }

    [Fact]
    public void ConfiguracionExpandida_EnlazaAIndex_YIndexTieneVista()
    {
        // Migas, "Volver" y "Cancelar" de la configuración apuntan a Mora/Index.
        var config = LeerVista("ConfiguracionExpandida");

        Assert.Contains("asp-controller=\"Mora\" asp-action=\"Index\"", config);
        Assert.True(File.Exists(RutaVista("Index")));
    }

    [Fact]
    public void Index_AccionesDeEscrituraSeMuestranSoloConPermisoYSonPost()
    {
        var view = LeerVista("Index");

        // Ejecutar el proceso escribe en la base: gate de UI + POST con antiforgery.
        Assert.Contains("User.TienePermiso(\"mora\", \"manage\")", view);
        Assert.Contains("asp-action=\"EjecutarJob\" method=\"post\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);

        // Configuración sólo con mora.config.
        Assert.Contains("User.TienePermiso(\"mora\", \"config\")", view);

        // Y el servidor sigue exigiendo el permiso, independientemente de la vista.
        var ejecutar = Acciones("EjecutarJob").Single();
        Assert.NotNull(ejecutar.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("manage", ejecutar.GetCustomAttribute<PermisoRequeridoAttribute>()!.Accion);
    }

    [Fact]
    public void Index_ContemplaEstadoDeErrorYEstadosVacios()
    {
        var view = LeerVista("Index");

        // El catch del controller deja ViewBag.KPIs en null: la vista no debe inventar ceros.
        Assert.Contains("cargaFallida", view);
        Assert.Contains("data-mora-estado=\"error\"", view);
        Assert.Contains("data-mora-estado=\"alertas-vacio\"", view);
        Assert.Contains("data-mora-estado=\"logs-vacio\"", view);
    }

    [Fact]
    public void Index_CargaElCssDeModuloQueExiste()
    {
        var view = LeerVista("Index");

        Assert.Contains("~/css/mora-module.css", view);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "wwwroot", "css", "mora-module.css")));
    }

    [Fact]
    public void Index_TablasUsanTarjetasEnTelefonoYNoSeDesbordan()
    {
        var view = LeerVista("Index");

        Assert.Equal(2, Regex.Matches(view, "<table data-erp-cards").Count);
        Assert.Contains("overflow-x-auto", view);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
