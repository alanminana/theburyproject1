using Microsoft.Extensions.Logging;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio de automatización de cobranza por tramos.
    /// - Genera tramos desde configuración
    /// - Determina acciones según días de atraso
    /// - Procesa alertas ejecutando acciones automáticas
    /// </summary>
    public sealed class CobranzaAutomatizacionService : ICobranzaAutomatizacionService
    {
        private readonly ILogger<CobranzaAutomatizacionService> _logger;

        public CobranzaAutomatizacionService(ILogger<CobranzaAutomatizacionService> logger)
        {
            _logger = logger;
        }

        #region Generación de Tramos

        /// <inheritdoc />
        public IReadOnlyList<TramoCobranza> GenerarTramos(ConfiguracionMora config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var tramos = new List<TramoCobranza>();

            // Tramo 1: Preventivo (antes del vencimiento)
            if (config.AlertasPreventivasActivas && config.DiasAntesAlertaPreventiva.HasValue)
            {
                tramos.Add(new TramoCobranza
                {
                    Nombre = "Preventivo",
                    DiasDesde = -config.DiasAntesAlertaPreventiva.Value,
                    DiasHasta = 0,
                    Prioridad = PrioridadAlerta.Baja,
                    Acciones = new[]
                    {
                        new AccionAutomatica
                        {
                            Tipo = TipoAccionAutomatica.GenerarAlerta,
                            DiaEjecucion = -config.DiasAntesAlertaPreventiva.Value,
                            Descripcion = "Generar alerta preventiva"
                        }
                    }
                });
            }

            // Tramo 2: Gracia (días 1 a DiasGracia)
            var diasGracia = config.DiasGracia ?? 0;
            if (diasGracia > 0)
            {
                tramos.Add(new TramoCobranza
                {
                    Nombre = "Período de Gracia",
                    DiasDesde = 1,
                    DiasHasta = diasGracia,
                    Prioridad = PrioridadAlerta.Baja,
                    Acciones = Array.Empty<AccionAutomatica>() // Sin acciones en gracia
                });
            }

            // Tramo 3: Prioridad Baja (primeros días después de gracia)
            var inicioMora = diasGracia + 1;
            var finTramoMedia = config.DiasParaPrioridadMedia ?? 15;

            tramos.Add(new TramoCobranza
            {
                Nombre = "Mora Inicial",
                DiasDesde = inicioMora,
                DiasHasta = finTramoMedia - 1,
                Prioridad = PrioridadAlerta.Baja,
                Acciones = new[]
                {
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.GenerarAlerta,
                        DiaEjecucion = inicioMora,
                        Descripcion = "Generar alerta de mora inicial"
                    },
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.CambiarEstadoCuota,
                        DiaEjecucion = inicioMora,
                        Descripcion = "Cambiar estado cuota a Vencida"
                    }
                }
            });

            // Tramo 4: Prioridad Media
            var finTramoAlta = config.DiasParaPrioridadAlta ?? 30;

            tramos.Add(new TramoCobranza
            {
                Nombre = "Mora Media",
                DiasDesde = finTramoMedia,
                DiasHasta = finTramoAlta - 1,
                Prioridad = PrioridadAlerta.Media,
                Acciones = new[]
                {
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.EscalarPrioridad,
                        DiaEjecucion = finTramoMedia,
                        Descripcion = "Escalar a prioridad Media"
                    },
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.EnviarNotificacion,
                        DiaEjecucion = finTramoMedia,
                        Canal = CanalNotificacion.WhatsApp,
                        Descripcion = "Enviar recordatorio por WhatsApp"
                    }
                }
            });

            // Tramo 5: Prioridad Alta
            var finTramoCritica = config.DiasParaPrioridadCritica ?? 60;

            tramos.Add(new TramoCobranza
            {
                Nombre = "Mora Alta",
                DiasDesde = finTramoAlta,
                DiasHasta = finTramoCritica - 1,
                Prioridad = PrioridadAlerta.Alta,
                Acciones = new[]
                {
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.EscalarPrioridad,
                        DiaEjecucion = finTramoAlta,
                        Descripcion = "Escalar a prioridad Alta"
                    },
                    new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.AsignarGestor,
                        DiaEjecucion = finTramoAlta,
                        Descripcion = "Requerir asignación de gestor"
                    }
                }
            });

            // Tramo 6: Prioridad Crítica
            var accionesCriticas = new List<AccionAutomatica>
            {
                new AccionAutomatica
                {
                    Tipo = TipoAccionAutomatica.EscalarPrioridad,
                    DiaEjecucion = finTramoCritica,
                    Descripcion = "Escalar a prioridad Crítica"
                }
            };

            // Agregar bloqueo si está configurado
            if (config.BloqueoAutomaticoActivo && config.DiasParaBloquear.HasValue)
            {
                accionesCriticas.Add(new AccionAutomatica
                {
                    Tipo = TipoAccionAutomatica.BloquearCliente,
                    DiaEjecucion = config.DiasParaBloquear.Value,
                    Descripcion = $"Bloquear cliente (día {config.DiasParaBloquear.Value})"
                });
            }

            tramos.Add(new TramoCobranza
            {
                Nombre = "Mora Crítica",
                DiasDesde = finTramoCritica,
                DiasHasta = null, // Sin límite superior
                Prioridad = PrioridadAlerta.Critica,
                Acciones = accionesCriticas
            });

            return tramos.OrderBy(t => t.DiasDesde).ToList();
        }

        #endregion

        #region Determinación de Tramo

        /// <inheritdoc />
        public TramoCobranza? ObtenerTramo(int diasAtraso, IReadOnlyList<TramoCobranza> tramos)
        {
            if (tramos == null || tramos.Count == 0)
                return null;

            // Buscar el tramo que aplica (el último que coincida por si hay overlap)
            return tramos
                .Where(t => t.Aplica(diasAtraso))
                .OrderByDescending(t => t.DiasDesde)
                .FirstOrDefault();
        }

        #endregion

        #region Determinación de Acciones

        /// <inheritdoc />
        public IReadOnlyList<AccionAutomatica> DeterminarAcciones(
            AlertaCobranza alerta,
            ConfiguracionMora config,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(alerta);
            ArgumentNullException.ThrowIfNull(config);

            if (!config.ProcesoAutomaticoActivo)
                return Array.Empty<AccionAutomatica>();

            var fecha = fechaCalculo ?? DateTime.Today;
            var tramos = GenerarTramos(config);
            var tramoActual = ObtenerTramo(alerta.DiasAtraso, tramos);

            if (tramoActual == null)
                return Array.Empty<AccionAutomatica>();

            var acciones = new List<AccionAutomatica>();

            foreach (var accion in tramoActual.Acciones)
            {
                // Si tiene día específico, verificar si corresponde ejecutar
                if (accion.DiaEjecucion.HasValue)
                {
                    if (alerta.DiasAtraso == accion.DiaEjecucion.Value)
                    {
                        acciones.Add(accion);
                    }
                }
                else
                {
                    // Acciones sin día específico se ejecutan al entrar al tramo
                    if (alerta.DiasAtraso == tramoActual.DiasDesde)
                    {
                        acciones.Add(accion);
                    }
                }
            }

            // Agregar acción de promesa incumplida si corresponde
            if (alerta.EstadoGestion == EstadoGestionCobranza.PromesaPago &&
                alerta.FechaPromesaPago.HasValue)
            {
                var diasTolerancia = config.DiasParaCumplirPromesa ?? 0;
                var fechaLimite = alerta.FechaPromesaPago.Value.AddDays(diasTolerancia);

                if (fecha > fechaLimite)
                {
                    acciones.Add(new AccionAutomatica
                    {
                        Tipo = TipoAccionAutomatica.MarcarPromesaIncumplida,
                        Descripcion = "Marcar promesa de pago como incumplida"
                    });
                }
            }

            return acciones;
        }

        #endregion

        #region Procesamiento de Alertas

        /// <inheritdoc />
        public async Task<ResultadoProcesamientoTramos> ProcesarAlertasAsync(
            IEnumerable<AlertaCobranza> alertasActivas,
            ConfiguracionMora config,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(alertasActivas);
            ArgumentNullException.ThrowIfNull(config);

            var fecha = fechaCalculo ?? DateTime.Today;
            var alertas = alertasActivas.ToList();

            if (alertas.Count == 0)
            {
                return ResultadoProcesamientoTramos.Vacio(fecha);
            }

            if (!config.ProcesoAutomaticoActivo)
            {
                _logger.LogInformation("Proceso automático desactivado en configuración");
                return ResultadoProcesamientoTramos.Vacio(fecha);
            }

            var detalles = new List<AccionEjecutada>();
            var tramos = GenerarTramos(config);

            int escaladas = 0;
            int notificaciones = 0;
            int promesasVencidas = 0;
            int bloqueados = 0;

            foreach (var alerta in alertas)
            {
                try
                {
                    var acciones = DeterminarAcciones(alerta, config, fecha);

                    foreach (var accion in acciones)
                    {
                        var resultado = await EjecutarAccionAsync(alerta, accion, config);
                        detalles.Add(resultado);

                        if (resultado.Exitoso)
                        {
                            switch (accion.Tipo)
                            {
                                case TipoAccionAutomatica.EscalarPrioridad:
                                    escaladas++;
                                    break;
                                case TipoAccionAutomatica.EnviarNotificacion:
                                    notificaciones++;
                                    break;
                                case TipoAccionAutomatica.MarcarPromesaIncumplida:
                                    promesasVencidas++;
                                    break;
                                case TipoAccionAutomatica.BloquearCliente:
                                    bloqueados++;
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error procesando alerta {AlertaId}", alerta.Id);
                    detalles.Add(new AccionEjecutada
                    {
                        AlertaId = alerta.Id,
                        ClienteId = alerta.ClienteId,
                        Tipo = TipoAccionAutomatica.RegistrarNota,
                        Descripcion = "Error en procesamiento",
                        Exitoso = false,
                        Error = ex.Message
                    });
                }
            }

            return new ResultadoProcesamientoTramos
            {
                FechaProcesamiento = fecha,
                AlertasProcesadas = alertas.Count,
                AccionesEjecutadas = detalles.Count(d => d.Exitoso),
                AlertasEscaladas = escaladas,
                NotificacionesProgramadas = notificaciones,
                PromesasVencidas = promesasVencidas,
                ClientesBloqueados = bloqueados,
                Detalle = detalles,
                Exitoso = true
            };
        }

        /// <summary>
        /// Ejecuta una acción automática sobre una alerta.
        /// Modifica la alerta in-memory (el caller debe persistir).
        /// </summary>
        private Task<AccionEjecutada> EjecutarAccionAsync(
            AlertaCobranza alerta,
            AccionAutomatica accion,
            ConfiguracionMora config)
        {
            var resultado = new AccionEjecutada
            {
                AlertaId = alerta.Id,
                ClienteId = alerta.ClienteId,
                Tipo = accion.Tipo,
                Descripcion = accion.Descripcion ?? accion.Tipo.ToString(),
                Exitoso = true
            };

            try
            {
                switch (accion.Tipo)
                {
                    case TipoAccionAutomatica.EscalarPrioridad:
                        var nuevaPrioridad = CalcularPrioridad(alerta, config);
                        if (nuevaPrioridad != alerta.Prioridad)
                        {
                            alerta.Prioridad = nuevaPrioridad;
                            resultado.Descripcion = $"Prioridad escalada a {nuevaPrioridad}";
                        }
                        break;

                    case TipoAccionAutomatica.MarcarPromesaIncumplida:
                        alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
                        alerta.Observaciones = (alerta.Observaciones ?? "") +
                            $"\n[{DateTime.UtcNow:yyyy-MM-dd}] Promesa de pago incumplida (automático)";
                        resultado.Descripcion = "Promesa marcada como incumplida";
                        break;

                    case TipoAccionAutomatica.CambiarEstadoCuota:
                        // Marca que debe cambiarse - el caller con acceso a DB lo ejecuta
                        resultado.Descripcion = "Cuota marcada para cambio de estado";
                        break;

                    case TipoAccionAutomatica.EnviarNotificacion:
                        // Marca para envío - el servicio de notificaciones lo ejecuta
                        resultado.Descripcion = $"Notificación programada por {accion.Canal}";
                        break;

                    case TipoAccionAutomatica.BloquearCliente:
                        // Marca para bloqueo - el caller con acceso a Cliente lo ejecuta
                        resultado.Descripcion = "Cliente marcado para bloqueo";
                        break;

                    case TipoAccionAutomatica.AsignarGestor:
                        if (string.IsNullOrEmpty(alerta.GestorAsignadoId))
                        {
                            resultado.Descripcion = "Requiere asignación manual de gestor";
                        }
                        break;

                    case TipoAccionAutomatica.GenerarAlerta:
                        resultado.Descripcion = "Alerta ya generada";
                        break;

                    case TipoAccionAutomatica.RegistrarNota:
                        alerta.Observaciones = (alerta.Observaciones ?? "") +
                            $"\n[{DateTime.UtcNow:yyyy-MM-dd}] {accion.Descripcion}";
                        break;
                }
            }
            catch (Exception ex)
            {
                resultado.Exitoso = false;
                resultado.Error = ex.Message;
                _logger.LogError(ex, "Error ejecutando acción {Tipo} en alerta {AlertaId}",
                    accion.Tipo, alerta.Id);
            }

            return Task.FromResult(resultado);
        }

        #endregion

        #region Cálculo de Prioridad

        /// <inheritdoc />
        public PrioridadAlerta CalcularPrioridad(AlertaCobranza alerta, ConfiguracionMora config)
        {
            ArgumentNullException.ThrowIfNull(alerta);
            ArgumentNullException.ThrowIfNull(config);

            // Evaluar por días de atraso
            if (config.DiasParaPrioridadCritica.HasValue &&
                alerta.DiasAtraso >= config.DiasParaPrioridadCritica.Value)
            {
                return PrioridadAlerta.Critica;
            }

            if (config.DiasParaPrioridadAlta.HasValue &&
                alerta.DiasAtraso >= config.DiasParaPrioridadAlta.Value)
            {
                return PrioridadAlerta.Alta;
            }

            if (config.DiasParaPrioridadMedia.HasValue &&
                alerta.DiasAtraso >= config.DiasParaPrioridadMedia.Value)
            {
                return PrioridadAlerta.Media;
            }

            // Evaluar por monto
            if (config.MontoParaPrioridadCritica.HasValue &&
                alerta.MontoTotal >= config.MontoParaPrioridadCritica.Value)
            {
                return PrioridadAlerta.Critica;
            }

            if (config.MontoParaPrioridadAlta.HasValue &&
                alerta.MontoTotal >= config.MontoParaPrioridadAlta.Value)
            {
                return PrioridadAlerta.Alta;
            }

            if (config.MontoParaPrioridadMedia.HasValue &&
                alerta.MontoTotal >= config.MontoParaPrioridadMedia.Value)
            {
                return PrioridadAlerta.Media;
            }

            return PrioridadAlerta.Baja;
        }

        #endregion

        #region Bloqueo de Clientes

        /// <inheritdoc />
        public bool DebeBloquearCliente(
            int diasAtraso,
            int cuotasVencidas,
            decimal montoMora,
            ConfiguracionMora config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!config.BloqueoAutomaticoActivo)
                return false;

            // Verificar por días
            if (config.DiasParaBloquear.HasValue &&
                diasAtraso >= config.DiasParaBloquear.Value)
            {
                return true;
            }

            // Verificar por cuotas vencidas
            if (config.CuotasVencidasParaBloquear.HasValue &&
                cuotasVencidas >= config.CuotasVencidasParaBloquear.Value)
            {
                return true;
            }

            // Verificar por monto de mora
            if (config.MontoMoraParaBloquear.HasValue &&
                montoMora >= config.MontoMoraParaBloquear.Value)
            {
                return true;
            }

            return false;
        }

        #endregion
    }
}
