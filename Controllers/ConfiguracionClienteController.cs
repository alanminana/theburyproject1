using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Configuración global del aviso periódico "actualizar datos del cliente".
    /// Vive como pestaña de "Configuración de pagos" y usa los mismos permisos (configuracion:view/update).
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class ConfiguracionClienteController : Controller
    {
        private readonly IConfiguracionActualizacionDatosClienteService _servicio;
        private readonly ILogger<ConfiguracionClienteController> _logger;

        public ConfiguracionClienteController(
            IConfiguracionActualizacionDatosClienteService servicio,
            ILogger<ConfiguracionClienteController> logger)
        {
            _servicio = servicio;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var config = await _servicio.GetConfiguracionAsync();
            return View("Index_tw", new ConfiguracionActualizacionDatosClienteViewModel
            {
                Activa = config.Activa,
                DiasRevision = config.DiasRevision
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Index(ConfiguracionActualizacionDatosClienteViewModel model)
        {
            if (!ModelState.IsValid)
                return View("Index_tw", model);

            try
            {
                await _servicio.SaveConfiguracionAsync(model.Activa, model.DiasRevision);
                TempData["Success"] = "Configuración de actualización de datos guardada.";
                return RedirectToAction(nameof(Index));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                ModelState.AddModelError(nameof(model.DiasRevision), ex.Message);
                return View("Index_tw", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar la configuración de actualización de datos del cliente");
                ModelState.AddModelError("", "No se pudo guardar la configuración.");
                return View("Index_tw", model);
            }
        }
    }
}
