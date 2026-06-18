using Microsoft.AspNetCore.Mvc.Razor;

namespace TheBuryProject.Modules.MercadoLibre;

/// <summary>
/// Repunta las vistas del <c>MercadoLibreController</c> al rework visual en
/// <c>/Views/MercadoLibre1/</c> sin tocar las ~20 llamadas a <c>View(...)</c>
/// del controller. Antepone la ubicación nueva: cada vista (y el layout/parciales
/// del módulo) se resuelve primero en MercadoLibre1; si faltara alguna, cae al
/// folder original <c>/Views/MercadoLibre/</c>. Reversible quitando el registro
/// en Program.cs.
/// </summary>
public sealed class MercadoLibre1ViewLocationExpander : IViewLocationExpander
{
    private const string Controller = "MercadoLibre";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        // Sin valores propios: la decisión depende solo del controller en runtime.
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (!string.Equals(context.ControllerName, Controller, StringComparison.Ordinal))
        {
            return viewLocations;
        }

        var rework = new[]
        {
            "/Views/MercadoLibre1/{0}.cshtml",
            "/Views/MercadoLibre1/Shared/{0}.cshtml",
        };

        return rework.Concat(viewLocations);
    }
}
