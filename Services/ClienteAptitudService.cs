using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación del servicio de aptitud crediticia.
    /// Evalúa: documentación completa/no vencida + cupo manual + mora.
    /// </summary>
    public class ClienteAptitudService : IClienteAptitudService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ClienteAptitudService> _logger;
        private readonly ICreditoDisponibleService _creditoDisponibleService;

        public ClienteAptitudService(
            AppDbContext context,
            ILogger<ClienteAptitudService> logger,
            ICreditoDisponibleService creditoDisponibleService)
        {
            _context = context;
            _logger = logger;
            _creditoDisponibleService = creditoDisponibleService;
        }

        #region Evaluación de Aptitud

        public async Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true)
        {
            var resultado = await EvaluarAptitudInternoAsync(clienteId);

            if (guardarResultado)
            {
                await GuardarResultadoEvaluacionAsync(clienteId, resultado);
            }

            return resultado;
        }

        public async Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId)
        {
            return await EvaluarAptitudInternoAsync(clienteId);
        }

        private async Task<AptitudCrediticiaViewModel> EvaluarAptitudInternoAsync(int clienteId)
        {
            var resultado = new AptitudCrediticiaViewModel
            {
                FechaEvaluacion = DateTime.UtcNow
            };

            // Obtener configuración
            var config = await GetConfiguracionAsync();
            var (configCompleta, mensajeConfig) = await VerificarConfiguracionAsync();
            
            if (!configCompleta)
            {
                resultado.ConfiguracionCompleta = false;
                resultado.AdvertenciaConfiguracion = mensajeConfig;
                resultado.Estado = EstadoCrediticioCliente.NoEvaluado;
                return resultado;
            }

            // Ejecutar las tres evaluaciones
            resultado.Documentacion = await EvaluarDocumentacionInternaAsync(clienteId, config);
            resultado.Cupo = await EvaluarCupoInternoAsync(clienteId, config);
            resultado.Mora = await EvaluarMoraInternaAsync(clienteId, config);

            // Consolidar resultado
            DeterminarEstadoFinal(resultado, config);

            return resultado;
        }

        private void DeterminarEstadoFinal(AptitudCrediticiaViewModel resultado, ConfiguracionCredito config)
        {
            var detalles = new List<AptitudDetalleItem>();
            var esNoApto = false;
            var requiereAutorizacion = false;
            var motivos = new List<string>();

            // Evaluar documentación
            if (config.ValidarDocumentacion && resultado.Documentacion.Evaluada)
            {
                if (!resultado.Documentacion.Completa)
                {
                    esNoApto = true;
                    var faltantes = string.Join(", ", resultado.Documentacion.DocumentosFaltantes);
                    motivos.Add($"Documentación incompleta: {faltantes}");
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Documentación",
                        Descripcion = $"Faltan documentos: {faltantes}",
                        EsBloqueo = true,
                        Icono = "bi-file-earmark-x",
                        Color = "danger"
                    });
                }

                if (resultado.Documentacion.TieneVencidos && config.ValidarVencimientoDocumentos)
                {
                    esNoApto = true;
                    var vencidos = string.Join(", ", resultado.Documentacion.DocumentosVencidos);
                    motivos.Add($"Documentos vencidos: {vencidos}");
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Documentación",
                        Descripcion = $"Documentos vencidos: {vencidos}",
                        EsBloqueo = true,
                        Icono = "bi-calendar-x",
                        Color = "danger"
                    });
                }
            }

            // Evaluar cupo
            if (config.ValidarLimiteCredito && resultado.Cupo.Evaluado)
            {
                if (!resultado.Cupo.TieneCupoAsignado)
                {
                    esNoApto = true;
                    var motivoCupo = string.IsNullOrWhiteSpace(resultado.Cupo.Mensaje)
                        ? "No hay límite de crédito configurado para el puntaje del cliente"
                        : resultado.Cupo.Mensaje;
                    motivos.Add(motivoCupo);
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Cupo",
                        Descripcion = motivoCupo,
                        EsBloqueo = true,
                        Icono = "bi-credit-card-2-front",
                        Color = "danger"
                    });
                }
                else if (!resultado.Cupo.CupoSuficiente)
                {
                    esNoApto = true;
                    motivos.Add($"Cupo insuficiente (disponible: {resultado.Cupo.CupoDisponible:C0})");
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Cupo",
                        Descripcion = $"Cupo agotado. Disponible: {resultado.Cupo.CupoDisponible:C0}",
                        EsBloqueo = true,
                        Icono = "bi-wallet2",
                        Color = "danger"
                    });
                }
            }

            // Evaluar mora
            if (config.ValidarMora && resultado.Mora.Evaluada && resultado.Mora.TieneMora)
            {
                if (resultado.Mora.EsBloqueante)
                {
                    esNoApto = true;
                    motivos.Add($"Mora crítica: {resultado.Mora.DiasMaximoMora} días, {resultado.Mora.MontoTotalMora:C0}");
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Mora",
                        Descripcion = $"Mora crítica: {resultado.Mora.DiasMaximoMora} días, monto {resultado.Mora.MontoTotalMora:C0}",
                        EsBloqueo = true,
                        Icono = "bi-exclamation-octagon",
                        Color = "danger"
                    });
                }
                else if (resultado.Mora.RequiereAutorizacion)
                {
                    requiereAutorizacion = true;
                    motivos.Add($"Tiene mora: {resultado.Mora.DiasMaximoMora} días");
                    detalles.Add(new AptitudDetalleItem
                    {
                        Categoria = "Mora",
                        Descripcion = $"Cliente en mora ({resultado.Mora.DiasMaximoMora} días) - Requiere autorización de supervisor",
                        EsBloqueo = false,
                        Icono = "bi-clock-history",
                        Color = "warning"
                    });
                }
            }

            resultado.Detalles = detalles;

            // Determinar estado final
            if (esNoApto)
            {
                resultado.Estado = EstadoCrediticioCliente.NoApto;
                resultado.Motivo = string.Join(". ", motivos);
            }
            else if (requiereAutorizacion)
            {
                resultado.Estado = EstadoCrediticioCliente.RequiereAutorizacion;
                resultado.Motivo = string.Join(". ", motivos);
            }
            else
            {
                resultado.Estado = EstadoCrediticioCliente.Apto;
                resultado.Motivo = null;
            }
        }

        private async Task GuardarResultadoEvaluacionAsync(int clienteId, AptitudCrediticiaViewModel resultado)
        {
            try
            {
                var cliente = await _context.Clientes.FindAsync(clienteId);
                if (cliente == null) return;

                var estadoAnterior = cliente.EstadoCrediticio;

                cliente.EstadoCrediticio = resultado.Estado;
                cliente.MotivoNoApto = resultado.Motivo;
                cliente.FechaUltimaEvaluacion = resultado.FechaEvaluacion;

                await _context.SaveChangesAsync();

                // Log de auditoría si cambió el estado
                if (estadoAnterior != resultado.Estado)
                {
                    _logger.LogInformation(
                        "Cambio de estado crediticio del cliente {ClienteId}: {Anterior} -> {Nuevo}. Motivo: {Motivo}",
                        clienteId, estadoAnterior, resultado.Estado, resultado.Motivo ?? "N/A");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar resultado de evaluación para cliente {ClienteId}", clienteId);
            }
        }

        public async Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null || cliente.FechaUltimaEvaluacion == null)
                return null;

            return new AptitudCrediticiaViewModel
            {
                Estado = cliente.EstadoCrediticio,
                Motivo = cliente.MotivoNoApto,
                FechaEvaluacion = cliente.FechaUltimaEvaluacion.Value
            };
        }

        public async Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto)
        {
            var evaluacion = await EvaluarAptitudSinGuardarAsync(clienteId);

            if (evaluacion.Estado == EstadoCrediticioCliente.NoApto)
                return (false, evaluacion.Motivo);

            // Verificar si tiene cupo suficiente para el monto específico
            if (evaluacion.Cupo.CupoDisponible < monto)
                return (false, $"Cupo insuficiente. Disponible: {evaluacion.Cupo.CupoDisponible:C0}, Solicitado: {monto:C0}");

            return (evaluacion.Estado == EstadoCrediticioCliente.Apto, null);
        }

        #endregion

        #region Evaluaciones Parciales

        public async Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId)
        {
            var config = await GetConfiguracionAsync();
            return await EvaluarDocumentacionInternaAsync(clienteId, config);
        }

        private async Task<AptitudDocumentacionDetalle> EvaluarDocumentacionInternaAsync(int clienteId, ConfiguracionCredito config)
        {
            var resultado = new AptitudDocumentacionDetalle { Evaluada = config.ValidarDocumentacion };

            if (!config.ValidarDocumentacion)
            {
                resultado.Completa = true;
                resultado.Mensaje = "Validación de documentación deshabilitada";
                return resultado;
            }

            // Obtener tipos requeridos
            var tiposRequeridos = ObtenerTiposDocumentoRequeridos(config);

            // Obtener documentos del cliente
            var documentos = await _context.Set<DocumentoCliente>()
                .Where(d => d.ClienteId == clienteId && !d.IsDeleted)
                .ToListAsync();

            var faltantes = new List<string>();
            var vencidos = new List<string>();
            var hoy = DateTime.UtcNow.Date;
            var diasGracia = config.DiasGraciaVencimientoDocumento ?? 0;

            foreach (var tipo in tiposRequeridos)
            {
                var doc = documentos
                    .Where(d => d.TipoDocumento == tipo && d.Estado == EstadoDocumento.Verificado)
                    .OrderByDescending(d => d.FechaVerificacion)
                    .FirstOrDefault();

                if (doc == null)
                {
                    faltantes.Add(tipo.ToString());
                }
                else if (config.ValidarVencimientoDocumentos && doc.FechaVencimiento.HasValue)
                {
                    var fechaLimite = doc.FechaVencimiento.Value.AddDays(diasGracia);
                    if (hoy > fechaLimite)
                    {
                        vencidos.Add(tipo.ToString());
                    }
                }
            }

            resultado.DocumentosFaltantes = faltantes;
            resultado.DocumentosVencidos = vencidos;
            resultado.TieneVencidos = vencidos.Count > 0;
            resultado.Completa = faltantes.Count == 0 && vencidos.Count == 0;

            if (resultado.Completa)
            {
                resultado.Mensaje = "Documentación completa y vigente";
            }
            else
            {
                var mensajes = new List<string>();
                if (faltantes.Count > 0) mensajes.Add($"Faltan: {string.Join(", ", faltantes)}");
                if (vencidos.Count > 0) mensajes.Add($"Vencidos: {string.Join(", ", vencidos)}");
                resultado.Mensaje = string.Join(". ", mensajes);
            }

            return resultado;
        }

        private List<TipoDocumentoCliente> ObtenerTiposDocumentoRequeridos(ConfiguracionCredito config)
        {
            if (string.IsNullOrWhiteSpace(config.TiposDocumentoRequeridos))
                return DropdownConstants.DocumentosClienteRequeridos.ToList();

            try
            {
                return JsonSerializer.Deserialize<List<TipoDocumentoCliente>>(config.TiposDocumentoRequeridos)
                    ?? DropdownConstants.DocumentosClienteRequeridos.ToList();
            }
            catch
            {
                return DropdownConstants.DocumentosClienteRequeridos.ToList();
            }
        }

        public async Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId)
        {
            var config = await GetConfiguracionAsync();
            return await EvaluarCupoInternoAsync(clienteId, config);
        }

        private async Task<AptitudCupoDetalle> EvaluarCupoInternoAsync(int clienteId, ConfiguracionCredito config)
        {
            var resultado = new AptitudCupoDetalle { Evaluado = config.ValidarLimiteCredito };

            if (!config.ValidarLimiteCredito)
            {
                resultado.CupoSuficiente = true;
                resultado.Mensaje = "Validación de cupo deshabilitada";
                return resultado;
            }

            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
            {
                resultado.Mensaje = "Cliente no encontrado";
                return resultado;
            }

            try
            {
                var disponible = await _creditoDisponibleService.CalcularDisponibleAsync(clienteId);
                resultado.LimiteCredito = disponible.Limite;
                resultado.CreditoUtilizado = disponible.SaldoVigente;
                resultado.CupoDisponible = disponible.Disponible;
                resultado.TieneCupoAsignado = disponible.Limite > 0;
                resultado.PorcentajeUtilizado = disponible.Limite > 0
                    ? (disponible.SaldoVigente / disponible.Limite) * 100
                    : 0;
            }
            catch (CreditoDisponibleException ex)
            {
                resultado.LimiteCredito = null;
                resultado.CreditoUtilizado = 0;
                resultado.CupoDisponible = 0;
                resultado.PorcentajeUtilizado = 0;
                resultado.TieneCupoAsignado = false;
                resultado.CupoSuficiente = false;
                resultado.Mensaje = ex.Message;
                return resultado;
            }

            // Verificar porcentaje mínimo si está configurado
            if (config.PorcentajeCupoMinimoRequerido.HasValue)
            {
                var porcentajeDisponible = 100 - resultado.PorcentajeUtilizado;
                resultado.CupoSuficiente = porcentajeDisponible >= config.PorcentajeCupoMinimoRequerido.Value;
            }
            else
            {
                // Si no hay porcentaje mínimo, basta con que tenga algo disponible
                resultado.CupoSuficiente = resultado.CupoDisponible > 0 ||
                    (config.LimiteCreditoMinimo.HasValue && resultado.CupoDisponible >= config.LimiteCreditoMinimo.Value);
            }

            resultado.Mensaje = resultado.CupoSuficiente
                ? $"Cupo disponible: {resultado.CupoDisponible:C0}"
                : $"Cupo insuficiente. Disponible: {resultado.CupoDisponible:C0}";

            return resultado;
        }

        public async Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId)
        {
            var config = await GetConfiguracionAsync();
            return await EvaluarMoraInternaAsync(clienteId, config);
        }

        private async Task<AptitudMoraDetalle> EvaluarMoraInternaAsync(int clienteId, ConfiguracionCredito config)
        {
            var resultado = new AptitudMoraDetalle { Evaluada = config.ValidarMora };

            if (!config.ValidarMora)
            {
                resultado.TieneMora = false;
                resultado.Mensaje = "Validación de mora deshabilitada";
                return resultado;
            }

            var hoy = DateTime.UtcNow.Date;

            // Buscar cuotas vencidas del cliente
            var cuotasVencidas = await _context.Cuotas
                .Include(c => c.Credito)
                .Where(c => c.Credito!.ClienteId == clienteId &&
                           !c.Credito.IsDeleted &&
                           c.Estado != EstadoCuota.Pagada &&
                           c.Estado != EstadoCuota.Cancelada &&
                           c.FechaVencimiento < hoy)
                .ToListAsync();

            if (!cuotasVencidas.Any())
            {
                resultado.TieneMora = false;
                resultado.Mensaje = "Sin mora";
                return resultado;
            }

            resultado.TieneMora = true;
            resultado.CuotasVencidas = cuotasVencidas.Count;
            resultado.DiasMaximoMora = cuotasVencidas.Max(c => (hoy - c.FechaVencimiento).Days);
            resultado.MontoTotalMora = cuotasVencidas.Sum(c => c.MontoTotal + c.MontoPunitorio - c.MontoPagado);

            // Determinar si es bloqueante o requiere autorización
            var esBloqueante = false;
            var requiereAuth = false;

            // Verificar días para NoApto
            if (config.DiasParaNoApto.HasValue && resultado.DiasMaximoMora >= config.DiasParaNoApto.Value)
            {
                esBloqueante = true;
            }

            // Verificar monto para NoApto
            if (config.MontoMoraParaNoApto.HasValue && resultado.MontoTotalMora >= config.MontoMoraParaNoApto.Value)
            {
                esBloqueante = true;
            }

            // Verificar cuotas para NoApto
            if (config.CuotasVencidasParaNoApto.HasValue && resultado.CuotasVencidas >= config.CuotasVencidasParaNoApto.Value)
            {
                esBloqueante = true;
            }

            // Si no es bloqueante, verificar si requiere autorización
            if (!esBloqueante)
            {
                if (config.DiasParaRequerirAutorizacion.HasValue && resultado.DiasMaximoMora >= config.DiasParaRequerirAutorizacion.Value)
                {
                    requiereAuth = true;
                }

                if (config.MontoMoraParaRequerirAutorizacion.HasValue && resultado.MontoTotalMora >= config.MontoMoraParaRequerirAutorizacion.Value)
                {
                    requiereAuth = true;
                }
            }

            resultado.EsBloqueante = esBloqueante;
            resultado.RequiereAutorizacion = requiereAuth || esBloqueante;

            resultado.Mensaje = esBloqueante
                ? $"Mora crítica: {resultado.DiasMaximoMora} días, {resultado.CuotasVencidas} cuotas, {resultado.MontoTotalMora:C0}"
                : $"En mora: {resultado.DiasMaximoMora} días - Requiere autorización";

            return resultado;
        }

        #endregion

        #region Configuración

        public async Task<ConfiguracionCredito> GetConfiguracionAsync()
        {
            var config = await _context.Set<ConfiguracionCredito>()
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            if (config == null)
            {
                // Crear configuración por defecto
                config = new ConfiguracionCredito();
                _context.Set<ConfiguracionCredito>().Add(config);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Configuración de crédito creada con valores por defecto");
            }

            return config;
        }

        public async Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel)
        {
            var config = await GetConfiguracionAsync();

            config.ValidarDocumentacion = viewModel.ValidarDocumentacion;
            config.TiposDocumentoRequeridos = viewModel.TiposDocumentoRequeridos?.Count > 0
                ? JsonSerializer.Serialize(viewModel.TiposDocumentoRequeridos)
                : null;
            config.ValidarVencimientoDocumentos = viewModel.ValidarVencimientoDocumentos;
            config.DiasGraciaVencimientoDocumento = viewModel.DiasGraciaVencimientoDocumento;

            config.ValidarLimiteCredito = viewModel.ValidarLimiteCredito;
            config.LimiteCreditoMinimo = viewModel.LimiteCreditoMinimo;
            config.LimiteCreditoDefault = viewModel.LimiteCreditoDefault;
            config.PorcentajeCupoMinimoRequerido = viewModel.PorcentajeCupoMinimoRequerido;

            config.ValidarMora = viewModel.ValidarMora;
            config.DiasParaRequerirAutorizacion = viewModel.DiasParaRequerirAutorizacion;
            config.DiasParaNoApto = viewModel.DiasParaNoApto;
            config.MontoMoraParaRequerirAutorizacion = viewModel.MontoMoraParaRequerirAutorizacion;
            config.MontoMoraParaNoApto = viewModel.MontoMoraParaNoApto;
            config.CuotasVencidasParaNoApto = viewModel.CuotasVencidasParaNoApto;

            config.RecalculoAutomatico = viewModel.RecalculoAutomatico;
            config.DiasValidezEvaluacion = viewModel.DiasValidezEvaluacion;
            config.AuditoriaActiva = viewModel.AuditoriaActiva;

            config.FechaUltimaModificacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Configuración de crédito actualizada");

            return config;
        }

        public async Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync()
        {
            var config = await GetConfiguracionAsync();

            // La configuración está completa si al menos una validación está activa
            // o todas están explícitamente deshabilitadas (lo cual es válido)
            var alMenosUnaActiva = config.ValidarDocumentacion ||
                                    config.ValidarLimiteCredito ||
                                    config.ValidarMora;

            if (!alMenosUnaActiva)
            {
                return (true, "Todas las validaciones de aptitud crediticia están deshabilitadas. Los clientes serán marcados como 'Apto' automáticamente.");
            }

            return (true, null);
        }

        #endregion

        #region Gestión de Límite de Crédito

        public async Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null)
        {
            try
            {
                var cliente = await _context.Clientes.FindAsync(clienteId);
                if (cliente == null) return false;

                var limiteAnterior = cliente.LimiteCredito;
                cliente.LimiteCredito = limite;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Límite de crédito actualizado para cliente {ClienteId}: {Anterior} -> {Nuevo}. Motivo: {Motivo}",
                    clienteId, limiteAnterior, limite, motivo ?? "N/A");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al asignar límite de crédito al cliente {ClienteId}", clienteId);
                return false;
            }
        }

        public async Task<decimal> GetCupoDisponibleAsync(int clienteId)
        {
            try
            {
                var disponible = await _creditoDisponibleService.CalcularDisponibleAsync(clienteId);
                return disponible.Disponible;
            }
            catch (CreditoDisponibleException ex)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible calcular cupo disponible por puntaje para cliente {ClienteId}. Se retorna 0.",
                    clienteId);
                return 0;
            }
        }

        public async Task<decimal> GetCreditoUtilizadoAsync(int clienteId)
        {
            // Suma de saldo pendiente de todos los créditos activos
            return await _context.Creditos
                .Where(c => c.ClienteId == clienteId &&
                           !c.IsDeleted &&
                           (c.Estado == EstadoCredito.Activo ||
                            c.Estado == EstadoCredito.Aprobado ||
                            c.Estado == EstadoCredito.Solicitado ||
                            c.Estado == EstadoCredito.PendienteConfiguracion ||
                            c.Estado == EstadoCredito.Configurado ||
                            c.Estado == EstadoCredito.Generado))
                .SumAsync(c => c.SaldoPendiente);
        }

        #endregion
    }
}
