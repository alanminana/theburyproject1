using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "ventas", Accion = "view")]
    public class ContratoVentaCreditoController : Controller
    {
        private readonly IContratoVentaCreditoService _contratoService;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<ContratoVentaCreditoController> _logger;

        public ContratoVentaCreditoController(
            IContratoVentaCreditoService contratoService,
            ICurrentUserService currentUser,
            ILogger<ContratoVentaCreditoController> logger)
        {
            _contratoService = contratoService;
            _currentUser = currentUser;
            _logger = logger;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generar(int ventaId)
        {
            try
            {
                await _contratoService.GenerarPdfAsync(ventaId, _currentUser.GetUsername());
                return RedirectToAction(nameof(Ver), new { ventaId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar contrato para venta {VentaId}", ventaId);
                TempData["Error"] = "Error al generar el contrato: " + ex.Message;
                return RedirectToAction("Details", "Venta", new { id = ventaId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Ver(int ventaId)
        {
            var archivo = await _contratoService.ObtenerPdfAsync(ventaId);
            if (archivo == null)
            {
                TempData["Error"] = "Contrato no encontrado. Genere el contrato antes de visualizarlo.";
                return RedirectToAction("Details", "Venta", new { id = ventaId });
            }

            Response.Headers.ContentDisposition = $"inline; filename=\"{archivo.NombreArchivo}\"";
            return File(archivo.Contenido, archivo.TipoContenido, enableRangeProcessing: true);
        }
    }
}
