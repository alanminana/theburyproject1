using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "clientes", Accion = "view")]
    public class ClienteController : Controller
    {
        private readonly IClienteService _clienteService;
        private readonly IDocumentoClienteService _documentoService;
        private readonly ICreditoService _creditoService;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly IClienteAptitudService _aptitudService;
        private readonly IClienteScoringService _scoringService;
        private readonly ISituacionCrediticiaBcraService _bcraService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IGaranteService _garanteService;
        private readonly IVentaService _ventaService;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ILogger<ClienteController> _logger;

        public ClienteController(
            IClienteService clienteService,
            IDocumentoClienteService documentoService,
            ICreditoService creditoService,
            ICreditoDisponibleService creditoDisponibleService,
            IClienteAptitudService aptitudService,
            IClienteScoringService scoringService,
            ISituacionCrediticiaBcraService bcraService,
            IConfiguracionPagoService configuracionPagoService,
            IGaranteService garanteService,
            IVentaService ventaService,
            ICurrentUserService currentUser,
            IMapper mapper,
            ILogger<ClienteController> logger)
        {
            _clienteService = clienteService;
            _documentoService = documentoService;
            _creditoService = creditoService;
            _creditoDisponibleService = creditoDisponibleService;
            _aptitudService = aptitudService;
            _scoringService = scoringService;
            _bcraService = bcraService;
            _configuracionPagoService = configuracionPagoService;
            _garanteService = garanteService;
            _ventaService = ventaService;
            _currentUser = currentUser;
            _mapper = mapper;
            _logger = logger;
        }

        #region CRUD

        public async Task<IActionResult> Index(ClienteFilterViewModel filter, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var clientes = await _clienteService.SearchAsync(
                    searchTerm: filter.SearchTerm,
                    tipoDocumento: filter.TipoDocumento,
                    soloActivos: filter.SoloActivos,
                    conCreditosActivos: filter.ConCreditosActivos,
                    puntajeMinimo: filter.PuntajeMinimo,
                    orderBy: filter.OrderBy,
                    orderDirection: filter.OrderDirection);

                var viewModels = _mapper.Map<List<ClienteViewModel>>(clientes);

                filter.Clientes = viewModels;
                filter.TotalResultados = viewModels.Count;

                CargarDropdowns();
                return View("Index_tw", filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener clientes");
                TempData["Error"] = "Error al cargar los clientes";

                var fallback = new ClienteFilterViewModel();
                CargarDropdowns();
                return View("Index_tw", fallback);
            }
        }

        public async Task<IActionResult> Details(int id, string? tab, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var cliente = await _clienteService.GetByIdAsync(id);
                if (cliente == null)
                    return this.RedirectToReturnUrlOrIndex(returnUrl);

                var detalleViewModel = await ConstructDetalleViewModel(cliente!, tab);
                return View("Details_tw", detalleViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cliente {Id}", id);
                TempData["Error"] = "Error al cargar el cliente";
                return this.RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        public async Task<IActionResult> Create(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            CargarDropdowns();
            await CargarPerfilesCredito();
            return View("Create_tw", new ClienteViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ClienteViewModel viewModel, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                if (!ModelState.IsValid)
                {
                    CargarDropdowns();
                    await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                    return View("Create_tw", viewModel);
                }

                var cliente = _mapper.Map<Cliente>(viewModel);
                await _clienteService.CreateAsync(cliente);

                TempData["Success"] = $"Cliente {cliente.NombreCompleto} creado exitosamente";
                return RedirectToAction(nameof(Details), new { id = cliente.Id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                CargarDropdowns();
                await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear cliente");
                ModelState.AddModelError("", "Error al crear el cliente");
                CargarDropdowns();
                await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                return View("Create_tw", viewModel);
            }
        }

        public async Task<IActionResult> Edit(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var cliente = await _clienteService.GetByIdAsync(id);
                if (cliente == null)
                    return this.RedirectToReturnUrlOrIndex(returnUrl);

                var viewModel = _mapper.Map<ClienteViewModel>(cliente!);
                CargarDropdowns();
                await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar cliente {Id} para edición", id);
                TempData["Error"] = "Error al cargar el cliente";
                return this.RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ClienteViewModel viewModel, string? returnUrl = null)
        {
            if (id != viewModel.Id)
                return NotFound();

            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                if (!ModelState.IsValid)
                {
                    CargarDropdowns();
                    await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                    return View("Edit_tw", viewModel);
                }

                var cliente = _mapper.Map<Cliente>(viewModel);
                await _clienteService.UpdateAsync(cliente);

                TempData["Success"] = "Cliente actualizado exitosamente";
                return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                CargarDropdowns();
                await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar cliente {Id}", id);
                ModelState.AddModelError("", "Error al actualizar el cliente");
                CargarDropdowns();
                await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
                return View("Edit_tw", viewModel);
            }
        }

        #endregion

        #region Modal AJAX (Create / Edit sin navegación)

        [HttpGet]
        public async Task<IActionResult> ModalCreate()
        {
            CargarDropdowns();
            await CargarPerfilesCredito();
            return PartialView("_ClienteFormPartial", new ClienteViewModel());
        }

        [HttpGet]
        public async Task<IActionResult> ModalEdit(int id)
        {
            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente == null)
                return NotFound();

            var viewModel = _mapper.Map<ClienteViewModel>(cliente);
            CargarDropdowns();
            await CargarPerfilesCredito(viewModel.PerfilCreditoPreferidoId);
            return PartialView("_ClienteFormPartial", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "create")]
        public async Task<IActionResult> CreateAjax(ClienteViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList();
                return Json(new { success = false, errors });
            }

            try
            {
                var cliente = _mapper.Map<Cliente>(viewModel);
                await _clienteService.CreateAsync(cliente);
                return Json(new { success = true, id = cliente.Id, nombre = cliente.NombreCompleto });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, errors = new[] { ex.Message } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear cliente vía AJAX");
                return Json(new { success = false, errors = new[] { "Error al crear el cliente." } });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> EditAjax(ClienteViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList();
                return Json(new { success = false, errors });
            }

            try
            {
                var cliente = _mapper.Map<Cliente>(viewModel);
                await _clienteService.UpdateAsync(cliente);
                return Json(new { success = true, id = cliente.Id, nombre = cliente.NombreCompleto });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, errors = new[] { ex.Message } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar cliente {Id} vía AJAX", viewModel.Id);
                return Json(new { success = false, errors = new[] { "Error al actualizar el cliente." } });
            }
        }

        #endregion

        public async Task<IActionResult> Delete(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var cliente = await _clienteService.GetByIdAsync(id);
                if (cliente == null)
                    return this.RedirectToReturnUrlOrIndex(returnUrl);

                var viewModel = _mapper.Map<ClienteViewModel>(cliente!);
                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar cliente {Id} para eliminación", id);
                TempData["Error"] = "Error al cargar el cliente";
                return this.RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            try
            {
                await _clienteService.DeleteAsync(id);
                TempData["Success"] = "Cliente eliminado exitosamente";
                return this.RedirectToReturnUrlOrIndex(returnUrl);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Delete), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar cliente {Id}", id);
                TempData["Error"] = "Error al eliminar el cliente";
                return RedirectToAction(nameof(Delete), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
        }

        #region Crédito y aptitud

        /// <summary>
        /// GET: Devuelve el contenido parcial del modal de límites por puntaje (carga vía AJAX).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> LimitesPorPuntajePartial()
        {
            try
            {
                ViewBag.PuedeAdministrarLimites = _currentUser.HasPermission("clientes", "managecreditlimits");
                var model = await ConstruirModeloLimitesPorPuntajeAsync();
                return PartialView("_LimitesPorPuntajeModal_tw", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar partial de límites por puntaje");
                return StatusCode(500, "No se pudo cargar la configuración de límites por puntaje.");
            }
        }

        /// <summary>
        /// POST: Guarda la configuración de límites por puntaje vía AJAX y devuelve JSON.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "managecreditlimits")]
        public async Task<IActionResult> LimitesPorPuntajeGuardar([FromForm] ClienteCreditoLimitesViewModel model)
        {
            try
            {
                var items = (model.Items ?? new List<ClienteCreditoLimiteItemViewModel>())
                    .Select(i => (i.Puntaje, i.LimiteMonto, i.Activo))
                    .ToList()
                    .AsReadOnly();

                var usuario = _currentUser.GetUsername();

                var (ok, errores) = await _creditoDisponibleService.GuardarLimitesPorPuntajeAsync(items, usuario);

                if (!ok)
                    return Json(new { ok = false, errores });

                return Json(new { ok = true, mensaje = "Configuración de límites por puntaje guardada correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración de límites por puntaje");
                return Json(new { ok = false, errores = new[] { "No se pudo guardar la configuración. Intentá de nuevo." } });
            }
        }

        /// <summary>
        /// Asigna o actualiza el límite de crédito (cupo) de un cliente.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]        public async Task<IActionResult> AsignarLimiteCredito(int clienteId, decimal limiteCredito, string? motivo = null, string? returnUrl = null)
        {
            try
            {
                if (limiteCredito < 0)
                {
                    TempData["Error"] = "El límite de crédito no puede ser negativo";
                    return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                var exito = await _aptitudService.AsignarLimiteCreditoAsync(clienteId, limiteCredito, motivo);

                if (exito)
                {
                    // Re-evaluar aptitud después de asignar cupo
                    await _aptitudService.EvaluarAptitudAsync(clienteId, guardarResultado: true);
                    TempData["Success"] = $"Límite de crédito actualizado a {limiteCredito:C0}";
                }
                else
                {
                    TempData["Error"] = "Error al actualizar el límite de crédito";
                }

                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al asignar límite de crédito al cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al actualizar el límite de crédito";
                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "managecreditlimits")]
        public async Task<IActionResult> AsignarNivelCreditoManual(
            int clienteId,
            int nivelCreditoManual,
            string motivo,
            string? returnUrl = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "El motivo para asignar nivel manual es obligatorio.";
                    return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                var exito = await _clienteService.AsignarNivelCreditoManualAsync(
                    clienteId,
                    nivelCreditoManual,
                    motivo,
                    _currentUser.GetUsername());

                if (!exito)
                {
                    TempData["Error"] = "No se pudo asignar el nivel manual.";
                    return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                await _aptitudService.EvaluarAptitudAsync(clienteId, guardarResultado: true);
                TempData["Success"] = $"Puntaje crediticio manual asignado: {nivelCreditoManual}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al asignar nivel crediticio manual al cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al asignar el nivel manual.";
            }

            return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "managecreditlimits")]
        public async Task<IActionResult> LimpiarNivelCreditoManual(
            int clienteId,
            string motivo,
            string? returnUrl = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "El motivo para limpiar el nivel manual es obligatorio.";
                    return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                var exito = await _clienteService.LimpiarNivelCreditoManualAsync(
                    clienteId,
                    motivo,
                    _currentUser.GetUsername());

                if (!exito)
                {
                    TempData["Error"] = "No se pudo limpiar el nivel manual.";
                    return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                await _aptitudService.EvaluarAptitudAsync(clienteId, guardarResultado: true);
                TempData["Success"] = "Nivel manual limpiado. El cliente vuelve al calculo automatico.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al limpiar nivel crediticio manual del cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al limpiar el nivel manual.";
            }

            return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        /// <summary>
        /// Recalcula la aptitud crediticia del cliente.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalcularAptitud(int clienteId, string? returnUrl = null)
        {
            try
            {
                // Refrescar BCRA (con caché) antes de evaluar para que la aptitud
                // considere el dato consultado en esta misma operación.
                await _bcraService.ConsultarYActualizarAsync(clienteId);
                await _aptitudService.EvaluarAptitudAsync(clienteId, guardarResultado: true);
                TempData["Success"] = "Aptitud crediticia recalculada";
                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al recalcular aptitud del cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al recalcular aptitud";
                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
        }

        /// <summary>
        /// Recalcula el scoring de comportamiento del cliente (PuntajeCliente + snapshots).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalcularPuntaje(int clienteId, string? returnUrl = null)
        {
            try
            {
                var resultado = await _scoringService.RecalcularAsync(clienteId);
                TempData[resultado != null ? "Success" : "Error"] =
                    resultado != null ? "Puntaje del cliente recalculado" : "No se pudo recalcular el puntaje";
                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al recalcular puntaje del cliente {ClienteId}", clienteId);
                TempData["Error"] = "Error al recalcular puntaje";
                return RedirectToAction(nameof(Details), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarBcra(int clienteId)
        {
            try
            {
                await _bcraService.ForzarActualizacionAsync(clienteId);

                var cliente = await _clienteService.GetByIdAsync(clienteId);
                if (cliente == null)
                    return NotFound();

                return Json(new
                {
                    ok = cliente.SituacionCrediticiaConsultaOk ?? false,
                    situacion = cliente.SituacionCrediticiaBcra,
                    descripcion = cliente.SituacionCrediticiaDescripcion,
                    periodo = cliente.SituacionCrediticiaPeriodo,
                    ultimaConsulta = cliente.SituacionCrediticiaUltimaConsultaUtc?.ToString("dd/MM/yyyy HH:mm")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forzando actualización BCRA para cliente {Id}", clienteId);
                return Json(new { ok = false, descripcion = "Error al consultar BCRA" });
            }
        }

        #endregion

        #region Métodos Privados

        private async Task<ClienteDetalleViewModel> ConstructDetalleViewModel(Cliente cliente, string? tab)
        {
            var detalleViewModel = new ClienteDetalleViewModel
            {
                TabActivo = tab ?? "informacion",
                Cliente = _mapper.Map<ClienteViewModel>(cliente)
            };

            detalleViewModel.Documentos = await _documentoService.GetByClienteIdAsync(cliente.Id);
            detalleViewModel.CreditosActivos = await _creditoService.GetByClienteIdAsync(cliente.Id);

            // Situación Crediticia BCRA (con caché de 7 días). Debe refrescarse antes de
            // evaluar aptitud para que el semáforo use el dato de la misma operación.
            try
            {
                var situacionBcra = await _bcraService.ConsultarYObtenerAsync(cliente.Id);
                if (situacionBcra != null)
                    AplicarSituacionBcra(detalleViewModel.Cliente, situacionBcra);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error consultando BCRA para cliente {Id}", cliente.Id);
            }

            // Evaluar aptitud crediticia (semáforo)
            detalleViewModel.AptitudCrediticia = await _aptitudService.EvaluarAptitudSinGuardarAsync(cliente.Id);

            // Panel de visibilidad del crédito disponible
            detalleViewModel.CreditoDisponiblePanel.PuntajeActual = cliente.PuntajeCliente;
            detalleViewModel.CreditoDisponiblePanel.NivelesDisponibles = await ConstruirOpcionesNivelCreditoAsync();
            try
            {
                var valores = await _creditoDisponibleService.CalcularDisponibleAsync(cliente.Id);
                detalleViewModel.CreditoDisponiblePanel.Valores = valores;
                detalleViewModel.CreditoDisponiblePanel.PuntajeActual = valores.NivelCreditoFinal;
                detalleViewModel.CreditoDisponiblePanel.PorcentajeLibre =
                    valores.Limite > 0 ? Math.Round(valores.Disponible / valores.Limite * 100, 0) : 0m;
            }
            catch (CreditoDisponibleException ex)
            {
                detalleViewModel.CreditoDisponiblePanel.TieneErrorConfiguracion = true;
                detalleViewModel.CreditoDisponiblePanel.MensajeError = ex.Message;
            }

            // Garante actual del cliente
            try
            {
                detalleViewModel.GaranteInfo = await _garanteService.ObtenerInfoGaranteAsync(cliente.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error obteniendo info de garante para cliente {Id}", cliente.Id);
            }

            // Historial de PuntajeCliente (solo lectura)
            try
            {
                detalleViewModel.HistorialPuntaje = await _clienteService.GetHistorialPuntajeAsync(cliente.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error obteniendo historial de puntaje para cliente {Id}", cliente.Id);
            }

            // Ventas pendientes de autorización crediticia (solo lectura)
            try
            {
                var ventasPendientes = await _ventaService.GetAllAsync(new VentaFilterViewModel
                {
                    ClienteId = cliente.Id,
                    EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion
                });

                detalleViewModel.VentasPendientesAutorizacion = ventasPendientes
                    .Where(v => v.RequiereAutorizacion)
                    .Take(5)
                    .Select(v => new VentaPendienteAutorizacionItemViewModel
                    {
                        VentaId = v.Id,
                        Numero = v.Numero,
                        Fecha = v.FechaVenta,
                        Total = v.Total,
                        Resumen = v.RazonesAutorizacion.Any()
                            ? string.Join("; ", v.RazonesAutorizacion.Select(r => r.Descripcion))
                            : null
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error obteniendo ventas pendientes de autorización para cliente {Id}", cliente.Id);
            }

            return detalleViewModel;
        }

        private void CargarDropdowns()
        {
            ViewBag.TiposDocumento = new SelectList(DropdownConstants.TiposDocumento);
            ViewBag.EstadosCiviles = new SelectList(DropdownConstants.EstadosCiviles);
            ViewBag.TiposEmpleo = new SelectList(DropdownConstants.TiposEmpleo);
            ViewBag.Provincias = new SelectList(DropdownConstants.Provincias);
            
            // Niveles de riesgo crediticio (1-5)
            ViewBag.NivelesRiesgo = Enum.GetValues<NivelRiesgoCredito>()
                .Select(n => new SelectListItem
                {
                    Value = ((int)n).ToString(),
                    Text = n.GetDisplayName()
                })
                .ToList();
        }

        private async Task CargarPerfilesCredito(int? perfilSeleccionadoId = null)
        {
            var perfiles = await _configuracionPagoService.GetPerfilesCreditoAsync();
            ViewBag.PerfilesCredito = new SelectList(perfiles, "Id", "Nombre", perfilSeleccionadoId);
        }

        private async Task<ClienteCreditoLimitesViewModel> ConstruirModeloLimitesPorPuntajeAsync()
        {
            var dbItems = await _creditoDisponibleService.GetAllLimitesPorPuntajeAsync();

            var items = new List<ClienteCreditoLimiteItemViewModel>();

            foreach (var puntaje in Enumerable.Range(0, 6))
            {
                var existente = dbItems.FirstOrDefault(x => x.Puntaje == puntaje);
                if (existente != null)
                {
                    items.Add(new ClienteCreditoLimiteItemViewModel
                    {
                        Id = existente.Id,
                        Puntaje = existente.Puntaje,
                        LimiteMonto = existente.LimiteMonto,
                        Activo = existente.Activo,
                        FechaActualizacion = existente.FechaActualizacion,
                        UsuarioActualizacion = existente.UsuarioActualizacion
                    });
                }
                else
                {
                    items.Add(new ClienteCreditoLimiteItemViewModel
                    {
                        Puntaje = puntaje,
                        LimiteMonto = 0m,
                        Activo = true
                    });
                }
            }

            return new ClienteCreditoLimitesViewModel { Items = items };
        }

        private async Task<List<ClienteNivelCreditoOpcionViewModel>> ConstruirOpcionesNivelCreditoAsync()
        {
            var dbItems = await _creditoDisponibleService.GetAllLimitesPorPuntajeAsync();

            return Enumerable.Range(0, 6)
                .Select(nivel =>
                {
                    var existente = dbItems.FirstOrDefault(x => x.Puntaje == nivel);
                    return new ClienteNivelCreditoOpcionViewModel
                    {
                        Nivel = nivel,
                        LimiteMonto = existente?.LimiteMonto ?? 0m,
                        Activo = existente?.Activo ?? false
                    };
                })
                .ToList();
        }

        private static void AplicarSituacionBcra(ClienteViewModel vm, SituacionBcraResult resultado)
        {
            vm.SituacionCrediticiaBcra = resultado.SituacionCrediticiaBcra;
            vm.SituacionCrediticiaDescripcion = resultado.SituacionCrediticiaDescripcion;
            vm.SituacionCrediticiaPeriodo = resultado.SituacionCrediticiaPeriodo;
            vm.SituacionCrediticiaUltimaConsultaUtc = resultado.SituacionCrediticiaUltimaConsultaUtc;
            vm.SituacionCrediticiaConsultaOk = resultado.SituacionCrediticiaConsultaOk;
        }

        #endregion

        #region Garante

        /// <summary>
        /// GET /Cliente/BuscarPosiblesGarantes?q=...&amp;clienteId=...
        /// Devuelve candidatos a garante filtrados por nombre o documento.
        /// </summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "clientes", Accion = "view")]
        public async Task<IActionResult> BuscarPosiblesGarantes(string? q, int clienteId)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Json(new List<object>());

            var candidatos = await _garanteService.BuscarCandidatosAsync(q, clienteId, maxResultados: 10);

            var resultado = candidatos.Select(c => new
            {
                clienteId = c.ClienteId,
                nombreCompleto = c.NombreCompleto,
                numeroDocumento = c.NumeroDocumento,
                puntajeCliente = c.PuntajeCliente,
                activo = c.Activo,
                cantidadCompras = c.CantidadCompras,
                garantiasActivas = c.GarantiasActivas,
                display = $"{c.NombreCompleto} — DNI {c.NumeroDocumento} — Puntaje {c.PuntajeCliente}"
            });

            return Json(resultado);
        }

        /// <summary>
        /// POST /Cliente/AsignarGarante
        /// Asigna un cliente como garante de otro. Valida todas las reglas antes de persistir.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> AsignarGarante([FromBody] AsignarGaranteRequest request)
        {
            if (request is null || request.ClienteId <= 0 || request.GaranteClienteId <= 0)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            var usuario = _currentUser.GetUsername();
            var (ok, error) = await _garanteService.AsignarGaranteAsync(
                request.ClienteId,
                request.GaranteClienteId,
                request.Observacion,
                usuario);

            if (!ok)
                return BadRequest(new { ok = false, error });

            var info = await _garanteService.ObtenerInfoGaranteAsync(request.ClienteId);
            return Ok(new { ok = true, garante = info });
        }

        /// <summary>
        /// POST /Cliente/RemoverGarante
        /// Remueve el garante actual del cliente (baja suave).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> RemoverGarante([FromBody] RemoverGaranteRequest request)
        {
            if (request is null || request.ClienteId <= 0)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? "Removido manualmente." : request.Motivo;
            var usuario = _currentUser.GetUsername();

            var (ok, error) = await _garanteService.RemoverGaranteAsync(request.ClienteId, motivo, usuario);

            if (!ok)
                return BadRequest(new { ok = false, error });

            return Ok(new { ok = true });
        }

        #endregion
    }
}
