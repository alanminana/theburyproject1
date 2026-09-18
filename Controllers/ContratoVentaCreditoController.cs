using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Exceptions;
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

        [HttpGet]
        public async Task<IActionResult> Preparar(int ventaId)
        {
            var contratoGenerado = await _contratoService.ExisteContratoGeneradoAsync(ventaId);
            ViewBag.VentaId = ventaId;
            ViewBag.ContratoGenerado = contratoGenerado;

            // PROBLEMA 1 (Localidad): antes esto se descubría recién al pulsar "Generar e
            // imprimir contrato" (InvalidOperationException 500, sin ningún aviso previo).
            // ValidarDatosParaGenerarAsync ya agrega TODOS los faltantes de una sola pasada
            // (Cliente/Crédito/Garante/Plantilla — no sólo Localidad) sin persistir nada.
            if (!contratoGenerado)
            {
                var validacion = await _contratoService.ValidarDatosParaGenerarAsync(ventaId);
                ViewBag.DatosFaltantes = validacion.EsValido ? new List<string>() : validacion.Errores;
                ViewBag.ClienteId = validacion.ClienteId;
            }
            else
            {
                ViewBag.DatosFaltantes = new List<string>();
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generar(int ventaId)
        {
            var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";

            try
            {
                await _contratoService.GenerarPdfAsync(ventaId, _currentUser.GetUsername());

                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        ventaId,
                        verUrl = Url.Action(nameof(Ver), new { ventaId }),
                        confirmarUrl = Url.Action("Confirmar", "Venta", new { id = ventaId })
                    });
                }

                return RedirectToAction(nameof(Preparar), new { ventaId });
            }
            catch (ContratoVentaCreditoValidacionException ex)
            {
                // §12 del pedido: una validación de negocio conocida (cliente sin localidad,
                // crédito sin plan, plantilla incompleta, etc. — ver
                // ContratoVentaCreditoService.CargarDatosValidadosAsync) no es un error
                // inesperado del sistema. El preflight ya evita que el usuario llegue hasta
                // acá con datos faltantes (Preparar GET y ConfigurarVenta GET la corren
                // antes), así que si esto se dispara es porque algo cambió entre el aviso y
                // el click (u otro caller que no pasó por el preflight) — se registra como
                // advertencia, no como error, y el mensaje sigue siendo el mismo para el usuario.
                // Se atrapa el tipo específico (no InvalidOperationException genérico) para no
                // esconder como Warning otras InvalidOperationException reales del mismo camino
                // (ruta de archivo inválida, snapshot corrupto, contrato no encontrado tras
                // generarlo) — esas siguen cayendo al catch (Exception) de abajo como Error.
                _logger.LogWarning(ex, "Validación de negocio al generar contrato para venta {VentaId}", ventaId);

                if (isAjax)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo generar el contrato: " + ex.Message
                    });
                }

                TempData["Error"] = "No se pudo generar el contrato: " + ex.Message;
                return RedirectToAction(nameof(Preparar), new { ventaId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar contrato para venta {VentaId}", ventaId);

                if (isAjax)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Error al generar el contrato: " + ex.Message
                    });
                }

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
