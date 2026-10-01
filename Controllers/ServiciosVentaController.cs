using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Administración de los precios fijos globales de Envío Ciudad/Rural y Armado N.º 1..6.
    /// Vive como pestaña de "Configuración de pagos" y usa los mismos permisos (configuracion:view/update).
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class ServiciosVentaController : Controller
    {
        private readonly IServicioVentaPrecioService _servicio;
        private readonly ILogger<ServiciosVentaController> _logger;

        public ServiciosVentaController(IServicioVentaPrecioService servicio, ILogger<ServiciosVentaController> logger)
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
        public async Task<IActionResult> Guardar(ServiciosVentaAdminViewModel modelo)
        {
            var items = (modelo.Envios ?? new()).Concat(modelo.Armados ?? new()).ToList();

            if (items.Any(i => i.Precio < 0m))
            {
                TempData["Error"] = "Los precios no pueden ser negativos.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _servicio.GuardarAsync(items.Select(i =>
                    new ServicioVentaPrecioComando(i.Tipo, i.Precio, i.Activo)));
                TempData["Success"] = "Precios de envíos y armados guardados.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "No se pudieron guardar los precios de servicios de venta");
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<ServiciosVentaAdminViewModel> ConstruirModeloAsync()
        {
            var precios = await _servicio.ListarAsync();
            var modelo = new ServiciosVentaAdminViewModel();

            foreach (var p in precios)
            {
                var item = new ServicioVentaPrecioItemViewModel
                {
                    Tipo = p.Tipo,
                    Nombre = p.Tipo.NombreVisible(),
                    Domiciliario = p.Tipo.EsDomiciliario(),
                    Precio = p.Precio,
                    Activo = p.Activo
                };

                (p.Tipo.EsEnvio() ? modelo.Envios : modelo.Armados).Add(item);
            }

            return modelo;
        }
    }
}
