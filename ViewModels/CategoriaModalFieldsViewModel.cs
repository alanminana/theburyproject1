using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos para renderizar los campos compartidos de "Datos de la categoría" en los
    /// modales Nueva Categoría / Editar Categoría de Catalogo/Index_tw.cshtml.
    /// El único punto que varía entre alta y edición es el prefijo de <c>id</c> usado
    /// por categoria-crear-modal.js / categoria-editar-modal.js para leer/escribir los campos.
    /// </summary>
    public class CategoriaModalFieldsViewModel
    {
        public required string Prefix { get; set; }
        public SelectList? CategoriasFiltro { get; set; }
        public List<AlicuotaIVAFormItem>? AlicuotasIVADatos { get; set; }
    }
}
