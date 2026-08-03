using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "creditos", Accion = "view")]
    public class CreditoController : Controller
    {
        private readonly ICreditoService _creditoService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IConfiguracionMoraService _configuracionMoraService;
        private readonly IVentaService _ventaService;
        private readonly ILogger<CreditoController> _logger;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly IContratoVentaCreditoService _contratoVentaCreditoService;
        private readonly ICreditoRangoProductoService? _creditoRangoProductoService;
        private readonly ICreditoConfiguracionVentaService _creditoConfiguracionVentaService;
        private readonly ICreditoSimulacionVentaService _creditoSimulacionVentaService;
        private readonly ICreditoUiQueryService _creditoUiQueryService;

        private readonly ICurrentUserService _currentUser;
        private readonly CreditoViewBagBuilder _viewBagBuilder;
        private readonly IRelojComercial _reloj;

        private IActionResult RedirectToReturnUrlOrDetails(string? returnUrl, int creditoId)
        {
            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Details), new { id = creditoId });
        }

        public CreditoController(
            ICreditoService creditoService,
            IFinancialCalculationService financialService,
            IConfiguracionPagoService configuracionPagoService,
            IConfiguracionMoraService configuracionMoraService,
            IVentaService ventaService,
            ILogger<CreditoController> logger,
            ICreditoDisponibleService creditoDisponibleService,
            ICurrentUserService currentUser,
            CreditoViewBagBuilder viewBagBuilder,
            IContratoVentaCreditoService contratoVentaCreditoService,
            IClienteAptitudService? aptitudService = null,
            IProductoCreditoRestriccionService? productoCreditoRestriccionService = null,
            ICreditoRangoProductoService? creditoRangoProductoService = null,
            ICreditoConfiguracionVentaService? creditoConfiguracionVentaService = null,
            ICreditoSimulacionVentaService? creditoSimulacionVentaService = null,
            ICreditoUiQueryService? creditoUiQueryService = null,
            IRelojComercial? reloj = null)
        {
            _creditoService = creditoService;
            // PUN-ML7: fuente única de "hoy" para vencimiento. La inyección obligatoria sería
            // preferible, pero este controller ya sigue la convención (todo el resto de sus
            // colaboradores opcionales) de aceptar null por los múltiples tests que lo construyen
            // pasando solo un subconjunto de parámetros nombrados — fuera del alcance de esta
            // corrección. El fallback es inerte en producción (Program.cs registra IRelojComercial
            // como Singleton; DI siempre lo resuelve) y solo se alcanza en esos tests.
            _reloj = reloj ?? RelojComercial.Sistema;
            _configuracionPagoService = configuracionPagoService;
            _configuracionMoraService = configuracionMoraService;
            _ventaService = ventaService;
            _logger = logger;
            _creditoDisponibleService = creditoDisponibleService;
            _currentUser = currentUser;
            _viewBagBuilder = viewBagBuilder;
            _contratoVentaCreditoService = contratoVentaCreditoService;
            _creditoRangoProductoService = creditoRangoProductoService
                ?? (productoCreditoRestriccionService is not null
                    ? new CreditoRangoProductoService(productoCreditoRestriccionService)
                    : null);
            _creditoConfiguracionVentaService = creditoConfiguracionVentaService
                ?? new CreditoConfiguracionVentaService(
                    configuracionPagoService,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<CreditoConfiguracionVentaService>.Instance,
                    _creditoRangoProductoService);
            _creditoSimulacionVentaService = creditoSimulacionVentaService
                ?? new CreditoSimulacionVentaService(
                    financialService,
                    configuracionPagoService,
                    aptitudService,
                    _ventaService,
                    _creditoRangoProductoService);
            _creditoUiQueryService = creditoUiQueryService ?? new CreditoUiQueryService();
        }

        #region Index / Detalle / Simular

        // GET: Credito
        public async Task<IActionResult> Index(CreditoFilterViewModel filter)
        {
            try
            {
                var creditos = await _creditoService.GetAllAsync(filter);
                var clientes = _creditoUiQueryService.AgruparCreditosPorCliente(creditos);

                return View("Index_tw", new CreditoIndexViewModel
                {
                    Filter = filter,
                    Clientes = clientes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar créditos");
                TempData["Error"] = "Error al cargar los créditos";
                return View("Index_tw", new CreditoIndexViewModel
                {
                    Filter = filter,
                    Clientes = new List<CreditoClienteIndexViewModel>()
                });
            }
        }

        // GET: Credito/Simular — retirada (ML10): usaba sistema francés con tasa como
        // fracción, sin anticipo, para una "línea de crédito" manual sin relación con
        // Crédito Personal (ese producto nunca genera cuotas persistidas en la app real:
        // el único generador de Cuota vivo es VentaService.GenerarCuotasCreditoAsync,
        // exclusivo del flujo Cotización/Venta). Redirige en vez de 404 por si quedan
        // enlaces o favoritos antiguos.
        [HttpGet]
        public IActionResult Simular(string? returnUrl = null)
        {
            TempData["Info"] = "El simulador independiente fue retirado. Usá la simulación integrada en Configurar Venta o Cotización.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Credito/Details/5
        public async Task<IActionResult> Details(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var credito = await _creditoService.GetByIdAsync(id);
                if (credito == null)
                {
                    TempData["Error"] = "Crédito no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                var detalle = new CreditoDetalleViewModel
                {
                    Credito = credito
                };

                try
                {
                    var cupoGlobal = await _creditoDisponibleService.CalcularDisponibleAsync(credito.ClienteId);
                    detalle.CupoGlobalDisponible = cupoGlobal.Disponible;
                    detalle.CupoGlobalOrigenLimite = cupoGlobal.OrigenLimite;
                }
                catch (CreditoDisponibleException ex)
                {
                    detalle.CupoGlobalConError = true;
                    detalle.CupoGlobalMensajeError = ex.Message;
                }

                ViewBag.ContratoVentaCredito = await ObtenerContratoResumenPorCreditoAsync(id);

                return View("Details_tw", detalle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener crédito {Id}", id);
                TempData["Error"] = "Error al cargar el crédito";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<ContratoVentaCreditoResumenViewModel?> ObtenerContratoResumenPorCreditoAsync(int creditoId)
        {
            var contrato = await _contratoVentaCreditoService.ObtenerContratoPorCreditoAsync(creditoId);
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

        // GET: Credito/PanelCliente/5
        [HttpGet]
        public async Task<IActionResult> PanelCliente(int id)
        {
            if (id <= 0)
                return BadRequest();

            var creditos = await _creditoService.GetByClienteIdAsync(id);
            var grupo = _creditoUiQueryService.AgruparCreditosPorCliente(creditos).FirstOrDefault();

            if (grupo == null)
                return NotFound();

            return PartialView("_PanelClientePartial", grupo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        #endregion

        #region Aprobar / Rechazar / Cancelar

        public async Task<IActionResult> Aprobar(int id, string? returnUrl = null)
        {
            try
            {
                var aprobadoPor = _currentUser.GetUsername();

                var ok = await _creditoService.AprobarCreditoAsync(id, aprobadoPor);
                TempData[ok ? "Success" : "Error"] = ok
                    ? "Crédito aprobado exitosamente"
                    : "No se pudo aprobar el crédito";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aprobar crédito {Id}", id);
                TempData["Error"] = "Error al aprobar el crédito: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string motivo, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                TempData["Error"] = "Debe especificar un motivo para rechazar.";
                return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }

            try
            {
                var ok = await _creditoService.RechazarCreditoAsync(id, motivo);
                TempData[ok ? "Success" : "Error"] = ok
                    ? "Crédito rechazado."
                    : "No se pudo rechazar el crédito";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar crédito {Id}", id);
                TempData["Error"] = "Error al rechazar el crédito: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int id, string motivo, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                TempData["Error"] = "Debe especificar un motivo para cancelar.";
                return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }

            try
            {
                var ok = await _creditoService.CancelarCreditoAsync(id, motivo);
                TempData[ok ? "Success" : "Error"] = ok
                    ? "Crédito cancelado."
                    : "No se pudo cancelar el crédito";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar crédito {Id}", id);
                TempData["Error"] = "Error al cancelar el crédito: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [HttpGet]
        #endregion

        #region Configurar venta

        public async Task<IActionResult> ConfigurarVenta(int id, int? ventaId, string? returnUrl = null, bool embedded = false)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            var credito = await _creditoService.GetByIdAsync(id);
            if (credito == null)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status404NotFound, "Crédito no encontrado.", returnUrl, ventaId);

                TempData["Error"] = "Crédito no encontrado";
                return RedirectToAction(nameof(Index));
            }

            // Validar que el crédito esté en estado que permita configuración
            if (!EsConfigurable(credito.Estado))
            {
                // Si ya está Generado o más avanzado, no permitir reconfigurar
                var mensajeEstado = credito.Estado == EstadoCredito.Generado ||
                         credito.Estado == EstadoCredito.Activo ||
                         credito.Estado == EstadoCredito.Finalizado
                    ? "El crédito ya fue generado y no puede reconfigurarse."
                    : $"El crédito no puede configurarse en estado {credito.Estado}.";

                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, mensajeEstado, returnUrl, ventaId);

                TempData[credito.Estado is EstadoCredito.Generado or EstadoCredito.Activo or EstadoCredito.Finalizado ? "Warning" : "Error"] = mensajeEstado;

                if (ventaId.HasValue)
                    return RedirectToAction("Details", "Venta", new { id = ventaId });
                return RedirectToAction("Details", new { id });
            }

            decimal montoVenta = credito.MontoAprobado > 0 ? credito.MontoAprobado : credito.MontoSolicitado;

            var tasaMensualConfig = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
            if (tasaMensualConfig == null)
            {
                var mensajeSinTasa = "La tasa de interés de Crédito Personal no está configurada. " +
                    "Configure el valor en Administración → Tipos de Pago antes de continuar.";

                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, mensajeSinTasa, returnUrl, ventaId);

                TempData["Error"] = mensajeSinTasa;
                if (ventaId.HasValue)
                    return RedirectToAction("Details", "Venta", new { id = ventaId });
                return RedirectToAction("Details", new { id });
            }

            if (ventaId.HasValue)
            {
                var ventaTotal = await _ventaService.GetTotalVentaAsync(ventaId.Value);
                if (ventaTotal.HasValue)
                    montoVenta = ventaTotal.Value;
            }

            var perfilesActivos = await _configuracionPagoService.GetPerfilesCreditoActivosAsync();

            // Resolver parámetros de crédito del cliente (Personalizado > Perfil > Global)
            var parametrosCliente = await _configuracionPagoService
                .ObtenerParametrosCreditoClienteAsync(credito.ClienteId, tasaMensualConfig.Value);

            var modelo = new ConfiguracionCreditoVentaViewModel
            {
                CreditoId = credito.Id,
                VentaId = ventaId,
                ClienteId = credito.ClienteId,
                ClienteNombre = credito.ClienteNombre ?? string.Empty,
                NumeroCredito = credito.Numero,
                FuenteConfiguracion = parametrosCliente.Fuente,
                MetodoCalculo = MetodoCalculoCredito.Global,
                PerfilCreditoSeleccionadoId = parametrosCliente.PerfilPreferidoId,
                Monto = montoVenta,
                // Intención conservada de una cotización convertida (mismo patrón que
                // CantidadCuotas abajo). No es autoridad: el operador puede modificarla y el
                // servidor revalida contra el total real al confirmar.
                Anticipo = credito.AnticipoPreseleccionado,
                MontoFinanciado = montoVenta,
                CantidadCuotas = credito.CantidadCuotas > 0 ? credito.CantidadCuotas : 0,
                TasaMensual = parametrosCliente.TasaMensual,
                GastosAdministrativos = parametrosCliente.GastosAdministrativos,
                FechaPrimeraCuota = credito.FechaPrimeraCuota,
                // F2: reabrir la configuración muestra la decisión ya persistida.
                CobrarPrimeraCuota = credito.CobrarPrimeraCuotaSolicitada,
                MedioPagoPrimeraCuota = credito.MedioPagoPrimeraCuota,
                CreditoEstaConfigurado = credito.Estado == EstadoCredito.Configurado,
                ContratoGenerado = ventaId.HasValue &&
                    await _contratoVentaCreditoService.ExisteContratoGeneradoAsync(ventaId.Value),
                PlantillaActivaDisponible = await _contratoVentaCreditoService.ExistePlantillaActivaAsync()
            };

            var (cuotasMinGet, cuotasMaxGet, _, _) =
                await _configuracionPagoService.ResolverRangoCuotasAsync(
                    modelo.MetodoCalculo!.Value,
                    modelo.PerfilCreditoSeleccionadoId,
                    modelo.ClienteId);
            var venta = ventaId.HasValue
                ? await _ventaService.GetByIdAsync(ventaId.Value)
                : null;

            if (venta?.RowVersion is { Length: > 0 })
            {
                modelo.VentaRowVersionBase64 = Convert.ToBase64String(venta.RowVersion);
            }

            if (venta != null && venta.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, "La venta requiere autorización antes de configurar el crédito.", returnUrl, ventaId);

                TempData["Error"] = "La venta requiere autorización antes de configurar el crédito.";
                return RedirectToAction("Details", "Venta", new { id = ventaId });
            }

            var rangoGet = await ResolverRangoCreditoProductoAsync(venta, cuotasMinGet, cuotasMaxGet);
            if (rangoGet.Error is not null)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, rangoGet.Error, returnUrl, ventaId);

                TempData["Error"] = rangoGet.Error;
                if (ventaId.HasValue)
                    return RedirectToAction("Details", "Venta", new { id = ventaId });
                return RedirectToAction("Details", new { id });
            }

            AplicarRangoEfectivoAlModelo(modelo, rangoGet);

            var planesGet = await ResolverPlanesVentaAsync(venta);

            // Pasar datos del cliente a la vista para JS
            var perfilPreferido = parametrosCliente.PerfilPreferidoId.HasValue
                ? perfilesActivos.FirstOrDefault(p => p.Id == parametrosCliente.PerfilPreferidoId.Value)
                : null;

            modelo.ClienteConfigPersonalizada = new ClienteConfigCreditoVentaViewModel
            {
                TieneTasaPersonalizada = parametrosCliente.TieneTasaPersonalizada,
                TasaPersonalizada = parametrosCliente.TasaPersonalizada,
                GastosPersonalizados = parametrosCliente.GastosPersonalizados,
                CuotasMaximas = parametrosCliente.CuotasMaximas,
                CuotasMinimas = parametrosCliente.CuotasMinimas,
                TasaGlobal = tasaMensualConfig.Value,
                GastosGlobales = 0,
                TienePerfilPreferido = parametrosCliente.PerfilPreferidoId.HasValue,
                PerfilPreferidoId = parametrosCliente.PerfilPreferidoId,
                PerfilNombre = parametrosCliente.PerfilPreferidoNombre,
                PerfilTasa = perfilPreferido?.TasaMensual,
                PerfilGastos = perfilPreferido?.GastosAdministrativos,
                PerfilMinCuotas = perfilPreferido?.MinCuotas,
                PerfilMaxCuotas = perfilPreferido?.MaxCuotas,
                TieneConfiguracionCliente = parametrosCliente.TieneConfiguracionPersonalizada,
                MontoMinimo = parametrosCliente.MontoMinimo,
                MontoMaximo = parametrosCliente.MontoMaximo,
                MaxCuotasCreditoProducto = modelo.MaxCuotasCreditoProducto,
                RestriccionCreditoProductoDescripcion = modelo.RestriccionCreditoProductoDescripcion,
                MaxCuotasBase = modelo.MaxCuotasBase,
                ProductoIdRestrictivo = modelo.ProductoIdRestrictivo,
                ProductoRestrictivoNombre = modelo.ProductoRestrictivoNombre,
                CuotasHabilitadas = planesGet.Planes,
                SinPlanesCompatibles = !planesGet.EsValido,
                MotivoSinPlanes = planesGet.MensajeRechazo
            };

            modelo.PerfilesActivos = perfilesActivos
                .Select(p => new PerfilCreditoActivoViewModel
                {
                    Id = p.Id,
                    Nombre = p.Nombre,
                    Descripcion = p.Descripcion,
                    TasaMensual = p.TasaMensual,
                    GastosAdministrativos = p.GastosAdministrativos,
                    MinCuotas = p.MinCuotas,
                    MaxCuotas = p.MaxCuotas
                })
                .ToList();

            // El wizard de Venta pide este mismo GET con embedded=true para inyectar el
            // configurador dentro del paso "Crédito" (fetch + innerHTML), sin el layout de
            // página completa (breadcrumb, header, navegación). Mismo modelo, misma
            // resolución server-authoritative; sólo cambia el fragmento HTML devuelto.
            if (embedded)
            {
                return PartialView("_ConfigurarVentaEmbebida", modelo);
            }

            return View("ConfigurarVenta_tw", modelo);
        }

        // Errores del POST fuera del ModelState (crédito/venta inexistente, estado no
        // configurable, autorización pendiente): en modo embebido el wizard espera JSON
        // (fetch), nunca un redirect ni un fragmento HTML de la vista de página completa.
        private IActionResult ErrorConfigurarVenta(bool embedded, int statusCode, string mensaje, string? returnUrl, int? ventaId)
        {
            if (embedded)
            {
                Response.StatusCode = statusCode;
                return Json(new { success = false, message = mensaje });
            }

            Response.StatusCode = statusCode;
            TempData["Error"] = mensaje;
            return ventaId.HasValue
                ? RedirectToAction("Details", "Venta", new { id = ventaId })
                : RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfigurarVenta(ConfiguracionCreditoVentaViewModel modelo, string? returnUrl = null, bool embedded = false)
        {
            if (!ModelState.IsValid)
            {
                if (embedded)
                {
                    var erroresModelo = ModelState
                        .Where(kvp => kvp.Value?.Errors.Count > 0)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return Json(new { success = false, errors = erroresModelo });
                }

                Response.StatusCode = StatusCodes.Status400BadRequest;
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return View("ConfigurarVenta_tw", modelo);
            }

            // El crédito debe existir y admitir configuración. Sin esta verificación el POST
            // reconfiguraba créditos ya generados que el GET sí rechaza, y un id inexistente
            // llegaba hasta el service y salía como 500.
            var creditoPost = await _creditoService.GetByIdAsync(modelo.CreditoId);
            if (creditoPost == null)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status404NotFound, "No se encontró el crédito indicado.", returnUrl, modelo.VentaId);

                Response.StatusCode = StatusCodes.Status404NotFound;
                ModelState.AddModelError(string.Empty, "No se encontró el crédito indicado.");
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            if (!EsConfigurable(creditoPost.Estado))
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, $"El crédito no puede configurarse en estado {creditoPost.Estado}.", returnUrl, modelo.VentaId);

                Response.StatusCode = StatusCodes.Status409Conflict;
                ModelState.AddModelError(string.Empty,
                    $"El crédito no puede configurarse en estado {creditoPost.Estado}.");
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            var venta = modelo.VentaId.HasValue
                ? await _ventaService.GetByIdAsync(modelo.VentaId.Value)
                : null;

            if (modelo.VentaId.HasValue && venta == null)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status404NotFound, "No se encontró la venta indicada.", returnUrl, modelo.VentaId);

                Response.StatusCode = StatusCodes.Status404NotFound;
                ModelState.AddModelError(string.Empty, "No se encontró la venta indicada.");
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            if (venta != null && venta.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
            {
                if (embedded)
                    return ErrorConfigurarVenta(true, StatusCodes.Status409Conflict, "La venta requiere autorización antes de configurar el crédito.", returnUrl, modelo.VentaId);

                TempData["Error"] = "La venta requiere autorización antes de configurar el crédito.";
                return RedirectToAction("Details", "Venta", new { id = modelo.VentaId });
            }

            var resultadoConfiguracion = await _creditoConfiguracionVentaService.ResolverAsync(modelo, venta);
            if (resultadoConfiguracion.RangoEfectivo is not null)
            {
                AplicarRangoEfectivoAlModelo(modelo, resultadoConfiguracion.RangoEfectivo);
            }

            if (!resultadoConfiguracion.EsValido)
            {
                // Conflicto = la combinación de productos no admite lo enviado (bloqueo, intersección
                // vacía o cantidad fuera de los planes). El resto son datos inválidos del formulario.
                var statusConflicto = resultadoConfiguracion.Motivo == MotivoRechazoConfiguracionCredito.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;

                if (embedded)
                {
                    Response.StatusCode = statusConflicto;
                    return Json(new
                    {
                        success = false,
                        errorKey = resultadoConfiguracion.ErrorKey,
                        message = resultadoConfiguracion.ErrorMessage
                    });
                }

                Response.StatusCode = statusConflicto;
                ModelState.AddModelError(resultadoConfiguracion.ErrorKey ?? string.Empty, resultadoConfiguracion.ErrorMessage ?? string.Empty);
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            await _creditoService.ConfigurarCreditoAsync(resultadoConfiguracion.Comando!);

            // El wizard crea una venta pendiente y reutiliza esta misma validación y
            // configuración canónica. Configurar no confirma ni factura: sólo deja el
            // crédito listo para que el paso Revisión continúe por Confirmar.
            if (embedded)
            {
                return Json(new { success = true, ventaId = modelo.VentaId, creditoId = modelo.CreditoId });
            }

            if (modelo.VentaId.HasValue)
            {
                TempData["Success"] = "Crédito configurado. Generá el contrato para continuar.";
                return RedirectToAction("Preparar", "ContratoVentaCredito", new { ventaId = modelo.VentaId.Value });
            }

            TempData["Success"] = "Crédito configurado y listo para confirmación.";
            return RedirectToReturnUrlOrDetails(returnUrl, modelo.CreditoId);
        }

        /// <summary>
        /// Simula el plan de cuotas para una venta. Los parámetros opcionales se normalizan a 0 si vienen vacíos.
        /// Server-authoritative cuando se envía <paramref name="ventaId"/>: el total real de la venta
        /// reemplaza a <paramref name="totalVenta"/> y el porcentaje lo resuelve el servidor a partir del
        /// plan efectivo, ignorando <paramref name="tasaMensual"/> salvo que <paramref name="fuenteConfiguracion"/>
        /// y <paramref name="metodoCalculo"/> sean ambos Manual (ver CreditoSimulacionVentaService.SimularAsync).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SimularPlanVenta(
            decimal totalVenta,
            decimal? anticipo,
            int cuotas,
            decimal? gastosAdministrativos,
            string? fechaPrimeraCuota,
            decimal? tasaMensual,
            int? ventaId = null,
            MetodoCalculoCredito? metodoCalculo = null,
            FuenteConfiguracionCredito? fuenteConfiguracion = null)
        {
            try
            {
                var resultado = await _creditoSimulacionVentaService.SimularAsync(new CreditoSimulacionVentaRequest
                {
                    TotalVenta = totalVenta,
                    Anticipo = anticipo,
                    Cuotas = cuotas,
                    GastosAdministrativos = gastosAdministrativos,
                    FechaPrimeraCuota = fechaPrimeraCuota,
                    TasaMensual = tasaMensual,
                    VentaId = ventaId,
                    MetodoCalculo = metodoCalculo,
                    FuenteConfiguracion = fuenteConfiguracion
                });

                if (!resultado.EsValido)
                    return BadRequest(resultado.Error);

                return Json(resultado.Plan);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al simular plan de crédito");
                return StatusCode(500, new { error = "Ocurrió un error al calcular el plan de crédito." });
            }
        }

        /// <summary>
        /// Recarga los perfiles de crédito activos en ViewBag y retorna la vista ConfigurarVenta_tw.
        /// Centraliza el patrón repetido en los early returns de validación de ConfigurarVenta POST.
        /// </summary>
        private async Task<IActionResult> RetornarVistaConPerfilesAsync(ConfiguracionCreditoVentaViewModel modelo)
        {
            modelo.ContratoGenerado = modelo.VentaId.HasValue &&
                await _contratoVentaCreditoService.ExisteContratoGeneradoAsync(modelo.VentaId.Value);
            modelo.PlantillaActivaDisponible = await _contratoVentaCreditoService.ExistePlantillaActivaAsync();
            modelo.PerfilesActivos = (await _configuracionPagoService.GetPerfilesCreditoActivosAsync())
                .Select(p => new PerfilCreditoActivoViewModel
                {
                    Id = p.Id,
                    Nombre = p.Nombre,
                    Descripcion = p.Descripcion,
                    TasaMensual = p.TasaMensual,
                    GastosAdministrativos = p.GastosAdministrativos,
                    MinCuotas = p.MinCuotas,
                    MaxCuotas = p.MaxCuotas
                })
                .ToList();
            var ventaCuotas = modelo.VentaId.HasValue
                ? await _ventaService.GetByIdAsync(modelo.VentaId.Value)
                : null;
            var planes = await ResolverPlanesVentaAsync(ventaCuotas);
            modelo.ClienteConfigPersonalizada = new ClienteConfigCreditoVentaViewModel
            {
                MaxCuotasCreditoProducto = modelo.MaxCuotasCreditoProducto,
                RestriccionCreditoProductoDescripcion = modelo.RestriccionCreditoProductoDescripcion,
                MaxCuotasBase = modelo.MaxCuotasBase,
                ProductoIdRestrictivo = modelo.ProductoIdRestrictivo,
                ProductoRestrictivoNombre = modelo.ProductoRestrictivoNombre,
                CuotasHabilitadas = planes.Planes,
                SinPlanesCompatibles = !planes.EsValido,
                MotivoSinPlanes = planes.MensajeRechazo
            };
            return View("ConfigurarVenta_tw", modelo);
        }

        /// <summary>
        /// Planes efectivos de Crédito Personal de la venta. Misma resolución canónica que usa
        /// <see cref="ICreditoConfiguracionVentaService"/> al validar el POST.
        /// </summary>
        private Task<PlanesCreditoPersonalResultado> ResolverPlanesVentaAsync(VentaViewModel? venta) =>
            _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(
                venta?.Detalles?.Select(d => d.ProductoId) ?? Enumerable.Empty<int>());

        /// <summary>
        /// Estados en los que el plan de un crédito todavía puede definirse. Una vez generado
        /// existen cuotas, contrato y movimientos que dependen de él: reconfigurarlo los rompería.
        /// </summary>
        private static bool EsConfigurable(EstadoCredito estado) =>
            estado is EstadoCredito.PendienteConfiguracion
                   or EstadoCredito.Solicitado
                   or EstadoCredito.Configurado;

        private async Task<CreditoRangoProductoResultado> ResolverRangoCreditoProductoAsync(
            VentaViewModel? venta,
            int minBase,
            int maxBase)
        {
            if (venta is null || _creditoRangoProductoService is null)
            {
                return new CreditoRangoProductoResultado(minBase, maxBase, maxBase, null, null, null, null, null);
            }

            return await _creditoRangoProductoService.ResolverAsync(
                venta,
                TipoPago.CreditoPersonal,
                minBase,
                maxBase);
        }

        private static void AplicarRangoEfectivoAlModelo(
            ConfiguracionCreditoVentaViewModel modelo,
            CreditoRangoProductoResultado rango)
        {
            modelo.CuotasMinPermitidas = rango.Min;
            modelo.CuotasMaxPermitidas = rango.Max;
            modelo.MaxCuotasBase = rango.MaxBase;
            modelo.MaxCuotasCreditoProducto = rango.MaxProducto;
            modelo.ProductoIdRestrictivo = rango.ProductoIdRestrictivo;
            modelo.ProductoRestrictivoNombre = rango.ProductoRestrictivoNombre;
            modelo.RestriccionCreditoProductoDescripcion = rango.DescripcionProducto;
        }

        private async Task CargarCuotasPago(PagarCuotaViewModel modelo, IReadOnlyCollection<CuotaViewModel> cuotas)
        {
            modelo.Cuotas = _creditoUiQueryService.ProyectarCuotasPendientes(cuotas);
            modelo.CuotasJson = _creditoUiQueryService.BuildCuotasJson(cuotas);
            modelo.RecargosPorMedioJson = await BuildRecargosPorMedioJsonAsync();
        }

        // Porcentaje de recargo/descuento vigente por medio de pago, para previsualizar el
        // total en el formulario. El backend sigue siendo la autoridad al confirmar el cobro.
        private static readonly (string Etiqueta, TipoPago Tipo)[] MediosPagoCobroCuota =
        {
            ("Efectivo", TipoPago.Efectivo),
            ("Transferencia", TipoPago.Transferencia),
            ("Tarjeta Débito", TipoPago.TarjetaDebito),
            ("Tarjeta Crédito", TipoPago.TarjetaCredito),
            ("Cheque", TipoPago.Cheque)
        };

        private async Task<string> BuildRecargosPorMedioJsonAsync()
        {
            if (_configuracionPagoService is null)
                return "{}";

            var mapa = new Dictionary<string, decimal>();
            foreach (var (etiqueta, tipo) in MediosPagoCobroCuota)
            {
                mapa[etiqueta] = await _configuracionPagoService.ObtenerPorcentajeAjusteUnPagoAsync(tipo);
            }
            return System.Text.Json.JsonSerializer.Serialize(mapa);
        }

        #endregion

        #region Crear / Editar — retirado (ML11)

        // GET/POST: Credito/Create, Credito/Edit — retirados (ML11): auditoría de
        // consumidores confirmó que esta "línea de crédito" manual es un producto
        // separado de Crédito Personal (comparte la tabla Creditos pero nunca genera
        // cuotas persistidas: el único generador real es VentaService.GenerarCuotasCreditoAsync,
        // exclusivo del flujo Cotización/Venta). Sólo tenía dos puntos de entrada de
        // navegación (Credito/Index "Nueva línea" y Cliente/Details "Nuevo credito"),
        // ambos ya retirados, y el GET de alta seguía sembrando TasaInteres como fracción
        // (0.05m = 5%) mientras el resto del sistema usa porcentaje de recargo total
        // (10 = 10%) desde ML9/ML10 — la colisión de unidades que motivó esta auditoría.
        // Edit sólo aplica a créditos en estado Solicitado, un estado que Create ya no
        // puede producir; en esta base no existe ningún crédito en ese estado (verificado
        // por consulta directa), así que no hay historial que preservar todavía, pero se
        // redirige en vez de eliminar la acción por si algún entorno sí tuviera datos
        // legacy — el mismo criterio usado para retirar Credito/Simular en ML10.
        // Details/Index/Delete NO se tocan: siguen sirviendo para consulta histórica.
        [HttpGet]
        public IActionResult Create(string? returnUrl = null)
        {
            TempData["Info"] = "El alta manual de línea de crédito fue retirada. Los créditos personales se generan desde la Venta.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreditoViewModel viewModel, string? returnUrl = null)
        {
            TempData["Info"] = "El alta manual de línea de crédito fue retirada. Los créditos personales se generan desde la Venta.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Edit(int id, string? returnUrl = null)
        {
            TempData["Info"] = "La edición manual de línea de crédito fue retirada.";
            return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, CreditoViewModel viewModel, string? returnUrl = null)
        {
            TempData["Info"] = "La edición manual de línea de crédito fue retirada.";
            return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        #endregion

        #region Eliminar

        // GET: Credito/Delete/5
        public async Task<IActionResult> Delete(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var credito = await _creditoService.GetByIdAsync(id);
                if (credito == null)
                {
                    TempData["Error"] = "Crédito no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                return View("Delete_tw", credito);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar crédito para eliminar: {Id}", id);
                TempData["Error"] = "Error al cargar el crédito";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Credito/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            try
            {
                var resultado = await _creditoService.DeleteAsync(id);
                if (resultado)
                    TempData["Success"] = "Crédito eliminado exitosamente";
                else
                    TempData["Error"] = "No se pudo eliminar el crédito";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar crédito: {Id}", id);
                TempData["Error"] = "Error al eliminar el crédito: " + ex.Message;
            }

            return this.RedirectToReturnUrlOrIndex(returnUrl);
        }

        #endregion

        #region Pagar / Adelantar cuota

        // GET: Credito/PagarCuota/5
        public async Task<IActionResult> PagarCuota(int id, int? cuotaId = null, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var credito = await _creditoService.GetByIdAsync(id);
                if (credito == null)
                {
                    TempData["Error"] = "Crédito no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                var cuotasDisponibles = _creditoUiQueryService.ObtenerCuotasPendientes(credito.Cuotas);

                if (!cuotasDisponibles.Any())
                {
                    TempData["Warning"] = "No hay cuotas pendientes o vencidas para registrar pago.";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                var cuotaSeleccionada = cuotaId.HasValue
                    ? cuotasDisponibles.FirstOrDefault(c => c.Id == cuotaId.Value)
                    : cuotasDisponibles.FirstOrDefault();

                if (cuotaSeleccionada == null)
                {
                    TempData["Error"] = "Cuota no encontrada.";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                // PUN-ML7: fecha comercial única (antes DateTime.Today) para decidir "vencida" en la
                // pantalla de cobro.
                var hoyComercial = _reloj.HoyComercial;
                var estaVencida = EstadoCuotaResolver.EsVencidaPorFecha(cuotaSeleccionada.FechaVencimiento, hoyComercial);
                var diasAtraso = EstadoCuotaResolver.DiasAtrasoDerivado(
                    cuotaSeleccionada.Estado, cuotaSeleccionada.FechaVencimiento, hoyComercial);

                var modelo = new PagarCuotaViewModel
                {
                    CreditoId = credito.Id,
                    CuotaId = cuotaSeleccionada.Id,
                    NumeroCuota = cuotaSeleccionada.NumeroCuota,
                    MontoCuota = cuotaSeleccionada.MontoTotal,
                    MontoPunitorio = cuotaSeleccionada.MontoPunitorio,
                    TotalAPagar = cuotaSeleccionada.SaldoPendiente,
                    MontoPagado = cuotaSeleccionada.SaldoPendiente,
                    ClienteNombre = credito.ClienteNombre,
                    NumeroCreditoTexto = credito.Numero,
                    FechaVencimiento = cuotaSeleccionada.FechaVencimiento,
                    EstaVencida = estaVencida,
                    DiasAtraso = diasAtraso,
                    FechaPago = _reloj.AhoraUtc
                };
                await CargarCuotasPago(modelo, cuotasDisponibles);

                return View("PagarCuota_tw", modelo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar pago de cuota: {Id}", id);
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Credito/PagarCuota
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PagarCuota(PagarCuotaViewModel modelo, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                    var credito = await _creditoService.GetByIdAsync(modelo.CreditoId);
                    if (credito == null)
                    {
                        TempData["Error"] = "Crédito no encontrado";
                        return RedirectToAction(nameof(Index));
                    }

                    var cuotasPendientes = _creditoUiQueryService.ObtenerCuotasPendientes(credito.Cuotas);
                    await CargarCuotasPago(modelo, cuotasPendientes);

                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return View("PagarCuota_tw", modelo);
                }

                var resultado = await _creditoService.PagarCuotaAsync(modelo);

                if (resultado)
                {
                    TempData["Success"] = "Pago registrado exitosamente";
                    return RedirectToReturnUrlOrDetails(returnUrl, modelo.CreditoId);
                }

                // El servicio no distingue el motivo para no revelar la existencia de
                // cuotas o créditos ajenos: crédito inexistente, cuota inexistente o
                // cuota de otro crédito devuelven lo mismo.
                Response.StatusCode = StatusCodes.Status404NotFound;
                ModelState.AddModelError(string.Empty, "No se encontró la cuota indicada para este crédito.");
            }
            catch (PagoCuotaRechazadoException ex)
            {
                Response.StatusCode = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al pagar cuota");
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                ModelState.AddModelError(string.Empty, "No se pudo registrar el pago. Intentá nuevamente.");
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var credito = await _creditoService.GetByIdAsync(modelo.CreditoId);
                var cuotasPendientes = _creditoUiQueryService.ObtenerCuotasPendientes(credito?.Cuotas);
                await CargarCuotasPago(modelo, cuotasPendientes);
            }
            catch
            {
                // si falla, igual mostramos la vista con errores
            }

            return View("PagarCuota_tw", modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPagoMultiple([FromBody] PagoMultipleCuotasRequest? request)
        {
            if (request == null)
                return BadRequest(new { success = false, errors = new[] { "Solicitud inválida." } });

            if (!ModelState.IsValid)
            {
                var errores = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToArray();

                return BadRequest(new { success = false, errors = errores });
            }

            try
            {
                var resultado = await _creditoService.PagarCuotasAsync(request);
                return Ok(new { success = true, data = resultado });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, errors = new[] { ex.Message } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar pago múltiple para cliente {ClienteId}", request.ClienteId);
                return StatusCode(500, new { success = false, errors = new[] { "Error al registrar el pago múltiple." } });
            }
        }

        // GET: Credito/AdelantarCuota/5
        public async Task<IActionResult> AdelantarCuota(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var credito = await _creditoService.GetByIdAsync(id);
                if (credito == null)
                {
                    TempData["Error"] = "Crédito no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                // Obtener la ÚLTIMA cuota pendiente (la que se cancela al adelantar)
                var ultimaCuota = await _creditoService.GetUltimaCuotaPendienteAsync(id);
                if (ultimaCuota == null)
                {
                    TempData["Warning"] = "No hay cuotas pendientes para adelantar.";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                // El adelanto cancela el SALDO de la cuota: si ya tuvo un pago parcial, el total
                // de la cuota sería un sobrepago y el servidor lo rechazaría.
                var saldoAdelanto = ultimaCuota.SaldoPendiente;

                var modelo = new PagarCuotaViewModel
                {
                    CreditoId = credito.Id,
                    CuotaId = ultimaCuota.Id,
                    NumeroCuota = ultimaCuota.NumeroCuota,
                    MontoCuota = ultimaCuota.MontoTotal,
                    MontoPunitorio = ultimaCuota.MontoPunitorio,
                    TotalAPagar = saldoAdelanto,
                    MontoPagado = saldoAdelanto,
                    ClienteNombre = credito.ClienteNombre,
                    NumeroCreditoTexto = credito.Numero,
                    FechaVencimiento = ultimaCuota.FechaVencimiento,
                    EstaVencida = false,
                    DiasAtraso = 0,
                    FechaPago = _reloj.AhoraUtc
                };

                return View("AdelantarCuota_tw", modelo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar adelanto de cuota: {Id}", id);
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Credito/AdelantarCuota
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdelantarCuota(PagarCuotaViewModel modelo, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return View("AdelantarCuota_tw", modelo);
                }

                var resultado = await _creditoService.AdelantarCuotaAsync(modelo);

                if (resultado)
                {
                    TempData["Success"] = $"Cuota #{modelo.NumeroCuota} adelantada exitosamente. Se ha reducido el plazo del crédito.";
                    return RedirectToReturnUrlOrDetails(returnUrl, modelo.CreditoId);
                }

                // Igual que en el pago normal, el servicio no distingue el motivo: crédito
                // inexistente, cuota de otro crédito o crédito sin cuotas adelantables.
                Response.StatusCode = StatusCodes.Status404NotFound;
                ModelState.AddModelError(string.Empty, "No se encontró una cuota adelantable para este crédito.");
            }
            catch (PagoCuotaRechazadoException ex)
            {
                Response.StatusCode = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al adelantar cuota");
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                ModelState.AddModelError(string.Empty, "No se pudo registrar el adelanto. Intentá nuevamente.");
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            return View("AdelantarCuota_tw", modelo);
        }

        #endregion

        #region Cuotas vencidas

        // GET: Credito/CuotasVencidas
        public async Task<IActionResult> CuotasVencidas(string? returnUrl = null)
        {
            try
            {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                var cuotasViewModel = await _creditoService.GetCuotasVencidasAsync();

                await _configuracionMoraService.AplicarAlertasMoraAsync(cuotasViewModel);

                return View("CuotasVencidas_tw", cuotasViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar cuotas vencidas");
                TempData["Error"] = "Error al cargar las cuotas vencidas";
                return View("CuotasVencidas_tw", new List<CuotaViewModel>());
            }
        }

        #endregion

        #region Métodos privados

        private async Task CargarViewBags(int? clienteIdSeleccionado = null, int? garanteIdSeleccionado = null)
        {
            await _viewBagBuilder.CargarAsync(ViewBag, clienteIdSeleccionado, garanteIdSeleccionado);
        }

        #endregion
    }
}
