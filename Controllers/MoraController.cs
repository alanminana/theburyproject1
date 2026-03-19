using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "cobranzas", Accion = "viewarrears")]
    public class MoraController : Controller
    {
        private readonly IMoraService _moraService;
        private readonly IMapper _mapper;
        private readonly ILogger<MoraController> _logger;

        private string? GetSafeReturnUrl(string? returnUrl)
        {
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : null;
        }

        private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
        {
            var safeReturnUrl = GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Index));
        }

        private string GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        }

        public MoraController(
            IMoraService moraService,
            IMapper mapper,
            ILogger<MoraController> logger)
        {
            _moraService = moraService;
            _mapper = mapper;
            _logger = logger;
        }

        #region Dashboard

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        public async Task<IActionResult> Index(string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var kpis = await _moraService.GetDashboardKPIsAsync();
                var alertas = await _moraService.GetAlertasActivasAsync();
                var logs = await _moraService.GetLogsAsync(10);
                var config = await _moraService.GetConfiguracionAsync();

                ViewBag.KPIs = kpis;
                ViewBag.HistorialEjecuciones = logs;
                ViewBag.AlertasCriticas = kpis.AlertasCriticas;
                ViewBag.AlertasNoLeidas = kpis.AlertasNoLeidas;

                var viewModel = new MoraIndexViewModel
                {
                    Configuracion = _mapper.Map<ConfiguracionMoraViewModel>(config),
                    Alertas = alertas.Take(10).ToList(),
                    TotalAlertas = alertas.Count,
                    AlertasPendientes = alertas.Count(a => !a.Resuelta),
                    AlertasResueltas = alertas.Count(a => a.Resuelta),
                    MontoTotalVencido = alertas.Sum(a => a.MontoVencido)
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar mora");
                TempData["Error"] = "Error al cargar mora: " + ex.Message;
                return View(new MoraIndexViewModel());
            }
        }

        #endregion

        #region Bandeja de Clientes en Mora

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        public async Task<IActionResult> ClientesMora(
            PrioridadAlerta? prioridad = null,
            EstadoGestionCobranza? estadoGestion = null,
            int? diasMinAtraso = null,
            int? diasMaxAtraso = null,
            decimal? montoMinVencido = null,
            decimal? montoMaxVencido = null,
            bool? conPromesaActiva = null,
            bool? conAcuerdoActivo = null,
            bool? sinContactoReciente = null,
            int? diasSinContacto = null,
            string? busqueda = null,
            string? ordenamiento = "PrioridadDesc",
            int pagina = 1,
            string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var filtros = new FiltrosBandejaClientes
                {
                    Prioridad = prioridad,
                    EstadoGestion = estadoGestion,
                    DiasMinAtraso = diasMinAtraso,
                    DiasMaxAtraso = diasMaxAtraso,
                    MontoMinVencido = montoMinVencido,
                    MontoMaxVencido = montoMaxVencido,
                    ConPromesaActiva = conPromesaActiva,
                    ConAcuerdoActivo = conAcuerdoActivo,
                    SinContactoReciente = sinContactoReciente,
                    DiasSinContacto = diasSinContacto,
                    Busqueda = busqueda,
                    Ordenamiento = ordenamiento,
                    Pagina = pagina
                };

                var bandeja = await _moraService.GetClientesEnMoraAsync(filtros);
                var conteoPrioridad = await _moraService.GetConteoPorPrioridadAsync();

                ViewBag.ConteoPrioridad = conteoPrioridad;

                return View(bandeja);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar bandeja de clientes en mora");
                TempData["Error"] = "Error al cargar clientes en mora: " + ex.Message;
                return View(new BandejaClientesMoraViewModel());
            }
        }

        #endregion

        #region Ficha de Cliente

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        public async Task<IActionResult> FichaCliente(int id, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var ficha = await _moraService.GetFichaClienteAsync(id);
                if (ficha == null)
                {
                    TempData["Error"] = "Cliente no encontrado";
                    return RedirectToAction(nameof(ClientesMora));
                }

                return View(ficha);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar ficha del cliente {ClienteId}", id);
                TempData["Error"] = "Error al cargar ficha: " + ex.Message;
                return RedirectToAction(nameof(ClientesMora));
            }
        }

        #endregion

        #region Gestión de Contactos

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpGet]
        public async Task<IActionResult> RegistrarContacto(int clienteId, int? alertaId = null, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var ficha = await _moraService.GetFichaClienteAsync(clienteId);
                if (ficha == null)
                {
                    TempData["Error"] = "Cliente no encontrado";
                    return RedirectToAction(nameof(ClientesMora));
                }

                var vm = new RegistrarContactoViewModel
                {
                    ClienteId = clienteId,
                    AlertaId = alertaId,
                    NombreCliente = ficha.NombreCliente,
                    DocumentoCliente = ficha.DocumentoCliente,
                    MontoVencido = ficha.Resumen.MontoTotal,
                    DiasAtraso = ficha.Resumen.DiasMaxAtraso
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de contacto");
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarContacto(RegistrarContactoViewModel model, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos inválidos";
                    return View(model);
                }

                var gestorId = GetCurrentUserId();
                await _moraService.RegistrarContactoAsync(model, gestorId);
                
                TempData["Success"] = "Contacto registrado correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar contacto");
                TempData["Error"] = "Error al registrar contacto: " + ex.Message;
                return View(model);
            }
        }

        #endregion

        #region Promesas de Pago

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpGet]
        public async Task<IActionResult> RegistrarPromesa(int alertaId, int clienteId, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var alerta = await _moraService.GetAlertaByIdAsync(alertaId);
                if (alerta == null)
                {
                    TempData["Error"] = "Alerta no encontrada";
                    return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
                }

                var vm = new RegistrarPromesaViewModel
                {
                    AlertaId = alertaId,
                    ClienteId = clienteId,
                    NombreCliente = alerta.ClienteNombre,
                    MontoVencidoTotal = alerta.MontoVencido,
                    FechaPromesa = DateTime.Today.AddDays(7),
                    MontoPromesa = alerta.MontoVencido
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de promesa");
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPromesa(RegistrarPromesaViewModel model, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos inválidos";
                    return View(model);
                }

                var gestorId = GetCurrentUserId();
                await _moraService.RegistrarPromesaPagoAsync(model, gestorId);
                
                TempData["Success"] = "Promesa de pago registrada correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar promesa");
                TempData["Error"] = "Error al registrar promesa: " + ex.Message;
                return View(model);
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarPromesaCumplida(int alertaId, int clienteId, string? returnUrl = null)
        {
            try
            {
                await _moraService.MarcarPromesaCumplidaAsync(alertaId);
                TempData["Success"] = "Promesa marcada como cumplida";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como cumplida");
                TempData["Error"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = GetSafeReturnUrl(returnUrl) });
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarPromesaIncumplida(int alertaId, int clienteId, string? observaciones, string? returnUrl = null)
        {
            try
            {
                await _moraService.MarcarPromesaIncumplidaAsync(alertaId, observaciones);
                TempData["Success"] = "Promesa marcada como incumplida";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como incumplida");
                TempData["Error"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = GetSafeReturnUrl(returnUrl) });
        }

        #endregion

        #region Acuerdos de Pago

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpGet]
        public async Task<IActionResult> CrearAcuerdo(int alertaId, int clienteId, int creditoId, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var alerta = await _moraService.GetAlertaByIdAsync(alertaId);
                var config = await _moraService.GetConfiguracionAsync();

                if (alerta == null)
                {
                    TempData["Error"] = "Alerta no encontrada";
                    return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
                }

                var vm = new CrearAcuerdoViewModel
                {
                    AlertaId = alertaId,
                    ClienteId = clienteId,
                    CreditoId = creditoId,
                    NombreCliente = alerta.ClienteNombre,
                    MontoDeudaOriginal = alerta.MontoVencido,
                    MontoMoraOriginal = 0, // Se calculará
                    MaximoCuotasPermitido = config.MaximoCuotasAcuerdo ?? 12,
                    PorcentajeMinEntrega = config.PorcentajeMinimoEntrega ?? 10,
                    PermiteCondonacion = config.PermitirCondonacionMora,
                    MaximoCondonacionPermitido = config.PermitirCondonacionMora ? config.PorcentajeMaximoCondonacion ?? 50 : 0,
                    FechaPrimeraCuota = DateTime.Today.AddDays(30),
                    CantidadCuotas = 6
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de acuerdo");
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearAcuerdo(CrearAcuerdoViewModel model, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos inválidos";
                    return View(model);
                }

                var gestorId = GetCurrentUserId();
                var acuerdoId = await _moraService.CrearAcuerdoPagoAsync(model, gestorId);
                
                TempData["Success"] = "Acuerdo de pago creado correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = GetSafeReturnUrl(returnUrl) });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear acuerdo");
                TempData["Error"] = "Error al crear acuerdo: " + ex.Message;
                return View(model);
            }
        }

        #endregion

        #region Configuración

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpGet]
        public async Task<IActionResult> Configuracion(string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var config = await _moraService.GetConfiguracionAsync();
                var vm = _mapper.Map<ConfiguracionMoraViewModel>(config);
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar configuración de mora");
                TempData["Error"] = "Error al cargar configuración: " + ex.Message;
                return View(new ConfiguracionMoraViewModel());
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpGet]
        public async Task<IActionResult> ConfiguracionExpandida(string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var config = await _moraService.GetConfiguracionAsync();
                var vm = _mapper.Map<ConfiguracionMoraExpandidaViewModel>(config);
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar configuración expandida");
                TempData["Error"] = "Error al cargar configuración: " + ex.Message;
                return View(new ConfiguracionMoraExpandidaViewModel());
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracion(ConfiguracionMoraViewModel viewModel, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos inválidos";
                    return RedirectToAction(nameof(Configuracion), new { returnUrl = GetSafeReturnUrl(returnUrl) });
                }

                await _moraService.UpdateConfiguracionAsync(viewModel);
                TempData["Success"] = "Configuración actualizada correctamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración");
                TempData["Error"] = "Error al guardar configuración: " + ex.Message;
            }

            return RedirectToAction(nameof(Configuracion), new { returnUrl = GetSafeReturnUrl(returnUrl) });
        }

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracionExpandida(ConfiguracionMoraExpandidaViewModel viewModel, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos inválidos";
                    return RedirectToAction(nameof(ConfiguracionExpandida), new { returnUrl = GetSafeReturnUrl(returnUrl) });
                }

                await _moraService.UpdateConfiguracionExpandidaAsync(viewModel);
                TempData["Success"] = "Configuración guardada correctamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración expandida");
                TempData["Error"] = "Error al guardar configuración: " + ex.Message;
            }

            return RedirectToAction(nameof(ConfiguracionExpandida), new { returnUrl = GetSafeReturnUrl(returnUrl) });
        }

        #endregion

        #region Ejecución Manual

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EjecutarJob(string? returnUrl = null)
        {
            try
            {
                await _moraService.ProcesarMoraAsync();
                TempData["Success"] = "Proceso de mora ejecutado correctamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de mora");
                TempData["Error"] = "Error al ejecutar mora: " + ex.Message;
            }

            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        #endregion

        #region Alertas

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        [HttpGet]
        public async Task<IActionResult> Alertas(int? tipo = null, int? prioridad = null, string? estado = null, string? cliente = null, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

                var alertas = await _moraService.GetTodasAlertasAsync();

                if (tipo.HasValue)
                    alertas = alertas.Where(a => (int)a.Tipo == tipo.Value).ToList();

                if (prioridad.HasValue)
                    alertas = alertas.Where(a => (int)a.Prioridad == prioridad.Value).ToList();

                if (!string.IsNullOrWhiteSpace(estado))
                {
                    alertas = estado switch
                    {
                        "noLeidas" => alertas.Where(a => !a.Leida).ToList(),
                        "leidas" => alertas.Where(a => a.Leida).ToList(),
                        "noResueltas" => alertas.Where(a => !a.Resuelta).ToList(),
                        "resueltas" => alertas.Where(a => a.Resuelta).ToList(),
                        _ => alertas
                    };
                }

                if (!string.IsNullOrWhiteSpace(cliente))
                {
                    alertas = alertas.Where(a =>
                        (a.ClienteNombre != null && a.ClienteNombre.Contains(cliente, StringComparison.OrdinalIgnoreCase)) ||
                        (a.ClienteDocumento != null && a.ClienteDocumento.Contains(cliente, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                ViewBag.ClienteFiltro = cliente;
                return View(alertas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar alertas de mora");
                TempData["Error"] = "Error al cargar alertas: " + ex.Message;
                return View(Enumerable.Empty<AlertaCobranzaViewModel>());
            }
        }

        [HttpGet]
        public IActionResult MarcarLeida(int id, string? returnUrl = null)
        {
            TempData["Error"] = "Acción no disponible por GET. Use los botones de la pantalla.";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> MarcarLeida(int id, [FromForm] string? rowVersion, string? returnUrl = null)
        {
            return MarcarLeidaPost(id, rowVersion, returnUrl);
        }

        private async Task<IActionResult> MarcarLeidaPost(int id, string? rowVersion, string? returnUrl)
        {
            try
            {
                var bytes = string.IsNullOrWhiteSpace(rowVersion) ? null : Convert.FromBase64String(rowVersion);
                var ok = await _moraService.MarcarAlertaComoLeidaAsync(id, bytes);
                TempData[ok ? "Success" : "Error"] = ok ? "Alerta marcada como leída" : "No se pudo marcar la alerta";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (FormatException)
            {
                TempData["Error"] = "RowVersion inválida. Recargue la página e intente nuevamente.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar alerta como leída");
                TempData["Error"] = "Error al marcar alerta como leída: " + ex.Message;
            }

            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        [HttpGet]
        public IActionResult Resolver(int id, string? returnUrl = null)
        {
            TempData["Error"] = "Acción no disponible por GET. Use los botones de la pantalla.";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Resolver(int id, [FromForm] string? rowVersion, [FromForm] string? observaciones = null, string? returnUrl = null)
        {
            return ResolverPost(id, rowVersion, observaciones, returnUrl);
        }

        private async Task<IActionResult> ResolverPost(int id, string? rowVersion, string? observaciones, string? returnUrl)
        {
            try
            {
                var bytes = string.IsNullOrWhiteSpace(rowVersion) ? null : Convert.FromBase64String(rowVersion);
                var resultado = await _moraService.ResolverAlertaAsync(id, observaciones, bytes);

                TempData[resultado ? "Success" : "Error"] = resultado
                    ? "Alerta resuelta correctamente"
                    : "No se pudo resolver la alerta";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (FormatException)
            {
                TempData["Error"] = "RowVersion inválida. Recargue la página e intente nuevamente.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver alerta");
                TempData["Error"] = "Error al resolver alerta: " + ex.Message;
            }

            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> ProcesarMora(string? returnUrl = null)
        {
            return EjecutarJob(returnUrl);
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> ResolverAlerta(int id, string? observaciones, [FromForm] string? rowVersion, string? returnUrl = null)
        {
            return ResolverPost(id, rowVersion, observaciones, returnUrl);
        }

        #endregion

        #region Logs

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        public async Task<IActionResult> Logs()
        {
            try
            {
                var logs = await _moraService.GetLogsAsync(100);
                return View(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar logs");
                TempData["Error"] = "Error al cargar logs: " + ex.Message;
                return View(new List<Models.Entities.LogMora>());
            }
        }

        #endregion
    }
}