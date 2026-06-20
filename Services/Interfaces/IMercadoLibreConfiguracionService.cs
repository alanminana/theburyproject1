using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Configuración operativa del canal Mercado Libre (fila única).
    /// Los demás servicios del módulo leen la configuración desde acá.
    /// </summary>
    public interface IMercadoLibreConfiguracionService
    {
        /// <summary>
        /// Devuelve la configuración vigente (la crea con defaults seguros si no existe).
        /// Snapshot sin tracking, para consumo de servicios.
        /// </summary>
        Task<MercadoLibreConfiguracion> GetAsync(CancellationToken ct = default);

        /// <summary>
        /// Arma el ViewModel de la pantalla de configuración con sus lookups
        /// (cuentas, listas de precios, sucursales).
        /// </summary>
        Task<MercadoLibreConfiguracionViewModel> GetViewModelAsync(CancellationToken ct = default);

        /// <summary>
        /// Persiste los cambios de configuración. Valida referencias (cuenta,
        /// lista, sucursal, cliente) antes de guardar.
        /// </summary>
        Task GuardarAsync(MercadoLibreConfiguracionViewModel viewModel, string usuario, CancellationToken ct = default);
    }
}
