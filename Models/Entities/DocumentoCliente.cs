using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un documento adjunto de un cliente (DNI, recibos, servicios, etc.)
    /// </summary>
    public class DocumentoCliente  : AuditableEntity
    {
        public int ClienteId { get; set; }

        public TipoDocumentoCliente TipoDocumento { get; set; }

        [Required]
        [StringLength(200)]
        public string NombreArchivo { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string RutaArchivo { get; set; } = string.Empty; // Ruta f�sica del archivo

        [StringLength(100)]
        public string? TipoMIME { get; set; } // application/pdf, image/jpeg, etc.

        public long TamanoBytes { get; set; }

        public EstadoDocumento Estado { get; set; } = EstadoDocumento.Pendiente;

        public DateTime FechaSubida { get; set; } = DateTime.UtcNow;

        public DateTime? FechaVencimiento { get; set; } // Para docs que expiran (ej: recibo de sueldo)

        // Verificaci�n
        public DateTime? FechaVerificacion { get; set; }

        [StringLength(100)]
        public string? VerificadoPor { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [StringLength(500)]
        public string? MotivoRechazo { get; set; }

        // Navigation
        public virtual Cliente Cliente { get; set; } = null!;
    }
}