using TheBuryProject.Models.Enums;
using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// Consolida cálculo de mora, generación de alertas y auditoría
    /// </summary>
    /// <summary>Resultado de registrar un pago de acuerdo.</summary>
    public sealed record ResultadoPagoAcuerdo(string Concepto, decimal ImportePagado, bool AcuerdoCumplido, decimal? DeudaVencidaRestante = null);

    /// <summary>Deuda cobrable de una cuota original: capital pendiente y punitorio aplicado pendiente.</summary>
    public sealed record DeudaCobrableCuota(int CuotaId, int NumeroCuota, DateTime FechaVencimiento, decimal Capital, decimal Punitorio);

    /// <summary>Deuda vencida cobrable de un crédito: capital pendiente y punitorio aplicado pendiente de sus cuotas con deuda.</summary>
    public sealed record DeudaCobrableCredito(decimal Capital, decimal Punitorio, IReadOnlyList<DeudaCobrableCuota> Cuotas)
    {
        public decimal Total => Capital + Punitorio;
    }

    public interface IMoraService
    {
        #region Configuración
        
        Task<ConfiguracionMora> GetConfiguracionAsync();
        Task<ConfiguracionMora> UpdateConfiguracionAsync(ConfiguracionMoraViewModel viewModel);
        /// <param name="camposEnviados">Campos presentes en el formulario; sólo esos se aplican. Null aplica todos.</param>
        Task<ConfiguracionMora> UpdateConfiguracionExpandidaAsync(ConfiguracionMoraExpandidaViewModel viewModel, IEnumerable<string>? camposEnviados = null);
        
        #endregion

        #region Procesamiento de mora
        
        Task ProcesarMoraAsync(CancellationToken ct = default);
        
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
        /// Crea un acuerdo de pago para un crédito del cliente con los importes tomados de la deuda cobrable real: lo que
        /// venga en el modelo (deuda, mora, condonación) se ignora o se rechaza. Un acuerdo financia la deuda completa en
        /// cuotas: no condona, no descuenta y no reduce nada. Es la única forma de crear un acuerdo: la alerta se deriva en el
        /// servidor (puede no existir si la deuda es solo de punitorio aplicado).
        /// </summary>
        Task<int> CrearAcuerdoPagoParaCreditoAsync(CrearAcuerdoViewModel solicitud, string gestorId);
        
        /// <summary>
        /// Obtiene los acuerdos de pago de un cliente
        /// </summary>
        Task<List<AcuerdoPagoResumenViewModel>> GetAcuerdosPagoAsync(int clienteId);
        
        /// <summary>
        /// Obtiene el detalle de un acuerdo de pago
        /// </summary>
        Task<AcuerdoPago?> GetAcuerdoPagoDetalleAsync(int acuerdoId);

        /// <summary>
        /// Cambia el estado de un acuerdo del cliente: Borrador→Activo/Cancelado, Activo→Cumplido/Incumplido/Cancelado.
        /// Lanza InvalidOperationException ante una transición no permitida o un acuerdo ajeno al cliente.
        /// </summary>
        Task<bool> ExisteAcuerdoVigenteAsync(int creditoId);

        /// <summary>
        /// Deuda vencida del crédito que se puede cobrar hoy: por cada cuota en mora, capital pendiente más punitorio
        /// aplicado pendiente (la misma cifra que muestra el cobro de cuotas). Base de la deuda de un acuerdo nuevo.
        /// </summary>
        Task<decimal> CalcularDeudaCobrableCreditoAsync(int creditoId);

        /// <summary>La misma deuda cobrable de <see cref="CalcularDeudaCobrableCreditoAsync"/>, separada en capital y punitorio.</summary>
        Task<DeudaCobrableCredito> CalcularDesgloseDeudaCobrableCreditoAsync(int creditoId);

        /// <summary>
        /// Punitorio que el cálculo informa hoy pero que NO está aplicado (no es deuda exigible ni entra al acuerdo), sumado
        /// sobre las cuotas con deuda del crédito. Es solo informativo: nunca se aplica automáticamente.
        /// </summary>
        Task<decimal> CalcularPunitorioCalculadoSinAplicarAsync(int creditoId);

        /// <summary>
        /// Registra el próximo pago pendiente de un acuerdo activo (primero la entrega inicial, después las cuotas en
        /// orden). El dinero se imputa a las cuotas vencidas del crédito, de la más vieja a la más nueva, con el
        /// circuito de cobro de cuotas (caja, recibo, punitorio). Al completar todos los pagos el acuerdo pasa a
        /// Cumplido y se resuelve la alerta si ya no queda deuda vencida.
        /// </summary>
        Task<ResultadoPagoAcuerdo> RegistrarPagoAcuerdoAsync(int acuerdoId, int clienteId, string medioPago, string? comprobante, int? pasoEsperado = null);

        /// <summary>
        /// Marca incumplida la promesa de una alerta del cliente (limpia la promesa y escala la prioridad).
        /// Lanza InvalidOperationException si la alerta es ajena, está resuelta o no tiene una promesa vigente
        /// (por eso un reenvío del mismo POST no vuelve a escalar).
        /// </summary>
        Task<bool> MarcarPromesaIncumplidaDelClienteAsync(int alertaId, int clienteId, string? observaciones);

        /// <summary>Igual que <see cref="MarcarPromesaCumplidaAsync"/> pero validando que la alerta sea del cliente y tenga promesa.</summary>
        Task<bool> MarcarPromesaCumplidaDelClienteAsync(int alertaId, int clienteId);

        Task<bool> CambiarEstadoAcuerdoAsync(int acuerdoId, int clienteId, EstadoAcuerdo nuevoEstado, string? motivo = null);

        /// <summary>
        /// Marca Incumplido los acuerdos Activos con un pago (entrega inicial o cuota) impago pasada su gracia, con la fecha
        /// comercial de Argentina. Antes de decidir concilia los cobros que hubieran quedado sin reflejar. Corre a diario,
        /// independiente de <c>ProcesoAutomaticoActivo</c>. Devuelve cuántos acuerdos marcó.
        /// </summary>
        Task<int> VerificarIncumplimientoAcuerdosAsync(CancellationToken cancellationToken = default);
        
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