using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Mora
{
    /// <summary>
    /// ViewModel para la bandeja de clientes en mora
    /// </summary>
    public class ClienteMoraViewModel
    {
        public int ClienteId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Documento { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        
        // Resumen de mora
        public int CreditosConMora { get; set; }
        public int CuotasVencidas { get; set; }
        public int DiasMaxAtraso { get; set; }
        public decimal MontoVencido { get; set; }
        public decimal MontoMora { get; set; }
        public decimal MontoTotal => MontoVencido + MontoMora;
        
        // Clasificación
        public PrioridadAlerta PrioridadMaxima { get; set; }
        public EstadoGestionCobranza EstadoGestion { get; set; }
        
        // Gestión
        public int AlertasActivas { get; set; }
        public DateTime? UltimoContacto { get; set; }
        public DateTime? ProximoContacto { get; set; }
        public bool TienePromesaActiva { get; set; }
        public DateTime? FechaPromesa { get; set; }
        public bool TieneAcuerdoActivo { get; set; }
        
        // Auditoría
        public int TotalContactos { get; set; }
        public DateTime? FechaUltimaAlerta { get; set; }
    }

    /// <summary>
    /// Filtros para la bandeja de clientes en mora
    /// </summary>
    public class FiltrosBandejaClientes
    {
        public PrioridadAlerta? Prioridad { get; set; }
        public EstadoGestionCobranza? EstadoGestion { get; set; }
        public int? DiasMinAtraso { get; set; }
        public int? DiasMaxAtraso { get; set; }
        public decimal? MontoMinVencido { get; set; }
        public decimal? MontoMaxVencido { get; set; }
        public bool? ConPromesaActiva { get; set; }
        public bool? ConAcuerdoActivo { get; set; }
        public bool? SinContactoReciente { get; set; }
        public int? DiasSinContacto { get; set; }
        public string? Busqueda { get; set; }
        public string? Ordenamiento { get; set; } = "PrioridadDesc";
        public int Pagina { get; set; } = 1;
        public int TamañoPagina { get; set; } = 20;
    }

    /// <summary>
    /// Resultado paginado de la bandeja de clientes
    /// </summary>
    public class BandejaClientesMoraViewModel
    {
        public List<ClienteMoraViewModel> Clientes { get; set; } = new();
        public FiltrosBandejaClientes Filtros { get; set; } = new();
        public int TotalClientes { get; set; }
        public int TotalPaginas => (int)Math.Ceiling(TotalClientes / (double)Filtros.TamañoPagina);
        
        // Resumen general
        public decimal MontoTotalVencido { get; set; }
        public decimal MontoTotalMora { get; set; }
        public int ClientesCriticos { get; set; }
        public int ClientesConPromesa { get; set; }
        public int ClientesConAcuerdo { get; set; }
    }
}
