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

        /// <summary>
        /// Recalcula el puntaje del cliente y audita el cambio en ClientePuntajeHistorial
        /// solo si el puntaje efectivamente cambió respecto del valor previo.
        /// </summary>
        Task<ClienteScoringResultado?> RecalcularYAuditarAsync(
            int clienteId,
            string origen,
            string? observacion = null,
            string? registradoPor = null,
            CancellationToken ct = default);

        /// <summary>
        /// PUN-ML10-G: recalcula (y audita, salvo modo preview) el scoring de todos los clientes
        /// activos elegibles, en lotes, reutilizando <see cref="RecalcularYAuditarAsync"/> — nunca
        /// reimplementa la fórmula ni la ejecuta por SQL. Pensado para corregir scores persistidos
        /// contaminados por un cambio de regla ya desplegado (ver PUN-ML10-E). No se dispara
        /// automáticamente: requiere invocación explícita (comando/acción administrativa).
        /// </summary>
        Task<RecalculoGlobalScoringResultado> RecalcularTodosAsync(
            RecalculoGlobalScoringOpciones opciones,
            CancellationToken ct = default);
    }
}
