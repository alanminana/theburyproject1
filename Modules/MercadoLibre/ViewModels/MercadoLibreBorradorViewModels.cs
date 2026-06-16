using System.ComponentModel.DataAnnotations;
using TheBuryProject.Modules.MercadoLibre.Entities;

namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>Fila de la grilla de borradores de publicación.</summary>
    public class MercadoLibreBorradorListViewModel
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int Stock { get; set; }
        public string? CategoryIdMl { get; set; }
        public string? CategoryNombre { get; set; }
        public MercadoLibreBorradorEstado Estado { get; set; }
        public bool FueValidado => FechaValidacionUtc.HasValue;
        public DateTime? FechaValidacionUtc { get; set; }
        public bool PublicadoEnSimulacion { get; set; }
        public DateTime? FechaSimulacionUtc { get; set; }
        public string? PublicadoItemId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Edición/detalle de un borrador de publicación.</summary>
    public class MercadoLibreBorradorEditViewModel
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }

        [Required(ErrorMessage = "El título es obligatorio")]
        [StringLength(60, ErrorMessage = "Mercado Libre limita el título a 60 caracteres")]
        public string Titulo { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        [Range(0.01, 999999999, ErrorMessage = "El precio debe ser mayor a 0")]
        public decimal Precio { get; set; }

        [Range(0, 99999, ErrorMessage = "El stock no puede ser negativo")]
        public int Stock { get; set; }

        [StringLength(30)]
        public string? CategoryIdMl { get; set; }

        // Snapshot de la categoría resuelto por el picker (hidden en el form).
        [StringLength(200)]
        public string? CategoryNombre { get; set; }

        [StringLength(500)]
        public string? CategoryPathFromRoot { get; set; }

        public bool? CategoryEsHoja { get; set; }

        [Required]
        [StringLength(20)]
        public string Condicion { get; set; } = "new";

        [StringLength(30)]
        public string ListingTypeId { get; set; } = "gold_special";

        [StringLength(200)]
        public string? Garantia { get; set; }

        // Solo lectura (estado y contexto)
        public MercadoLibreBorradorEstado Estado { get; set; }
        public string? ErroresValidacion { get; set; }
        public DateTime? FechaValidacionUtc { get; set; }
        public string? PublicadoItemId { get; set; }
        public DateTime? FechaPublicadoUtc { get; set; }
        public bool PublicadoEnSimulacion { get; set; }
        public DateTime? FechaSimulacionUtc { get; set; }
        public string? PayloadSimuladoJson { get; set; }

        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public decimal ProductoPrecioVenta { get; set; }
        public decimal ProductoStockActual { get; set; }
        public bool ProductoRequiereNumeroSerie { get; set; }

        public bool ModoSimulacion { get; set; }
        public bool PermitirPublicacionDesdeErp { get; set; }

        /// <summary>True si hay una cuenta ML conectada (requerida para publicación real).</summary>
        public bool CuentaConectada { get; set; }

        public bool PuedeEditar => Estado is MercadoLibreBorradorEstado.Borrador or MercadoLibreBorradorEstado.Validado;
        public bool PuedeValidar => PuedeEditar;
        public bool PuedeSimular => Estado == MercadoLibreBorradorEstado.Validado && ModoSimulacion;
        public bool PuedePublicarReal => Estado == MercadoLibreBorradorEstado.Validado && PermitirPublicacionDesdeErp && !ModoSimulacion;
        public bool PuedePublicar => PuedeSimular || PuedePublicarReal;
        public bool PuedeDescartar => Estado != MercadoLibreBorradorEstado.Publicado;
    }
}
