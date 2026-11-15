using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Referencia de un cliente (personal, laboral o comercial).
    /// Las referencias en estado Verificada (no dadas de baja) cuentan contra el mínimo configurable para otorgar crédito
    /// personal (ClienteAptitudService); no cambian el límite de crédito.
    /// Baja lógica vía <see cref="AuditableEntity.IsDeleted"/>.
    /// </summary>
    public class ClienteReferencia : AuditableEntity
    {
        public int ClienteId { get; set; }

        public TipoReferenciaCliente Tipo { get; set; } = TipoReferenciaCliente.Personal;

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Parentesco, vínculo o cargo de la persona referida.</summary>
        [StringLength(100)]
        public string? Relacion { get; set; }

        [Required]
        [StringLength(30)]
        public string Telefono { get; set; } = string.Empty;

        /// <summary>Teléfono reducido a dígitos canónicos; sirve para detectar duplicadas.</summary>
        [Required]
        [StringLength(30)]
        public string TelefonoNormalizado { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Email { get; set; }

        /// <summary>Empresa (referencias laborales y comerciales).</summary>
        [StringLength(150)]
        public string? Empresa { get; set; }

        public EstadoVerificacionReferencia EstadoVerificacion { get; set; } = EstadoVerificacionReferencia.Pendiente;

        public DateTime? FechaVerificacion { get; set; }

        [StringLength(100)]
        public string? VerificadaPor { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public virtual Cliente Cliente { get; set; } = null!;
    }
}
