using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class ConfiguracionPagoController : Controller
    {
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IConfiguracionPagoGlobalAdminService _configuracionPagoGlobalAdminService;
        private readonly IClienteAptitudService _aptitudService;
        private readonly ILogger<ConfiguracionPagoController> _logger;

        public ConfiguracionPagoController(
            IConfiguracionPagoService configuracionPagoService,
            IConfiguracionPagoGlobalAdminService configuracionPagoGlobalAdminService,
            IClienteAptitudService aptitudService,
            ILogger<ConfiguracionPagoController> logger)
        {
            _configuracionPagoService = configuracionPagoService;
            _configuracionPagoGlobalAdminService = configuracionPagoGlobalAdminService;
            _aptitudService = aptitudService;
            _logger = logger;
        }

        #region CRUD — Index / Detalle / Crear / Editar / Eliminar

        // GET: ConfiguracionPago
        public async Task<IActionResult> Index()
        {
            try
            {
                var configuraciones = await _configuracionPagoService.GetAllAsync();
                return View("Index_tw", configuraciones);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuraciones de pago");
                TempData["Error"] = "Error al cargar las configuraciones de pago";
                return View("Index_tw", new List<ConfiguracionPagoViewModel>());
            }
        }

        // GET: ConfiguracionPago/MediosPago
        public async Task<IActionResult> MediosPago()
        {
            try
            {
                var modelo = await _configuracionPagoGlobalAdminService.ObtenerAdminGlobalAsync();
                return View("MediosPago_tw", modelo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuracion global de pagos");
                TempData["Error"] = "Error al cargar la configuracion global de pagos";
                return View("MediosPago_tw", new ConfiguracionPagoGlobalAdminViewModel());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ListarTarjetasGlobales(int? configuracionPagoId = null)
        {
            try
            {
                var tarjetas = await _configuracionPagoGlobalAdminService.ListarTarjetasGlobalesAsync(configuracionPagoId);
                return Json(new { success = true, data = tarjetas });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar tarjetas globales");
                return Json(new { success = false, message = "Error al listar tarjetas globales" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerTarjetaGlobal(int id)
        {
            try
            {
                var tarjeta = await _configuracionPagoGlobalAdminService.ObtenerTarjetaGlobalAsync(id);
                if (tarjeta == null)
                    return NotFound(new { success = false, message = "Tarjeta no encontrada." });

                return Json(new { success = true, data = tarjeta });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tarjeta global {TarjetaId}", id);
                return StatusCode(500, new { success = false, message = "Error al obtener la tarjeta global" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CrearTarjetaGlobal(TarjetaGlobalCommandViewModel tarjeta)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo crear la tarjeta.");
                return RedirectToAction(nameof(MediosPago));
            }

            try
            {
                await _configuracionPagoGlobalAdminService.CrearTarjetaGlobalAsync(tarjeta);
                TempData["Success"] = "Tarjeta creada correctamente.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al crear tarjeta global");
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), new { medioId = tarjeta.ConfiguracionPagoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> EditarTarjetaGlobal(int id, TarjetaGlobalCommandViewModel tarjeta)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo editar la tarjeta.");
                return RedirectToAction(nameof(MediosPago));
            }

            try
            {
                var resultado = await _configuracionPagoGlobalAdminService.ActualizarTarjetaGlobalAsync(id, tarjeta);
                TempData[resultado == null ? "Error" : "Success"] = resultado == null
                    ? "Tarjeta no encontrada."
                    : "Tarjeta actualizada correctamente.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al editar tarjeta global {TarjetaId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), new { medioId = tarjeta.ConfiguracionPagoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CambiarEstadoTarjetaGlobal(int id, bool activa, int medioId = 0)
        {
            try
            {
                var actualizado = await _configuracionPagoGlobalAdminService.CambiarEstadoTarjetaGlobalAsync(id, activa);
                TempData[actualizado ? "Success" : "Error"] = actualizado
                    ? (activa ? "Tarjeta activada correctamente." : "Tarjeta quitada correctamente.")
                    : "Tarjeta no encontrada.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al cambiar estado de tarjeta global {TarjetaId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), medioId > 0 ? new { medioId } : null);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CrearPlanGlobal(PlanPagoGlobalCommandViewModel plan)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo crear el plan global.");
                return RedirectToAction(nameof(MediosPago));
            }

            try
            {
                await _configuracionPagoGlobalAdminService.CrearPlanGlobalAsync(plan);
                TempData["Success"] = "Plan global creado correctamente.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al crear plan global de pago");
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), new { medioId = plan.ConfiguracionPagoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> EditarPlanGlobal(int id, PlanPagoGlobalCommandViewModel plan)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo editar el plan global.");
                return RedirectToAction(nameof(MediosPago));
            }

            try
            {
                var resultado = await _configuracionPagoGlobalAdminService.ActualizarPlanGlobalAsync(id, plan);
                TempData[resultado == null ? "Error" : "Success"] = resultado == null
                    ? "Plan global no encontrado."
                    : "Plan global actualizado correctamente.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al editar plan global de pago {PlanId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), new { medioId = plan.ConfiguracionPagoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> EliminarTarjetaGlobal(int id, int medioId = 0)
        {
            try
            {
                var eliminado = await _configuracionPagoGlobalAdminService.EliminarTarjetaGlobalAsync(id);
                TempData[eliminado ? "Success" : "Error"] = eliminado
                    ? "Tarjeta eliminada correctamente."
                    : "Tarjeta no encontrada.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al eliminar tarjeta global {TarjetaId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), medioId > 0 ? new { medioId } : null);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CrearMedioPagoGlobal(MedioPagoGlobalCommandViewModel command)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo crear el método de pago.");
                return RedirectToAction(nameof(MediosPago));
            }

            try
            {
                var vm = new ConfiguracionPagoViewModel
                {
                    TipoPago = command.TipoPago,
                    Nombre = command.Nombre.Trim(),
                    Descripcion = string.IsNullOrWhiteSpace(command.Descripcion) ? null : command.Descripcion.Trim(),
                    Activo = command.Activo
                };
                var resultado = await _configuracionPagoService.CreateAsync(vm);
                TempData["Success"] = "Método de pago creado correctamente.";
                return RedirectToAction(nameof(MediosPago), new { medioId = resultado.Id });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al crear método de pago global");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(MediosPago));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> EditarMedioPagoGlobal(int id, MedioPagoGlobalEditViewModel command)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = ObtenerPrimerErrorModelState("No se pudo guardar el método de pago.");
                return RedirectToAction(nameof(MediosPago), new { medioId = id });
            }

            try
            {
                var actualizado = await _configuracionPagoGlobalAdminService.EditarMedioPagoAsync(id, command);
                TempData[actualizado ? "Success" : "Error"] = actualizado
                    ? "Método de pago actualizado correctamente."
                    : "Método de pago no encontrado.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al editar método de pago {MedioId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), new { medioId = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> EliminarMedioPagoGlobal(int id)
        {
            try
            {
                var eliminado = await _configuracionPagoGlobalAdminService.EliminarMedioPagoAsync(id);
                TempData[eliminado ? "Success" : "Error"] = eliminado
                    ? "Método de pago eliminado correctamente."
                    : "Método de pago no encontrado.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al eliminar método de pago {MedioId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CambiarEstadoPlanGlobal(int id, bool activo, int medioId = 0)
        {
            try
            {
                var actualizado = await _configuracionPagoGlobalAdminService.CambiarEstadoPlanGlobalAsync(id, activo);
                TempData[actualizado ? "Success" : "Error"] = actualizado
                    ? (activo ? "Cuota activada correctamente." : "Cuota quitada correctamente.")
                    : "Plan global no encontrado.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al cambiar estado del plan global de pago {PlanId}", id);
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MediosPago), medioId > 0 ? new { medioId } : null);
        }

        // GET: ConfiguracionPago/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var configuracion = await _configuracionPagoService.GetByIdAsync(id);
                if (configuracion == null)
                {
                    TempData["Error"] = "Configuración no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                return View("Details_tw", configuracion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración {Id}", id);
                TempData["Error"] = "Error al cargar la configuración";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: ConfiguracionPago/Create
        public IActionResult Create()
        {
            ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
            return View("Create_tw", new ConfiguracionPagoViewModel());
        }

        // POST: ConfiguracionPago/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ConfiguracionPagoViewModel viewModel)
        {
            try
            {
                if (viewModel.TipoPago == TipoPago.CreditoPersonal &&
                    !viewModel.TasaInteresMensualCreditoPersonal.HasValue)
                {
                    ModelState.AddModelError(
                        nameof(viewModel.TasaInteresMensualCreditoPersonal),
                        "La tasa mensual es requerida para Credito Personal.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                    return View("Create_tw", viewModel);
                }

                await _configuracionPagoService.CreateAsync(viewModel);
                TempData["Success"] = "Configuración de pago creada exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear configuración de pago");
                ModelState.AddModelError("", "Error al crear la configuración: " + ex.Message);
                ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                return View("Create_tw", viewModel);
            }
        }

        // GET: ConfiguracionPago/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var configuracion = await _configuracionPagoService.GetByIdAsync(id);
                if (configuracion == null)
                {
                    TempData["Error"] = "Configuración no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                return View("Edit_tw", configuracion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar configuración para editar: {Id}", id);
                TempData["Error"] = "Error al cargar la configuración";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: ConfiguracionPago/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ConfiguracionPagoViewModel viewModel)
        {
            try
            {
                if (viewModel.TipoPago == TipoPago.CreditoPersonal &&
                    !viewModel.TasaInteresMensualCreditoPersonal.HasValue)
                {
                    ModelState.AddModelError(
                        nameof(viewModel.TasaInteresMensualCreditoPersonal),
                        "La tasa mensual es requerida para Credito Personal.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                    return View("Edit_tw", viewModel);
                }

                var resultado = await _configuracionPagoService.UpdateAsync(id, viewModel);
                if (resultado == null)
                {
                    TempData["Error"] = "Configuración no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = "Configuración actualizada exitosamente";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar configuración: {Id}", id);
                ModelState.AddModelError("", "Error al actualizar la configuración: " + ex.Message);
                ViewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();
                return View("Edit_tw", viewModel);
            }
        }

        // GET: ConfiguracionPago/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var configuracion = await _configuracionPagoService.GetByIdAsync(id);
                if (configuracion == null)
                {
                    TempData["Error"] = "Configuración no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                return View("Delete_tw", configuracion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar configuración para eliminar: {Id}", id);
                TempData["Error"] = "Error al cargar la configuración";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: ConfiguracionPago/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _configuracionPagoService.DeleteAsync(id);
                TempData["Success"] = "Configuración eliminada exitosamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar configuración: {Id}", id);
                TempData["Error"] = "Error al eliminar la configuración: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Tarjetas — Configurar / Calcular cuotas

        // GET: ConfiguracionPago/ConfigurarTarjeta/5
        public async Task<IActionResult> ConfigurarTarjeta(int configuracionPagoId)
        {
            try
            {
                var configuracion = await _configuracionPagoService.GetByIdAsync(configuracionPagoId);
                if (configuracion == null)
                {
                    TempData["Error"] = "Configuración no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                ViewBag.ConfiguracionPago = configuracion;
                ViewBag.TiposTarjeta = new SelectList(Enum.GetValues(typeof(TipoTarjeta)));
                ViewBag.TiposCuota = new SelectList(Enum.GetValues(typeof(TipoCuotaTarjeta)));

                var viewModel = new ConfiguracionTarjetaViewModel
                {
                    ConfiguracionPagoId = configuracionPagoId
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de configuración de tarjeta");
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: API para obtener configuración de tarjeta
        [HttpGet]
        public async Task<IActionResult> GetTarjetaConfig(int tarjetaId)
        {
            try
            {
                var tarjeta = await _configuracionPagoService.GetTarjetaByIdAsync(tarjetaId);
                if (tarjeta == null)
                    return NotFound();

                return Json(new
                {
                    id = tarjeta.Id,
                    nombreTarjeta = tarjeta.NombreTarjeta,
                    tipoTarjeta = tarjeta.TipoTarjeta,
                    permiteCuotas = tarjeta.PermiteCuotas,
                    cantidadMaximaCuotas = tarjeta.CantidadMaximaCuotas,
                    tipoCuota = tarjeta.TipoCuota,
                    tasaIntereses = tarjeta.TasaInteresesMensual,
                    tieneRecargo = tarjeta.TieneRecargoDebito,
                    porcentajeRecargo = tarjeta.PorcentajeRecargoDebito
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración de tarjeta: {TarjetaId}", tarjetaId);
                return StatusCode(500, "Error al obtener la configuración de tarjeta");
            }
        }

        // GET: API para calcular cuotas
        [HttpGet]
        public async Task<IActionResult> CalcularCuotas(int tarjetaId, decimal monto, int cuotas)
        {
            try
            {
                var tarjeta = await _configuracionPagoService.GetTarjetaByIdAsync(tarjetaId);
                if (tarjeta == null)
                    return NotFound();

                if (!tarjeta.PermiteCuotas || cuotas > tarjeta.CantidadMaximaCuotas)
                    return BadRequest("Cantidad de cuotas no válida");

                decimal montoCuota;
                decimal montoTotal;

                if (tarjeta.TipoCuota == TipoCuotaTarjeta.SinInteres)
                {
                    montoCuota = monto / cuotas;
                    montoTotal = monto;
                }
                else
                {
                    var tasaDecimal = (tarjeta.TasaInteresesMensual ?? 0) / 100;
                    var factor = (decimal)Math.Pow((double)(1 + tasaDecimal), cuotas);
                    montoCuota = monto * (tasaDecimal * factor) / (factor - 1);
                    montoTotal = montoCuota * cuotas;
                }

                return Json(new
                {
                    montoCuota = montoCuota,
                    montoTotal = montoTotal,
                    interes = montoTotal - monto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular cuotas");
                return StatusCode(500, "Error al calcular las cuotas");
            }
        }

        #endregion

        private string ObtenerPrimerErrorModelState(string fallback)
        {
            return ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? fallback;
        }

        #region Crédito personal — Perfiles y configuración

        [HttpGet]
        public async Task<IActionResult> CreditoPersonal(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            var modelo = await ConstruirCreditoPersonalConfigAsync();
            return View("CreditoPersonal_tw", modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> CreditoPersonal(
            CreditoPersonalConfigViewModel config,
            string? nuevoPerfilNombre,
            string? nuevoPerfilDescripcion,
            decimal? nuevoPerfilTasaMensual,
            decimal? nuevoPerfilGastosAdministrativos,
            int? nuevoPerfilMinCuotas,
            int? nuevoPerfilMaxCuotas,
            bool nuevoPerfilActivo = true,
            int? nuevoPerfilOrden = null,
            string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            if (config.DefaultsGlobales == null)
            {
                ModelState.AddModelError(nameof(config.DefaultsGlobales), "Debe indicar los valores globales.");
            }
            else if (config.DefaultsGlobales.MaxCuotas < config.DefaultsGlobales.MinCuotas)
            {
                ModelState.AddModelError("DefaultsGlobales.MaxCuotas", "El máximo global debe ser mayor o igual al mínimo.");
            }

            if (config.ScoringThresholds != null)
            {
                var sc = config.ScoringThresholds;

                if (sc.PuntajeMinimoParaAprobacion <= sc.PuntajeMinimoParaAnalisis)
                    ModelState.AddModelError(
                        "ScoringThresholds.PuntajeMinimoParaAprobacion",
                        "El umbral de aprobación debe ser mayor al umbral de análisis.");

                if (sc.PuntajeRiesgoMinimo >= sc.PuntajeRiesgoMedio)
                    ModelState.AddModelError(
                        "ScoringThresholds.PuntajeRiesgoMedio",
                        "El puntaje de riesgo medio debe ser mayor al mínimo.");

                if (sc.PuntajeRiesgoMedio >= sc.PuntajeRiesgoExcelente)
                    ModelState.AddModelError(
                        "ScoringThresholds.PuntajeRiesgoExcelente",
                        "El puntaje de riesgo excelente debe ser mayor al medio.");

                if (sc.UmbralCuotaIngresoBajo >= sc.RelacionCuotaIngresoMax)
                    ModelState.AddModelError(
                        "ScoringThresholds.RelacionCuotaIngresoMax",
                        "La relación cuota/ingreso máxima debe ser mayor al umbral bajo.");

                if (sc.RelacionCuotaIngresoMax >= sc.UmbralCuotaIngresoAlto)
                    ModelState.AddModelError(
                        "ScoringThresholds.UmbralCuotaIngresoAlto",
                        "El umbral alto de cuota/ingreso debe ser mayor a la relación máxima.");
            }

            if (config.SemaforoFinanciero != null)
            {
                var sf = config.SemaforoFinanciero;

                if (sf.RatioVerdeMax >= sf.RatioAmarilloMax)
                    ModelState.AddModelError(
                        "SemaforoFinanciero.RatioAmarilloMax",
                        "El ratio máximo amarillo debe ser mayor al ratio máximo verde.");
            }

            config.Perfiles = (config.Perfiles ?? new List<PerfilCreditoViewModel>())
                .Where(p => p.Id > 0 || !string.IsNullOrWhiteSpace(p.Nombre))
                .ToList();

            foreach (var perfil in config.Perfiles)
            {
                if (string.IsNullOrWhiteSpace(perfil.Nombre))
                {
                    ModelState.AddModelError(nameof(config.Perfiles), "Los perfiles existentes deben tener nombre.");
                }

                if (perfil.MaxCuotas < perfil.MinCuotas)
                {
                    ModelState.AddModelError(nameof(config.Perfiles), $"El perfil '{perfil.Nombre}' tiene máximo menor al mínimo.");
                }
            }

            if (!string.IsNullOrWhiteSpace(nuevoPerfilNombre))
            {
                var minNuevo = nuevoPerfilMinCuotas ?? 1;
                var maxNuevo = nuevoPerfilMaxCuotas ?? 24;
                if (maxNuevo < minNuevo)
                {
                    ModelState.AddModelError("nuevoPerfilMaxCuotas", "El máximo del nuevo perfil debe ser mayor o igual al mínimo.");
                }

                config.Perfiles.Add(new PerfilCreditoViewModel
                {
                    Nombre = nuevoPerfilNombre.Trim(),
                    Descripcion = nuevoPerfilDescripcion,
                    TasaMensual = nuevoPerfilTasaMensual ?? 0m,
                    GastosAdministrativos = nuevoPerfilGastosAdministrativos ?? 0m,
                    MinCuotas = minNuevo,
                    MaxCuotas = maxNuevo,
                    Activo = nuevoPerfilActivo,
                    Orden = nuevoPerfilOrden ?? 0
                });
            }

            if (!ModelState.IsValid)
            {
                return View("CreditoPersonal_tw", config);
            }

            await _configuracionPagoService.GuardarCreditoPersonalAsync(config);

            if (config.ScoringThresholds != null)
                await _aptitudService.UpdateScoringThresholdsAsync(config.ScoringThresholds);

            TempData["Success"] = "Configuración de crédito personal guardada correctamente.";

            if (config.SemaforoFinanciero != null)
                await _aptitudService.UpdateSemaforoFinancieroAsync(config.SemaforoFinanciero);

            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
                return Redirect(safeReturnUrl);

            return RedirectToAction(nameof(CreditoPersonal));
        }

        /// <summary>
        /// Obtiene todos los perfiles de crédito.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetPerfilesCredito()
        {
            try
            {
                var viewModels = await _configuracionPagoService.GetPerfilesCreditoAsync();
                return Json(new { success = true, data = viewModels });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener perfiles de crédito");
                return Json(new { success = false, message = "Error al obtener perfiles de crédito" });
            }
        }

        /// <summary>
        /// Guarda perfiles de crédito y defaults globales desde el modal.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GuardarCreditoPersonalModal(
            [FromBody] CreditoPersonalConfigViewModel config)
        {
            try
            {
                await _configuracionPagoService.GuardarCreditoPersonalAsync(config);
                return Json(new { success = true, message = "Configuración de crédito personal guardada exitosamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración de crédito personal");
                return Json(new { success = false, message = "Error al guardar: " + ex.Message });
            }
        }

        private async Task<CreditoPersonalConfigViewModel> ConstruirCreditoPersonalConfigAsync()
        {
            var configuraciones = await _configuracionPagoService.GetAllAsync();
            var creditoPersonal = configuraciones
                .FirstOrDefault(c => c.TipoPago == TipoPago.CreditoPersonal);

            var perfiles = await _configuracionPagoService.GetPerfilesCreditoAsync();
            var scoring  = await _aptitudService.GetScoringThresholdsAsync();
            var semaforo = await _aptitudService.GetSemaforoFinancieroAsync();

            return new CreditoPersonalConfigViewModel
            {
                DefaultsGlobales = new DefaultsGlobalesViewModel
                {
                    TasaMensual = creditoPersonal?.TasaInteresMensualCreditoPersonal ?? 0m,
                    GastosAdministrativos = creditoPersonal?.GastosAdministrativosDefaultCreditoPersonal ?? 0m,
                    MinCuotas = creditoPersonal?.MinCuotasDefaultCreditoPersonal ?? 1,
                    MaxCuotas = creditoPersonal?.MaxCuotasDefaultCreditoPersonal ?? 24
                },
                Perfiles = perfiles,
                ScoringThresholds = scoring,
                SemaforoFinanciero = semaforo
            };
        }

        #endregion
    }

}
