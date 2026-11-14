using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// D-02: referencias informativas de un cliente (no afectan aptitud ni límite de crédito).
    /// Lectura con clientes.view; alta, edición, baja y verificación con clientes.edit.
    /// El id de la referencia siempre se resuelve dentro del cliente indicado (aislamiento entre clientes).
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "clientes", Accion = "view")]
    public class ClienteReferenciaController : Controller
    {
        private readonly IClienteReferenciaService _service;
        private readonly ICurrentUserService _currentUser;

        public ClienteReferenciaController(IClienteReferenciaService service, ICurrentUserService currentUser)
        {
            _service = service;
            _currentUser = currentUser;
        }

        /// <summary>GET /ClienteReferencia/Lista?clienteId=: parcial con el listado actual.</summary>
        [HttpGet]
        public async Task<IActionResult> Lista(int clienteId)
        {
            if (clienteId <= 0)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            var items = await _service.ListarPorClienteAsync(clienteId);
            return PartialView("_ClienteReferenciasLista", items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> Crear(int clienteId, [FromBody] ClienteReferenciaViewModel modelo)
        {
            if (clienteId <= 0 || modelo is null)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = PrimerError() });

            var (ok, error, id) = await _service.CrearAsync(clienteId, modelo, _currentUser.GetUsername());
            return ok ? Ok(new { ok = true, id }) : BadRequest(new { ok = false, error });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> Editar(int clienteId, int id, [FromBody] ClienteReferenciaViewModel modelo)
        {
            if (clienteId <= 0 || id <= 0 || modelo is null)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = PrimerError() });

            var (ok, error) = await _service.EditarAsync(clienteId, id, modelo, _currentUser.GetUsername());
            return ok ? Ok(new { ok = true }) : BadRequest(new { ok = false, error });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> Verificar(int clienteId, int id, [FromBody] ClienteReferenciaVerificacionRequest request)
        {
            if (clienteId <= 0 || id <= 0 || request is null)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = PrimerError() });

            var (ok, error) = await _service.CambiarVerificacionAsync(
                clienteId, id, request.Estado, request.Observaciones, _currentUser.GetUsername());
            return ok ? Ok(new { ok = true }) : BadRequest(new { ok = false, error });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "clientes", Accion = "edit")]
        public async Task<IActionResult> Baja(int clienteId, int id)
        {
            if (clienteId <= 0 || id <= 0)
                return BadRequest(new { ok = false, error = "Datos inválidos." });

            var (ok, error) = await _service.DarDeBajaAsync(clienteId, id, _currentUser.GetUsername());
            return ok ? Ok(new { ok = true }) : BadRequest(new { ok = false, error });
        }

        private string PrimerError() =>
            ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Datos inválidos.";
    }
}
