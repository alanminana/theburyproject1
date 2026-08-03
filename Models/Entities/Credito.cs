using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un cr�dito otorgado a un cliente
    /// </summary>
    public class Credito  : AuditableEntity
    {
        public int ClienteId { get; set; }

        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        public decimal MontoSolicitado { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal TasaInteres { get; set; } // Tasa mensual
        public int CantidadCuotas { get; set; }
        public decimal MontoCuota { get; set; }

        /// <summary>
        /// Anticipo propuesto por una cotización convertida (Cotizacion.Anticipo), precargado acá
        /// para no perderlo al llegar a ConfigurarVenta (mismo patrón que CantidadCuotas). Es una
        /// intención, no autoridad: ConfigurarVenta la usa solo como valor inicial del formulario;
        /// el anticipo real se valida y aplica server-side recién al confirmar el crédito.
        /// </summary>
        public decimal AnticipoPreseleccionado { get; set; }

        public decimal CFTEA { get; set; } // Costo Financiero Total Efectivo Anual
        public decimal TotalAPagar { get; set; }
        public decimal SaldoPendiente { get; set; }

        /// <summary>
        /// Método de cálculo usado al configurar este crédito
        /// </summary>
        public MetodoCalculoCredito? MetodoCalculoAplicado { get; set; }

        /// <summary>
        /// Fuente de configuración resultante.
        /// </summary>
        public FuenteConfiguracionCredito? FuenteConfiguracionAplicada { get; set; }

        /// <summary>
        /// ID del perfil de crédito aplicado (si se usó método UsarPerfil)
        /// </summary>
        public int? PerfilCreditoAplicadoId { get; set; }

        /// <summary>
        /// Nombre del perfil aplicado (snapshot para auditoría)
        /// </summary>
        [StringLength(100)]
        public string? PerfilCreditoAplicadoNombre { get; set; }

        /// <summary>
        /// Gastos administrativos aplicados al momento de la configuración
        /// </summary>
        public decimal GastosAdministrativos { get; set; }

        /// <summary>
        /// Tasa de interés mensual aplicada al momento de la configuración (auditoría)
        /// </summary>
        public decimal? TasaInteresAplicada { get; set; }

        /// <summary>
        /// Mínimo de cuotas permitido al momento de configuración
        /// </summary>
        public int? CuotasMinimasPermitidas { get; set; }

        /// <summary>
        /// Máximo de cuotas permitido al momento de configuración
        /// </summary>
        public int? CuotasMaximasPermitidas { get; set; }

        /// <summary>
        /// Fuente que determinó la restricción de cuotas efectiva: "Producto" o "Global".
        /// </summary>
        [StringLength(20)]
        public string? FuenteRestriccionCuotasSnap { get; set; }

        /// <summary>
        /// ID del producto que impuso el límite de cuotas más restrictivo (snapshot histórico, sin FK).
        /// </summary>
        public int? ProductoIdRestrictivoSnap { get; set; }

        /// <summary>
        /// Máximo de cuotas global (antes de aplicar restricción por producto).
        /// </summary>
        public int? MaxCuotasBaseSnap { get; set; }

        public EstadoCredito Estado { get; set; } = EstadoCredito.Solicitado;

        public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
        public DateTime? FechaAprobacion { get; set; }
        public DateTime? FechaFinalizacion { get; set; }
        public DateTime? FechaPrimeraCuota { get; set; }

        public decimal PuntajeRiesgoInicial { get; set; }

        /// <summary>
        /// Micro-lote 6 (F2): decisión tomada en la configuración del crédito de cobrar la primera
        /// cuota al confirmar la venta. Se persiste acá (no en la UI ni en el payload de confirmación)
        /// y el servidor la revalida contra la base al momento de confirmar.
        /// </summary>
        public bool CobrarPrimeraCuotaSolicitada { get; set; }

        /// <summary>
        /// Medio de pago elegido para el cobro inmediato de la primera cuota (snapshot de la decisión).
        /// Nulo si no se solicitó el cobro. Se valida como habilitado en la ejecución.
        /// </summary>
        [StringLength(30)]
        public string? MedioPagoPrimeraCuota { get; set; }

        // Garante (opcional)
        public int? GaranteId { get; set; }
        public bool RequiereGarante { get; set; } = false;

        // Datos de aprobaci�n
        [StringLength(100)]
        public string? AprobadoPor { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        // Navigation Properties
        public virtual Cliente Cliente { get; set; } = null!;
        public virtual Garante? Garante { get; set; }
        public virtual PerfilCredito? PerfilCreditoAplicado { get; set; }
        public virtual ICollection<Cuota> Cuotas { get; set; } = new List<Cuota>();
    }
}