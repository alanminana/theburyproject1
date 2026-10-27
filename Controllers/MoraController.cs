using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Helpers;
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
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<MoraController> _logger;

        public MoraController(
            IMoraService moraService,
            IMapper mapper,
            ICurrentUserService currentUser,
            ILogger<MoraController> logger)
        {
            _moraService = moraService;
            _mapper = mapper;
            _currentUser = currentUser;
            _logger = logger;
        }

        #region Dashboard

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        public async Task<IActionResult> Index(string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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
                    MontoTotalVencido = alertas.Where(a => a.Tipo != TipoAlertaCobranza.ProximoVencimiento).Sum(a => a.MontoVencido)
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
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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

                TempData["Warning"] = "Esta gestión todavía no está disponible en pantalla.";
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
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
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }

                var gestorId = _currentUser.GetUserId();
                await _moraService.RegistrarContactoAsync(model, gestorId);
                
                TempData["Success"] = "Contacto registrado correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar contacto");
                TempData["Error"] = "Error al registrar contacto: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
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
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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

                TempData["Warning"] = "Esta gestión todavía no está disponible en pantalla.";
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
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
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }

                if (model.FechaPromesa.Date < DateTime.Today)
                {
                    TempData["Error"] = "La fecha de la promesa no puede ser anterior a hoy.";
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }

                var gestorId = _currentUser.GetUserId();
                await _moraService.RegistrarPromesaPagoAsync(model, gestorId);
                
                TempData["Success"] = "Promesa de pago registrada correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar promesa");
                TempData["Error"] = "Error al registrar promesa: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
            }
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarPromesaCumplida(int alertaId, int clienteId, string? returnUrl = null)
        {
            try
            {
                await _moraService.MarcarPromesaCumplidaDelClienteAsync(alertaId, clienteId);
                TempData["Success"] = "Promesa marcada como cumplida";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como cumplida");
                TempData["Error"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarPromesaIncumplida(int alertaId, int clienteId, string? observaciones, string? returnUrl = null)
        {
            try
            {
                await _moraService.MarcarPromesaIncumplidaDelClienteAsync(alertaId, clienteId, observaciones);
                TempData["Success"] = "Promesa marcada como incumplida";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como incumplida");
                TempData["Error"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        #endregion

        #region Acuerdos de Pago

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpGet]
        public async Task<IActionResult> CrearAcuerdo(int alertaId, int clienteId, int creditoId, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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

                TempData["Warning"] = "Esta gestión todavía no está disponible en pantalla.";
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId });
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
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }

                // Deuda y mora salen de la ficha vigente del cliente, nunca del formulario: un importe enviado a mano
                // abarataría el acuerdo (o lo inflaría) sin respaldo en las cuotas reales.
                var ficha = await _moraService.GetFichaClienteAsync(model.ClienteId);
                var creditoEnMora = ficha?.Creditos?.FirstOrDefault(c => c.CreditoId == model.CreditoId);
                if (creditoEnMora == null)
                {
                    TempData["Error"] = "El crédito elegido no tiene deuda vencida para este cliente.";
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }
                if (await _moraService.ExisteAcuerdoVigenteAsync(model.CreditoId))
                {
                    TempData["Error"] = "Ese crédito ya tiene un acuerdo vigente. Cancelalo antes de crear otro.";
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }
                // La condonación se aplicaría sobre el punitorio, que el cobro de cuotas descuenta primero: hasta que se
                // anule por el circuito de punitorios, un acuerdo no condona (quedaría capital vencido sin cobrar).
                if (model.MontoCondonar > 0m)
                {
                    TempData["Error"] = "La condonación todavía no está disponible en los acuerdos.";
                    return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
                }
                model.MontoDeudaOriginal = await _moraService.CalcularDeudaCobrableCreditoAsync(model.CreditoId);
                // El punitorio ya viaja dentro de la deuda cobrable: sumar además la mora legacy lo contaría dos veces.
                model.MontoMoraOriginal = 0m;

                var gestorId = _currentUser.GetUserId();
                var acuerdoId = await _moraService.CrearAcuerdoPagoAsync(model, gestorId);
                
                TempData["Success"] = "Acuerdo de pago creado correctamente";
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear acuerdo");
                TempData["Error"] = "Error al crear acuerdo: " + ex.Message;
                return RedirectToAction(nameof(FichaCliente), new { id = model.ClienteId });
            }
        }

        #endregion

        // Cobrar exige gestionar mora Y poder cobrar cuotas: el dinero entra a caja por el circuito de cuotas.
        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPagoAcuerdo(int acuerdoId, int clienteId, string medioPago, string? comprobante, int? pasoEsperado = null, string? returnUrl = null)
        {
            if (!User.TienePermiso("cobranzas", "payinstallment"))
            {
                TempData["Error"] = "No tenés permiso para cobrar cuotas.";
                return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
            }

            try
            {
                var resultado = await _moraService.RegistrarPagoAcuerdoAsync(acuerdoId, clienteId, medioPago, comprobante, pasoEsperado);
                TempData["Success"] = resultado.AcuerdoCumplido
                    ? $"Pago registrado ({resultado.Concepto}: {resultado.ImportePagado:C2}). El acuerdo quedó cumplido."
                    : $"Pago registrado ({resultado.Concepto}: {resultado.ImportePagado:C2}).";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar el pago del acuerdo {AcuerdoId}", acuerdoId);
                TempData["Error"] = "No se pudo registrar el pago del acuerdo.";
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [PermisoRequerido(Modulo = "mora", Accion = "manage")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoAcuerdo(int acuerdoId, int clienteId, EstadoAcuerdo estado, string? returnUrl = null)
        {
            try
            {
                await _moraService.CambiarEstadoAcuerdoAsync(acuerdoId, clienteId, estado);
                TempData["Success"] = estado == EstadoAcuerdo.Activo ? "Acuerdo activado" : $"Acuerdo marcado como {estado}";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del acuerdo {AcuerdoId}", acuerdoId);
                TempData["Error"] = "No se pudo cambiar el estado del acuerdo.";
            }

            return RedirectToAction(nameof(FichaCliente), new { id = clienteId, returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }


        #region Configuración

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpGet]
        public IActionResult Configuracion(string? returnUrl = null)
        {
            // La vista "Configuracion" no existe; la pantalla vigente es ConfiguracionExpandida (FUN-D-001).
            return RedirectToAction(nameof(ConfiguracionExpandida), new { returnUrl = Url.GetSafeReturnUrl(returnUrl) });
        }

        [PermisoRequerido(Modulo = "mora", Accion = "config")]
        [HttpGet]
        public async Task<IActionResult> ConfiguracionExpandida(string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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
                    return RedirectToAction(nameof(Configuracion), new { returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                await _moraService.UpdateConfiguracionAsync(viewModel);
                TempData["Success"] = "Configuración actualizada correctamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración");
                TempData["Error"] = "Error al guardar configuración: " + ex.Message;
            }

            return RedirectToAction(nameof(Configuracion), new { returnUrl = Url.GetSafeReturnUrl(returnUrl) });
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
                    return RedirectToAction(nameof(ConfiguracionExpandida), new { returnUrl = Url.GetSafeReturnUrl(returnUrl) });
                }

                // El formulario publica sólo un subconjunto de la configuración: los campos
                // ausentes se preservan en lugar de sobrescribirse con null/false.
                await _moraService.UpdateConfiguracionExpandidaAsync(viewModel, Request.Form.Keys);
                TempData["Success"] = "Configuración guardada correctamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración expandida");
                TempData["Error"] = "Error al guardar configuración: " + ex.Message;
            }

            return RedirectToAction(nameof(ConfiguracionExpandida), new { returnUrl = Url.GetSafeReturnUrl(returnUrl) });
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

            return this.RedirectToReturnUrlOrIndex(returnUrl);
        }

        #endregion

        #region Alertas

        [PermisoRequerido(Modulo = "mora", Accion = "view")]
        [HttpGet]
        public async Task<IActionResult> Alertas(int? tipo = null, int? prioridad = null, string? estado = null, string? cliente = null, string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

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
            return this.RedirectToReturnUrlOrIndex(returnUrl);
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
                TempData["Error"] = "RowVersion inválida. Recargá la página e intentá nuevamente.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar alerta como leída");
                TempData["Error"] = "Error al marcar alerta como leída: " + ex.Message;
            }

            return this.RedirectToReturnUrlOrIndex(returnUrl);
        }

        [HttpGet]
        public IActionResult Resolver(int id, string? returnUrl = null)
        {
            TempData["Error"] = "Acción no disponible por GET. Use los botones de la pantalla.";
            return this.RedirectToReturnUrlOrIndex(returnUrl);
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
                TempData["Error"] = "RowVersion inválida. Recargá la página e intentá nuevamente.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver alerta");
                TempData["Error"] = "Error al resolver alerta: " + ex.Message;
            }

            return this.RedirectToReturnUrlOrIndex(returnUrl);
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