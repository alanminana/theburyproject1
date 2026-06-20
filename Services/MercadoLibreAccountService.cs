using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class MercadoLibreAccountService : IMercadoLibreAccountService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly ILogger<MercadoLibreAccountService> _logger;

        public MercadoLibreAccountService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreAuthService authService,
            IMercadoLibreApiClient apiClient,
            ILogger<MercadoLibreAccountService> logger)
        {
            _contextFactory = contextFactory;
            _authService = authService;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<List<MercadoLibreAccount>> GetCuentasAsync(CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            return await context.MercadoLibreAccounts
                .AsNoTracking()
                .OrderBy(a => a.Nickname)
                .ToListAsync(ct);
        }

        public async Task<MercadoLibreAccount?> GetCuentaAsync(int accountId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            return await context.MercadoLibreAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        }

        public async Task<(bool Ok, string Mensaje)> ProbarConexionAsync(int accountId, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            bool ok;
            string mensaje;

            try
            {
                var token = await _authService.GetValidAccessTokenAsync(accountId, ct);
                var usuario = await _apiClient.GetCurrentUserAsync(token, ct);

                ok = true;
                mensaje = $"Conexión OK como {usuario.Nickname} (user_id {usuario.Id}, site {usuario.SiteId}).";
            }
            catch (Exception ex)
            {
                ok = false;
                mensaje = $"Fallo de conexión: {ex.Message}";
                _logger.LogWarning(ex, "Prueba de conexión a Mercado Libre falló para cuenta {AccountId}", accountId);
            }

            sw.Stop();

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var cuenta = await context.MercadoLibreAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (cuenta is not null)
            {
                cuenta.UltimaPruebaConexionUtc = DateTime.UtcNow;
                cuenta.UltimaPruebaConexionOk = ok;
            }

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = accountId,
                Operacion = "TestConnection",
                Exito = ok,
                Detalle = mensaje,
                DuracionMs = sw.ElapsedMilliseconds
            });

            await context.SaveChangesAsync(ct);

            return (ok, mensaje);
        }

        public async Task DesconectarAsync(int accountId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var cuenta = await context.MercadoLibreAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

            if (cuenta is null)
                return;

            // Borrar material criptográfico: una cuenta desconectada no conserva tokens.
            cuenta.AccessTokenEncrypted = string.Empty;
            cuenta.RefreshTokenEncrypted = string.Empty;
            cuenta.AccessTokenExpiresAtUtc = DateTime.MinValue;
            cuenta.Activa = false;
            cuenta.IsDeleted = true;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = accountId,
                Operacion = "Disconnect",
                Exito = true,
                Detalle = $"Cuenta {cuenta.Nickname} desconectada; tokens eliminados."
            });

            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Cuenta de Mercado Libre {AccountId} desconectada", accountId);
        }
    }
}
