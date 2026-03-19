using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registro de evaluaci�n crediticia autom�tica
    /// </summary>
    public class EvaluacionCredito  : AuditableEntity
    {
        public int CreditoId { get; set; }
        public int ClienteId { get; set; }

        public ResultadoEvaluacion Resultado { get; set; }

        public decimal PuntajeRiesgoCliente { get; set; }
        public decimal MontoSolicitado { get; set; }
        public decimal? SueldoCliente { get; set; }
        public decimal? RelacionCuotaIngreso { get; set; } // % que representa la cuota del sueldo

        // Flags de cumplimiento
        public bool TieneDocumentacionCompleta { get; set; }
        public bool TieneIngresosSuficientes { get; set; }
        public bool TieneBuenHistorial { get; set; }
        public bool TieneGarante { get; set; }

        // Puntuaci�n calculada
        public decimal PuntajeFinal { get; set; } // 0-100

        [StringLength(1000)]
        public string? Motivo { get; set; } // Explicaci�n del resultado

        [StringLength(2000)]
        public string? Observaciones { get; set; }

        public DateTime FechaEvaluacion { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Credito Credito { get; set; } = null!;
        public virtual Cliente Cliente { get; set; } = null!;
    }
}