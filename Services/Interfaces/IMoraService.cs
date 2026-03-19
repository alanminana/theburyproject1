using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// Consolida cálculo de mora, generación de alertas y auditoría
    /// </summary>
    public interface IMoraService
    {
        #region Configuración
        
        Task<ConfiguracionMora> GetConfiguracionAsync();
        Task<ConfiguracionMora> UpdateConfiguracionAsync(ConfiguracionMoraViewModel viewModel);
        Task<ConfiguracionMora> UpdateConfiguracionExpandidaAsync(ConfiguracionMoraExpandidaViewModel viewModel);
        
        #endregion

        #region Procesamiento de mora
        
        Task ProcesarMoraAsync();
        
        #endregion
        
        #region Gestión de alertas
        
        Task<List<AlertaCobranzaViewModel>> GetAlertasActivasAsync();
        Task<List<AlertaCobranzaViewModel>> GetTodasAlertasAsync();
        Task<AlertaCobranzaViewModel?> GetAlertaByIdAsync(int id);
        Task<bool> ResolverAlertaAsync(int id, string? observaciones = null, byte[]? rowVersion = null);
        Task<bool> MarcarAlertaComoLeidaAsync(int id, byte[]? rowVersion = null);
        Task<List<AlertaCobranzaViewModel>> GetAlertasPorClienteAsync(int clienteId);
        
        #endregion

        #region Bandeja de clientes en mora
        
        /// <summary>
        /// Obtiene la lista paginada de clientes en mora con filtros
        /// </summary>
        Task<BandejaClientesMoraViewModel> GetClientesEnMoraAsync(FiltrosBandejaClientes filtros);
        
        /// <summary>
        /// Obtiene el conteo de clientes por prioridad
        /// </summary>
        Task<Dictionary<string, int>> GetConteoPorPrioridadAsync();
        
        #endregion

        #region Ficha de cliente
        
        /// <summary>
        /// Obtiene la ficha completa de mora de un cliente
        /// </summary>
        Task<FichaMoraViewModel?> GetFichaClienteAsync(int clienteId);
        
        /// <summary>
        /// Obtiene los créditos en mora de un cliente
        /// </summary>
        Task<List<CreditoMoraViewModel>> GetCreditosEnMoraAsync(int clienteId);
        
        #endregion

        #region Gestión de contactos
        
        /// <summary>
        /// Registra un nuevo contacto con cliente moroso
        /// </summary>
        Task<bool> RegistrarContactoAsync(RegistrarContactoViewModel contacto, string gestorId);
        
        /// <summary>
        /// Obtiene el historial de contactos de un cliente
        /// </summary>
        Task<List<HistorialContactoViewModel>> GetHistorialContactosAsync(int clienteId);
        
        #endregion

        #region Promesas de pago
        
        /// <summary>
        /// Registra una promesa de pago
        /// </summary>
        Task<bool> RegistrarPromesaPagoAsync(RegistrarPromesaViewModel promesa, string gestorId);
        
        /// <summary>
        /// Obtiene las promesas de pago activas de un cliente
        /// </summary>
        Task<List<PromesaPagoViewModel>> GetPromesasActivasAsync(int clienteId);
        
        /// <summary>
        /// Marca una promesa como cumplida
        /// </summary>
        Task<bool> MarcarPromesaCumplidaAsync(int alertaId);
        
        /// <summary>
        /// Marca una promesa como incumplida
        /// </summary>
        Task<bool> MarcarPromesaIncumplidaAsync(int alertaId, string? observaciones);
        
        #endregion

        #region Acuerdos de pago
        
        /// <summary>
        /// Crea un nuevo acuerdo de pago
        /// </summary>
        Task<int> CrearAcuerdoPagoAsync(CrearAcuerdoViewModel acuerdo, string gestorId);
        
        /// <summary>
        /// Obtiene los acuerdos de pago de un cliente
        /// </summary>
        Task<List<AcuerdoPagoResumenViewModel>> GetAcuerdosPagoAsync(int clienteId);
        
        /// <summary>
        /// Obtiene el detalle de un acuerdo de pago
        /// </summary>
        Task<AcuerdoPago?> GetAcuerdoPagoDetalleAsync(int acuerdoId);
        
        #endregion

        #region Logs y auditoría
        
        Task<List<LogMora>> GetLogsAsync(int cantidad = 50);
        
        #endregion
        
        #region Dashboard KPIs
        
        /// <summary>
        /// Obtiene los KPIs del dashboard de mora
        /// </summary>
        Task<DashboardMoraKPIs> GetDashboardKPIsAsync();
        
        #endregion
    }
    
    /// <summary>
    /// KPIs para el dashboard de mora
    /// </summary>
    public class DashboardMoraKPIs
    {
        // Clientes en mora
        public int TotalClientesMora { get; set; }
        public int ClientesSinGestion { get; set; }
        public int ClientesPrioridadCritica { get; set; }
        public int ClientesPrioridadAlta { get; set; }
        public int ClientesPrioridadMedia { get; set; }
        public int ClientesPrioridadBaja { get; set; }
        public decimal DiasPromedioAtraso { get; set; }
        
        // Montos
        public decimal MontoTotalVencido { get; set; }
        public decimal MoraTotal { get; set; }
        public decimal MontoTotal => MontoTotalVencido + MoraTotal;
        
        // Alertas
        public int AlertasActivas { get; set; }
        public int AlertasNoLeidas { get; set; }
        public int AlertasCriticas { get; set; }
        
        // Promesas
        public int PromesasActivas { get; set; }
        public int PromesasVencenHoy { get; set; }
        public decimal MontoPromesasHoy { get; set; }
        public int PromesasVencidas { get; set; }
        
        // Acuerdos
        public int AcuerdosActivos { get; set; }
        public decimal MontoAcuerdosActivos { get; set; }
        
        // Gestiones
        public int ContactosHoy { get; set; }
        public int ContactosSemana { get; set; }
        public int CobrosHoy { get; set; }
        public decimal MontoCobradoHoy { get; set; }
        
        // Métricas
        public decimal TasaRecuperoMes { get; set; }
        
        // Estado del proceso
        public DateTime? UltimaEjecucion { get; set; }
        public bool ProcesoActivo { get; set; }
        
        // Propiedades legacy para compatibilidad
        public int TotalClientesEnMora => TotalClientesMora;
        public int ClientesCriticos => ClientesPrioridadCritica;
        public int ClientesAlta => ClientesPrioridadAlta;
        public int ClientesMedia => ClientesPrioridadMedia;
        public int ClientesBaja => ClientesPrioridadBaja;
        public decimal MontoTotalMora => MoraTotal;
    }
}