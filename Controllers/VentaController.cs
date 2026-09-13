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
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
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
        private const string AccionCancelar = "cancel";
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
        private readonly IRelojComercial _reloj;

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
            AppDbContext context,
            IRelojComercial reloj)
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
            _reloj = reloj;
        }

        #endregion

        #region Index / Detalle

        // GET: Venta
        public async Task<IActionResult> Index(VentaFilterViewModel filter)
        {
            try
            {
                var userName = _currentUser.GetUsername();

                // VENTA-UI-04B: page size fijo de Operaciones, sin selector de usuario.
                // Se normaliza antes de consultar para que ViewBag.Filter (los links del
                // paginador) ya refleje el valor efectivo aunque llegue otro por querystring.
                filter.PageSize = 20;

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

                // VENTA-UI-04B: paginación server-rendered en memoria, solo para la
                // pestaña Operaciones. "ventas" sigue siendo el conjunto filtrado completo:
                // KPIs, Pendientes, Cotizaciones y Devoluciones lo necesitan íntegro.
                // GetAllAsync y su contrato no cambian; el recorte ocurre acá, después de
                // traer todo.
                var totalRegistros = ventas.Count;
                var totalPaginas = totalRegistros == 0
                    ? 0
                    : (int)Math.Ceiling(totalRegistros / (double)filter.PageSize);

                // PageNumber < 1 ya lo clampea el setter de PaginationViewModel; acá solo
                // falta el límite superior, que depende del total recién calculado.
                if (totalPaginas == 0)
                {
                    filter.PageNumber = 1;
                }
                else if (filter.PageNumber > totalPaginas)
                {
                    filter.PageNumber = totalPaginas;
                }

                var ventasOperacionesPagina = ventas
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToList();

                ViewBag.Paginacion = new PaginatedResult<VentaViewModel>
                {
                    Items = ventasOperacionesPagina,
                    TotalRecords = totalRegistros,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize
                };

                return View("Index_tw", ventas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener las ventas");
                TempData["Error"] = "Error al cargar las ventas";
                ViewBag.Paginacion = new PaginatedResult<VentaViewModel> { PageNumber = 1, PageSize = 20 };
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

                // Cobro de la primera cuota el mismo día (fecha comercial de Argentina, no la zona
                // del proceso). Micro-lote 6: la DECISIÓN se toma en la configuración del crédito;
                // esta pantalla solo la muestra (no la vuelve a pedir).
                if (venta.TipoPago == TipoPago.CreditoPersonal && venta.CreditoId.HasValue)
                {
                    var creditoDetalle = await _creditoService.GetByIdAsync(venta.CreditoId.Value);
                    var hoy = _reloj.HoyComercial;

                    if (venta.Estado == EstadoVenta.Confirmada)
                    {
                        // Post-confirmación: la 1ª cuota ya existe, está pendiente y vence hoy → banner
                        // con link al cobro (flujo PagarCuota). Fallback si no se cobró al confirmar.
                        var primeraCuota = creditoDetalle?.Cuotas?
                            .OrderBy(c => c.NumeroCuota)
                            .FirstOrDefault();
                        if (primeraCuota != null &&
                            primeraCuota.Estado == EstadoCuota.Pendiente &&
                            DateOnly.FromDateTime(primeraCuota.FechaVencimiento) == hoy)
                        {
                            ViewBag.PrimeraCuotaHoyCreditoId = venta.CreditoId.Value;
                            ViewBag.PrimeraCuotaHoyCuotaId = primeraCuota.Id;
                        }
                    }
                    else if (venta.PuedeConfirmar &&
                             creditoDetalle?.FechaPrimeraCuota.HasValue == true &&
                             DateOnly.FromDateTime(creditoDetalle.FechaPrimeraCuota.Value) == hoy)
                    {
                        // Pre-confirmación: la 1ª cuota vence hoy. Se muestra el resumen de la decisión
                        // tomada en la configuración (cobrar o no + medio); no se re-pregunta.
                        ViewBag.PrimeraCuotaVenceHoy = true;
                        ViewBag.PrimeraCuotaDecisionCobrar = creditoDetalle.CobrarPrimeraCuotaSolicitada;
                        ViewBag.PrimeraCuotaDecisionMedio = creditoDetalle.MedioPagoPrimeraCuota;
                    }
                }

                // Datos para modal de facturación
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
                ViewBag.TiposFactura = new SelectList(Enum.GetValues(typeof(TipoFactura)));
                ViewBag.FacturaViewModel = facturaViewModel;

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

                // El vendedor siempre es el usuario logueado: el backend lo resuelve
                // (VentaService.ResolverVendedorAsync). No se delega ni se selecciona en la UI.

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

                // La configuración final necesita el crédito persistido.
                if (venta.TipoPago == TipoPago.CreditoPersonal && venta.CreditoId.HasValue)
                {
                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id = venta.Id });
                    TempData["Success"] = $"Venta {venta.Numero} creada. Configure el plan de financiamiento.";
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
                return await RetornarVistaConDatos(viewModel);
            }
            catch (InvalidOperationException ex)
            {
                // Regla de negocio con mensaje ya redactado para el operador (ver Edit POST).
                _logger.LogWarning(ex, "Venta rechazada por regla de negocio en Create");
                ModelState.AddModelError("", ex.Message);
                return await RetornarVistaConDatos(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear venta");
                ModelState.AddModelError("", "Error al crear la venta: " + ex.Message);
                return await RetornarVistaConDatos(viewModel);
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

                // El vendedor siempre es el usuario logueado: el backend lo resuelve
                // (VentaService.ResolverVendedorAsync). No se delega ni se selecciona en la UI.

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
                        new { id = venta.CreditoId, ventaId = venta.Id, embedded = true });
                    var msg = $"Venta {venta.Numero} creada. Configure el plan de financiamiento.";

                    // El wizard conserva este identificador (y el RowVersion) para confirmar
                    // el mismo borrador luego de configurar el crédito por Edit, sin crear
                    // una segunda venta.
                    return Json(new { success = true, requiresRedirect = true, redirectUrl, ventaId = venta.Id, creditoId = venta.CreditoId, rowVersion = venta.RowVersion, message = msg });
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

                if (venta.Estado != EstadoVenta.Cotizacion &&
                    venta.Estado != EstadoVenta.Presupuesto &&
                    venta.Estado != EstadoVenta.PendienteRequisitos &&
                    venta.Estado != EstadoVenta.PendienteFinanciacion)
                {
                    _logger.LogWarning(
                        "Edit(GET) venta {Id} estado no editable. Estado:{Estado}",
                        id,
                        venta.Estado);
                    TempData["Error"] = "Solo se pueden editar ventas en estado Cotización, Presupuesto, Pendiente Requisitos o Pendiente Financiación";
                    return RedirectToAction(nameof(Details), new { id });
                }

                await CargarViewBags(
                    venta.ClienteId,
                    venta.Detalles.Select(d => d.ProductoId).Distinct(),
                    venta.VendedorUserId,
                    venta.TipoPago);
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
        public async Task<IActionResult> Edit(
            int id,
            VentaViewModel viewModel,
            // "Confirmar venta" (+ checkbox "Facturar") del wizard: en vez de solo guardar,
            // este POST puede encadenar Confirmar/ConfirmarYFacturar sin duplicar su lógica
            // (ver ProcesarAccionPostGuardadoAsync). "guardar" preserva el comportamiento
            // histórico de este botón (botón secundario "Guardar sin confirmar").
            [FromForm] string accionConfirmacion = "guardar",
            [FromForm] TipoFactura tipoFactura = TipoFactura.B)
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

                var ventaAntes = await _ventaService.GetByIdAsync(id);

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
                    return await RetornarVistaEdicionConDatos(viewModel);
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
                    var returnToVentaDetailsUrl = Url.Action(nameof(Details), new { id = resultado.Id });

                    // Si la venta ya tenía crédito asociado antes de este edit, se conserva el
                    // recheck de documentación existente. Si no tenía (p.ej. venta convertida
                    // desde cotización), UpdateAsync ya corrió la validación de aptitud +
                    // excepción documental (misma que CreateAsync) y creó el crédito pendiente,
                    // o lanzó una excepción si quedó NoViable sin excepción aplicada.
                    //
                    // BUG reportado: con una excepción documental ya autorizada (motivo cargado,
                    // crédito configurado y contrato generado en el wizard), este recheck volvía a
                    // exigir documentación real del cliente y mandaba a "Confirmar operación" a
                    // /DocumentoCliente en vez de a los detalles de la venta — pisando una excepción
                    // que el propio sistema ya había aceptado y auditado (MotivoAutorizacion
                    // "EXCEPCION_DOC|..."). El recheck sólo tiene sentido cuando nadie autorizó
                    // todavía esa excepción para esta venta.
                    if (ventaAntes?.CreditoId.HasValue == true && !resultado.TieneExcepcionDocumentalRegistrada)
                    {
                        var documentacion = await _documentacionService.ProcesarDocumentacionVentaAsync(resultado.Id);

                        if (!documentacion.DocumentacionCompleta)
                        {
                            TempData["Warning"] =
                                $"Falta documentación obligatoria para otorgar crédito: {documentacion.MensajeFaltantes}";

                            return RedirectToAction(
                                "Index",
                                "DocumentoCliente",
                                new { clienteId = resultado.ClienteId, returnToVentaId = resultado.Id, returnUrl = returnToVentaDetailsUrl });
                        }
                    }

                    // Si el crédito ya fue configurado (p.ej. por el configurador embebido del
                    // wizard, antes de este guardado), no reenviar a ConfigurarVenta: seguiría
                    // rebotando al operador ahí en cada guardado posterior. Sólo faltan
                    // configurar los créditos que siguen PendienteConfiguracion.
                    if (resultado.CreditoConfigurado)
                    {
                        return await ProcesarAccionPostGuardadoAsync(id, accionConfirmacion, tipoFactura);
                    }

                    TempData["Success"] = "Venta actualizada. Crédito listo para configurar.";
                    return RedirectToAction(
                        "ConfigurarVenta",
                        "Credito",
                        new { id = resultado.CreditoId, ventaId = resultado.Id, returnUrl = returnToVentaDetailsUrl });
                }

                return await ProcesarAccionPostGuardadoAsync(id, accionConfirmacion, tipoFactura);
            }
            catch (CondicionesPagoVentaException ex)
            {
                var mensaje = CrearMensajePresentacionCondicionesPago(ex.Message);
                _logger.LogWarning(ex, "Edit(POST) venta {Id} rechazada por condiciones de pago", id);
                ModelState.AddModelError("", mensaje);
                return await RetornarVistaEdicionConDatos(viewModel);
            }
            catch (InvalidOperationException ex)
            {
                // Regla de negocio (trazabilidad, estado, concurrencia, crédito): el mensaje ya
                // viene redactado para el operador, así que se muestra tal cual y se registra
                // como warning. No es una falla del sistema.
                _logger.LogWarning(ex, "Edit(POST) venta {Id} rechazada por regla de negocio", id);
                ModelState.AddModelError("", ex.Message);
                return await RetornarVistaEdicionConDatos(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar venta: {Id}", id);
                ModelState.AddModelError("", "Error al actualizar la venta: " + ex.Message);
                return await RetornarVistaEdicionConDatos(viewModel);
            }
        }

        // "Confirmar venta" (+ checkbox "Facturar") del wizard de edición: decide qué pasa
        // después de guardar según lo que pidió el operador, reutilizando la misma lógica
        // de negocio de Confirmar/ConfirmarYFacturar que ya usa Details en vez de
        // duplicarla. "guardar" (botón secundario "Guardar sin confirmar") conserva el
        // comportamiento histórico de este POST: sólo persiste, sin cambiar de estado.
        private async Task<IActionResult> ProcesarAccionPostGuardadoAsync(
            int id,
            string accionConfirmacion,
            TipoFactura tipoFactura)
        {
            var usuarioActual = User ?? new ClaimsPrincipal(new ClaimsIdentity());
            var puedeFacturar = usuarioActual.TienePermiso(ModuloVentas, AccionFacturar);

            switch (accionConfirmacion)
            {
                case "confirmar-facturar" when puedeFacturar:
                    return await EjecutarConfirmarYFacturarAsync(id, tipoFactura);
                case "confirmar":
                case "confirmar-facturar":
                    // Sin permiso de facturar (checkbox forzado a mano sin el permiso real,
                    // o venta con Crédito Personal donde ConfirmarYFacturar ya se autobloquea):
                    // degrada a "solo confirmar" en vez de descartar el guardado ya aplicado.
                    return await EjecutarConfirmarVentaAsync(id, aplicarExcepcionDocumental: false, motivoExcepcionDocumental: null);
                default:
                    TempData["Success"] = "Venta actualizada exitosamente";
                    return RedirectToAction(nameof(Details), new { id });
            }
        }

        // POST: Venta/EditAjax/5 — versión AJAX para el configurador de crédito embebido
        // del wizard. Existe porque una venta en edición puede llegar al paso "Crédito"
        // todavía sin CreditoId (p.ej. el operador recién cambia el medio de pago a
        // Crédito Personal en este mismo edit, o la venta nunca llegó a tener un crédito
        // pendiente creado). CreateAjax no sirve acá: la venta YA existe, y usarlo
        // duplicaría la venta (crea una segunda fila) en vez de actualizar ésta.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionActualizar)]
        public async Task<IActionResult> EditAjax(int id, VentaViewModel viewModel)
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
                            { "", new[] { "Debe abrir una caja antes de editar ventas." } }
                        }
                    });
                }

                LimpiarModelStateSegunTipoPago(viewModel.TipoPago, viewModel);

                if (!ModelState.IsValid || !ValidarDetalles(viewModel))
                {
                    var errors = ModelState
                        .Where(k => k.Value?.Errors.Any() == true)
                        .ToDictionary(
                            k => k.Key,
                            k => k.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                    return Json(new { success = false, errors });
                }

                var resultado = await _ventaService.UpdateAsync(id, viewModel);
                if (resultado == null)
                {
                    return Json(new { success = false, message = "Venta no encontrada." });
                }

                // El wizard sólo llama a EditAjax para (a) crear el crédito pendiente la
                // primera vez que hace falta, o (b) resincronizar el borrador porque el
                // configurador embebido detectó un cambio invalidante (cliente, productos o
                // pago) después de que el crédito ya estaba Configurado. En el caso (b), el
                // crédito quedó con un saldo/tasa calculados sobre un total que ya no es el
                // vigente: GenerarCuotasCreditoAsync usa credito.MontoAprobado tal cual, sin
                // re-derivarlo de venta.Total, así que confirmar con el estado Configurado
                // stale generaría cuotas sobre el importe viejo. Se revierte a
                // PendienteConfiguracion para forzar una reconfiguración real antes de poder
                // confirmar; no toca TasaInteres/MontoAprobado (se sobrescriben enteros la
                // próxima vez que se configure).
                if (resultado.CreditoId.HasValue)
                {
                    var creditoActual = await _context.Creditos
                        .FirstOrDefaultAsync(c => c.Id == resultado.CreditoId.Value && !c.IsDeleted);
                    if (creditoActual != null && creditoActual.Estado == EstadoCredito.Configurado)
                    {
                        creditoActual.Estado = EstadoCredito.PendienteConfiguracion;
                        var ventaEntidad = await _context.Ventas.FindAsync(resultado.Id);
                        if (ventaEntidad != null)
                        {
                            ventaEntidad.FechaConfiguracionCredito = null;
                        }
                        await _context.SaveChangesAsync();
                        resultado.RowVersion = ventaEntidad?.RowVersion ?? resultado.RowVersion;
                    }
                }

                return Json(new
                {
                    success = true,
                    ventaId = resultado.Id,
                    creditoId = resultado.CreditoId,
                    rowVersion = resultado.RowVersion,
                    message = "Venta actualizada."
                });
            }
            catch (CondicionesPagoVentaException ex)
            {
                var mensaje = CrearMensajePresentacionCondicionesPago(ex.Message);
                _logger.LogWarning(ex, "Venta rechazada por condiciones de pago en EditAjax {Id}", id);
                return Json(new
                {
                    success = false,
                    message = mensaje,
                    errors = new Dictionary<string, string[]> { { "", new[] { mensaje } } }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Venta rechazada por regla de negocio en EditAjax {Id}", id);
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar venta via AJAX {Id}", id);
                return Json(new
                {
                    success = false,
                    errors = new Dictionary<string, string[]>
                    {
                        { "", new[] { "Error al actualizar la venta: " + ex.Message } }
                    }
                });
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

            // Ver comentario equivalente en Edit(POST): con una excepción documental ya
            // autorizada para esta venta, no volver a mandar al operador a cargar
            // documentación real del cliente.
            if (!resultado.DocumentacionCompleta && !venta.TieneExcepcionDocumentalRegistrada)
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

        // POST: Venta/PrepararVenta/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionActualizar)]
        public async Task<IActionResult> PrepararVenta(int id)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de preparar una venta.",
                    nameof(Details),
                    new { id });
                if (cajaGuard != null)
                {
                    return cajaGuard;
                }

                var resultado = await _ventaService.PrepararVentaDesdeCotizacionAsync(id);
                if (resultado)
                {
                    TempData["Success"] = "Venta preparada. Ya puede confirmarla antes de facturar.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                TempData["Error"] = "Venta no encontrada";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "No se pudo preparar venta {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al preparar venta {Id}", id);
                TempData["Error"] = "Error al preparar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Venta/Confirmar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirmar(
            int id,
            bool aplicarExcepcionDocumental = false,
            string? motivoExcepcionDocumental = null)
        {
            return await EjecutarConfirmarVentaAsync(id, aplicarExcepcionDocumental, motivoExcepcionDocumental);
        }

        // Lógica real de "Confirmar", extraída para reutilizarla desde Edit(POST) (botón
        // "Confirmar venta" del wizard de edición): guardar y confirmar en un solo submit
        // sin duplicar toda esta lógica de negocio (crédito personal, contrato, excepción
        // documental, cobro de 1ª cuota, etc.).
        private async Task<IActionResult> EjecutarConfirmarVentaAsync(
            int id,
            bool aplicarExcepcionDocumental,
            string? motivoExcepcionDocumental)
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
                    var autorizacionFormalDocumental = venta.RequiereAutorizacion
                        && venta.EstadoAutorizacion == EstadoAutorizacionVenta.Autorizada
                        && venta.RazonesAutorizacion.Any(r => r.Tipo == TipoRazonAutorizacion.DocumentacionVencida);
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
                    else if ((excepcionDocumentalRegistrada || autorizacionFormalDocumental) && soloDocumentacionFaltante)
                    {
                        validacionConfirmacion.NoViable = false;
                        validacionConfirmacion.PendienteRequisitos = false;
                        excepcionDocumentalAplicada = true;

                        _logger.LogInformation(
                            "Confirmar(POST) venta {Id}: se reutiliza excepción documental ya autorizada (traza de confirmación o autorización formal de creación).",
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
                        var tempDataKey = excepcionDocumentalAplicada ? "Warning" : "Success";
                        var mensajeConfirmacion = excepcionDocumentalAplicada
                            ? "Venta confirmada por excepción documental autorizada. Crédito generado con cuotas."
                            : "Venta confirmada. Crédito generado con cuotas.";

                        // F2 (Micro-lote 6): la decisión de cobrar la 1ª cuota se tomó y persistió en
                        // la configuración del crédito. Acá se lee de la base (server-authoritative,
                        // no del payload) y se ejecuta reutilizando PagarCuota (aplica recargo del
                        // medio e impacta en caja). El servidor revalida vence-hoy, saldo, medio y
                        // caja. Un fallo del cobro NO revierte la venta/crédito ya confirmados.
                        var decisionCredito = venta.CreditoId.HasValue
                            ? await _creditoService.GetByIdAsync(venta.CreditoId.Value)
                            : null;

                        if (decisionCredito?.CobrarPrimeraCuotaSolicitada == true && venta.CreditoId.HasValue)
                        {
                            try
                            {
                                var cobro = await _creditoService.CobrarPrimeraCuotaAlGenerarAsync(
                                    venta.CreditoId.Value,
                                    decisionCredito.MedioPagoPrimeraCuota);

                                switch (cobro.Estado)
                                {
                                    case EstadoCobroPrimeraCuota.Cobrada:
                                        mensajeConfirmacion += $" Se cobró la 1ª cuota: {cobro.Total:C2} ({cobro.MedioPago}).";
                                        break;
                                    case EstadoCobroPrimeraCuota.Error:
                                        mensajeConfirmacion += " No se pudo cobrar la 1ª cuota; podés cobrarla desde el detalle.";
                                        tempDataKey = "Warning";
                                        break;
                                    default:
                                        mensajeConfirmacion += " La 1ª cuota no se cobró: " + (cobro.Mensaje ?? "no corresponde.");
                                        break;
                                }
                            }
                            catch (Exception exCobro)
                            {
                                _logger.LogError(exCobro, "Error al cobrar 1ª cuota al confirmar venta {Id}", id);
                                mensajeConfirmacion += " No se pudo cobrar la 1ª cuota: " + exCobro.Message;
                                tempDataKey = "Warning";
                            }
                        }

                        TempData[tempDataKey] = mensajeConfirmacion;
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
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionCancelar)]
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
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionCancelar)]
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
                    TempData["Success"] = "Venta cancelada. El stock fue revertido y las unidades físicas asociadas volvieron a disponible.";
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

        // POST: Venta/ConfirmarYFacturar/5 — acción combinada (mostrador): confirma y factura en un paso.
        // Solo para medios sin crédito personal; el crédito requiere contrato/configuración.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloVentas, Accion = AccionFacturar)]
        public async Task<IActionResult> ConfirmarYFacturar(int id, TipoFactura tipo = TipoFactura.B)
        {
            return await EjecutarConfirmarYFacturarAsync(id, tipo);
        }

        // Extraída por el mismo motivo que EjecutarConfirmarVentaAsync: Edit(POST) la
        // reutiliza cuando el operador tilda "Facturar" al confirmar desde el wizard.
        private async Task<IActionResult> EjecutarConfirmarYFacturarAsync(int id, TipoFactura tipo)
        {
            try
            {
                var cajaGuard = await RedirigirSiCajaCerradaAsync(
                    "Debe abrir una caja antes de confirmar y facturar una venta.",
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

                if (venta.TipoPago == TipoPago.CreditoPersonal)
                {
                    TempData["Error"] = "Las ventas con crédito personal no se pueden facturar en un solo paso. Use el flujo de confirmación con contrato.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                _logger.LogInformation(
                    "ConfirmarYFacturar(POST) venta {Id}. Estado:{Estado} TipoPago:{TipoPago} User:{User}",
                    id,
                    venta.Estado,
                    venta.TipoPago,
                    _currentUser.GetUsername());

                var confirmada = await _ventaService.ConfirmarVentaAsync(id);
                if (!confirmada)
                {
                    TempData["Error"] = "No se pudo confirmar la venta; no se generó la factura.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var facturaViewModel = new FacturaViewModel
                {
                    VentaId = id,
                    FechaEmision = DateTime.Today,
                    Tipo = tipo
                };

                var facturada = await _ventaService.FacturarVentaAsync(id, facturaViewModel);
                if (facturada)
                {
                    TempData["Success"] = "Venta confirmada y facturada en un solo paso. El stock fue descontado.";
                }
                else
                {
                    TempData["Warning"] = "La venta se confirmó pero no se pudo generar la factura. Reintente facturar.";
                }

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (CondicionesPagoVentaException ex)
            {
                _logger.LogWarning(ex, "Venta {Id} rechazada por condiciones de pago en ConfirmarYFacturar", id);
                TempData["Error"] = CrearMensajePresentacionCondicionesPago(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "ConfirmarYFacturar venta {Id} bloqueada", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar y facturar venta {Id}", id);
                TempData["Error"] = "Error al confirmar y facturar la venta: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
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
            string? vendedorUserIdSeleccionado = null,
            TipoPago? tipoPagoSeleccionado = null)
        {
            await _viewBagBuilder.CargarAsync(
                ViewBag,
                clienteIdSeleccionado,
                productoIdsIncluidos,
                vendedorUserIdSeleccionado,
                tipoPagoSeleccionado);
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
            await PrepararVistaFormularioAsync(viewModel);
            return View("Create_tw", viewModel);
        }

        private async Task<IActionResult> RetornarVistaEdicionConDatos(VentaViewModel viewModel)
        {
            await PrepararVistaFormularioAsync(viewModel);
            return View("Edit_tw", viewModel);
        }

        private async Task PrepararVistaFormularioAsync(VentaViewModel viewModel)
        {
            await RehidratarCamposDeVistaAsync(viewModel);
            await CargarViewBags(
                viewModel.ClienteId,
                viewModel.Detalles?.Select(d => d.ProductoId).Distinct(),
                viewModel.VendedorUserId,
                viewModel.TipoPago);
        }

        /// <summary>
        /// Repone los campos de sólo lectura que el formulario no postea (nombre y documento del
        /// cliente, código/nombre/stock del producto, código de la unidad física). Sin esto, al
        /// volver a la vista por un error el wizard se re-hidrata con esos campos vacíos y el
        /// operador ve el cliente y el detalle "borrados" aunque los datos sigan en el POST.
        /// </summary>
        private async Task RehidratarCamposDeVistaAsync(VentaViewModel viewModel)
        {
            if (viewModel.ClienteId > 0 && string.IsNullOrWhiteSpace(viewModel.ClienteNombre))
            {
                var cliente = await _context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == viewModel.ClienteId && !c.IsDeleted);

                if (cliente != null)
                {
                    viewModel.ClienteNombre = cliente.ToDisplayName();
                    viewModel.ClienteDocumento = cliente.NumeroDocumento;
                }
            }

            var detalles = viewModel.Detalles;
            if (detalles == null || detalles.Count == 0)
            {
                return;
            }

            var productoIds = detalles.Select(d => d.ProductoId).Distinct().ToList();
            var productos = await _context.Productos
                .AsNoTracking()
                .Where(p => productoIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Codigo, p.Nombre, p.StockActual, p.RequiereNumeroSerie })
                .ToListAsync();

            var unidadIds = detalles
                .Where(d => d.ProductoUnidadId.HasValue)
                .Select(d => d.ProductoUnidadId!.Value)
                .Distinct()
                .ToList();

            var unidades = unidadIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.ProductoUnidades
                    .AsNoTracking()
                    .Where(u => unidadIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.CodigoInternoUnidad);

            foreach (var detalle in detalles)
            {
                var producto = productos.FirstOrDefault(p => p.Id == detalle.ProductoId);
                if (producto != null)
                {
                    if (string.IsNullOrWhiteSpace(detalle.ProductoCodigo))
                    {
                        detalle.ProductoCodigo = producto.Codigo;
                    }

                    if (string.IsNullOrWhiteSpace(detalle.ProductoNombre))
                    {
                        detalle.ProductoNombre = producto.Nombre;
                    }

                    detalle.StockDisponible = (int)producto.StockActual;
                    detalle.RequiereNumeroSerie = producto.RequiereNumeroSerie;
                }

                if (detalle.ProductoUnidadId.HasValue &&
                    string.IsNullOrWhiteSpace(detalle.ProductoUnidadCodigoInterno) &&
                    unidades.TryGetValue(detalle.ProductoUnidadId.Value, out var codigoUnidad))
                {
                    detalle.ProductoUnidadCodigoInterno = codigoUnidad;
                }
            }
        }

        #endregion
    }
}

