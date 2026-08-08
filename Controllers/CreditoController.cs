using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
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
using TheBuryProject.ViewModels.PagoCuota;
using TheBuryProject.ViewModels.Punitorio;
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
        private readonly IPunitorioService? _punitorioService;

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
            IRelojComercial? reloj = null,
            IPunitorioService? punitorioService = null)
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
            _punitorioService = punitorioService;
        }

        #region Index / Detalle / Simular

        // GET: Credito
        public async Task<IActionResult> Index(CreditoFilterViewModel filter, CancellationToken cancellationToken = default)
        {
            try
            {
                var creditos = await _creditoService.GetAllAsync(filter);
                var clientes = _creditoUiQueryService.AgruparCreditosPorCliente(creditos);

                // PUN-ML10-G: _PanelClientePartial se renderiza acá, inline, para cada tarjeta de
                // cliente (Index_tw.cshtml) — este es el camino real de la UI (PanelCliente/id, más
                // abajo, no tiene ningún caller JS, confirmado por grep). Una sola consulta batch
                // sobre TODAS las cuotas de TODOS los clientes listados, sin importar cuántos sean.
                await PoblarPunitorioAplicadoPendienteAsync(clientes, cancellationToken);

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

        /// <summary>
        /// PUN-ML10-G: puebla <see cref="CreditoClienteIndexViewModel.MontoPunitorioAplicadoPendiente"/>/
        /// <see cref="CreditoClienteIndexViewModel.CuotasConPunitorioAplicadoPendiente"/> de todos los
        /// grupos recibidos con una única consulta batch autoritativa
        /// (<see cref="IPunitorioService.ObtenerPunitorioAplicadoPendientePorCuotasAsync"/>), nunca
        /// una consulta por cliente/cuota. Si el servicio no está disponible, no rompe la pantalla —
        /// el punitorio pendiente queda en 0 (default) y sólo se registra un warning.
        /// </summary>
        private async Task PoblarPunitorioAplicadoPendienteAsync(
            IReadOnlyCollection<CreditoClienteIndexViewModel> grupos, CancellationToken cancellationToken)
        {
            if (_punitorioService is null)
            {
                if (grupos.Count > 0)
                {
                    _logger.LogWarning(
                        "IPunitorioService no está disponible al listar créditos; punitorio pendiente se muestra en 0 para {Cantidad} cliente(s).",
                        grupos.Count);
                }
                return;
            }

            var cuotaIds = grupos
                .SelectMany(g => g.Creditos)
                .SelectMany(c => c.Cuotas ?? Enumerable.Empty<CuotaViewModel>())
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            if (cuotaIds.Count == 0)
                return;

            var pendientePorCuota = await _punitorioService.ObtenerPunitorioAplicadoPendientePorCuotasAsync(
                cuotaIds, cancellationToken);

            foreach (var grupo in grupos)
            {
                var idsDelGrupo = grupo.Creditos
                    .SelectMany(c => c.Cuotas ?? Enumerable.Empty<CuotaViewModel>())
                    .Select(c => c.Id);

                var pendientesDelGrupo = idsDelGrupo
                    .Where(id => pendientePorCuota.TryGetValue(id, out var monto) && monto > 0m)
                    .Select(id => pendientePorCuota[id])
                    .ToList();

                grupo.MontoPunitorioAplicadoPendiente = pendientesDelGrupo.Sum();
                grupo.CuotasConPunitorioAplicadoPendiente = pendientesDelGrupo.Count;
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

        // GET: /Credito/{creditoId}/Cuotas/{cuotaId}/Punitorio
        // PUN-ML9-B2: consulta read-only y bajo demanda. La ruta incluye el crédito para impedir
        // que un panel de Details cargue accidentalmente una cuota perteneciente a otro crédito.
        [HttpGet("/Credito/{creditoId:int}/Cuotas/{cuotaId:int}/Punitorio")]
        public async Task<IActionResult> DetallePunitorioCuota(
            int creditoId,
            int cuotaId,
            CancellationToken cancellationToken)
        {
            if (creditoId <= 0 || cuotaId <= 0)
                return NotFound();

            if (_punitorioService is null)
            {
                _logger.LogError(
                    "IPunitorioService no está disponible al consultar la cuota {CuotaId} del crédito {CreditoId}",
                    cuotaId,
                    creditoId);
                return Problem(
                    title: "No se pudo cargar el detalle de punitorios.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            try
            {
                var detalle = await _punitorioService.ObtenerDetalleCuotaAsync(cuotaId, cancellationToken);
                if (detalle.CreditoId != creditoId)
                    return NotFound();

                return PartialView(
                    "_PunitorioCuotaDetallePartial",
                    CuotaPunitorioDetalleViewModel.Desde(
                        detalle,
                        User.TienePermiso("cobranzas", "applyfine"),
                        User.TienePermiso("cobranzas", "revertfine")));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al consultar punitorios de la cuota {CuotaId} del crédito {CreditoId}",
                    cuotaId,
                    creditoId);
                return Problem(
                    title: "No se pudo cargar el detalle de punitorios.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("/Credito/{creditoId:int}/Cuotas/{cuotaId:int}/Punitorio/Aplicar")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "applyfine")]
        public async Task<IActionResult> AplicarPunitorioCuota(
            int creditoId,
            int cuotaId,
            [Bind(Prefix = "Acciones.Aplicar")] AplicarPunitorioHttpViewModel form,
            CancellationToken cancellationToken)
        {
            if (creditoId <= 0 || cuotaId <= 0)
                return PunitorioOperacionError(
                    StatusCodes.Status404NotFound,
                    "El credito o la cuota no existen.");

            if (_punitorioService is null)
                return PunitorioOperacionError(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo aplicar el punitorio. Intentá nuevamente.");

            if (string.IsNullOrWhiteSpace(form.Motivo))
                ModelState.AddModelError("Acciones.Aplicar.Motivo", "El motivo es obligatorio.");

            if (!TryDecodeRowVersion(form.CuotaRowVersionBase64, out var cuotaRowVersion))
            {
                ModelState.AddModelError(
                    "Acciones.Aplicar.CuotaRowVersionBase64",
                    "El panel está desactualizado. Recargalo e intentá nuevamente.");
            }

            if (!ModelState.IsValid)
                return PunitorioValidationError();

            try
            {
                var detalle = await _punitorioService.ObtenerDetalleCuotaAsync(cuotaId, cancellationToken);
                if (detalle.CreditoId != creditoId)
                    return PunitorioOperacionError(
                        StatusCodes.Status404NotFound,
                        "La cuota no pertenece al credito indicado.");

                var aplicado = await _punitorioService.AplicarAsync(
                    cuotaId,
                    new PunitorioAplicarComando
                    {
                        Motivo = form.Motivo!,
                        CuotaRowVersionEsperada = cuotaRowVersion
                    },
                    cancellationToken);

                return Json(new PunitorioOperacionResponseViewModel
                {
                    Success = true,
                    ReloadPanel = true,
                    ImporteAplicadoReal = aplicado.Importe,
                    Message = $"Punitorio aplicado por $ {aplicado.Importe:N2}. El importe fue recalculado por el servidor."
                });
            }
            catch (KeyNotFoundException)
            {
                return PunitorioOperacionError(
                    StatusCodes.Status404NotFound,
                    "El credito o la cuota no existen.");
            }
            catch (PunitorioAplicadoRechazadoException ex)
            {
                return PunitorioRechazado(ex, "aplicar");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al aplicar punitorio de la cuota {CuotaId} del crédito {CreditoId}",
                    cuotaId,
                    creditoId);
                return PunitorioOperacionError(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo aplicar el punitorio. Intentá nuevamente.");
            }
        }

        [HttpPost("/Credito/{creditoId:int}/Cuotas/{cuotaId:int}/Punitorio/{punitorioAplicadoId:int}/Anular")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "revertfine")]
        public async Task<IActionResult> AnularPunitorioCuota(
            int creditoId,
            int cuotaId,
            int punitorioAplicadoId,
            [Bind(Prefix = "Acciones.Anulacion.Form")] AnularPunitorioHttpViewModel form,
            CancellationToken cancellationToken)
        {
            if (creditoId <= 0 || cuotaId <= 0 || punitorioAplicadoId <= 0)
                return PunitorioOperacionError(
                    StatusCodes.Status404NotFound,
                    "El crédito, la cuota o la aplicación no existen.");

            if (_punitorioService is null)
                return PunitorioOperacionError(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo anular el punitorio. Intentá nuevamente.");

            if (string.IsNullOrWhiteSpace(form.Motivo))
                ModelState.AddModelError("Acciones.Anulacion.Form.Motivo", "El motivo es obligatorio.");

            if (!TryDecodeRowVersion(form.PunitorioAplicadoRowVersionBase64, out var aplicacionRowVersion))
            {
                ModelState.AddModelError(
                    "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64",
                    "El panel está desactualizado. Recargalo e intentá nuevamente.");
            }

            if (!ModelState.IsValid)
                return PunitorioValidationError();

            try
            {
                var detalle = await _punitorioService.ObtenerDetalleCuotaAsync(cuotaId, cancellationToken);
                if (detalle.CreditoId != creditoId ||
                    !detalle.AplicacionesHistoricas.Any(a => a.PunitorioAplicadoId == punitorioAplicadoId))
                {
                    return PunitorioOperacionError(
                        StatusCodes.Status404NotFound,
                        "La aplicación no pertenece a la cuota y al crédito indicados.");
                }

                await _punitorioService.AnularAsync(
                    punitorioAplicadoId,
                    new PunitorioAnularComando
                    {
                        Motivo = form.Motivo!,
                        RowVersionEsperado = aplicacionRowVersion
                    },
                    cancellationToken);

                return Json(new PunitorioOperacionResponseViewModel
                {
                    Success = true,
                    ReloadPanel = true,
                    Message = "La aplicación de punitorio fue anulada. El historial se conserva."
                });
            }
            catch (KeyNotFoundException)
            {
                return PunitorioOperacionError(
                    StatusCodes.Status404NotFound,
                    "El crédito, la cuota o la aplicación no existen.");
            }
            catch (PunitorioAplicadoRechazadoException ex)
            {
                return PunitorioRechazado(ex, "anular");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al anular punitorio {PunitorioAplicadoId} de la cuota {CuotaId} del crédito {CreditoId}",
                    punitorioAplicadoId,
                    cuotaId,
                    creditoId);
                return PunitorioOperacionError(
                    StatusCodes.Status500InternalServerError,
                    "No se pudo anular el punitorio. Intentá nuevamente.");
            }
        }

        private IActionResult PunitorioRechazado(PunitorioAplicadoRechazadoException ex, string operacion)
        {
            var statusCode = ex.Motivo switch
            {
                MotivoRechazoPunitorioAplicado.NoAutorizado => StatusCodes.Status403Forbidden,
                MotivoRechazoPunitorioAplicado.SolicitudInvalida => StatusCodes.Status400BadRequest,
                MotivoRechazoPunitorioAplicado.NoAplicable => StatusCodes.Status409Conflict,
                MotivoRechazoPunitorioAplicado.Conflicto => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };

            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? $"No se pudo {operacion} el punitorio."
                : ex.Message;

            return PunitorioOperacionError(
                statusCode,
                message,
                reloadPanel: statusCode == StatusCodes.Status409Conflict);
        }

        private IActionResult PunitorioValidationError()
        {
            var errors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!.Errors
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "El valor ingresado no es válido."
                            : error.ErrorMessage)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal);

            return PunitorioOperacionError(
                StatusCodes.Status400BadRequest,
                "Revisá los datos del formulario.",
                errors: errors);
        }

        private IActionResult PunitorioOperacionError(
            int statusCode,
            string message,
            bool reloadPanel = false,
            IReadOnlyDictionary<string, string[]>? errors = null) =>
            StatusCode(statusCode, new PunitorioOperacionResponseViewModel
            {
                Success = false,
                Message = message,
                ReloadPanel = reloadPanel,
                Errors = errors
            });

        private static bool TryDecodeRowVersion(string? base64, out byte[] rowVersion)
        {
            rowVersion = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(base64))
                return false;

            try
            {
                rowVersion = Convert.FromBase64String(base64);
                return rowVersion.Length == 8;
            }
            catch (FormatException)
            {
                rowVersion = Array.Empty<byte>();
                return false;
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
        public async Task<IActionResult> PanelCliente(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
                return BadRequest();

            var creditos = await _creditoService.GetByClienteIdAsync(id);
            var grupo = _creditoUiQueryService.AgruparCreditosPorCliente(creditos).FirstOrDefault();

            if (grupo == null)
                return NotFound();

            // PUN-ML10-G: mismo helper que Index (batch autoritativo, nunca Cuota.MontoPunitorio).
            await PoblarPunitorioAplicadoPendienteAsync(new[] { grupo }, cancellationToken);

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
                // ML6: la UI ya no ofrece elegir método/fuente (Global/Manual/Cliente/Perfil/
                // Producto); ambos quedan fijos en Global — sin autoridad sobre el porcentaje,
                // que sale siempre del plan de cuotas (ver CreditoConfiguracionVentaService).
                // Se conservan en el modelo/payload solo por compatibilidad de binding.
                FuenteConfiguracion = FuenteConfiguracionCredito.Global,
                MetodoCalculo = MetodoCalculoCredito.Global,
                PerfilCreditoSeleccionadoId = null,
                Monto = montoVenta,
                // Intención conservada de una cotización convertida (mismo patrón que
                // CantidadCuotas abajo). No es autoridad: el operador puede modificarla y el
                // servidor revalida contra el total real al confirmar.
                Anticipo = credito.AnticipoPreseleccionado,
                MontoFinanciado = montoVenta,
                CantidadCuotas = credito.CantidadCuotas > 0 ? credito.CantidadCuotas : 0,
                // ML6: ya no se precarga la tasa/perfil/cliente como si fuera el porcentaje
                // financiero. La UI la muestra sólo de forma read-only, resuelta por el servidor
                // desde el plan de cuotas (SimularPlanVenta), nunca desde ParametrosCreditoCliente.
                TasaMensual = null,
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
        /// plan efectivo. ML6.1 — Contrato congelado: <paramref name="tasaMensual"/> ya no tiene
        /// autoridad bajo ninguna combinación de <paramref name="fuenteConfiguracion"/>/
        /// <paramref name="metodoCalculo"/> (ni siquiera ambos Manual): el porcentaje sale siempre
        /// del plan de cuotas (ver CreditoSimulacionVentaService.SimularAsync).
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
                // ML6/ML6.1: con ventaId (el único caso real de Configurar Venta — venta con
                // productos ya asociados) metodoCalculo/fuenteConfiguracion/tasaMensual dejan de
                // reenviarse al service, aunque el caller todavía los mande: el porcentaje sale
                // siempre del plan de cuotas resuelto por el servidor, igual que en el POST de
                // confirmación (CreditoConfiguracionVentaService.ResolverAsync). Un payload
                // manipulado con metodoCalculo=Manual&fuenteConfiguracion=Manual&tasaMensual=99 no
                // puede pisar el porcentaje del plan — el service tampoco lo honraría aunque
                // llegara (ML6.1 eliminó la rama Manual). Sin ventaId se preserva el contrato
                // previo de parámetros (simulación standalone sin venta real): metodoCalculo/
                // fuenteConfiguracion siguen decidiendo si hace falta un cliente; tasaMensual sigue
                // sin autoridad en ningún caso.
                var metodoEfectivo = ventaId.HasValue ? null : metodoCalculo;
                var fuenteEfectiva = ventaId.HasValue ? null : fuenteConfiguracion;
                var tasaEfectiva = ventaId.HasValue ? null : tasaMensual;

                var resultado = await _creditoSimulacionVentaService.SimularAsync(new CreditoSimulacionVentaRequest
                {
                    TotalVenta = totalVenta,
                    Anticipo = anticipo,
                    Cuotas = cuotas,
                    GastosAdministrativos = gastosAdministrativos,
                    FechaPrimeraCuota = fechaPrimeraCuota,
                    TasaMensual = tasaEfectiva,
                    VentaId = ventaId,
                    MetodoCalculo = metodoEfectivo,
                    FuenteConfiguracion = fuenteEfectiva
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

        private const string PagoCuotaResultadoTempDataKey = "PagoCuotaResultado";

        // El Id de ruta es el Id de la cuota. El crédito y el cliente se derivan server-side.
        [HttpGet("/Credito/PagarCuota/{id:int}")]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> PagarCuota(int id, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            var contexto = await _creditoService.ObtenerContextoPagoCuotaAsync(id);
            if (contexto is null)
                return NotFound();

            PagoCuotaResultadoViewModel? resultado = null;
            if (TempData is not null && TempData[PagoCuotaResultadoTempDataKey] is string resultadoJson)
            {
                try
                {
                    resultado = JsonSerializer.Deserialize<PagoCuotaResultadoViewModel>(resultadoJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "No se pudo leer el resultado temporal del pago de cuota {CuotaId}.", id);
                }
            }

            var input = new PagarCuotaInputModel
            {
                MontoIngresado = contexto.TotalCobrableActual ?? 0m,
                MedioPago = "Efectivo",
                CuotaRowVersionBase64 = contexto.CuotaRowVersionBase64
            };

            var page = await ConstruirPaginaPagoCuotaAsync(contexto, input, resultado, incluirPreview: resultado is null);
            return View("PagarCuota_tw", page);
        }

        [HttpPost("/Credito/PagarCuota/{id:int}/Preview")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> PrevisualizarPagoCuota(
            int id,
            [Bind(Prefix = "Input")] PagarCuotaInputModel input)
        {
            if (!TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
                ModelState.AddModelError("Input.CuotaRowVersionBase64", "La versión de la cuota es inválida.");

            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var preview = await _creditoService.PrevisualizarPagoCuotaAsync(
                    CrearComandoPago(id, input, rowVersion));
                return preview is null ? NotFound() : Ok(MapPreview(preview));
            }
            catch (PagoCuotaRechazadoException ex)
            {
                var status = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return StatusCode(status, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al previsualizar el pago de cuota {CuotaId}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "No se pudo previsualizar el pago. Intentá nuevamente." });
            }
        }

        [HttpPost("/Credito/PagarCuota/{id:int}")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> PagarCuota(
            int id,
            [Bind(Prefix = "Input")] PagarCuotaInputModel input,
            string? returnUrl = null)
        {
            if (!TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
                ModelState.AddModelError("Input.CuotaRowVersionBase64", "La versión de la cuota es inválida.");

            if (!ModelState.IsValid)
                return await RenderPagoCuotaErrorAsync(id, input, StatusCodes.Status400BadRequest, returnUrl);

            try
            {
                var resultado = await _creditoService.RegistrarPagoCuotaIndividualAsync(
                    CrearComandoPago(id, input, rowVersion));
                if (resultado is null)
                    return NotFound();

                TempData[PagoCuotaResultadoTempDataKey] = JsonSerializer.Serialize(MapResultado(resultado));
                // returnUrl solo gobierna a dónde vuelve el operador (link "Volver al crédito" en la
                // página de recibo) — nunca la operación financiera, que ya quedó persistida arriba.
                return RedirectToAction(nameof(PagarCuota), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (PagoCuotaRechazadoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                var status = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return await RenderPagoCuotaErrorAsync(id, input, status, returnUrl);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return await RenderPagoCuotaErrorAsync(id, input, StatusCodes.Status400BadRequest, returnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al pagar la cuota {CuotaId}.", id);
                ModelState.AddModelError(string.Empty, "No se pudo registrar el pago. Intentá nuevamente.");
                return await RenderPagoCuotaErrorAsync(id, input, StatusCodes.Status500InternalServerError, returnUrl);
            }
        }

        private async Task<IActionResult> RenderPagoCuotaErrorAsync(
            int cuotaId,
            PagarCuotaInputModel input,
            int statusCode,
            string? returnUrl)
        {
            var contexto = await _creditoService.ObtenerContextoPagoCuotaAsync(cuotaId);
            if (contexto is null)
                return NotFound();

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            Response.StatusCode = statusCode;
            var page = await ConstruirPaginaPagoCuotaAsync(
                contexto,
                input,
                resultado: null,
                incluirPreview: statusCode == StatusCodes.Status400BadRequest && ModelState.IsValid);
            return View("PagarCuota_tw", page);
        }

        private async Task<PagarCuotaPageViewModel> ConstruirPaginaPagoCuotaAsync(
            PagoCuotaContextoResultado contexto,
            PagarCuotaInputModel input,
            PagoCuotaResultadoViewModel? resultado,
            bool incluirPreview)
        {
            PagoCuotaPreviewViewModel? preview = null;
            if (incluirPreview &&
                contexto.TotalCobrableActual is > 0m &&
                TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
            {
                try
                {
                    var calculada = await _creditoService.PrevisualizarPagoCuotaAsync(
                        CrearComandoPago(contexto.CuotaId, input, rowVersion));
                    if (calculada is not null)
                        preview = MapPreview(calculada);
                }
                catch (PagoCuotaRechazadoException)
                {
                    // El error explícito del POST prevalece. En GET, la ausencia de preview deja
                    // deshabilitada la confirmación y el navegador puede volver a solicitarla.
                }
                catch (InvalidOperationException)
                {
                    // Mismo criterio: no ocultar ni reemplazar ModelState con un error derivado.
                }
            }

            return new PagarCuotaPageViewModel
            {
                Contexto = MapContexto(contexto),
                Input = input,
                Preview = preview,
                Resultado = resultado
            };
        }

        private static PagoCuotaIndividualComando CrearComandoPago(
            int cuotaId,
            PagarCuotaInputModel input,
            byte[] rowVersion) =>
            new(
                cuotaId,
                input.MontoIngresado,
                input.MedioPago,
                input.Comprobante,
                input.Observaciones,
                rowVersion);

        private static PagoCuotaContextoViewModel MapContexto(PagoCuotaContextoResultado source) => new()
        {
            CuotaId = source.CuotaId,
            CreditoId = source.CreditoId,
            NumeroCuota = source.NumeroCuota,
            NumeroCredito = source.NumeroCredito,
            ClienteNombre = source.ClienteNombre,
            FechaVencimiento = source.FechaVencimiento,
            FechaComercial = source.FechaComercial,
            Estado = source.Estado,
            DiasAtraso = source.DiasAtraso,
            CapitalPendiente = source.CapitalPendiente,
            PunitorioCalculadoInformativo = source.PunitorioCalculadoInformativo,
            EstadoCalculoPunitorio = source.EstadoCalculoPunitorio,
            MotivoNoCalculoPunitorio = source.MotivoNoCalculoPunitorio,
            PunitorioAplicadoPendiente = source.PunitorioAplicadoPendiente,
            TotalCobrableActual = source.TotalCobrableActual,
            HistorialCompleto = source.HistorialCompleto,
            MotivoHistorialIncompleto = source.MotivoHistorialIncompleto,
            CuotaRowVersionBase64 = source.CuotaRowVersionBase64
        };

        private static PagoCuotaPreviewViewModel MapPreview(PagoCuotaPreviewResultado source) => new()
        {
            ImporteIngresado = source.ImporteIngresado,
            AplicadoPunitorio = source.AplicadoPunitorio,
            AplicadoCapital = source.AplicadoCapital,
            Excedente = source.Excedente,
            RecargoMedioPago = source.RecargoMedioPago,
            TotalCaja = source.TotalCaja,
            PunitorioRestante = source.PunitorioRestante,
            CapitalRestante = source.CapitalRestante,
            EstadoEstimado = source.EstadoEstimado,
            EstadoEstimadoTexto = source.EstadoEstimado.ToString(),
            FechaComercial = source.FechaComercial,
            CuotaRowVersionBase64 = source.CuotaRowVersionBase64
        };

        private static PagoCuotaResultadoViewModel MapResultado(PagoCuotaResultado source) => new()
        {
            CuotaId = source.CuotaId,
            ImporteRecibido = source.ImporteRecibido,
            AplicadoPunitorio = source.AplicadoPunitorio,
            AplicadoCapital = source.AplicadoCapital,
            RecargoMedioPago = source.RecargoMedioPago,
            TotalCaja = source.TotalCaja,
            PunitorioRestante = source.PunitorioRestante,
            CapitalRestante = source.CapitalRestante,
            EstadoFinal = source.EstadoFinal,
            EstadoFinalTexto = source.EstadoFinal.ToString(),
            FechaComercial = source.FechaComercial,
            MovimientoCajaId = source.MovimientoCajaId,
            PagoCuotaId = source.PagoCuotaId,
            MedioPago = source.MedioPago
        };

        // PUN-ML9-E: el request sólo trae intención (cliente + cuotas + medio); el servidor
        // recalcula capital y punitorio aplicado pendiente de cada una. Cobrar requiere el mismo
        // permiso que el pago individual y el adelanto — antes sólo exigía creditos.view (heredado
        // del controller), lo que permitía a un Vendedor cobrar con un POST directo.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
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
            catch (PagoCuotaRechazadoException ex)
            {
                // Conflicto = RowVersion vencida (alguna cuota cambió desde el preview/listado);
                // el resto son datos inválidos del request. Ambos casos: rollback total ya ocurrió
                // en el servicio, cero pagos/movimientos/cupo parciales.
                var status = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return StatusCode(status, new { success = false, errors = new[] { ex.Message } });
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

        // PUN-ML9-E: preview read-only del pago múltiple — misma autoridad (DistribuirPago) que la
        // confirmación, no persiste nada. Requiere el mismo permiso que cobrar: aunque no muta datos,
        // expone el punitorio aplicado pendiente real de cada cuota.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> PreviewPagoMultiple([FromBody] PagoMultiplePreviewRequestViewModel? request)
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
                var preview = await _creditoService.PrevisualizarPagoMultipleAsync(
                    request.ClienteId, request.CuotaIds, request.MedioPago);

                return Ok(new { success = true, data = MapPreviewMultiple(preview) });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, errors = new[] { ex.Message } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al previsualizar el pago múltiple para cliente {ClienteId}", request.ClienteId);
                return StatusCode(500, new { success = false, errors = new[] { "No se pudo previsualizar el pago. Intentá nuevamente." } });
            }
        }

        private static PagoMultiplePreviewViewModel MapPreviewMultiple(PagoMultiplePreviewResultado source) => new()
        {
            ClienteId = source.ClienteId,
            Cuotas = source.Cuotas
                .Select(c => new PagoMultiplePreviewCuotaViewModel
                {
                    CuotaId = c.CuotaId,
                    CreditoId = c.CreditoId,
                    CreditoNumero = c.CreditoNumero,
                    NumeroCuota = c.NumeroCuota,
                    CapitalPendiente = c.CapitalPendiente,
                    PunitorioAplicadoPendiente = c.PunitorioAplicadoPendiente,
                    Total = c.Total,
                    RecargoMedioPago = c.RecargoMedioPago,
                    TotalCaja = c.TotalCaja,
                    CuotaRowVersionBase64 = c.CuotaRowVersionBase64
                })
                .ToList(),
            CapitalTotal = source.CapitalTotal,
            PunitorioTotal = source.PunitorioTotal,
            RecargoTotal = source.RecargoTotal,
            TotalCaja = source.TotalCaja,
            FechaComercial = source.FechaComercial
        };

        private const string AdelantoResultadoTempDataKey = "AdelantoResultado";

        // El Id de ruta es el Id del crédito: el adelanto siempre opera sobre "la última cuota
        // pendiente del plan", que el servidor resuelve — nunca una cuota que elija el navegador.
        [HttpGet("/Credito/AdelantarCuota/{id:int}")]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> AdelantarCuota(int id, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            var credito = await _creditoService.GetByIdAsync(id);
            if (credito == null)
            {
                TempData["Error"] = "Crédito no encontrado";
                return RedirectToAction(nameof(Index));
            }

            PagoCuotaResultadoViewModel? resultado = null;
            if (TempData[AdelantoResultadoTempDataKey] is string resultadoJson)
            {
                try
                {
                    resultado = JsonSerializer.Deserialize<PagoCuotaResultadoViewModel>(resultadoJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "No se pudo leer el resultado temporal del adelanto del crédito {CreditoId}.", id);
                }
            }

            var contexto = await _creditoService.ObtenerContextoAdelantoAsync(id);
            if (contexto is null)
            {
                // Adelantar la ÚLTIMA cuota pendiente del plan deja el crédito sin ninguna cuota
                // adelantable — este mismo GET (al que redirige el POST exitoso) no puede resolver
                // "la próxima". Sin este fallback, el resultado recién persistido (con su desglose
                // capital/punitorio/PagoCuotaId) nunca llegaba a mostrarse: la pantalla rebotaba
                // directo a Details con el mensaje de "no hay cuotas" pisando al de éxito.
                if (resultado is not null)
                {
                    var contextoPagado = await _creditoService.ObtenerContextoPagoCuotaAsync(resultado.CuotaId);
                    if (contextoPagado is not null)
                    {
                        var pagePagado = new AdelantarCuotaPageViewModel
                        {
                            Contexto = MapContexto(contextoPagado),
                            Input = new AdelantoCuotaInputModel
                            {
                                MedioPago = "Efectivo",
                                CuotaRowVersionBase64 = contextoPagado.CuotaRowVersionBase64
                            },
                            Preview = null,
                            Resultado = resultado
                        };
                        return View("AdelantarCuota_tw", pagePagado);
                    }
                }

                TempData["Warning"] = "No hay cuotas pendientes para adelantar.";
                return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }

            var input = new AdelantoCuotaInputModel
            {
                MedioPago = "Efectivo",
                CuotaRowVersionBase64 = contexto.CuotaRowVersionBase64
            };

            var page = await ConstruirPaginaAdelantoAsync(id, contexto, input, resultado, incluirPreview: resultado is null);
            return View("AdelantarCuota_tw", page);
        }

        [HttpPost("/Credito/AdelantarCuota/{id:int}/Preview")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> PrevisualizarAdelanto(
            int id,
            [Bind(Prefix = "Input")] AdelantoCuotaInputModel input)
        {
            if (!TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
                ModelState.AddModelError("Input.CuotaRowVersionBase64", "La versión de la cuota es inválida.");

            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var preview = await _creditoService.PrevisualizarAdelantoAsync(
                    CrearComandoAdelanto(id, input, rowVersion));
                return preview is null ? NotFound() : Ok(MapPreview(preview));
            }
            catch (PagoCuotaRechazadoException ex)
            {
                var status = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return StatusCode(status, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al previsualizar el adelanto del crédito {CreditoId}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "No se pudo previsualizar el adelanto. Intentá nuevamente." });
            }
        }

        [HttpPost("/Credito/AdelantarCuota/{id:int}")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cobranzas", Accion = "payinstallment")]
        public async Task<IActionResult> AdelantarCuota(
            int id,
            [Bind(Prefix = "Input")] AdelantoCuotaInputModel input,
            string? returnUrl = null)
        {
            if (!TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
                ModelState.AddModelError("Input.CuotaRowVersionBase64", "La versión de la cuota es inválida.");

            if (!ModelState.IsValid)
                return await RenderAdelantoErrorAsync(id, input, StatusCodes.Status400BadRequest, returnUrl);

            try
            {
                var resultado = await _creditoService.RegistrarAdelantoAsync(
                    CrearComandoAdelanto(id, input, rowVersion));

                if (resultado is null)
                {
                    TempData["Warning"] = "No se encontró una cuota adelantable para este crédito.";
                    return RedirectToReturnUrlOrDetails(returnUrl, id);
                }

                TempData[AdelantoResultadoTempDataKey] = JsonSerializer.Serialize(MapResultado(resultado));
                TempData["Success"] = $"Cuota #{resultado.NumeroCuota} adelantada exitosamente. Se ha reducido el plazo del crédito.";
                // returnUrl sólo gobierna a dónde vuelve el operador — nunca la operación
                // financiera, que ya quedó persistida arriba.
                return RedirectToAction(nameof(AdelantarCuota), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (PagoCuotaRechazadoException ex)
            {
                var status = ex.Motivo == MotivoRechazoPagoCuota.Conflicto
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, ex.Message);
                return await RenderAdelantoErrorAsync(id, input, status, returnUrl);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return await RenderAdelantoErrorAsync(id, input, StatusCodes.Status400BadRequest, returnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al adelantar la cuota del crédito {CreditoId}.", id);
                ModelState.AddModelError(string.Empty, "No se pudo registrar el adelanto. Intentá nuevamente.");
                return await RenderAdelantoErrorAsync(id, input, StatusCodes.Status500InternalServerError, returnUrl);
            }
        }

        private async Task<IActionResult> RenderAdelantoErrorAsync(
            int creditoId,
            AdelantoCuotaInputModel input,
            int statusCode,
            string? returnUrl)
        {
            var contexto = await _creditoService.ObtenerContextoAdelantoAsync(creditoId);
            if (contexto is null)
            {
                TempData["Warning"] = "No se encontró una cuota adelantable para este crédito.";
                return RedirectToReturnUrlOrDetails(returnUrl, creditoId);
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            Response.StatusCode = statusCode;
            var page = await ConstruirPaginaAdelantoAsync(
                creditoId,
                contexto,
                input,
                resultado: null,
                incluirPreview: statusCode == StatusCodes.Status400BadRequest && ModelState.IsValid);
            return View("AdelantarCuota_tw", page);
        }

        private async Task<AdelantarCuotaPageViewModel> ConstruirPaginaAdelantoAsync(
            int creditoId,
            PagoCuotaContextoResultado contexto,
            AdelantoCuotaInputModel input,
            PagoCuotaResultadoViewModel? resultado,
            bool incluirPreview)
        {
            PagoCuotaPreviewViewModel? preview = null;
            if (incluirPreview &&
                contexto.TotalCobrableActual is > 0m &&
                TryDecodeRowVersion(input.CuotaRowVersionBase64, out var rowVersion))
            {
                try
                {
                    var calculada = await _creditoService.PrevisualizarAdelantoAsync(
                        CrearComandoAdelanto(creditoId, input, rowVersion));
                    if (calculada is not null)
                        preview = MapPreview(calculada);
                }
                catch (PagoCuotaRechazadoException)
                {
                    // El error explícito del POST prevalece. En GET, la ausencia de preview deja
                    // deshabilitada la confirmación y el navegador puede volver a solicitarla.
                }
                catch (InvalidOperationException)
                {
                    // Mismo criterio: no ocultar ni reemplazar ModelState con un error derivado.
                }
            }

            return new AdelantarCuotaPageViewModel
            {
                Contexto = MapContexto(contexto),
                Input = input,
                Preview = preview,
                Resultado = resultado
            };
        }

        private static AdelantoCuotaComando CrearComandoAdelanto(
            int creditoId,
            AdelantoCuotaInputModel input,
            byte[] rowVersion) =>
            new(creditoId, input.MedioPago, input.Comprobante, input.Observaciones, rowVersion);

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
