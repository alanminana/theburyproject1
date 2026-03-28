using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "creditos", Accion = "view")]
    public class CreditoController : Controller
    {
        private readonly ICreditoService _creditoService;
        private readonly IEvaluacionCreditoService _evaluacionService;
        private readonly IFinancialCalculationService _financialService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IConfiguracionMoraService _configuracionMoraService;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IVentaService _ventaService;
        private readonly ILogger<CreditoController> _logger;
        private readonly IClienteLookupService _clienteLookup;
        private readonly IProductoService _productoService;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly ICurrentUserService _currentUser;

        private IActionResult RedirectToReturnUrlOrDetails(string? returnUrl, int creditoId)
        {
            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Details), new { id = creditoId });
        }

        private static List<CuotaViewModel> ObtenerCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas) =>
            (cuotas ?? Enumerable.Empty<CuotaViewModel>())
                .Where(c => c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Vencida || c.Estado == EstadoCuota.Parcial)
                .OrderBy(c => c.NumeroCuota)
                .ToList();

        private static List<SelectListItem> ProyectarCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas) =>
            ObtenerCuotasPendientes(cuotas)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = $"Cuota #{c.NumeroCuota} - Vto: {c.FechaVencimiento:dd/MM/yyyy} - {c.MontoTotal:C}"
                })
                .ToList();

        public CreditoController(
            ICreditoService creditoService,
            IEvaluacionCreditoService evaluacionService,
            IFinancialCalculationService financialService,
            IConfiguracionPagoService configuracionPagoService,
            IConfiguracionMoraService configuracionMoraService,
            IDbContextFactory<AppDbContext> contextFactory,
            IVentaService ventaService,
            ILogger<CreditoController> logger,
            IClienteLookupService clienteLookup,
            IProductoService productoService,
            ICreditoDisponibleService creditoDisponibleService,
            ICurrentUserService currentUser)
        {
            _creditoService = creditoService;
            _evaluacionService = evaluacionService;
            _financialService = financialService;
            _configuracionPagoService = configuracionPagoService;
            _configuracionMoraService = configuracionMoraService;
            _contextFactory = contextFactory;
            _ventaService = ventaService;
            _logger = logger;
            _clienteLookup = clienteLookup;
            _productoService = productoService;
            _creditoDisponibleService = creditoDisponibleService;
            _currentUser = currentUser;
        }

        #region Index / Detalle / Simular

        // GET: Credito
        public async Task<IActionResult> Index(CreditoFilterViewModel filter)
        {
            try
            {
                var creditos = await _creditoService.GetAllAsync(filter);
                ViewBag.Filter = filter;
                return View("Index_tw", creditos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar créditos");
                TempData["Error"] = "Error al cargar los créditos";
                return View("Index_tw", new List<CreditoViewModel>());
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

                var creditoTask = _creditoService.GetByIdAsync(id);
                var evaluacionTask = _evaluacionService.GetEvaluacionByCreditoIdAsync(id);

                await Task.WhenAll(creditoTask, evaluacionTask);

                var credito = creditoTask.Result;
                if (credito == null)
                {
                    TempData["Error"] = "Crédito no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                var detalle = new CreditoDetalleViewModel
                {
                    Credito = credito,
                    Evaluacion = evaluacionTask.Result
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

                return View("Details_tw", detalle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener crédito {Id}", id);
                TempData["Error"] = "Error al cargar el crédito";
                return RedirectToAction(nameof(Index));
            }
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
                credito.Estado != EstadoCredito.Solicitado)
            {
                // Si ya está Configurado, Generado o más avanzado, no permitir reconfigurar
                if (credito.Estado == EstadoCredito.Configurado)
                {
                    TempData["Info"] = "El crédito ya está configurado. Puede confirmar la venta.";
                }
                else if (credito.Estado == EstadoCredito.Generado || 
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

            var tasaTask = _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
            var ventaTotalTask = ventaId.HasValue
                ? _ventaService.GetTotalVentaAsync(ventaId.Value)
                : Task.FromResult<decimal?>(null);
            var perfilesTask = _configuracionPagoService.GetPerfilesCreditoActivosAsync();

            await Task.WhenAll(tasaTask, ventaTotalTask, perfilesTask);

            var tasaMensualConfig = tasaTask.Result;
            if (ventaTotalTask.Result.HasValue)
                montoVenta = ventaTotalTask.Result.Value;
            var perfilesActivos = perfilesTask.Result;

            // Resolver parámetros de crédito del cliente (Personalizado > Perfil > Global)
            var parametrosCliente = await _configuracionPagoService
                .ObtenerParametrosCreditoClienteAsync(credito.ClienteId, tasaMensualConfig);

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
                FechaPrimeraCuota = credito.FechaPrimeraCuota
            };

            // Pasar datos del cliente a la vista para JS
            var perfilPreferido = parametrosCliente.PerfilPreferidoId.HasValue
                ? perfilesActivos.FirstOrDefault(p => p.Id == parametrosCliente.PerfilPreferidoId.Value)
                : null;

            ViewBag.ClienteConfigPersonalizada = new
            {
                TieneTasaPersonalizada = parametrosCliente.TieneTasaPersonalizada,
                TasaPersonalizada = parametrosCliente.TasaPersonalizada,
                GastosPersonalizados = parametrosCliente.GastosPersonalizados,
                CuotasMaximas = parametrosCliente.CuotasMaximas,
                CuotasMinimas = parametrosCliente.CuotasMinimas,
                TasaGlobal = tasaMensualConfig,
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
                MontoMaximo = parametrosCliente.MontoMaximo
            };

            ViewBag.PerfilesActivos = perfilesActivos
                .Select(p => new
                {
                    p.Id,
                    p.Nombre,
                    p.Descripcion,
                    p.TasaMensual,
                    p.GastosAdministrativos,
                    p.MinCuotas,
                    p.MaxCuotas
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

            if (!modelo.MetodoCalculo.HasValue)
            {
                ModelState.AddModelError(nameof(modelo.MetodoCalculo),
                    "Debe seleccionar un método de cálculo.");
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            if (modelo.MetodoCalculo == MetodoCalculoCredito.UsarCliente)
            {
                var tasaGlobal = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
                var parametros = await _configuracionPagoService.ObtenerParametrosCreditoClienteAsync(modelo.ClienteId, tasaGlobal);

                if (!parametros.TieneConfiguracionPersonalizada)
                {
                    ModelState.AddModelError(nameof(modelo.MetodoCalculo),
                        "El cliente no tiene configuración de crédito personal. " +
                        "Configure el cliente con valores personalizados o seleccione otro método.");
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                    return await RetornarVistaConPerfilesAsync(modelo);
                }
            }

            // Normalizar campos opcionales a valores por defecto
            var anticipo = modelo.Anticipo ?? 0m;
            var gastosAdministrativos = modelo.GastosAdministrativos ?? 0m;
            
            // Obtener tasa según fuente de configuración
            var tasaMensual = modelo.TasaMensual;

            if (!tasaMensual.HasValue || modelo.FuenteConfiguracion != FuenteConfiguracionCredito.Manual)
            {
                var tasaGlobal = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();

                if (modelo.FuenteConfiguracion == FuenteConfiguracionCredito.PorCliente)
                {
                    // Usar parámetros ya resueltos por el service (cadena: personalizado > perfil > global)
                    var parametros = await _configuracionPagoService.ObtenerParametrosCreditoClienteAsync(modelo.ClienteId, tasaGlobal);
                    tasaMensual = parametros.TasaMensual;
                    gastosAdministrativos = modelo.GastosAdministrativos ?? parametros.GastosAdministrativos;
                    _logger.LogInformation(
                        "Crédito {CreditoId}: Usando configuración del cliente {ClienteId} - Tasa: {Tasa}%, Gastos: ${Gastos}",
                        modelo.CreditoId, modelo.ClienteId, tasaMensual, gastosAdministrativos);
                }
                else
                {
                    tasaMensual = tasaGlobal;
                    gastosAdministrativos = modelo.GastosAdministrativos ?? 0m;
                    _logger.LogInformation(
                        "Crédito {CreditoId}: Usando configuración global - Tasa: {Tasa}%",
                        modelo.CreditoId, tasaMensual);
                }
            }
            else
            {
                // Manual: usar valores ingresados por el usuario
                if (modelo.MetodoCalculo == MetodoCalculoCredito.Manual && (!tasaMensual.HasValue || tasaMensual.Value <= 0))
                {
                    ModelState.AddModelError(nameof(modelo.TasaMensual),
                        "La tasa de interés debe ser mayor a 0% en modo Manual.");
                    ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                    return await RetornarVistaConPerfilesAsync(modelo);
                }

                _logger.LogInformation(
                    "Crédito {CreditoId}: Configuración manual - Tasa: {Tasa}%, Gastos: ${Gastos}",
                    modelo.CreditoId, tasaMensual, gastosAdministrativos);
            }

            // Validar rangos de cuotas según método activo
            await using var context = await _contextFactory.CreateDbContextAsync();

            PerfilCredito? perfilParaRango = null;
            if (modelo.PerfilCreditoSeleccionadoId.HasValue &&
                (modelo.MetodoCalculo == MetodoCalculoCredito.UsarPerfil ||
                 modelo.MetodoCalculo == MetodoCalculoCredito.AutomaticoPorCliente))
            {
                perfilParaRango = await context.PerfilesCredito
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == modelo.PerfilCreditoSeleccionadoId.Value && !p.IsDeleted);
            }

            Cliente? clienteParaRango = null;
            if (modelo.MetodoCalculo == MetodoCalculoCredito.UsarCliente)
            {
                clienteParaRango = await context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == modelo.ClienteId && !c.IsDeleted);
            }

            var (cuotasMinPermitidas, cuotasMaxPermitidas, descripcionMetodo) =
                CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(modelo.MetodoCalculo!.Value, perfilParaRango, clienteParaRango);

            if (modelo.CantidadCuotas < cuotasMinPermitidas || modelo.CantidadCuotas > cuotasMaxPermitidas)
            {
                ModelState.AddModelError(nameof(modelo.CantidadCuotas),
                    $"La cantidad de cuotas debe estar entre {cuotasMinPermitidas} y {cuotasMaxPermitidas} " +
                    $"según el método '{descripcionMetodo}'.");
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
                return await RetornarVistaConPerfilesAsync(modelo);
            }

            var comando = new ConfiguracionCreditoComando
            {
                CreditoId                  = modelo.CreditoId,
                VentaId                    = modelo.VentaId,
                Monto                      = modelo.Monto,
                Anticipo                   = anticipo,
                CantidadCuotas             = modelo.CantidadCuotas,
                TasaMensual                = tasaMensual ?? 0,
                GastosAdministrativos      = gastosAdministrativos,
                FechaPrimeraCuota          = modelo.FechaPrimeraCuota,
                MetodoCalculo              = modelo.MetodoCalculo!.Value,
                FuenteConfiguracion        = modelo.FuenteConfiguracion,
                PerfilCreditoAplicadoId    = perfilParaRango?.Id,
                PerfilCreditoAplicadoNombre = perfilParaRango?.Nombre,
                CuotasMinPermitidas        = cuotasMinPermitidas,
                CuotasMaxPermitidas        = cuotasMaxPermitidas
            };

            await _creditoService.ConfigurarCreditoAsync(comando);

            if (modelo.VentaId.HasValue)
            {
                TempData["Success"] = "Crédito configurado. Puede confirmar la venta.";
                return RedirectToAction("Details", "Venta", new { id = modelo.VentaId.Value });
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
                var anticipoVal = anticipo ?? 0m;
                var tasaVal = tasaMensual ?? await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
                var gastosVal = gastosAdministrativos ?? 0m;

                if (totalVenta <= 0)
                    return BadRequest(new { error = "El monto total de la venta debe ser mayor a cero." });
                if (anticipoVal < 0)
                    return BadRequest(new { error = "El anticipo no puede ser negativo." });
                if (cuotas <= 0)
                    return BadRequest(new { error = "Ingresá una cantidad de cuotas mayor a cero." });
                if (tasaVal < 0)
                    return BadRequest(new { error = "La tasa mensual no puede ser negativa." });
                if (gastosVal < 0)
                    return BadRequest(new { error = "Los gastos administrativos no pueden ser negativos." });

                var fecha = DateTime.TryParse(fechaPrimeraCuota, out var parsed) ? parsed : DateTime.Today.AddMonths(1);

                var plan = _financialService.SimularPlanCredito(totalVenta, anticipoVal, cuotas, tasaVal, gastosVal, fecha);

                return Json(new
                {
                    montoFinanciado       = plan.MontoFinanciado,
                    cuotaEstimada         = plan.CuotaEstimada,
                    tasaAplicada          = plan.TasaAplicada,
                    interesTotal          = plan.InteresTotal,
                    totalAPagar           = plan.TotalAPagar,
                    gastosAdministrativos = plan.GastosAdministrativos,
                    totalPlan             = plan.TotalPlan,
                    fechaPrimerPago       = plan.FechaPrimerPago.ToString("yyyy-MM-dd"),
                    semaforoEstado        = plan.SemaforoEstado,
                    semaforoMensaje       = plan.SemaforoMensaje,
                    mostrarMsgIngreso     = plan.MostrarMsgIngreso,
                    mostrarMsgAntiguedad  = plan.MostrarMsgAntiguedad
                });
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
            ViewBag.PerfilesActivos = await _configuracionPagoService.GetPerfilesCreditoActivosAsync();
            return View("ConfigurarVenta_tw", modelo);
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

                var cuotasDisponibles = ObtenerCuotasPendientes(credito.Cuotas);

                if (!cuotasDisponibles.Any())
                {
                    TempData["Warning"] = "No hay cuotas pendientes o vencidas para registrar pago.";
                    return RedirectToAction(nameof(Details), new { id, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                ViewBag.Cuotas = ProyectarCuotasPendientes(cuotasDisponibles);

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
                    TotalAPagar = cuotaSeleccionada.MontoTotal + cuotaSeleccionada.MontoPunitorio,
                    MontoPagado = cuotaSeleccionada.MontoTotal + cuotaSeleccionada.MontoPunitorio,
                    ClienteNombre = credito.ClienteNombre,
                    NumeroCreditoTexto = credito.Numero,
                    FechaVencimiento = cuotaSeleccionada.FechaVencimiento,
                    EstaVencida = estaVencida,
                    DiasAtraso = diasAtraso,
                    FechaPago = DateTime.UtcNow
                };

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

                    ViewBag.Cuotas = ProyectarCuotasPendientes(credito.Cuotas);

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
                ViewBag.Cuotas = ProyectarCuotasPendientes(credito?.Cuotas);
            }
            catch
            {
                // si falla, igual mostramos la vista con errores
            }

            return View("PagarCuota_tw", modelo);
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

        #region API JSON — Evaluar crédito

        // GET: API endpoint para evaluar crédito en tiempo real
        [HttpGet]
        public async Task<IActionResult> EvaluarCredito(int clienteId, decimal montoSolicitado, int? garanteId = null)
        {
            try
            {
                _logger.LogInformation("Evaluando crédito para cliente {ClienteId}, monto {Monto}", clienteId, montoSolicitado);

                var evaluacion = await _evaluacionService.EvaluarSolicitudAsync(clienteId, montoSolicitado, garanteId);

                return Json(evaluacion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al evaluar crédito");
                return StatusCode(500, new { error = "Error al evaluar crédito: " + ex.Message });
            }
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
            _logger.LogInformation("Cargando ViewBags...");

            // Usar servicio centralizado para clientes y garantes
            var clientes = await _clienteLookup.GetClientesSelectListAsync(clienteIdSeleccionado);
            ViewBag.Clientes = new SelectList(clientes, "Value", "Text", clienteIdSeleccionado?.ToString());

            var garantes = await _clienteLookup.GetClientesSelectListAsync(garanteIdSeleccionado);
            ViewBag.Garantes = new SelectList(garantes, "Value", "Text", garanteIdSeleccionado?.ToString());

            var productos = await _productoService.SearchAsync(soloActivos: true, orderBy: "nombre");
            ViewBag.Productos = new SelectList(
                productos
                    .Where(p => p.StockActual > 0)
                    .Select(p => new
                    {
                        p.Id,
                        Detalle = $"{p.Codigo} - {p.Nombre} (Stock: {p.StockActual}) - ${p.PrecioVenta:N2}"
                    }),
                "Id",
                "Detalle");
        }

        #endregion
    }
}
