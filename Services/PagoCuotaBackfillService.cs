using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Backfill conservador de <see cref="PagoCuota"/> (PUN-ML2) desde <c>MovimientosCaja</c>
    /// existentes con <c>Concepto == CobroCuota</c>.
    ///
    /// Criterio rector: ante datos históricos ambiguos, marcar incompleto — nunca reconstruir
    /// importes o fechas por suposición. En particular, la composición capital/punitorio de un
    /// pago histórico solo se completa cuando es matemáticamente inequívoca (cuota cuyo
    /// punitorio vigente es 0: por monotonía del cálculo legacy, si el punitorio final es 0
    /// ningún pago anterior sobre esa cuota pudo haber tenido punitorio > 0). En cualquier otro
    /// caso se registra el importe total exacto y se deja la composición sin completar — jamás
    /// se reparte proporcionalmente ni se asume un orden de imputación.
    /// </summary>
    public sealed class PagoCuotaBackfillService : IPagoCuotaBackfillService
    {
        private readonly AppDbContext _context;
        private readonly IRelojComercial _reloj;

        public PagoCuotaBackfillService(AppDbContext context, IRelojComercial reloj)
        {
            _context = context;
            _reloj = reloj;
        }

        public async Task<PagoCuotaBackfillResultado> EjecutarAsync(CancellationToken cancellationToken = default)
        {
            var detalles = new List<PagoCuotaBackfillDetalle>();
            int completos = 0, parciales = 0, omitidos = 0, ambiguos = 0;

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var movimientos = await _context.MovimientosCaja
                    .AsNoTracking()
                    .Where(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota)
                    .OrderBy(m => m.Id)
                    .ToListAsync(cancellationToken);

                // Guarda de aplicación: evita reconstruir un movimiento que ya tiene fila (sea de
                // un backfill previo o de un pago nuevo que ya escribió su propio ledger). La
                // garantía real, comprobable ante una corrida concurrente, es el índice único
                // filtrado sobre PagosCuota.MovimientoCajaId — esto es solo para no intentarlo.
                var yaImportados = (await _context.PagosCuota
                        .AsNoTracking()
                        .Where(p => p.MovimientoCajaId != null)
                        .Select(p => p.MovimientoCajaId!.Value)
                        .ToListAsync(cancellationToken))
                    .ToHashSet();

                var cuotaIdsReferenciadas = movimientos
                    .Where(m => m.ReferenciaId.HasValue)
                    .Select(m => m.ReferenciaId!.Value)
                    .Distinct()
                    .ToList();

                var cuotasPorId = await _context.Cuotas
                    .AsNoTracking()
                    .Where(c => cuotaIdsReferenciadas.Contains(c.Id) && !c.IsDeleted)
                    .ToDictionaryAsync(c => c.Id, cancellationToken);

                foreach (var movimiento in movimientos)
                {
                    if (yaImportados.Contains(movimiento.Id))
                    {
                        omitidos++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, movimiento.ReferenciaId, "Omitido",
                            "Ya existe una fila de ledger para este movimiento (idempotencia)."));
                        continue;
                    }

                    if (!movimiento.ReferenciaId.HasValue ||
                        !cuotasPorId.TryGetValue(movimiento.ReferenciaId.Value, out var cuota))
                    {
                        omitidos++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, movimiento.ReferenciaId, "Omitido",
                            "ReferenciaId ausente o no corresponde a una cuota existente."));
                        continue;
                    }

                    if (!movimiento.ImporteBase.HasValue)
                    {
                        omitidos++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, cuota.Id, "Omitido",
                            "ImporteBase ausente: usar Monto lo contaminaría con el recargo del medio de pago."));
                        continue;
                    }

                    if (movimiento.ImporteBase.Value <= 0m)
                    {
                        ambiguos++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, cuota.Id, "Ambiguo",
                            $"ImporteBase inválido ({movimiento.ImporteBase.Value:N2}): no se puede interpretar como un cobro."));
                        continue;
                    }

                    var fechaPagoComercial = DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(
                            DateTime.SpecifyKind(movimiento.FechaMovimiento, DateTimeKind.Utc),
                            _reloj.ZonaComercial));

                    // Monotonía del cálculo legacy: el punitorio nunca decrece entre pagos
                    // sucesivos de una misma cuota (los días de atraso solo pueden crecer). Si el
                    // valor final vigente es 0, ningún pago anterior sobre esta cuota pudo haber
                    // tenido punitorio > 0 — la composición 100% cuota es una prueba, no una
                    // suposición. Si es > 0, no hay forma de saber cuánto valía en cada pago
                    // individual (se sobrescribe en cada cobro, ver PUN-ML1 Legacy_MontoPunitorio...).
                    //
                    // La prueba exige que ni la tasa ni el vencimiento ni la base hayan cambiado
                    // entre cobros de la misma cuota. Verificado por búsqueda exhaustiva (PUN-ML2,
                    // reverificación 2026-07-31):
                    //   - Credito.TasaInteres solo se escribe en CreditoService.ConfigurarCreditoAsync
                    //     (único escritor en todo el repo), alcanzable únicamente mientras
                    //     Estado ∈ {PendienteConfiguracion, Solicitado, Configurado}
                    //     (CreditoController.EsConfigurable, único caller). Las cuotas se generan
                    //     recién en VentaService.GenerarCuotasCreditoAsync, después de ese punto:
                    //     ninguna cuota que pueda tener un pago vio cambiar la tasa de su crédito.
                    //   - Cuota.FechaVencimiento y Cuota.MontoTotal se asignan una sola vez, en el
                    //     inicializador de VentaService.GenerarCuotasCreditoAsync (VentaService.cs
                    //     ~1088-1118); no existe ningún otro escritor en el repo.
                    // Si alguno de estos escritores cambia, esta prueba deja de ser válida y
                    // "HistorialCompleto = true" pasaría a inventar composición: revisar antes de
                    // tocar ConfigurarCreditoAsync, EsConfigurable o GenerarCuotasCreditoAsync.
                    var historialCompleto = cuota.MontoPunitorio <= 0m;

                    var pago = new PagoCuota
                    {
                        CuotaId = cuota.Id,
                        FechaPagoComercial = fechaPagoComercial,
                        ImporteTotal = movimiento.ImporteBase.Value,
                        ImporteAplicadoCuota = historialCompleto ? movimiento.ImporteBase.Value : null,
                        ImporteAplicadoPunitorio = historialCompleto ? 0m : null,
                        MovimientoCajaId = movimiento.Id,
                        MedioPago = movimiento.MedioPagoDetalle,
                        Origen = historialCompleto
                            ? OrigenPagoCuota.BackfillMovimientoCaja
                            : OrigenPagoCuota.BackfillIncompleto,
                        Estado = EstadoPagoCuota.Aplicado,
                        HistorialCompleto = historialCompleto,
                        MotivoIncompleto = historialCompleto
                            ? null
                            : "Cuota con punitorio vigente > 0: la composición capital/punitorio de este pago " +
                              "histórico no es reconstruible porque MontoPunitorio se sobrescribe en cada cobro " +
                              "(ver PUN-ML1, Legacy_MontoPunitorioSePisaEnCadaCobro).",
                        CreatedAt = movimiento.FechaMovimiento,
                        CreatedBy = string.IsNullOrWhiteSpace(movimiento.Usuario) ? null : movimiento.Usuario
                    };

                    _context.PagosCuota.Add(pago);

                    if (historialCompleto)
                    {
                        completos++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, cuota.Id, "Completo",
                            "Punitorio vigente 0: la cuota nunca estuvo en mora, composición 100% cuota."));
                    }
                    else
                    {
                        parciales++;
                        detalles.Add(new PagoCuotaBackfillDetalle(
                            movimiento.Id, cuota.Id, "Parcial",
                            "Importe total reconstruido; composición capital/punitorio no reconstruible."));
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return new PagoCuotaBackfillResultado(completos, parciales, omitidos, ambiguos, detalles);
        }
    }
}
