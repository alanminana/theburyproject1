using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación canónica de <see cref="IPunitorioService"/> (PUN-ML5). Único punto que combina
    /// datos reales de cuota/pagos/configuraciones con <see cref="IPunitorioCalculator"/> (PUN-ML4) y
    /// decide qué hacer con el resultado — controller, Razor y JS nunca reconstruyen la fórmula.
    ///
    /// Separación estricta: <see cref="CalcularCuotaAsync"/> es de solo lectura (ni siquiera dentro
    /// de una transacción); <see cref="AplicarAsync"/> y <see cref="AnularAsync"/> son las únicas dos
    /// operaciones que persisten, y ambas requieren una identidad autorizada y un motivo.
    ///
    /// Autorización: el servicio resuelve el actor y valida el permiso él mismo, vía
    /// <see cref="ICurrentUserService"/> (mismo servicio inyectado que ya usan <c>VentaService</c> y
    /// <c>CreditoService</c> para checks de permiso en la capa de servicio). No hay todavía un
    /// controller para PunitorioService, así que esta es la única barrera real: no se apoya en un
    /// <c>[PermisoRequerido]</c> futuro. Ningún caller — controller, job, u otro servicio — puede
    /// pasar una identidad o un permiso por parámetro; ambos se leen exclusivamente del contexto
    /// autenticado del servidor.
    /// </summary>
    public sealed class PunitorioService : IPunitorioService
    {
        private readonly AppDbContext _context;
        private readonly IPunitorioCalculator _calculator;
        private readonly IRelojComercial _reloj;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<PunitorioService> _logger;

        private const string ModuloCobranzas = "cobranzas";
        private const string AccionAplicarPunitorio = "applyfine";
        private const string AccionAnularPunitorio = "revertfine";

        private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
        {
            WriteIndented = false
        };

        public PunitorioService(
            AppDbContext context,
            IPunitorioCalculator calculator,
            IRelojComercial reloj,
            ICurrentUserService currentUserService,
            ILogger<PunitorioService> logger)
        {
            _context = context;
            _calculator = calculator;
            _reloj = reloj;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<PunitorioConsultaResultado> CalcularCuotaAsync(
            int cuotaId, DateOnly? fechaCalculo = null, CancellationToken cancellationToken = default)
        {
            var cuota = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Pagos)
                .FirstOrDefaultAsync(c => c.Id == cuotaId && !c.IsDeleted, cancellationToken)
                ?? throw new KeyNotFoundException($"Cuota #{cuotaId} no encontrada.");

            var fecha = fechaCalculo ?? _reloj.HoyComercial;

            var configuraciones = await _context.ConfiguracionesPunitorio
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .ToListAsync(cancellationToken);

            var resultado = _calculator.Calcular(ConstruirEntrada(cuota, configuraciones, fecha));

            var pendiente = await ObtenerPunitorioAplicadoPendienteAsync(cuotaId, cancellationToken);

            var configuracionesUtilizadas = ConfiguracionesUsadasEnSegmentos(resultado, configuraciones);

            return new PunitorioConsultaResultado
            {
                CuotaId = cuota.Id,
                CreditoId = cuota.CreditoId,
                FechaCalculo = fecha,
                EstadoCalculo = resultado.EstadoResultado,
                SaldoImpago = resultado.SaldoFinal,
                PunitorioCalculado = resultado.PunitorioRedondeado,
                Segmentos = resultado.Segmentos,
                ConfiguracionesUtilizadas = configuracionesUtilizadas,
                HistorialCompleto = resultado.EstadoResultado != EstadoResultadoPunitorio.HistorialIncompleto,
                MotivoNoCalculo = resultado.EstadoResultado == EstadoResultadoPunitorio.Calculado ? null : resultado.Motivo,
                PunitorioAplicadoPendiente = pendiente,
                TotalPendienteEstimado = resultado.PunitorioRedondeado is null
                    ? null
                    : resultado.SaldoFinal + resultado.PunitorioRedondeado.Value
            };
        }

        public async Task<decimal> ObtenerPunitorioAplicadoPendienteAsync(
            int cuotaId, CancellationToken cancellationToken = default)
        {
            // Modelo A: a lo sumo una fila Estado=Aplicado por cuota (índice único filtrado).
            var activa = await _context.PunitoriosAplicados
                .AsNoTracking()
                .Where(p => p.CuotaId == cuotaId && p.Estado == EstadoPunitorioAplicado.Aplicado)
                .Select(p => new { p.Id, p.Importe })
                .FirstOrDefaultAsync(cancellationToken);

            if (activa is null)
                return 0m;

            var pagado = await ObtenerMontoPagadoAplicacionAsync(activa.Id, cancellationToken);
            return Math.Max(0m, activa.Importe - pagado);
        }

        public async Task<PunitorioAplicadoProgreso?> ObtenerAplicacionActivaConProgresoAsync(
            int cuotaId, CancellationToken cancellationToken = default)
        {
            // Tracked a propósito (sin AsNoTracking): el caller (cobro, PUN-ML6) necesita poder mutar
            // Estado dentro de su propia transacción/AppDbContext sin una consulta adicional.
            var activa = await _context.PunitoriosAplicados
                .FirstOrDefaultAsync(p => p.CuotaId == cuotaId && p.Estado == EstadoPunitorioAplicado.Aplicado, cancellationToken);

            if (activa is null)
                return null;

            var pagado = await ObtenerMontoPagadoAplicacionAsync(activa.Id, cancellationToken);

            return new PunitorioAplicadoProgreso
            {
                Aplicacion = activa,
                MontoPagado = pagado,
                MontoPendiente = Math.Max(0m, activa.Importe - pagado)
            };
        }

        /// <summary>
        /// Suma de <c>PagoCuota.ImporteAplicadoPunitorio</c> efectivamente cobrados (Estado=Aplicado,
        /// ignora anulados/revertidos) atribuidos a una aplicación puntual, vía
        /// <c>PagoCuota.PunitorioAplicadoId</c> (PUN-ML6). Fuente única para el helper canónico de
        /// progreso (<see cref="ObtenerPunitorioAplicadoPendienteAsync"/>,
        /// <see cref="ObtenerAplicacionActivaConProgresoAsync"/>): no se guarda un acumulado
        /// redundante en <see cref="PunitorioAplicado"/>.
        /// </summary>
        private async Task<decimal> ObtenerMontoPagadoAplicacionAsync(
            int punitorioAplicadoId, CancellationToken cancellationToken)
        {
            // Suma en cliente: el proveedor Sqlite (tests) no traduce Sum sobre decimal a SQL.
            var importes = await _context.PagosCuota
                .AsNoTracking()
                .Where(p => p.PunitorioAplicadoId == punitorioAplicadoId && p.Estado == EstadoPagoCuota.Aplicado)
                .Select(p => p.ImporteAplicadoPunitorio)
                .ToListAsync(cancellationToken);

            return importes.Sum(i => i ?? 0m);
        }

        /// <summary>
        /// Helper canónico de transición de estado (PUN-ML6): una aplicación pasa a
        /// <see cref="EstadoPunitorioAplicado.Pagado"/> exactamente cuando lo efectivamente cobrado
        /// alcanza su importe. Única fuente de esta regla — no se duplica en <c>CreditoService</c>.
        /// </summary>
        internal static EstadoPunitorioAplicado ResolverEstadoAplicado(decimal importe, decimal montoPagado) =>
            montoPagado >= importe ? EstadoPunitorioAplicado.Pagado : EstadoPunitorioAplicado.Aplicado;

        public async Task<PunitorioAplicado> AplicarAsync(
            int cuotaId, PunitorioAplicarComando comando, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);

            var usuarioAutorizado = ValidarAutorizacion(AccionAplicarPunitorio, "aplicar");

            if (string.IsNullOrWhiteSpace(comando.Motivo))
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.SolicitudInvalida,
                    "El motivo es obligatorio para aplicar un punitorio.");

            await using var transaction =
                await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                var cuota = await _context.Cuotas
                    .Include(c => c.Pagos)
                    .FirstOrDefaultAsync(c => c.Id == cuotaId && !c.IsDeleted, cancellationToken);

                if (cuota is null)
                    throw new KeyNotFoundException($"Cuota #{cuotaId} no encontrada.");

                if (comando.CuotaRowVersionEsperada is { Length: > 0 } esperado &&
                    !esperado.SequenceEqual(cuota.RowVersion))
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        "La cuota cambió desde que se calculó el punitorio. Recalculá antes de aplicar.");

                // Modelo A: como máximo una aplicación activa por cuota. Este chequeo es la
                // verificación previa (mensaje específico); el índice único filtrado sobre
                // PunitoriosAplicados.CuotaId (ver AppDbContext) es la garantía real contra una
                // carrera concurrente entre dos aplicaciones simultáneas.
                var existeActiva = await _context.PunitoriosAplicados
                    .AnyAsync(p => p.CuotaId == cuotaId && p.Estado == EstadoPunitorioAplicado.Aplicado, cancellationToken);
                if (existeActiva)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        "Ya existe una aplicación activa de punitorio para esta cuota. Anulala antes de volver a aplicar.");

                var configuraciones = await _context.ConfiguracionesPunitorio
                    .AsNoTracking()
                    .Where(c => !c.IsDeleted)
                    .ToListAsync(cancellationToken);

                var fechaCalculo = _reloj.HoyComercial;
                var resultado = _calculator.Calcular(ConstruirEntrada(cuota, configuraciones, fechaCalculo));

                ValidarEstadoCalculable(resultado);

                // Aplicaciones sucesivas (PUN-ML6): el teórico del calculador es el acumulado TOTAL
                // desde el vencimiento, no un incremental. Netear lo ya aplicado (activas — ya
                // rechazado arriba — y pagadas; anuladas no cuentan) evita cobrar dos veces los mismos
                // días cuando una aplicación anterior ya fue pagada y se quiere aplicar el diferencial
                // de días transcurridos desde entonces.
                var previas = await _context.PunitoriosAplicados
                    .AsNoTracking()
                    .Where(p => p.CuotaId == cuotaId && p.Estado != EstadoPunitorioAplicado.Anulado)
                    .Select(p => p.Importe)
                    .ToListAsync(cancellationToken);
                var importePreviamenteAplicado = previas.Sum();

                var teorico = resultado.PunitorioRedondeado!.Value;

                if (teorico < importePreviamenteAplicado)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        $"El cálculo actual (${teorico:N2}) es menor a lo ya aplicado (${importePreviamenteAplicado:N2}): " +
                        "probablemente una configuración retroactiva redujo el punitorio. No se genera un importe " +
                        "negativo — requiere ajuste manual.")
                    {
                        EstadoCalculo = resultado.EstadoResultado
                    };

                var diferencial = teorico - importePreviamenteAplicado;

                if (diferencial <= 0m)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.NoAplicable,
                        "No hay diferencial nuevo para aplicar: el cálculo acumulado coincide con lo ya aplicado " +
                        $"(${importePreviamenteAplicado:N2}).")
                    {
                        EstadoCalculo = resultado.EstadoResultado
                    };

                var idsUsados = resultado.Segmentos
                    .Select(s => s.ConfiguracionPunitorioId)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList();
                var configUnica = idsUsados.Count == 1 ? configuraciones.First(c => c.Id == idsUsados[0]) : null;

                var aplicado = new PunitorioAplicado
                {
                    CuotaId = cuota.Id,
                    FechaCalculo = fechaCalculo,
                    SaldoBase = resultado.SaldoFinal,
                    DiasComputados = resultado.DiasTranscurridos,
                    Importe = diferencial,
                    Estado = EstadoPunitorioAplicado.Aplicado,
                    ConfiguracionPunitorioId = configUnica?.Id,
                    Porcentaje = configUnica?.Porcentaje,
                    PeriodoDias = configUnica?.PeriodoDias,
                    DiasGracia = configUnica?.DiasGracia,
                    DesgloseSnapshotJson = JsonSerializer.Serialize(
                        ConstruirSnapshot(resultado, configuraciones, teorico, importePreviamenteAplicado, diferencial),
                        SnapshotJsonOptions),
                    MotivoAplicacion = comando.Motivo.Trim(),
                    FechaAplicacion = _reloj.AhoraUtc,
                    UsuarioAplicacion = usuarioAutorizado
                };

                _context.PunitoriosAplicados.Add(aplicado);
                await _context.SaveChangesAsync(cancellationToken);

                // PUN-ML7: aplicar un punitorio puede dejar pendiente una cuota que hasta acá
                // figuraba Pagada (capital saldado, sin punitorio previo) — "capital cero + punitorio
                // pendiente no está pagada" es un contrato congelado. diferencial > 0 siempre acá
                // (verificado arriba), así que el pendiente nuevo de ESTA cuota es exactamente
                // diferencial: Modelo A garantiza que no había otra aplicación activa (rechazada más
                // arriba si existía) y las pagadas/anuladas no aportan pendiente.
                var estadoAnterior = cuota.Estado;
                cuota.Estado = EstadoCuotaResolver.Resolver(
                    cuota.Estado, cuota.FechaVencimiento, fechaCalculo, cuota.MontoPagado, cuota.MontoTotal, diferencial);

                // Si la cuota deja de estar Pagada por esta aplicación, FechaPago ya no representa
                // "cuándo quedó saldada" (contrato PUN-ML7 de Cuota.FechaPago) — se limpia.
                if (estadoAnterior == EstadoCuota.Pagada && cuota.Estado != EstadoCuota.Pagada)
                    cuota.FechaPago = null;

                if (cuota.Estado != estadoAnterior)
                    await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Punitorio aplicado - Cuota {CuotaId} - Importe {Importe} - Usuario {Usuario}",
                    aplicado.CuotaId, aplicado.Importe, aplicado.UsuarioAplicacion);

                return aplicado;
            }
            catch (PunitorioAplicadoRechazadoException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning("Aplicación de punitorio rechazada - Cuota {CuotaId}: {Motivo}", cuotaId, ex.Message);
                throw;
            }
            catch (KeyNotFoundException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (DbUpdateException ex)
            {
                // Única causa realista en un INSERT nuevo: el índice único filtrado de
                // PunitoriosAplicados.CuotaId (carrera concurrente que pasó la verificación previa
                // antes de que la otra confirmara). Mismo patrón que ConfiguracionPunitorioService.
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(ex, "Conflicto de concurrencia al aplicar punitorio - Cuota {CuotaId}.", cuotaId);
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.Conflicto,
                    "Ya existe una aplicación activa de punitorio para esta cuota (conflicto de concurrencia). Recargá e intentá nuevamente.",
                    ex);
            }
            catch (Exception ex) when (EsConflictoTransitorioDeBase(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(ex, "Conflicto de concurrencia en base al aplicar punitorio - Cuota {CuotaId}.", cuotaId);
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.Conflicto,
                    "La aplicación se cruzó con otra operación sobre la misma cuota. Recargá e intentá nuevamente.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(ex, "Error al aplicar punitorio - Cuota {CuotaId}.", cuotaId);
                throw;
            }
        }

        public async Task<PunitorioAplicado> AnularAsync(
            int punitorioAplicadoId, PunitorioAnularComando comando, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);

            var usuarioAutorizado = ValidarAutorizacion(AccionAnularPunitorio, "anular");

            if (string.IsNullOrWhiteSpace(comando.Motivo))
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.SolicitudInvalida,
                    "El motivo es obligatorio para anular una aplicación de punitorio.");

            await using var transaction =
                await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                var aplicado = await _context.PunitoriosAplicados
                    .Include(p => p.Cuota)
                    .FirstOrDefaultAsync(p => p.Id == punitorioAplicadoId, cancellationToken);

                if (aplicado is null)
                    throw new KeyNotFoundException($"Punitorio aplicado #{punitorioAplicadoId} no encontrado.");

                if (comando.RowVersionEsperado is { Length: > 0 } esperado)
                    _context.Entry(aplicado).Property(a => a.RowVersion).OriginalValue = esperado;

                if (aplicado.Estado == EstadoPunitorioAplicado.Anulado)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        "Esta aplicación de punitorio ya fue anulada.");

                if (aplicado.Estado != EstadoPunitorioAplicado.Aplicado)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        $"No se puede anular una aplicación en estado {aplicado.Estado}.");

                // PUN-ML6: una aplicación con pagos efectivos ya atribuidos (parcial o total — total
                // ya la habría pasado a Pagado, cubierto por el chequeo de arriba) no puede anularse
                // silenciosamente: se perdería la atribución de un cobro real ya registrado en el
                // ledger. Anular primero requiere resolver esos pagos por otra vía (fuera de alcance).
                var montoPagado = await ObtenerMontoPagadoAplicacionAsync(aplicado.Id, cancellationToken);
                if (montoPagado > 0m)
                    throw new PunitorioAplicadoRechazadoException(
                        MotivoRechazoPunitorioAplicado.Conflicto,
                        $"No se puede anular: ya se cobraron ${montoPagado:N2} de esta aplicación.");

                aplicado.Estado = EstadoPunitorioAplicado.Anulado;
                aplicado.FechaAnulacion = _reloj.AhoraUtc;
                aplicado.UsuarioAnulacion = usuarioAutorizado;
                aplicado.MotivoAnulacion = comando.Motivo.Trim();

                // PUN-ML7: anular la única aplicación activa (Modelo A) dejó el pendiente de esta
                // cuota en 0 — sin necesidad de re-consultar: ya se rechazó arriba si tenía pagos, y
                // el índice único filtrado garantiza que no hay otra fila Aplicado simultánea. Eso
                // puede recalcular la cuota de vuelta a Pagada (contrato: "anular una aplicación no
                // pagada permite recalcular el estado").
                var cuota = aplicado.Cuota;
                var estadoAnterior = cuota.Estado;
                cuota.Estado = EstadoCuotaResolver.Resolver(
                    cuota.Estado, cuota.FechaVencimiento, _reloj.HoyComercial, cuota.MontoPagado, cuota.MontoTotal, 0m);

                if (estadoAnterior != EstadoCuota.Pagada && cuota.Estado == EstadoCuota.Pagada)
                    cuota.FechaPago = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Punitorio anulado - Id {Id} - Cuota {CuotaId} - Usuario {Usuario}",
                    aplicado.Id, aplicado.CuotaId, aplicado.UsuarioAnulacion);

                return aplicado;
            }
            catch (PunitorioAplicadoRechazadoException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning("Anulación de punitorio rechazada - Id {Id}: {Motivo}", punitorioAplicadoId, ex.Message);
                throw;
            }
            catch (KeyNotFoundException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(ex, "Conflicto de concurrencia al anular punitorio - Id {Id}.", punitorioAplicadoId);
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.Conflicto,
                    "La aplicación fue modificada por otro usuario. Recargá los datos e intentá nuevamente.", ex);
            }
            catch (Exception ex) when (EsConflictoTransitorioDeBase(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(ex, "Conflicto de concurrencia en base al anular punitorio - Id {Id}.", punitorioAplicadoId);
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.Conflicto,
                    "La anulación se cruzó con otra operación. Recargá e intentá nuevamente.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(ex, "Error al anular punitorio - Id {Id}.", punitorioAplicadoId);
                throw;
            }
        }

        /// <summary>
        /// Resuelve y valida el actor desde <see cref="ICurrentUserService"/> (infraestructura
        /// confiable de servidor) para las dos operaciones que persisten. Nunca lee identidad ni
        /// permiso de un parámetro o del comando: si algún caller (controller, job, otro servicio)
        /// pasara un string arbitrario, no habría forma de que llegue hasta acá, porque no existe
        /// ningún parámetro de ese tipo en la firma pública.
        /// </summary>
        /// <param name="accion">Clave de acción dentro del módulo "cobranzas" (<c>applyfine</c> o <c>revertfine</c>).</param>
        /// <param name="verboInfinitivo">Verbo para el mensaje de rechazo ("aplicar"/"anular").</param>
        /// <returns>El nombre de usuario autenticado, para persistir en el campo de auditoría de dominio.</returns>
        private string ValidarAutorizacion(string accion, string verboInfinitivo)
        {
            if (!_currentUserService.IsAuthenticated())
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.NoAutorizado,
                    $"Debe iniciar sesión para {verboInfinitivo} punitorios.");

            if (!_currentUserService.HasPermission(ModuloCobranzas, accion))
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.NoAutorizado,
                    $"No tiene permiso para {verboInfinitivo} punitorios.");

            var usuario = _currentUserService.GetUsername();
            if (string.IsNullOrWhiteSpace(usuario))
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.NoAutorizado,
                    "No se pudo determinar el usuario autenticado.");

            return usuario;
        }

        private static PunitorioCalculoEntrada ConstruirEntrada(
            Cuota cuota, IReadOnlyList<ConfiguracionPunitorio> configuraciones, DateOnly fechaCalculo) => new()
        {
            MontoOriginalCuota = cuota.MontoTotal,
            FechaVencimiento = DateOnly.FromDateTime(cuota.FechaVencimiento),
            FechaCalculo = fechaCalculo,
            PagosAplicados = cuota.Pagos
                .Select(p => new PagoAplicadoPunitorioEntrada
                {
                    FechaPagoComercial = p.FechaPagoComercial,
                    ImporteAplicadoCuota = p.ImporteAplicadoCuota,
                    Estado = p.Estado,
                    HistorialCompleto = p.HistorialCompleto
                })
                .ToList(),
            Configuraciones = configuraciones
                .Select(c => new ConfiguracionPunitorioEntrada
                {
                    Id = c.Id,
                    VigenteDesde = c.VigenteDesde,
                    Porcentaje = c.Porcentaje,
                    PeriodoDias = c.PeriodoDias,
                    DiasGracia = c.DiasGracia,
                    ProrrateoDiario = c.ProrrateoDiario,
                    Activa = c.Activa,
                    AplicacionRetroactiva = c.AplicacionRetroactiva
                })
                .ToList()
        };

        private static List<ConfiguracionPunitorio> ConfiguracionesUsadasEnSegmentos(
            PunitorioCalculoResultado resultado, IReadOnlyList<ConfiguracionPunitorio> configuraciones)
        {
            var idsUsados = resultado.Segmentos
                .Select(s => s.ConfiguracionPunitorioId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            return configuraciones
                .Where(c => idsUsados.Contains(c.Id))
                .OrderBy(c => c.VigenteDesde)
                .ToList();
        }

        private static PunitorioAplicadoSnapshot ConstruirSnapshot(
            PunitorioCalculoResultado resultado,
            IReadOnlyList<ConfiguracionPunitorio> configuraciones,
            decimal punitorioTeoricoAcumulado,
            decimal importePreviamenteAplicado,
            decimal importeNuevoAplicado)
        {
            var configuracionesUsadas = ConfiguracionesUsadasEnSegmentos(resultado, configuraciones)
                .Select(c => new PunitorioAplicadoSnapshotConfiguracion
                {
                    Id = c.Id,
                    VigenteDesde = c.VigenteDesde,
                    Porcentaje = c.Porcentaje,
                    PeriodoDias = c.PeriodoDias,
                    DiasGracia = c.DiasGracia,
                    Activa = c.Activa
                })
                .ToList();

            return new PunitorioAplicadoSnapshot
            {
                FechaVencimiento = resultado.FechaVencimiento,
                FechaCalculo = resultado.FechaCalculo,
                SaldoInicial = resultado.SaldoInicial,
                SaldoFinal = resultado.SaldoFinal,
                DiasTranscurridos = resultado.DiasTranscurridos,
                PunitorioExacto = resultado.PunitorioExacto ?? 0m,
                PunitorioRedondeado = resultado.PunitorioRedondeado ?? 0m,
                Segmentos = resultado.Segmentos
                    .Select(s => new PunitorioAplicadoSnapshotSegmento
                    {
                        Desde = s.Desde,
                        Hasta = s.Hasta,
                        Dias = s.Dias,
                        SaldoBase = s.SaldoBase,
                        ConfiguracionPunitorioId = s.ConfiguracionPunitorioId,
                        Porcentaje = s.Porcentaje,
                        PeriodoDias = s.PeriodoDias,
                        DiasGracia = s.DiasGracia,
                        ImporteExacto = s.ImporteExacto,
                        MotivoDeInicio = s.MotivoDeInicio.ToString(),
                        MotivoDeFin = s.MotivoDeFin.ToString()
                    })
                    .ToList(),
                ConfiguracionesUtilizadas = configuracionesUsadas,
                PunitorioTeoricoAcumulado = punitorioTeoricoAcumulado,
                ImportePreviamenteAplicado = importePreviamenteAplicado,
                ImporteNuevoAplicado = importeNuevoAplicado
            };
        }

        /// <summary>
        /// Rechaza aplicar cuando el cálculo no tiene un total autoritativo cobrable. El rechazo por
        /// "nada nuevo para aplicar" (importe cero o negativo tras netear lo ya aplicado, PUN-ML6) se
        /// resuelve después, una vez calculado el diferencial contra aplicaciones previas — no acá,
        /// porque una primera aplicación de $0 y una sucesiva sin diferencial son el mismo caso base
        /// (importePreviamenteAplicado = 0) resuelto por la misma regla.
        /// </summary>
        private static void ValidarEstadoCalculable(PunitorioCalculoResultado resultado)
        {
            if (resultado.EstadoResultado != EstadoResultadoPunitorio.Calculado)
                throw new PunitorioAplicadoRechazadoException(
                    MotivoRechazoPunitorioAplicado.NoAplicable,
                    $"No se puede aplicar punitorio ({resultado.EstadoResultado}): {resultado.Motivo}")
                {
                    EstadoCalculo = resultado.EstadoResultado
                };
        }

        /// <summary>
        /// Reconoce el fallo que reporta el motor cuando la transacción serializable pierde frente a
        /// otra simultánea. Copia del helper homónimo de <c>CreditoService</c> (mismo criterio,
        /// duplicado en vez de extraído a un utilitario compartido: es el único otro punto del
        /// proyecto con esta necesidad, y una abstracción prematura entre dos usos no se justifica).
        /// </summary>
        private static bool EsConflictoTransitorioDeBase(Exception excepcion)
        {
            for (Exception? actual = excepcion; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number is 1205 or 3960))
                    return true;

                if (actual is DbException { IsTransient: true })
                    return true;
            }

            return false;
        }
    }
}
