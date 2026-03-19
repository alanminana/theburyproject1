using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio de gestión de promesas de pago.
    /// - Registro con auditoría completa
    /// - Verificación de cumplimiento
    /// - Integración con historial de contactos
    /// </summary>
    public sealed class PromesaPagoService : IPromesaPagoService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PromesaPagoService> _logger;

        public PromesaPagoService(
            AppDbContext context,
            ILogger<PromesaPagoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Registro de Promesas

        /// <inheritdoc />
        public async Task<ResultadoPromesaPago> RegistrarPromesaAsync(
            PromesaPagoDto promesa,
            string gestorId)
        {
            ArgumentNullException.ThrowIfNull(promesa);

            if (string.IsNullOrWhiteSpace(gestorId))
                return ResultadoPromesaPago.ConError("El gestorId es requerido");

            if (promesa.FechaPromesa < DateTime.Today)
                return ResultadoPromesaPago.ConError("La fecha de promesa no puede ser pasada");

            if (promesa.MontoPromesa <= 0)
                return ResultadoPromesaPago.ConError("El monto de promesa debe ser mayor a cero");

            try
            {
                // Obtener la alerta
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == promesa.AlertaCobranzaId && !a.IsDeleted);

                if (alerta == null)
                    return ResultadoPromesaPago.ConError($"Alerta {promesa.AlertaCobranzaId} no encontrada");

                if (alerta.Resuelta)
                    return ResultadoPromesaPago.ConError("La alerta ya está resuelta");

                // Crear historial de contacto
                var historial = new HistorialContacto
                {
                    AlertaCobranzaId = alerta.Id,
                    ClienteId = alerta.ClienteId,
                    GestorId = gestorId,
                    FechaContacto = DateTime.UtcNow,
                    TipoContacto = promesa.TipoContacto,
                    Resultado = ResultadoContacto.PromesaPago,
                    Telefono = promesa.TipoContacto == TipoContacto.LlamadaTelefonica ||
                               promesa.TipoContacto == TipoContacto.WhatsApp
                               ? promesa.MedioContacto : null,
                    Email = promesa.TipoContacto == TipoContacto.Email
                            ? promesa.MedioContacto : null,
                    Observaciones = promesa.Observaciones,
                    FechaPromesaPago = promesa.FechaPromesa,
                    MontoPromesaPago = promesa.MontoPromesa,
                    CreatedBy = gestorId
                };

                _context.HistorialContactos.Add(historial);

                // Actualizar la alerta
                alerta.EstadoGestion = EstadoGestionCobranza.PromesaPago;
                alerta.FechaPromesaPago = promesa.FechaPromesa;
                alerta.MontoPromesaPago = promesa.MontoPromesa;
                alerta.UpdatedAt = DateTime.UtcNow;
                alerta.UpdatedBy = gestorId;

                // Si no tiene gestor asignado, asignar al que registra
                if (string.IsNullOrEmpty(alerta.GestorAsignadoId))
                {
                    alerta.GestorAsignadoId = gestorId;
                    alerta.FechaAsignacion = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Promesa de pago registrada: Alerta={AlertaId}, Cliente={ClienteId}, " +
                    "Fecha={FechaPromesa:yyyy-MM-dd}, Monto={Monto:C}, Gestor={GestorId}",
                    alerta.Id, alerta.ClienteId, promesa.FechaPromesa, promesa.MontoPromesa, gestorId);

                return new ResultadoPromesaPago
                {
                    AlertaId = alerta.Id,
                    HistorialContactoId = historial.Id,
                    FechaPromesa = promesa.FechaPromesa,
                    MontoPromesa = promesa.MontoPromesa,
                    FechaLimite = promesa.FechaPromesa,
                    Exitoso = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registrando promesa de pago para alerta {AlertaId}",
                    promesa.AlertaCobranzaId);
                return ResultadoPromesaPago.ConError(ex.Message);
            }
        }

        #endregion

        #region Consulta de Promesas Vencidas

        /// <inheritdoc />
        public async Task<IReadOnlyList<PromesaVencidaDto>> ObtenerPromesasVencidasAsync(
            ConfiguracionMora config,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(config);

            var fecha = fechaCalculo ?? DateTime.Today;
            var diasTolerancia = config.DiasParaCumplirPromesa ?? 0;

            var promesasVencidas = await _context.AlertasCobranza
                .Include(a => a.Cliente)
                .Where(a => !a.IsDeleted &&
                           !a.Resuelta &&
                           a.EstadoGestion == EstadoGestionCobranza.PromesaPago &&
                           a.FechaPromesaPago.HasValue &&
                           a.FechaPromesaPago.Value.AddDays(diasTolerancia) < fecha)
                .Select(a => new PromesaVencidaDto
                {
                    AlertaId = a.Id,
                    ClienteId = a.ClienteId,
                    ClienteNombre = a.Cliente != null
                        ? $"{a.Cliente.Apellido}, {a.Cliente.Nombre}"
                        : "Desconocido",
                    FechaPromesa = a.FechaPromesaPago!.Value,
                    MontoPromesa = a.MontoPromesaPago ?? 0,
                    DiasVencida = (int)(fecha - a.FechaPromesaPago!.Value.AddDays(diasTolerancia)).TotalDays,
                    SaldoActual = a.MontoTotal
                })
                .OrderByDescending(p => p.DiasVencida)
                .ToListAsync();

            return promesasVencidas;
        }

        #endregion

        #region Gestión de Estado de Promesas

        /// <inheritdoc />
        public async Task<bool> MarcarPromesaIncumplidaAsync(
            int alertaId,
            string gestorId,
            string? observaciones = null)
        {
            if (string.IsNullOrWhiteSpace(gestorId))
                return false;

            try
            {
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);

                if (alerta == null)
                    return false;

                if (alerta.EstadoGestion != EstadoGestionCobranza.PromesaPago)
                    return false;

                // Registrar en historial
                var historial = new HistorialContacto
                {
                    AlertaCobranzaId = alerta.Id,
                    ClienteId = alerta.ClienteId,
                    GestorId = gestorId,
                    FechaContacto = DateTime.UtcNow,
                    TipoContacto = TipoContacto.NotaInterna,
                    Resultado = ResultadoContacto.PromesaIncumplida,
                    Observaciones = observaciones ??
                        $"Promesa de pago del {alerta.FechaPromesaPago:dd/MM/yyyy} por " +
                        $"{alerta.MontoPromesaPago:C} marcada como incumplida",
                    CreatedBy = gestorId
                };

                _context.HistorialContactos.Add(historial);

                // Actualizar alerta
                alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
                alerta.Observaciones = (alerta.Observaciones ?? "") +
                    $"\n[{DateTime.UtcNow:yyyy-MM-dd HH:mm}] Promesa incumplida - {gestorId}";
                alerta.UpdatedAt = DateTime.UtcNow;
                alerta.UpdatedBy = gestorId;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Promesa marcada como incumplida: Alerta={AlertaId}, Gestor={GestorId}",
                    alertaId, gestorId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marcando promesa incumplida para alerta {AlertaId}", alertaId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> MarcarPromesaCumplidaAsync(
            int alertaId,
            string gestorId,
            decimal montoPagado)
        {
            if (string.IsNullOrWhiteSpace(gestorId))
                return false;

            if (montoPagado <= 0)
                return false;

            try
            {
                var alerta = await _context.AlertasCobranza
                    .FirstOrDefaultAsync(a => a.Id == alertaId && !a.IsDeleted);

                if (alerta == null)
                    return false;

                // Registrar en historial
                var historial = new HistorialContacto
                {
                    AlertaCobranzaId = alerta.Id,
                    ClienteId = alerta.ClienteId,
                    GestorId = gestorId,
                    FechaContacto = DateTime.UtcNow,
                    TipoContacto = TipoContacto.NotaInterna,
                    Resultado = ResultadoContacto.PagoRealizado,
                    Observaciones = $"Pago realizado por {montoPagado:C}. Promesa cumplida.",
                    CreatedBy = gestorId
                };

                _context.HistorialContactos.Add(historial);

                // Actualizar alerta
                var cumplioTotal = montoPagado >= (alerta.MontoPromesaPago ?? alerta.MontoTotal);

                if (cumplioTotal)
                {
                    alerta.EstadoGestion = EstadoGestionCobranza.Regularizado;
                    alerta.Resuelta = true;
                    alerta.FechaResolucion = DateTime.UtcNow;
                    alerta.MotivoResolucion = $"Pago realizado: {montoPagado:C}";
                }
                else
                {
                    // Pago parcial, vuelve a gestión
                    alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
                    alerta.MontoVencido -= montoPagado;
                    alerta.MontoTotal = alerta.MontoVencido + alerta.MontoMoraCalculada;
                }

                alerta.FechaPromesaPago = null;
                alerta.MontoPromesaPago = null;
                alerta.UpdatedAt = DateTime.UtcNow;
                alerta.UpdatedBy = gestorId;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Promesa cumplida: Alerta={AlertaId}, MontoPagado={Monto:C}, Completo={Completo}",
                    alertaId, montoPagado, cumplioTotal);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marcando promesa cumplida para alerta {AlertaId}", alertaId);
                return false;
            }
        }

        #endregion

        #region Verificación de Vencimientos

        /// <inheritdoc />
        public bool EstaProximaAVencer(
            AlertaCobranza alerta,
            int diasAnticipacion,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(alerta);

            if (alerta.EstadoGestion != EstadoGestionCobranza.PromesaPago)
                return false;

            if (!alerta.FechaPromesaPago.HasValue)
                return false;

            var fecha = fechaCalculo ?? DateTime.Today;
            var diasRestantes = (alerta.FechaPromesaPago.Value - fecha).Days;

            return diasRestantes >= 0 && diasRestantes <= diasAnticipacion;
        }

        /// <inheritdoc />
        public bool EstaVencida(AlertaCobranza alerta, DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(alerta);

            if (alerta.EstadoGestion != EstadoGestionCobranza.PromesaPago)
                return false;

            if (!alerta.FechaPromesaPago.HasValue)
                return false;

            var fecha = fechaCalculo ?? DateTime.Today;
            return fecha > alerta.FechaPromesaPago.Value;
        }

        /// <inheritdoc />
        public int DiasParaVencimiento(AlertaCobranza alerta, DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(alerta);

            if (!alerta.FechaPromesaPago.HasValue)
                return int.MaxValue;

            var fecha = fechaCalculo ?? DateTime.Today;
            return (alerta.FechaPromesaPago.Value - fecha).Days;
        }

        #endregion
    }
}
