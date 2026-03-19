using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// ✅ REFACTORIZADO: Consolidado, sin duplicaciones, optimizado
    /// </summary>
    public class MoraService : IMoraService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<MoraService> _logger;
        private ConfiguracionMora? _configuracion;

        public MoraService(
            AppDbContext context,
            IMapper mapper,
            ILogger<MoraService> logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
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

        public async Task ProcesarMoraAsync()
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
                var hoy = DateTime.Today;
                var fechaLimite = hoy.AddDays(-(config.DiasGracia ?? 0));

                // ✅ OPTIMIZADO: Obtener cuotas vencidas en UN solo query (sin N+1)
                var cuotasVencidas = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr!.Cliente)
                    .Where(c => !c.IsDeleted &&
                           !c.Credito!.IsDeleted &&
                           !c.Credito!.Cliente!.IsDeleted &&
                           c.Estado == EstadoCuota.Pendiente &&
                           c.FechaVencimiento < fechaLimite)
                    .ToListAsync();

                log.CuotasProcesadas = cuotasVencidas.Count;

                // ✅ OPTIMIZADO: Agrupar por crédito para evitar múltiples queries
                var cuotasPorCredito = cuotasVencidas.GroupBy(c => c.CreditoId).ToList();
                int alertasCreadas = 0;

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

                        // Verificar si ya existe alerta activa (evitar duplicados)
                        var alertaExistente = await _context.AlertasCobranza
                            .AnyAsync(a => a.CreditoId == creditoId &&
                                      !a.Resuelta &&
                                      a.Tipo == TipoAlertaCobranza.CuotaVencida &&
                                      !a.IsDeleted);

                        if (alertaExistente)
                            continue;

                        // Calcular datos de mora
                        var diasMora = (hoy - cuotasDelCredito.Min(c => c.FechaVencimiento)).Days;
                        var montoVencido = cuotasDelCredito.Sum(c => c.MontoTotal - c.MontoPagado);
                        var moraCalculada = CalcularMora(montoVencido, diasMora, config);
                        var prioridad = DeterminarPrioridad(diasMora, montoVencido, config);

                        // ✅ MEJORADO: Crear alerta con información completa
                        var alerta = new AlertaCobranza
                        {
                            CreditoId = creditoId,
                            ClienteId = cliente.Id,
                            Tipo = ObtenerTipoAlerta(diasMora),
                            Prioridad = prioridad,
                            Mensaje = GenerarMensajeAlerta(cliente, montoVencido, cuotasDelCredito.Count, diasMora),
                            MontoVencido = montoVencido,
                            CuotasVencidas = cuotasDelCredito.Count,
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

                // Generar alertas de próximos vencimientos
                await GenerarAlertasProximosVencimientosAsync(config);

                // Actualizar configuración
                config.UltimaEjecucion = DateTime.UtcNow;
                log.AlertasGeneradas = alertasCreadas;
                log.Exitoso = true;
                log.Mensaje = $"Proceso completado. {cuotasVencidas.Count} cuotas procesadas, " +
                    $"{alertasCreadas} alertas generadas, ${log.TotalMora:F2} en mora.";

                await _context.SaveChangesAsync();

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

        private async Task GenerarAlertasProximosVencimientosAsync(ConfiguracionMora config)
        {
            try
            {
                var hoy = DateTime.Today;
                var diasAntesAlerta = 5; // ✅ TODO: Hacer configurable en ConfiguracionMora
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
                    .ToListAsync();

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
                        .ToListAsync())
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
                        Mensaje = $"Cliente {cliente.NombreCompleto} tiene cuota por vencer en {diasRestantes} días",
                        MontoVencido = cuota.MontoTotal,
                        CuotasVencidas = 1,
                        FechaAlerta = now,
                        Resuelta = false,
                        CreatedAt = now
                    };

                    _context.AlertasCobranza.Add(alerta);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Alertas de próximos vencimientos generadas");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al generar alertas de próximos vencimientos");
            }
        }

        #endregion

        #region Gestión de Alertas

        public async Task<List<AlertaCobranzaViewModel>> GetAlertasActivasAsync()
        {
            try
            {
                var alertas = await _context.AlertasCobranza
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => !a.IsDeleted && !a.Resuelta)
                    .OrderByDescending(a => a.Prioridad)
                    .ThenBy(a => a.FechaAlerta)
                    .ToListAsync();

                // ✅ OPTIMIZADO: Usar AutoMapper en lugar de mapeo manual
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
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
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
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
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

        public async Task<ConfiguracionMora> UpdateConfiguracionExpandidaAsync(ConfiguracionMoraExpandidaViewModel viewModel)
        {
            try
            {
                var config = await _context.ConfiguracionesMora
                    .FirstOrDefaultAsync(c => c.Id == viewModel.Id && !c.IsDeleted);
                if (config == null)
                    throw new InvalidOperationException("Configuración no encontrada");

                // Mapear todos los campos del ViewModel
                config.TipoTasaMora = viewModel.TipoTasaMora;
                config.TasaMoraBase = viewModel.TasaMoraBase;
                config.BaseCalculoMora = viewModel.BaseCalculoMora;
                config.DiasGracia = viewModel.DiasGracia;
                config.EscalonamientoActivo = viewModel.EscalonamientoActivo;
                config.TasaPrimerMes = viewModel.TasaPrimerMes;
                config.TasaSegundoMes = viewModel.TasaSegundoMes;
                config.TasaTercerMesEnAdelante = viewModel.TasaTercerMesEnAdelante;
                config.TopeMaximoMoraActivo = viewModel.TopeMaximoMoraActivo;
                config.TipoTopeMora = viewModel.TipoTopeMora;
                config.ValorTopeMora = viewModel.ValorTopeMora;
                config.MoraMinima = viewModel.MoraMinima;
                config.DiasParaPrioridadMedia = viewModel.DiasParaPrioridadMedia;
                config.DiasParaPrioridadAlta = viewModel.DiasParaPrioridadAlta;
                config.DiasParaPrioridadCritica = viewModel.DiasParaPrioridadCritica;
                config.MontoParaPrioridadMedia = viewModel.MontoParaPrioridadMedia;
                config.MontoParaPrioridadAlta = viewModel.MontoParaPrioridadAlta;
                config.MontoParaPrioridadCritica = viewModel.MontoParaPrioridadCritica;
                config.ProcesoAutomaticoActivo = viewModel.ProcesoAutomaticoActivo;
                config.HoraEjecucionDiaria = viewModel.HoraEjecucionDiaria;
                config.AlertasPreventivasActivas = viewModel.AlertasPreventivasActivas;
                config.DiasAntesAlertaPreventiva = viewModel.DiasAntesAlertaPreventiva;
                config.CambiarEstadoCuotaAuto = viewModel.CambiarEstadoCuotaAuto;
                config.ActualizarMoraAutomaticamente = viewModel.ActualizarMoraAutomaticamente;
                config.NotificacionesActivas = viewModel.NotificacionesActivas;
                config.WhatsAppActivo = viewModel.WhatsAppActivo;
                config.EmailActivo = viewModel.EmailActivo;
                config.CanalPreferido = viewModel.CanalPreferido;
                config.NotificarProximoVencimiento = viewModel.NotificarProximoVencimiento;
                config.DiasAntesNotificacionPreventiva = viewModel.DiasAntesNotificacionPreventiva;
                config.NotificarCuotaVencida = viewModel.NotificarCuotaVencida;
                config.NotificarMoraAcumulada = viewModel.NotificarMoraAcumulada;
                config.FrecuenciaRecordatorioMora = viewModel.FrecuenciaRecordatorioMora;
                config.MaximoNotificacionesDiarias = viewModel.MaximoNotificacionesDiarias;
                config.MaximoNotificacionesPorCuota = viewModel.MaximoNotificacionesPorCuota;
                config.HoraInicioEnvio = viewModel.HoraInicioEnvio;
                config.HoraFinEnvio = viewModel.HoraFinEnvio;
                config.EnviarFinDeSemana = viewModel.EnviarFinDeSemana;
                config.DiasMaximosSinGestion = viewModel.DiasMaximosSinGestion;
                config.DiasParaCumplirPromesa = viewModel.DiasParaCumplirPromesa;
                config.MaximoCuotasAcuerdo = viewModel.MaximoCuotasAcuerdo;
                config.PorcentajeMinimoEntrega = viewModel.PorcentajeMinimoEntrega;
                config.PermitirCondonacionMora = viewModel.PermitirCondonacionMora;
                config.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
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
                var hoy = DateTime.Today;

                // Base query: clientes con alertas activas
                var queryBase = _context.AlertasCobranza
                    .Include(a => a.Cliente)
                    .Include(a => a.Credito)
                    .Where(a => !a.IsDeleted && !a.Resuelta && a.Cliente != null && !a.Cliente.IsDeleted)
                    .AsQueryable();

                // Agrupar por cliente
                var clientesData = await queryBase
                    .GroupBy(a => new { a.ClienteId, a.Cliente!.NombreCompleto, a.Cliente.NumeroDocumento, a.Cliente.Telefono, a.Cliente.Email })
                    .Select(g => new
                    {
                        ClienteId = g.Key.ClienteId,
                        Nombre = g.Key.NombreCompleto,
                        Documento = g.Key.NumeroDocumento,
                        Telefono = g.Key.Telefono,
                        Email = g.Key.Email,
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
                    .ToListAsync();

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

                // Aplicar filtros
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
                    var busqueda = filtros.Busqueda.ToLower();
                    clientes = clientes.Where(c =>
                        c.Nombre.ToLower().Contains(busqueda) ||
                        c.Documento.ToLower().Contains(busqueda)).ToList();
                }

                // Ordenamiento
                clientes = filtros.Ordenamiento switch
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

                var hoy = DateTime.Today;

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
                    ProximoContacto = historial.Where(h => h.ProximoContacto >= hoy).OrderBy(h => h.ProximoContacto).FirstOrDefault()?.ProximoContacto,
                    PromesaActiva = promesas.Any(),
                    AcuerdoActivo = acuerdos.Any(a => a.Estado == EstadoAcuerdo.Activo)
                };

                return new FichaMoraViewModel
                {
                    ClienteId = clienteId,
                    NombreCliente = cliente.NombreCompleto ?? "",
                    DocumentoCliente = cliente.NumeroDocumento ?? "",
                    Telefono = cliente.Telefono,
                    TelefonoLaboral = cliente.TelefonoLaboral,
                    Email = cliente.Email,
                    Direccion = cliente.Direccion,
                    Resumen = resumen,
                    Creditos = creditos,
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

        public async Task<List<CreditoMoraViewModel>> GetCreditosEnMoraAsync(int clienteId)
        {
            try
            {
                var hoy = DateTime.Today;
                var creditos = await _context.Creditos
                    .Include(c => c.Cuotas)
                    .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
                    .ToListAsync();

                var resultado = new List<CreditoMoraViewModel>();

                foreach (var credito in creditos)
                {
                    var cuotasVencidas = credito.Cuotas?
                        .Where(c => !c.IsDeleted && c.Estado == EstadoCuota.Pendiente && c.FechaVencimiento < hoy)
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
                        MontoMora = c.MontoPunitorio,
                        DiasAtraso = (hoy - c.FechaVencimiento).Days
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
                        DiasAtraso = cuotasVencidas.Max(c => (hoy - c.FechaVencimiento).Days),
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
                var historial = new HistorialContacto
                {
                    ClienteId = contacto.ClienteId,
                    AlertaCobranzaId = contacto.AlertaId ?? 0,
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
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == promesa.AlertaId && !a.IsDeleted);
                if (alerta == null)
                    return false;

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
                var hoy = DateTime.Today;
                var alertasConPromesa = await _context.AlertasCobranza
                    .Where(a => a.ClienteId == clienteId && 
                           !a.IsDeleted && 
                           !a.Resuelta && 
                           a.FechaPromesaPago != null)
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

        #endregion

        #region Acuerdos de Pago

        public async Task<int> CrearAcuerdoPagoAsync(CrearAcuerdoViewModel acuerdo, string gestorId)
        {
            try
            {
                var config = await GetConfiguracionAsync();

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

                // Validar condonación
                if (acuerdo.MontoCondonar > 0 && !config.PermitirCondonacionMora)
                    throw new InvalidOperationException("No está permitida la condonación de mora");

                var nuevoAcuerdo = new AcuerdoPago
                {
                    AlertaCobranzaId = acuerdo.AlertaId,
                    ClienteId = acuerdo.ClienteId,
                    CreditoId = acuerdo.CreditoId,
                    NumeroAcuerdo = $"ACU-{DateTime.UtcNow:yyyyMMddHHmmss}-{acuerdo.ClienteId}",
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
                await _context.SaveChangesAsync();

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

        public async Task<List<AcuerdoPagoResumenViewModel>> GetAcuerdosPagoAsync(int clienteId)
        {
            try
            {
                var acuerdos = await _context.AcuerdosPago
                    .Include(a => a.Cuotas)
                    .Where(a => a.ClienteId == clienteId && !a.IsDeleted)
                    .OrderByDescending(a => a.FechaCreacion)
                    .ToListAsync();

                var hoy = DateTime.Today;
                return acuerdos.Select(a =>
                {
                    var cuotasPagadas = a.Cuotas?.Count(c => c.Estado == EstadoCuotaAcuerdo.Pagada) ?? 0;
                    var montoPagado = (a.Cuotas?.Where(c => c.Estado == EstadoCuotaAcuerdo.Pagada).Sum(c => c.MontoTotal) ?? 0) +
                                     (a.EntregaInicialPagada ? a.MontoEntregaInicial : 0);
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
                        ProximaFechaVencimiento = proximaCuota?.FechaVencimiento
                    };
                }).ToList();
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
                var hoy = DateTime.Today;
                var inicioSemana = hoy.AddDays(-(int)hoy.DayOfWeek);
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
                var config = await GetConfiguracionAsync();

                // Alertas activas (no resueltas)
                var alertas = await _context.AlertasCobranza
                    .Where(a => !a.IsDeleted && !a.Resuelta)
                    .Include(a => a.Cliente)
                    .ToListAsync();

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
                var promesasActivas = alertas.Count(a => a.FechaPromesaPago >= hoy);
                var promesasVencenHoy = alertas.Count(a => a.FechaPromesaPago?.Date == hoy);
                var montoPromesasHoy = alertas.Where(a => a.FechaPromesaPago?.Date == hoy).Sum(a => a.MontoPromesaPago ?? 0);
                var promesasVencidas = alertas.Count(a => a.FechaPromesaPago < hoy && a.FechaPromesaPago != null);

                // Acuerdos activos (calcular saldo pendiente a partir de cuotas)
                var acuerdos = await _context.AcuerdosPago
                    .Include(a => a.Cuotas)
                    .Where(a => a.Estado == EstadoAcuerdo.Activo && !a.IsDeleted)
                    .ToListAsync();
                var acuerdosActivos = acuerdos.Count;
                var montoAcuerdosActivos = acuerdos.Sum(a => 
                    a.MontoTotalAcuerdo - (a.Cuotas?.Where(c => c.Estado == EstadoCuotaAcuerdo.Pagada).Sum(c => c.MontoTotal) ?? 0) - 
                    (a.EntregaInicialPagada ? a.MontoEntregaInicial : 0));

                // Contactos hoy y semana
                var contactosHoy = await _context.HistorialContactos
                    .CountAsync(h => h.FechaContacto.Date == hoy && !h.IsDeleted);
                var contactosSemana = await _context.HistorialContactos
                    .CountAsync(h => h.FechaContacto >= inicioSemana && !h.IsDeleted);

                // Cobros hoy (cuotas pagadas hoy)
                var cuotasPagadasHoy = await _context.Cuotas
                    .Where(c => c.FechaPago.HasValue && c.FechaPago.Value.Date == hoy && !c.IsDeleted)
                    .ToListAsync();
                var cantidadCobrosHoy = cuotasPagadasHoy.Count;
                var montoCobradoHoy = cuotasPagadasHoy.Sum(c => c.MontoPagado);

                // Tasa de recupero del mes (monto cobrado / monto vencido al inicio del mes)
                var cobrosMes = await _context.Cuotas
                    .Where(c => c.FechaPago.HasValue && c.FechaPago.Value >= inicioMes && !c.IsDeleted)
                    .SumAsync(c => c.MontoPagado);
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

        // ✅ NUEVO: Consolidar cálculo de mora en un lugar
        private decimal CalcularMora(decimal montoVencido, int diasAtraso, ConfiguracionMora config)
        {
            if (diasAtraso <= 0 || montoVencido <= 0)
                return 0;

            var tasaBase = config.TasaMoraBase ?? 0m;
            var tasaDiaria = tasaBase / 100m / 30m; // Convertir a tasa diaria
            var mora = montoVencido * tasaDiaria * diasAtraso;

            return Math.Round(mora, 2);
        }

        // ✅ MEJORADO: Usar configuración en lugar de constantes hardcodeadas
        private PrioridadAlerta DeterminarPrioridad(int diasMora, decimal montoVencido, ConfiguracionMora config)
        {
            // Umbrales por defecto (TODO: Hacer configurables)
            const int diasAlertaCritica = 30;
            const int diasAlertaAlta = 15;
            const int diasAlertaMedia = 7;
            const decimal montoAlertaCritica = 50000;
            const decimal montoAlertaAlta = 30000;
            const decimal montoAlertaMedia = 15000;

            if (diasMora > diasAlertaCritica || montoVencido > montoAlertaCritica)
                return PrioridadAlerta.Critica;
            if (diasMora > diasAlertaAlta || montoVencido > montoAlertaAlta)
                return PrioridadAlerta.Alta;
            if (diasMora > diasAlertaMedia || montoVencido > montoAlertaMedia)
                return PrioridadAlerta.Media;

            return PrioridadAlerta.Baja;
        }

        private TipoAlertaCobranza ObtenerTipoAlerta(int diasAtraso)
        {
            return diasAtraso > 90 ? TipoAlertaCobranza.MoraElevada : TipoAlertaCobranza.CuotaVencida;
        }

        private string GenerarMensajeAlerta(Cliente cliente, decimal montoVencido, int cuotasVencidas, int diasAtraso)
        {
            return $"Cliente {cliente.NombreCompleto} tiene ${montoVencido:F2} en mora " +
                   $"con {cuotasVencidas} cuota(s) vencida(s) por {diasAtraso} día(s). " +
                   $"Documento: {cliente.NumeroDocumento}";
        }

        #endregion
    }
}
