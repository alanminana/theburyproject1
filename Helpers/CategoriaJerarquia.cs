using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers;

/// <summary>Orden jerárquico de categorías para el listado y el combo «Categoría padre» de Inventario.</summary>
public static class CategoriaJerarquia
{
    /// <summary>
    /// Raíz, sus hijas, siguiente raíz… Una categoría cuyo padre no figura en el listado se trata como raíz para no
    /// perderla. Se recorre con un conjunto de visitadas por si algún dato heredado trajera un ciclo.
    /// </summary>
    public static IReadOnlyList<(CategoriaViewModel Cat, int Nivel)> Ordenar(IEnumerable<CategoriaViewModel> categorias)
    {
        var listado = categorias.ToList();
        var ids = listado.Select(c => c.Id).ToHashSet();
        var hijasPorPadre = listado
            .Where(c => c.ParentId.HasValue && ids.Contains(c.ParentId.Value))
            .ToLookup(c => c.ParentId!.Value);
        var ordenadas = new List<(CategoriaViewModel Cat, int Nivel)>();
        var visitadas = new HashSet<int>();

        void Agregar(CategoriaViewModel categoria, int nivel)
        {
            if (!visitadas.Add(categoria.Id)) return;
            ordenadas.Add((categoria, nivel));
            foreach (var hija in hijasPorPadre[categoria.Id].OrderBy(h => h.Nombre, StringComparer.CurrentCultureIgnoreCase))
                Agregar(hija, nivel + 1);
        }

        foreach (var raiz in listado
                     .Where(c => !c.ParentId.HasValue || !ids.Contains(c.ParentId.Value))
                     .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
            Agregar(raiz, 0);
        foreach (var restante in listado.Where(c => !visitadas.Contains(c.Id)))
            Agregar(restante, 0);

        return ordenadas;
    }

    /// <summary>
    /// Opciones de «Categoría padre» de los diálogos Nueva/Editar: todas las vigentes (activas o no). El diálogo de edición
    /// oculta la propia categoría desde JS; el servidor sigue validando ciclos.
    /// </summary>
    public static List<SelectListItem> Opciones(IEnumerable<CategoriaViewModel> categorias) =>
        Ordenar(categorias)
            .Select(x => new SelectListItem
            {
                Value = x.Cat.Id.ToString(),
                Text = (x.Nivel > 0 ? "— " : string.Empty) + x.Cat.Nombre + (x.Cat.Activo ? string.Empty : " (inactiva)")
            })
            .ToList();
}
