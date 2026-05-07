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
        private readonly IEvaluacionCreditoService _evaluacionService;
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

        private IActionResult RedirectToReturnUrlOrDetails(string? returnUrl, int creditoId)
        {
            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Details), new { id = creditoId });
        }

        public CreditoController(
            ICreditoService creditoService,
            IEvaluacionCreditoService evaluacionService,
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
            ICondicionesPagoCarritoResolver? condicionesPagoCarritoResolver = null,
            ICreditoRangoProductoService? creditoRangoProductoService = null,
            ICreditoConfiguracionVentaService? creditoConfiguracionVentaService = null,
            ICreditoSimulacionVentaService? creditoSimulacionVentaService = null,
            ICreditoUiQueryService? creditoUiQueryService = null)
        {
            _creditoService = creditoService;
            _evaluacionService = evaluacionService;
            _configuracionPagoService = configuracionPagoService;
            _configuracionMoraService = configuracionMoraService;
            _ventaService = ventaService;
            _logger = logger;
            _creditoDisponibleService = creditoDisponibleService;
            _currentUser = currentUser;
            _viewBagBuilder = viewBagBuilder;
            _contratoVentaCreditoService = contratoVentaCreditoService;
            _creditoRangoProductoService = creditoRangoProductoService
                ?? (condicionesPagoCarritoResolver is not null
                    ? new CreditoRangoProductoService(condicionesPagoCarritoResolver)
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
                    aptitudService);
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

        // GET: Credito/Simular
        [HttpGet]
        public IActionResult Simular(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            return View("Simular_tw", new SimularCreditoViewModel
            {
                CantidadCuotas = 12,
                TasaInteresMensual = 0.05m
            });
        }

        // POST: Credito/Simular
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Simular(SimularCreditoViewModel modelo, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            if (!ModelState.IsValid)
                return View("Simular_tw", modelo);

            try
            {
                var resultado = await _creditoService.SimularCreditoAsync(modelo);
                return View("Simular_tw", resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al simular crédito");
                TempData["Error"] = "Error al simular el crédito: " + ex.Message;
                return View("Simular_tw", modelo);
            }
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

                var evaluacion = await _evaluacionService.GetEvaluacionByCreditoIdAsync(id);

                var detalle = new CreditoDetalleViewModel
                {
                    Credito = credito,
                    Evaluacion = evaluacion
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

        public async Task<IActionResult> ConfigurarVenta(int id, int? ventaId, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            var credito = await _creditoService.GetByIdAsync(id);
            if (credito == null)
            {
                TempData["Error"] = "Crédito no encontrado";
                return RedirectToAction(nameof(Index));
            }

            // Validar que el crédito esté en estado que permita configuración
            if (credito.Estado != EstadoCredito.PendienteConfiguracion &&
                credito.Estado != EstadoCredito.Solicitado &&
                credito.Estado != EstadoCredito.Configurado)
            {
                // Si ya está Generado o más avanzado, no permitir reconfigurar
                if (credito.Estado == EstadoCredito.Generado ||
                         credito.Estado == EstadoCredito.Activo ||
                         credito.Estado == EstadoCredito.Finalizado)
                {
                    TempData["Warning"] = "El crédito ya fue generado y no puede reconfigurarse.";
                }
                else
                {
                    TempData["Error"] = $"El crédito no puede configurarse en estado {credito.Estado}.";
                }

                if (ventaId.HasValue)
                    return RedirectToAction("Details", "Venta", new { id = ventaId });
                return RedirectToAction("Details", new { id });
            }

            decimal montoVenta = credito.MontoAprobado > 0 ? credito.MontoAprobado : credito.MontoSolicitado;

            var tasaMensualConfig = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
            if (tasaMensualConfig == null)
            {
                TempData["Error"] = "La tasa de interés de Crédito Personal no está configurada. " +
                    "Configure el valor en Administración → Tipos de Pago antes de continuar.";
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
                MetodoCalculo = MetodoCalculoCredito.AutomaticoPorCliente,
                PerfilCreditoSeleccionadoId = parametrosCliente.PerfilPreferidoId,
                Monto = montoVenta,
                Anticipo = 0,
                MontoFinanciado = montoVenta,
                CantidadCuotas = credito.CantidadCuotas > 0 ? credito.CantidadCuotas : 0,
                TasaMensual = parametrosCliente.TasaMensual,
                GastosAdministrativos = parametrosCliente.GastosAdministrativos,
                FechaPrimeraCuota = credito.FechaPrimeraCuota,
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
            var rangoGet = await ResolverRangoCreditoProductoAsync(venta, cuotasMinGet, cuotasMaxGet);
            if (rangoGet.Error is not null)
            {
                TempData["Error"] = rangoGet.Error;
                if (ventaId.HasValue)
                    return RedirectToAction("Details", "Venta", new { id = ventaId });
                return RedirectToAction("Details", new { id });
            }

            AplicarRangoEfectivoAlModelo(modelo, rangoGet);

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
                ProductoRestrictivoNombre = modelo.ProductoRestrictivoNombre
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

            return View("ConfigurarVenta_tw", modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfigurarVenta(ConfiguracionCreditoVentaViewModel modelo, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return View("ConfigurarVenta_tw", modelo);
            }

            var venta = modelo.VentaId.HasValue
                ? await _ventaService.GetByIdAsync(modelo.VentaId.Value)
                : null;

            var resultadoConfiguracion = await _creditoConfiguracionVentaService.ResolverAsync(modelo, venta);
            if (resultadoConfiguracion.RangoEfectivo is not null)
            {
                AplicarRangoEfectivoAlModelo(modelo, resultadoConfiguracion.RangoEfectivo);
            }

            if (!resultadoConfiguracion.EsValido)
            {
                ModelState.AddModelError(resultadoConfiguracion.ErrorKey ?? string.Empty, resultadoConfiguracion.ErrorMessage ?? string.Empty);
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            await _creditoService.ConfigurarCreditoAsync(resultadoConfiguracion.Comando!);

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
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SimularPlanVenta(
            decimal totalVenta,
            decimal? anticipo,
            int cuotas,
            decimal? gastosAdministrativos,
            string? fechaPrimeraCuota,
            decimal? tasaMensual)
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
                    TasaMensual = tasaMensual
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
            modelo.ClienteConfigPersonalizada = new ClienteConfigCreditoVentaViewModel
            {
                MaxCuotasCreditoProducto = modelo.MaxCuotasCreditoProducto,
                RestriccionCreditoProductoDescripcion = modelo.RestriccionCreditoProductoDescripcion,
                MaxCuotasBase = modelo.MaxCuotasBase,
                ProductoIdRestrictivo = modelo.ProductoIdRestrictivo,
                ProductoRestrictivoNombre = modelo.ProductoRestrictivoNombre
            };
            return View("ConfigurarVenta_tw", modelo);
        }

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

        private void CargarCuotasPago(PagarCuotaViewModel modelo, IReadOnlyCollection<CuotaViewModel> cuotas)
        {
            modelo.Cuotas = _creditoUiQueryService.ProyectarCuotasPendientes(cuotas);
            modelo.CuotasJson = _creditoUiQueryService.BuildCuotasJson(cuotas);
        }

        #endregion

        #region Crear / Editar

        // GET: Credito/Create
        public async Task<IActionResult> Create(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            await CargarViewBags();
            return View("Create_tw", new CreditoViewModel
            {
                FechaSolicitud = DateTime.UtcNow,
                TasaInteres = 0.05m,
                CantidadCuotas = 12
            });
        }

        // POST: Credito/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditoViewModel viewModel, string? returnUrl = null)
        {
            _logger.LogInformation("=== INICIANDO CREACIÓN DE LÍNEA DE CRÉDITO ===");
            _logger.LogInformation("ClienteId: {ClienteId}", viewModel.ClienteId);
            _logger.LogInformation("MontoSolicitado: {Monto}", viewModel.MontoSolicitado);
            _logger.LogInformation("TasaInteres: {Tasa}", viewModel.TasaInteres);
            _logger.LogInformation("RequiereGarante: {RequiereGarante}", viewModel.RequiereGarante);

            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("ModelState inválido al crear crédito");
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                    await CargarViewBags(viewModel.ClienteId, viewModel.GaranteId);
                    return View("Create_tw", viewModel);
                }

                var credito = await _creditoService.CreateAsync(viewModel);

                TempData["Success"] = $"Línea de Crédito {credito.Numero} creada exitosamente";
                return RedirectToAction(nameof(Details), new { id = credito.Id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (CreditoDisponibleException ex)
            {
                _logger.LogWarning(ex, "Alta de crédito bloqueada por disponible insuficiente para cliente {ClienteId}", viewModel.ClienteId);
                ModelState.AddModelError("", ex.Message);
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                await CargarViewBags(viewModel.ClienteId, viewModel.GaranteId);
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear línea de crédito");
                ModelState.AddModelError("", "Error al crear la línea de crédito: " + ex.Message);
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                await CargarViewBags(viewModel.ClienteId, viewModel.GaranteId);
                return View("Create_tw", viewModel);
            }
        }

        // GET: Credito/Edit/5
        public async Task<IActionResult> Edit(int id, string? returnUrl = null)
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

                if (credito.Estado != EstadoCredito.Solicitado)
                {
                    TempData["Error"] = "Solo se pueden editar créditos en estado Solicitado";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                await CargarViewBags(credito.ClienteId, credito.GaranteId);
                return View("Edit_tw", credito);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar crédito para editar: {Id}", id);
                TempData["Error"] = "Error al cargar el crédito";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Credito/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreditoViewModel viewModel, string? returnUrl = null)
        {
            if (id != viewModel.Id)
                return RedirectToAction(nameof(Index));

            try
            {
                if (!ModelState.IsValid)
                {
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                    await CargarViewBags(viewModel.ClienteId, viewModel.GaranteId);
                    return View("Edit_tw", viewModel);
                }

                var resultado = await _creditoService.UpdateAsync(viewModel);
                if (resultado)
                {
                    TempData["Success"] = "Crédito actualizado exitosamente";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                TempData["Error"] = "No se pudo actualizar el crédito";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar crédito: {Id}", id);
                ModelState.AddModelError("", "Error al actualizar el crédito: " + ex.Message);
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                await CargarViewBags(viewModel.ClienteId, viewModel.GaranteId);
                return View("Edit_tw", viewModel);
            }
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

                var estaVencida = cuotaSeleccionada.FechaVencimiento.Date < DateTime.Today;
                var diasAtraso = estaVencida ? (DateTime.Today - cuotaSeleccionada.FechaVencimiento.Date).Days : 0;

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
                    FechaPago = DateTime.UtcNow
                };
                CargarCuotasPago(modelo, cuotasDisponibles);

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
                    CargarCuotasPago(modelo, cuotasPendientes);

                    return View("PagarCuota_tw", modelo);
                }

                var resultado = await _creditoService.PagarCuotaAsync(modelo);

                if (resultado)
                {
                    TempData["Success"] = "Pago registrado exitosamente";
                    return RedirectToReturnUrlOrDetails(returnUrl, modelo.CreditoId);
                }

                ModelState.AddModelError(string.Empty, "No se pudo registrar el pago");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al pagar cuota");
                ModelState.AddModelError("", "Error al registrar el pago: " + ex.Message);
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var credito = await _creditoService.GetByIdAsync(modelo.CreditoId);
                var cuotasPendientes = _creditoUiQueryService.ObtenerCuotasPendientes(credito?.Cuotas);
                CargarCuotasPago(modelo, cuotasPendientes);
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

                var modelo = new PagarCuotaViewModel
                {
                    CreditoId = credito.Id,
                    CuotaId = ultimaCuota.Id,
                    NumeroCuota = ultimaCuota.NumeroCuota,
                    MontoCuota = ultimaCuota.MontoTotal,
                    MontoPunitorio = 0, // No hay punitorio en adelanto
                    TotalAPagar = ultimaCuota.MontoTotal,
                    MontoPagado = ultimaCuota.MontoTotal,
                    ClienteNombre = credito.ClienteNombre,
                    NumeroCreditoTexto = credito.Numero,
                    FechaVencimiento = ultimaCuota.FechaVencimiento,
                    EstaVencida = false,
                    DiasAtraso = 0,
                    FechaPago = DateTime.UtcNow
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
                    return View("AdelantarCuota_tw", modelo);
                }

                var resultado = await _creditoService.AdelantarCuotaAsync(modelo);

                if (resultado)
                {
                    TempData["Success"] = $"Cuota #{modelo.NumeroCuota} adelantada exitosamente. Se ha reducido el plazo del crédito.";
                    return RedirectToReturnUrlOrDetails(returnUrl, modelo.CreditoId);
                }

                ModelState.AddModelError(string.Empty, "No se pudo registrar el adelanto");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al adelantar cuota");
                ModelState.AddModelError("", "Error al registrar el adelanto: " + ex.Message);
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
