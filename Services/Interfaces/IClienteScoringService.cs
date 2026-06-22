using System.Threading;
using System.Threading.Tasks;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Recalcula y persiste el scoring de comportamiento del cliente (PuntajeCliente + snapshots).
    /// </summary>
    public interface IClienteScoringService
    {
        /// <summary>
        /// Configuración de scoring vigente. Devuelve la fila persistida o una default si no existe.
        /// </summary>
        Task<ConfiguracionScoringCliente> GetConfiguracionAsync(CancellationToken ct = default);

        /// <summary>
        /// Recalcula snapshots y puntaje del cliente y los persiste.
        /// Devuelve null si el cliente no existe o está eliminado.
        /// </summary>
        Task<ClienteScoringResultado?> RecalcularAsync(int clienteId, CancellationToken ct = default);
    }
}
