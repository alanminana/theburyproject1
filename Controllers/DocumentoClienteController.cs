using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using System.Text.Json;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "clientes", Accion = "viewdocs")]
    public class DocumentoClienteController : Controller
    {
        private readonly IDocumentoClienteService _documentoService;
        private readonly IVentaService _ventaService;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<DocumentoClienteController> _logger;
        private readonly IDocumentacionService _documentacionService;
        private readonly IClienteLookupService _clienteLookup;

        private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl, object? indexRouteValues = null)
        {
            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Index), indexRouteValues);
        }

        public DocumentoClienteController(
            IDocumentoClienteService documentoService,
            IVentaService ventaService,
            ICurrentUserService currentUser,
            ILogger<DocumentoClienteController> logger,
            IDocumentacionService documentacionService,
            IClienteLookupService clienteLookup)
        {
            _documentoService = documentoService;
            _ventaService = ventaService;
            _currentUser = currentUser;
            _logger = logger;
            _documentacionService = documentacionService;
            _clienteLookup = clienteLookup;
        }

        // GET: DocumentoCliente
        public async Task<IActionResult> Index(DocumentoClienteFilterViewModel? filtro, int? returnToVentaId, string? returnUrl = null)
        {
            try
            {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                if (filtro == null)
                    filtro = new DocumentoClienteFilterViewModel();

                if (returnToVentaId.HasValue)
                    filtro.ReturnToVentaId = returnToVentaId;

                var (documentos, total) = await _documentoService.BuscarAsync(filtro);
                filtro.Documentos = documentos;
                filtro.TotalResultados = total;

                filtro.UploadModel = new DocumentoClienteViewModel
                {
                    ClienteId = filtro.ClienteId ?? 0,
                    ReturnToVentaId = filtro.ReturnToVentaId
                };

                if (filtro.UploadModel.ClienteId > 0)
                {
                    var display = await _clienteLookup.GetClienteDisplayNameAsync(filtro.UploadModel.ClienteId);
                    if (!string.IsNullOrWhiteSpace(display))
                    {
                        filtro.UploadModel.ClienteNombre = display;
                    }
                }

                if (filtro.ReturnToVentaId.HasValue)
                {
                    var venta = await _ventaService.GetByIdAsync(filtro.ReturnToVentaId.Value);
                    if (venta != null)
                    {
                        ViewBag.DocumentacionPendiente =
                            await _documentoService.ValidarDocumentacionObligatoriaAsync(venta.ClienteId);
                    }
                }

                await CargarViewBags(filtro.ClienteId);

                return View("Index_tw", filtro);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar documentos");
                TempData["Error"] = $"Error al cargar los documentos: {ex.Message}";

                var emptyModel = new DocumentoClienteFilterViewModel();
                await CargarViewBags(null);
                return View("Index_tw", emptyModel);
            }
        }

        // GET: DocumentoCliente/Upload
        public async Task<IActionResult> Upload(int? clienteId, int? returnToVentaId, int? replaceId, string? returnUrl = null)
        {
            var viewModel = new DocumentoClienteViewModel();
            var bloquearCliente = false;

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            if (clienteId.HasValue)
                viewModel.ClienteId = clienteId.Value;
            if (returnToVentaId.HasValue)
            {
                viewModel.ReturnToVentaId = returnToVentaId;

                var venta = await _ventaService.GetByIdAsync(returnToVentaId.Value);
                if (venta != null)
                {
                    viewModel.ClienteId = venta.ClienteId;
                    viewModel.ClienteNombre = venta.ClienteNombre;
                    bloquearCliente = true;
                }
            }

            if (replaceId.HasValue)
            {
                var documento = await _documentoService.GetByIdAsync(replaceId.Value);
                if (documento != null)
                {
                    viewModel.DocumentoAReemplazarId = documento.Id;
                    viewModel.ReemplazarExistente = true;
                    viewModel.DocumentoAReemplazarNombre = documento.NombreArchivo;
                    viewModel.ClienteId = documento.ClienteId;
                    viewModel.TipoDocumento = documento.TipoDocumento;
                    await CargarViewBags(documento.ClienteId, false);
                }
            }

            ViewBag.ClienteBloqueado = bloquearCliente;
            await CargarViewBags(viewModel.ClienteId, bloquearCliente);

            if (!string.IsNullOrWhiteSpace(viewModel.ClienteNombre))
            {
                return View("Upload_tw", viewModel);
            }

            if (viewModel.ClienteId > 0)
            {
                var display = await _clienteLookup.GetClienteDisplayNameAsync(viewModel.ClienteId);
                if (!string.IsNullOrWhiteSpace(display))
                {
                    viewModel.ClienteNombre = display;
                }
            }

            return View("Upload_tw", viewModel);
        }

        // POST: DocumentoCliente/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(DocumentoClienteViewModel viewModel, bool returnToDetails = false, string? returnUrl = null)
        {
            try
            {
                if (viewModel.ReturnToVentaId.HasValue)
                {
                    var venta = await _ventaService.GetByIdAsync(viewModel.ReturnToVentaId.Value);

                    if (venta == null)
                    {
                        ModelState.AddModelError("", "No se encontró la venta asociada al crédito.");
                    }
                    else if (venta.ClienteId != viewModel.ClienteId)
                    {
                        ModelState.AddModelError("ClienteId", "Debe adjuntar documentación para el cliente seleccionado en la venta.");
                        viewModel.ClienteId = venta.ClienteId;
                        viewModel.ClienteNombre = venta.ClienteNombre;
                    }
                    else
                    {
                        viewModel.ClienteNombre = venta.ClienteNombre;
                    }
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ClienteBloqueado = viewModel.ReturnToVentaId.HasValue;
                    await CargarViewBags(viewModel.ClienteId, viewModel.ReturnToVentaId.HasValue);

                    // Si viene del inline upload, redirigir con error
                    if (returnToDetails)
                    {
                        TempData["Error"] = "Por favor corrija los errores en el formulario";
                        return RedirectToReturnUrlOrIndex(
                            returnUrl,
                            new { clienteId = viewModel.ClienteId, returnToVentaId = viewModel.ReturnToVentaId });
                    }

                    return View("Upload_tw", viewModel);
                }

                var resultado = await _documentoService.UploadAsync(viewModel);

                TempData["Success"] = $"Documento '{resultado.TipoDocumentoNombre}' subido exitosamente";

                if (viewModel.ReturnToVentaId.HasValue)
                {
                    var returnToVentaDetailsUrl = Url.Action("Details", "Venta", new { id = viewModel.ReturnToVentaId.Value });

                    var estado = await _documentacionService.ProcesarDocumentacionVentaAsync(viewModel.ReturnToVentaId.Value);

                    if (!estado.DocumentacionCompleta)
                    {
                        TempData["Warning"] =
                            $"Falta documentación obligatoria para otorgar crédito: {estado.MensajeFaltantes}";

                        return RedirectToAction(nameof(Index), new
                        {
                            clienteId = viewModel.ClienteId,
                            returnToVentaId = viewModel.ReturnToVentaId,
                            returnUrl = returnToVentaDetailsUrl
                        });
                    }

                    TempData["Info"] = estado.CreditoCreado
                        ? "Documentación completa. Crédito generado para esta venta."
                        : "Documentación completa. Crédito listo para configurar.";

                    return RedirectToAction(
                        "ConfigurarVenta",
                        "Credito",
                        new { id = estado.CreditoId, ventaId = viewModel.ReturnToVentaId, returnUrl = returnToVentaDetailsUrl });
                }

                // Si viene del upload inline, redirigir a Cliente/Details con tab documentos
                if (returnToDetails)
                {
                    return RedirectToReturnUrlOrIndex(
                        returnUrl,
                        new { clienteId = viewModel.ClienteId, returnToVentaId = viewModel.ReturnToVentaId });
                }

                // Redirigir al índice de documentos filtrado por el cliente
                return RedirectToReturnUrlOrIndex(
                    returnUrl,
                    new { clienteId = viewModel.ClienteId, returnToVentaId = viewModel.ReturnToVentaId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al subir documento");

                TempData["Error"] = "Error al subir documento: " + ex.Message;

                // Si viene del inline upload, redirigir al tab documentos
                if (returnToDetails)
                {
                    return RedirectToReturnUrlOrIndex(
                        returnUrl,
                        new { clienteId = viewModel.ClienteId, returnToVentaId = viewModel.ReturnToVentaId });
                }

                ModelState.AddModelError("", "Error al subir documento: " + ex.Message);
                ViewBag.ClienteBloqueado = viewModel.ReturnToVentaId.HasValue;
                await CargarViewBags(viewModel.ClienteId, viewModel.ReturnToVentaId.HasValue);
                return View("Upload_tw", viewModel);
            }
        }

        // GET: DocumentoCliente/Details/5
        public async Task<IActionResult> Details(int id, string? returnUrl = null)
        {
            try
            {
                var documento = await _documentoService.GetByIdAsync(id);
                if (documento == null)
                {
                    TempData["Error"] = "Documento no encontrado";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }

                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                return View("Details_tw", documento);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener documento {Id}", id);
                TempData["Error"] = "Error al cargar el documento";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        // POST: DocumentoCliente/Verificar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verificar(int id, string? observaciones, string? returnUrl = null)
        {
            try
            {
                // CAMBIO: Capturar usuario actual en lugar de hardcodear "System"
                var usuario = _currentUser.GetUsername();
                var resultado = await _documentoService.VerificarAsync(id, usuario, observaciones);

                if (resultado)
                    TempData["Success"] = "Documento verificado exitosamente";
                else
                    TempData["Error"] = "No se pudo verificar el documento";

                // Redirigir a returnUrl si existe (la lista de documentos del cliente)
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar documento {Id}", id);
                TempData["Error"] = "Error al verificar el documento";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        // POST: DocumentoCliente/Rechazar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string motivo, string? returnUrl = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "Debe especificar el motivo del rechazo";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }

                // CAMBIO: Capturar usuario actual en lugar de hardcodear "System"
                var usuario = _currentUser.GetUsername();
                var resultado = await _documentoService.RechazarAsync(id, motivo, usuario);

                if (resultado)
                    TempData["Success"] = "Documento rechazado";
                else
                    TempData["Error"] = "No se pudo rechazar el documento";

                // Redirigir a returnUrl si existe (la lista de documentos del cliente)
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar documento {Id}", id);
                TempData["Error"] = "Error al rechazar el documento";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        // POST: DocumentoCliente/VerificarTodos
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerificarTodos(int clienteId, string? observaciones, string? returnUrl = null)
        {
            try
            {
                var usuario = _currentUser.GetUsername();
                var resultado = await _documentoService.VerificarTodosAsync(clienteId, usuario, observaciones);

                if (resultado > 0)
                    TempData["Success"] = $"Se verificaron {resultado} documento(s) exitosamente";
                else
                    TempData["Warning"] = "No había documentos pendientes para verificar";

                return RedirectToReturnUrlOrIndex(returnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar todos los documentos del cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al verificar los documentos";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        // GET: DocumentoCliente/Descargar/5
        public async Task<IActionResult> Descargar(int id, string? returnUrl = null)
        {
            try
            {
                var documento = await _documentoService.GetByIdAsync(id);
                if (documento == null)
                {
                    TempData["Error"] = "Documento no encontrado";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }

                var bytes = await _documentoService.DescargarArchivoAsync(id);
                return File(bytes, documento.TipoMIME ?? "application/octet-stream", documento.NombreArchivo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar documento {Id}", id);
                TempData["Error"] = "Error al descargar el documento";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        // POST: DocumentoCliente/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, string? returnUrl = null)
        {
            try
            {
                var documento = await _documentoService.GetByIdAsync(id);
                if (documento == null)
                {
                    TempData["Error"] = "Documento no encontrado";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }

                var clienteId = documento.ClienteId;

                var resultado = await _documentoService.DeleteAsync(id);

                if (resultado)
                    TempData["Success"] = "Documento eliminado exitosamente";
                else
                    TempData["Error"] = "No se pudo eliminar el documento";

                return RedirectToReturnUrlOrIndex(returnUrl, new { clienteId = clienteId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar documento {Id}", id);
                TempData["Error"] = "Error al eliminar el documento";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        private async Task CargarViewBags(int? clienteIdSeleccionado = null, bool limitarAClienteSeleccionado = false)
        {
            var clientesSelect = await _clienteLookup.GetClientesSelectListAsync(clienteIdSeleccionado, limitarAClienteSeleccionado);
            ViewBag.Clientes = new SelectList(clientesSelect, "Value", "Text", clienteIdSeleccionado?.ToString());

            ViewBag.TiposDocumento = new SelectList(Enum.GetValues(typeof(TipoDocumentoCliente))
                .Cast<TipoDocumentoCliente>()
                .Select(t => new { Value = (int)t, Text = new DocumentoClienteViewModel { TipoDocumento = t }.TipoDocumentoNombre }), "Value", "Text");

            ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoDocumento))
                .Cast<EstadoDocumento>()
                .Select(e => new { Value = (int)e, Text = e.ToString() }), "Value", "Text");
        }

        // GET: API endpoint para obtener documentos por cliente
        [HttpGet]
        public async Task<IActionResult> GetDocumentosByCliente(int clienteId)
        {
            try
            {
                var documentos = await _documentoService.GetByClienteIdAsync(clienteId);
                return Json(documentos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener documentos del cliente {ClienteId}", clienteId);
                return StatusCode(500, new { error = "Error al obtener documentos" });
            }
        }

        /// <summary>
        /// POST: API endpoint para verificar múltiples documentos en batch (AJAX)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerificarBatch([FromBody] BatchDocumentosRequest request)
        {
            try
            {
                if (request?.Ids == null || !request.Ids.Any())
                {
                    return BadRequest(new BatchDocumentosResponse 
                    { 
                        Success = false, 
                        Message = "Debe seleccionar al menos un documento" 
                    });
                }

                var usuario = _currentUser.GetUsername();
                var resultado = await _documentoService.VerificarBatchAsync(
                    request.Ids, 
                    usuario, 
                    request.Observaciones);

                var response = new BatchDocumentosResponse
                {
                    Success = resultado.Exitosos > 0,
                    Exitosos = resultado.Exitosos,
                    Fallidos = resultado.Fallidos,
                    Errores = resultado.Errores.Select(e => new BatchItemErrorResponse 
                    { 
                        Id = e.Id, 
                        Mensaje = e.Mensaje 
                    }).ToList()
                };

                if (resultado.Exitosos > 0 && resultado.Fallidos == 0)
                {
                    response.Message = $"Se verificaron {resultado.Exitosos} documento(s) exitosamente";
                }
                else if (resultado.Exitosos > 0)
                {
                    response.Message = $"Se verificaron {resultado.Exitosos} documento(s). {resultado.Fallidos} fallaron.";
                }
                else
                {
                    response.Message = "No se pudo verificar ningún documento";
                }

                _logger.LogInformation(
                    "VerificarBatch: {Exitosos} exitosos, {Fallidos} fallidos por {Usuario}",
                    resultado.Exitosos, resultado.Fallidos, usuario);

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en VerificarBatch");
                return StatusCode(500, new BatchDocumentosResponse 
                { 
                    Success = false, 
                    Message = "Error al verificar documentos: " + ex.Message 
                });
            }
        }

        /// <summary>
        /// POST: API endpoint para rechazar múltiples documentos en batch (AJAX)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RechazarBatch([FromBody] BatchDocumentosRequest request)
        {
            try
            {
                if (request?.Ids == null || !request.Ids.Any())
                {
                    return BadRequest(new BatchDocumentosResponse 
                    { 
                        Success = false, 
                        Message = "Debe seleccionar al menos un documento" 
                    });
                }

                if (string.IsNullOrWhiteSpace(request.Motivo))
                {
                    return BadRequest(new BatchDocumentosResponse 
                    { 
                        Success = false, 
                        Message = "Debe especificar el motivo del rechazo" 
                    });
                }

                var usuario = _currentUser.GetUsername();
                var resultado = await _documentoService.RechazarBatchAsync(
                    request.Ids, 
                    request.Motivo, 
                    usuario);

                var response = new BatchDocumentosResponse
                {
                    Success = resultado.Exitosos > 0,
                    Exitosos = resultado.Exitosos,
                    Fallidos = resultado.Fallidos,
                    Errores = resultado.Errores.Select(e => new BatchItemErrorResponse 
                    { 
                        Id = e.Id, 
                        Mensaje = e.Mensaje 
                    }).ToList()
                };

                if (resultado.Exitosos > 0 && resultado.Fallidos == 0)
                {
                    response.Message = $"Se rechazaron {resultado.Exitosos} documento(s)";
                }
                else if (resultado.Exitosos > 0)
                {
                    response.Message = $"Se rechazaron {resultado.Exitosos} documento(s). {resultado.Fallidos} fallaron.";
                }
                else
                {
                    response.Message = "No se pudo rechazar ningún documento";
                }

                _logger.LogInformation(
                    "RechazarBatch: {Exitosos} exitosos, {Fallidos} fallidos por {Usuario}. Motivo: {Motivo}",
                    resultado.Exitosos, resultado.Fallidos, usuario, request.Motivo);

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en RechazarBatch");
                return StatusCode(500, new BatchDocumentosResponse 
                { 
                    Success = false, 
                    Message = "Error al rechazar documentos: " + ex.Message 
                });
            }
        }

        /// <summary>
        /// GET: API para obtener documentos parcial (para refrescar la grilla por AJAX)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDocumentosPartial(int clienteId)
        {
            try
            {
                var documentos = await _documentoService.GetByClienteIdAsync(clienteId);
                return Json(documentos.Select(d => new 
                {
                    d.Id,
                    d.TipoDocumentoNombre,
                    d.NombreArchivo,
                    d.TamanoFormateado,
                    FechaSubida = d.FechaSubida.ToString("dd/MM/yyyy"),
                    Estado = d.Estado.ToString(),
                    d.EstadoNombre,
                    d.EstadoColor,
                    d.EstadoIcono,
                    FechaVencimiento = d.FechaVencimiento?.ToString("dd/MM/yyyy"),
                    Vencido = d.FechaVencimiento.HasValue && d.FechaVencimiento.Value < DateTime.UtcNow,
                    PorVencer = d.FechaVencimiento.HasValue
                        && d.FechaVencimiento.Value >= DateTime.UtcNow
                        && (d.FechaVencimiento.Value - DateTime.UtcNow).Days <= 30,
                    EsPendiente = d.Estado == EstadoDocumento.Pendiente
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener documentos parciales para cliente {ClienteId}", clienteId);
                return StatusCode(500, new { error = "Error al obtener documentos" });
            }
        }
    }
}