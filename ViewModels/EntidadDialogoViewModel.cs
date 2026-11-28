using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos del diálogo de alta y edición de Categorías y Marcas (Views/Catalogo/_EntidadDialogo.cshtml).
    /// </summary>
    public class EntidadDialogoViewModel
    {
        /// <summary>"categoria" o "marca": prefijo de ids y configuración que toma catalogo-entidad-modal.js.</summary>
        public required string Entidad { get; init; }

        /// <summary>Todas las vigentes: la edición debe poder mostrar el padre actual, sea del nivel que sea.</summary>
        public required IEnumerable<SelectListItem> Padres { get; init; }

        /// <summary>Solo categorías.</summary>
        public IEnumerable<AlicuotaIVAFormItem> Alicuotas { get; init; } = Enumerable.Empty<AlicuotaIVAFormItem>();
    }
}
