using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Documentos;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>Referencias a la operación que originó el evento (basta con una ancla).</summary>
    public sealed class DocumentoOrigen
    {
        public int? VentaId { get; init; }
        public int? PagoCuotaId { get; init; }
        public int? CotizacionId { get; init; }

        /// <summary>
        /// Todos los pagos de una misma cobranza (un recibo con varias cuotas). <see cref="PagoCuotaId"/> es el pago ancla
        /// (el menor) y da la identidad e idempotencia del recibo; si es null el recibo cubre solo ese pago.
        /// </summary>
        public IReadOnlyList<int>? PagoCuotaIds { get; init; }
    }

    public sealed class DocumentoEventoRequest
    {
        public required string Evento { get; init; }
        public required DocumentoOrigen Origen { get; init; }

        /// <summary>Contexto ya armado por el llamador (ej. el contrato legado, que proyecta cuotas). Si es null se construye desde <see cref="Origen"/>.</summary>
        public DocumentoContexto? Contexto { get; init; }

        /// <summary>Números ya asignados por tipo (código de tipo → número), para convivir con la numeración legada de contratos.</summary>
        public IReadOnlyDictionary<string, string>? NumerosPreasignados { get; init; }

        /// <summary>Si se informa, todos los documentos del evento comparten este grupo de impresión (ej. cobro de varias cuotas).</summary>
        public Guid? GrupoImpresionId { get; init; }

        /// <summary>Usuario emisor; si es null se toma el usuario actual.</summary>
        public string? Usuario { get; init; }
    }

    public sealed class DocumentoEventoResultado
    {
        public List<DocumentoGenerado> Generados { get; } = new();

        /// <summary>Documentos que ya existían (reintento idempotente): no se duplican.</summary>
        public List<DocumentoGenerado> YaExistentes { get; } = new();

        public List<string> Advertencias { get; } = new();

        /// <summary>Errores de documentos NO obligatorios (recuperables). Los obligatorios lanzan <see cref="DocumentoObligatorioFallidoException"/>.</summary>
        public List<DocumentoErrorItem> Errores { get; } = new();

        public Guid? GrupoImpresionId { get; set; }

        public IEnumerable<DocumentoGenerado> Todos => Generados.Concat(YaExistentes);
        public bool GeneroAlgo => Generados.Count > 0;
    }

    public sealed class DocumentoFiltro
    {
        /// <summary>Busca por número de documento, nombre/apellido o documento del cliente.</summary>
        public string? Texto { get; set; }
        public int? TipoDocumentoId { get; set; }
        public EstadoDocumentoGenerado? Estado { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public int Pagina { get; set; } = 1;
        public int Tamano { get; set; } = 25;
    }

    public sealed class DocumentoPdfArchivo
    {
        public string NombreArchivo { get; init; } = string.Empty;
        public string TipoContenido { get; init; } = "application/pdf";
        public byte[] Contenido { get; init; } = Array.Empty<byte>();
    }

    public interface IDocumentoService
    {
        /// <summary>
        /// Evalúa las reglas activas del evento y genera los documentos que correspondan.
        /// Corre en la transacción del llamador: si hay un documento OBLIGATORIO que no puede
        /// generarse lanza <see cref="DocumentoObligatorioFallidoException"/> sin persistir nada;
        /// los no obligatorios fallidos se devuelven como errores recuperables. Idempotente.
        /// </summary>
        Task<DocumentoEventoResultado> ProcesarEventoAsync(DocumentoEventoRequest request, CancellationToken cancellationToken = default);

        Task<DocumentoGenerado?> ObtenerAsync(int id);
        Task<(List<DocumentoGenerado> Items, int Total)> BuscarAsync(DocumentoFiltro filtro);
        Task<List<DocumentoGenerado>> ObtenerPorVentaAsync(int ventaId);
        Task<List<DocumentoGenerado>> ObtenerPorCreditoAsync(int creditoId);
        Task<List<DocumentoGenerado>> ObtenerPorPagoAsync(int pagoCuotaId);
        Task<List<DocumentoGenerado>> ObtenerPorCotizacionAsync(int cotizacionId);
        Task<List<DocumentoGenerado>> ObtenerPorClienteAsync(int clienteId, int take = 100);
        Task<List<DocumentoGenerado>> ObtenerPorGrupoAsync(Guid grupoImpresionId);

        /// <summary>Visualización: sirve el PDF del contenido histórico sin registrar reimpresión.</summary>
        Task<DocumentoPdfArchivo> VerPdfAsync(IReadOnlyCollection<int> ids);

        /// <summary>Reimpresión: sirve el contenido histórico tal cual y registra la reimpresión. Nunca recalcula.</summary>
        Task<DocumentoPdfArchivo> ReimprimirAsync(IReadOnlyCollection<int> ids);

        Task CancelarAsync(int id, string motivo);

        /// <summary>
        /// Regeneración explícita: crea un documento nuevo con la plantilla vigente y los datos
        /// actuales y marca el anterior como Reemplazado. No permitida sobre documentos firmados
        /// salvo <paramref name="confirmarSobreFirmado"/>.
        /// </summary>
        Task<DocumentoGenerado> RegenerarAsync(int id, string motivo, bool confirmarSobreFirmado = false);

        /// <summary>Registra la firma de un rol. <paramref name="imagenFirma"/> (data URL PNG) es opcional: firma manuscrita capturada en pantalla.</summary>
        Task FirmarAsync(int id, string rol, string? firmante, string? imagenFirma = null);

        /// <summary>Documentos de la venta que exigen firma para continuar y todavía no están firmados.</summary>
        Task<List<DocumentoGenerado>> ObtenerBloqueantesDeFirmaAsync(int ventaId);

        /// <summary>Nombre del paquete documental ("Documentación de Crédito") de cada grupo de impresión cuyos documentos lo forman.</summary>
        Task<Dictionary<Guid, string>> ObtenerNombresDePaqueteAsync(IEnumerable<DocumentoGenerado> documentos);

        /// <summary>¿Hay alguna regla activa para el evento? (para no ofrecer botones que no pueden generar nada).</summary>
        Task<bool> TieneReglaActivaAsync(string evento);

        /// <summary>Genera (idempotente) los documentos que falten para una operación, por si falló una emisión recuperable.</summary>
        Task<DocumentoEventoResultado> ReintentarEventoAsync(string evento, DocumentoOrigen origen);
    }

    public interface IDocumentoPdfService
    {
        DocumentoPdfArchivo GenerarPdf(IReadOnlyList<DocumentoGenerado> documentos);
    }

    public interface IDocumentoNumeracionService
    {
        /// <summary>Siguiente número del tipo, único incluso con procesos concurrentes. Debe llamarse dentro de una transacción.</summary>
        Task<string> SiguienteNumeroAsync(int tipoDocumentoId);
    }

    public interface IDocumentoContextoBuilder
    {
        Task<DocumentoContexto> ConstruirAsync(string evento, DocumentoOrigen origen);
    }
}
