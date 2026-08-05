using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<ClienteScoringResultado?> RecalcularYAuditarAsync(
            int clienteId,
            string origen,
            string? observacion = null,
            string? registradoPor = null,
            CancellationToken ct = default)
        {
            var clienteAntes = await _context.Clientes
                .AsNoTracking()
                .Where(c => c.Id == clienteId)
                .Select(c => new { c.PuntajeCliente, c.NivelRiesgo })
                .FirstOrDefaultAsync(ct);

            if (clienteAntes == null)
                return null;

            var resultado = await RecalcularAsync(clienteId, ct);

            if (resultado == null || resultado.Puntaje == clienteAntes.PuntajeCliente)
                return resultado;

            _context.ClientesPuntajeHistorial.Add(new ClientePuntajeHistorial
            {
                ClienteId = clienteId,
                Puntaje = resultado.Puntaje,
                NivelRiesgo = clienteAntes.NivelRiesgo,
                Fecha = DateTime.UtcNow,
                Origen = origen,
                Observacion = observacion,
                RegistradoPor = registradoPor
            });

            await _context.SaveChangesAsync(ct);

            return resultado;
        }

        /// <inheritdoc />
        public async Task<RecalculoGlobalScoringResultado> RecalcularTodosAsync(
            RecalculoGlobalScoringOpciones opciones,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(opciones);

            if (ct.IsCancellationRequested)
            {
                return new RecalculoGlobalScoringResultado { Preview = opciones.Preview, Interrumpido = true };
            }

            var batchSize = Math.Max(1, opciones.BatchSize);

            var clienteIds = await ObtenerClienteIdsElegiblesAsync(ct);

            var examinados = 0;
            var recalculados = 0;
            var sinCambios = 0;
            var fallidos = 0;
            var idsFallidos = new List<int>();
            var interrumpido = false;

            foreach (var lote in Lotear(clienteIds, batchSize))
            {
                if (ct.IsCancellationRequested)
                {
                    interrumpido = true;
                    break;
                }

                foreach (var clienteId in lote)
                {
                    if (ct.IsCancellationRequested)
                    {
                        interrumpido = true;
                        break;
                    }

                    examinados++;

                    try
                    {
                        int? puntajeAntes;
                        int? puntajeDespues;

                        if (opciones.Preview)
                        {
                            puntajeAntes = await _context.Clientes
                                .AsNoTracking()
                                .Where(c => c.Id == clienteId)
                                .Select(c => (int?)c.PuntajeCliente)
                                .FirstOrDefaultAsync(ct);
                            puntajeDespues = await CalcularPuntajeSoloLecturaAsync(clienteId, ct);
                        }
                        else
                        {
                            puntajeAntes = await _context.Clientes
                                .AsNoTracking()
                                .Where(c => c.Id == clienteId)
                                .Select(c => (int?)c.PuntajeCliente)
                                .FirstOrDefaultAsync(ct);

                            var resultado = await RecalcularYAuditarAsync(
                                clienteId, opciones.Origen, opciones.Observacion, opciones.RegistradoPor, ct);
                            puntajeDespues = resultado?.Puntaje;
                        }

                        if (puntajeAntes == null || puntajeDespues == null)
                        {
                            _logger.LogWarning(
                                "Recalculo global de scoring: cliente {ClienteId} no encontrado o eliminado, se omite.",
                                clienteId);

                            fallidos++;
                            idsFallidos.Add(clienteId);
                            if (opciones.PoliticaErrores == PoliticaErroresRecalculoGlobal.DetenerEnPrimerFallo)
                            {
                                interrumpido = true;
                                break;
                            }
                            continue;
                        }

                        if (puntajeDespues.Value != puntajeAntes.Value)
                            recalculados++;
                        else
                            sinCambios++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex, "Recalculo global de scoring: fallo al procesar el cliente {ClienteId}.", clienteId);

                        fallidos++;
                        idsFallidos.Add(clienteId);
                        if (opciones.PoliticaErrores == PoliticaErroresRecalculoGlobal.DetenerEnPrimerFallo)
                        {
                            interrumpido = true;
                            break;
                        }
                    }
                }

                if (interrumpido)
                    break;
            }

            return new RecalculoGlobalScoringResultado
            {
                Examinados = examinados,
                Recalculados = recalculados,
                SinCambios = sinCambios,
                Fallidos = fallidos,
                IdsFallidos = idsFallidos,
                Preview = opciones.Preview,
                Interrumpido = interrumpido
            };
        }

        /// <summary>
        /// Clientes activos elegibles para el recálculo global, orden determinístico. Método propio
        /// (no inline en <see cref="RecalcularTodosAsync"/>) para permitir que los tests inyecten
        /// escenarios de fallo controlados (ver <c>protected internal virtual</c>) sin depender de
        /// fragilidad de base de datos real.
        /// </summary>
        protected internal virtual async Task<List<int>> ObtenerClienteIdsElegiblesAsync(CancellationToken ct)
        {
            return await _context.Clientes
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.Activo)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Calcula el puntaje que resultaría para el cliente sin persistir nada — mismo
        /// <see cref="ClienteScoringCalculator"/> que <see cref="RecalcularAsync"/>, sólo que lee
        /// todo <c>AsNoTracking</c> y nunca llama <c>SaveChangesAsync</c>. Usado exclusivamente por
        /// el modo preview de <see cref="RecalcularTodosAsync"/>. <c>null</c> si el cliente no existe
        /// o está eliminado (mismo criterio que <see cref="RecalcularAsync"/>).
        /// </summary>
        private async Task<int?> CalcularPuntajeSoloLecturaAsync(int clienteId, CancellationToken ct)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted, ct);

            if (cliente == null)
                return null;

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.ClienteId == clienteId && !v.IsDeleted)
                .ToListAsync(ct);

            var creditos = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cuotas)
                .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
                .ToListAsync(ct);

            var config = await GetConfiguracionAsync(ct);
            var ahora = DateTime.UtcNow;
            var snapshot = ClienteScoringCalculator.CalcularSnapshot(cliente.CreatedAt, ventas, creditos, ahora);
            return ClienteScoringCalculator.CalcularPuntaje(snapshot, cliente.Sueldo, config, ahora);
        }

        private static IEnumerable<List<int>> Lotear(List<int> ids, int batchSize)
        {
            for (var i = 0; i < ids.Count; i += batchSize)
                yield return ids.GetRange(i, Math.Min(batchSize, ids.Count - i));
        }
    }
}
