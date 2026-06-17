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

        /// <summary>
        /// Imágenes para Mercado Libre: una URL http/https por línea. Se normaliza
        /// y persiste como JSON en el borrador. ML exige al menos una para varios
        /// listing types (ej: free).
        /// </summary>
        public string? ImagenesUrls { get; set; }

        /// <summary>URLs ya normalizadas (solo lectura, para preview y vista detalle).</summary>
        public IReadOnlyList<string> Imagenes { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Atributos específicos de la categoría ML completados por el operador
        /// (bound del formulario dinámico; se serializan a AtributosCompletadosJson).
        /// </summary>
        public List<AtributoCompletadoVm> Atributos { get; set; } = new();

        /// <summary>
        /// Atributos a mostrar/exigir según la categoría elegida (solo lectura, render
        /// inicial server-side; el JS los recarga al cambiar la categoría).
        /// </summary>
        public IReadOnlyList<CatalogoAtributoVm> AtributosRequeridos { get; set; }
            = System.Array.Empty<CatalogoAtributoVm>();

        /// <summary>True si hay catálogo local de categorías importado (habilita el formulario dinámico).</summary>
        public bool CatalogoImportado { get; set; }

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

        /// <summary>Permiso maestro de seguridad: habilita la publicación real desde el ERP.</summary>
        public bool PermitirPublicacionDesdeErp { get; set; }

        /// <summary>True si hay una cuenta ML conectada (requerida para publicación real).</summary>
        public bool CuentaConectada { get; set; }

        public bool PuedeEditar => Estado is MercadoLibreBorradorEstado.Borrador or MercadoLibreBorradorEstado.Validado;
        public bool PuedeValidar => PuedeEditar;

        // La simulación es el comportamiento por defecto: todo borrador validado se
        // puede simular sin permiso ni cuenta. Ya no depende de ModoSimulacion.
        public bool PuedeSimular => Estado == MercadoLibreBorradorEstado.Validado;

        // La publicación REAL exige permiso maestro + cuenta conectada.
        public bool PuedePublicarReal => Estado == MercadoLibreBorradorEstado.Validado
            && PermitirPublicacionDesdeErp && CuentaConectada;

        // El botón principal queda habilitado al estar validado: por defecto simula,
        // y solo publica real si el operador marca "Publicación REAL" (y hay permiso/cuenta).
        public bool PuedePublicar => Estado == MercadoLibreBorradorEstado.Validado;
        public bool PuedeDescartar => Estado != MercadoLibreBorradorEstado.Publicado;
    }
}
