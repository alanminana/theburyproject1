using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class ConfiguracionRentabilidadController : Controller
    {
        private readonly IConfiguracionRentabilidadService _configuracionRentabilidadService;
        private readonly ILogger<ConfiguracionRentabilidadController> _logger;

        public ConfiguracionRentabilidadController(
            IConfiguracionRentabilidadService configuracionRentabilidadService,
            ILogger<ConfiguracionRentabilidadController> logger)
        {
            _configuracionRentabilidadService = configuracionRentabilidadService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var config = await _configuracionRentabilidadService.GetConfiguracionAsync();
                return View("Index_tw", new ConfiguracionRentabilidadViewModel
                {
                    MargenBajoMax = config.MargenBajoMax,
                    MargenAltoMin = config.MargenAltoMin
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar configuracion de rentabilidad");
                ModelState.AddModelError("", "Error al cargar la configuracion: " + ex.Message);
                return View("Index_tw", new ConfiguracionRentabilidadViewModel());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Index(ConfiguracionRentabilidadViewModel model)
        {
            const string margenError = "El margen bajo debe ser menor al margen alto.";

            if (model.MargenBajoMax >= model.MargenAltoMin &&
                !ModelState.Values.SelectMany(v => v.Errors).Any(e => e.ErrorMessage == margenError))
            {
                ModelState.AddModelError(
                    nameof(model.MargenBajoMax),
                    margenError);
            }

            if (!ModelState.IsValid)
                return View("Index_tw", model);

            try
            {
                await _configuracionRentabilidadService.SaveConfiguracionAsync(
                    model.MargenBajoMax,
                    model.MargenAltoMin);

                TempData["Success"] = "Configuracion de rentabilidad guardada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuracion de rentabilidad");
                ModelState.AddModelError("", "Error al guardar la configuracion: " + ex.Message);
                return View("Index_tw", model);
            }
        }
    }
}
