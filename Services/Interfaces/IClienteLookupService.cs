using Microsoft.AspNetCore.Mvc.Rendering;


namespace TheBuryProject.Services.Interfaces
{
    public interface IClienteLookupService
    {
        Task<List<SelectListItem>> GetClientesSelectListAsync(int? selectedId = null, bool limitarACliente = false);
        Task<string?> GetClienteDisplayNameAsync(int clienteId);
    }
}