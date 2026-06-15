using TheBuryProject.Modules.MercadoLibre.Entities;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Administración de las cuentas de Mercado Libre conectadas al ERP.
    /// </summary>
    public interface IMercadoLibreAccountService
    {
        Task<List<MercadoLibreAccount>> GetCuentasAsync(CancellationToken ct = default);

        Task<MercadoLibreAccount?> GetCuentaAsync(int accountId, CancellationToken ct = default);

        /// <summary>
        /// GET /users/me con el token vigente. Registra el resultado en la cuenta
        /// y en MercadoLibreSyncLogs.
        /// </summary>
        Task<(bool Ok, string Mensaje)> ProbarConexionAsync(int accountId, CancellationToken ct = default);

        /// <summary>
        /// Desconecta la cuenta: borra los tokens cifrados y la desactiva (soft delete).
        /// </summary>
        Task DesconectarAsync(int accountId, CancellationToken ct = default);
    }
}
