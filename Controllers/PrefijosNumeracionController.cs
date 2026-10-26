using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Prefijos de los números de documento (cotización, venta, facturas, crédito, devolución, etc.).
    /// Vive como pestaña de "Configuración" y usa los mismos permisos (configuracion:view/update).
    /// Cambiar un prefijo solo afecta a los números nuevos.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class PrefijosNumeracionController : Controller
    {
        private readonly IPrefijoNumeracionService _servicio;
        private readonly ILogger<PrefijosNumeracionController> _logger;

        public PrefijosNumeracionController(IPrefijoNumeracionService servicio, ILogger<PrefijosNumeracionController> logger)
        {
            _servicio = servicio;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View("Index_tw", await ConstruirModeloAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Guardar(PrefijosNumeracionAdminViewModel modelo)
        {
            try
            {
                await _servicio.GuardarAsync((modelo.Items ?? new()).Select(i =>
                    new PrefijoNumeracionComando(i.Tipo, i.Prefijo)));
                TempData["Success"] = "Prefijos guardados. Se aplican a los números nuevos.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "No se pudieron guardar los prefijos de numeración");
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<PrefijosNumeracionAdminViewModel> ConstruirModeloAsync()
        {
            var modelo = new PrefijosNumeracionAdminViewModel();
            foreach (var p in await _servicio.ListarAsync())
            {
                modelo.Items.Add(new PrefijoNumeracionItemViewModel
                {
                    Tipo = p.Tipo,
                    Nombre = p.Tipo.NombreVisible(),
                    Prefijo = p.Prefijo,
                    PrefijoPorDefecto = p.PrefijoPorDefecto,
                    Ejemplo = p.Tipo.Ejemplo(p.Prefijo),
                    LongitudMaxima = p.Tipo.LongitudMaxima()
                });
            }

            return modelo;
        }
    }
}
