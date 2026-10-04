using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Documentos emitidos por el motor documental: consulta, visualización, reimpresión, firma,
    /// cancelación y regeneración. Los documentos son históricos: ver/reimprimir sirve siempre el
    /// contenido guardado; solo "Regenerar" crea una instancia nueva (y deja la anterior como
    /// Reemplazada).
    /// </summary>
    [Authorize]
    public class DocumentoController : Controller
    {
        private const int MaxDocumentosPorImpresion = 20;

        private readonly IDocumentoService _documentos;
        private readonly IDocumentoConfiguracionService _config;
        private readonly ILogger<DocumentoController> _logger;

        public DocumentoController(
            IDocumentoService documentos,
            IDocumentoConfiguracionService config,
            ILogger<DocumentoController> logger)
        {
            _documentos = documentos;
            _config = config;
            _logger = logger;
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "view")]
        public async Task<IActionResult> Index(DocumentoFiltro filtro)
        {
            var (items, total) = await _documentos.BuscarAsync(filtro);
            ViewBag.Tipos = await _config.ListarTiposAsync();
            ViewBag.Total = total;
            ViewBag.Filtro = filtro;
            return View(items);
        }

        /// <summary>Visualiza el PDF del documento histórico (no cuenta como reimpresión).</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "view")]
        public async Task<IActionResult> Ver(int id)
            => await ServirPdfAsync(new[] { id }, reimprimir: false);

        /// <summary>Visualiza juntos todos los documentos vigentes de un grupo de impresión (ej. contrato + pagaré).</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "view")]
        public async Task<IActionResult> VerGrupo(Guid id)
        {
            var docs = await _documentos.ObtenerPorGrupoAsync(id);
            var ids = docs.Select(d => d.Id).ToList();
            if (ids.Count == 0)
                return NotFound();

            return await ServirPdfAsync(ids, reimprimir: false);
        }

        /// <summary>Reimpresión: mismo contenido histórico, registrada y auditada. ids = lista separada por comas.</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "reprint")]
        public async Task<IActionResult> Reimprimir(string ids)
        {
            var lista = (ids ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .Take(MaxDocumentosPorImpresion)
                .ToList();

            if (lista.Count == 0)
                return BadRequest("Indique al menos un documento.");

            return await ServirPdfAsync(lista, reimprimir: true);
        }

        private async Task<IActionResult> ServirPdfAsync(IReadOnlyCollection<int> ids, bool reimprimir)
        {
            try
            {
                var archivo = reimprimir
                    ? await _documentos.ReimprimirAsync(ids)
                    : await _documentos.VerPdfAsync(ids);

                Response.Headers.ContentDisposition = $"inline; filename=\"{archivo.NombreArchivo}\"";
                return File(archivo.Contenido, archivo.TipoContenido, enableRangeProcessing: true);
            }
            catch (DocumentoException ex)
            {
                _logger.LogWarning("No se pudo servir el documento: {Mensaje}", ex.Message);
                return NotFound(ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "sign")]
        public Task<IActionResult> Firmar(int id, string rol, string? firmante, string? firmaImagen, string? returnUrl)
            => EjecutarAsync(() => _documentos.FirmarAsync(id, rol, firmante, firmaImagen), "Firma registrada.", returnUrl);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "cancel")]
        public Task<IActionResult> Cancelar(int id, string motivo, string? returnUrl)
            => EjecutarAsync(() => _documentos.CancelarAsync(id, motivo), "Documento cancelado. Queda como histórico.", returnUrl);

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "generate")]
        public Task<IActionResult> Regenerar(int id, string motivo, bool confirmarSobreFirmado, string? returnUrl)
            => EjecutarAsync(async () => await _documentos.RegenerarAsync(id, motivo, confirmarSobreFirmado),
                "Documento regenerado: el anterior quedó como Reemplazado.", returnUrl);

        /// <summary>
        /// Reintenta (idempotente) los eventos de una operación por si la emisión de un documento no
        /// obligatorio había fallado. Lo que ya existe no se duplica.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "generate")]
        public async Task<IActionResult> GenerarPendientes(int? ventaId, int? pagoCuotaId, int? cotizacionId, string? returnUrl)
        {
            try
            {
                DocumentoEventoResultado resultado;
                if (cotizacionId is int cotizacion)
                    resultado = await _documentos.ReintentarEventoAsync(EventosDocumentales.PresupuestoGenerado, new DocumentoOrigen { CotizacionId = cotizacion });
                else if (pagoCuotaId is int pago)
                    resultado = await _documentos.ReintentarEventoAsync(EventosDocumentales.PagoRegistrado, new DocumentoOrigen { PagoCuotaId = pago });
                else if (ventaId is int venta)
                    resultado = await _documentos.ReintentarEventoAsync(EventosDocumentales.VentaConfirmada, new DocumentoOrigen { VentaId = venta });
                else
                    return BadRequest();

                if (resultado.Errores.Count > 0)
                    TempData["Error"] = string.Join(" ", resultado.Errores.Select(e => e.Mensaje));
                else if (resultado.GeneroAlgo)
                    TempData["Success"] = $"Se generaron {resultado.Generados.Count} documento(s).";
                else
                    TempData["Success"] = "No había documentos pendientes.";
            }
            catch (DocumentoException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return Volver(returnUrl);
        }

        /// <summary>
        /// Emite el presupuesto de una cotización como documento (número propio, histórico, reimprimible) y lo abre.
        /// Es idempotente: si ya se emitió, abre el mismo documento.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "generate")]
        public async Task<IActionResult> GenerarPresupuesto(int? cotizacionId, int? ventaId, string? returnUrl)
        {
            if (cotizacionId == null && ventaId == null)
                return BadRequest();

            try
            {
                // Presupuesto de una venta a crédito (número de operación, entrega y plan) o de una cotización.
                var resultado = await _documentos.ProcesarEventoAsync(new DocumentoEventoRequest
                {
                    Evento = ventaId != null ? EventosDocumentales.PresupuestoVentaGenerado : EventosDocumentales.PresupuestoGenerado,
                    Origen = new DocumentoOrigen { VentaId = ventaId, CotizacionId = ventaId == null ? cotizacionId : null }
                });

                var documento = resultado.Todos.FirstOrDefault();
                if (documento != null)
                    return RedirectToAction(nameof(Ver), new { id = documento.Id });

                TempData["Error"] = resultado.Errores.Count > 0
                    ? string.Join(" ", resultado.Errores.Select(e => e.Mensaje))
                    : "No hay una regla activa que genere el presupuesto. Configurala en Documentos → Reglas.";
            }
            catch (DocumentoException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return Volver(returnUrl);
        }

        private async Task<IActionResult> EjecutarAsync(Func<Task> accion, string mensajeOk, string? returnUrl)
        {
            try
            {
                await accion();
                TempData["Success"] = mensajeOk;
            }
            catch (DocumentoException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return Volver(returnUrl);
        }

        private IActionResult Volver(string? returnUrl)
        {
            var seguro = Url.GetSafeReturnUrl(returnUrl);
            return !string.IsNullOrWhiteSpace(seguro) ? Redirect(seguro) : RedirectToAction(nameof(Index));
        }
    }
}
