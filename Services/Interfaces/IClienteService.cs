using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface IClienteService
    {
        Task<IEnumerable<Cliente>> GetAllAsync();
        Task<Cliente?> GetByIdAsync(int id);
        Task<Cliente> CreateAsync(Cliente cliente);
        Task<Cliente> UpdateAsync(Cliente cliente);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<Cliente>> SearchAsync(
            string? searchTerm = null,
            string? tipoDocumento = null,
            bool? soloActivos = null,
            bool? conCreditosActivos = null,
            decimal? puntajeMinimo = null,
            string? orderBy = null,
            string? orderDirection = null);
        Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null);
        Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento);
        Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo);
    }
}