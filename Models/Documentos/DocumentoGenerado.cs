using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Documento emitido. Es un registro histórico: guarda el contenido ya renderizado y el snapshot
    /// de datos usados, así que ni editar la plantilla ni cambiar el cliente/precios lo altera.
    /// Reimprimir usa siempre este contenido; regenerar crea un documento nuevo que reemplaza
    /// explícitamente al anterior.
    /// </summary>
    public class DocumentoGenerado : AuditableEntity
    {
        public int TipoDocumentoId { get; set; }
        public int PlantillaDocumentoId { get; set; }
        public int PlantillaDocumentoVersionId { get; set; }

        [Required, StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        // Relaciones de la operación (todas opcionales: depende del evento).
        public int? ClienteId { get; set; }
        public int? VentaId { get; set; }
        public int? CreditoId { get; set; }
        public int? CuotaId { get; set; }
        public int? PagoCuotaId { get; set; }
        public int? CotizacionId { get; set; }

        [Required, StringLength(50)]
        public string EventoOrigen { get; set; } = string.Empty;

        public int? ReglaDocumentoId { get; set; }

        /// <summary>evento|ancla|regla|plantilla — evita duplicados por reintentos/doble clic.</summary>
        [Required, StringLength(200)]
        public string ClaveIdempotencia { get; set; } = string.Empty;

        /// <summary>Documentos que se imprimen juntos (mismo evento/operación).</summary>
        public Guid? GrupoImpresionId { get; set; }

        public EstadoDocumentoGenerado Estado { get; set; } = EstadoDocumentoGenerado.Generado;

        public DateTime FechaGeneracionUtc { get; set; } = DateTime.UtcNow;

        [Required, StringLength(100)]
        public string UsuarioGeneracion { get; set; } = string.Empty;

        [Required]
        public string ContenidoRenderizado { get; set; } = string.Empty;

        [Required]
        public string DatosSnapshotJson { get; set; } = "{}";

        public string? MetadataJson { get; set; }

        [StringLength(128)]
        public string? ContentHash { get; set; }

        public bool RequiereFirma { get; set; }

        [StringLength(200)]
        public string? FirmantesRequeridos { get; set; }

        /// <summary>Firmas registradas: JSON [{rol, firmante, fechaUtc, usuario}].</summary>
        public string? FirmasJson { get; set; }

        public bool ExigeFirmaParaContinuar { get; set; }

        public int? ReemplazaADocumentoId { get; set; }
        public int? ReemplazadoPorDocumentoId { get; set; }

        [StringLength(100)]
        public string? CanceladoPor { get; set; }
        public DateTime? FechaCancelacionUtc { get; set; }
        [StringLength(500)]
        public string? MotivoCancelacion { get; set; }

        public int ContadorReimpresiones { get; set; }
        public DateTime? UltimaReimpresionUtc { get; set; }

        /// <summary>Id del ContratoVentaCredito legado del que se migró este documento (null si es nuevo).</summary>
        public int? ContratoLegadoId { get; set; }

        public virtual TipoDocumento TipoDocumento { get; set; } = null!;
        public virtual PlantillaDocumento PlantillaDocumento { get; set; } = null!;
        public virtual PlantillaDocumentoVersion PlantillaDocumentoVersion { get; set; } = null!;
        public virtual Venta? Venta { get; set; }
        public virtual Credito? Credito { get; set; }
        public virtual Cliente? Cliente { get; set; }
    }
}
