using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuraciones", Accion = "view")]
    public class ConfiguracionContratoCreditoController : Controller
    {
        private readonly IPlantillaContratoCreditoService _plantillaService;
        private readonly ILogger<ConfiguracionContratoCreditoController> _logger;

        public ConfiguracionContratoCreditoController(
            IPlantillaContratoCreditoService plantillaService,
            ILogger<ConfiguracionContratoCreditoController> logger)
        {
            _plantillaService = plantillaService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            var model = await _plantillaService.ObtenerParaEdicionAsync();
            return View("Index_tw", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuraciones", Accion = "update")]
        public async Task<IActionResult> Index(PlantillaContratoCreditoViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            if (model.VigenteHasta.HasValue && model.VigenteHasta.Value.Date < model.VigenteDesde.Date)
                ModelState.AddModelError(nameof(model.VigenteHasta), "La fecha de fin no puede ser anterior al inicio.");

            if (!ModelState.IsValid)
                return View("Index_tw", model);

            try
            {
                var guardada = await _plantillaService.GuardarAsync(model);
                TempData["Success"] = "Plantilla de contrato guardada correctamente.";

                var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
                if (!string.IsNullOrWhiteSpace(safeReturnUrl))
                    return Redirect(safeReturnUrl);

                return RedirectToAction(nameof(Index), new { id = guardada.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar plantilla de contrato de crédito");
                ModelState.AddModelError("", "Error al guardar la plantilla: " + ex.Message);
                return View("Index_tw", model);
            }
        }
    }
}
