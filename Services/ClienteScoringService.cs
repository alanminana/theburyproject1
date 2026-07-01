using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <inheritdoc cref="IClienteScoringService"/>
    public class ClienteScoringService : IClienteScoringService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ClienteScoringService> _logger;

        public ClienteScoringService(AppDbContext context, ILogger<ClienteScoringService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ConfiguracionScoringCliente> GetConfiguracionAsync(CancellationToken ct = default)
        {
            return await _context.ConfiguracionesScoringCliente
                .AsNoTracking()
                .FirstOrDefaultAsync(ct)
                ?? ConfiguracionScoringCliente.CrearDefault();
        }

        public async Task<ClienteScoringResultado?> RecalcularAsync(int clienteId, CancellationToken ct = default)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted, ct);

            if (cliente == null)
            {
                _logger.LogWarning("Scoring: cliente {ClienteId} no encontrado o eliminado.", clienteId);
                return null;
            }

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.ClienteId == clienteId && !v.IsDeleted)
                .ToListAsync(ct);

            var creditos = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cuotas)
                .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
                .ToListAsync(ct);

            var config = await _context.ConfiguracionesScoringCliente
                .AsNoTracking()
                .FirstOrDefaultAsync(ct)
                ?? ConfiguracionScoringCliente.CrearDefault();

            var ahora = DateTime.UtcNow;
            var snapshot = ClienteScoringCalculator.CalcularSnapshot(cliente.CreatedAt, ventas, creditos, ahora);
            var puntaje = ClienteScoringCalculator.CalcularPuntaje(snapshot, cliente.Sueldo, config, ahora);

            cliente.AntiguedadDias = snapshot.AntiguedadDias;
            cliente.UltimaVentaFecha = snapshot.UltimaVentaFecha;
            cliente.CantidadComprasCliente = snapshot.CantidadComprasCliente;
            cliente.CreditosEnTermino = snapshot.CreditosEnTermino;
            cliente.CreditosConAtraso = snapshot.CreditosConAtraso;
            cliente.PuntajeCliente = puntaje;

            await _context.SaveChangesAsync(ct);

            return new ClienteScoringResultado
            {
                ClienteId = clienteId,
                Puntaje = puntaje,
                Snapshot = snapshot
            };
        }
    }
}
