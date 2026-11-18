using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.ViewComponents
{
    /// <summary>
    /// Listado de referencias del cliente (las verificadas cuentan contra el mínimo exigido para crédito personal).
    /// La vista que lo usa ya exige clientes.view.
    /// </summary>
    public class ClienteReferenciasViewComponent : ViewComponent
    {
        private readonly IClienteReferenciaService _service;

        public ClienteReferenciasViewComponent(IClienteReferenciaService service) => _service = service;

        public async Task<IViewComponentResult> InvokeAsync(int clienteId)
        {
            var items = await _service.ListarPorClienteAsync(clienteId);
            return View(items);
        }
    }
}
