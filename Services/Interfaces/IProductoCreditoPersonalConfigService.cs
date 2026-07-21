using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Administra la configuración de Crédito Personal específica de un producto:
    /// permitido/bloqueado, máximo de cuotas y planes de cuotas propios (cantidad + tasa).
    /// </summary>
    public interface IProductoCreditoPersonalConfigService
    {
        /// <summary>
        /// Carga la configuración de crédito personal del producto, incluyendo filas
        /// plantilla (candidatas no activas) para edición en la UI.
        /// </summary>
        Task<ProductoCreditoPersonalConfigViewModel> ObtenerAsync(int productoId);

        /// <summary>
        /// Persiste la configuración: upsert de ProductoCreditoRestriccion y
        /// alta/actualización de los planes de cuotas propios del producto.
        /// </summary>
        Task GuardarAsync(int productoId, ProductoCreditoPersonalConfigViewModel config, string usuario);
    }
}
