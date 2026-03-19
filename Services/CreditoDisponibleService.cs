using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class CreditoDisponibleService : ICreditoDisponibleService
    {
        private static readonly EstadoCredito[] EstadosVigentes =
        {
            EstadoCredito.Solicitado,
            EstadoCredito.Aprobado,
            EstadoCredito.Activo,
            EstadoCredito.PendienteConfiguracion,
            EstadoCredito.Configurado,
            EstadoCredito.Generado
        };

        private readonly AppDbContext _context;

        public CreditoDisponibleService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<decimal> ObtenerLimitePorPuntajeAsync(
            NivelRiesgoCredito puntaje,
            CancellationToken cancellationToken = default)
        {
            var limiteConfig = await _context.PuntajesCreditoLimite
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.Puntaje == puntaje && p.Activo,
                    cancellationToken);

            if (limiteConfig == null)
            {
                throw new CreditoDisponibleException(
                    $"No existe límite de crédito configurado para el puntaje '{puntaje}' ({(int)puntaje}).");
            }

            return limiteConfig.LimiteMonto;
        }

        public async Task<decimal> CalcularSaldoVigenteAsync(
            int clienteId,
            CancellationToken cancellationToken = default)
        {
            var saldosVigentes = await _context.Creditos
                .AsNoTracking()
                .Where(c => c.ClienteId == clienteId
                            && !c.IsDeleted
                            && c.SaldoPendiente > 0
                            && EstadosVigentes.Contains(c.Estado))
                .Select(c => c.SaldoPendiente)
                .ToListAsync(cancellationToken);

            return saldosVigentes.Sum();
        }

        public async Task<CreditoDisponibleResultado> CalcularDisponibleAsync(
            int clienteId,
            CancellationToken cancellationToken = default)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted, cancellationToken);

            if (cliente == null)
            {
                throw new CreditoDisponibleException($"Cliente no encontrado para calcular crédito disponible. Id: {clienteId}.");
            }

            var config = await _context.ClientesCreditoConfiguraciones
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteId == clienteId, cancellationToken);

            decimal limite;
            string origenLimite;

            if (config is not null)
            {
                var limiteBase = await ObtenerLimiteBaseAsync(cliente, config, cancellationToken);
                var overrideConfig = config.LimiteOverride;
                var excepcionDelta = ObtenerExcepcionDeltaVigente(config, DateTime.UtcNow);

                var limiteEfectivo = CalcularLimiteEfectivo(limiteBase, overrideConfig, excepcionDelta);
                limite = limiteEfectivo.Limite;
                origenLimite = limiteEfectivo.OrigenLimite;
            }
            else
            {
                // Compatibilidad con comportamiento histórico mientras se completa la migración funcional.
                var limitePorPuntaje = await ObtenerLimitePorPuntajeAsync(cliente.NivelRiesgo, cancellationToken);
                limite = limitePorPuntaje;
                origenLimite = "Puntaje";

                if (cliente.LimiteCredito.HasValue && cliente.LimiteCredito.Value > limite)
                {
                    limite = cliente.LimiteCredito.Value;
                    origenLimite = "Límite manual del cliente";
                }

                if (cliente.MontoMaximoPersonalizado.HasValue && cliente.MontoMaximoPersonalizado.Value > limite)
                {
                    limite = cliente.MontoMaximoPersonalizado.Value;
                    origenLimite = "Monto máximo personalizado";
                }
            }

            var saldoVigente = await CalcularSaldoVigenteAsync(clienteId, cancellationToken);
            var disponible = Math.Max(0m, limite - saldoVigente);

            return new CreditoDisponibleResultado
            {
                Limite = limite,
                OrigenLimite = origenLimite,
                SaldoVigente = saldoVigente,
                Disponible = disponible
            };
        }

        public static (decimal Limite, string OrigenLimite) CalcularLimiteEfectivo(
            decimal limiteBase,
            decimal? limiteOverride,
            decimal excepcionDeltaVigente)
        {
            if (limiteOverride.HasValue)
            {
                return (limiteOverride.Value, "Override absoluto");
            }

            var limite = limiteBase + Math.Max(0m, excepcionDeltaVigente);
            var origen = excepcionDeltaVigente > 0m
                ? "Preset + Excepción"
                : "Preset";

            return (limite, origen);
        }

        private async Task<decimal> ObtenerLimiteBaseAsync(
            Cliente cliente,
            ClienteCreditoConfiguracion config,
            CancellationToken cancellationToken)
        {
            if (config.CreditoPresetId.HasValue)
            {
                var preset = await _context.PuntajesCreditoLimite
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        p => p.Id == config.CreditoPresetId.Value && p.Activo,
                        cancellationToken);

                if (preset == null)
                {
                    throw new CreditoDisponibleException(
                        $"No existe preset activo para CreditoPresetId={config.CreditoPresetId.Value} del cliente {cliente.Id}.");
                }

                return preset.LimiteMonto;
            }

            return await ObtenerLimitePorPuntajeAsync(cliente.NivelRiesgo, cancellationToken);
        }

        private static decimal ObtenerExcepcionDeltaVigente(
            ClienteCreditoConfiguracion config,
            DateTime fechaUtc)
        {
            if (!config.ExcepcionDelta.HasValue || config.ExcepcionDelta.Value <= 0m)
            {
                return 0m;
            }

            if (config.ExcepcionDesde.HasValue && config.ExcepcionDesde.Value > fechaUtc)
            {
                return 0m;
            }

            if (config.ExcepcionHasta.HasValue && config.ExcepcionHasta.Value < fechaUtc)
            {
                return 0m;
            }

            return config.ExcepcionDelta.Value;
        }
    }
}