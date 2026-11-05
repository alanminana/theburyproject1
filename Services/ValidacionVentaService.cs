using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación del servicio de validación unificada para ventas con crédito personal.
    /// Integra el servicio de aptitud crediticia con las validaciones de venta.
    /// </summary>
    public class ValidacionVentaService : IValidacionVentaService
    {
        private readonly AppDbContext _context;
        private readonly IClienteAptitudService _aptitudService;
        private readonly ISituacionCrediticiaBcraService _bcraService;
        private readonly ILogger<ValidacionVentaService> _logger;

        public ValidacionVentaService(
            AppDbContext context,
            IClienteAptitudService aptitudService,
            ISituacionCrediticiaBcraService bcraService,
            ILogger<ValidacionVentaService> logger)
        {
            _context = context;
            _aptitudService = aptitudService;
            _bcraService = bcraService;
            _logger = logger;
        }

        #region Prevalidación (E1 - Solo lectura, no persiste)

        /// <inheritdoc />
        public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto)
            => PrevalidarAsync(clienteId, monto, ventaId: null);

        /// <inheritdoc />
        public async Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto, int? ventaId)
        {
            _logger.LogInformation(
                "Iniciando prevalidación para cliente {ClienteId}, monto {Monto}",
                clienteId, monto);

            try
            {
                // Usar evaluación unificada
                var saldoPropio = await ObtenerSaldoCreditoPropioAsync(clienteId, ventaId);
                var evaluacion = await EvaluarCreditoUnificadoAsync(clienteId, monto, null, saldoPropio);

                // Mapear a PrevalidacionResultViewModel
                var resultado = MapearAPrevalidacion(evaluacion);

                _logger.LogInformation(
                    "Prevalidación completada para cliente {ClienteId}: {Resultado}",
                    clienteId, resultado.Resultado);

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en prevalidación para cliente {ClienteId}", clienteId);

                return new PrevalidacionResultViewModel
                {
                    ClienteId = clienteId,
                    MontoSolicitado = monto,
                    Timestamp = DateTime.UtcNow,
                    Resultado = ResultadoPrevalidacion.NoViable,
                    Motivos = new List<MotivoPrevalidacion>
                    {
                        new()
                        {
                            Categoria = CategoriaMotivo.Configuracion,
                            Descripcion = "Error al evaluar aptitud crediticia",
                            EsBloqueante = true
                        }
                    }
                };
            }
        }

        #endregion

        #region Validación de Venta

        public async Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
            int clienteId,
            decimal montoVenta,
            int? creditoId = null)
        {
            // Usar evaluación unificada
            var evaluacion = await EvaluarCreditoUnificadoAsync(clienteId, montoVenta, creditoId);
            
            // Mapear a ValidacionVentaResult
            return MapearAValidacionVenta(evaluacion);
        }

        #endregion

        #region Evaluación Unificada (Core)

        /// <summary>
        /// Evaluación unificada de crédito que puede mapearse a cualquier ViewModel de resultado.
        /// </summary>
        private async Task<EvaluacionCrediticiaIntermedia> EvaluarCreditoUnificadoAsync(
            int clienteId,
            decimal monto,
            int? creditoId = null,
            decimal saldoPropioExcluido = 0m)
        {
            var evaluacion = new EvaluacionCrediticiaIntermedia
            {
                ClienteId = clienteId,
                MontoSolicitado = monto
            };

            // 1. Refrescar BCRA/Veraz (con caché) para que la aptitud evalúe con el
            // dato más reciente disponible en esta misma operación.
            await RefrescarBcraAntesDeEvaluarCreditoAsync(clienteId);

            // 2. Evaluar aptitud crediticia del cliente
            var aptitud = await _aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

            // 3. Poblar datos básicos
            PoblarDatosBasicos(evaluacion, aptitud);

            // 3b. El crédito propio de la venta que se está editando ya figura como "utilizado" (existe en
            // PendienteConfiguracion/Configurado antes de confirmar): se excluye para no descontarlo dos veces
            // (misma regla que VentaService al confirmar). Se recalcula con el clamp a 0 sobre el límite.
            if (saldoPropioExcluido > 0m && evaluacion.LimiteCredito.HasValue && evaluacion.LimiteCredito.Value > 0m)
            {
                evaluacion.CreditoUtilizado = Math.Max(0m, evaluacion.CreditoUtilizado - saldoPropioExcluido);
                evaluacion.CupoDisponible = Math.Max(0m, evaluacion.LimiteCredito.Value - evaluacion.CreditoUtilizado);
            }

            // 4. Evaluar según estado de aptitud
            EvaluarSegunEstadoAptitud(evaluacion, aptitud);

            // 5. Verificar cupo para el monto específico (si no es NoViable)
            if (!evaluacion.EsNoViable)
            {
                await VerificarCupoUnificado(evaluacion, clienteId, monto, creditoId, saldoPropioExcluido);
            }

            return evaluacion;
        }

        /// <summary>
        /// Saldo pendiente del crédito propio de la venta en edición (0 si no hay venta, no es del cliente o el
        /// crédito ya está finalizado/rechazado/cancelado). Solo lectura.
        /// </summary>
        private async Task<decimal> ObtenerSaldoCreditoPropioAsync(int clienteId, int? ventaId)
        {
            if (ventaId is null or <= 0)
                return 0m;

            var credito = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.Id == ventaId.Value && !v.IsDeleted && v.ClienteId == clienteId && v.CreditoId != null)
                .Select(v => v.Credito)
                .FirstOrDefaultAsync();

            if (credito == null || credito.IsDeleted || credito.SaldoPendiente <= 0m
                || credito.Estado is EstadoCredito.Finalizado or EstadoCredito.Rechazado or EstadoCredito.Cancelado)
                return 0m;

            return credito.SaldoPendiente;
        }

        /// <summary>
        /// Refresca la situación BCRA/Veraz del cliente (con caché) antes de evaluar
        /// aptitud, para que la clasificación (FASE 4B) use el dato más reciente
        /// disponible en la misma operación de venta.
        /// </summary>
        private async Task RefrescarBcraAntesDeEvaluarCreditoAsync(int clienteId)
        {
            // ConsultarYActualizarAsync ya maneja internamente errores de red/timeout/
            // parseo (los persiste como consulta fallida y no relanza), por eso no se
            // envuelve en try/catch acá.
            await _bcraService.ConsultarYActualizarAsync(clienteId);
        }

        private void PoblarDatosBasicos(
            EvaluacionCrediticiaIntermedia evaluacion,
            AptitudCrediticiaViewModel aptitud)
        {
            // Estado de aptitud
            evaluacion.EstadoAptitud = aptitud.Estado;
            
            // Documentación
            if (aptitud.Documentacion != null)
            {
                evaluacion.DocumentacionCompleta = aptitud.Documentacion.Completa;
                evaluacion.DocumentosFaltantes = aptitud.Documentacion.DocumentosFaltantes ?? new List<string>();
                evaluacion.DocumentosVencidos = aptitud.Documentacion.DocumentosVencidos ?? new List<string>();
            }

            // Cupo
            if (aptitud.Cupo != null)
            {
                evaluacion.LimiteCredito = aptitud.Cupo.LimiteCredito;
                evaluacion.CupoDisponible = aptitud.Cupo.CupoDisponible;
                evaluacion.CreditoUtilizado = aptitud.Cupo.CreditoUtilizado;
            }

            // Mora
            if (aptitud.Mora != null)
            {
                evaluacion.TieneMora = aptitud.Mora.TieneMora;
                evaluacion.DiasMora = aptitud.Mora.DiasMaximoMora;
                evaluacion.MontoMora = aptitud.Mora.MontoTotalMora;
            }
        }

        private void EvaluarSegunEstadoAptitud(
            EvaluacionCrediticiaIntermedia evaluacion,
            AptitudCrediticiaViewModel aptitud)
        {
            // Verificar configuración del sistema
            if (!aptitud.ConfiguracionCompleta)
            {
                evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.Configuracion,
                    Titulo = "Configuración del sistema",
                    Descripcion = aptitud.AdvertenciaConfiguracion ?? "Sistema de crédito no configurado",
                    AccionSugerida = "Contactar al administrador",
                    EsBloqueante = true,
                    TipoRequisito = TipoRequisitoPendiente.SinEvaluacionCrediticia
                });
                return;
            }

            switch (aptitud.Estado)
            {
                case EstadoCrediticioCliente.NoEvaluado:
                    EvaluarClienteNoEvaluadoUnificado(evaluacion, aptitud);
                    break;

                case EstadoCrediticioCliente.NoApto:
                    EvaluarClienteNoAptoUnificado(evaluacion, aptitud);
                    break;

                case EstadoCrediticioCliente.RequiereAutorizacion:
                    EvaluarClienteRequiereAutorizacionUnificado(evaluacion, aptitud);
                    break;

                case EstadoCrediticioCliente.Apto:
                    evaluacion.Resultado = ResultadoPrevalidacion.Aprobable;
                    break;
            }
        }

        private void EvaluarClienteNoEvaluadoUnificado(
            EvaluacionCrediticiaIntermedia evaluacion,
            AptitudCrediticiaViewModel aptitud)
        {
            evaluacion.Resultado = ResultadoPrevalidacion.NoViable;

            // Analizar qué falta para estar evaluado
            if (aptitud.Documentacion != null && !aptitud.Documentacion.Completa)
            {
                var faltantes = string.Join(", ", aptitud.Documentacion.DocumentosFaltantes);
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.Documentacion,
                    Titulo = "Documentación incompleta",
                    Descripcion = $"Documentación faltante: {faltantes}",
                    AccionSugerida = "Cargar documentación obligatoria",
                    UrlAccion = $"/DocumentoCliente/Index?clienteId={evaluacion.ClienteId}",
                    EsBloqueante = true,
                    TipoRequisito = TipoRequisitoPendiente.DocumentacionFaltante
                });
            }

            if (aptitud.Cupo != null && !aptitud.Cupo.TieneCupoAsignado)
            {
                var descripcionCupo = string.IsNullOrWhiteSpace(aptitud.Cupo.Mensaje)
                    ? "No hay límite de crédito configurado para el puntaje del cliente."
                    : aptitud.Cupo.Mensaje;

                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.Cupo,
                    Titulo = "Sin límite de crédito por puntaje",
                    Descripcion = descripcionCupo,
                    AccionSugerida = "Configurar límite por puntaje desde Clientes > Límites por Puntaje",
                    UrlAccion = $"/Cliente/Details/{evaluacion.ClienteId}",
                    EsBloqueante = true,
                    TipoRequisito = TipoRequisitoPendiente.SinLimiteCredito
                });
            }

            // Si no hay problemas específicos, agregar uno genérico
            if (!evaluacion.Problemas.Any())
            {
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.EstadoCliente,
                    Titulo = "Cliente sin evaluar",
                    Descripcion = "El cliente no ha sido evaluado crediticiamente.",
                    AccionSugerida = "Completar evaluación del cliente",
                    UrlAccion = $"/Cliente/Details/{evaluacion.ClienteId}",
                    EsBloqueante = true,
                    TipoRequisito = TipoRequisitoPendiente.SinEvaluacionCrediticia
                });
            }
        }

        private void EvaluarClienteNoAptoUnificado(
            EvaluacionCrediticiaIntermedia evaluacion,
            AptitudCrediticiaViewModel aptitud)
        {
            evaluacion.Resultado = ResultadoPrevalidacion.NoViable;

            // Analizar los detalles para determinar qué bloquea
            foreach (var detalle in aptitud.Detalles.Where(d => d.EsBloqueo))
            {
                var problema = new ProblemaCredito
                {
                    Descripcion = detalle.Descripcion,
                    EsBloqueante = true
                };

                switch (detalle.Categoria)
                {
                    case "Documentación":
                        problema.Categoria = CategoriaMotivo.Documentacion;
                        problema.Titulo = "Documentación";
                        problema.AccionSugerida = "Actualizar documentación";
                        problema.UrlAccion = $"/DocumentoCliente/Index?clienteId={evaluacion.ClienteId}";
                        problema.TipoRequisito = TipoRequisitoPendiente.DocumentacionFaltante;
                        break;

                    case "Cupo":
                        problema.Categoria = CategoriaMotivo.Cupo;
                        problema.Titulo = "Cupo";
                        problema.AccionSugerida = "Configurar o actualizar límites por puntaje";
                        problema.UrlAccion = $"/Cliente/Details/{evaluacion.ClienteId}";
                        problema.TipoRequisito = TipoRequisitoPendiente.SinLimiteCredito;
                        break;

                    case "Mora":
                        problema.Categoria = CategoriaMotivo.Mora;
                        problema.Titulo = "Mora activa";
                        problema.AccionSugerida = "Regularizar mora antes de continuar";
                        problema.UrlAccion = $"/Mora/FichaCliente/{evaluacion.ClienteId}";
                        problema.TipoRequisito = TipoRequisitoPendiente.ClienteNoApto;
                        break;

                    default:
                        problema.Categoria = CategoriaMotivo.EstadoCliente;
                        problema.Titulo = "Estado del cliente";
                        problema.TipoRequisito = TipoRequisitoPendiente.ClienteNoApto;
                        break;
                }

                evaluacion.Problemas.Add(problema);
            }

            // Si no hay detalles específicos, usar el motivo general
            if (!evaluacion.Problemas.Any())
            {
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.EstadoCliente,
                    Titulo = "Cliente no apto",
                    Descripcion = aptitud.Motivo ?? "Cliente no apto para crédito",
                    AccionSugerida = "Revisar estado del cliente",
                    UrlAccion = $"/Cliente/Details/{evaluacion.ClienteId}",
                    EsBloqueante = true,
                    TipoRequisito = TipoRequisitoPendiente.ClienteNoApto
                });
            }

            // PUN-ML10-D (regla 3): capital NoApto (u otra causa bloqueante) + punitorio aplicado
            // pendiente — el punitorio nunca es la causa del bloqueo (ClienteAptitudService ya lo
            // marca EsBloqueo=false, PUN-ML10-C regla 4), pero se informa como deuda adicional sin
            // duplicar bloqueo ni autorización: el resultado ya quedó NoViable arriba y este motivo
            // se agrega sólo como razón informativa adicional.
            foreach (var detalle in aptitud.Detalles.Where(d => !d.EsBloqueo && d.Categoria == "Punitorio"))
            {
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.Punitorio,
                    Titulo = "Punitorio aplicado pendiente",
                    Descripcion = detalle.Descripcion,
                    AccionSugerida = "Deuda adicional; no bloquea por sí sola",
                    DetalleAdicional = detalle.Descripcion,
                    EsBloqueante = false,
                    TipoRazon = TipoRazonAutorizacion.Punitorio,
                    // PUN-ML10-F: monto real del punitorio aplicado pendiente (nunca días,
                    // nunca $0 espurio — este bloque sólo corre cuando TienePunitorioAplicadoPendiente).
                    MontoAsociado = aptitud.Mora?.MontoPunitorioAplicadoPendiente,
                    ValorAsociado = aptitud.Mora?.MontoPunitorioAplicadoPendiente,
                    Unidad = UnidadValorAsociado.Monto
                });
            }
        }

        private void EvaluarClienteRequiereAutorizacionUnificado(
            EvaluacionCrediticiaIntermedia evaluacion,
            AptitudCrediticiaViewModel aptitud)
        {
            evaluacion.Resultado = ResultadoPrevalidacion.RequiereAutorizacion;

            // Analizar razones de por qué requiere autorización
            foreach (var detalle in aptitud.Detalles.Where(d => !d.EsBloqueo))
            {
                var problema = new ProblemaCredito
                {
                    Descripcion = detalle.Descripcion,
                    EsBloqueante = false
                };

                switch (detalle.Categoria)
                {
                    case "Mora":
                        problema.Categoria = CategoriaMotivo.Mora;
                        problema.Titulo = "Mora no bloqueante";
                        problema.AccionSugerida = "Supervisor debe autorizar venta";
                        problema.DetalleAdicional = detalle.Descripcion; // Incluye "mora" y días
                        problema.TipoRazon = TipoRazonAutorizacion.MoraActiva;
                        // Convención de Mora: el valor asociado es días de atraso (nunca dinero).
                        problema.DiasAsociado = aptitud.Mora?.DiasMaximoMora;
                        problema.ValorAsociado = aptitud.Mora?.DiasMaximoMora;
                        problema.Unidad = UnidadValorAsociado.Dias;
                        break;

                    // PUN-ML10-D: punitorio aplicado pendiente sin mora de capital bloqueante —
                    // motivo específico, nunca el genérico "Requiere revisión" (regla 3 de PUN-ML10-D).
                    // PUN-ML10-F: el valor asociado es el MONTO real del punitorio pendiente, nunca
                    // días de mora de capital (bug corregido — antes reutilizaba DiasMaximoMora acá
                    // y mostraba "Monto: $0,00" cuando no había mora de capital).
                    case "Punitorio":
                        problema.Categoria = CategoriaMotivo.Punitorio;
                        problema.Titulo = "Punitorio aplicado pendiente";
                        problema.AccionSugerida = "Supervisor debe autorizar venta";
                        problema.DetalleAdicional = detalle.Descripcion;
                        problema.TipoRazon = TipoRazonAutorizacion.Punitorio;
                        problema.MontoAsociado = aptitud.Mora?.MontoPunitorioAplicadoPendiente;
                        problema.ValorAsociado = aptitud.Mora?.MontoPunitorioAplicadoPendiente;
                        problema.Unidad = UnidadValorAsociado.Monto;
                        break;

                    default:
                        problema.Categoria = CategoriaMotivo.EstadoCliente;
                        problema.Titulo = "Requiere revisión";
                        problema.TipoRazon = TipoRazonAutorizacion.ClienteRequiereAutorizacion;
                        break;
                }

                evaluacion.Problemas.Add(problema);
            }

            // Si no hay detalles específicos, agregar razón genérica
            if (!evaluacion.Problemas.Any())
            {
                evaluacion.Problemas.Add(new ProblemaCredito
                {
                    Categoria = CategoriaMotivo.EstadoCliente,
                    Titulo = "Requiere autorización",
                    Descripcion = aptitud.Motivo ?? "Cliente requiere autorización para crédito",
                    AccionSugerida = "La venta requerirá aprobación de un supervisor",
                    EsBloqueante = false,
                    TipoRazon = TipoRazonAutorizacion.ClienteRequiereAutorizacion
                });
            }
        }

        private async Task VerificarCupoUnificado(
            EvaluacionCrediticiaIntermedia evaluacion,
            int clienteId,
            decimal monto,
            int? creditoId,
            decimal saldoPropioExcluido = 0m)
        {
            // Si se especifica un crédito, verificar contra ese crédito específico
            if (creditoId.HasValue)
            {
                var credito = await _context.Creditos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == creditoId.Value && c.ClienteId == clienteId && !c.IsDeleted);

                // FIN-S-002 (A1): un crédito ajeno se trata como inexistente (no revela su existencia
                // ni permite evaluar el cupo contra el saldo de otro cliente).
                if (credito == null)
                {
                    evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                    evaluacion.Problemas.Add(new ProblemaCredito
                    {
                        Categoria = CategoriaMotivo.EstadoCliente,
                        Titulo = "Crédito no encontrado",
                        Descripcion = "El crédito especificado no existe o fue eliminado",
                        EsBloqueante = true,
                        TipoRequisito = TipoRequisitoPendiente.SinCreditoAprobado
                    });
                    return;
                }

                if (credito.Estado != EstadoCredito.Activo && credito.Estado != EstadoCredito.Aprobado)
                {
                    evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                    evaluacion.Problemas.Add(new ProblemaCredito
                    {
                        Categoria = CategoriaMotivo.EstadoCliente,
                        Titulo = "Crédito no activo",
                        Descripcion = $"El crédito no está activo (estado: {credito.Estado})",
                        EsBloqueante = true,
                        TipoRequisito = TipoRequisitoPendiente.SinCreditoAprobado
                    });
                    return;
                }

                if (monto > credito.SaldoPendiente)
                {
                    evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                    evaluacion.Problemas.Add(new ProblemaCredito
                    {
                        Categoria = CategoriaMotivo.Cupo,
                        Titulo = "Excede crédito disponible",
                        Descripcion = $"Excede el crédito disponible por puntaje. Disponible: {credito.SaldoPendiente:C2}. Ajuste el monto, cambie método de pago o actualice puntaje/límites.",
                        AccionSugerida = "Ajustar monto, cambiar método de pago o actualizar puntaje/límites",
                        ValorAsociado = monto,
                        ValorLimite = credito.SaldoPendiente,
                        EsBloqueante = true,
                        TipoRequisito = TipoRequisitoPendiente.ClienteNoApto
                    });
                }
                return;
            }

            // Verificar contra el cupo general del cliente
            // Con crédito propio excluido, el disponible ya se recalculó sobre el límite (ver EvaluarCreditoUnificadoAsync).
            var cupoDisponible = await _aptitudService.GetCupoDisponibleAsync(clienteId, saldoPropioExcluido);
            evaluacion.CupoDisponible = cupoDisponible;

            if (monto > cupoDisponible)
            {
                // Si ya es NoViable, no cambiar
                if (evaluacion.Resultado == ResultadoPrevalidacion.NoViable)
                    return;

                // Verificar si hay cupo asignado (limite o cupo > 0)
                var tieneCupoAsignado = (evaluacion.LimiteCredito.HasValue && evaluacion.LimiteCredito.Value > 0) 
                                       || cupoDisponible > 0;
                
                // Si excede cupo pero hay cupo asignado, requiere autorización
                if (tieneCupoAsignado)
                {
                    evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                    evaluacion.Problemas.Add(new ProblemaCredito
                    {
                        Categoria = CategoriaMotivo.Cupo,
                        Titulo = "Excede crédito disponible",
                        Descripcion = $"Excede el crédito disponible por puntaje. Disponible: {cupoDisponible:C2}. Ajuste el monto, cambie método de pago o actualice puntaje/límites.",
                        AccionSugerida = "Ajustar monto, cambiar método de pago o actualizar puntaje/límites",
                        ValorAsociado = monto,
                        ValorLimite = cupoDisponible,
                        EsBloqueante = true,
                        TipoRequisito = TipoRequisitoPendiente.ClienteNoApto
                    });
                }
                else
                {
                    // Sin cupo asignado, es bloqueante
                    evaluacion.Resultado = ResultadoPrevalidacion.NoViable;
                    evaluacion.Problemas.Add(new ProblemaCredito
                    {
                        Categoria = CategoriaMotivo.Cupo,
                        Titulo = "Sin límite de crédito por puntaje",
                        Descripcion = "No hay límite de crédito configurado para el puntaje del cliente.",
                        AccionSugerida = "Configurar límite por puntaje del cliente",
                        UrlAccion = $"/Cliente/Details/{clienteId}",
                        EsBloqueante = true,
                        TipoRequisito = TipoRequisitoPendiente.SinLimiteCredito
                    });
                }
            }
        }

        #endregion

        #region Mappers

        private PrevalidacionResultViewModel MapearAPrevalidacion(EvaluacionCrediticiaIntermedia evaluacion)
        {
            var resultado = new PrevalidacionResultViewModel
            {
                ClienteId = evaluacion.ClienteId,
                MontoSolicitado = evaluacion.MontoSolicitado,
                Resultado = evaluacion.Resultado,
                Timestamp = DateTime.UtcNow,
                LimiteCredito = evaluacion.LimiteCredito,
                CupoDisponible = evaluacion.CupoDisponible,
                CreditoUtilizado = evaluacion.CreditoUtilizado,
                TieneMora = evaluacion.TieneMora,
                DiasMora = evaluacion.DiasMora,
                MontoMora = evaluacion.MontoMora,
                DocumentacionCompleta = evaluacion.DocumentacionCompleta,
                DocumentosFaltantes = evaluacion.DocumentosFaltantes,
                DocumentosVencidos = evaluacion.DocumentosVencidos
            };

            foreach (var problema in evaluacion.Problemas)
            {
                resultado.Motivos.Add(new MotivoPrevalidacion
                {
                    Categoria = problema.Categoria,
                    Titulo = problema.Titulo,
                    Descripcion = problema.Descripcion,
                    AccionSugerida = problema.AccionSugerida,
                    UrlAccion = problema.UrlAccion,
                    EsBloqueante = problema.EsBloqueante,
                    // PUN-ML10-F: valor tipado (nunca mezcla monto/días sin unidad).
                    MontoAsociado = problema.MontoAsociado,
                    DiasAsociado = problema.DiasAsociado,
                    Unidad = problema.Unidad
                });
            }

            return resultado;
        }

        private ValidacionVentaResult MapearAValidacionVenta(EvaluacionCrediticiaIntermedia evaluacion)
        {
            var resultado = new ValidacionVentaResult
            {
                NoViable = evaluacion.EsNoViable,
                RequiereAutorizacion = evaluacion.RequiereAutorizacion,
                PendienteRequisitos = evaluacion.EsNoViable,
                EstadoAptitud = evaluacion.EstadoAptitud
            };

            foreach (var problema in evaluacion.Problemas)
            {
                if (problema.EsBloqueante)
                {
                    resultado.RequisitosPendientes.Add(new RequisitoPendiente
                    {
                        Tipo = problema.TipoRequisito ?? TipoRequisitoPendiente.ClienteNoApto,
                        Descripcion = problema.Descripcion,
                        AccionRequerida = problema.AccionSugerida,
                        UrlAccion = problema.UrlAccion
                    });
                }
                else
                {
                    resultado.RazonesAutorizacion.Add(new RazonAutorizacion
                    {
                        Tipo = problema.TipoRazon ?? TipoRazonAutorizacion.ClienteRequiereAutorizacion,
                        Descripcion = problema.Descripcion,
                        DetalleAdicional = problema.DetalleAdicional ?? problema.AccionSugerida,
                        ValorAsociado = problema.ValorAsociado,
                        ValorLimite = problema.ValorLimite,
                        // PUN-ML10-F: valor tipado (nunca mezcla monto/días sin unidad).
                        MontoAsociado = problema.MontoAsociado,
                        DiasAsociado = problema.DiasAsociado,
                        Unidad = problema.Unidad
                    });
                }
            }

            return resultado;
        }

        #endregion

        #region Otros Métodos Públicos

        public async Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);

            if (venta == null)
            {
                return new ValidacionVentaResult
                {
                    PendienteRequisitos = true,
                    RequisitosPendientes = new List<RequisitoPendiente>
                    {
                        new() { Tipo = TipoRequisitoPendiente.SinCreditoAprobado, Descripcion = "Venta no encontrada" }
                    }
                };
            }

            // Si no es crédito personal, no hay validaciones adicionales
            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return new ValidacionVentaResult(); // Puede proceder
            }

            var clienteId = venta.ClienteId ?? throw new InvalidOperationException("El crédito requiere un cliente registrado.");

            // Si el crédito ya está configurado/generado/activo, la aptitud fue evaluada
            // al configurar. En confirmación sólo corresponde verificar mora y documentación,
            // no volver a bloquear por cupo (que ya fue comprometido en la configuración).
            if (venta.CreditoId.HasValue)
            {
                var credito = await _context.Creditos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == venta.CreditoId.Value && !c.IsDeleted);

                if (credito != null && (
                    credito.Estado == EstadoCredito.Configurado ||
                    credito.Estado == EstadoCredito.Generado   ||
                    credito.Estado == EstadoCredito.Activo))
                {
                    return await ValidarSinCupoAsync(clienteId);
                }
            }

            return await ValidarVentaCreditoPersonalAsync(clienteId, venta.Total, venta.CreditoId);
        }

        // Evaluación sin cupo: sólo mora bloqueante y documentación.
        // Para el paso de confirmación, donde el cupo ya fue comprometido.
        private async Task<ValidacionVentaResult> ValidarSinCupoAsync(int clienteId)
        {
            var aptitud = await _aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);
            var resultado = new ValidacionVentaResult();

            foreach (var detalle in aptitud.Detalles.Where(d => d.EsBloqueo && d.Categoria != "Cupo"))
            {
                var tipo = detalle.Categoria switch
                {
                    "Documentación" => TipoRequisitoPendiente.DocumentacionFaltante,
                    _               => TipoRequisitoPendiente.ClienteNoApto
                };
                resultado.RequisitosPendientes.Add(new RequisitoPendiente
                {
                    Tipo             = tipo,
                    Descripcion      = detalle.Descripcion,
                    AccionRequerida  = "Revisar"
                });
            }

            if (resultado.RequisitosPendientes.Any())
            {
                resultado.NoViable          = true;
                resultado.PendienteRequisitos = true;
            }

            return resultado;
        }

        #endregion
    }
}
