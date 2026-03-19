using System.ComponentModel.DataAnnotations;
using System.Globalization;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class VentaViewModel
    {
        public int Id { get; set; }

        /// <summary>
        /// RowVersion para control de concurrencia optimista.
        /// Debe enviarse en POST para detectar conflictos.
        /// </summary>
        public byte[]? RowVersion { get; set; }

        [Display(Name = "Número")]
        public string Numero { get; set; } = string.Empty;

        [Display(Name = "Cliente")]
        [Required(ErrorMessage = "El cliente es requerido")]
        public int ClienteId { get; set; }

        public string ClienteNombre { get; set; } = string.Empty;
        public string ClienteDocumento { get; set; } = string.Empty;

        [Display(Name = "Fecha de Venta")]
        [Required]
        [DataType(DataType.Date)]
        public DateTime FechaVenta { get; set; } = DateTime.UtcNow;

        [Display(Name = "Estado")]
        public EstadoVenta Estado { get; set; } = EstadoVenta.Cotizacion;

        [Display(Name = "Tipo de Pago")]
        [Required(ErrorMessage = "El tipo de pago es requerido")]
        public TipoPago TipoPago { get; set; } = TipoPago.Efectivo;

        [Display(Name = "Subtotal")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Subtotal { get; set; }

        [Display(Name = "Descuento")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Descuento { get; set; }

        [Display(Name = "IVA")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal IVA { get; set; }

        [Display(Name = "Total")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Total { get; set; }

        [Display(Name = "Crédito Personal")]
        public int? CreditoId { get; set; }
        public string? CreditoNumero { get; set; }

        public int? AperturaCajaId { get; set; }

        [Display(Name = "Vendedor")]
        public string? VendedorUserId { get; set; }

        // Autorización
        [Display(Name = "Estado Autorización")]
        public EstadoAutorizacionVenta EstadoAutorizacion { get; set; } = EstadoAutorizacionVenta.NoRequiere;

        public bool RequiereAutorizacion { get; set; } = false;
        public string? UsuarioSolicita { get; set; }
        public DateTime? FechaSolicitudAutorizacion { get; set; }
        public string? UsuarioAutoriza { get; set; }
        public DateTime? FechaAutorizacion { get; set; }
        public string? MotivoAutorizacion { get; set; }
        public string? MotivoRechazo { get; set; }

        [Display(Name = "Vendedor")]
        [StringLength(200)]
        public string? VendedorNombre { get; set; }

        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        [StringLength(500)]
        public string? Observaciones { get; set; }

        public DateTime? FechaConfirmacion { get; set; }
        public DateTime? FechaFacturacion { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaCancelacion { get; set; }
        public string? MotivoCancelacion { get; set; }

        [Display(Name = "Detalles de la Venta")]
        public List<VentaDetalleViewModel> Detalles { get; set; } = new List<VentaDetalleViewModel>();

        public List<FacturaViewModel> Facturas { get; set; } = new List<FacturaViewModel>();

        // Datos adicionales según tipo de pago
        public DatosTarjetaViewModel? DatosTarjeta { get; set; }
        public DatosChequeViewModel? DatosCheque { get; set; }
        public DatosCreditoPersonallViewModel? DatosCreditoPersonall { get; set; }  // NUEVO

        // Datos de financiamiento
        [Display(Name = "Venta financiada")]
        public bool EsFinanciada { get; set; }

        [Display(Name = "Anticipo"), DataType(DataType.Currency)]
        [Range(0, double.MaxValue, ErrorMessage = "El anticipo no puede ser negativo")]
        public decimal? Anticipo { get; set; }

        [Display(Name = "Tasa mensual (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0% y 100%")]
        public decimal? TasaInteresMensualFinanciacion { get; set; }

        [Display(Name = "Cantidad de cuotas")]
        [Range(1, 120, ErrorMessage = "Las cuotas deben estar entre 1 y 120")]
        public int? CantidadCuotasFinanciacion { get; set; }

        [Display(Name = "Monto financiado estimado"), DataType(DataType.Currency)]
        public decimal? MontoFinanciadoEstimado { get; set; }

        [Display(Name = "Cuota estimada"), DataType(DataType.Currency)]
        public decimal? CuotaEstimada { get; set; }

        [Display(Name = "Ingreso neto declarado"), DataType(DataType.Currency)]
        public decimal? IngresoNetoDeclarado { get; set; }

        [Display(Name = "Otras deudas declaradas"), DataType(DataType.Currency)]
        public decimal? EndeudamientoDeclarado { get; set; }

        [Display(Name = "Antigüedad laboral (meses)")]
        public int? AntiguedadLaboralMeses { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Resultado de validación para ventas con crédito personal
        /// </summary>
        public ValidacionVentaResult? ValidacionCredito { get; set; }

        /// <summary>
        /// Permite forzar excepción documental en create cuando el usuario tiene permiso de autorización.
        /// </summary>
        public bool AplicarExcepcionDocumental { get; set; }

        /// <summary>
        /// Motivo obligatorio para la excepción documental en create.
        /// </summary>
        [StringLength(500)]
        public string? MotivoExcepcionDocumentalCreate { get; set; }

        #region Presentación

        public string EstadoDisplay => Estado switch
        {
            EstadoVenta.Cotizacion => "Cotización",
            EstadoVenta.Presupuesto => "Presupuesto",
            EstadoVenta.Confirmada => "Confirmada",
            EstadoVenta.Facturada => "Facturada",
            EstadoVenta.Entregada => "Entregada",
            EstadoVenta.Cancelada => "Cancelada",
            EstadoVenta.PendienteRequisitos => "Pendiente Requisitos",
            EstadoVenta.PendienteFinanciacion => "Pendiente Financiación",
            _ => Estado.ToString()
        };

        public string EstadoBadgeClass => Estado switch
        {
            EstadoVenta.Cotizacion => "badge bg-secondary",
            EstadoVenta.Presupuesto => "badge bg-info text-dark",
            EstadoVenta.Confirmada => "badge bg-primary",
            EstadoVenta.Facturada => "badge bg-success",
            EstadoVenta.Entregada => "badge bg-dark text-light",
            EstadoVenta.Cancelada => "badge bg-danger",
            EstadoVenta.PendienteRequisitos => "badge bg-warning text-dark",
            EstadoVenta.PendienteFinanciacion => "badge bg-info",
            _ => "badge bg-secondary"
        };

        public string EstadoAutorizacionDisplay => EstadoAutorizacion switch
        {
            EstadoAutorizacionVenta.NoRequiere => "No Requiere",
            EstadoAutorizacionVenta.PendienteAutorizacion => "Pendiente",
            EstadoAutorizacionVenta.Autorizada => "Autorizada",
            EstadoAutorizacionVenta.Rechazada => "Rechazada",
            _ => EstadoAutorizacion.ToString()
        };

        public string EstadoAutorizacionBadgeClass => EstadoAutorizacion switch
        {
            EstadoAutorizacionVenta.NoRequiere => "badge bg-dark text-light",
            EstadoAutorizacionVenta.PendienteAutorizacion => "badge bg-warning text-dark",
            EstadoAutorizacionVenta.Autorizada => "badge bg-success",
            EstadoAutorizacionVenta.Rechazada => "badge bg-danger",
            _ => "badge bg-secondary"
        };

        public string EstadoAutorizacionIconClass => EstadoAutorizacion switch
        {
            EstadoAutorizacionVenta.PendienteAutorizacion => "bi bi-hourglass-split",
            EstadoAutorizacionVenta.Autorizada => "bi bi-check-circle",
            EstadoAutorizacionVenta.Rechazada => "bi bi-x-circle",
            _ => string.Empty
        };

        #endregion

        #region Estado del Crédito (para ventas con TipoPago = CreditoPersonal)

        /// <summary>
        /// Estado del crédito asociado a la venta.
        /// Se usa para determinar botones en la vista.
        /// </summary>
        public EstadoCredito? CreditoEstado { get; set; }

        /// <summary>
        /// Fecha en que se configuró el financiamiento.
        /// Se usa como flag persistente para evitar redireccionamientos repetidos.
        /// </summary>
        public DateTime? FechaConfiguracionCredito { get; set; }

        /// <summary>
        /// Indica si el financiamiento ya fue configurado (flag persistente).
        /// </summary>
        public bool FinanciamientoConfigurado => FechaConfiguracionCredito.HasValue;

        /// <summary>
        /// Indica si el crédito está pendiente de configuración del plan
        /// </summary>
        public bool CreditoPendienteConfiguracion =>
            TipoPago == TipoPago.CreditoPersonal &&
            CreditoId.HasValue &&
            CreditoEstado == EstadoCredito.PendienteConfiguracion &&
            !FinanciamientoConfigurado; // Si ya está configurado, no mostrar como pendiente

        /// <summary>
        /// Indica si el crédito ya fue configurado y está listo para confirmar.
        /// Usa tanto el estado del crédito como el flag persistente.
        /// </summary>
        public bool CreditoConfigurado =>
            TipoPago == TipoPago.CreditoPersonal &&
            CreditoId.HasValue &&
            (CreditoEstado == EstadoCredito.Configurado || FinanciamientoConfigurado);

        /// <summary>
        /// Indica si el crédito ya fue generado (cuotas creadas)
        /// </summary>
        public bool CreditoGenerado =>
            TipoPago == TipoPago.CreditoPersonal &&
            CreditoId.HasValue &&
            (CreditoEstado == EstadoCredito.Generado ||
             CreditoEstado == EstadoCredito.Activo ||
             CreditoEstado == EstadoCredito.Finalizado);

        #endregion

        #region Permisos de acción

        public bool PuedeEditar => Estado == EstadoVenta.Cotizacion || 
                                   Estado == EstadoVenta.Presupuesto || 
                                   Estado == EstadoVenta.PendienteRequisitos ||
                                   Estado == EstadoVenta.PendienteFinanciacion;

        /// <summary>
        /// Para ventas con crédito personal:
        /// - PendienteFinanciacion: NO puede confirmar (debe configurar primero)
        /// - Con financiación configurada (FechaConfiguracionCredito): PUEDE confirmar
        /// Para ventas sin crédito, usa la lógica original.
        /// </summary>
        public bool PuedeConfirmar
        {
            get
            {
                // No se puede confirmar si está en PendienteFinanciacion
                if (Estado == EstadoVenta.PendienteFinanciacion)
                    return false;

                var estadoValido = Estado == EstadoVenta.Presupuesto || Estado == EstadoVenta.PendienteRequisitos;
                var autorizacionOk = !RequiereAutorizacion || EstadoAutorizacion == EstadoAutorizacionVenta.Autorizada;

                if (TipoPago == TipoPago.CreditoPersonal)
                {
                    // Solo puede confirmar si crédito está Configurado
                    return estadoValido && autorizacionOk && CreditoConfigurado;
                }

                return estadoValido && autorizacionOk;
            }
        }

        /// <summary>
        /// Indica si la venta está en estado PendienteFinanciacion (crédito personal sin configurar)
        /// </summary>
        public bool EsPendienteFinanciacion => Estado == EstadoVenta.PendienteFinanciacion;

        /// <summary>
        /// Muestra botón "Configurar Crédito" si:
        /// - Es crédito personal Y
        /// - Está en PendienteFinanciacion O tiene crédito PendienteConfiguracion
        /// </summary>
        public bool PuedeConfigurarCredito =>
            TipoPago == TipoPago.CreditoPersonal &&
            CreditoId.HasValue &&
            (Estado == EstadoVenta.PendienteFinanciacion || CreditoPendienteConfiguracion);

        public bool PuedeFacturar =>
            Estado == EstadoVenta.Confirmada && (!RequiereAutorizacion || EstadoAutorizacion == EstadoAutorizacionVenta.Autorizada);

        public bool PuedeCancelar => Estado != EstadoVenta.Cancelada;

        public bool PuedeAutorizar => EstadoAutorizacion == EstadoAutorizacionVenta.PendienteAutorizacion;

        public bool PuedeCrearDevolucion =>
            Estado == EstadoVenta.Confirmada || Estado == EstadoVenta.Facturada || Estado == EstadoVenta.Entregada;

        public bool DebeAlertarAutorizacionPendiente =>
            RequiereAutorizacion && EstadoAutorizacion == EstadoAutorizacionVenta.PendienteAutorizacion;

        public bool FueRechazada => EstadoAutorizacion == EstadoAutorizacionVenta.Rechazada;

        public bool TieneRequisitosPendientes => Estado == EstadoVenta.PendienteRequisitos;

        public bool TieneExcepcionDocumentalRegistrada => TryGetUltimaExcepcionDocumental(out _, out _, out _);

        public DateTime? FechaExcepcionDocumental
        {
            get
            {
                return TryGetUltimaExcepcionDocumental(out var fecha, out _, out _)
                    ? fecha
                    : null;
            }
        }

        public string? UsuarioExcepcionDocumental
        {
            get
            {
                return TryGetUltimaExcepcionDocumental(out _, out var usuario, out _)
                    ? usuario
                    : null;
            }
        }

        public string? MotivoExcepcionDocumental
        {
            get
            {
                return TryGetUltimaExcepcionDocumental(out _, out _, out var motivo)
                    ? motivo
                    : null;
            }
        }

        private bool TryGetUltimaExcepcionDocumental(out DateTime fecha, out string usuario, out string motivo)
        {
            fecha = default;
            usuario = string.Empty;
            motivo = string.Empty;

            if (string.IsNullOrWhiteSpace(MotivoAutorizacion))
            {
                return false;
            }

            var lineas = MotivoAutorizacion
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var linea in lineas.Reverse())
            {
                if (!linea.StartsWith("EXCEPCION_DOC|", StringComparison.Ordinal))
                {
                    continue;
                }

                var partes = linea.Split('|', 4, StringSplitOptions.None);
                if (partes.Length != 4)
                {
                    continue;
                }

                if (!DateTime.TryParse(
                        partes[1],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out fecha))
                {
                    continue;
                }

                usuario = partes[2];
                motivo = partes[3];
                return true;
            }

            return false;
        }

        #endregion
    }
}
