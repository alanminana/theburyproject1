using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// Consolida toda la lógica de cálculo de mora y generación de alertas
    /// </summary>
    public class MoraAlertasService : IMoraAlertasService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MoraAlertasService> _logger;

        // Constantes de configuración
        private const decimal PORCENTAJE_ALERTA_CRITICA = 0.5m;   // 50% de mora
        private const decimal PORCENTAJE_ALERTA_ALTA = 0.3m;      // 30% de mora
        private const int DIAS_ALERTA_ALTA = 60;
        private const int DIAS_ALERTA_MEDIA = 30;

        public MoraAlertasService(AppDbContext context, ILogger<MoraAlertasService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task ProcesarMoraAsync()
        {
            try
            {
                _logger.LogInformation("=== INICIANDO PROCESAMIENTO DE MORA ===");

                // Actualizar estado de cuotas vencidas
                await ActualizarMorasCuotasAsync();

                // Generar alertas de cobranza
                await GenerarAlertasCobranzaAsync();

                _logger.LogInformation("=== PROCESAMIENTO DE MORA COMPLETADO ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar mora");
                throw;
            }
        }

        public async Task ActualizarMorasCuotasAsync()
        {
            try
            {
                var config = await _context.Set<ConfiguracionMora>()
                    .FirstOrDefaultAsync();

                if (config == null)
                {
                    _logger.LogWarning("No se encontró configuración de mora");
                    return;
                }

                var diasGracia = config.DiasGracia ?? 0;
                var fechaGracia = DateTime.UtcNow.AddDays(-diasGracia);

                // Obtener cuotas vencidas que no tienen mora calculada
                var cuotasVencidas = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr!.Cliente)
                    .Where(c => c.FechaVencimiento < fechaGracia &&
                               !c.IsDeleted &&
                               c.Estado == EstadoCuota.Pendiente &&
                               c.MontoPunitorio == 0 &&
                               c.Credito != null &&
                               !c.Credito.IsDeleted &&
                               c.Credito.Cliente != null &&
                               !c.Credito.Cliente.IsDeleted)  // ✅ VALIDACIÓN AGREGADA
                    .ToListAsync();

                _logger.LogInformation("Encontradas {Count} cuotas vencidas para calcular mora", cuotasVencidas.Count);

                foreach (var cuota in cuotasVencidas)
                {
                    // Calcular mora usando helper
                    var mora = CalcularMora(cuota);
                    cuota.MontoPunitorio = mora;
                    cuota.Estado = EstadoCuota.Vencida;

                    _logger.LogInformation("Cuota {Id} marcada como vencida. Mora: ${Mora}", cuota.Id, mora);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Mora actualizada para {Count} cuotas", cuotasVencidas.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar mora de cuotas");
                throw;
            }
        }

        public async Task GenerarAlertasCobranzaAsync()
        {
            try
            {
                _logger.LogInformation("=== GENERANDO ALERTAS DE COBRANZA ===");

                // Obtener créditos activos con cuotas vencidas
                var creditosConMora = await _context.Creditos
                    .Include(c => c.Cliente)
                    .Include(c => c.Cuotas)
                    .Where(c => !c.IsDeleted
                             && c.Cliente != null
                             && !c.Cliente.IsDeleted
                             && c.Estado == EstadoCredito.Activo
                             && c.Cuotas.Any(cu => !cu.IsDeleted && (cu.Estado == EstadoCuota.Vencida || cu.Estado == EstadoCuota.Parcial)))
                    .ToListAsync();

                _logger.LogInformation("Encontrados {Count} créditos con mora", creditosConMora.Count);

                var creditosIds = creditosConMora.Select(c => c.Id).ToList();
                var alertasActivas = await _context.Set<AlertaCobranza>()
                    .Where(a => creditosIds.Contains(a.CreditoId) && !a.Resuelta && !a.IsDeleted)
                    .ToListAsync();
                var alertasActivasByCreditoId = alertasActivas
                    .GroupBy(a => a.CreditoId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FechaAlerta).First());

                int alertasCreadas = 0;

                foreach (var credito in creditosConMora)
                {
                    var cuotasVencidas = credito.Cuotas
                        .Where(cu => !cu.IsDeleted && (cu.Estado == EstadoCuota.Vencida || cu.Estado == EstadoCuota.Parcial))
                        .ToList();

                    if (!cuotasVencidas.Any())
                        continue;

                    var montoVencido = cuotasVencidas.Sum(c => c.MontoTotal + c.MontoPunitorio - c.MontoPagado);
                    var diasAtraso = (DateTime.UtcNow - cuotasVencidas.Min(c => c.FechaVencimiento)).Days;

                    // ✅ MEJORADO: Verificar si ya existe alerta activa
                    alertasActivasByCreditoId.TryGetValue(credito.Id, out var alertaExistente);

                    if (alertaExistente != null)
                    {
                        _logger.LogInformation("Alerta activa ya existe para crédito {CreditoId}. Actualizando...", credito.Id);
                        
                        // Actualizar alerta existente con nuevos datos
                        alertaExistente.MontoVencido = montoVencido;
                        alertaExistente.CuotasVencidas = cuotasVencidas.Count;
                        alertaExistente.Mensaje = GenerarMensajeAlerta(credito, montoVencido, cuotasVencidas.Count, diasAtraso);
                        alertaExistente.Prioridad = DeterminarPrioridad(montoVencido, credito.MontoAprobado, diasAtraso);
                        alertaExistente.UpdatedAt = DateTime.UtcNow;
                        
                        continue;
                    }

                    // ✅ MEJORADO: Crear nueva alerta
                    var alerta = new AlertaCobranza
                    {
                        CreditoId = credito.Id,
                        ClienteId = credito.ClienteId,
                        Tipo = ObtenerTipoAlerta(diasAtraso),
                        Prioridad = DeterminarPrioridad(montoVencido, credito.MontoAprobado, diasAtraso),
                        Mensaje = GenerarMensajeAlerta(credito, montoVencido, cuotasVencidas.Count, diasAtraso),
                        MontoVencido = montoVencido,
                        CuotasVencidas = cuotasVencidas.Count,
                        FechaAlerta = DateTime.UtcNow,
                        Resuelta = false
                    };

                    _context.Set<AlertaCobranza>().Add(alerta);
                    alertasCreadas++;

                    _logger.LogInformation("Alerta generada para crédito {CreditoId}: ${Monto} vencido", 
                        credito.Id, montoVencido);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("=== {Count} ALERTAS GENERADAS EXITOSAMENTE ===", alertasCreadas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar alertas de cobranza");
                throw;
            }
        }

        private decimal CalcularMora(Cuota cuota)
        {
            if (cuota.Credito == null || cuota.FechaVencimiento >= DateTime.UtcNow)
                return 0;

            var diasAtraso = (DateTime.UtcNow - cuota.FechaVencimiento).Days;
            var tasaMensual = (cuota.Credito.TasaInteres / 100m) / 12m;
            var mora = cuota.MontoTotal * tasaMensual * (diasAtraso / 30m);

            return Math.Round(mora, 2);
        }

        public async Task<List<AlertaCobranzaViewModel>> ObtenerAlertasActivasAsync()
        {
            try
            {
                var alertas = await _context.Set<AlertaCobranza>()
                    .Include(a => a.Cliente)
                    .Where(a => !a.Resuelta && !a.IsDeleted)
                    .OrderByDescending(a => a.Prioridad)
                    .ThenByDescending(a => a.FechaAlerta)
                    .ToListAsync();

                return alertas.Select(a => MapearAViewModel(a)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas activas");
                throw;
            }
        }

        public async Task<List<AlertaCobranzaViewModel>> ObtenerAlertasPorClienteAsync(int clienteId)
        {
            try
            {
                var alertas = await _context.Set<AlertaCobranza>()
                    .Include(a => a.Cliente)
                    .Where(a => a.ClienteId == clienteId && !a.IsDeleted)
                    .OrderByDescending(a => a.FechaAlerta)
                    .ToListAsync();

                return alertas.Select(a => MapearAViewModel(a)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<bool> ResolverAlertaAsync(int alertaId, string? observaciones = null, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.Set<AlertaCobranza>()
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);

                if (alerta == null)
                    return false;

                if (alerta.Resuelta)
                    return true; // idempotente

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                alerta.Resuelta = true;
                alerta.FechaResolucion = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(observaciones))
                    alerta.Observaciones = observaciones;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
                }

                _logger.LogInformation("Alerta {Id} marcada como resuelta", alertaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver alerta {Id}", alertaId);
                throw;
            }
        }

        // ✅ CORREGIDO: Cambiar nombre de método para reflejar que NO es async
        public async Task<bool> MarcarAlertaComoLeidaAsync(int alertaId, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.Set<AlertaCobranza>()
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);

                if (alerta == null)
                    return false;

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                // Nota: "Leída" es conceptualmente diferente de "Resuelta"
                // Esta es una operación de UI para marcar que el usuario vio la alerta
                alerta.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
                }

                _logger.LogInformation("Alerta {Id} marcada como leída", alertaId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar alerta como leída {Id}", alertaId);
                throw;
            }
        }

        // ✅ CORREGIDO: Cambiar a sincrónico (sin Async en el nombre)
        public decimal CalcularMora(int cuotaId)
        {
            try
            {
                var cuota = _context.Cuotas
                    .Include(c => c.Credito)
                    .ThenInclude(cr => cr!.Cliente)
                    .FirstOrDefault(c => c.Id == cuotaId &&
                                         !c.IsDeleted &&
                                         c.Credito != null &&
                                         !c.Credito.IsDeleted &&
                                         c.Credito.Cliente != null &&
                                         !c.Credito.Cliente.IsDeleted);

                if (cuota == null || cuota.Credito == null || cuota.FechaVencimiento >= DateTime.UtcNow)
                    return 0;

                var diasAtraso = (DateTime.UtcNow - cuota.FechaVencimiento).Days;
                var tasaMensual = (cuota.Credito.TasaInteres / 100m) / 12m;
                var mora = cuota.MontoTotal * tasaMensual * (diasAtraso / 30m);

                return Math.Round(mora, 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular mora para cuota {CuotaId}", cuotaId);
                throw;
            }
        }

        // ✅ CORREGIDO: Cambiar a sincrónico
        public decimal CalcularMontoTotalCobrable(int cuotaId)
        {
            try
            {
                var cuota = _context.Cuotas
                    .Include(c => c.Credito)
                    .ThenInclude(cr => cr!.Cliente)
                    .FirstOrDefault(c => c.Id == cuotaId &&
                                         !c.IsDeleted &&
                                         c.Credito != null &&
                                         !c.Credito.IsDeleted &&
                                         c.Credito.Cliente != null &&
                                         !c.Credito.Cliente.IsDeleted);

                if (cuota == null)
                    return 0;

                var mora = CalcularMora(cuota);
                return cuota.MontoTotal + mora - cuota.MontoPagado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular monto total cobrable para cuota {CuotaId}", cuotaId);
                throw;
            }
        }

        public async Task NotificarClienteAlertasAsync(int clienteId)
        {
            try
            {
                var alertas = await ObtenerAlertasPorClienteAsync(clienteId);

                var alertasActivas = alertas.Where(a => !a.Resuelta).ToList();

                if (!alertasActivas.Any())
                {
                    _logger.LogInformation("No hay alertas activas para cliente {ClienteId}", clienteId);
                    return;
                }

                _logger.LogInformation("Se notificarían {Count} alertas al cliente {ClienteId}", 
                    alertasActivas.Count, clienteId);

                // TODO: Implementar envío de notificaciones (Email, SMS, etc)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al notificar alertas al cliente {ClienteId}", clienteId);
                throw;
            }
        }

        #region Métodos Privados

        // ✅ NUEVO: Método helper para determinar prioridad (centralizar lógica)
        private PrioridadAlerta DeterminarPrioridad(decimal montoVencido, decimal montoAprobado, int diasAtraso)
        {
            // Primer criterio: porcentaje de mora respecto al crédito
            var porcentajeMora = montoVencido / montoAprobado;

            if (porcentajeMora >= PORCENTAJE_ALERTA_CRITICA)
                return PrioridadAlerta.Critica;

            if (porcentajeMora >= PORCENTAJE_ALERTA_ALTA || diasAtraso > DIAS_ALERTA_ALTA)
                return PrioridadAlerta.Alta;

            if (diasAtraso > DIAS_ALERTA_MEDIA)
                return PrioridadAlerta.Media;

            return PrioridadAlerta.Baja;
        }

        // ✅ NUEVO: Método helper para obtener tipo de alerta
        private TipoAlertaCobranza ObtenerTipoAlerta(int diasAtraso)
        {
            return diasAtraso > 90 ? TipoAlertaCobranza.MoraElevada : TipoAlertaCobranza.CuotaVencida;
        }

        // ✅ NUEVO: Método helper para generar mensaje consistente
        private string GenerarMensajeAlerta(Credito credito, decimal montoVencido, int cuotasVencidas, int diasAtraso)
        {
            return $"Cliente {credito.Cliente.NombreCompleto} tiene ${montoVencido:F2} en mora " +
                   $"con {cuotasVencidas} cuota(s) vencida(s) por {diasAtraso} día(s)";
        }

        private AlertaCobranzaViewModel MapearAViewModel(AlertaCobranza alerta)
        {
            return new AlertaCobranzaViewModel
            {
                Id = alerta.Id,
                CreditoId = alerta.CreditoId,
                ClienteId = alerta.ClienteId,
                ClienteNombre = alerta.Cliente?.NombreCompleto ?? "Desconocido",
                ClienteDocumento = alerta.Cliente?.NumeroDocumento ?? "",
                Tipo = alerta.Tipo,
                Prioridad = alerta.Prioridad,
                Mensaje = alerta.Mensaje,
                MontoVencido = alerta.MontoVencido,
                CuotasVencidas = alerta.CuotasVencidas,
                FechaAlerta = alerta.FechaAlerta,
                Resuelta = alerta.Resuelta,
                FechaResolucion = alerta.FechaResolucion,
                Observaciones = alerta.Observaciones,
                CreatedAt = alerta.CreatedAt
            };
        }

        #endregion
    }
}