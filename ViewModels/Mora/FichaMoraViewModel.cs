using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Mora
{
    /// <summary>
    /// ViewModel para la ficha completa de mora de un cliente
    /// </summary>
    public class FichaMoraViewModel
    {
        // Datos del cliente
        public int ClienteId { get; set; }
        public string NombreCliente { get; set; } = string.Empty;
        public string DocumentoCliente { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? TelefonoLaboral { get; set; }
        public string? Email { get; set; }
        public string? Direccion { get; set; }
        
        // Resumen de mora
        public ResumenMoraViewModel Resumen { get; set; } = new();
        
        // Créditos en mora
        public List<CreditoMoraViewModel> Creditos { get; set; } = new();
        
        // Alertas activas
        public List<AlertaCobranzaViewModel> AlertasActivas { get; set; } = new();
        
        // Historial de contactos
        public List<HistorialContactoViewModel> HistorialContactos { get; set; } = new();
        
        // Promesas de pago activas
        public List<PromesaPagoViewModel> PromesasPago { get; set; } = new();
        
        // Acuerdos de pago
        public List<AcuerdoPagoResumenViewModel> Acuerdos { get; set; } = new();
    }

    /// <summary>
    /// Resumen de mora del cliente
    /// </summary>
    public class ResumenMoraViewModel
    {
        public int TotalCreditosConMora { get; set; }
        public int TotalCuotasVencidas { get; set; }
        public int DiasMaxAtraso { get; set; }
        public decimal MontoCapitalVencido { get; set; }
        public decimal MontoMoraAcumulada { get; set; }
        public decimal MontoTotal => MontoCapitalVencido + MontoMoraAcumulada;
        public PrioridadAlerta PrioridadMaxima { get; set; }
        public EstadoGestionCobranza EstadoGestion { get; set; }
        public int ContactosRealizados { get; set; }
        public DateTime? UltimoContacto { get; set; }
        public DateTime? ProximoContacto { get; set; }
        public bool PromesaActiva { get; set; }
        public bool AcuerdoActivo { get; set; }
    }

    /// <summary>
    /// Crédito con mora
    /// </summary>
    public class CreditoMoraViewModel
    {
        public int CreditoId { get; set; }
        public string NumeroCredito { get; set; } = string.Empty;
        public DateTime FechaOtorgamiento { get; set; }
        public decimal MontoOriginal { get; set; }
        public decimal SaldoActual { get; set; }
        public int TotalCuotas { get; set; }
        public int CuotasPagadas { get; set; }
        public int CuotasVencidas { get; set; }
        public int DiasAtraso { get; set; }
        public decimal MontoCuotasVencidas { get; set; }
        public decimal MontoMora { get; set; }
        public List<CuotaVencidaViewModel> CuotasDetalle { get; set; } = new();
    }

    /// <summary>
    /// Cuota vencida
    /// </summary>
    public class CuotaVencidaViewModel
    {
        public int CuotaId { get; set; }
        public int NumeroCuota { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoCapital { get; set; }
        public decimal MontoInteres { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoMora { get; set; }
        public int DiasAtraso { get; set; }
    }

    /// <summary>
    /// Historial de contacto
    /// </summary>
    public class HistorialContactoViewModel
    {
        public int Id { get; set; }
        public DateTime FechaContacto { get; set; }
        public TipoContacto TipoContacto { get; set; }
        public ResultadoContacto Resultado { get; set; }
        public string? Observaciones { get; set; }
        public string? GestorNombre { get; set; }
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public int? DuracionMinutos { get; set; }
        public DateTime? FechaPromesaPago { get; set; }
        public decimal? MontoPromesaPago { get; set; }
        public DateTime? ProximoContacto { get; set; }
        
        public string TipoContactoNombre => TipoContacto switch
        {
            TipoContacto.LlamadaTelefonica => "Llamada Telefónica",
            TipoContacto.WhatsApp => "WhatsApp",
            TipoContacto.Email => "Email",
            TipoContacto.VisitaPresencial => "Visita Presencial",
            TipoContacto.SMS => "SMS",
            TipoContacto.NotaInterna => "Nota Interna",
            _ => "Otro"
        };

        public string ResultadoNombre => Resultado switch
        {
            ResultadoContacto.ContactoExitoso => "Contacto Exitoso",
            ResultadoContacto.NoContesta => "No Contesta",
            ResultadoContacto.NumeroEquivocado => "Número Equivocado",
            ResultadoContacto.PromesaPago => "Promesa de Pago",
            ResultadoContacto.RechazaPagar => "Rechaza Pagar",
            ResultadoContacto.SolicitaAcuerdo => "Solicita Acuerdo",
            ResultadoContacto.MensajeDejado => "Mensaje Dejado",
            ResultadoContacto.PromesaIncumplida => "Promesa Incumplida",
            ResultadoContacto.PagoRealizado => "Pago Realizado",
            _ => "Otro"
        };

        public string ResultadoColor => Resultado switch
        {
            ResultadoContacto.ContactoExitoso => "success",
            ResultadoContacto.PromesaPago => "primary",
            ResultadoContacto.PagoRealizado => "success",
            ResultadoContacto.NoContesta => "warning",
            ResultadoContacto.NumeroEquivocado => "danger",
            ResultadoContacto.RechazaPagar => "danger",
            ResultadoContacto.PromesaIncumplida => "danger",
            _ => "secondary"
        };
    }

    /// <summary>
    /// Promesa de pago
    /// </summary>
    public class PromesaPagoViewModel
    {
        public int AlertaId { get; set; }
        public DateTime FechaPromesa { get; set; }
        public decimal MontoPrometido { get; set; }
        public DateTime FechaRegistro { get; set; }
        public bool Vencida => FechaPromesa.Date < DateTime.Today;
        public int DiasRestantes => (FechaPromesa.Date - DateTime.Today).Days;
        public string EstadoPromesa => Vencida ? "Vencida" : $"{DiasRestantes} días restantes";
    }

    /// <summary>
    /// Resumen de acuerdo de pago
    /// </summary>
    public class AcuerdoPagoResumenViewModel
    {
        public int AcuerdoId { get; set; }
        public string NumeroAcuerdo { get; set; } = string.Empty;
        public DateTime FechaCreacion { get; set; }
        public EstadoAcuerdo Estado { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoCondonado { get; set; }
        public int TotalCuotas { get; set; }
        public int CuotasPagadas { get; set; }
        public DateTime? ProximaFechaVencimiento { get; set; }
        
        public string EstadoNombre => Estado switch
        {
            EstadoAcuerdo.Borrador => "Borrador",
            EstadoAcuerdo.Activo => "Activo",
            EstadoAcuerdo.Cumplido => "Cumplido",
            EstadoAcuerdo.Incumplido => "Incumplido",
            EstadoAcuerdo.Cancelado => "Cancelado",
            _ => "Desconocido"
        };
        
        public string EstadoColor => Estado switch
        {
            EstadoAcuerdo.Activo => "primary",
            EstadoAcuerdo.Cumplido => "success",
            EstadoAcuerdo.Incumplido => "danger",
            EstadoAcuerdo.Cancelado => "secondary",
            _ => "warning"
        };
    }
}
