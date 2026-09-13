using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Punitorio;
using PunitorioEntity = TheBuryProject.Models.Entities.ConfiguracionPunitorio;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class ConfiguracionPagoController : Controller
    {
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IConfiguracionPagoGlobalAdminService _configuracionPagoGlobalAdminService;
        private readonly IClienteAptitudService _aptitudService;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly IFinancialCalculationService _financialService;
        private readonly IConfiguracionPunitorioService _configuracionPunitorioService;
        private readonly IPunitorioCalculator _punitorioCalculator;
        private readonly IRelojComercial _reloj;
        private readonly ILogger<ConfiguracionPagoController> _logger;

        public ConfiguracionPagoController(
            IConfiguracionPagoService configuracionPagoService,
            IConfiguracionPagoGlobalAdminService configuracionPagoGlobalAdminService,
            IClienteAptitudService aptitudService,
            ICreditoDisponibleService creditoDisponibleService,
            IConfiguracionPunitorioService configuracionPunitorioService,
            IPunitorioCalculator punitorioCalculator,
            IRelojComercial reloj,
            ILogger<ConfiguracionPagoController> logger,
            IFinancialCalculationService? financialService = null)
        {
            _configuracionPagoService = configuracionPagoService;
            _configuracionPagoGlobalAdminService = configuracionPagoGlobalAdminService;
            _aptitudService = aptitudService;
            _creditoDisponibleService = creditoDisponibleService;
            _configuracionPunitorioService = configuracionPunitorioService;
            _punitorioCalculator = punitorioCalculator;
            _reloj = reloj;
            _logger = logger;
            _financialService = financialService ?? new FinancialCalculationService();
        }

        #region CRUD — Index / Detalle / Crear / Editar / Eliminar

        // GET: ConfiguracionPago
        // La vista lista "Index_tw" fue retirada en el rework; el landing canónico de este
        // controller es MediosPago (así lo enlaza el menú y así redirigen todas las demás acciones).
        // Redirigir evita el HTTP 500 por vista inexistente al entrar a /ConfiguracionPago.
        public IActionResult Index()
        {
            return RedirectToAction(nameof(MediosPago));
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
            int? nuevaCuotaCantidad = null,
            decimal? nuevaCuotaTasaMensual = null,
            bool nuevaCuotaActivo = true,
            int? nuevaCuotaOrden = null,
            string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            config.ScoringThresholds = null;
            config.MontosPorPuntaje = new List<MontoPorPuntajeCreditoViewModel>();

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

            config.CuotasCreditoPersonal = (config.CuotasCreditoPersonal ?? new List<CuotaCreditoPersonalViewModel>())
                .Where(c => c.Id > 0 || c.CantidadCuotas > 0)
                .ToList();

            if (nuevaCuotaCantidad.HasValue && nuevaCuotaCantidad.Value > 0)
            {
                config.CuotasCreditoPersonal.Add(new CuotaCreditoPersonalViewModel
                {
                    CantidadCuotas = nuevaCuotaCantidad.Value,
                    TasaMensual = nuevaCuotaTasaMensual, // vacio = null: hereda el recargo global legacy al resolverse contra una venta
                    Activo = nuevaCuotaActivo,
                    Orden = nuevaCuotaOrden ?? nuevaCuotaCantidad.Value
                });
            }

            if (config.CuotasCreditoPersonal.Count > 0)
            {
                var cuotasEnviadas = config.CuotasCreditoPersonal.Select(c => c.CantidadCuotas).ToList();
                var duplicadasCuotas = cuotasEnviadas.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (duplicadasCuotas.Any())
                    ModelState.AddModelError(nameof(config.CuotasCreditoPersonal), $"Cantidades de cuotas duplicadas: {string.Join(", ", duplicadasCuotas)}.");

                var fueraDeRangoCuotas = cuotasEnviadas.Where(c => c < 1 || c > 120).ToList();
                if (fueraDeRangoCuotas.Any())
                    ModelState.AddModelError(nameof(config.CuotasCreditoPersonal), $"Cantidades de cuotas fuera de rango 1–120: {string.Join(", ", fueraDeRangoCuotas)}.");

                if (config.CuotasCreditoPersonal.Any(c => c.TasaMensual < 0))
                    ModelState.AddModelError(nameof(config.CuotasCreditoPersonal), "Las tasas mensuales por cuota no pueden ser negativas.");

                // Un plan activo puede guardarse sin recargo explicito: hereda el recargo global
                // legacy al resolverse contra una venta (ver ConfiguracionPagoService.
                // ResolverPlanesCreditoPersonalAsync). No hay gate temprano que lo rechace acá.

                // CSR-ML5 — gate temprano (antes de guardar nada): misma regla pura que valida la
                // persistencia (ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar, ya usada
                // por GuardarCuotasSinRecargoCreditoPersonalAsync), evaluada contra el estado que
                // ESTA request está por guardar (CantidadCuotas/TasaMensual posteados, no los ya
                // persistidos). Un payload manipulado (numero fuera de [1, CantidadCuotas],
                // duplicado, o la totalidad marcada con un recargo > 0) nunca llega a tocar la
                // base de datos: ni este plan ni el resto de la configuracion de esta misma
                // request se guardan.
                // Un plan sin TasaMensual propia va a heredar el recargo global legacy al resolverse
                // (ver ResolverPlanesCreditoPersonalAsync): la regla de "no marcar todas sin recargo
                // con un recargo > 0" debe validarse contra ese porcentaje EFECTIVO, no contra el
                // valor crudo (posiblemente null) que se está por guardar.
                var legacyTasaGlobalPreview = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
                foreach (var cuota in config.CuotasCreditoPersonal)
                {
                    var tasaEfectiva = cuota.TasaMensual ?? legacyTasaGlobalPreview;
                    var erroresSinRecargo = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
                        cuota.CantidadCuotas, tasaEfectiva, cuota.CuotasSinRecargo);
                    foreach (var err in erroresSinRecargo)
                        ModelState.AddModelError(
                            nameof(config.CuotasCreditoPersonal),
                            $"Cuotas sin recargo del plan de {cuota.CantidadCuotas} cuotas: {err}");
                }
            }

            ValidarLimitesPorPuntaje(config);

            // Validar tabla montos por puntaje
            if (config.MontosPorPuntaje != null && config.MontosPorPuntaje.Count > 0)
            {
                var puntajesEnviados = config.MontosPorPuntaje.Select(m => m.Puntaje).ToList();
                var duplicados = puntajesEnviados.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (duplicados.Any())
                    ModelState.AddModelError(nameof(config.MontosPorPuntaje), $"Puntajes duplicados: {string.Join(", ", duplicados)}.");

                var fueraRango = puntajesEnviados.Where(p => p < 0 || p > 10).ToList();
                if (fueraRango.Any())
                    ModelState.AddModelError(nameof(config.MontosPorPuntaje), $"Puntajes fuera de rango 0–10: {string.Join(", ", fueraRango)}.");

                if (config.MontosPorPuntaje.Any(m => m.MontoMaximoFinanciable < 0))
                    ModelState.AddModelError(nameof(config.MontosPorPuntaje), "Los montos no pueden ser negativos.");
            }

            if (!ModelState.IsValid)
            {
                await PrepararCreditoPersonalConfigParaVistaAsync(config);
                return View("CreditoPersonal_tw", config);
            }

            await _configuracionPagoService.GuardarCreditoPersonalAsync(config);

            if (config.ScoringThresholds != null)
                await _aptitudService.UpdateScoringThresholdsAsync(config.ScoringThresholds);

            if (config.MontosPorPuntaje != null && config.MontosPorPuntaje.Count > 0)
            {
                var usuario = User.Identity?.Name ?? "sistema";
                var (ok, erroresMontos) = await _configuracionPagoService.GuardarMontosPorPuntajeAsync(config.MontosPorPuntaje, usuario);
                if (!ok)
                {
                    foreach (var err in erroresMontos)
                        ModelState.AddModelError(nameof(config.MontosPorPuntaje), err);
                    await PrepararCreditoPersonalConfigParaVistaAsync(config);
                    return View("CreditoPersonal_tw", config);
                }
            }

            {
                var usuario = User.Identity?.Name ?? "sistema";
                var (ok, erroresCuotas) = await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(config.CuotasCreditoPersonal, usuario);
                if (!ok)
                {
                    foreach (var err in erroresCuotas)
                        ModelState.AddModelError(nameof(config.CuotasCreditoPersonal), err);
                    await PrepararCreditoPersonalConfigParaVistaAsync(config);
                    return View("CreditoPersonal_tw", config);
                }

                // CSR-ML5: solo planes ya persistidos (Id > 0) tienen un Id valido para guardar su
                // seleccion de cuotas sin recargo. Un plan agregado en esta misma request via el
                // modal "Agregar cuota" (Id == 0 hasta este punto — GuardarCuotasCreditoPersonalAsync
                // no devuelve el Id generado) no expone esa UI: aparece recien en el proximo GET,
                // con su Id real y seleccion vacia por defecto. La regla ya se validó arriba (gate
                // temprano); esta llamada vuelve a validar server-side contra el plan recien
                // persistido — "la validación server-side manda", nunca solo el JS del checkbox.
                foreach (var cuota in config.CuotasCreditoPersonal.Where(c => c.Id > 0))
                {
                    var (okSinRecargo, erroresSinRecargo) = await _configuracionPagoService
                        .GuardarCuotasSinRecargoCreditoPersonalAsync(cuota.Id, cuota.CuotasSinRecargo, usuario);
                    if (!okSinRecargo)
                    {
                        foreach (var err in erroresSinRecargo)
                            ModelState.AddModelError(
                                nameof(config.CuotasCreditoPersonal),
                                $"Cuotas sin recargo del plan de {cuota.CantidadCuotas} cuotas: {err}");
                        await PrepararCreditoPersonalConfigParaVistaAsync(config);
                        return View("CreditoPersonal_tw", config);
                    }
                }
            }

            if (config.LimitesPorPuntaje.Count > 0)
            {
                var usuario = User.Identity?.Name ?? "sistema";
                var items = config.LimitesPorPuntaje
                    .Select(i => (i.Puntaje, i.LimiteMonto, i.Activo))
                    .ToList()
                    .AsReadOnly();

                var (ok, erroresLimites) = await _creditoDisponibleService.GuardarLimitesPorPuntajeAsync(items, usuario);
                if (!ok)
                {
                    foreach (var err in erroresLimites)
                        ModelState.AddModelError(nameof(config.LimitesPorPuntaje), err);
                    await PrepararCreditoPersonalConfigParaVistaAsync(config);
                    return View("CreditoPersonal_tw", config);
                }
            }

            if (config.SemaforoFinanciero != null)
                await _aptitudService.UpdateSemaforoFinancieroAsync(config.SemaforoFinanciero);

            TempData["Success"] = "Configuración de crédito personal guardada correctamente.";

            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
                return Redirect(safeReturnUrl);

            return RedirectToAction(nameof(CreditoPersonal));
        }

        /// <summary>
        /// Vista previa de recargo TOTAL para la sección "Config financiera base" (#s2), sobre
        /// un monto de referencia ilustrativo. Reutiliza el mismo cálculo canónico que la
        /// persistencia (<see cref="IFinancialCalculationService.SimularPlanCredito"/>): no
        /// implementa una fórmula independiente, no persiste nada y no es autoridad para
        /// guardar (el guardado real vuelve a validar el porcentaje server-side).
        /// </summary>
        [HttpGet]
        public IActionResult PreviewRecargoCreditoPersonal(int cuotas, decimal porcentajeRecargoTotal)
        {
            const decimal montoReferencia = 100_000m;

            if (cuotas < 1)
                return BadRequest(new { error = "La cantidad de cuotas debe ser al menos 1." });

            if (porcentajeRecargoTotal < 0)
                return BadRequest(new { error = "El porcentaje de recargo no puede ser negativo." });

            var resultado = _financialService.SimularPlanCredito(
                montoReferencia, 0m, cuotas, porcentajeRecargoTotal, 0m, DateTime.Today.AddMonths(1));

            return Json(new
            {
                montoReferencia,
                saldoAFinanciar = resultado.MontoFinanciado,
                recargoTotal = resultado.InteresTotal,
                totalFinanciado = resultado.TotalAPagar,
                cuotaEstimada = resultado.CuotaEstimada,
                // Vector exacto de cuotas (misma fuente que persiste el guardado real): el
                // front no debe reconstruir la ultima cuota multiplicando cuotaEstimada por
                // la cantidad, porque esa multiplicacion no cierra contra totalFinanciado
                // cuando el residuo de redondeo no es exactamente divisible entre cuotas.
                cuotas = resultado.Cuotas.Select(c => new { numero = c.NumeroCuota, total = c.Total }).ToList()
            });
        }

        #region Punitorios por mora — PUN-ML8

        /// <summary>
        /// Crea una nueva versión de <see cref="PunitorioEntity"/> (append-only, PUN-ML3). Acción y
        /// permiso distintos de <see cref="CreditoPersonal(CreditoPersonalConfigViewModel, string?, string?, decimal?, decimal?, int?, int?, bool, int?, int?, decimal?, bool, int?, string?)"/>:
        /// gestionar punitorios no implica poder editar el resto de la configuración de crédito
        /// personal. <see cref="ConfiguracionPunitorioComando.ProrrateoDiario"/>,
        /// <see cref="ConfiguracionPunitorioComando.AplicacionRetroactiva"/> y
        /// <see cref="ConfiguracionPunitorioComando.AutorizadoParaRetroactivo"/> nunca se leen del
        /// formulario: se derivan/resuelven acá, server-side.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "managepunitorio")]
        // Gestionar implica poder ver: sin "viewpunitorio" la pestaña s7 no se renderiza en el
        // re-render tras un error de validación, y el usuario perdería el mensaje de error.
        [PermisoRequerido(Modulo = "configuracion", Accion = "viewpunitorio")]
        // La vista arma los name="" de los inputs relativos al modelo de PAGINA completo
        // (Punitorios.CrearForm.*, vía asp-for="Punitorios!.CrearForm.X" en CreditoPersonal_tw),
        // no al nombre de este parámetro. Sin este prefijo explícito, el binder nunca encuentra
        // ninguna clave coincidente y "form" queda con los valores por defecto del tipo — la
        // creación de versiones fallaba en silencio (sin fila persistida, sin error visible)
        // para toda request real del navegador, aunque los tests unitarios/HTTP que construyen
        // el ViewModel en código o postean con claves sin prefijo pasaban igual.
        public async Task<IActionResult> CrearVersionPunitorio(
            [Bind(Prefix = "Punitorios.CrearForm")] CrearConfiguracionPunitorioViewModel form,
            string? returnUrl)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            var hoy = _reloj.HoyComercial;
            var esRetroactiva = form.VigenteDesde.HasValue && form.VigenteDesde.Value < hoy;
            var autorizadoRetroactivo = User.TienePermiso("configuracion", "retroactivepunitorio");

            if (esRetroactiva)
            {
                if (string.IsNullOrWhiteSpace(form.MotivoCambio))
                    ModelState.AddModelError(nameof(form.MotivoCambio), "Una vigencia retroactiva requiere motivo.");

                if (!autorizadoRetroactivo)
                    ModelState.AddModelError(nameof(form.VigenteDesde), "La fecha elegida es retroactiva y requiere permiso administrativo específico.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var comando = new ConfiguracionPunitorioComando
                    {
                        Porcentaje = form.Porcentaje,
                        PeriodoDias = form.PeriodoDias,
                        DiasGracia = form.DiasGracia,
                        ProrrateoDiario = true,
                        AplicacionRetroactiva = esRetroactiva,
                        VigenteDesde = form.VigenteDesde!.Value,
                        Activa = form.Activa,
                        MotivoCambio = string.IsNullOrWhiteSpace(form.MotivoCambio) ? null : form.MotivoCambio.Trim(),
                        AutorizadoParaRetroactivo = esRetroactiva && autorizadoRetroactivo
                    };

                    await _configuracionPunitorioService.CrearNuevaVersionAsync(comando);

                    TempData["Success"] = "Nueva versión de punitorios creada correctamente.";

                    var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
                    var destino = string.IsNullOrWhiteSpace(safeReturnUrl)
                        ? Url.Action(nameof(CreditoPersonal))
                        : safeReturnUrl;

                    return Redirect($"{destino}#s7");
                }
                catch (ConfiguracionPunitorioRechazadaException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear una nueva versión de punitorios");
                    ModelState.AddModelError(string.Empty, "Error inesperado al crear la nueva versión. Intentá nuevamente.");
                }
            }

            var config = await ConstruirCreditoPersonalConfigAsync();
            config.Punitorios!.CrearForm = form;
            ViewData["ActiveSection"] = "s7";
            return View("CreditoPersonal_tw", config);
        }

        /// <summary>
        /// Vista previa NO autoritativa del punitorio sobre una deuda de referencia, reutilizando el
        /// mismo <see cref="IPunitorioCalculator"/> puro que usa la persistencia real (PUN-ML4/ML5).
        /// No persiste nada, no depende de <c>Credito.TasaInteres</c>, no reimplementa la fórmula.
        /// </summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "configuracion", Accion = "viewpunitorio")]
        public IActionResult PreviewPunitorio(decimal porcentaje, int periodoDias, int diasGracia)
        {
            if (periodoDias < 1)
                return BadRequest(new { error = "El período debe ser de al menos 1 día." });
            if (porcentaje < 0)
                return BadRequest(new { error = "El porcentaje no puede ser negativo." });
            if (diasGracia < 0)
                return BadRequest(new { error = "Los días de gracia no pueden ser negativos." });

            const decimal montoReferencia = 100_000m;
            var vencimiento = _reloj.HoyComercial;

            var configuracion = new ConfiguracionPunitorioEntrada
            {
                Id = 0,
                VigenteDesde = vencimiento,
                Porcentaje = porcentaje,
                PeriodoDias = periodoDias,
                DiasGracia = diasGracia,
                ProrrateoDiario = true,
                Activa = true
            };

            PunitorioPreviewPuntoViewModel Punto(string etiqueta, int dias)
            {
                var resultado = _punitorioCalculator.Calcular(new PunitorioCalculoEntrada
                {
                    MontoOriginalCuota = montoReferencia,
                    FechaVencimiento = vencimiento,
                    FechaCalculo = vencimiento.AddDays(dias),
                    Configuraciones = new[] { configuracion }
                });

                return new PunitorioPreviewPuntoViewModel
                {
                    Etiqueta = etiqueta,
                    Dias = dias,
                    Estado = resultado.EstadoResultado.ToString(),
                    Importe = resultado.PunitorioRedondeado
                };
            }

            var puntos = new List<PunitorioPreviewPuntoViewModel>
            {
                Punto($"A los {diasGracia} día(s) (dentro de gracia)", diasGracia),
                Punto("Primer día posterior a la gracia", diasGracia + 1),
                Punto($"A los {periodoDias} día(s)", periodoDias),
                Punto($"A los {periodoDias * 2} día(s)", periodoDias * 2)
            };

            return Json(new PunitorioPreviewViewModel { MontoReferencia = montoReferencia, Puntos = puntos });
        }

        private async Task<ConfiguracionPunitorioPageViewModel> ConstruirPunitorioPageAsync()
        {
            var hoy = _reloj.HoyComercial;
            var vigente = await _configuracionPunitorioService.ObtenerVigenteAsync(hoy);
            var historialEntidades = await _configuracionPunitorioService.ListarHistorialAsync();
            var vigenteId = vigente.Configuracion?.Id;

            var historial = historialEntidades
                .Select(c => ConstruirVersionViewModel(c, hoy, vigenteId))
                .ToList();

            var proxima = historial
                .Where(v => v.EsFutura)
                .OrderBy(v => v.VigenteDesde)
                .FirstOrDefault();

            var actual = new ConfiguracionPunitorioActualViewModel
            {
                TieneConfiguracion = vigente.Estado != EstadoConfiguracionPunitorio.Ausente,
                EsActiva = vigente.Estado == EstadoConfiguracionPunitorio.Activa && vigente.Configuracion!.Porcentaje > 0m,
                EsInactiva = vigente.Estado == EstadoConfiguracionPunitorio.Inactiva,
                EsCero = vigente.Estado == EstadoConfiguracionPunitorio.Activa && vigente.Configuracion?.Porcentaje == 0m,
                VigenteDesde = vigente.Configuracion?.VigenteDesde,
                Porcentaje = vigente.Configuracion?.Porcentaje,
                PeriodoDias = vigente.Configuracion?.PeriodoDias,
                DiasGracia = vigente.Configuracion?.DiasGracia,
                ProximaVersion = proxima,
                ReglaTexto = ConstruirReglaTexto(vigente)
            };

            (actual.EstadoTexto, actual.EstadoBadgeClass) = vigente.Estado switch
            {
                EstadoConfiguracionPunitorio.Inactiva => ("Inactivo", "badge-erp badge-erp-warning"),
                EstadoConfiguracionPunitorio.Activa when vigente.Configuracion!.Porcentaje == 0m => ("Activo al 0%", "badge-erp badge-erp-info"),
                EstadoConfiguracionPunitorio.Activa => ("Activo", "badge-erp badge-erp-success"),
                _ => ("Sin configuración", "badge-erp")
            };

            return new ConfiguracionPunitorioPageViewModel
            {
                Actual = actual,
                Historial = historial,
                CrearForm = new CrearConfiguracionPunitorioViewModel(),
                HoyComercial = hoy
            };
        }

        private static ConfiguracionPunitorioVersionViewModel ConstruirVersionViewModel(
            PunitorioEntity c, DateOnly hoy, int? vigenteId) => new()
        {
            Id = c.Id,
            VigenteDesde = c.VigenteDesde,
            Activa = c.Activa,
            Porcentaje = c.Porcentaje,
            PeriodoDias = c.PeriodoDias,
            DiasGracia = c.DiasGracia,
            MotivoCambio = c.MotivoCambio,
            EsRetroactiva = c.AplicacionRetroactiva,
            CreatedBy = c.CreatedBy,
            CreatedAt = c.CreatedAt,
            EsVigenteHoy = vigenteId.HasValue && c.Id == vigenteId.Value,
            EsFutura = c.VigenteDesde > hoy
        };

        private static string ConstruirReglaTexto(ConfiguracionPunitorioVigente vigente)
        {
            if (vigente.Estado == EstadoConfiguracionPunitorio.Ausente || vigente.Configuracion is null)
                return "No existe una regla de punitorios aplicable para hoy. No se pueden aplicar nuevos punitorios.";

            var c = vigente.Configuracion;

            if (vigente.Estado == EstadoConfiguracionPunitorio.Inactiva)
                return $"La configuración existe, pero el cobro de nuevos punitorios está desactivado desde {c.VigenteDesde:dd/MM/yyyy}.";

            if (c.Porcentaje == 0m)
                return "La regla está activa con una tasa de 0%. El cálculo es válido, pero no genera importe.";

            return $"Se aplica un {c.Porcentaje.ToString("0.##")}% cada {c.PeriodoDias} día(s), prorrateado diariamente. " +
                   $"Durante los primeros {c.DiasGracia} día(s) posteriores al vencimiento no se cobra. " +
                   "Al superar la gracia, el cálculo se realiza desde la fecha de vencimiento.";
        }

        #endregion

        private async Task<CreditoPersonalConfigViewModel> ConstruirCreditoPersonalConfigAsync()
        {
            var configuraciones = await _configuracionPagoService.GetAllAsync();
            var creditoPersonal = configuraciones
                .FirstOrDefault(c => c.TipoPago == TipoPago.CreditoPersonal);

            var perfiles = await _configuracionPagoService.GetPerfilesCreditoAsync();
            var semaforo = await _aptitudService.GetSemaforoFinancieroAsync();
            var limites = await ConstruirLimitesPorPuntajeAsync();
            var cuotas = await _configuracionPagoService.GetCuotasCreditoPersonalAsync();

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
                SemaforoFinanciero = semaforo,
                LimitesPorPuntaje = limites,
                CuotasCreditoPersonal = cuotas,
                Punitorios = await ConstruirPunitorioPageAsync()
            };
        }

        private async Task PrepararCreditoPersonalConfigParaVistaAsync(CreditoPersonalConfigViewModel config)
        {
            config.Perfiles ??= await _configuracionPagoService.GetPerfilesCreditoAsync();
            config.SemaforoFinanciero ??= await _aptitudService.GetSemaforoFinancieroAsync();
            config.CuotasCreditoPersonal ??= await _configuracionPagoService.GetCuotasCreditoPersonalAsync();
            config.Punitorios ??= await ConstruirPunitorioPageAsync();

            if (config.LimitesPorPuntaje.Count == 0)
                config.LimitesPorPuntaje = await ConstruirLimitesPorPuntajeAsync();
        }

        private async Task<List<ClienteCreditoLimiteItemViewModel>> ConstruirLimitesPorPuntajeAsync()
        {
            var dbItems = await _creditoDisponibleService.GetAllLimitesPorPuntajeAsync();

            return Enumerable.Range(0, 6)
                .Select(puntaje =>
                {
                    var existente = dbItems.FirstOrDefault(x => x.Puntaje == puntaje);
                    return new ClienteCreditoLimiteItemViewModel
                    {
                        Id = existente?.Id ?? 0,
                        Puntaje = puntaje,
                        LimiteMonto = existente?.LimiteMonto ?? 0m,
                        Activo = existente?.Activo ?? true,
                        FechaActualizacion = existente?.FechaActualizacion,
                        UsuarioActualizacion = existente?.UsuarioActualizacion
                    };
                })
                .ToList();
        }

        private void ValidarLimitesPorPuntaje(CreditoPersonalConfigViewModel config)
        {
            if (config.LimitesPorPuntaje.Count == 0)
                return;

            var puntajesEsperados = Enumerable.Range(0, 6).ToArray();
            var puntajes = config.LimitesPorPuntaje.Select(i => i.Puntaje).ToList();

            var duplicados = puntajes
                .GroupBy(p => p)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicados.Any())
                ModelState.AddModelError(nameof(config.LimitesPorPuntaje), $"Puntajes duplicados: {string.Join(", ", duplicados)}.");

            if (puntajes.Count != puntajesEsperados.Length || puntajesEsperados.Except(puntajes).Any())
                ModelState.AddModelError(nameof(config.LimitesPorPuntaje), "La configuracion debe contener exactamente los puntajes del 0 al 5.");

            if (config.LimitesPorPuntaje.Any(i => i.LimiteMonto < 0))
                ModelState.AddModelError(nameof(config.LimitesPorPuntaje), "Los limites no pueden ser negativos.");

            if (config.LimitesPorPuntaje.Any(i => i.LimiteMonto != decimal.Truncate(i.LimiteMonto)))
                ModelState.AddModelError(nameof(config.LimitesPorPuntaje), "Los limites por puntaje deben cargarse como numeros enteros.");
        }

        #endregion
    }

}
