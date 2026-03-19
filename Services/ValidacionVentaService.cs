using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<ValidacionVentaService> _logger;

        public ValidacionVentaService(
            AppDbContext context,
            IClienteAptitudService aptitudService,
            ILogger<ValidacionVentaService> logger)
        {
            _context = context;
            _aptitudService = aptitudService;
            _logger = logger;
        }

        #region Prevalidación (E1 - Solo lectura, no persiste)

        /// <inheritdoc />
        public async Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto)
        {
            _logger.LogInformation(
                "Iniciando prevalidación para cliente {ClienteId}, monto {Monto}",
                clienteId, monto);

            try
            {
                // Usar evaluación unificada
                var evaluacion = await EvaluarCreditoUnificadoAsync(clienteId, monto);
                
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
            int? creditoId = null)
        {
            var evaluacion = new EvaluacionCrediticiaIntermedia
            {
                ClienteId = clienteId,
                MontoSolicitado = monto
            };

            // 1. Evaluar aptitud crediticia del cliente
            var aptitud = await _aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

            // 2. Poblar datos básicos
            PoblarDatosBasicos(evaluacion, aptitud);

            // 3. Evaluar según estado de aptitud
            EvaluarSegunEstadoAptitud(evaluacion, aptitud);

            // 4. Verificar cupo para el monto específico (si no es NoViable)
            if (!evaluacion.EsNoViable)
            {
                await VerificarCupoUnificado(evaluacion, clienteId, monto, creditoId);
            }

            return evaluacion;
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
                    EsBloqueante = false,
                    ValorAsociado = aptitud.Mora?.DiasMaximoMora
                };

                switch (detalle.Categoria)
                {
                    case "Mora":
                        problema.Categoria = CategoriaMotivo.Mora;
                        problema.Titulo = "Mora no bloqueante";
                        problema.AccionSugerida = "Supervisor debe autorizar venta";
                        problema.DetalleAdicional = detalle.Descripcion; // Incluye "mora" y días
                        problema.TipoRazon = TipoRazonAutorizacion.MoraActiva;
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
            int? creditoId)
        {
            // Si se especifica un crédito, verificar contra ese crédito específico
            if (creditoId.HasValue)
            {
                var credito = await _context.Creditos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == creditoId.Value && !c.IsDeleted);

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
            var cupoDisponible = await _aptitudService.GetCupoDisponibleAsync(clienteId);
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
                    EsBloqueante = problema.EsBloqueante
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
                        ValorLimite = problema.ValorLimite
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

            return await ValidarVentaCreditoPersonalAsync(venta.ClienteId, venta.Total, venta.CreditoId);
        }

        public async Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado)
        {
            var resultado = await ValidarVentaCreditoPersonalAsync(clienteId, montoSolicitado);
            return resultado.PuedeProceeder;
        }

        public async Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId)
        {
            var resumen = new ResumenCrediticioClienteViewModel();

            // Evaluar aptitud (sin guardar)
            var aptitud = await _aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

            resumen.EstadoAptitud = aptitud.TextoEstado;
            resumen.ColorSemaforo = aptitud.ColorSemaforo;
            resumen.Icono = aptitud.Icono;

            // Información de documentación
            if (aptitud.Documentacion != null)
            {
                resumen.DocumentacionCompleta = aptitud.Documentacion.Completa;
                if (!aptitud.Documentacion.Completa)
                {
                    resumen.DocumentosFaltantes = string.Join(", ", aptitud.Documentacion.DocumentosFaltantes);
                }
            }

            // Información de cupo
            if (aptitud.Cupo != null)
            {
                resumen.LimiteCredito = aptitud.Cupo.LimiteCredito;
                resumen.CupoDisponible = aptitud.Cupo.CupoDisponible;
                resumen.CreditoUtilizado = aptitud.Cupo.CreditoUtilizado;
            }

            // Información de mora
            if (aptitud.Mora != null)
            {
                resumen.TieneMoraActiva = aptitud.Mora.TieneMora;
                resumen.DiasMaxMora = aptitud.Mora.DiasMaximoMora;
            }

            // Mensaje de advertencia
            if (aptitud.Estado == EstadoCrediticioCliente.NoApto)
            {
                resumen.MensajeAdvertencia = aptitud.Motivo ?? "Cliente no apto para crédito";
            }
            else if (aptitud.Estado == EstadoCrediticioCliente.RequiereAutorizacion)
            {
                resumen.MensajeAdvertencia = "Este cliente requiere autorización para crédito";
            }
            else if (aptitud.Estado == EstadoCrediticioCliente.NoEvaluado && !aptitud.ConfiguracionCompleta)
            {
                resumen.MensajeAdvertencia = aptitud.AdvertenciaConfiguracion;
            }

            // Créditos activos
            var creditosActivos = await _context.Creditos
                .AsNoTracking()
                .Where(c => c.ClienteId == clienteId &&
                           !c.IsDeleted &&
                           (c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Aprobado))
                .ToListAsync();

            resumen.CreditosActivos = creditosActivos.Select(c => new CreditoActivoResumen
            {
                Id = c.Id,
                Numero = c.Numero,
                MontoAprobado = c.MontoAprobado,
                SaldoDisponible = c.SaldoPendiente,
                Estado = c.Estado.ToString()
            }).ToList();

            return resumen;
        }
        
        #endregion
    }
}
