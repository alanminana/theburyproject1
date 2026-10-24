using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IClienteService
    {
        Task<IEnumerable<Cliente>> GetAllAsync();
        Task<Cliente?> GetByIdAsync(int id);
        Task<Cliente> CreateAsync(Cliente cliente);
        /// <summary>Actualiza los datos del cliente. NO toca la configuración personalizada de crédito.</summary>
        Task<Cliente> UpdateAsync(Cliente cliente);

        /// <summary>
        /// Igual que <see cref="UpdateAsync(Cliente)"/> y, solo si <paramref name="actualizarConfiguracionCredito"/>, también
        /// guarda la configuración personalizada de crédito (perfil, tasa, gastos, cuotas máximas, montos). El llamador lo
        /// pide únicamente para usuarios con clientes.configurecredit: sin él, esas columnas ni siquiera entran en el UPDATE,
        /// así que no pueden revertir un cambio autorizado concurrente. La implementación por defecto delega en el
        /// método sin configuración (los dobles de prueba que no la necesitan no cambian).
        /// </summary>
        Task<Cliente> UpdateAsync(Cliente cliente, bool actualizarConfiguracionCredito) => UpdateAsync(cliente);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<Cliente>> SearchAsync(
            string? searchTerm = null,
            string? tipoDocumento = null,
            bool? soloActivos = null,
            bool? conCreditosActivos = null,
            decimal? puntajeMinimo = null,
            string? orderBy = null,
            string? orderDirection = null,
            string? nivelRiesgo = null);

        /// <summary>
        /// Igual que <see cref="SearchAsync"/> pero pagina a nivel de base de datos
        /// (Skip/Take + Count reales, no en memoria) — pensado para listados con
        /// potencialmente miles de clientes (Cliente/Index). Clampea automáticamente
        /// una página fuera de rango al último valor válido y devuelve la página
        /// efectivamente usada.
        /// </summary>
        Task<(List<Cliente> Items, int Total, int PageNumber)> SearchPagedAsync(
            string? searchTerm = null,
            string? tipoDocumento = null,
            bool? soloActivos = null,
            bool? conCreditosActivos = null,
            decimal? puntajeMinimo = null,
            string? nivelRiesgo = null,
            string? orderBy = null,
            string? orderDirection = null,
            int page = 1,
            int pageSize = 25);
        Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null);
        Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento);
        Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo);
        Task<bool> AsignarNivelCreditoManualAsync(
            int clienteId,
            int nivel,
            string motivo,
            string usuario);
        Task<bool> LimpiarNivelCreditoManualAsync(int clienteId, string motivo, string usuario);
        Task<List<ClientePuntajeHistorialItemViewModel>> GetHistorialPuntajeAsync(int clienteId, int top = 5);
    }
}
