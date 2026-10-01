using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class AplicarCambioPrecioDirectoViewModel
    {
        [Required]
        public string Alcance { get; set; } = string.Empty; // "seleccionados" | "filtrados"

        [Required]
        public decimal ValorPorcentaje { get; set; }

        public string? ProductoIdsText { get; set; } // CSV de IDs si seleccionados

        public string? FiltrosJson { get; set; } // JSON si filtrados

        public int? ListaPrecioId { get; set; } // lista objetivo opcional (si null usa precio base)

        public string? Motivo { get; set; } // opcional

        /// <summary>"porcentaje" (predeterminado) o "montofijo": ValorPorcentaje se interpreta como % o como monto $.</summary>
        public string? TipoCambio { get; set; }

        /// <summary>"none" (predeterminado), "entero" (sin decimales) o "99" (terminar en .99).</summary>
        public string? Redondeo { get; set; }
    }
}
