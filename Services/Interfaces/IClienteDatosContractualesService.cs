using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Lectura y actualización acotada de los datos del Cliente que exige el contrato de crédito.
    /// No toca ningún otro campo del cliente (a diferencia de <see cref="IClienteService.UpdateAsync"/>,
    /// que reemplaza el formulario completo).
    /// </summary>
    public interface IClienteDatosContractualesService
    {
        Task<ClienteDatosContractualesEstado?> ObtenerAsync(int clienteId);

        Task<ClienteDatosContractualesResultado> ActualizarAsync(ClienteDatosContractualesDto datos);
    }
}
