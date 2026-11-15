using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Referencias informativas de clientes (D-02). Registro administrativo: no participa de la aptitud
    /// ni del límite de crédito. Toda operación sobre una referencia exige el id del cliente dueño
    /// (aislamiento: una referencia de otro cliente se trata como inexistente).
    /// </summary>
    public interface IClienteReferenciaService
    {
        Task<List<ClienteReferenciaViewModel>> ListarPorClienteAsync(int clienteId);

        Task<(bool Ok, string? Error, int? Id)> CrearAsync(int clienteId, ClienteReferenciaViewModel modelo, string usuario);

        Task<(bool Ok, string? Error)> EditarAsync(int clienteId, int referenciaId, ClienteReferenciaViewModel modelo, string usuario);

        /// <summary>Baja lógica. Exige el RowVersion leído (control de concurrencia optimista).</summary>
        Task<(bool Ok, string? Error)> DarDeBajaAsync(int clienteId, int referenciaId, string usuario, byte[]? rowVersion);

        /// <summary>
        /// Registra la verificación. Exige el RowVersion leído. La nota se agrega a las observaciones existentes
        /// (con fecha); nunca las reemplaza.
        /// </summary>
        Task<(bool Ok, string? Error)> CambiarVerificacionAsync(
            int clienteId, int referenciaId, EstadoVerificacionReferencia estado, string? observaciones, string usuario, byte[]? rowVersion);
    }
}
