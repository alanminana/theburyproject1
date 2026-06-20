using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Variación de una publicación de Mercado Libre (color, talle, etc.).
    /// Se importan en modo lectura; las actualizaciones de stock deben hacerse
    /// por variación y nunca con PUT destructivos sobre el array completo.
    /// </summary>
    public class MercadoLibreListingVariation : AuditableEntity
    {
        public int ListingId { get; set; }

        /// <summary>
        /// Id de la variación en Mercado Libre.
        /// </summary>
        public long VariationId { get; set; }

        public decimal Precio { get; set; }

        public int AvailableQuantity { get; set; }

        public int SoldQuantity { get; set; }

        [StringLength(100)]
        public string? SellerSku { get; set; }

        /// <summary>
        /// Producto interno vinculado a esta variación. Null = puede usar el
        /// producto de la publicación solo si la regla de sync lo permite.
        /// </summary>
        public int? ProductoId { get; set; }

        /// <summary>
        /// Override de origen de stock para esta variación.
        /// Null = usa el origen de la publicación, o el global si la publicación no define override.
        /// </summary>
        public MercadoLibreOrigenStock? OrigenStockOverride { get; set; }

        /// <summary>
        /// Unidad física concreta que representa esta variación cuando el origen
        /// efectivo es UnidadFisicaEspecifica.
        /// </summary>
        public int? ProductoUnidadId { get; set; }

        /// <summary>
        /// attribute_combinations crudos en JSON (ej: Color=Rojo, Talle=M).
        /// </summary>
        public string? AttributesJson { get; set; }

        public virtual MercadoLibreListing Listing { get; set; } = null!;
        public virtual Producto? Producto { get; set; }
        public virtual ProductoUnidad? ProductoUnidad { get; set; }
    }
}
