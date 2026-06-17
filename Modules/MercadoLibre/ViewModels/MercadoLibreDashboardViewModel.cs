namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>
    /// Severidad de una alerta operativa del dashboard ML.
    /// El orden numérico se usa para priorizar (Critical primero).
    /// </summary>
    public enum MercadoLibreAlertaSeveridad
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    /// <summary>
    /// Alerta operativa priorizada. Solo informa y enlaza a la pantalla
    /// donde se resuelve: el dashboard nunca ejecuta acciones por sí mismo.
    /// </summary>
    public class MercadoLibreDashboardAlerta
    {
        public MercadoLibreAlertaSeveridad Severidad { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Contador { get; set; }

        /// <summary>Acción del controller a la que enlaza (ej: "Listings").</summary>
        public string? LinkAccion { get; set; }

        /// <summary>Filtro opcional para la acción enlazada (ej: "sin-vincular").</summary>
        public string? LinkFiltro { get; set; }

        public string LinkTexto { get; set; } = "Ver";

        /// <summary>Prioridad de orden (menor = más arriba). Deriva de la severidad y el contador.</summary>
        public int Prioridad { get; set; }
    }

    /// <summary>Salud de la conexión y estado de simulación del canal.</summary>
    public class MercadoLibreConexionResumen
    {
        public bool ModuloConfigurado { get; set; }
        public bool CuentaConectada { get; set; }
        public string? Nickname { get; set; }
        public long? MeliUserId { get; set; }
        public string? SiteId { get; set; }

        /// <summary>Cerrojo interno de sync/precio/mensajes (no gobierna la publicación de borradores).</summary>
        public bool ModoSimulacion { get; set; }

        /// <summary>Permiso maestro de publicación real desde el ERP.</summary>
        public bool PermitirPublicacionDesdeErp { get; set; }

        public DateTime? UltimaImportacionUtc { get; set; }
        public DateTime? UltimaPruebaConexionUtc { get; set; }
        public bool? UltimaPruebaConexionOk { get; set; }
        public DateTime? AccessTokenExpiraUtc { get; set; }
        public DateTime? UltimoSyncUtc { get; set; }
        public DateTime? UltimoErrorUtc { get; set; }
        public string? UltimoErrorDetalle { get; set; }
    }

    /// <summary>KPIs del inventario de publicaciones.</summary>
    public class MercadoLibrePublicacionesResumen
    {
        public int Total { get; set; }
        public int Activas { get; set; }
        public int Pausadas { get; set; }
        public int Finalizadas { get; set; }
        public int OtroEstado { get; set; }
        public int ConVariaciones { get; set; }
        public int SinVincular { get; set; }
        public int ConError { get; set; }
        public int SinStock { get; set; }
        public int VariacionesSinVincular { get; set; }
    }

    /// <summary>
    /// Diferencias stock/precio ERP vs ML (indicador de sincronización).
    /// El cálculo usa el stock lógico del producto vinculado; el detalle
    /// exacto por origen vive en el preview de sync.
    /// </summary>
    public class MercadoLibreStockPrecioResumen
    {
        public int PublicacionesVinculadas { get; set; }
        public int ConDiferenciaStock { get; set; }
        public int ConDiferenciaPrecio { get; set; }
        public int StockMlMayorErp { get; set; }
        public int StockErpMayorMl { get; set; }
        public int UltimosSyncOk { get; set; }
        public int UltimosSyncFallidos { get; set; }
    }

    /// <summary>KPIs de órdenes y ventas generadas.</summary>
    public class MercadoLibreOrdenesResumen
    {
        public int Total { get; set; }
        public int Pendientes { get; set; }
        public int ConVentaCreada { get; set; }
        public int Liquidadas { get; set; }
        public int ConError { get; set; }
        public int PendientesUnidad { get; set; }
        public int Ignoradas { get; set; }
        public int VentasCreadas { get; set; }
        public int Ventas24h { get; set; }
        public int Ventas7d { get; set; }
        public int Ventas30d { get; set; }
        public decimal MontoBrutoTotal { get; set; }
    }

    /// <summary>KPIs de liquidación ML/MP.</summary>
    public class MercadoLibreLiquidacionesResumen
    {
        public int Pendientes { get; set; }
        public int Liquidadas { get; set; }
        public decimal NetoEstimadoPendiente { get; set; }
        public decimal NetoRealAcreditado { get; set; }
        public decimal DiferenciaEstimadoVsReal { get; set; }
    }

    /// <summary>KPIs logísticos derivados del estado de envío.</summary>
    public class MercadoLibreEnviosResumen
    {
        public int Pendientes { get; set; }
        public int ListosParaDespachar { get; set; }
        public int Despachados { get; set; }
        public int EnCamino { get; set; }
        public int Entregados { get; set; }
        public int Cancelados { get; set; }
        public int Demorados { get; set; }
        public int ConTracking { get; set; }
        public int SinTracking { get; set; }
    }

    /// <summary>KPIs de reclamos/devoluciones/garantías.</summary>
    public class MercadoLibreReclamosResumen
    {
        public int Abiertos { get; set; }
        public int PendientesRevision { get; set; }
        public int Resueltos { get; set; }
        public int ReingresosStock { get; set; }
        public int Danados { get; set; }
        public int Mermas { get; set; }
        public int Garantias { get; set; }
    }

    /// <summary>KPIs de lotes de aumento masivo de precios.</summary>
    public class MercadoLibreAumentosResumen
    {
        public int Total { get; set; }
        public int Simulados { get; set; }
        public int Aplicados { get; set; }
        public int Revertidos { get; set; }
        public int Cancelados { get; set; }
    }

    /// <summary>KPIs de preguntas preventa.</summary>
    public class MercadoLibrePreguntasResumen
    {
        public int Total { get; set; }
        public int Pendientes { get; set; }
        public int Respondidas { get; set; }
        public int ConError { get; set; }
        public int Simuladas { get; set; }
        public int SinPublicacion { get; set; }
    }

    /// <summary>KPIs de mensajes postventa.</summary>
    public class MercadoLibreMensajesResumen
    {
        public int Total { get; set; }
        public int RecibidosPendientes { get; set; }
        public int Enviados { get; set; }
        public int ConError { get; set; }
        public int Simulados { get; set; }
        public int SinOrden { get; set; }
    }

    /// <summary>KPIs del procesamiento de webhooks/background.</summary>
    public class MercadoLibreWebhooksResumen
    {
        public int Recibidos { get; set; }
        public int Procesados { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int ReintentosAgotados { get; set; }
        public int TopicItems { get; set; }
        public int TopicOrders { get; set; }
        public int TopicShipments { get; set; }
        public int TopicClaims { get; set; }
        public int TopicPreguntas { get; set; }
    }

    /// <summary>Fila compacta de orden reciente.</summary>
    public class MercadoLibreOrdenResumenRow
    {
        public int Id { get; set; }
        public long MeliOrderId { get; set; }
        public string EstadoInterno { get; set; } = string.Empty;
        public string EstadoEnvio { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string CurrencyId { get; set; } = "ARS";
        public DateTime FechaCreacionUtc { get; set; }
        public bool TieneVenta { get; set; }
    }

    /// <summary>Fila compacta de evento webhook reciente.</summary>
    public class MercadoLibreWebhookRow
    {
        public string Topic { get; set; } = string.Empty;
        public bool Procesado { get; set; }
        public int Intentos { get; set; }
        public DateTime RecibidoUtc { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>Fila compacta del log de sincronización reciente.</summary>
    public class MercadoLibreLogRow
    {
        public string Operacion { get; set; } = string.Empty;
        public bool Exito { get; set; }
        public string? Detalle { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>Fila compacta de lote de aumento reciente.</summary>
    public class MercadoLibreAumentoRow
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public int CantidadPublicaciones { get; set; }
        public bool AplicadoEnSimulacion { get; set; }
        public DateTime FechaSolicitud { get; set; }
    }

    /// <summary>
    /// ViewModel del Dashboard operativo de Mercado Libre (Fase 17).
    /// Es el centro de operación del canal dentro del ERP: solo lectura,
    /// armado por <c>IMercadoLibreDashboardService</c> sin llamar a la API real.
    /// </summary>
    public class MercadoLibreDashboardViewModel
    {
        public DateTime GeneradoUtc { get; set; } = DateTime.UtcNow;

        public MercadoLibreConexionResumen Conexion { get; set; } = new();
        public MercadoLibrePublicacionesResumen Publicaciones { get; set; } = new();
        public MercadoLibreStockPrecioResumen StockPrecio { get; set; } = new();
        public MercadoLibreOrdenesResumen Ordenes { get; set; } = new();
        public MercadoLibreLiquidacionesResumen Liquidaciones { get; set; } = new();
        public MercadoLibreEnviosResumen Envios { get; set; } = new();
        public MercadoLibreReclamosResumen Reclamos { get; set; } = new();
        public MercadoLibreAumentosResumen Aumentos { get; set; } = new();
        public MercadoLibrePreguntasResumen Preguntas { get; set; } = new();
        public MercadoLibreMensajesResumen Mensajes { get; set; } = new();
        public MercadoLibreWebhooksResumen Webhooks { get; set; } = new();

        public List<MercadoLibreDashboardAlerta> Alertas { get; set; } = new();

        public List<MercadoLibreOrdenResumenRow> UltimasOrdenes { get; set; } = new();
        public List<MercadoLibreAumentoRow> UltimosAumentos { get; set; } = new();
        public List<MercadoLibreWebhookRow> UltimosWebhooks { get; set; } = new();
        public List<MercadoLibreLogRow> UltimosLogs { get; set; } = new();

        public bool TieneAlertas => Alertas.Count > 0;
    }
}
