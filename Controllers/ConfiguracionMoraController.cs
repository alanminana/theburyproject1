using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "managepunitorio")]
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
    }
}
