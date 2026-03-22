using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionVer)]
    public class VentaController : Controller
    {
        private const string ModuloVentas = "ventas";
        private const string AccionVer = "view";
        private const string AccionCrear = "create";
        private const string AccionActualizar = "update";
        private const string AccionAutorizar = "authorize";
        private const string AccionRechazar = "reject";
        private const string AccionFacturar = "invoice";
        private readonly IVentaService _ventaService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly ILogger<VentaController> _logger;
        private readonly IFinancialCalculationService _financialCalculationService;
        private readonly IPrequalificationService _prequalificationService;
        private readonly IDocumentoClienteService _documentoClienteService;
        private readonly ICreditoService _creditoService;
        private readonly IDocumentacionService _documentacionService;
        private readonly IClienteService _clienteService;
        private readonly IProductoService _productoService;
        private readonly IClienteLookupService _clienteLookup;
        private readonly IValidacionVentaService _validacionVentaService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICajaService _cajaService;

        private string? GetSafeReturnUrl(string? returnUrl)
        {
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : null;
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        private async Task<bool> UsuarioTieneCajaAbiertaAsync()
        {
            var userName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userName))
            {
                return false;
            }

            var aperturaActiva = await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(userName);
            return aperturaActiva != null;
        }

        private async Task<IActionResult?> RedirigirSiCajaCerradaAsync(string mensaje, string actionName, object? routeValues = null)
        {
            if (await UsuarioTieneCajaAbiertaAsync())
            {
                return null;
            }

            TempData["Warning"] = mensaje;
            return RedirectToAction(actionName, routeValues);
        }

        public VentaController(
            IVentaService ventaService,
            IConfiguracionPagoService configuracionPagoService,
            ILogger<VentaController> logger,
            IFinancialCalculationService financialCalculationService,
            IPrequalificationService prequalificationService,
            IDocumentoClienteService documentoClienteService,
            ICreditoService creditoService,
            IDocumentacionService documentacionService,
            IClienteService clienteService,
            IProductoService productoService,
            IClienteLookupService clienteLookup,
            IValidacionVentaService validacionVentaService,
            UserManager<ApplicationUser> userManager,
            ICajaService cajaService)
        {
            _ventaService = ventaService;
            _configuracionPagoService = configuracionPagoService;
            _logger = logger;
            _financialCalculationService = financialCalculationService;
            _prequalificationService = prequalificationService;
            _documentoClienteService = documentoClienteService;
            _creditoService = creditoService;
            _documentacionService = documentacionService;
            _clienteService = clienteService;
            _productoService = productoService;
            _clienteLookup = clienteLookup;
            _validacionVentaService = validacionVentaService;
            _userManager = userManager;
            _cajaService = cajaService;
        }

        // GET: Venta
        public async Task<IActionResult> Index(VentaFilterViewModel filter)
        {
            try
            {
                var ventas = await _ventaService.GetAllAsync(filter);

                // Cargar datos para filtros
                var clientesSelect = await _clienteLookup.GetClientesSelectListAsync();
                ViewBag.Clientes = clientesSelect;

                ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoVenta)));
                ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                ViewBag.EstadosAutorizacion = new SelectList(Enum.GetValues(typeof(EstadoAutorizacionVenta)));
                ViewBag.Filter = filter;

                var userName = User?.Identity?.Name;
                var aperturaActiva = !string.IsNullOrWhiteSpace(userName)
                    ? await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(userName)
                    : null;
                ViewBag.PuedeCrearVenta = aperturaActiva != null;
                ViewBag.PuedeOperarVentas = aperturaActiva != null;

                return View("Index_tw", ventas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener las ventas");
                TempData["Error"] = "Error al cargar las ventas";
                return View("Index_tw", new List<VentaViewModel>());
            }
        }

        // GET: Venta/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    _logger.LogWarning("Edit(GET) venta {Id} not found", id);
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                ViewBag.PuedeOperarVentas = await UsuarioTieneCajaAbiertaAsync();
                return View("Details_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la venta {Id}", id);
                TempData["Error"] = "Error al cargar los detalles de la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Venta/Cotizar
        [HttpGet]
        public async Task<IActionResult> Cotizar()
        {
            await CargarViewBags(vendedorUserIdSeleccionado: GetCurrentUserId());
            ViewBag.IvaRate = VentaConstants.IVA_RATE;
            return View("Create_tw", CrearVentaInicial(EstadoVenta.Cotizacion));
        }

        // POST: Venta/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionCrear)]
        public async Task<IActionResult> Create(VentaViewModel viewModel, string? DatosCreditoPersonallJson)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de crear una venta.",
                    nameof(Index));
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (!ModelState.IsValid || !ValidarDetalles(viewModel))
                {
                    return await RetornarVistaConDatos(viewModel);
                }

                var venta = await _ventaService.CreateAsync(viewModel);

                // Para CréditoPersonal: SIEMPRE redirigir a ConfigurarVenta
                if (venta.TipoPago == TipoPago.CreditoPersonal && venta.CreditoId.HasValue)
                {
                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id = venta.Id });
                    
                    if (venta.RequiereAutorizacion)
                    {
                        TempData["Warning"] = $"Venta {venta.Numero} creada. Requiere autorización. Configure el plan de pago.";
                    }
                    else
                    {
                        TempData["Success"] = $"Venta {venta.Numero} creada. Configure el plan de financiamiento.";
                    }
                    
                    return RedirectToAction(
                        "ConfigurarVenta",
                        "Credito",
                        new { id = venta.CreditoId, ventaId = venta.Id, returnUrl = returnToVentaDetailsUrl });
                }

                // Para otros tipos de pago
                var mensajeCreacion = venta.RequiereAutorizacion
                    ? $"Venta {venta.Numero} creada. Requiere autorización antes de confirmar."
                    : $"Venta {venta.Numero} creada exitosamente";

                TempData[venta.RequiereAutorizacion ? "Warning" : "Success"] = mensajeCreacion;
                return RedirectToAction(nameof(Details), new { id = venta.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear venta");
                ModelState.AddModelError("", "Error al crear la venta: " + ex.Message);
                await CargarViewBags(viewModel.ClienteId, vendedorUserIdSeleccionado: viewModel.VendedorUserId);
                ViewBag.IvaRate = VentaConstants.IVA_RATE;
                return View("Create_tw", viewModel);
            }
        }

        // GET: Venta/Create
        [HttpGet]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionCrear)]
        public async Task<IActionResult> Create()
        {
            var cajaGuard = await RedirigirSiCajaCerradaAsync(
                "Debe abrir una caja antes de crear una venta.",
                nameof(Index));
            if (cajaGuard != null)
            {
                return cajaGuard;
            }

            await CargarViewBags(vendedorUserIdSeleccionado: GetCurrentUserId());
            ViewBag.IvaRate = VentaConstants.IVA_RATE;
            return View("Create_tw", CrearVentaInicial(EstadoVenta.Presupuesto));
        }

        /// <summary>
        /// Prevalida la aptitud crediticia del cliente sin persistir datos.
        /// Retorna el resultado de la evaluación para informar al vendedor antes de guardar.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PrevalidarCredito(int clienteId, decimal monto)
        {
            try
            {
                if (clienteId <= 0)
                {
                    return BadRequest(new { error = "Debe seleccionar un cliente válido" });
                }

                if (monto <= 0)
                {
                    return BadRequest(new { error = "El monto debe ser mayor a cero" });
                }

                var resultado = await _validacionVentaService.PrevalidarAsync(clienteId, monto);
                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al prevalidar crédito para cliente {ClienteId}", clienteId);
                return StatusCode(500, new { error = "Error interno al validar aptitud crediticia" });
            }
        }

        // GET: Venta/Edit/5
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionActualizar)]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de editar ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                _logger.LogDebug(
                    "Edit(GET) venta {Id} requested. Path:{Path} Query:{Query} User:{User}",
                    id,
                    Request.Path.Value ?? string.Empty,
                    Request.QueryString.HasValue ? Request.QueryString.Value : string.Empty,
                    User?.Identity?.Name ?? "anonymous");

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    _logger.LogWarning("Edit(GET) venta {Id} not found", id);
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (venta.Estado != EstadoVenta.Cotizacion && venta.Estado != EstadoVenta.Presupuesto)
                {
                    _logger.LogWarning(
                        "Edit(GET) venta {Id} estado no editable. Estado:{Estado}",
                        id,
                        venta.Estado);
                    TempData["Error"] = "Solo se pueden editar ventas en estado Cotización o Presupuesto";
                    return RedirectToAction(nameof(Details), new { id });
                }

                await CargarViewBags(
                    venta.ClienteId,
                    venta.Detalles.Select(d => d.ProductoId).Distinct(),
                    venta.VendedorUserId);
                ViewBag.IvaRate = VentaConstants.IVA_RATE;
                var ventaJson = JsonSerializer.Serialize(venta, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
                _logger.LogDebug(
                    "Edit(GET) venta {Id} model loaded. Detalles:{Detalles} TipoPago:{TipoPago} Estado:{Estado} RowVersion:{RowVersionLength} Data:{VentaJson}",
                    id,
                    venta.Detalles.Count,
                    venta.TipoPago,
                    venta.Estado,
                    venta.RowVersion?.Length ?? 0,
                    ventaJson);
                return View("Edit_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar venta para editar: {Id}", id);
                TempData["Error"] = "Error al cargar la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionActualizar)]
        public async Task<IActionResult> Edit(int id, VentaViewModel viewModel)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de editar ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                _logger.LogDebug(
                    "Edit(POST) venta {Id} received. ModelStateValid:{ModelStateValid} Detalles:{Detalles} TipoPago:{TipoPago} RowVersion:{RowVersionLength}",
                    id,
                    ModelState.IsValid,
                    viewModel.Detalles?.Count ?? 0,
                    viewModel.TipoPago,
                    viewModel.RowVersion?.Length ?? 0);

                if (!ModelState.IsValid || !ValidarDetalles(viewModel))
                {
                    var errors = ModelState
                        .Where(kvp => kvp.Value?.Errors.Count > 0)
                        .Select(kvp => new
                        {
                            Field = kvp.Key,
                            Errors = kvp.Value!.Errors.Select(e => e.ErrorMessage).ToList()
                        })
                        .ToList();
                    var errorsJson = JsonSerializer.Serialize(errors, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                    _logger.LogWarning(
                        "Edit(POST) venta {Id} ModelState invalid. Errors:{Errors}",
                        id,
                        errorsJson);
                    return await RetornarVistaConDatos(viewModel);
                }

                var resultado = await _ventaService.UpdateAsync(id, viewModel);

                if (resultado == null)
                {
                    _logger.LogWarning("Edit(POST) venta {Id} not found on update", id);
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (resultado.TipoPago == TipoPago.CreditoPersonal)
                {
                    var documentacion = await _documentacionService.ProcesarDocumentacionVentaAsync(resultado.Id);

                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id = resultado.Id });

                    if (!documentacion.DocumentacionCompleta)
                    {
                        TempData["Warning"] =
                            $"Falta documentación obligatoria para otorgar crédito: {documentacion.MensajeFaltantes}";

                        return RedirectToAction(
                            "Index",
                            "DocumentoCliente",
                            new { clienteId = resultado.ClienteId, returnToVentaId = resultado.Id, returnUrl = returnToVentaDetailsUrl });
                    }

                    TempData["Success"] = "Venta actualizada. Crédito listo para configurar.";

                    return RedirectToAction(
                        "ConfigurarVenta",
                        "Credito",
                        new { id = documentacion.CreditoId, ventaId = resultado.Id, returnUrl = returnToVentaDetailsUrl });
                }

                TempData["Success"] = "Venta actualizada exitosamente";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar venta: {Id}", id);
                ModelState.AddModelError("", "Error al actualizar la venta: " + ex.Message);
                await CargarViewBags(viewModel.ClienteId, vendedorUserIdSeleccionado: viewModel.VendedorUserId);
                return View("Edit_tw", viewModel);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CalcularFinanciamiento([FromBody] CalculoFinanciamientoViewModel request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Solicitud inválida" });
            }

            try
            {
                var montoFinanciado = _financialCalculationService.ComputeFinancedAmount(request.Total, request.Anticipo);
                var cuota = _financialCalculationService.ComputePmt(request.TasaMensual, request.Cuotas, montoFinanciado);

                var prequalification = _prequalificationService.Evaluate(
                    cuota,
                    request.IngresoNeto,
                    request.OtrasDeudas,
                    request.AntiguedadLaboralMeses);

                return Ok(new
                {
                    financedAmount = montoFinanciado,
                    installment = cuota,
                    prequalification
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ValidarDocumentacionCredito(int ventaId, string? returnUrl = null)
        {
            var venta = await _ventaService.GetByIdAsync(ventaId);
            if (venta == null)
            {
                TempData["Error"] = "Venta no encontrada";
                return RedirectToAction(nameof(Index));
            }

            var safeReturnUrl = GetSafeReturnUrl(returnUrl) ?? Url.Action(nameof(Details), new { id = ventaId });

            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                TempData["Error"] = "La venta no utiliza crédito personal";
                return RedirectToAction(nameof(Details), new { id = ventaId });
            }

            var resultado = await _documentacionService.ProcesarDocumentacionVentaAsync(ventaId);

            if (!resultado.DocumentacionCompleta)
            {
                TempData["Warning"] =
                    $"Falta documentación obligatoria para otorgar crédito: {resultado.MensajeFaltantes}";

                return RedirectToAction(
                    "Index",
                    "DocumentoCliente",
                    new { clienteId = resultado.ClienteId, returnToVentaId = resultado.VentaId, returnUrl = safeReturnUrl });
            }

            TempData["Success"] = resultado.CreditoCreado
                ? "Documentación validada. Crédito creado y pendiente de configuración."
                : "Documentación validada. Crédito listo para configurar.";

            return RedirectToAction("ConfigurarVenta", "Credito", new { id = resultado.CreditoId, ventaId, returnUrl = safeReturnUrl });
        }

        // GET: Venta/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    _logger.LogWarning("Delete(GET) venta {Id} not found", id);
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (venta.Estado != EstadoVenta.Cotizacion && venta.Estado != EstadoVenta.Presupuesto)
                {
                    TempData["Error"] = "Solo se pueden eliminar ventas en estado Cotización o Presupuesto";
                    return RedirectToAction(nameof(Details), new { id });
                }

                return View("Delete_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar venta para eliminar: {Id}", id);
                TempData["Error"] = "Error al cargar la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _ventaService.DeleteAsync(id);
                TempData["Success"] = "Venta eliminada exitosamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar venta: {Id}", id);
                TempData["Error"] = "Error al eliminar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Venta/Confirmar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirmar(int id, bool aplicarExcepcionDocumental = false, string? motivoExcepcionDocumental = null)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de confirmar una venta.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                _logger.LogInformation(
                    "Confirmar(POST) venta {Id} requested. User:{User}",
                    id,
                    User?.Identity?.Name ?? "anonymous");

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    _logger.LogWarning("Confirmar(POST) venta {Id} not found", id);
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                // Para crédito personal, flujo simplificado
                _logger.LogInformation(
                    "Confirmar(POST) venta {Id} loaded. Estado:{Estado} TipoPago:{TipoPago} RequiereAutorizacion:{RequiereAutorizacion} EstadoAutorizacion:{EstadoAutorizacion}",
                    id,
                    venta.Estado,
                    venta.TipoPago,
                    venta.RequiereAutorizacion,
                    venta.EstadoAutorizacion);

                if (venta.TipoPago == TipoPago.CreditoPersonal)
                {
                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id });

                    // REGLA 1: Si está en PendienteFinanciacion → debe configurar primero
                    if (venta.Estado == EstadoVenta.PendienteFinanciacion)
                    {
                        if (!venta.CreditoId.HasValue)
                        {
                            _logger.LogWarning(
                                "Confirmar(POST) venta {Id} sin CreditoId en PendienteFinanciacion",
                                id);
                            TempData["Error"] = "La venta no tiene crédito asociado. Error de datos.";
                            return RedirectToAction(nameof(Details), new { id });
                        }
                        
                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id} pendiente financiacion, redirigiendo a configuracion",
                            id);
                        TempData["Warning"] = "Debe configurar el plan de financiamiento antes de confirmar.";
                        return RedirectToAction(
                            "ConfigurarVenta",
                            "Credito",
                            new { id = venta.CreditoId.Value, ventaId = venta.Id, returnUrl = returnToVentaDetailsUrl });
                    }

                    // Verificar que tiene crédito asociado
                    if (!venta.CreditoId.HasValue)
                    {
                        _logger.LogWarning(
                            "Confirmar(POST) venta {Id} sin CreditoId en flujo credito personal",
                            id);
                        TempData["Error"] = "La venta con crédito personal debe tener un crédito asociado.";
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    var credito = await _creditoService.GetByIdAsync(venta.CreditoId.Value);
                    
                    // REGLA 2: Si el crédito ya está Generado/Activo → venta ya confirmada
                    if (credito != null && (credito.Estado == EstadoCredito.Generado || 
                                            credito.Estado == EstadoCredito.Activo ||
                                            credito.Estado == EstadoCredito.Finalizado))
                    {
                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id} ya confirmada con credito {EstadoCredito}",
                            id,
                            credito.Estado);
                        TempData["Info"] = "Esta venta ya fue confirmada con crédito generado.";
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    // REGLA 3: Si financiación NO configurada → redirigir a configurar
                    if (!venta.FinanciamientoConfigurado && 
                        (credito == null || credito.Estado == EstadoCredito.PendienteConfiguracion))
                    {
                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id} sin financiamiento configurado, redirigiendo a configuracion",
                            id);
                        TempData["Warning"] = "El crédito debe configurarse antes de confirmar la venta.";
                        return RedirectToAction(
                            "ConfigurarVenta",
                            "Credito",
                            new { id = venta.CreditoId.Value, ventaId = venta.Id, returnUrl = returnToVentaDetailsUrl });
                    }

                    // REGLA 4: Financiación configurada → confirmar y generar cuotas
                    var validacionConfirmacion = await _validacionVentaService.ValidarConfirmacionVentaAsync(id);
                    var usuarioActual = User ?? new ClaimsPrincipal(new ClaimsIdentity());
                    var puedeAplicarExcepcionDocumental = usuarioActual.TienePermiso(ModuloVentas, AccionAutorizar);
                    var excepcionDocumentalRegistrada = !string.IsNullOrWhiteSpace(venta.MotivoAutorizacion)
                        && venta.MotivoAutorizacion.Contains("EXCEPCION_DOC|", StringComparison.Ordinal);
                    var soloDocumentacionFaltante = validacionConfirmacion.RequisitosPendientes.Any()
                        && validacionConfirmacion.RequisitosPendientes.All(r =>
                            r.Tipo == TipoRequisitoPendiente.DocumentacionFaltante);

                    var excepcionDocumentalAplicada = false;
                    if (aplicarExcepcionDocumental && puedeAplicarExcepcionDocumental && soloDocumentacionFaltante)
                    {
                        if (string.IsNullOrWhiteSpace(motivoExcepcionDocumental))
                        {
                            TempData["Error"] = "Debe ingresar un motivo para aplicar la excepción documental.";
                            return RedirectToAction(nameof(Details), new { id });
                        }

                        var usuarioAutoriza = User?.Identity?.Name ?? "desconocido";
                        var motivoNormalizado = motivoExcepcionDocumental.Trim();
                        var auditoriaRegistrada = await _ventaService.RegistrarExcepcionDocumentalAsync(
                            id,
                            usuarioAutoriza,
                            motivoNormalizado);

                        if (!auditoriaRegistrada)
                        {
                            TempData["Error"] = "No se pudo registrar la auditoría de excepción documental.";
                            return RedirectToAction(nameof(Details), new { id });
                        }

                        validacionConfirmacion.NoViable = false;
                        validacionConfirmacion.PendienteRequisitos = false;
                        excepcionDocumentalAplicada = true;

                        _logger.LogWarning(
                            "Confirmar(POST) venta {Id}: se aplica excepción documental por usuario {Usuario}. Motivo: {Motivo}",
                            id,
                            usuarioAutoriza,
                            motivoNormalizado);
                    }
                    else if (excepcionDocumentalRegistrada && soloDocumentacionFaltante)
                    {
                        validacionConfirmacion.NoViable = false;
                        validacionConfirmacion.PendienteRequisitos = false;
                        excepcionDocumentalAplicada = true;

                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id}: se reutiliza excepción documental registrada previamente.",
                            id);
                    }

                    if (validacionConfirmacion.NoViable || validacionConfirmacion.PendienteRequisitos)
                    {
                        TempData["Error"] = validacionConfirmacion.MensajeResumen;
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    var resultadoCredito = await _ventaService.ConfirmarVentaCreditoAsync(id);
                    _logger.LogInformation(
                        "Confirmar(POST) venta {Id} resultado confirmacion credito {Resultado}",
                        id,
                        resultadoCredito);
                    if (resultadoCredito)
                    {
                        TempData[excepcionDocumentalAplicada ? "Warning" : "Success"] = excepcionDocumentalAplicada
                            ? "Venta confirmada por excepción documental autorizada. Crédito generado con cuotas."
                            : "Venta confirmada. Crédito generado con cuotas.";
                    }
                    else
                    {
                        TempData["Error"] = "No se pudo confirmar la venta con crédito";
                    }
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Para otros tipos de pago
                var resultado = await _ventaService.ConfirmarVentaAsync(id);
                _logger.LogInformation(
                    "Confirmar(POST) venta {Id} resultado confirmacion {Resultado}",
                    id,
                    resultado);
                if (resultado)
                {
                    TempData["Success"] = "Venta confirmada exitosamente. El stock ha sido descontado.";
                }
                else
                {
                    TempData["Error"] = "No se pudo confirmar la venta";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar venta: {Id}", id);
                TempData["Error"] = "Error al confirmar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Venta/Cancelar/5
        public async Task<IActionResult> Cancelar(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                return View("Cancelar_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar venta para cancelar: {Id}", id);
                TempData["Error"] = "Error al cargar la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Cancelar/5
        [HttpPost, ActionName("Cancelar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarConfirmed(int id, string motivo)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "Debe indicar el motivo de la cancelación";
                    return RedirectToAction(nameof(Cancelar), new { id });
                }

                var resultado = await _ventaService.CancelarVentaAsync(id, motivo);
                if (resultado)
                {
                    TempData["Success"] = "Venta cancelada exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se pudo cancelar la venta";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar venta: {Id}", id);
                TempData["Error"] = "Error al cancelar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Venta/Autorizar/5
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionAutorizar)]
        public async Task<IActionResult> Autorizar(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (venta.EstadoAutorizacion != EstadoAutorizacionVenta.PendienteAutorizacion)
                {
                    TempData["Error"] = "La venta no está pendiente de autorización";
                    return RedirectToAction(nameof(Details), new { id });
                }

                return View("Autorizar_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar venta para autorizar: {Id}", id);
                TempData["Error"] = "Error al cargar la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Autorizar/5
        [HttpPost, ActionName("Autorizar")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionAutorizar)]
        public async Task<IActionResult> AutorizarConfirmed(int id, string motivo)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "Debe indicar el motivo de la autorización";
                    return RedirectToAction(nameof(Autorizar), new { id });
                }

                var usuarioAutoriza = User.Identity?.Name ?? Roles.Administrador;

                var resultado = await _ventaService.AutorizarVentaAsync(id, usuarioAutoriza, motivo);
                if (resultado)
                {
                    TempData["Success"] = "Venta autorizada exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se pudo autorizar la venta";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al autorizar venta: {Id}", id);
                TempData["Error"] = "Error al autorizar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Venta/Rechazar/5
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionRechazar)]
        public async Task<IActionResult> Rechazar(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (venta.EstadoAutorizacion != EstadoAutorizacionVenta.PendienteAutorizacion)
                {
                    TempData["Error"] = "La venta no está pendiente de autorización";
                    return RedirectToAction(nameof(Details), new { id });
                }

                return View("Rechazar_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar venta para rechazar: {Id}", id);
                TempData["Error"] = "Error al cargar la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Rechazar/5
        [HttpPost, ActionName("Rechazar")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionRechazar)]
        public async Task<IActionResult> RechazarConfirmed(int id, string motivo)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "Debe indicar el motivo del rechazo";
                    return RedirectToAction(nameof(Rechazar), new { id });
                }

                var usuarioAutoriza = User.Identity?.Name ?? Roles.Administrador;

                var resultado = await _ventaService.RechazarVentaAsync(id, usuarioAutoriza, motivo);
                if (resultado)
                {
                    TempData["Success"] = "Venta rechazada";
                }
                else
                {
                    TempData["Error"] = "No se pudo rechazar la venta";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar venta: {Id}", id);
                TempData["Error"] = "Error al rechazar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Venta/Facturar/5
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionFacturar)]
        public async Task<IActionResult> Facturar(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de facturar una venta.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                var venta = await _ventaService.GetByIdAsync(id);
                if (venta == null)
                {
                    TempData["Error"] = "Venta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (venta.Estado != EstadoVenta.Confirmada)
                {
                    TempData["Error"] = "Solo se pueden facturar ventas confirmadas";
                    return RedirectToAction(nameof(Details), new { id });
                }

                if (venta.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
                {
                    TempData["Error"] = "La venta requiere autorización antes de ser facturada";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var facturaViewModel = new FacturaViewModel
                {
                    VentaId = venta.Id,
                    FechaEmision = DateTime.Today,
                    Tipo = TipoFactura.B,
                    Subtotal = venta.Subtotal,
                    IVA = venta.IVA,
                    Total = venta.Total
                };

                ViewBag.Venta = venta;
                ViewBag.TiposFactura = new SelectList(Enum.GetValues(typeof(TipoFactura)));

                return View("Facturar_tw", facturaViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de facturación: {Id}", id);
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Venta/Facturar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionFacturar)]
        public async Task<IActionResult> Facturar(FacturaViewModel facturaViewModel)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de facturar una venta.",
                    nameof(Details),
                    new { id = facturaViewModel.VentaId });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (!ModelState.IsValid)
                {
                    var venta = await _ventaService.GetByIdAsync(facturaViewModel.VentaId);
                    ViewBag.Venta = venta;
                    ViewBag.TiposFactura = new SelectList(Enum.GetValues(typeof(TipoFactura)));
                    return View("Facturar_tw", facturaViewModel);
                }

                var resultado = await _ventaService.FacturarVentaAsync(facturaViewModel.VentaId, facturaViewModel);
                if (resultado)
                {
                    TempData["Success"] = "Factura generada exitosamente";
                    return RedirectToAction(nameof(Details), new { id = facturaViewModel.VentaId });
                }
                else
                {
                    TempData["Error"] = "No se pudo generar la factura";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al facturar venta");
                ModelState.AddModelError("", "Error al generar la factura: " + ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionFacturar)]
        public async Task<IActionResult> AnularFactura(int facturaId, string motivo)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de operar sobre ventas.",
                    nameof(Index));
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    TempData["Error"] = "Debe indicar el motivo de la anulación.";
                    return RedirectToAction(nameof(Index));
                }

                var ventaId = await _ventaService.AnularFacturaAsync(facturaId, motivo);
                if (!ventaId.HasValue)
                {
                    TempData["Error"] = "Factura no encontrada.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = "Factura anulada exitosamente.";
                return RedirectToAction(nameof(Details), new { id = ventaId.Value });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Intento inválido de anular factura {FacturaId}", facturaId);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al anular factura {FacturaId}", facturaId);
                TempData["Error"] = "Error al anular la factura: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: API endpoint para obtener tarjetas activas
        [HttpGet]
        public async Task<IActionResult> GetTarjetasActivas()
        {
            try
            {
                var tarjetas = await _configuracionPagoService.GetTarjetasActivasAsync();

                var resultado = tarjetas.Select(t => new
                {
                    id = t.Id,
                    nombre = t.NombreTarjeta,
                    tipo = t.TipoTarjeta,
                    permiteCuotas = t.PermiteCuotas,
                    cantidadMaximaCuotas = t.CantidadMaximaCuotas,
                    tipoCuota = t.TipoCuota,
                    tasaInteres = t.TasaInteresesMensual,
                    tieneRecargo = t.TieneRecargoDebito,
                    porcentajeRecargo = t.PorcentajeRecargoDebito
                });

                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tarjetas activas");
                return StatusCode(500, "Error al obtener las tarjetas");
            }
        }

        #region Métodos Privados

        private async Task CargarViewBags(
            int? clienteIdSeleccionado = null,
            IEnumerable<int>? productoIdsIncluidos = null,
            string? vendedorUserIdSeleccionado = null)
        {
            var creditosCount = 0;
            var vendedoresCount = 0;

            // Usar el servicio centralizado para obtener clientes ya formateados
            var clientes = await _clienteLookup.GetClientesSelectListAsync(clienteIdSeleccionado);
            ViewBag.Clientes = new SelectList(clientes, "Value", "Text", clienteIdSeleccionado?.ToString());

            var productos = await _productoService.SearchAsync(soloActivos: true, orderBy: "nombre");

            var productoIdsIncluidosSet = productoIdsIncluidos != null
                ? new HashSet<int>(productoIdsIncluidos)
                : null;

            ViewBag.Productos = new SelectList(
                productos
                    .Where(p => p.StockActual > 0 || (productoIdsIncluidosSet != null && productoIdsIncluidosSet.Contains(p.Id)))
                    .Select(p => new
                    {
                        p.Id,
                        Detalle = $"{p.Codigo} - {p.Nombre} (Stock: {p.StockActual}) - ${p.PrecioVenta:N2}"
                    }),
                "Id",
                "Detalle");

            var categoriasFiltro = productos
                .Where(p => p.Categoria != null)
                .Select(p => p.Categoria!)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .OrderBy(c => c.Nombre)
                .Select(c => new { c.Id, c.Nombre })
                .ToList();

            var marcasFiltro = productos
                .Where(p => p.Marca != null)
                .Select(p => p.Marca!)
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .OrderBy(m => m.Nombre)
                .Select(m => new { m.Id, m.Nombre })
                .ToList();

            ViewBag.CategoriasFiltro = new SelectList(categoriasFiltro, "Id", "Nombre");
            ViewBag.MarcasFiltro = new SelectList(marcasFiltro, "Id", "Nombre");
            ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();

            // Cargar créditos disponibles del cliente si hay uno seleccionado
            if (clienteIdSeleccionado.HasValue)
            {
                var creditosDisponibles = (await _creditoService.GetByClienteIdAsync(clienteIdSeleccionado.Value))
                    .Where(c => (c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Aprobado)
                                && c.SaldoPendiente > 0)
                    .OrderByDescending(c => c.FechaAprobacion ?? DateTime.MinValue)
                    .Select(c => new
                    {
                        c.Id,
                        Detalle = $"{c.Numero} - Saldo: ${c.SaldoPendiente:N2}"
                    })
                    .ToList();

                ViewBag.Creditos = new SelectList(creditosDisponibles, "Id", "Detalle");
                creditosCount = creditosDisponibles.Count;
            }
            else
            {
                ViewBag.Creditos = new SelectList(Enumerable.Empty<SelectListItem>());
            }

            // Cargar tarjetas activas
            var tarjetas = await _configuracionPagoService.GetTarjetasActivasAsync();
            var tarjetasDisponibles = tarjetas
                .Select(t => new
                {
                    t.Id,
                    Detalle = $"{t.NombreTarjeta} ({t.TipoTarjeta})"
                })
                .ToList();
            ViewBag.Tarjetas = new SelectList(tarjetasDisponibles, "Id", "Detalle");

            var puedeDelegarVendedor = User.IsInRole(Roles.SuperAdmin) ||
                                       User.IsInRole(Roles.Administrador) ||
                                       User.IsInRole(Roles.Gerente);
            ViewBag.PuedeDelegarVendedor = puedeDelegarVendedor;

            if (puedeDelegarVendedor)
            {
                var usuarios = await _userManager.GetUsersInRoleAsync(Roles.Vendedor);
                var usuariosOrdenados = usuarios
                    .OrderBy(u => u.UserName)
                    .ToList();

                ViewBag.Vendedores = new SelectList(
                    usuariosOrdenados,
                    "Id",
                    "UserName",
                    vendedorUserIdSeleccionado);
                vendedoresCount = usuariosOrdenados.Count;
            }

            _logger.LogDebug(
                "CargarViewBags ClienteId:{ClienteId} Clientes:{Clientes} ProductosTotal:{ProductosTotal} ProductosIncluidos:{ProductosIncluidos} Creditos:{Creditos} Tarjetas:{Tarjetas} Vendedores:{Vendedores}",
                clienteIdSeleccionado,
                clientes.Count(),
                productos.Count(),
                productoIdsIncluidos?.Distinct().Count() ?? 0,
                creditosCount,
                tarjetasDisponibles.Count,
                vendedoresCount);
        }

        #endregion
        // GET: API endpoint para calcular crédito personal
        [HttpGet]
        public async Task<IActionResult> CalcularCreditoPersonall(int creditoId, decimal monto, int cuotas, string fechaPrimeraCuota)
        {
            try
            {
                if (!DateTime.TryParse(fechaPrimeraCuota, out DateTime fecha))
                    fecha = DateTime.Today.AddMonths(1);

                var resultado = await _ventaService.CalcularCreditoPersonallAsync(creditoId, monto, cuotas, fecha);

                return Json(new
                {
                    creditoNumero = resultado.CreditoNumero,
                    creditoTotalAsignado = resultado.CreditoTotalAsignado,
                    creditoDisponible = resultado.CreditoDisponible,
                    montoAFinanciar = resultado.MontoAFinanciar,
                    cantidadCuotas = resultado.CantidadCuotas,
                    montoCuota = resultado.MontoCuota,
                    tasaInteres = resultado.TasaInteresMensual,
                    totalAPagar = resultado.TotalAPagar,
                    interesTotal = resultado.InteresTotal,
                    saldoRestante = resultado.SaldoRestante,
                    cuotas = resultado.Cuotas.Select(c => new
                    {
                        numeroCuota = c.NumeroCuota,
                        fechaVencimiento = c.FechaVencimiento.ToString("dd/MM/yyyy"),
                        monto = c.Monto,
                        saldo = c.Saldo
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular crédito personal");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET: API endpoint para validar disponibilidad de crédito
        [HttpGet]
        public async Task<IActionResult> ValidarCreditoDisponible(int creditoId, decimal monto)
        {
            try
            {
                var disponible = await _ventaService.ValidarDisponibilidadCreditoAsync(creditoId, monto);

                return Json(new
                {
                    disponible = disponible,
                    mensaje = disponible
                        ? "Crédito suficiente"
                        : "Crédito insuficiente para este monto"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al validar crédito");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Maneja el resultado de una venta con crédito personal según el nuevo sistema unificado
        /// </summary>
        private IActionResult ManejarResultadoVentaCreditoPersonal(VentaViewModel venta, string? returnUrl)
        {
            var validacion = venta.ValidacionCredito;

            // Caso 1: Venta pendiente de requisitos (documentación, cupo, etc.)
            if (venta.Estado == EstadoVenta.PendienteRequisitos)
            {
                var requisitos = validacion?.RequisitosPendientes ?? new List<RequisitoPendiente>();
                var primerRequisito = requisitos.FirstOrDefault();

                TempData["Warning"] = $"Venta {venta.Numero} creada con requisitos pendientes.";
                TempData["Info"] = validacion?.MensajeResumen ?? "Hay requisitos pendientes para completar la venta.";

                // Redirigir según el tipo de requisito principal
                if (primerRequisito?.Tipo == TipoRequisitoPendiente.DocumentacionFaltante)
                {
                    return RedirectToAction(
                        "Index",
                        "DocumentoCliente",
                        new { clienteId = venta.ClienteId, returnToVentaId = venta.Id, returnUrl });
                }

                if (primerRequisito?.Tipo == TipoRequisitoPendiente.SinLimiteCredito)
                {
                    return RedirectToAction(
                        "Details",
                        "Cliente",
                        new { id = venta.ClienteId, returnUrl });
                }

                // Para otros casos, ir al detalle de la venta
                return RedirectToAction(nameof(Details), new { id = venta.Id });
            }

            // Caso 2: Venta requiere autorización
            if (venta.RequiereAutorizacion)
            {
                TempData["Warning"] = $"Venta {venta.Numero} creada. Requiere autorización.";
                TempData["Info"] = validacion?.MensajeResumen ?? "La venta requiere autorización de un supervisor.";
                return RedirectToAction(nameof(Details), new { id = venta.Id });
            }

            // Caso 3: Venta puede proceder - ir a configurar crédito
            TempData["Success"] = $"Venta {venta.Numero} creada exitosamente.";
            TempData["Info"] = "Cliente apto para crédito. Configure los detalles del financiamiento.";

            // Si ya tiene crédito asociado, ir a configurar
            if (venta.CreditoId.HasValue)
            {
                return RedirectToAction(
                    "ConfigurarVenta",
                    "Credito",
                    new { id = venta.CreditoId, ventaId = venta.Id, returnUrl });
            }

            return RedirectToAction(nameof(Details), new { id = venta.Id });
        }

        private VentaViewModel CrearVentaInicial(EstadoVenta estadoInicial)
        {
            return new VentaViewModel
            {
                FechaVenta = DateTime.Today,
                Estado = estadoInicial,
                TipoPago = TipoPago.Efectivo
            };
        }

        private bool ValidarDetalles(VentaViewModel viewModel)
        {
            if (viewModel.Detalles != null && viewModel.Detalles.Any())
            {
                return true;
            }

            ModelState.AddModelError("", "Debe agregar al menos un producto a la venta");
            return false;
        }

        private async Task<IActionResult> RetornarVistaConDatos(VentaViewModel viewModel)
        {
            await CargarViewBags(
                viewModel.ClienteId,
                viewModel.Detalles.Select(d => d.ProductoId).Distinct(),
                viewModel.VendedorUserId);
            ViewBag.IvaRate = VentaConstants.IVA_RATE;
            return View("Create_tw", viewModel);
        }
    }
}

