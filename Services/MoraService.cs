using System.Globalization;
using System.Text;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// </summary>
    public class MoraService : IMoraService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<MoraService> _logger;
        /// <summary>Umbral de días de atraso para clasificar una mora como elevada.</summary>
        private const int DiasMoraElevada = 90;

        // Fallback cuando ConfiguracionMora.DiasAntesAlertaPreventiva no está configurado.
        private const int DiasAnticipacionAlerta = 5;

        // Divisor mensual para convertir tasa mensual en tasa diaria (días por mes comercial).
        private const decimal DiasPorMesComercial = 30m;

        // Fallbacks cuando ConfiguracionMora.DiasParaPrioridad* no están configurados.
        private const int DiasUmbralAlertaCritica = 30;
        private const int DiasUmbralAlertaAlta = 15;
        private const int DiasUmbralAlertaMedia = 7;

        // Fallbacks cuando ConfiguracionMora.MontoParaPrioridad* no están configurados.
        private const decimal MontoUmbralAlertaCritica = 50_000m;
        private const decimal MontoUmbralAlertaAlta = 30_000m;
        private const decimal MontoUmbralAlertaMedia = 15_000m;

        private ConfiguracionMora? _configuracion;

        private readonly ICreditoService _creditoService;
        private readonly IClienteScoringService _clienteScoringService;
        private readonly IRelojComercial _reloj;
        private readonly ISeguridadAuditoriaService? _auditoria;

        public MoraService(
            AppDbContext context,
            IMapper mapper,
            ILogger<MoraService> logger,
            ICreditoService creditoService,
            IClienteScoringService clienteScoringService,
            IRelojComercial reloj,
            ISeguridadAuditoriaService? auditoria = null)
        {
            _auditoria = auditoria;
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _creditoService = creditoService;
            _clienteScoringService = clienteScoringService;
            // PUN-ML7 (auditoría DI): fuente única de "hoy" para vencimiento/mora, inyección
            // obligatoria — el único constructor directo fuera de Program.cs es MoraServiceTests, ya
            // ajustado a pasarlo explícitamente. Program.cs registra IRelojComercial como Singleton.
            _reloj = reloj;
        }

        #region Configuración

        public async Task<ConfiguracionMora> GetConfiguracionAsync()
        {
            try
            {
                _configuracion ??= await _context.ConfiguracionesMora
                    .Where(c => !c.IsDeleted)
                    .FirstOrDefaultAsync();

                if (_configuracion == null)
                {
                    _configuracion = new ConfiguracionMora
                    {
                        DiasGracia = 3,
                        TasaMoraBase = 5.0m,
                        ProcesoAutomaticoActivo = true,
                        NotificacionesActivas = true,
                        HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.ConfiguracionesMora.Add(_configuracion);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Configuración de mora creada por defecto");
                }

                return _configuracion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración de mora");
                throw;
            }
        }

        public async Task<ConfiguracionMora> UpdateConfiguracionAsync(ConfiguracionMoraViewModel viewModel)
        {
            try
            {
                var config = await _context.ConfiguracionesMora
                    .FirstOrDefaultAsync(c => c.Id == viewModel.Id && !c.IsDeleted);
                if (config == null)
                    throw new InvalidOperationException("Configuración no encontrada");

                config.DiasGracia = viewModel.DiasGracia;
                config.TasaMoraBase = viewModel.PorcentajeRecargo;
                config.ProcesoAutomaticoActivo = viewModel.CalculoAutomatico;
                config.NotificacionesActivas = viewModel.NotificacionAutomatica;
                config.HoraEjecucionDiaria = viewModel.HoraEjecucion;
                config.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                _configuracion = null; // Limpiar caché para recargar

                _logger.LogInformation("Configuración de mora actualizada");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar configuración de mora");
                throw;
            }
        }

        #endregion

        #region Procesamiento de Mora

        public async Task ProcesarMoraAsync(CancellationToken ct = default)
        {
            var inicioEjecucion = DateTime.UtcNow;
            var log = new LogMora
            {
                FechaEjecucion = inicioEjecucion,
                Exitoso = false,
                CuotasProcesadas = 0,
                AlertasGeneradas = 0,
                CuotasConMora = 0,
                TotalMora = 0,
                TotalRecargosAplicados = 0,
                Errores = 0
            };

            try
            {
                _logger.LogInformation("=== INICIANDO PROCESAMIENTO DE MORA ===");

                var config = await GetConfiguracionAsync();
                // PUN-ML7: fecha comercial única (antes DateTime.Today — dependía de la zona del
                // proceso, no de Argentina). InicioDiaComercial es la cota diseñada para comparar
                // contra Cuota.FechaVencimiento (DateTime a medianoche).
                var hoy = _reloj.InicioDiaComercial;
                var fechaLimite = hoy.AddDays(-(config.DiasGracia ?? 0));
                // PUN-ML7 (cierre): DateOnly equivalente de "hoy", requerido por
                // EstadoCuotaResolver.DiasAtrasoDerivado (autoridad canónica para AlertaCobranza.DiasAtraso).
                var fechaComercial = _reloj.HoyComercial;

                // Obtener cuotas vencidas en un solo query (sin N+1).
                // PUN-ML7 (corrección): antes filtraba solo Estado==Pendiente. Como
                // ActualizarEstadoCuotasAsync (más abajo, y también disparado por este mismo método
                // en corridas previas) transiciona Pendiente→Vencida, esa condición hacía que una
                // cuota vencida desapareciera de este query en la corrida SIGUIENTE a la que cruzó la
                // frontera — quedaba invisible para generación de alertas mientras seguía en mora. El
                // filtro correcto no depende del enum puntual sino de la condición de negocio: no
                // terminal (ni Pagada ni Cancelada), con saldo de capital pendiente (una cuota cuyo
                // capital está saldado pero retiene solo un punitorio aplicado pendiente NO es
                // candidata acá — ese caso lo cubre el score de mora vía ImpactarScorePorMoraAsync,
                // no la cola de cobranza) y efectivamente vencida. Cubre Pendiente vencida, Vencida y
                // Parcial vencida por igual (Parcial nunca es Vencida en EstadoCuotaResolver.Resolver,
                // pero con saldo de capital y fecha vencida sigue siendo mora real).
                var cuotasVencidas = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr!.Cliente)
                    .Where(c => !c.IsDeleted &&
                           !c.Credito!.IsDeleted &&
                           !c.Credito!.Cliente!.IsDeleted &&
                           c.Estado != EstadoCuota.Pagada &&
                           c.Estado != EstadoCuota.Cancelada &&
                           c.MontoPagado < c.MontoTotal &&
                           c.FechaVencimiento < fechaLimite)
                    .ToListAsync(ct);

                log.CuotasProcesadas = cuotasVencidas.Count;

                // Agrupar por crédito para evitar múltiples queries
                var cuotasPorCredito = cuotasVencidas.GroupBy(c => c.CreditoId).ToList();
                int alertasCreadas = 0;

                // Pre-cargar en batch las alertas activas existentes por crédito, para evitar N+1.
                // Trackeadas (sin AsNoTracking) porque, a diferencia de antes, un reproceso puede
                // actualizar AlertaCobranza.DiasAtraso sobre la misma fila en vez de solo saltarla.
                var creditoIds = cuotasPorCredito.Select(g => g.Key).ToList();
                var alertasActivasPorCredito = (await _context.AlertasCobranza
                    .Where(a => creditoIds.Contains(a.CreditoId) &&
                                !a.Resuelta &&
                                a.Tipo == TipoAlertaCobranza.CuotaVencida &&
                                !a.IsDeleted)
                    .ToListAsync(ct))
                    .GroupBy(a => a.CreditoId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var grupo in cuotasPorCredito)
                {
                    try
                    {
                        var creditoId = grupo.Key;
                        var cuotasDelCredito = grupo.ToList();
                        var primeraCuota = cuotasDelCredito.First();
                        var credito = primeraCuota.Credito;
                        var cliente = credito?.Cliente;

                        if (credito == null || cliente == null)
                        {
                            log.Errores++;
                            continue;
                        }

                        // Días de atraso "actuales" (AlertaCobranza.DiasAtraso los documenta como
                        // tales, no como snapshot histórico de creación): la peor cuota del grupo,
                        // vía la autoridad canónica de fecha comercial — nunca resta manual sobre
                        // DateTime.Today/UtcNow.
                        var diasMora = cuotasDelCredito.Max(c =>
                            EstadoCuotaResolver.DiasAtrasoDerivado(c.Estado, c.FechaVencimiento, fechaComercial));

                        // Si ya existe una alerta activa para este crédito: reprocesarla (actualizar
                        // DiasAtraso a la fecha comercial actual) en vez de crear una duplicada.
                        // Idempotente para la misma fecha comercial (mismo valor => sin cambio real) y
                        // no toca ningún otro campo — ni datos manuales de gestión (Observaciones,
                        // EstadoGestion, promesas, resolución) ni UpdatedAt (del que depende
                        // AlertaCobranza.Leida; tocarlo marcaría como "leída" una alerta que nadie vio).
                        if (alertasActivasPorCredito.TryGetValue(creditoId, out var alertaExistente))
                        {
                            alertaExistente.DiasAtraso = diasMora;
                            continue;
                        }

                        // Calcular datos de mora
                        var montoVencido = cuotasDelCredito.Sum(c => c.MontoTotal - c.MontoPagado);
                        var moraCalculada = CalcularMora(montoVencido, diasMora, config);
                        var prioridad = DeterminarPrioridad(diasMora, montoVencido, config);

                        // Crear alerta con información completa
                        var alerta = new AlertaCobranza
                        {
                            CreditoId = creditoId,
                            ClienteId = cliente.Id,
                            Tipo = ObtenerTipoAlerta(diasMora),
                            Prioridad = prioridad,
                            Mensaje = GenerarMensajeAlerta(cliente, montoVencido, cuotasDelCredito.Count, diasMora),
                            MontoVencido = montoVencido,
                            CuotasVencidas = cuotasDelCredito.Count,
                            DiasAtraso = diasMora,
                            FechaAlerta = DateTime.UtcNow,
                            Resuelta = false,
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.AlertasCobranza.Add(alerta);
                        alertasCreadas++;

                        log.CuotasConMora += cuotasDelCredito.Count;
                        log.TotalMora += montoVencido;
                        log.TotalRecargosAplicados += moraCalculada;

                        _logger.LogInformation(
                            "Alerta generada - Crédito: {CreditoId}, Cliente: {Cliente}, Mora: ${Mora}, Días: {Días}",
                            creditoId, cliente.NombreCompleto, montoVencido, diasMora);
                    }
                    catch (Exception ex)
                    {
                        log.Errores++;
                        _logger.LogWarning(ex, "Error procesando crédito {CreditoId}", grupo.Key);
                    }
                }

                // Una alerta de mora activa cuyo crédito ya no tiene cuotas vencidas con saldo (se pagó o
                // se canceló todo lo vencido) deja de ser accionable: se resuelve para no seguir
                // reclamando una deuda saldada. Mismo predicado que la generación de alertas.
                await ResolverAlertasMoraRegularizadasAsync(creditoIds, hoy, ct);

                // Generar alertas de próximos vencimientos
                await GenerarAlertasProximosVencimientosAsync(config, ct);

                // Cambio de estado de cuota y ajuste de score por mora: solo si el job de
                // actualización automática está activo (ConfiguracionMora.ActualizarMoraAutomaticamente).
                if (config.ActualizarMoraAutomaticamente)
                {
                    if (config.CambiarEstadoCuotaAuto)
                    {
                        await _creditoService.ActualizarEstadoCuotasAsync();
                    }

                    if (config.ImpactarScorePorMora)
                    {
                        await ImpactarScorePorMoraAsync(fechaLimite, ct);
                    }
                }

                // Actualizar configuración
                config.UltimaEjecucion = DateTime.UtcNow;
                log.AlertasGeneradas = alertasCreadas;
                log.Exitoso = true;
                log.Mensaje = $"Proceso completado. {cuotasVencidas.Count} cuotas procesadas, " +
                    $"{alertasCreadas} alertas generadas, ${log.TotalMora:F2} en mora.";

                await _context.SaveChangesAsync(ct);

                _logger.LogInformation("=== PROCESAMIENTO DE MORA COMPLETADO ===");
                _logger.LogInformation(log.Mensaje);
            }
            catch (Exception ex)
            {
                log.Exitoso = false;
                log.Mensaje = "Error al procesar mora";
                log.DetalleError = ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : string.Empty);
                log.Errores++;

                _logger.LogError(ex, "Error al procesar mora");
            }
            finally
            {
                // Registrar duración
                log.DuracionEjecucion = DateTime.UtcNow - inicioEjecucion;

                _context.LogsMora.Add(log);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Log de ejecución guardado. Duración: {Duracion}ms",
                    log.DuracionEjecucion.TotalMilliseconds);
            }
        }

        private async Task ResolverAlertasMoraRegularizadasAsync(List<int> creditoIdsConMora, DateTime hoy, CancellationToken ct)
        {
            var ahora = DateTime.UtcNow;
            var candidatas = await _context.AlertasCobranza
                .Where(a => !a.IsDeleted && !a.Resuelta
                            && (a.Tipo == TipoAlertaCobranza.CuotaVencida || a.Tipo == TipoAlertaCobranza.MoraElevada)
                            && !creditoIdsConMora.Contains(a.CreditoId))
                .ToListAsync(ct);
            if (candidatas.Count == 0)
                return;

            // "Regularizada" = sin capital vencido impago, SIN aplicar los días de gracia: si la gracia se amplió
            // después de generar la alerta, la deuda sigue existiendo y su gestión (promesas, contactos) no se pierde.
            var idsCandidatos = candidatas.Select(a => a.CreditoId).Distinct().ToList();
            var creditosConDeudaVencida = (await _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            idsCandidatos.Contains(c.CreditoId) &&
                            c.Estado != EstadoCuota.Pagada &&
                            c.Estado != EstadoCuota.Cancelada &&
                            c.MontoPagado < c.MontoTotal &&
                            c.FechaVencimiento < hoy)
                .Select(c => c.CreditoId)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();
            // Un acuerdo de pago activo puede haber reprogramado los vencimientos: mientras siga activo, la gestión
            // de cobranza (y su visibilidad en la bandeja) se conserva; el acuerdo es la autoridad.
            var creditosConAcuerdoActivo = (await _context.AcuerdosPago
                .Where(ac => !ac.IsDeleted && ac.Estado == EstadoAcuerdo.Activo && idsCandidatos.Contains(ac.CreditoId))
                .Select(ac => ac.CreditoId)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();
            var regularizadas = candidatas
                .Where(a => !creditosConDeudaVencida.Contains(a.CreditoId) && !creditosConAcuerdoActivo.Contains(a.CreditoId))
                .ToList();

            foreach (var alerta in regularizadas)
            {
                alerta.Resuelta = true;
                alerta.FechaResolucion = ahora;
                alerta.EstadoGestion = EstadoGestionCobranza.Regularizado;
                alerta.MotivoResolucion = "Deuda vencida regularizada (resuelta por el proceso de mora)";
                alerta.UpdatedAt = ahora;
            }

            if (regularizadas.Count > 0)
                _logger.LogInformation("Alertas de mora regularizadas resueltas: {Cantidad}", regularizadas.Count);
        }

        private async Task GenerarAlertasProximosVencimientosAsync(ConfiguracionMora config, CancellationToken ct = default)
        {
            try
            {
                // PUN-ML7: fecha comercial única (antes DateTime.Today).
                var hoy = _reloj.InicioDiaComercial;
                var diasAntesAlerta = config.DiasAntesAlertaPreventiva ?? DiasAnticipacionAlerta;
                var proximosDias = hoy.AddDays(diasAntesAlerta);
                var now = DateTime.UtcNow;

                var cuotasPorVencer = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr!.Cliente)
                    .Where(c => !c.IsDeleted &&
                           !c.Credito!.IsDeleted &&
                           !c.Credito!.Cliente!.IsDeleted &&
                           c.Estado == EstadoCuota.Pendiente &&
                           c.FechaVencimiento > hoy &&
                           c.FechaVencimiento <= proximosDias)
                    .ToListAsync(ct);

                var creditoIds = cuotasPorVencer
                    .Where(c => c.Credito?.Cliente != null)
                    .Select(c => c.CreditoId)
                    .Distinct()
                    .ToList();

                var creditosConAlerta = creditoIds.Count == 0
                    ? new HashSet<int>()
                    : (await _context.AlertasCobranza
                        .AsNoTracking()
                        .Where(a => creditoIds.Contains(a.CreditoId) &&
                                   !a.Resuelta &&
                                   a.Tipo == TipoAlertaCobranza.ProximoVencimiento &&
                                   !a.IsDeleted)
                        .Select(a => a.CreditoId)
                        .Distinct()
                        .ToListAsync(ct))
                        .ToHashSet();

                foreach (var cuota in cuotasPorVencer)
                {
                    if (cuota.Credito?.Cliente == null)
                        continue;

                    // Verificar si ya existe alerta
                    if (creditosConAlerta.Contains(cuota.CreditoId))
                        continue;

                    var diasRestantes = (cuota.FechaVencimiento - hoy).Days;
                    var cliente = cuota.Credito.Cliente;

                    var alerta = new AlertaCobranza
                    {
                        CreditoId = cuota.CreditoId,
                        ClienteId = cliente.Id,
                        Tipo = TipoAlertaCobranza.ProximoVencimiento,
                        Prioridad = PrioridadAlerta.Baja,
                        Mensaje = $"Cliente {cliente.ToDisplayName()} tiene cuota por vencer en {diasRestantes} días",
                        MontoVencido = cuota.MontoTotal,
                        CuotasVencidas = 1,
                        FechaAlerta = now,
                        Resuelta = false,
                        CreatedAt = now
                    };

                    _context.AlertasCobranza.Add(alerta);
                }

                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("Alertas de próximos vencimientos generadas");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al generar alertas de próximos vencimientos");
            }
        }

        /// <summary>
        /// Recalcula y audita el PuntajeCliente de los clientes cuya mora ya superó
        /// ConfiguracionMora.DiasGracia (fechaLimite). Dentro de los días de gracia no se
        /// invoca el recalculo, por lo que el puntaje no se ve afectado todavía.
        /// PUN-ML7 (auditoría, lote 2): agregada la condición de saldo de capital pendiente
        /// (MontoPagado &lt; MontoTotal) — antes la faltaba y una cuota con capital ya saldado pero
        /// Estado=Parcial únicamente por un punitorio aplicado pendiente (ver
        /// EstadoCuotaResolver.Resolver) impactaba el puntaje del cliente como si todavía debiera
        /// capital. Misma condición que EstadoCuotaResolver.EstaEnMoraCapitalDerivado y que el query
        /// de selección de cuotas vencidas de ProcesarMoraAsync, arriba.
        /// </summary>
        private async Task ImpactarScorePorMoraAsync(DateTime fechaLimite, CancellationToken ct)
        {
            var clienteIds = await _context.Cuotas
                .Where(c => !c.IsDeleted &&
                       !c.Credito!.IsDeleted &&
                       !c.Credito!.Cliente!.IsDeleted &&
                       c.Estado != EstadoCuota.Pagada &&
                       c.Estado != EstadoCuota.Cancelada &&
                       c.MontoPagado < c.MontoTotal &&
                       c.FechaVencimiento < fechaLimite)
                .Select(c => c.Credito!.ClienteId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var clienteId in clienteIds)
            {
                await _clienteScoringService.RecalcularYAuditarAsync(
                    clienteId,
                    origen: "RecalculoAutomaticoMora",
                    observacion: "Recalculo automático por mora",
                    registradoPor: "Sistema",
                    ct: ct);
            }
        }

        #endregion

        #region Gestión de Alertas

        public async Task<List<AlertaCobranzaViewModel>> GetAlertasActivasAsync()
        {
            try
            {
                var alertas = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => !a.IsDeleted && !a.Resuelta)
                    .OrderByDescending(a => a.Prioridad)
                    .ThenBy(a => a.FechaAlerta)
                    .ToListAsync();

                return _mapper.Map<List<AlertaCobranzaViewModel>>(alertas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas activas");
                throw;
            }
        }

        public async Task<List<AlertaCobranzaViewModel>> GetTodasAlertasAsync()
        {
            try
            {
                var alertas = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => !a.IsDeleted)
                    .OrderByDescending(a => a.FechaAlerta)
                    .ToListAsync();

                return _mapper.Map<List<AlertaCobranzaViewModel>>(alertas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todas las alertas");
                throw;
            }
        }

        public async Task<AlertaCobranzaViewModel?> GetAlertaByIdAsync(int id)
        {
            try
            {
                var alerta = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);

                return alerta != null ? _mapper.Map<AlertaCobranzaViewModel>(alerta) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alerta {Id}", id);
                throw;
            }
        }

        public async Task<List<AlertaCobranzaViewModel>> GetAlertasPorClienteAsync(int clienteId)
        {
            try
            {
                var alertas = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => a.ClienteId == clienteId && !a.IsDeleted)
                    .OrderByDescending(a => a.FechaAlerta)
                    .ToListAsync();

                return _mapper.Map<List<AlertaCobranzaViewModel>>(alertas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<bool> ResolverAlertaAsync(int id, string? observaciones = null, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.AlertasCobranza.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
                if (alerta == null)
                    return false;

                if (alerta.Resuelta)
                    return true; // idempotente

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                alerta.Resuelta = true;
                alerta.FechaResolucion = DateTime.UtcNow;
                alerta.Observaciones = observaciones;
                alerta.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
                }

                _logger.LogInformation("Alerta {Id} resuelta. Observaciones: {Obs}", id, observaciones ?? "Ninguna");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver alerta {Id}", id);
                throw;
            }
        }

        public async Task<bool> MarcarAlertaComoLeidaAsync(int id, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.AlertasCobranza.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
                if (alerta == null)
                    return false;

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                alerta.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar alerta como leída {Id}", id);
                throw;
            }
        }

        #endregion

        #region Logs

        public async Task<List<LogMora>> GetLogsAsync(int cantidad = 50)
        {
            try
            {
                return await _context.LogsMora
                    .AsNoTracking()
                    .Where(l => !l.IsDeleted)
                    .OrderByDescending(l => l.FechaEjecucion)
                    .Take(cantidad)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener logs de mora");
                throw;
            }
        }

        #endregion

        #region Configuración Expandida

        /// <summary>
        /// Campos editables de la configuración expandida. El nombre es el del campo del formulario
        /// (= propiedad del ViewModel); permite aplicar sólo los campos efectivamente publicados.
        /// </summary>
        private static readonly (string Campo, Action<ConfiguracionMora, ConfiguracionMoraExpandidaViewModel> Aplicar)[] CamposConfiguracionExpandida =
        {
            (nameof(ConfiguracionMoraExpandidaViewModel.TipoTasaMora), (c, v) => c.TipoTasaMora = v.TipoTasaMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.TasaMoraBase), (c, v) => c.TasaMoraBase = v.TasaMoraBase),
            (nameof(ConfiguracionMoraExpandidaViewModel.BaseCalculoMora), (c, v) => c.BaseCalculoMora = v.BaseCalculoMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasGracia), (c, v) => c.DiasGracia = v.DiasGracia),
            (nameof(ConfiguracionMoraExpandidaViewModel.EscalonamientoActivo), (c, v) => c.EscalonamientoActivo = v.EscalonamientoActivo),
            (nameof(ConfiguracionMoraExpandidaViewModel.TasaPrimerMes), (c, v) => c.TasaPrimerMes = v.TasaPrimerMes),
            (nameof(ConfiguracionMoraExpandidaViewModel.TasaSegundoMes), (c, v) => c.TasaSegundoMes = v.TasaSegundoMes),
            (nameof(ConfiguracionMoraExpandidaViewModel.TasaTercerMesEnAdelante), (c, v) => c.TasaTercerMesEnAdelante = v.TasaTercerMesEnAdelante),
            (nameof(ConfiguracionMoraExpandidaViewModel.TopeMaximoMoraActivo), (c, v) => c.TopeMaximoMoraActivo = v.TopeMaximoMoraActivo),
            (nameof(ConfiguracionMoraExpandidaViewModel.TipoTopeMora), (c, v) => c.TipoTopeMora = v.TipoTopeMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.ValorTopeMora), (c, v) => c.ValorTopeMora = v.ValorTopeMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.MoraMinima), (c, v) => c.MoraMinima = v.MoraMinima),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasParaPrioridadMedia), (c, v) => c.DiasParaPrioridadMedia = v.DiasParaPrioridadMedia),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasParaPrioridadAlta), (c, v) => c.DiasParaPrioridadAlta = v.DiasParaPrioridadAlta),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasParaPrioridadCritica), (c, v) => c.DiasParaPrioridadCritica = v.DiasParaPrioridadCritica),
            (nameof(ConfiguracionMoraExpandidaViewModel.MontoParaPrioridadMedia), (c, v) => c.MontoParaPrioridadMedia = v.MontoParaPrioridadMedia),
            (nameof(ConfiguracionMoraExpandidaViewModel.MontoParaPrioridadAlta), (c, v) => c.MontoParaPrioridadAlta = v.MontoParaPrioridadAlta),
            (nameof(ConfiguracionMoraExpandidaViewModel.MontoParaPrioridadCritica), (c, v) => c.MontoParaPrioridadCritica = v.MontoParaPrioridadCritica),
            (nameof(ConfiguracionMoraExpandidaViewModel.ProcesoAutomaticoActivo), (c, v) => c.ProcesoAutomaticoActivo = v.ProcesoAutomaticoActivo),
            (nameof(ConfiguracionMoraExpandidaViewModel.HoraEjecucionDiaria), (c, v) => c.HoraEjecucionDiaria = v.HoraEjecucionDiaria),
            (nameof(ConfiguracionMoraExpandidaViewModel.AlertasPreventivasActivas), (c, v) => c.AlertasPreventivasActivas = v.AlertasPreventivasActivas),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasAntesAlertaPreventiva), (c, v) => c.DiasAntesAlertaPreventiva = v.DiasAntesAlertaPreventiva),
            (nameof(ConfiguracionMoraExpandidaViewModel.CambiarEstadoCuotaAuto), (c, v) => c.CambiarEstadoCuotaAuto = v.CambiarEstadoCuotaAuto),
            (nameof(ConfiguracionMoraExpandidaViewModel.ActualizarMoraAutomaticamente), (c, v) => c.ActualizarMoraAutomaticamente = v.ActualizarMoraAutomaticamente),
            (nameof(ConfiguracionMoraExpandidaViewModel.NotificacionesActivas), (c, v) => c.NotificacionesActivas = v.NotificacionesActivas),
            (nameof(ConfiguracionMoraExpandidaViewModel.WhatsAppActivo), (c, v) => c.WhatsAppActivo = v.WhatsAppActivo),
            (nameof(ConfiguracionMoraExpandidaViewModel.EmailActivo), (c, v) => c.EmailActivo = v.EmailActivo),
            (nameof(ConfiguracionMoraExpandidaViewModel.CanalPreferido), (c, v) => c.CanalPreferido = v.CanalPreferido),
            (nameof(ConfiguracionMoraExpandidaViewModel.NotificarProximoVencimiento), (c, v) => c.NotificarProximoVencimiento = v.NotificarProximoVencimiento),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasAntesNotificacionPreventiva), (c, v) => c.DiasAntesNotificacionPreventiva = v.DiasAntesNotificacionPreventiva),
            (nameof(ConfiguracionMoraExpandidaViewModel.NotificarCuotaVencida), (c, v) => c.NotificarCuotaVencida = v.NotificarCuotaVencida),
            (nameof(ConfiguracionMoraExpandidaViewModel.NotificarMoraAcumulada), (c, v) => c.NotificarMoraAcumulada = v.NotificarMoraAcumulada),
            (nameof(ConfiguracionMoraExpandidaViewModel.FrecuenciaRecordatorioMora), (c, v) => c.FrecuenciaRecordatorioMora = v.FrecuenciaRecordatorioMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.MaximoNotificacionesDiarias), (c, v) => c.MaximoNotificacionesDiarias = v.MaximoNotificacionesDiarias),
            (nameof(ConfiguracionMoraExpandidaViewModel.MaximoNotificacionesPorCuota), (c, v) => c.MaximoNotificacionesPorCuota = v.MaximoNotificacionesPorCuota),
            (nameof(ConfiguracionMoraExpandidaViewModel.HoraInicioEnvio), (c, v) => c.HoraInicioEnvio = v.HoraInicioEnvio),
            (nameof(ConfiguracionMoraExpandidaViewModel.HoraFinEnvio), (c, v) => c.HoraFinEnvio = v.HoraFinEnvio),
            (nameof(ConfiguracionMoraExpandidaViewModel.EnviarFinDeSemana), (c, v) => c.EnviarFinDeSemana = v.EnviarFinDeSemana),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasMaximosSinGestion), (c, v) => c.DiasMaximosSinGestion = v.DiasMaximosSinGestion),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasParaCumplirPromesa), (c, v) => c.DiasParaCumplirPromesa = v.DiasParaCumplirPromesa),
            (nameof(ConfiguracionMoraExpandidaViewModel.MaximoCuotasAcuerdo), (c, v) => c.MaximoCuotasAcuerdo = v.MaximoCuotasAcuerdo),
            (nameof(ConfiguracionMoraExpandidaViewModel.DiasGraciaIncumplimientoAcuerdo), (c, v) => c.DiasGraciaIncumplimientoAcuerdo = v.DiasGraciaIncumplimientoAcuerdo ?? 3),
            (nameof(ConfiguracionMoraExpandidaViewModel.PorcentajeMinimoEntrega), (c, v) => c.PorcentajeMinimoEntrega = v.PorcentajeMinimoEntrega),
            (nameof(ConfiguracionMoraExpandidaViewModel.PermitirCondonacionMora), (c, v) => c.PermitirCondonacionMora = v.PermitirCondonacionMora),
            // Score por mora (FASE 12B): el flag controla si el proceso automático
            // recalcula PuntajeCliente tras los días de gracia; no se mezcla con
            // CambiarEstadoCuotaAuto.
            (nameof(ConfiguracionMoraExpandidaViewModel.ImpactarScorePorMora), (c, v) => c.ImpactarScorePorMora = v.ImpactarScorePorMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.PuntosRestarPorCuotaVencida), (c, v) => c.PuntosRestarPorCuotaVencida = v.PuntosRestarPorCuotaVencida),
            (nameof(ConfiguracionMoraExpandidaViewModel.PuntosRestarPorDiaMora), (c, v) => c.PuntosRestarPorDiaMora = v.PuntosRestarPorDiaMora),
            (nameof(ConfiguracionMoraExpandidaViewModel.PuntosMaximosARestar), (c, v) => c.PuntosMaximosARestar = v.PuntosMaximosARestar),
            (nameof(ConfiguracionMoraExpandidaViewModel.RecuperarScoreAlPagar), (c, v) => c.RecuperarScoreAlPagar = v.RecuperarScoreAlPagar),
            (nameof(ConfiguracionMoraExpandidaViewModel.PorcentajeRecuperacionScore), (c, v) => c.PorcentajeRecuperacionScore = v.PorcentajeRecuperacionScore),
        };

        public async Task<ConfiguracionMora> UpdateConfiguracionExpandidaAsync(
            ConfiguracionMoraExpandidaViewModel viewModel,
            IEnumerable<string>? camposEnviados = null)
        {
            try
            {
                var config = await _context.ConfiguracionesMora
                    .FirstOrDefaultAsync(c => c.Id == viewModel.Id && !c.IsDeleted);
                if (config == null)
                    throw new InvalidOperationException("Configuración no encontrada");

                // Concurrencia optimista: sin la versión que el administrador vio al abrir el
                // formulario no se puede saber si está guardando sobre cambios de otro.
                // Sólo las llamadas internas con el ViewModel completo (camposEnviados == null)
                // pueden omitirla; cualquier POST de formulario la exige.
                if (viewModel.RowVersion is { Length: > 0 })
                    _context.Entry(config).Property(c => c.RowVersion).OriginalValue = viewModel.RowVersion;
                else if (camposEnviados != null)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la configuración e intentá nuevamente.");

                // Sólo se aplican los campos publicados: un campo ausente del formulario llega
                // con el default del binder (null/false) y no debe pisar el valor persistido.
                // Sin lista explícita (llamadas internas con el ViewModel completo) se aplican todos.
                var enviados = camposEnviados == null
                    ? null
                    : new HashSet<string>(camposEnviados, StringComparer.OrdinalIgnoreCase);

                foreach (var (campo, aplicar) in CamposConfiguracionExpandida)
                {
                    if (enviados == null || enviados.Contains(campo))
                        aplicar(config, viewModel);
                }

                config.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Descarta la entidad obsoleta del contexto: nada de lo aplicado se persistió.
                    _context.Entry(config).State = EntityState.Detached;
                    _configuracion = null;
                    throw new InvalidOperationException("La configuración fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
                }
                _configuracion = null;

                _logger.LogInformation("Configuración expandida de mora actualizada");
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar configuración expandida");
                throw;
            }
        }

        #endregion

        #region Bandeja de Clientes en Mora

        public async Task<BandejaClientesMoraViewModel> GetClientesEnMoraAsync(FiltrosBandejaClientes filtros)
        {
            try
            {
                // PUN-ML7 (auditoría, lote 2): antes DateTime.Today. No decide "mora de capital" acá
                // (eso ya lo resolvió ProcesarMoraAsync al generar AlertaCobranza con el predicado
                // canónico) — esta fecha solo decide vigencia de promesas/próximo contacto.
                var hoy = _reloj.InicioDiaComercial;

                // Base query: clientes con alertas activas
                var queryBase = _context.AlertasCobranza
                    .AsNoTracking()
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => !a.IsDeleted && !a.Resuelta && a.Tipo != TipoAlertaCobranza.ProximoVencimiento
                                && a.Cliente != null && !a.Cliente.IsDeleted)
                    .AsQueryable();

                // Materializar alertas con datos de cliente para poder usar ToDisplayName() en memoria
                var alertasRaw = await queryBase
                    .Select(a => new
                    {
                        a.ClienteId,
                        ClienteNombre = a.Cliente!.Apellido + ", " + a.Cliente.Nombre + " - DNI: " + a.Cliente.NumeroDocumento,
                        ClienteDocumento = a.Cliente.NumeroDocumento,
                        ClienteTelefono = a.Cliente.Telefono,
                        ClienteEmail = a.Cliente.Email,
                        a.CreditoId,
                        a.CuotasVencidas,
                        a.DiasAtraso,
                        a.MontoVencido,
                        a.MontoMoraCalculada,
                        a.Prioridad,
                        a.EstadoGestion,
                        a.FechaPromesaPago,
                        a.FechaAlerta
                    })
                    .ToListAsync();

                // Agrupar por cliente en memoria con nombre ya calculado
                var clientesData = alertasRaw
                    .GroupBy(a => new { a.ClienteId, a.ClienteNombre, a.ClienteDocumento, a.ClienteTelefono, a.ClienteEmail })
                    .Select(g => new
                    {
                        ClienteId = g.Key.ClienteId,
                        Nombre = g.Key.ClienteNombre,
                        Documento = g.Key.ClienteDocumento,
                        Telefono = g.Key.ClienteTelefono,
                        Email = g.Key.ClienteEmail,
                        CreditosConMora = g.Select(a => a.CreditoId).Distinct().Count(),
                        CuotasVencidas = g.Sum(a => a.CuotasVencidas),
                        DiasMaxAtraso = g.Max(a => a.DiasAtraso),
                        MontoVencido = g.Sum(a => a.MontoVencido),
                        MontoMora = g.Sum(a => a.MontoMoraCalculada),
                        PrioridadMaxima = g.Max(a => a.Prioridad),
                        EstadoGestion = g.Max(a => a.EstadoGestion),
                        AlertasActivas = g.Count(),
                        TienePromesaActiva = g.Any(a => a.FechaPromesaPago != null && a.FechaPromesaPago >= hoy),
                        FechaPromesa = g.Where(a => a.FechaPromesaPago >= hoy).Max(a => a.FechaPromesaPago),
                        TieneAcuerdoActivo = g.Any(a => a.EstadoGestion == EstadoGestionCobranza.AcuerdoActivo),
                        FechaUltimaAlerta = g.Max(a => a.FechaAlerta)
                    })
                    .ToList();

                // Obtener último contacto y próximo contacto por cliente
                var clienteIds = clientesData.Select(c => c.ClienteId).ToList();
                var contactosData = await _context.HistorialContactos
                    .Where(h => clienteIds.Contains(h.ClienteId) && !h.IsDeleted)
                    .GroupBy(h => h.ClienteId)
                    .Select(g => new
                    {
                        ClienteId = g.Key,
                        UltimoContacto = g.Max(h => h.FechaContacto),
                        ProximoContacto = g.Where(h => h.ProximoContacto >= hoy).Min(h => h.ProximoContacto),
                        TotalContactos = g.Count()
                    })
                    .ToListAsync();

                var contactosDict = contactosData.ToDictionary(c => c.ClienteId);

                // Mapear a ViewModel
                var clientes = clientesData.Select(c =>
                {
                    contactosDict.TryGetValue(c.ClienteId, out var contacto);
                    return new ClienteMoraViewModel
                    {
                        ClienteId = c.ClienteId,
                        Nombre = c.Nombre ?? "",
                        Documento = c.Documento ?? "",
                        Telefono = c.Telefono,
                        Email = c.Email,
                        CreditosConMora = c.CreditosConMora,
                        CuotasVencidas = c.CuotasVencidas,
                        DiasMaxAtraso = c.DiasMaxAtraso,
                        MontoVencido = c.MontoVencido,
                        MontoMora = c.MontoMora,
                        PrioridadMaxima = c.PrioridadMaxima,
                        EstadoGestion = c.EstadoGestion,
                        AlertasActivas = c.AlertasActivas,
                        UltimoContacto = contacto?.UltimoContacto,
                        ProximoContacto = contacto?.ProximoContacto,
                        TienePromesaActiva = c.TienePromesaActiva,
                        FechaPromesa = c.FechaPromesa,
                        TieneAcuerdoActivo = c.TieneAcuerdoActivo,
                        TotalContactos = contacto?.TotalContactos ?? 0,
                        FechaUltimaAlerta = c.FechaUltimaAlerta
                    };
                }).ToList();

                // Aplicar filtros y ordenamiento
                clientes = AplicarFiltros(clientes, filtros, hoy);
                clientes = AplicarOrdenamiento(clientes, filtros.Ordenamiento);

                var totalClientes = clientes.Count;

                // Paginación
                var clientesPaginados = clientes
                    .Skip((filtros.Pagina - 1) * filtros.TamañoPagina)
                    .Take(filtros.TamañoPagina)
                    .ToList();

                return new BandejaClientesMoraViewModel
                {
                    Clientes = clientesPaginados,
                    Filtros = filtros,
                    TotalClientes = totalClientes,
                    MontoTotalVencido = clientes.Sum(c => c.MontoVencido),
                    MontoTotalMora = clientes.Sum(c => c.MontoMora),
                    ClientesCriticos = clientes.Count(c => c.PrioridadMaxima == PrioridadAlerta.Critica),
                    ClientesConPromesa = clientes.Count(c => c.TienePromesaActiva),
                    ClientesConAcuerdo = clientes.Count(c => c.TieneAcuerdoActivo)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener clientes en mora");
                throw;
            }
        }

        public async Task<Dictionary<string, int>> GetConteoPorPrioridadAsync()
        {
            try
            {
                var conteos = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Where(a => !a.IsDeleted && !a.Resuelta)
                    .GroupBy(a => a.Prioridad)
                    .Select(g => new { Prioridad = g.Key, Cantidad = g.Select(a => a.ClienteId).Distinct().Count() })
                    .ToListAsync();

                return new Dictionary<string, int>
                {
                    ["Critica"] = conteos.FirstOrDefault(c => c.Prioridad == PrioridadAlerta.Critica)?.Cantidad ?? 0,
                    ["Alta"] = conteos.FirstOrDefault(c => c.Prioridad == PrioridadAlerta.Alta)?.Cantidad ?? 0,
                    ["Media"] = conteos.FirstOrDefault(c => c.Prioridad == PrioridadAlerta.Media)?.Cantidad ?? 0,
                    ["Baja"] = conteos.FirstOrDefault(c => c.Prioridad == PrioridadAlerta.Baja)?.Cantidad ?? 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener conteo por prioridad");
                throw;
            }
        }

        #endregion

        #region Ficha de Cliente

        public async Task<FichaMoraViewModel?> GetFichaClienteAsync(int clienteId)
        {
            try
            {
                var cliente = await _context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

                if (cliente == null)
                    return null;

                // PUN-ML7 (auditoría, lote 2): antes DateTime.Today. InicioDiaComercial es DateTime
                // a medianoche, comparable directamente contra HistorialContacto.ProximoContacto.
                var fechaComercial = _reloj.InicioDiaComercial;

                // Obtener alertas activas
                var alertas = await GetAlertasPorClienteAsync(clienteId);
                var alertasActivas = alertas.Where(a => !a.Resuelta).ToList();

                // Obtener créditos en mora
                var creditos = await GetCreditosEnMoraAsync(clienteId);

                // Obtener historial de contactos
                var historial = await GetHistorialContactosAsync(clienteId);

                // Obtener promesas activas
                var promesas = await GetPromesasActivasAsync(clienteId);

                // Obtener acuerdos
                var acuerdos = await GetAcuerdosPagoAsync(clienteId);

                // Calcular resumen
                var creditosParaAcuerdo = await GetCreditosParaAcuerdoAsync(clienteId);

                var resumen = new ResumenMoraViewModel
                {
                    TotalCreditosConMora = creditos.Count,
                    TotalCuotasVencidas = creditos.Sum(c => c.CuotasVencidas),
                    DiasMaxAtraso = creditos.Any() ? creditos.Max(c => c.DiasAtraso) : 0,
                    MontoCapitalVencido = creditos.Sum(c => c.MontoCuotasVencidas),
                    MontoMoraAcumulada = creditos.Sum(c => c.MontoMora),
                    PrioridadMaxima = alertasActivas.Any() ? alertasActivas.Max(a => a.Prioridad) : PrioridadAlerta.Baja,
                    // Determinar estado de gestión basado en alertas activas
                    // Nota: TipoNombre contiene TipoAlertaCobranza, no EstadoGestionCobranza
                    EstadoGestion = alertasActivas.Any() 
                        ? EstadoGestionCobranza.EnGestion 
                        : EstadoGestionCobranza.Pendiente,
                    ContactosRealizados = historial.Count,
                    UltimoContacto = historial.OrderByDescending(h => h.FechaContacto).FirstOrDefault()?.FechaContacto,
                    ProximoContacto = historial.Where(h => h.ProximoContacto >= fechaComercial).OrderBy(h => h.ProximoContacto).FirstOrDefault()?.ProximoContacto,
                    PromesaActiva = promesas.Any(),
                    AcuerdoActivo = acuerdos.Any(a => a.Estado == EstadoAcuerdo.Activo)
                };

                return new FichaMoraViewModel
                {
                    ClienteId = clienteId,
                    NombreCliente = cliente.ToDisplayName(),
                    DocumentoCliente = cliente.NumeroDocumento ?? "",
                    Telefono = cliente.Telefono,
                    TelefonoLaboral = cliente.TelefonoLaboral,
                    Email = cliente.Email,
                    Direccion = cliente.Direccion,
                    Resumen = resumen,
                    Creditos = creditos,
                    CreditosParaAcuerdo = creditosParaAcuerdo,
                    AlertasActivas = alertasActivas,
                    HistorialContactos = historial,
                    PromesasPago = promesas,
                    Acuerdos = acuerdos
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener ficha del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        // Créditos con deuda cobrable para un acuerdo: mora de capital más deuda solo de punitorio aplicado. La clasificación de
        // mora de capital (GetCreditosEnMoraAsync, PUN-ML7) no se toca: esto es una lista aparte, usada solo por el acuerdo.
        private async Task<List<CreditoAcuerdoViewModel>> GetCreditosParaAcuerdoAsync(int clienteId)
        {
            var hoy = _reloj.HoyComercial;
            var creditos = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cuotas)
                .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
                .ToListAsync();

            var cuotaIds = creditos.SelectMany(c => c.Cuotas.Where(q => !q.IsDeleted).Select(q => q.Id)).ToList();
            var conPunitorioPendiente = (await _context.PunitoriosAplicados
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Estado == EstadoPunitorioAplicado.Aplicado && cuotaIds.Contains(p.CuotaId))
                .Select(p => p.CuotaId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            var resultado = new List<CreditoAcuerdoViewModel>();
            foreach (var credito in creditos)
            {
                var candidato = credito.Cuotas.Any(c => !c.IsDeleted
                    && (EstadoCuotaResolver.EstaEnMoraCapitalDerivado(c.Estado, c.MontoPagado, c.MontoTotal, c.FechaVencimiento, hoy)
                        || (conPunitorioPendiente.Contains(c.Id) && c.Estado != EstadoCuota.Cancelada)));
                if (!candidato)
                    continue;

                var item = new CreditoAcuerdoViewModel { CreditoId = credito.Id, NumeroCredito = credito.Numero ?? credito.Id.ToString() };
                try
                {
                    var deuda = await CalcularDesgloseDeudaCobrableCreditoAsync(credito.Id);
                    if (deuda.Total <= 0m)
                        continue;
                    item.Capital = deuda.Capital;
                    item.Punitorio = deuda.Punitorio;
                    item.PunitorioCalculadoSinAplicar = await CalcularPunitorioCalculadoSinAplicarAsync(credito.Id);
                }
                catch (Exception ex)
                {
                    // Sin cifra autoritativa no se ofrece un importe: queda "a conciliar" y el servicio rechaza crearlo.
                    _logger.LogWarning(ex, "No se pudo determinar la deuda cobrable del crédito {CreditoId}", credito.Id);
                }
                resultado.Add(item);
            }
            return resultado;
        }

        public async Task<List<CreditoMoraViewModel>> GetCreditosEnMoraAsync(int clienteId)
        {
            try
            {
                // PUN-ML7 (auditoría, lote 2): antes filtraba solo Estado==Pendiente, perdiendo
                // cuotas Vencida/Parcial con saldo de capital. Ahora usa el predicado canónico
                // (EstadoCuotaResolver.EstaEnMoraCapitalDerivado), que además excluye correctamente
                // una cuota con capital saldado y solo punitorio aplicado pendiente (Estado=Parcial
                // por Resolver, pero MontoPagado>=MontoTotal). credito.Cuotas ya está materializado en
                // memoria por el Include de abajo, así que este Where es LINQ-to-Objects, no EF.
                var fechaComercial = _reloj.HoyComercial;
                var creditos = await _context.Creditos
                    .AsNoTracking()
                    .Include(c => c.Cuotas)
                    .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
                    .ToListAsync();

                var resultado = new List<CreditoMoraViewModel>();

                foreach (var credito in creditos)
                {
                    var cuotasVencidas = credito.Cuotas?
                        .Where(c => !c.IsDeleted && EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
                            c.Estado, c.MontoPagado, c.MontoTotal, c.FechaVencimiento, fechaComercial))
                        .ToList() ?? new List<Cuota>();

                    if (!cuotasVencidas.Any())
                        continue;

                    var cuotasDetalle = cuotasVencidas.Select(c => new CuotaVencidaViewModel
                    {
                        CuotaId = c.Id,
                        NumeroCuota = c.NumeroCuota,
                        FechaVencimiento = c.FechaVencimiento,
                        MontoCapital = c.MontoCapital,
                        MontoInteres = c.MontoInteres,
                        MontoTotal = c.MontoTotal,
                        MontoPagado = c.MontoPagado,
                        // MontoMora: valor legacy (Cuota.MontoPunitorio), congelado desde PUN-ML6 —
                        // fuera de alcance de esta corrección (no se toca fórmula de punitorios).
                        MontoMora = c.MontoPunitorio,
                        DiasAtraso = EstadoCuotaResolver.DiasAtrasoDerivado(c.Estado, c.FechaVencimiento, fechaComercial)
                    }).ToList();

                    var cuotasPagadas = credito.Cuotas?.Count(c => c.Estado == EstadoCuota.Pagada) ?? 0;
                    resultado.Add(new CreditoMoraViewModel
                    {
                        CreditoId = credito.Id,
                        NumeroCredito = credito.Numero ?? credito.Id.ToString(),
                        FechaOtorgamiento = credito.FechaSolicitud,
                        MontoOriginal = credito.MontoSolicitado,
                        SaldoActual = credito.SaldoPendiente,
                        TotalCuotas = credito.CantidadCuotas,
                        CuotasPagadas = cuotasPagadas,
                        CuotasVencidas = cuotasVencidas.Count,
                        DiasAtraso = cuotasVencidas.Max(c => EstadoCuotaResolver.DiasAtrasoDerivado(c.Estado, c.FechaVencimiento, fechaComercial)),
                        MontoCuotasVencidas = cuotasVencidas.Sum(c => c.MontoTotal - c.MontoPagado),
                        MontoMora = cuotasVencidas.Sum(c => c.MontoPunitorio),
                        CuotasDetalle = cuotasDetalle
                    });
                }

                return resultado.OrderByDescending(c => c.DiasAtraso).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener créditos en mora del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        #endregion

        #region Gestión de Contactos

        public async Task<bool> RegistrarContactoAsync(RegistrarContactoViewModel contacto, string gestorId)
        {
            try
            {
                // El historial cuelga de una alerta (FK obligatoria): sin alerta del cliente no hay a qué asociarlo.
                if (!contacto.AlertaId.HasValue)
                    throw new InvalidOperationException("Elegí la alerta de mora a la que corresponde el contacto.");
                await ValidarAlertaDelClienteAsync(contacto.AlertaId.Value, contacto.ClienteId);

                var historial = new HistorialContacto
                {
                    ClienteId = contacto.ClienteId,
                    AlertaCobranzaId = contacto.AlertaId.Value,
                    GestorId = gestorId,
                    FechaContacto = DateTime.UtcNow,
                    TipoContacto = contacto.TipoContacto,
                    Resultado = contacto.Resultado,
                    Telefono = contacto.Telefono,
                    Email = contacto.Email,
                    Observaciones = contacto.Observaciones,
                    DuracionMinutos = contacto.DuracionMinutos,
                    ProximoContacto = contacto.ProximoContacto,
                    FechaPromesaPago = contacto.FechaPromesaPago,
                    MontoPromesaPago = contacto.MontoPromesaPago,
                    CreatedAt = DateTime.UtcNow
                };

                _context.HistorialContactos.Add(historial);

                // Si hay promesa de pago, actualizar la alerta
                if (contacto.Resultado == ResultadoContacto.PromesaPago && 
                    contacto.AlertaId.HasValue && 
                    contacto.FechaPromesaPago.HasValue)
                {
                    var alerta = await _context.AlertasCobranza
                        .FirstOrDefaultAsync(a => a.Id == contacto.AlertaId.Value && !a.IsDeleted);
                    if (alerta != null)
                    {
                        alerta.EstadoGestion = EstadoGestionCobranza.PromesaPago;
                        alerta.FechaPromesaPago = contacto.FechaPromesaPago;
                        alerta.MontoPromesaPago = contacto.MontoPromesaPago;
                        alerta.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Contacto registrado para cliente {ClienteId}", contacto.ClienteId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar contacto para cliente {ClienteId}", contacto.ClienteId);
                throw;
            }
        }

        public async Task<List<HistorialContactoViewModel>> GetHistorialContactosAsync(int clienteId)
        {
            try
            {
                var historial = await _context.HistorialContactos
                    .AsNoTracking()
                    .Where(h => h.ClienteId == clienteId && !h.IsDeleted)
                    .OrderByDescending(h => h.FechaContacto)
                    .ToListAsync();

                return historial.Select(h => new HistorialContactoViewModel
                {
                    Id = h.Id,
                    FechaContacto = h.FechaContacto,
                    TipoContacto = h.TipoContacto,
                    Resultado = h.Resultado,
                    Observaciones = h.Observaciones,
                    GestorNombre = h.GestorId,
                    Telefono = h.Telefono,
                    Email = h.Email,
                    DuracionMinutos = h.DuracionMinutos,
                    FechaPromesaPago = h.FechaPromesaPago,
                    MontoPromesaPago = h.MontoPromesaPago,
                    ProximoContacto = h.ProximoContacto
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener historial de contactos del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        #endregion

        #region Promesas de Pago

        public async Task<bool> RegistrarPromesaPagoAsync(RegistrarPromesaViewModel promesa, string gestorId)
        {
            try
            {
                var existe = await _context.AlertasCobranza.AnyAsync(a => a.Id == promesa.AlertaId && !a.IsDeleted);
                if (!existe)
                    return false;
                var alerta = await ValidarAlertaDelClienteAsync(promesa.AlertaId, promesa.ClienteId);

                alerta.EstadoGestion = EstadoGestionCobranza.PromesaPago;
                alerta.FechaPromesaPago = promesa.FechaPromesa;
                alerta.MontoPromesaPago = promesa.MontoPromesa;
                alerta.Observaciones = promesa.Observaciones;
                alerta.UpdatedAt = DateTime.UtcNow;

                // Registrar en historial
                var historial = new HistorialContacto
                {
                    ClienteId = promesa.ClienteId,
                    AlertaCobranzaId = promesa.AlertaId,
                    GestorId = gestorId,
                    FechaContacto = DateTime.UtcNow,
                    TipoContacto = TipoContacto.NotaInterna,
                    Resultado = ResultadoContacto.PromesaPago,
                    Observaciones = promesa.Observaciones,
                    FechaPromesaPago = promesa.FechaPromesa,
                    MontoPromesaPago = promesa.MontoPromesa,
                    CreatedAt = DateTime.UtcNow
                };

                _context.HistorialContactos.Add(historial);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Promesa de pago registrada - Alerta {AlertaId}, Fecha: {Fecha}, Monto: {Monto}",
                    promesa.AlertaId, promesa.FechaPromesa, promesa.MontoPromesa);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar promesa de pago");
                throw;
            }
        }

        public async Task<List<PromesaPagoViewModel>> GetPromesasActivasAsync(int clienteId)
        {
            try
            {
                // PUN-ML7 (auditoría, lote 2): este método no decide mora ni vencimiento de deuda,
                // solo vigencia de la promesa ("activa" = todavía no pasó su fecha). Antes "hoy" se
                // declaraba con DateTime.Today pero nunca se conectaba al query (variable muerta) —
                // devolvía cualquier promesa no resuelta, incluida una incumplida hace meses sin
                // marcar Cumplida/Incumplida. Se cablea con el mismo criterio de vigencia que ya usa
                // GetClientesEnMoraAsync (TienePromesaActiva/FechaPromesa: FechaPromesaPago >= hoy).
                var hoy = _reloj.InicioDiaComercial;
                var alertasConPromesa = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Where(a => a.ClienteId == clienteId &&
                           !a.IsDeleted &&
                           !a.Resuelta &&
                           a.FechaPromesaPago != null &&
                           a.FechaPromesaPago >= hoy)
                    .ToListAsync();

                return alertasConPromesa.Select(a => new PromesaPagoViewModel
                {
                    AlertaId = a.Id,
                    FechaPromesa = a.FechaPromesaPago!.Value,
                    MontoPrometido = a.MontoPromesaPago ?? 0,
                    FechaRegistro = a.UpdatedAt ?? a.CreatedAt
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener promesas activas del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<bool> MarcarPromesaCumplidaAsync(int alertaId)
        {
            try
            {
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);
                if (alerta == null)
                    return false;

                alerta.EstadoGestion = EstadoGestionCobranza.Regularizado;
                alerta.Resuelta = true;
                alerta.FechaResolucion = DateTime.UtcNow;
                alerta.MotivoResolucion = "Promesa de pago cumplida";
                alerta.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Promesa cumplida - Alerta {AlertaId}", alertaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como cumplida");
                throw;
            }
        }

        public async Task<bool> MarcarPromesaIncumplidaAsync(int alertaId, string? observaciones)
        {
            try
            {
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);
                if (alerta == null)
                    return false;

                alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
                alerta.FechaPromesaPago = null;
                alerta.MontoPromesaPago = null;
                alerta.Observaciones = (alerta.Observaciones ?? "") + $" | Promesa incumplida: {observaciones}";
                alerta.UpdatedAt = DateTime.UtcNow;

                // Escalar prioridad
                if (alerta.Prioridad < PrioridadAlerta.Critica)
                    alerta.Prioridad++;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Promesa incumplida - Alerta {AlertaId}", alertaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar promesa como incumplida");
                throw;
            }
        }

        /// <summary>
        /// Semántica (conservadora) de <c>DiasParaCumplirPromesa</c>: días de espera DESPUÉS de la fecha prometida ("días máximos
        /// para esperar cumplimiento"). La promesa incumple cuando hoyComercial &gt; fechaPrometida + días (el último día de
        /// espera todavía vale). Sin el valor configurado no se hace nada.
        /// </summary>
        internal static bool PromesaVencidaParaIncumplir(DateTime fechaPromesa, int? diasParaCumplir, DateOnly hoyComercial) =>
            diasParaCumplir.HasValue && diasParaCumplir.Value >= 0
            && hoyComercial > DateOnly.FromDateTime(fechaPromesa).AddDays(diasParaCumplir.Value);

        public async Task<ResultadoVerificacionPromesas> VerificarPromesasVencidasAsync(CancellationToken cancellationToken = default)
        {
            var dias = (await GetConfiguracionAsync()).DiasParaCumplirPromesa;
            if (!dias.HasValue)
                return new ResultadoVerificacionPromesas(0, 0);

            var hoy = _reloj.HoyComercial;
            var candidatas = await _context.AlertasCobranza
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.EstadoGestion == EstadoGestionCobranza.PromesaPago && a.FechaPromesaPago != null)
                .Select(a => new { a.Id, Fecha = a.FechaPromesaPago!.Value })
                .ToListAsync(cancellationToken);

            var marcadas = 0;
            var fallidas = 0;
            foreach (var c in candidatas.Where(c => PromesaVencidaParaIncumplir(c.Fecha, dias, hoy)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _context.ChangeTracker.Clear();
                    var alerta = await _context.AlertasCobranza
                        .FirstOrDefaultAsync(a => a.Id == c.Id && !a.IsDeleted, cancellationToken);
                    // Relectura: si ya se cumplió o cerró entre la consulta y ahora, no se toca.
                    if (alerta == null || alerta.EstadoGestion != EstadoGestionCobranza.PromesaPago
                        || alerta.FechaPromesaPago == null
                        || !PromesaVencidaParaIncumplir(alerta.FechaPromesaPago.Value, dias, hoy))
                        continue;

                    var fechaPrometida = alerta.FechaPromesaPago.Value;
                    alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
                    alerta.FechaPromesaPago = null;
                    alerta.MontoPromesaPago = null;
                    alerta.Observaciones = (alerta.Observaciones ?? "")
                        + $" | Promesa incumplida automáticamente (prometida {fechaPrometida:dd/MM/yyyy}, {dias} días de espera)";
                    alerta.UpdatedAt = DateTime.UtcNow;
                    if (alerta.Prioridad < PrioridadAlerta.Critica)
                        alerta.Prioridad++;

                    // Estado y auditoría se confirman juntos.
                    _auditoria?.AgregarEvento("Mora", "PromesaIncumplidaAutomatica", $"AlertaCobranza:{alerta.Id}",
                        $"PromesaPago -> EnGestion. Prometida {fechaPrometida:dd/MM/yyyy}; {dias} días de espera vencidos al {hoy:dd/MM/yyyy}.");
                    await _context.SaveChangesAsync(cancellationToken);
                    marcadas++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _context.ChangeTracker.Clear();
                    fallidas++;
                    _logger.LogError(ex, "No se pudo marcar la promesa vencida de la alerta {AlertaId}", c.Id);
                }
            }

            _context.ChangeTracker.Clear();
            return new ResultadoVerificacionPromesas(marcadas, fallidas);
        }

        #endregion

        #region Acuerdos de Pago

        // Interno a propósito: persiste los importes que recibe. La única entrada pública es CrearAcuerdoPagoParaCreditoAsync,
        // que los toma de la deuda real; esto queda visible solo para las pruebas del servicio.
        internal async Task<int> CrearAcuerdoPagoAsync(CrearAcuerdoViewModel acuerdo, string gestorId)
        {
            try
            {
                var config = await GetConfiguracionAsync();

                if (acuerdo.AlertaId.HasValue)
                    await ValidarAlertaDelClienteAsync(acuerdo.AlertaId.Value, acuerdo.ClienteId, acuerdo.CreditoId);

                // Importes y cuotas coherentes antes de cualquier división o comparación con la configuración.
                if (acuerdo.CantidadCuotas <= 0)
                    throw new InvalidOperationException("La cantidad de cuotas debe ser mayor a cero");
                if (acuerdo.MontoDeudaOriginal < 0 || acuerdo.MontoMoraOriginal < 0
                    || acuerdo.MontoCondonar < 0 || acuerdo.MontoEntregaInicial < 0)
                    throw new InvalidOperationException("Los importes del acuerdo no pueden ser negativos");
                if (acuerdo.MontoTotalAcuerdo <= 0)
                    throw new InvalidOperationException("El total del acuerdo debe ser mayor a cero");
                // Regla de negocio: un acuerdo financia la deuda completa (capital + punitorio exigible) en cuotas; nunca la
                // perdona ni la descuenta. No depende de ninguna configuración.
                if (acuerdo.MontoCondonar > 0)
                    throw new InvalidOperationException("Los acuerdos de pago no admiten condonación ni descuentos: la deuda se financia completa.");
                if (acuerdo.MontoEntregaInicial > acuerdo.MontoTotalAcuerdo)
                    throw new InvalidOperationException("La entrega inicial no puede superar el total del acuerdo");

                // Validar máximo de cuotas
                if (config.MaximoCuotasAcuerdo.HasValue && acuerdo.CantidadCuotas > config.MaximoCuotasAcuerdo.Value)
                    throw new InvalidOperationException($"El máximo de cuotas permitido es {config.MaximoCuotasAcuerdo.Value}");

                // Validar entrega mínima
                if (config.PorcentajeMinimoEntrega.HasValue)
                {
                    var porcentajeEntrega = (acuerdo.MontoEntregaInicial / acuerdo.MontoTotalAcuerdo) * 100;
                    if (porcentajeEntrega < config.PorcentajeMinimoEntrega.Value)
                        throw new InvalidOperationException($"La entrega inicial debe ser al menos {config.PorcentajeMinimoEntrega.Value}% del total");
                }

                var nuevoAcuerdo = new AcuerdoPago
                {
                    AlertaCobranzaId = acuerdo.AlertaId,
                    ClienteId = acuerdo.ClienteId,
                    CreditoId = acuerdo.CreditoId,
                    NumeroAcuerdo = $"ACU-{DateTime.UtcNow:yyyyMMddHHmmss}-{acuerdo.ClienteId}-{acuerdo.CreditoId}",
                    FechaCreacion = DateTime.UtcNow,
                    Estado = EstadoAcuerdo.Borrador,
                    MontoDeudaOriginal = acuerdo.MontoDeudaOriginal,
                    MontoMoraOriginal = acuerdo.MontoMoraOriginal,
                    MontoCondonado = acuerdo.MontoCondonar,
                    MontoTotalAcuerdo = acuerdo.MontoTotalAcuerdo,
                    MontoEntregaInicial = acuerdo.MontoEntregaInicial,
                    CantidadCuotas = acuerdo.CantidadCuotas,
                    FechaPrimeraCuota = acuerdo.FechaPrimeraCuota,
                    MontoCuotaAcuerdo = (acuerdo.MontoTotalAcuerdo - acuerdo.MontoEntregaInicial) / acuerdo.CantidadCuotas,
                    CreatedAt = DateTime.UtcNow
                };

                _context.AcuerdosPago.Add(nuevoAcuerdo);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsViolacionAcuerdoVigente(ex))
                {
                    _context.Entry(nuevoAcuerdo).State = EntityState.Detached;
                    throw new InvalidOperationException("Ese crédito ya tiene un acuerdo vigente. Cancelalo antes de crear otro.");
                }

                _logger.LogInformation("Acuerdo de pago creado - {NumeroAcuerdo} para cliente {ClienteId}",
                    nuevoAcuerdo.NumeroAcuerdo, acuerdo.ClienteId);

                return nuevoAcuerdo.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear acuerdo de pago");
                throw;
            }
        }

        public async Task<int> CrearAcuerdoPagoParaCreditoAsync(CrearAcuerdoViewModel solicitud, string gestorId)
        {
            ArgumentNullException.ThrowIfNull(solicitud);

            // Los importes salen de las cuotas reales del crédito, nunca de la solicitud: un importe enviado a mano
            // abarataría el acuerdo (o lo inflaría) sin respaldo en la deuda.
            if (solicitud.MontoCondonar != 0m)
                throw new InvalidOperationException("Los acuerdos de pago no admiten condonación ni descuentos: la deuda se financia completa.");

            var creditoDelCliente = await _context.Creditos
                .AsNoTracking()
                .AnyAsync(c => c.Id == solicitud.CreditoId && c.ClienteId == solicitud.ClienteId && !c.IsDeleted);
            if (!creditoDelCliente)
                throw new InvalidOperationException("El crédito no existe o no pertenece a este cliente.");

            if (solicitud.FechaPrimeraCuota.Date < _reloj.InicioDiaComercial.Date)
                throw new InvalidOperationException("La fecha de la primera cuota no puede estar en el pasado.");

            var deuda = await CalcularDesgloseDeudaCobrableCreditoAsync(solicitud.CreditoId);
            if (deuda.Total <= 0m)
                throw new InvalidOperationException("El crédito no tiene deuda vencida para financiar.");

            solicitud.MontoDeudaOriginal = deuda.Capital;
            solicitud.MontoMoraOriginal = deuda.Punitorio;

            // La alerta no la elige el cliente HTTP: es la alerta de mora activa del mismo crédito y cliente, si existe. Una
            // deuda solo de punitorio aplicado no tiene alerta de capital y el acuerdo se crea sin ella (no se inventa una).
            solicitud.AlertaId = await _context.AlertasCobranza
                .AsNoTracking()
                .Where(a => !a.IsDeleted && !a.Resuelta
                            && a.CreditoId == solicitud.CreditoId && a.ClienteId == solicitud.ClienteId
                            && (a.Tipo == TipoAlertaCobranza.CuotaVencida || a.Tipo == TipoAlertaCobranza.MoraElevada))
                .OrderByDescending(a => a.FechaAlerta)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync();

            var id = await CrearAcuerdoPagoAsync(solicitud, gestorId);
            await AuditarAsync("CrearAcuerdoPago", id, $"Crédito {solicitud.CreditoId}: capital {deuda.Capital:N2} + punitorio {deuda.Punitorio:N2}, entrega {solicitud.MontoEntregaInicial:N2}, {solicitud.CantidadCuotas} cuotas.");
            return id;
        }

        public async Task<bool> MarcarPromesaIncumplidaDelClienteAsync(int alertaId, int clienteId, string? observaciones)
        {
            var alerta = await ValidarAlertaDelClienteAsync(alertaId, clienteId);
            if (alerta.FechaPromesaPago == null)
                throw new InvalidOperationException("La alerta no tiene una promesa de pago vigente.");
            return await MarcarPromesaIncumplidaAsync(alertaId, observaciones);
        }

        public async Task<bool> MarcarPromesaCumplidaDelClienteAsync(int alertaId, int clienteId)
        {
            var alerta = await ValidarAlertaDelClienteAsync(alertaId, clienteId);
            if (alerta.FechaPromesaPago == null)
                throw new InvalidOperationException("La alerta no tiene una promesa de pago vigente.");
            return await MarcarPromesaCumplidaAsync(alertaId);
        }

        // El acuerdo se activa con su calendario: (total − entrega inicial) en CantidadCuotas cuotas mensuales desde la
        // fecha de la primera; la última absorbe el redondeo para que la suma cierre exacto.
        private static void GenerarCalendarioDeCuotas(AcuerdoPago acuerdo)
        {
            if (acuerdo.Cuotas != null && acuerdo.Cuotas.Any(c => !c.IsDeleted))
                return;

            var n = acuerdo.CantidadCuotas;
            if (n <= 0)
                throw new InvalidOperationException("El acuerdo no tiene cantidad de cuotas.");

            var restante = acuerdo.MontoTotalAcuerdo - acuerdo.MontoEntregaInicial;
            // Entrega por el total: no hay saldo que financiar, no se generan cuotas de importe cero.
            if (restante <= 0m)
                return;

            var monto = Math.Round(restante / n, 2, MidpointRounding.AwayFromZero);
            acuerdo.Cuotas ??= new List<CuotaAcuerdo>();
            for (var i = 1; i <= n; i++)
            {
                var importe = i == n ? restante - monto * (n - 1) : monto;
                acuerdo.Cuotas.Add(new CuotaAcuerdo
                {
                    NumeroCuota = i,
                    MontoCapital = importe,
                    MontoMora = 0m,
                    MontoTotal = importe,
                    FechaVencimiento = acuerdo.FechaPrimeraCuota.AddMonths(i - 1),
                    Estado = EstadoCuotaAcuerdo.Pendiente
                });
            }
        }

        // Solo el índice del crédito vigente: una colisión de NumeroAcuerdo u otro error se propaga tal cual.
        // SQL Server nombra el índice en el mensaje; SQLite (tests) nombra la columna.
        private static bool EsViolacionAcuerdoVigente(DbUpdateException ex)
        {
            var mensaje = ex.InnerException?.Message ?? ex.Message;
            return mensaje.Contains("UX_AcuerdosPago_CreditoVigente", StringComparison.OrdinalIgnoreCase)
                || mensaje.Contains("AcuerdosPago.CreditoId", StringComparison.OrdinalIgnoreCase);
        }

        // Cuotas del crédito con deuda cobrable vencida, de la más vieja a la más nueva (orden de imputación): las que están en
        // mora de capital y también las de capital saldado que conservan un punitorio aplicado pendiente (esa deuda es
        // exigible y el cobro de cuotas la cobra igual). Dejarlas afuera haría que el acuerdo financie menos que la deuda.
        private async Task<List<Cuota>> CuotasConDeudaCobrableDelCreditoAsync(int creditoId)
        {
            var hoy = _reloj.HoyComercial;
            var cuotas = await _context.Cuotas
                .AsNoTracking()
                .Where(c => c.CreditoId == creditoId && !c.IsDeleted)
                .ToListAsync();
            var ids = cuotas.Select(c => c.Id).ToList();
            var conPunitorioPendiente = (await _context.PunitoriosAplicados
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Estado == EstadoPunitorioAplicado.Aplicado && ids.Contains(p.CuotaId))
                .Select(p => p.CuotaId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();
            return cuotas
                .Where(c => EstadoCuotaResolver.EstaEnMoraCapitalDerivado(c.Estado, c.MontoPagado, c.MontoTotal, c.FechaVencimiento, hoy)
                            || (conPunitorioPendiente.Contains(c.Id) && c.Estado != EstadoCuota.Cancelada))
                .OrderBy(c => c.FechaVencimiento)
                .ThenBy(c => c.NumeroCuota)
                .ToList();
        }

        public async Task<decimal> CalcularDeudaCobrableCreditoAsync(int creditoId) =>
            (await CalcularDesgloseDeudaCobrableCreditoAsync(creditoId)).Total;

        public async Task<DeudaCobrableCredito> CalcularDesgloseDeudaCobrableCreditoAsync(int creditoId)
        {
            decimal capital = 0m;
            decimal punitorio = 0m;
            var detalle = new List<DeudaCobrableCuota>();
            foreach (var cuota in await CuotasConDeudaCobrableDelCreditoAsync(creditoId))
            {
                var contexto = await _creditoService.ObtenerContextoPagoCuotaAsync(cuota.Id);
                // Sin el total cobrable autoritativo (p. ej. historial de punitorios incompleto) no se puede fijar la
                // deuda de un acuerdo: quedaría un total que omite un punitorio desconocido.
                if (contexto?.TotalCobrableActual == null)
                    throw new InvalidOperationException(
                        $"No se puede calcular la deuda de la cuota {cuota.NumeroCuota}: el historial de punitorios está incompleto. Conciliá el crédito antes de crear el acuerdo.");
                var capitalCuota = contexto.CapitalPendiente;
                var punitorioCuota = contexto.TotalCobrableActual.Value - contexto.CapitalPendiente;
                if (capitalCuota + punitorioCuota <= 0m)
                    continue;
                capital += capitalCuota;
                punitorio += punitorioCuota;
                detalle.Add(new DeudaCobrableCuota(cuota.Id, cuota.NumeroCuota, cuota.FechaVencimiento, capitalCuota, punitorioCuota));
            }
            return new DeudaCobrableCredito(capital, punitorio, detalle);
        }

        public async Task<decimal> CalcularPunitorioCalculadoSinAplicarAsync(int creditoId)
        {
            decimal total = 0m;
            foreach (var cuota in await CuotasConDeudaCobrableDelCreditoAsync(creditoId))
            {
                var contexto = await _creditoService.ObtenerContextoPagoCuotaAsync(cuota.Id);
                if (contexto?.PunitorioCalculadoInformativo is not { } calculado)
                    continue;
                // El cálculo es el acumulado desde el vencimiento: lo no aplicado es lo que excede a lo ya aplicado (activo o
                // pagado; las anulaciones no cuentan), igual que el diferencial que calcularía una nueva aplicación.
                var aplicado = (await _context.PunitoriosAplicados
                    .AsNoTracking()
                    .Where(p => p.CuotaId == cuota.Id && !p.IsDeleted && p.Estado != EstadoPunitorioAplicado.Anulado)
                    .Select(p => p.Importe)
                    .ToListAsync())
                    .Sum();
                total += Math.Max(0m, calculado - aplicado);
            }
            return total;
        }

        // Un pago de acuerdo son varios cobros independientes: dos solicitudes simultáneas sobre el mismo acuerdo se serializan
        // (la app corre en un solo proceso) para que la segunda lea el pendiente ya actualizado por la primera.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _bloqueosPagoAcuerdo = new();

        public async Task<ResultadoPagoAcuerdo> RegistrarPagoAcuerdoAsync(int acuerdoId, int clienteId, string medioPago, string? comprobante, int? pasoEsperado = null)
        {
            var bloqueo = _bloqueosPagoAcuerdo.GetOrAdd(acuerdoId, _ => new SemaphoreSlim(1, 1));
            await bloqueo.WaitAsync();
            try
            {
                return await RegistrarPagoAcuerdoSerializadoAsync(acuerdoId, clienteId, medioPago, comprobante, pasoEsperado);
            }
            finally
            {
                bloqueo.Release();
            }
        }

        // Refleja un importe ya cobrado en el acuerdo (entrega inicial o la cuota indicada).
        private static void AplicarImputacionAlAcuerdo(AcuerdoPago acuerdo, CuotaAcuerdo? cuotaDelAcuerdo, decimal aplicado, string? medioPago, string? comprobante)
        {
            if (cuotaDelAcuerdo == null)
            {
                acuerdo.MontoEntregaPagada += aplicado;
                if (acuerdo.MontoEntregaPagada >= acuerdo.MontoEntregaInicial - 0.004m)
                {
                    acuerdo.EntregaInicialPagada = true;
                    acuerdo.FechaPagoEntrega = DateTime.UtcNow;
                }
            }
            else
            {
                cuotaDelAcuerdo.MontoPagado += aplicado;
                cuotaDelAcuerdo.MedioPago = medioPago;
                cuotaDelAcuerdo.ComprobantePago = comprobante;
                if (cuotaDelAcuerdo.MontoPagado >= cuotaDelAcuerdo.MontoTotal - 0.004m)
                {
                    cuotaDelAcuerdo.Estado = EstadoCuotaAcuerdo.Pagada;
                    cuotaDelAcuerdo.FechaPago = DateTime.UtcNow;
                }
                else
                {
                    cuotaDelAcuerdo.Estado = EstadoCuotaAcuerdo.Parcial;
                }
            }
            acuerdo.UpdatedAt = DateTime.UtcNow;
        }

        // Con todos los pagos completos el acuerdo pasa a Cumplido y se resuelve la alerta si ya no queda deuda vencida.
        private async Task<bool> CompletarAcuerdoSiCorrespondeAsync(AcuerdoPago acuerdo)
        {
            var todoPagado = acuerdo.EntregaInicialPagada || acuerdo.MontoEntregaInicial <= 0m;
            todoPagado = todoPagado && acuerdo.Cuotas.Where(c => !c.IsDeleted).All(c => c.Estado == EstadoCuotaAcuerdo.Pagada);
            if (!todoPagado || acuerdo.Estado != EstadoAcuerdo.Activo)
                return todoPagado;

            acuerdo.Estado = EstadoAcuerdo.Cumplido;
            acuerdo.UpdatedAt = DateTime.UtcNow;
            // Como el incumplimiento automático: el cambio de estado y su auditoría se confirman juntos (QA H9-M4: el
            // pase a Cumplido no dejaba evento propio, solo el recibo de cada cobro).
            AgregarAuditoria("CumplimientoAutomatico", acuerdo.Id, $"{acuerdo.NumeroAcuerdo}: Activo -> Cumplido. Todos los pagos del acuerdo están completos.");
            await _context.SaveChangesAsync();
            await ResolverAlertasMoraRegularizadasAsync(new List<int>(), _reloj.InicioDiaComercial, CancellationToken.None);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Resuelve las intenciones que quedaron Pendientes (corte entre el cobro y su reflejo): busca el pago en el libro
        /// por cuota, importe y fecha; si existe y no respalda otra imputación, lo refleja en el acuerdo; si no existe, el
        /// cobro nunca se confirmó y la intención se descarta. Más de un candidato = ambiguo: se frena para revisión manual.
        /// Devuelve true si reflejó algún cobro.
        /// </summary>
        private async Task<bool> ConciliarImputacionesPendientesAsync(AcuerdoPago acuerdo)
        {
            var pendientes = await _context.AcuerdoPagoImputaciones
                .Where(i => i.AcuerdoPagoId == acuerdo.Id && i.Estado == EstadoImputacionAcuerdo.Pendiente)
                .OrderBy(i => i.Id)
                .ToListAsync();
            if (pendientes.Count == 0)
                return false;

            var yaRespaldados = await _context.AcuerdoPagoImputaciones
                .Where(i => i.PagoCuotaId != null)
                .Select(i => i.PagoCuotaId!.Value)
                .ToListAsync();

            var reflejado = false;
            foreach (var intencion in pendientes)
            {
                // Por identidad, no por importe y fecha: el pago lleva la OperacionId de la intención. Esta conciliación corre
                // serializada por acuerdo y sin cobros del mismo acuerdo en vuelo, así que un pago ausente = cobro no confirmado.
                var pago = await _context.PagosCuota
                    .FirstOrDefaultAsync(p => p.OperacionExternaId == intencion.OperacionId);

                if (pago == null || pago.Estado != EstadoPagoCuota.Aplicado || yaRespaldados.Contains(pago.Id))
                {
                    intencion.Estado = EstadoImputacionAcuerdo.Descartada;
                    continue;
                }

                var cuotaDelAcuerdo = intencion.CuotaAcuerdoId.HasValue
                    ? acuerdo.Cuotas.FirstOrDefault(c => c.Id == intencion.CuotaAcuerdoId.Value)
                    : null;
                AplicarImputacionAlAcuerdo(acuerdo, cuotaDelAcuerdo, pago.ImporteTotal, intencion.MedioPago, intencion.Comprobante);
                intencion.Estado = EstadoImputacionAcuerdo.Aplicada;
                intencion.PagoCuotaId = pago.Id;
                intencion.MontoAplicado = pago.ImporteTotal;
                yaRespaldados.Add(pago.Id);
                reflejado = true;
                _logger.LogWarning("Acuerdo {Numero}: se concilió el pago {PagoId} que había quedado sin reflejar", acuerdo.NumeroAcuerdo, pago.Id);
            }

            await _context.SaveChangesAsync();
            return reflejado;
        }

        // Pagos completos del acuerdo: la entrega inicial (si la hay) más las cuotas pagadas.
        private static int PagosCompletos(AcuerdoPago acuerdo) =>
            ((acuerdo.EntregaInicialPagada || acuerdo.MontoEntregaInicial <= 0m) ? 1 : 0)
            + acuerdo.Cuotas.Count(c => !c.IsDeleted && c.Estado == EstadoCuotaAcuerdo.Pagada);

        private async Task<ResultadoPagoAcuerdo> RegistrarPagoAcuerdoSerializadoAsync(int acuerdoId, int clienteId, string medioPago, string? comprobante, int? pasoEsperado)
        {
            var acuerdo = await _context.AcuerdosPago
                .Include(a => a.Cuotas)
                .FirstOrDefaultAsync(a => a.Id == acuerdoId && !a.IsDeleted && a.ClienteId == clienteId)
                ?? throw new InvalidOperationException("El acuerdo no existe o no pertenece a este cliente.");
            if (acuerdo.Estado != EstadoAcuerdo.Activo)
                throw new InvalidOperationException("Solo se pueden registrar pagos en un acuerdo activo.");

            // Un corte entre un cobro confirmado y su reflejo en el acuerdo se cierra acá, antes de decidir qué se cobra.
            await ConciliarImputacionesPendientesAsync(acuerdo);
            // Un acuerdo con todos los pagos completos que quedó Activo (corte después del último cobro) se cumple acá.
            await CompletarAcuerdoSiCorrespondeAsync(acuerdo);
            if (acuerdo.Estado != EstadoAcuerdo.Activo)
                throw new InvalidOperationException("El acuerdo ya está cumplido: el último pago ya estaba registrado.");

            // Con los cobros ya conciliados, un pago impago fuera de gracia pasa el acuerdo a Incumplido y este pago NO lo
            // reactiva: lo que quede se cobra por el circuito normal de cuotas.
            if (await MarcarIncumplidoSiCorrespondeAsync(acuerdo))
                throw new InvalidOperationException(
                    $"El acuerdo pasó a Incumplido. {acuerdo.MotivoIncumplimiento} Las cuotas se cobran ahora por el circuito normal de cuotas.");

            // El formulario viaja atado al avance del acuerdo: un reenvío (doble clic, recarga del POST, un formulario viejo
            // abierto en otra pestaña) trae un paso que ya pasó y se rechaza sin cobrar.
            if (pasoEsperado.HasValue && pasoEsperado.Value != PagosCompletos(acuerdo))
                throw new InvalidOperationException("Ese pago ya se registró o el acuerdo avanzó. Actualizá la ficha para ver el próximo pago.");

            // Próximo pago: primero la entrega inicial, después las cuotas en orden. No se elige: así el calendario no se salta.
            CuotaAcuerdo? cuotaDelAcuerdo = null;
            string concepto;
            decimal pendiente;
            if (!acuerdo.EntregaInicialPagada && acuerdo.MontoEntregaInicial - acuerdo.MontoEntregaPagada > 0m)
            {
                concepto = "Entrega inicial";
                pendiente = acuerdo.MontoEntregaInicial - acuerdo.MontoEntregaPagada;
            }
            else
            {
                cuotaDelAcuerdo = acuerdo.Cuotas
                    .Where(c => !c.IsDeleted && c.Estado != EstadoCuotaAcuerdo.Pagada)
                    .OrderBy(c => c.NumeroCuota)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException("El acuerdo no tiene pagos pendientes.");
                concepto = $"Cuota {cuotaDelAcuerdo.NumeroCuota} del acuerdo";
                pendiente = cuotaDelAcuerdo.MontoTotal - cuotaDelAcuerdo.MontoPagado;
            }

            // Solo la deuda congelada al activar: las cuotas que vencen o se atrasan después no se incorporan al acuerdo.
            var cuotasCubiertas = await _context.AcuerdoPagoCuotasCubiertas
                .AsNoTracking()
                .Where(c => c.AcuerdoPagoId == acuerdo.Id && !c.IsDeleted)
                .OrderBy(c => c.Orden)
                .ToListAsync();
            if (cuotasCubiertas.Count == 0)
                throw new InvalidOperationException("El acuerdo no tiene deuda congelada. Cancelalo y creá uno nuevo.");

            // Cada imputación es un cobro de cuota confirmado por separado (el cobro abre su propia transacción serializable).
            // Antes de cobrar se guarda la intención; al confirmarse, el reflejo en el acuerdo y el cierre de la intención van
            // juntos. Si el proceso se cae en el medio, la intención queda Pendiente y la conciliación la resuelve contra el libro.
            var restante = pendiente;
            decimal imputado = 0m;
            foreach (var cubierta in cuotasCubiertas)
            {
                if (restante <= 0.004m)
                    break;

                var contexto = await _creditoService.ObtenerContextoPagoCuotaAsync(cubierta.CuotaId);
                if (contexto == null)
                    continue;
                // Sin el total cobrable autoritativo no se puede imputar sin riesgo de cobrar de más o de menos.
                if (contexto.TotalCobrableActual == null)
                    throw new InvalidOperationException(
                        $"No se puede imputar el pago a la cuota {contexto.NumeroCuota}: el historial de punitorios está incompleto. Conciliá el crédito.");
                // Nunca más que la deuda congelada de la cuota, aunque el libro hoy mostrara otra cosa.
                var cobrable = Math.Min(contexto.TotalCobrableActual.Value, cubierta.CapitalCubierto + cubierta.PunitorioCubierto);
                if (cobrable <= 0m)
                    continue;

                var monto = Math.Round(Math.Min(restante, cobrable), 2, MidpointRounding.AwayFromZero);
                var intencion = new AcuerdoPagoImputacion
                {
                    AcuerdoPagoId = acuerdo.Id,
                    CuotaAcuerdoId = cuotaDelAcuerdo?.Id,
                    CuotaCreditoId = cubierta.CuotaId,
                    Monto = monto,
                    MedioPago = medioPago,
                    Comprobante = comprobante,
                    Estado = EstadoImputacionAcuerdo.Pendiente
                };
                _context.AcuerdoPagoImputaciones.Add(intencion);
                await _context.SaveChangesAsync();

                PagoCuotaResultado cobro;
                try
                {
                    cobro = await _creditoService.RegistrarPagoCuotaIndividualAsync(new PagoCuotaIndividualComando(
                        cubierta.CuotaId,
                        monto,
                        medioPago,
                        comprobante,
                        $"Acuerdo {acuerdo.NumeroAcuerdo}: {concepto}",
                        Convert.FromBase64String(contexto.CuotaRowVersionBase64),
                        intencion.OperacionId))
                        ?? throw new InvalidOperationException("No se pudo registrar el cobro de la cuota vencida.");
                }
                catch
                {
                    // Un error no dice si el cobro llegó a confirmarse: se verifica contra el libro por la identidad de la
                    // operación. Si el pago existe se refleja en el acuerdo; si no, la intención se descarta.
                    if (await ConciliarImputacionesPendientesAsync(acuerdo))
                        await CompletarAcuerdoSiCorrespondeAsync(acuerdo);
                    throw;
                }

                var aplicado = cobro.ImporteRecibido;
                restante -= aplicado;
                imputado += aplicado;

                AplicarImputacionAlAcuerdo(acuerdo, cuotaDelAcuerdo, aplicado, medioPago, comprobante);
                intencion.Estado = EstadoImputacionAcuerdo.Aplicada;
                intencion.PagoCuotaId = cobro.PagoCuotaId;
                intencion.MontoAplicado = aplicado;
                await _context.SaveChangesAsync();
            }

            if (restante > 0.004m)
                throw new InvalidOperationException(
                    $"Se imputaron {imputado:C2} de {pendiente:C2}: la deuda vencida del crédito no alcanza para el resto. Revisá el acuerdo.");

            var todoPagado = await CompletarAcuerdoSiCorrespondeAsync(acuerdo);

            // El acuerdo cumplido cubre la deuda que se fijó al crearlo; si después se aplicó un punitorio o venció otra
            // cuota, el crédito conserva deuda propia que se informa para que no se dé por saldado.
            decimal? deudaRestante = null;
            if (todoPagado)
            {
                try
                {
                    var restanteCredito = await CalcularDeudaCobrableCreditoAsync(acuerdo.CreditoId);
                    if (restanteCredito > 0m)
                        deudaRestante = restanteCredito;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Acuerdo {Numero}: no se pudo verificar la deuda restante del crédito", acuerdo.NumeroAcuerdo);
                }
            }

            _logger.LogInformation("Pago de acuerdo {Numero}: {Concepto} por {Importe}", acuerdo.NumeroAcuerdo, concepto, imputado);
            return new ResultadoPagoAcuerdo(concepto, imputado, todoPagado, deudaRestante);
        }

        // ¿Algún pago del acuerdo sigue impago pasada su gracia? Pura y sin escritura (la usan la verificación diaria y el pago).
        // Vencimiento D con gracia G: dentro de la gracia hasta D+G inclusive; incumple desde D+G+1. Un pago parcial no evita el
        // incumplimiento mientras quede saldo vencido. La entrega inicial vence el día de la activación; sin entrega inicial se
        // evalúan solo las cuotas, y con la entrega por el total no hay cuotas (se cumple al pagarla).
        internal static string? MotivoDeIncumplimiento(AcuerdoPago acuerdo, DateOnly hoyComercial)
        {
            var gracia = Math.Max(0, acuerdo.DiasGraciaIncumplimiento);

            if (acuerdo.MontoEntregaInicial > 0m && !acuerdo.EntregaInicialPagada
                && acuerdo.MontoEntregaInicial - acuerdo.MontoEntregaPagada > 0.004m
                && acuerdo.FechaActivacionComercial is { } activacion
                && hoyComercial > activacion.AddDays(gracia))
                return $"La entrega inicial venció el {activacion:dd/MM/yyyy} y sigue impaga pasados {gracia} días de gracia.";

            foreach (var cuota in acuerdo.Cuotas.Where(c => !c.IsDeleted && c.Estado != EstadoCuotaAcuerdo.Pagada).OrderBy(c => c.NumeroCuota))
            {
                var vencimiento = DateOnly.FromDateTime(cuota.FechaVencimiento);
                if (cuota.MontoTotal - cuota.MontoPagado > 0.004m && hoyComercial > vencimiento.AddDays(gracia))
                    return $"La cuota {cuota.NumeroCuota} del acuerdo venció el {vencimiento:dd/MM/yyyy} y sigue impaga pasados {gracia} días de gracia.";
            }

            return null;
        }

        // Marca Incumplido (y audita) si corresponde. Un acuerdo que no está Activo no se toca: un Incumplido nunca se reactiva solo.
        private async Task<bool> MarcarIncumplidoSiCorrespondeAsync(AcuerdoPago acuerdo)
        {
            if (acuerdo.Estado != EstadoAcuerdo.Activo)
                return false;

            var motivo = MotivoDeIncumplimiento(acuerdo, _reloj.HoyComercial);
            if (motivo == null)
                return false;

            acuerdo.Estado = EstadoAcuerdo.Incumplido;
            acuerdo.MotivoIncumplimiento = motivo;
            acuerdo.UpdatedAt = DateTime.UtcNow;
            // El cambio de estado y su auditoría se confirman juntos: no puede quedar un Incumplido sin rastro.
            AgregarAuditoria("IncumplimientoAutomatico", acuerdo.Id, $"{acuerdo.NumeroAcuerdo}: Activo -> Incumplido. {motivo}");
            await _context.SaveChangesAsync();

            _logger.LogWarning("Acuerdo {Numero} pasó a Incumplido automáticamente: {Motivo}", acuerdo.NumeroAcuerdo, motivo);
            return true;
        }

        public async Task<ResultadoVerificacionIncumplimiento> VerificarIncumplimientoAcuerdosAsync(CancellationToken cancellationToken = default)
        {
            var ids = await _context.AcuerdosPago
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.Estado == EstadoAcuerdo.Activo)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            var marcados = 0;
            var fallidos = 0;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // El mismo bloqueo que los pagos del acuerdo: un cobro en vuelo (intención Pendiente) no se concilia ni se
                // descarta a mitad de camino.
                var bloqueo = _bloqueosPagoAcuerdo.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                await bloqueo.WaitAsync(cancellationToken);
                try
                {
                    // Cada acuerdo parte de un contexto limpio y de lo que hay en la base: nada pendiente de otro acuerdo.
                    _context.ChangeTracker.Clear();
                    var acuerdo = await _context.AcuerdosPago
                        .Include(a => a.Cuotas)
                        .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);
                    if (acuerdo == null || acuerdo.Estado != EstadoAcuerdo.Activo)
                        continue;

                    // Primero lo que ya se cobró: un pago confirmado que quedó sin reflejar no puede contar como impago.
                    await ConciliarImputacionesPendientesAsync(acuerdo);
                    await CompletarAcuerdoSiCorrespondeAsync(acuerdo);
                    if (await MarcarIncumplidoSiCorrespondeAsync(acuerdo))
                        marcados++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un acuerdo con problemas no frena la verificación de los demás ni arrastra sus cambios a los siguientes
                    // (estado y evento de auditoría sin confirmar se descartan); se reintenta en la próxima corrida.
                    _context.ChangeTracker.Clear();
                    fallidos++;
                    _logger.LogError(ex, "No se pudo verificar el incumplimiento del acuerdo {AcuerdoId}", id);
                }
                finally
                {
                    bloqueo.Release();
                }
            }

            return new ResultadoVerificacionIncumplimiento(marcados, fallidos);
        }

        public Task<bool> ExisteAcuerdoVigenteAsync(int creditoId) =>
            _context.AcuerdosPago.AnyAsync(a => !a.IsDeleted
                && a.CreditoId == creditoId
                && (a.Estado == EstadoAcuerdo.Borrador || a.Estado == EstadoAcuerdo.Activo));

        private async Task<AlertaCobranza> ValidarAlertaDelClienteAsync(int alertaId, int clienteId, int? creditoId = null)
        {
            var alerta = await _context.AlertasCobranza
                .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);
            if (alerta == null)
                throw new InvalidOperationException("La alerta no existe.");
            if (alerta.ClienteId != clienteId)
                throw new InvalidOperationException("La alerta no pertenece a este cliente.");
            if (creditoId.HasValue && alerta.CreditoId != creditoId.Value)
                throw new InvalidOperationException("La alerta no pertenece al crédito del acuerdo.");
            if (alerta.Resuelta)
                throw new InvalidOperationException("La alerta ya está resuelta.");
            return alerta;
        }

        public async Task<bool> CambiarEstadoAcuerdoAsync(int acuerdoId, int clienteId, EstadoAcuerdo nuevoEstado, string? motivo = null)
        {
            var acuerdo = await _context.AcuerdosPago
                .Include(a => a.Cuotas)
                .FirstOrDefaultAsync(a => a.Id == acuerdoId && !a.IsDeleted && a.ClienteId == clienteId);
            if (acuerdo == null)
                throw new InvalidOperationException("El acuerdo no existe o no pertenece a este cliente.");

            var permitido = (acuerdo.Estado, nuevoEstado) switch
            {
                (EstadoAcuerdo.Borrador, EstadoAcuerdo.Activo) => true,
                (EstadoAcuerdo.Borrador, EstadoAcuerdo.Cancelado) => true,
                (EstadoAcuerdo.Activo, EstadoAcuerdo.Incumplido) => true,
                (EstadoAcuerdo.Activo, EstadoAcuerdo.Cancelado) => true,
                _ => false
            };
            if (!permitido)
                throw new InvalidOperationException(nuevoEstado == EstadoAcuerdo.Cumplido
                    ? "El cumplimiento de un acuerdo se registra con el pago de sus cuotas; todavía no se puede marcar a mano."
                    : $"Un acuerdo {acuerdo.Estado} no puede pasar a {nuevoEstado}.");

            motivo = motivo?.Trim();
            // Cancelar libera las cuotas cubiertas para cobrarlas por otro circuito: queda justificado y auditado.
            if ((nuevoEstado == EstadoAcuerdo.Cancelado || nuevoEstado == EstadoAcuerdo.Incumplido) && string.IsNullOrWhiteSpace(motivo))
                throw new InvalidOperationException("Indicá el motivo para cancelar o marcar incumplido el acuerdo.");

            var estadoAnterior = acuerdo.Estado;
            var detalleAuditoria = $"{acuerdo.NumeroAcuerdo}: {estadoAnterior} -> {nuevoEstado}." + (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo}");
            if (nuevoEstado == EstadoAcuerdo.Activo)
            {
                await ActivarAcuerdoAsync(acuerdo, detalleAuditoria);
            }
            else
            {
                acuerdo.Estado = nuevoEstado;
                if (nuevoEstado == EstadoAcuerdo.Cancelado)
                    acuerdo.MotivoCancelacion = motivo;
                else if (nuevoEstado == EstadoAcuerdo.Incumplido)
                    acuerdo.MotivoIncumplimiento = motivo;
                acuerdo.UpdatedAt = DateTime.UtcNow;
                AgregarAuditoria("CambiarEstadoAcuerdo", acuerdo.Id, detalleAuditoria); // se confirma con el cambio de estado
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Acuerdo {Numero} pasó a {Estado}", acuerdo.NumeroAcuerdo, nuevoEstado);
            return true;
        }

        // Activar congela la deuda: bajo el lock del crédito (el mismo que usan cobros y cancelaciones) se re-valida que la
        // deuda real coincida con el total acordado y se guarda la composición por cuota junto con el calendario y el estado,
        // todo en una transacción. Después, nada de lo que cubre el acuerdo cambia por otro circuito.
        private async Task ActivarAcuerdoAsync(AcuerdoPago acuerdo, string detalleAuditoria)
        {
            var restanteAFinanciar = acuerdo.MontoTotalAcuerdo - acuerdo.MontoEntregaInicial;
            if (restanteAFinanciar > 0m && acuerdo.FechaPrimeraCuota.Date < _reloj.InicioDiaComercial.Date)
                throw new InvalidOperationException("La fecha de la primera cuota ya pasó. Cancelá el acuerdo y creá uno nuevo.");

            // La gracia de incumplimiento queda fijada en el acuerdo: un cambio posterior de la configuración no lo afecta.
            var diasGracia = (await GetConfiguracionAsync()).DiasGraciaIncumplimientoAcuerdo;

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                await CreditoConcurrencia.BloquearAsync(_context, acuerdo.CreditoId);

                if (await _context.AcuerdosPago.AnyAsync(a => !a.IsDeleted && a.Id != acuerdo.Id
                        && a.CreditoId == acuerdo.CreditoId && a.Estado == EstadoAcuerdo.Activo))
                    throw new InvalidOperationException("Ese crédito ya tiene otro acuerdo activo.");

                // El total se fijó al crearlo: si la deuda cambió desde entonces (un cobro, un punitorio aplicado) el acuerdo ya
                // no financia la deuda real, ni de más ni de menos. Se rehace en vez de activarlo.
                var deuda = await CalcularDesgloseDeudaCobrableCreditoAsync(acuerdo.CreditoId);
                if (Math.Abs(deuda.Total - acuerdo.MontoTotalAcuerdo) > 0.01m)
                    throw new InvalidOperationException(
                        $"La deuda del crédito cambió desde que se creó el acuerdo (acordada {acuerdo.MontoTotalAcuerdo:C2}, hoy {deuda.Total:C2}). Cancelalo y creá uno nuevo.");

                var orden = 0;
                foreach (var cuota in deuda.Cuotas.OrderBy(c => c.FechaVencimiento).ThenBy(c => c.NumeroCuota))
                {
                    _context.AcuerdoPagoCuotasCubiertas.Add(new AcuerdoPagoCuotaCubierta
                    {
                        AcuerdoPagoId = acuerdo.Id,
                        CuotaId = cuota.CuotaId,
                        Orden = ++orden,
                        CapitalCubierto = cuota.Capital,
                        PunitorioCubierto = cuota.Punitorio
                    });
                }

                acuerdo.Estado = EstadoAcuerdo.Activo;
                acuerdo.FechaActivacion = DateTime.UtcNow;
                acuerdo.FechaActivacionComercial = _reloj.HoyComercial; // la entrega inicial vence este día
                acuerdo.DiasGraciaIncumplimiento = diasGracia;
                GenerarCalendarioDeCuotas(acuerdo);
                acuerdo.UpdatedAt = DateTime.UtcNow;
                AgregarAuditoria("CambiarEstadoAcuerdo", acuerdo.Id, detalleAuditoria); // misma transacción que la activación

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsViolacionAcuerdoVigente(ex))
                {
                    throw new InvalidOperationException("Ese crédito ya tiene otro acuerdo vigente.");
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // El estado en memoria no puede quedar Activo si la transacción se revirtió.
                acuerdo.Estado = EstadoAcuerdo.Borrador;
                acuerdo.FechaActivacion = null;
                acuerdo.FechaActivacionComercial = null;
                throw;
            }
        }

        // Agrega el evento al contexto sin guardar: se confirma con el SaveChanges del cambio auditado (atómico).
        private void AgregarAuditoria(string accion, int acuerdoId, string detalle) =>
            _auditoria?.AgregarEvento("Mora", accion, $"AcuerdoPago:{acuerdoId}", detalle);

        private async Task AuditarAsync(string accion, int acuerdoId, string detalle)
        {
            if (_auditoria == null)
                return;
            await _auditoria.RegistrarEventoAsync("Mora", accion, $"AcuerdoPago:{acuerdoId}", detalle);
        }

        public async Task<List<AcuerdoPagoResumenViewModel>> GetAcuerdosPagoAsync(int clienteId)
        {
            try
            {
                var acuerdos = await _context.AcuerdosPago
                    .AsNoTracking()
                    .Include(a => a.Cuotas)
                    .Where(a => a.ClienteId == clienteId && !a.IsDeleted)
                    .OrderByDescending(a => a.FechaCreacion)
                    .ToListAsync();

                // PUN-ML7 (cierre): antes DateTime.Today. Solo decide qué cuota del acuerdo es "la
                // próxima" (primera no pagada con vencimiento >= hoy) — no hay clasificación dinámica
                // de "acuerdo vencido"/cumplimiento acá, EstadoAcuerdo se lee tal cual está persistido.
                var hoy = _reloj.InicioDiaComercial;
                var resumenes = acuerdos.Select(a =>
                {
                    var cuotasPagadas = a.Cuotas?.Count(c => c.Estado == EstadoCuotaAcuerdo.Pagada) ?? 0;
                    var montoPagado = (a.Cuotas?.Where(c => !c.IsDeleted).Sum(c => c.MontoPagado) ?? 0) + a.MontoEntregaPagada;
                    (string Concepto, decimal Pendiente)? siguiente = null;
                    if (a.Estado == EstadoAcuerdo.Activo)
                    {
                        if (!a.EntregaInicialPagada && a.MontoEntregaInicial - a.MontoEntregaPagada > 0m)
                        {
                            siguiente = ("Entrega inicial", a.MontoEntregaInicial - a.MontoEntregaPagada);
                        }
                        else
                        {
                            var proxima = a.Cuotas?.Where(c => !c.IsDeleted && c.Estado != EstadoCuotaAcuerdo.Pagada)
                                .OrderBy(c => c.NumeroCuota).FirstOrDefault();
                            if (proxima != null)
                                siguiente = ($"Cuota {proxima.NumeroCuota} del acuerdo", proxima.MontoTotal - proxima.MontoPagado);
                        }
                    }
                    var proximaCuota = a.Cuotas?
                        .Where(c => c.Estado != EstadoCuotaAcuerdo.Pagada && c.FechaVencimiento >= hoy)
                        .OrderBy(c => c.FechaVencimiento)
                        .FirstOrDefault();

                    return new AcuerdoPagoResumenViewModel
                    {
                        AcuerdoId = a.Id,
                        NumeroAcuerdo = a.NumeroAcuerdo,
                        FechaCreacion = a.FechaCreacion,
                        Estado = a.Estado,
                        MontoTotal = a.MontoTotalAcuerdo,
                        MontoPagado = montoPagado,
                        MontoCondonado = a.MontoCondonado,
                        TotalCuotas = a.CantidadCuotas,
                        CuotasPagadas = cuotasPagadas,
                        ProximaFechaVencimiento = proximaCuota?.FechaVencimiento,
                        ProximoPagoConcepto = siguiente?.Concepto,
                        ProximoPagoPendiente = siguiente?.Pendiente ?? 0m,
                        ProximoPagoPaso = PagosCompletos(a),
                        DiasGraciaIncumplimiento = a.DiasGraciaIncumplimiento,
                        MotivoIncumplimiento = a.Estado == EstadoAcuerdo.Incumplido ? a.MotivoIncumplimiento : null
                    };
                }).ToList();

                // Antes de activar un borrador se muestra el punitorio calculado que NO incluye (no se aplica solo).
                foreach (var borrador in resumenes.Where(r => r.Estado == EstadoAcuerdo.Borrador))
                {
                    var creditoId = acuerdos.First(a => a.Id == borrador.AcuerdoId).CreditoId;
                    try
                    {
                        borrador.PunitorioCalculadoSinAplicar = await CalcularPunitorioCalculadoSinAplicarAsync(creditoId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo calcular el punitorio sin aplicar del acuerdo {AcuerdoId}", borrador.AcuerdoId);
                    }
                }

                return resumenes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener acuerdos de pago del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<AcuerdoPago?> GetAcuerdoPagoDetalleAsync(int acuerdoId)
        {
            try
            {
                return await _context.AcuerdosPago
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Include(a => a.Cuotas)
                    .FirstOrDefaultAsync(a => a.Id == acuerdoId && !a.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalle del acuerdo {AcuerdoId}", acuerdoId);
                throw;
            }
        }

        #endregion

        #region Dashboard KPIs

        public async Task<DashboardMoraKPIs> GetDashboardKPIsAsync()
        {
            try
            {
                // PUN-ML7 (cierre): antes DateTime.Today — dependía de la zona del proceso, no de
                // Argentina, e inconsistente con el resto de las lecturas de mora ya corregidas.
                var hoy = _reloj.InicioDiaComercial;
                var inicioSemana = hoy.AddDays(-(int)hoy.DayOfWeek);
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
                var config = await GetConfiguracionAsync();

                // Alertas activas (no resueltas). Las de ProximoVencimiento son preventivas: la cuota todavía no
                // venció, así que no cuentan como clientes en mora ni como monto vencido; sus promesas de pago sí
                // siguen en los indicadores de promesas.
                var alertasActivas = await _context.AlertasCobranza
                    .AsNoTracking()
                    .Where(a => !a.IsDeleted && !a.Resuelta)
                    .Include(a => a.Cliente)
                    .ToListAsync();
                var alertas = alertasActivas.Where(a => a.Tipo != TipoAlertaCobranza.ProximoVencimiento).ToList();

                var clienteIdsEnMora = alertas.Select(a => a.ClienteId).Distinct().ToList();

                // Conteo por prioridad (clientes únicos)
                var conteoPorPrioridad = alertas
                    .GroupBy(a => a.Prioridad)
                    .ToDictionary(g => g.Key, g => g.Select(a => a.ClienteId).Distinct().Count());

                // Clientes sin gestión (sin contacto en los últimos N días)
                var diasMaxSinGestion = config.DiasMaximosSinGestion > 0 ? config.DiasMaximosSinGestion : 7;
                var fechaLimiteGestion = hoy.AddDays((double)(-diasMaxSinGestion));
                var clientesConGestionReciente = await _context.HistorialContactos
                    .Where(h => h.FechaContacto >= fechaLimiteGestion && !h.IsDeleted && clienteIdsEnMora.Contains(h.ClienteId))
                    .Select(h => h.ClienteId)
                    .Distinct()
                    .ToListAsync();
                var clientesSinGestion = clienteIdsEnMora.Except(clientesConGestionReciente).Count();

                // Días promedio de atraso
                var diasPromedio = alertas.Any() ? alertas.Average(a => a.DiasAtraso) : 0;

                // Promesas
                var promesasActivas = alertasActivas.Count(a => a.FechaPromesaPago >= hoy);
                var promesasVencenHoy = alertasActivas.Count(a => a.FechaPromesaPago?.Date == hoy);
                var montoPromesasHoy = alertasActivas.Where(a => a.FechaPromesaPago?.Date == hoy).Sum(a => a.MontoPromesaPago ?? 0);
                var promesasVencidas = alertasActivas.Count(a => a.FechaPromesaPago < hoy && a.FechaPromesaPago != null);

                // Acuerdos activos (calcular saldo pendiente a partir de cuotas)
                var acuerdos = await _context.AcuerdosPago
                    .AsNoTracking()
                    .Include(a => a.Cuotas)
                    .Where(a => a.Estado == EstadoAcuerdo.Activo && !a.IsDeleted)
                    .ToListAsync();
                var acuerdosActivos = acuerdos.Count;
                var montoAcuerdosActivos = acuerdos.Sum(a => 
                    a.MontoTotalAcuerdo - (a.Cuotas?.Where(c => !c.IsDeleted).Sum(c => c.MontoPagado) ?? 0) - a.MontoEntregaPagada);

                // Contactos hoy y semana
                var contactosHoy = await _context.HistorialContactos
                    .CountAsync(h => h.FechaContacto.Date == hoy && !h.IsDeleted);
                var contactosSemana = await _context.HistorialContactos
                    .CountAsync(h => h.FechaContacto >= inicioSemana && !h.IsDeleted);

                // Cobros hoy (cuotas pagadas hoy)
                var cuotasPagadasHoy = await _context.Cuotas
                    .AsNoTracking()
                    .Where(c => c.FechaPago.HasValue && c.FechaPago.Value.Date == hoy && !c.IsDeleted)
                    .ToListAsync();
                var cantidadCobrosHoy = cuotasPagadasHoy.Count;
                var montoCobradoHoy = cuotasPagadasHoy.Sum(c => c.MontoPagado);

                // Tasa de recupero del mes (monto cobrado / monto vencido al inicio del mes).
                // SumAsync sobre decimal no traduce a SQL bajo el proveedor Sqlite (usado en tests);
                // mismo patrón ya establecido en el resto del código para este límite del proveedor:
                // traer las filas y sumar en cliente.
                var cobrosMes = (await _context.Cuotas
                    .Where(c => c.FechaPago.HasValue && c.FechaPago.Value >= inicioMes && !c.IsDeleted)
                    .Select(c => c.MontoPagado)
                    .ToListAsync())
                    .Sum();
                var montoVencidoMes = alertas.Sum(a => a.MontoVencido);
                var tasaRecuperoMes = montoVencidoMes > 0 ? (cobrosMes / montoVencidoMes) * 100 : 0;

                return new DashboardMoraKPIs
                {
                    TotalClientesMora = clienteIdsEnMora.Count,
                    ClientesSinGestion = clientesSinGestion,
                    ClientesPrioridadCritica = conteoPorPrioridad.GetValueOrDefault(PrioridadAlerta.Critica, 0),
                    ClientesPrioridadAlta = conteoPorPrioridad.GetValueOrDefault(PrioridadAlerta.Alta, 0),
                    ClientesPrioridadMedia = conteoPorPrioridad.GetValueOrDefault(PrioridadAlerta.Media, 0),
                    ClientesPrioridadBaja = conteoPorPrioridad.GetValueOrDefault(PrioridadAlerta.Baja, 0),
                    DiasPromedioAtraso = (decimal)diasPromedio,
                    MontoTotalVencido = alertas.Sum(a => a.MontoVencido),
                    MoraTotal = alertas.Sum(a => a.MontoMoraCalculada),
                    AlertasActivas = alertas.Count,
                    AlertasNoLeidas = alertas.Count(a => a.CreatedAt > (config.UltimaEjecucion ?? DateTime.MinValue)),
                    AlertasCriticas = alertas.Count(a => a.Prioridad == PrioridadAlerta.Critica),
                    PromesasActivas = promesasActivas,
                    PromesasVencenHoy = promesasVencenHoy,
                    MontoPromesasHoy = montoPromesasHoy,
                    PromesasVencidas = promesasVencidas,
                    AcuerdosActivos = acuerdosActivos,
                    MontoAcuerdosActivos = montoAcuerdosActivos,
                    ContactosHoy = contactosHoy,
                    ContactosSemana = contactosSemana,
                    CobrosHoy = cantidadCobrosHoy,
                    MontoCobradoHoy = montoCobradoHoy,
                    TasaRecuperoMes = tasaRecuperoMes,
                    UltimaEjecucion = config.UltimaEjecucion,
                    ProcesoActivo = config.ProcesoAutomaticoActivo
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener KPIs del dashboard de mora");
                throw;
            }
        }

        #endregion

        #region Métodos Privados Helpers

        // Fórmula de mora para alertas de cobranza: usa ConfiguracionMora.TasaMoraBase (tasa global configurable).
        // NOTA: difiere de MoraAlertasService y CreditoService que usan Credito.TasaInteres por crédito.
        // Esta bifurcación produce MontoMoraCalculada en AlertaCobranza inconsistente con MontoPunitorio en Cuota.
        // Pendiente de alineación — requiere decisión de negocio sobre régimen de tasa punitoria.
        private decimal CalcularMora(decimal montoVencido, int diasAtraso, ConfiguracionMora config)
        {
            if (diasAtraso <= 0 || montoVencido <= 0)
                return 0;

            var tasaBase = config.TasaMoraBase ?? 0m;
            var tasaDiaria = tasaBase / 100m / DiasPorMesComercial; // Convertir a tasa diaria
            var mora = montoVencido * tasaDiaria * diasAtraso;

            return Math.Round(mora, 2);
        }

        private PrioridadAlerta DeterminarPrioridad(int diasMora, decimal montoVencido, ConfiguracionMora config)
        {
            var diasCritica = config.DiasParaPrioridadCritica ?? DiasUmbralAlertaCritica;
            var diasAlta = config.DiasParaPrioridadAlta ?? DiasUmbralAlertaAlta;
            var diasMedia = config.DiasParaPrioridadMedia ?? DiasUmbralAlertaMedia;
            var montoCritica = config.MontoParaPrioridadCritica ?? MontoUmbralAlertaCritica;
            var montoAlta = config.MontoParaPrioridadAlta ?? MontoUmbralAlertaAlta;
            var montoMedia = config.MontoParaPrioridadMedia ?? MontoUmbralAlertaMedia;

            if (diasMora > diasCritica || montoVencido > montoCritica)
                return PrioridadAlerta.Critica;
            if (diasMora > diasAlta || montoVencido > montoAlta)
                return PrioridadAlerta.Alta;
            if (diasMora > diasMedia || montoVencido > montoMedia)
                return PrioridadAlerta.Media;

            return PrioridadAlerta.Baja;
        }

        private TipoAlertaCobranza ObtenerTipoAlerta(int diasAtraso)
        {
            return diasAtraso > DiasMoraElevada ? TipoAlertaCobranza.MoraElevada : TipoAlertaCobranza.CuotaVencida;
        }

        private string GenerarMensajeAlerta(Cliente cliente, decimal montoVencido, int cuotasVencidas, int diasAtraso)
        {
            return $"Cliente {cliente.ToDisplayName()} tiene ${montoVencido:F2} en mora " +
                   $"con {cuotasVencidas} cuota(s) vencida(s) por {diasAtraso} día(s). " +
                   $"Documento: {cliente.NumeroDocumento}";
        }

        internal static List<ClienteMoraViewModel> AplicarFiltros(
            List<ClienteMoraViewModel> clientes,
            FiltrosBandejaClientes filtros,
            DateTime hoy)
        {
            if (filtros.Prioridad.HasValue)
                clientes = clientes.Where(c => c.PrioridadMaxima == filtros.Prioridad.Value).ToList();
            if (filtros.EstadoGestion.HasValue)
                clientes = clientes.Where(c => c.EstadoGestion == filtros.EstadoGestion.Value).ToList();
            if (filtros.DiasMinAtraso.HasValue)
                clientes = clientes.Where(c => c.DiasMaxAtraso >= filtros.DiasMinAtraso.Value).ToList();
            if (filtros.DiasMaxAtraso.HasValue)
                clientes = clientes.Where(c => c.DiasMaxAtraso <= filtros.DiasMaxAtraso.Value).ToList();
            if (filtros.MontoMinVencido.HasValue)
                clientes = clientes.Where(c => c.MontoVencido >= filtros.MontoMinVencido.Value).ToList();
            if (filtros.MontoMaxVencido.HasValue)
                clientes = clientes.Where(c => c.MontoVencido <= filtros.MontoMaxVencido.Value).ToList();
            if (filtros.ConPromesaActiva == true)
                clientes = clientes.Where(c => c.TienePromesaActiva).ToList();
            if (filtros.ConAcuerdoActivo == true)
                clientes = clientes.Where(c => c.TieneAcuerdoActivo).ToList();
            if (filtros.SinContactoReciente == true && filtros.DiasSinContacto.HasValue)
            {
                var fechaLimite = hoy.AddDays(-filtros.DiasSinContacto.Value);
                clientes = clientes.Where(c => c.UltimoContacto == null || c.UltimoContacto < fechaLimite).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filtros.Busqueda))
            {
                var busqueda = NormalizarBusqueda(filtros.Busqueda);
                clientes = clientes.Where(c =>
                    NormalizarBusqueda(c.Nombre).Contains(busqueda) ||
                    NormalizarBusqueda(c.Documento).Contains(busqueda)).ToList();
            }

            return clientes;
        }

        internal static List<ClienteMoraViewModel> AplicarOrdenamiento(
            List<ClienteMoraViewModel> clientes,
            string? ordenamiento)
        {
            return ordenamiento switch
            {
                "PrioridadDesc" => clientes.OrderByDescending(c => c.PrioridadMaxima).ThenByDescending(c => c.DiasMaxAtraso).ToList(),
                "PrioridadAsc" => clientes.OrderBy(c => c.PrioridadMaxima).ThenBy(c => c.DiasMaxAtraso).ToList(),
                "DiasAtrasoDesc" => clientes.OrderByDescending(c => c.DiasMaxAtraso).ToList(),
                "DiasAtrasoAsc" => clientes.OrderBy(c => c.DiasMaxAtraso).ToList(),
                "MontoDesc" => clientes.OrderByDescending(c => c.MontoTotal).ToList(),
                "MontoAsc" => clientes.OrderBy(c => c.MontoTotal).ToList(),
                "NombreAsc" => clientes.OrderBy(c => c.Nombre).ToList(),
                "NombreDesc" => clientes.OrderByDescending(c => c.Nombre).ToList(),
                _ => clientes.OrderByDescending(c => c.PrioridadMaxima).ThenByDescending(c => c.DiasMaxAtraso).ToList()
            };
        }

        private static string NormalizarBusqueda(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return new string(
                s.Normalize(NormalizationForm.FormD)
                 .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                 .ToArray()
            ).ToLowerInvariant();
        }

        #endregion
    }
}
