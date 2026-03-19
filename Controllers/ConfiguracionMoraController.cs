using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuraciones", Accion = "managemora")]
    public class ConfiguracionMoraController : Controller
    {
        private readonly IConfiguracionMoraService _configuracionMoraService;
        private readonly ILogger<ConfiguracionMoraController> _logger;

        public ConfiguracionMoraController(
            IConfiguracionMoraService configuracionMoraService,
            ILogger<ConfiguracionMoraController> logger)
        {
            _configuracionMoraService = configuracionMoraService;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene la configuración de mora completa (configuración base + alertas)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetConfiguracion()
        {
            try
            {
                var configuracion = await _configuracionMoraService.GetConfiguracionAsync();
                return Json(new { success = true, data = configuracion });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración de mora");
                return Json(new { success = false, message = "Error al cargar la configuración: " + ex.Message });
            }
        }

        /// <summary>
        /// Guarda la configuración de mora completa (configuración base + alertas)
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveConfiguracion([FromBody] ConfiguracionMoraCompletaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return Json(new { success = false, message = "Datos inválidos", errors });
            }

            try
            {
                await _configuracionMoraService.SaveConfiguracionAsync(model);
                return Json(new { success = true, message = "Configuración guardada exitosamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración de mora");
                return Json(new { success = false, message = "Error al guardar: " + ex.Message });
            }
        }
    }
}
