using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Modal "Completar datos del cliente" del flujo de venta a crédito: lee y guarda sólo los
    /// campos que exige el contrato, sin sacar al operador del wizard.
    /// </summary>
    [Authorize]
    public class ClienteDatosContractualesController : Controller
    {
        private readonly IClienteDatosContractualesService _service;
        private readonly ILogger<ClienteDatosContractualesController> _logger;

        public ClienteDatosContractualesController(
            IClienteDatosContractualesService service,
            ILogger<ClienteDatosContractualesController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "clientes", Accion = "view")]
        public async Task<IActionResult> Obtener(int clienteId)
        {
            var estado = await _service.ObtenerAsync(clienteId);
            if (estado == null)
                return NotFound(new { success = false, message = "Cliente no encontrado." });

            return Json(new { success = true, datos = estado.Datos, faltantes = estado.Faltantes });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> Guardar(ClienteDatosContractualesDto datos)
        {
            try
            {
                var resultado = await _service.ActualizarAsync(datos);
                return resultado.Exito
                    ? Json(new { success = true })
                    : Json(new { success = false, errors = resultado.Errores });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al completar datos contractuales del cliente {ClienteId}", datos.ClienteId);
                return Json(new { success = false, errors = new[] { "No se pudieron guardar los datos del cliente." } });
            }
        }
    }
}
