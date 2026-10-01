using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// La auditoría es una pestaña de Seguridad (Index?tab=auditoria). La ruta histórica
/// /Seguridad/Auditoria se conserva solo para enlaces guardados: redirige conservando los filtros.
/// </summary>
public class SeguridadControllerAuditoriaRedirectTests
{
    private static SeguridadController CrearController() =>
        new(null!, null!, null!, null!, NullLogger<SeguridadController>.Instance);

    [Fact]
    public void Auditoria_RedirigeALaPestanaConLosFiltros()
    {
        var controller = CrearController();

        var result = controller.Auditoria("ana", "Ventas", "Crear", new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6), 3);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SeguridadController.Index), redirect.ActionName);
        Assert.Equal("auditoria", redirect.RouteValues!["tab"]);
        Assert.Equal("ana", redirect.RouteValues["usuario"]);
        Assert.Equal("Ventas", redirect.RouteValues["modulo"]);
        Assert.Equal("Crear", redirect.RouteValues["accion"]);
        Assert.Equal(new DateOnly(2026, 1, 5), redirect.RouteValues["desde"]);
        Assert.Equal(new DateOnly(2026, 2, 6), redirect.RouteValues["hasta"]);
        Assert.Equal(3, redirect.RouteValues["pagina"]);
    }

    [Fact]
    public void Auditoria_SinFiltros_RedirigeALaPestanaPrimeraPagina()
    {
        var controller = CrearController();

        var result = controller.Auditoria(null, null, null, null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("auditoria", redirect.RouteValues!["tab"]);
        Assert.Equal(1, redirect.RouteValues["pagina"]);
    }
}
