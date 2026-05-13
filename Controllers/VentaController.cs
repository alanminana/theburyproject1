using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
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
        private readonly ILogger<VentaController> _logger;
        private readonly IFinancialCalculationService _financialCalculationService;
        private readonly IPrequalificationService _prequalificationService;
        private readonly ICreditoService _creditoService;
        private readonly IDocumentacionService _documentacionService;
        private readonly IClienteLookupService _clienteLookup;
        private readonly IValidacionVentaService _validacionVentaService;
        private readonly ICurrentUserService _currentUser;
        private readonly ICajaService _cajaService;
        private readonly VentaViewBagBuilder _viewBagBuilder;
        private readonly IContratoVentaCreditoService _contratoVentaCreditoService;
        private readonly AppDbContext _context;

        #region Helpers de caja

        private async Task<bool> UsuarioTieneCajaAbiertaAsync()
        {
            var userName = _currentUser.GetUsername();
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
            ILogger<VentaController> logger,
            IFinancialCalculationService financialCalculationService,
            IPrequalificationService prequalificationService,
            ICreditoService creditoService,
            IDocumentacionService documentacionService,
            IClienteLookupService clienteLookup,
            IValidacionVentaService validacionVentaService,
            ICurrentUserService currentUser,
            ICajaService cajaService,
            VentaViewBagBuilder viewBagBuilder,
            IContratoVentaCreditoService contratoVentaCreditoService,
            AppDbContext context)
        {
            _ventaService = ventaService;
            _logger = logger;
            _financialCalculationService = financialCalculationService;
            _prequalificationService = prequalificationService;
            _creditoService = creditoService;
            _documentacionService = documentacionService;
            _clienteLookup = clienteLookup;
            _validacionVentaService = validacionVentaService;
            _currentUser = currentUser;
            _cajaService = cajaService;
            _viewBagBuilder = viewBagBuilder;
            _contratoVentaCreditoService = contratoVentaCreditoService;
            _context = context;
        }

        #endregion

        #region Index / Detalle

        // GET: Venta
        public async Task<IActionResult> Index(VentaFilterViewModel filter)
        {
            try
            {
                var userName = _currentUser.GetUsername();

                // Ejecutar secuencialmente: el DbContext compartido no soporta operaciones concurrentes.
                var ventas = await _ventaService.GetAllAsync(filter);
                ViewBag.Clientes = await _clienteLookup.GetClientesSelectListAsync();
                var aperturaActiva = !string.IsNullOrWhiteSpace(userName)
                    ? await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(userName)
                    : null;

                ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoVenta)));
                ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                ViewBag.EstadosAutorizacion = new SelectList(Enum.GetValues(typeof(EstadoAutorizacionVenta)));
                ViewBag.Filter = filter;

                ViewBag.PuedeCrearVenta = aperturaActiva != null;
                ViewBag.PuedeOperarVentas = aperturaActiva != null;
                ViewBag.UserNameCaja = userName;

                // Cargar datos del formulario de creación solo cuando hay caja abierta
                if (aperturaActiva != null)
                {
                    await CargarViewBags(vendedorUserIdSeleccionado: _currentUser.GetUserId());
                    // Restaurar TiposPago/Estados sobreescritos por CargarViewBags
                    ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoVenta)));
                    ViewBag.EstadosAutorizacion = new SelectList(Enum.GetValues(typeof(EstadoAutorizacionVenta)));
                }

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
                ViewBag.ContratoVentaCredito = await ObtenerContratoResumenPorVentaAsync(id);
                return View("Details_tw", venta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la venta {Id}", id);
                TempData["Error"] = "Error al cargar los detalles de la venta";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<ContratoVentaCreditoResumenViewModel?> ObtenerContratoResumenPorVentaAsync(int ventaId)
        {
            var contrato = await _contratoVentaCreditoService.ObtenerContratoPorVentaAsync(ventaId);
            if (contrato == null)
                return null;

            return new ContratoVentaCreditoResumenViewModel
            {
                VentaId = contrato.VentaId,
                CreditoId = contrato.CreditoId,
                NumeroContrato = contrato.NumeroContrato,
                NumeroPagare = contrato.NumeroPagare,
                FechaGeneracionUtc = contrato.FechaGeneracionUtc,
                UsuarioGeneracion = contrato.UsuarioGeneracion,
                EstadoDocumento = contrato.EstadoDocumento,
                NombreArchivo = contrato.NombreArchivo,
                ContentHash = contrato.ContentHash
            };
        }

        #endregion

        #region Cotizar / Crear

        // GET: Venta/Cotizar
        [HttpGet]
        public async Task<IActionResult> Cotizar()
        {
            await CargarViewBags(vendedorUserIdSeleccionado: _currentUser.GetUserId());
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

                LimpiarModelStateSegunTipoPago(viewModel.TipoPago, viewModel);

                var puedeDelegar = _currentUser.IsInRole(Roles.SuperAdmin) ||
                                   _currentUser.IsInRole(Roles.Administrador) ||
                                   _currentUser.IsInRole(Roles.Gerente);
                if (puedeDelegar && string.IsNullOrWhiteSpace(viewModel.VendedorUserId))
                    ModelState.AddModelError("VendedorUserId", "Debe seleccionar un vendedor.");

                _logger.LogInformation(
                    "Create POST: TipoPago={TipoPago} AplicarExcepcion={Excepcion} Motivo={Motivo}",
                    viewModel.TipoPago,
                    viewModel.AplicarExcepcionDocumental,
                    viewModel.MotivoExcepcionDocumentalCreate ?? "(vacío)");

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
            catch (CondicionesPagoVentaException ex)
            {
                var mensaje = CrearMensajePresentacionCondicionesPago(ex.Message);
                _logger.LogWarning(ex, "Venta rechazada por condiciones de pago en Create");
                ModelState.AddModelError("", mensaje);
                await CargarViewBags(viewModel.ClienteId, vendedorUserIdSeleccionado: viewModel.VendedorUserId);
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear venta");
                ModelState.AddModelError("", "Error al crear la venta: " + ex.Message);
                await CargarViewBags(viewModel.ClienteId, vendedorUserIdSeleccionado: viewModel.VendedorUserId);
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

            await CargarViewBags(vendedorUserIdSeleccionado: _currentUser.GetUserId());
            return View("Create_tw", CrearVentaInicial(EstadoVenta.Presupuesto));
        }

        // POST: Venta/CreateAjax — version AJAX para el modal del Index
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionCrear)]
        public async Task<IActionResult> CreateAjax(VentaViewModel viewModel)
        {
            try
            {
                if (!await UsuarioTieneCajaAbiertaAsync())
                {
                    return Json(new
                    {
                        success = false,
                        errors = new Dictionary<string, string[]>
                        {
                            { "", new[] { "Debe abrir una caja antes de crear una venta." } }
                        }
                    });
                }

                LimpiarModelStateSegunTipoPago(viewModel.TipoPago, viewModel);

                var puedeDelegar = _currentUser.IsInRole(Roles.SuperAdmin) ||
                                   _currentUser.IsInRole(Roles.Administrador) ||
                                   _currentUser.IsInRole(Roles.Gerente);
                if (puedeDelegar && string.IsNullOrWhiteSpace(viewModel.VendedorUserId))
                    ModelState.AddModelError("VendedorUserId", "Debe seleccionar un vendedor.");

                if (!ModelState.IsValid || !ValidarDetalles(viewModel))
                {
                    var errors = ModelState
                        .Where(k => k.Value?.Errors.Any() == true)
                        .ToDictionary(
                            k => k.Key,
                            k => k.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                    return Json(new { success = false, errors });
                }

                var venta = await _ventaService.CreateAsync(viewModel);

                if (venta.TipoPago == TipoPago.CreditoPersonal && venta.CreditoId.HasValue)
                {
                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id = venta.Id });
                    var redirectUrl = Url.Action(
                        "ConfigurarVenta", "Credito",
                        new { id = venta.CreditoId, ventaId = venta.Id, returnUrl = returnToVentaDetailsUrl });

                    var msg = venta.RequiereAutorizacion
                        ? $"Venta {venta.Numero} creada. Requiere autorización. Configure el plan de pago."
                        : $"Venta {venta.Numero} creada. Configure el plan de financiamiento.";

                    return Json(new { success = true, requiresRedirect = true, redirectUrl, message = msg });
                }

                var detailsUrl = Url.Action(nameof(Details), new { id = venta.Id });
                var mensaje = venta.RequiereAutorizacion
                    ? $"Venta {venta.Numero} creada. Requiere autorización antes de confirmar."
                    : $"Venta {venta.Numero} creada exitosamente";

                return Json(new { success = true, requiresRedirect = true, redirectUrl = detailsUrl, message = mensaje });
            }
            catch (CondicionesPagoVentaException ex)
            {
                var mensaje = CrearMensajePresentacionCondicionesPago(ex.Message);
                _logger.LogWarning(ex, "Venta rechazada por condiciones de pago en CreateAjax");
                return Json(new
                {
                    success = false,
                    message = mensaje,
                    errors = new Dictionary<string, string[]>
                    {
                        { "", new[] { mensaje } }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear venta via AJAX");
                return Json(new
                {
                    success = false,
                    errors = new Dictionary<string, string[]>
                    {
                        { "", new[] { "Error al crear la venta: " + ex.Message } }
                    }
                });
            }
        }

        #endregion

        #region Editar

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
                    _currentUser.GetUsername());

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

                LimpiarModelStateSegunTipoPago(viewModel.TipoPago, viewModel);

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
                    await CargarViewBags(
                        viewModel.ClienteId,
                        viewModel.Detalles?.Select(d => d.ProductoId).Distinct(),
                        viewModel.VendedorUserId);
                    return View("Edit_tw", viewModel);
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

            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl) ?? Url.Action(nameof(Details), new { id = ventaId });

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

        #endregion

        #region Eliminar

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

        #endregion

        #region Confirmar

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
                    _currentUser.GetUsername());

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

                    // REGLA 3.5: Crédito configurado → contrato obligatorio antes de confirmar
                    var contratoGenerado = await _contratoVentaCreditoService.ExisteContratoGeneradoAsync(id);
                    if (!contratoGenerado)
                    {
                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id}: contrato no generado, redirigiendo a Preparar",
                            id);
                        TempData["Warning"] = "Debe generar e imprimir el contrato antes de confirmar la operación con Crédito Personal.";
                        return RedirectToAction("Preparar", "ContratoVentaCredito", new { ventaId = id });
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

                        var usuarioAutoriza = _currentUser.GetUsername();
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
            catch (CondicionesPagoVentaException ex)
            {
                _logger.LogWarning(ex, "Venta {Id} rechazada por condiciones de pago al confirmar", id);
                TempData["Error"] = CrearMensajePresentacionCondicionesPago(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar venta: {Id}", id);
                TempData["Error"] = "Error al confirmar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        #endregion

        #region Cancelar

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

        #endregion

        #region Autorizar / Rechazar

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

                var usuarioAutoriza = _currentUser.GetUsername();

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

                var usuarioAutoriza = _currentUser.GetUsername();

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

        #endregion

        #region Facturar

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
                    Total = venta.Total,
                    ResumenAlicuotas = FacturaAlicuotaResumenBuilder.Build(venta.Detalles)
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

        [HttpGet]
        public async Task<IActionResult> ComprobanteFactura(int facturaId)
        {
            var factura = await _context.Facturas
                .AsNoTracking()
                .Include(f => f.Venta)
                    .ThenInclude(v => v.Cliente)
                .Include(f => f.Venta)
                    .ThenInclude(v => v.DatosTarjeta)
                .Include(f => f.Venta)
                    .ThenInclude(v => v.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                .FirstOrDefaultAsync(f => f.Id == facturaId && !f.IsDeleted);

            if (factura == null)
            {
                return NotFound();
            }

            var viewModel = FacturaComprobanteBuilder.Build(factura);
            return View("ComprobanteFactura_tw", viewModel);
        }

        #endregion

        #region Métodos privados

        private async Task CargarViewBags(
            int? clienteIdSeleccionado = null,
            IEnumerable<int>? productoIdsIncluidos = null,
            string? vendedorUserIdSeleccionado = null)
        {
            await _viewBagBuilder.CargarAsync(ViewBag, clienteIdSeleccionado, productoIdsIncluidos, vendedorUserIdSeleccionado);
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

        /// <summary>
        /// Elimina del ModelState los errores de validación de los sub-modelos
        /// que no corresponden al tipo de pago seleccionado.
        /// DatosTarjeta, DatosCheque y CreditoId tienen [Required] internamente,
        /// pero solo deben validarse cuando su tipo de pago está activo.
        /// </summary>
        private void LimpiarModelStateSegunTipoPago(TipoPago tipoPago, VentaViewModel? viewModel = null)
        {
            bool esTarjeta = tipoPago == TipoPago.TarjetaCredito
                          || tipoPago == TipoPago.TarjetaDebito
                          || tipoPago == TipoPago.MercadoPago
                          || tipoPago == TipoPago.Tarjeta;
            bool esCheque = tipoPago == TipoPago.Cheque;

            if (!esTarjeta)
            {
                foreach (var key in ModelState.Keys
                    .Where(k => k.StartsWith("DatosTarjeta.", StringComparison.OrdinalIgnoreCase))
                    .ToList())
                    ModelState.Remove(key);

                if (viewModel != null) viewModel.DatosTarjeta = null;
            }

            if (!esCheque)
            {
                foreach (var key in ModelState.Keys
                    .Where(k => k.StartsWith("DatosCheque.", StringComparison.OrdinalIgnoreCase))
                    .ToList())
                    ModelState.Remove(key);

                if (viewModel != null) viewModel.DatosCheque = null;
            }

            // CreditoId nunca es obligatorio: el sistema crea el crédito automáticamente
            ModelState.Remove("CreditoId");
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

        private static string CrearMensajePresentacionCondicionesPago(string mensajeBackend)
        {
            var mensaje = mensajeBackend.Trim();
            return mensaje.StartsWith("Condiciones de pago del producto:", StringComparison.OrdinalIgnoreCase)
                ? mensaje
                : $"Condiciones de pago del producto: {mensaje}";
        }

        private async Task<IActionResult> RetornarVistaConDatos(VentaViewModel viewModel)
        {
            await CargarViewBags(
                viewModel.ClienteId,
                viewModel.Detalles.Select(d => d.ProductoId).Distinct(),
                viewModel.VendedorUserId);
            return View("Create_tw", viewModel);
        }

        #endregion
    }
}

