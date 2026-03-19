using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para evaluación crediticia
    /// Implementa scoring automático, validaciones y cálculo de capacidad de pago
    /// </summary>
    public class EvaluacionCreditoService : IEvaluacionCreditoService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<EvaluacionCreditoService> _logger;
        private ConfiguracionEvaluacionViewModel? _config;

        public EvaluacionCreditoService(
            AppDbContext context,
            IMapper mapper,
            ILogger<EvaluacionCreditoService> logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<EvaluacionCreditoViewModel> EvaluarSolicitudAsync(
            int clienteId,
            decimal montoSolicitado,
            int? garanteId = null)
        {
            try
            {
                _logger.LogInformation("=== INICIANDO EVALUACIÓN CREDITICIA ===");
                _logger.LogInformation("ClienteId: {ClienteId}, Monto: {Monto}, Garante: {GaranteId}",
                    clienteId, montoSolicitado, garanteId ?? 0);

                // Cargar configuración
                _config ??= await GetConfiguracionAsync();

                var cliente = await _context.Clientes
                    .Include(c => c.Creditos)
                    .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

                if (cliente == null)
                    throw new InvalidOperationException($"Cliente {clienteId} no encontrado");

                var evaluacion = new EvaluacionCreditoViewModel
                {
                    ClienteId = clienteId,
                    ClienteNombre = cliente.NombreCompleto ?? "Desconocido",  // ✅ CORREGIDO: ?? operator
                    MontoSolicitado = montoSolicitado,
                    SueldoCliente = cliente.Sueldo,
                    PuntajeRiesgoCliente = cliente.PuntajeRiesgo,
                    TieneGarante = garanteId.HasValue,
                    FechaEvaluacion = DateTime.UtcNow,
                    Reglas = new List<ReglaEvaluacionViewModel>()
                };

                // Evaluar cada regla
                await EvaluarTodasLasReglasAsync(evaluacion, cliente, montoSolicitado);

                // Calcular puntaje total
                evaluacion.PuntajeFinal = Math.Max(0, evaluacion.Reglas.Sum(r => r.Peso));

                // Determinar resultado
                DeterminarResultado(evaluacion);

                // Generar motivos explicativos
                GenerarMotivos(evaluacion);

                // ✅ GUARDAR en BD para auditoría
                await GuardarEvaluacionAsync(evaluacion);

                _logger.LogInformation("Evaluación completada: {Resultado} - Puntaje: {Puntaje}",
                    evaluacion.Resultado, evaluacion.PuntajeFinal);

                return evaluacion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al evaluar solicitud de crédito para cliente {ClienteId}", clienteId);
                throw;
            }
        }

        private async Task EvaluarTodasLasReglasAsync(
            EvaluacionCreditoViewModel evaluacion,
            Cliente cliente,
            decimal montoSolicitado)
        {
            // 1️⃣ Puntaje de Riesgo
            evaluacion.Reglas.Add(EvaluarPuntajeRiesgo(cliente.PuntajeRiesgo));

            // 2️⃣ Documentación
            var reglaDoc = await EvaluarDocumentacionAsync(cliente.Id);
            evaluacion.Reglas.Add(reglaDoc);
            evaluacion.TieneDocumentacionCompleta = reglaDoc.Cumple;

            // 3️⃣ Ingresos y Capacidad de Pago
            var reglaIngresos = EvaluarIngresos(cliente, montoSolicitado);
            evaluacion.Reglas.Add(reglaIngresos);
            evaluacion.TieneIngresosSuficientes = reglaIngresos.Cumple;
            
            if (cliente.Sueldo.HasValue && cliente.Sueldo > 0)
            {
                decimal cuotaEstimada = montoSolicitado * 0.10m;
                evaluacion.RelacionCuotaIngreso = cuotaEstimada / cliente.Sueldo.Value;
            }

            // 4️⃣ Historial Crediticio
            var reglaHistorial = EvaluarHistorial(cliente);
            evaluacion.Reglas.Add(reglaHistorial);
            evaluacion.TieneBuenHistorial = reglaHistorial.Cumple;

            // 5️⃣ Garante (si aplica)
            var reglaGarante = EvaluarGarante(montoSolicitado, evaluacion.TieneGarante);
            evaluacion.Reglas.Add(reglaGarante);
        }

        // ✅ MÉTODOS PRIVADOS CONSOLIDADOS
        private ReglaEvaluacionViewModel EvaluarPuntajeRiesgo(decimal puntajeRiesgo)
        {
            var regla = new ReglaEvaluacionViewModel { Nombre = "Puntaje de Riesgo" };

            if (puntajeRiesgo >= 7.0m)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 30, $"Excelente: {puntajeRiesgo}/10", false);
            else if (puntajeRiesgo >= 5.0m)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 20, $"Bueno: {puntajeRiesgo}/10", false);
            else if (puntajeRiesgo >= _config!.PuntajeRiesgoMinimo)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 10, $"Aceptable: {puntajeRiesgo}/10", false);
            else
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (false, 0, $"Insuficiente: {puntajeRiesgo}/10 (mínimo {_config!.PuntajeRiesgoMinimo})", true);

            return regla;
        }

        private async Task<ReglaEvaluacionViewModel> EvaluarDocumentacionAsync(int clienteId)
        {
            var regla = new ReglaEvaluacionViewModel { Nombre = "Documentación", EsCritica = true };

            var documentosVerificados = await _context.Set<DocumentoCliente>()
                .Where(d => d.ClienteId == clienteId
                         && !d.IsDeleted
                         && d.Estado == EstadoDocumento.Verificado
                         && (!d.FechaVencimiento.HasValue || d.FechaVencimiento.Value >= DateTime.Today))
                .ToListAsync();

            int docsImportantes = 0;
            var docsEncontrados = new List<string>();

            if (documentosVerificados.Any(d => d.TipoDocumento == TipoDocumentoCliente.DNI))
            {
                docsImportantes++;
                docsEncontrados.Add("DNI");
            }

            if (documentosVerificados.Any(d => d.TipoDocumento == TipoDocumentoCliente.ReciboSueldo))
            {
                docsImportantes++;
                docsEncontrados.Add("Recibo Sueldo");
            }

            if (documentosVerificados.Any(d => d.TipoDocumento == TipoDocumentoCliente.Servicio))
            {
                docsImportantes++;
                docsEncontrados.Add("Servicio");
            }

            bool tieneVeraz = documentosVerificados.Any(d => d.TipoDocumento == TipoDocumentoCliente.Veraz);
            bool tieneCUIL = documentosVerificados.Any(d => d.TipoDocumento == TipoDocumentoCliente.ConstanciaCUIL);

            if (docsImportantes >= 3)
            {
                regla.Cumple = true;
                regla.Peso = tieneVeraz || tieneCUIL ? 25 : 20;
                regla.Detalle = $"Completa: {string.Join(", ", docsEncontrados)}" + 
                    (tieneVeraz || tieneCUIL ? " + Veraz/CUIL" : "");
            }
            else if (docsImportantes >= 2)
            {
                regla.Cumple = true;
                regla.Peso = 10;
                regla.Detalle = $"Parcial: {string.Join(", ", docsEncontrados)} ({docsImportantes}/3)";
            }
            else
            {
                regla.Cumple = false;
                regla.Peso = -5;
                regla.Detalle = $"Insuficiente: {docsImportantes}/3 documentos requeridos";
            }

            return regla;
        }

        private ReglaEvaluacionViewModel EvaluarIngresos(Cliente cliente, decimal montoSolicitado)
        {
            var regla = new ReglaEvaluacionViewModel { Nombre = "Capacidad de Pago", EsCritica = true };

            if (!cliente.Sueldo.HasValue || cliente.Sueldo <= 0)
                return new ReglaEvaluacionViewModel 
                { 
                    Nombre = "Capacidad de Pago",
                    Cumple = false,
                    Peso = -10,
                    Detalle = "No declaró ingresos",
                    EsCritica = true
                };

            decimal cuotaEstimada = montoSolicitado * 0.10m;
            decimal relacionCuotaIngreso = cuotaEstimada / cliente.Sueldo.Value;

            if (relacionCuotaIngreso <= 0.25m)
                (regla.Cumple, regla.Peso, regla.Detalle) = 
                    (true, 25, $"Excelente: {relacionCuotaIngreso:P0} del sueldo");
            else if (relacionCuotaIngreso <= _config!.RelacionCuotaIngresoMax)
                (regla.Cumple, regla.Peso, regla.Detalle) = 
                    (true, 15, $"Aceptable: {relacionCuotaIngreso:P0} del sueldo");
            else if (relacionCuotaIngreso <= 0.45m)
                (regla.Cumple, regla.Peso, regla.Detalle) = 
                    (false, 0, $"Ajustada: {relacionCuotaIngreso:P0} del sueldo");
            else
                (regla.Cumple, regla.Peso, regla.Detalle) = 
                    (false, -15, $"Insuficiente: {relacionCuotaIngreso:P0} > {_config!.RelacionCuotaIngresoMax:P0}");

            return regla;
        }

        private ReglaEvaluacionViewModel EvaluarHistorial(Cliente cliente)
        {
            var regla = new ReglaEvaluacionViewModel { Nombre = "Historial Crediticio" };

            var creditos = cliente.Creditos.Where(c => !c.IsDeleted && c.Estado != EstadoCredito.Solicitado).ToList();

            if (!creditos.Any())
                return new ReglaEvaluacionViewModel
                {
                    Nombre = "Historial Crediticio",
                    Cumple = true,
                    Peso = 10,
                    Detalle = "Cliente nuevo (sin historial previo)"
                };

            var finalizados = creditos.Count(c => c.Estado == EstadoCredito.Finalizado);
            var cancelados = creditos.Count(c => c.Estado == EstadoCredito.Cancelado);
            var activos = creditos.Count(c => c.Estado == EstadoCredito.Activo);

            if (cancelados > 0)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (false, -20, $"Historial negativo: {cancelados} cancelado(s)", true);
            else if (finalizados >= 2)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 15, $"Excelente: {finalizados} pagado(s), {activos} activo(s)", false);
            else if (finalizados >= 1 || activos > 0)
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 10, $"Bueno: {finalizados} finalizado(s), {activos} activo(s)", false);
            else
                (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                    (true, 5, "En construcción", false);

            return regla;
        }

        private ReglaEvaluacionViewModel EvaluarGarante(decimal montoSolicitado, bool tienesGarante)
        {
            var regla = new ReglaEvaluacionViewModel { Nombre = "Garantía/Garante" };

            if (montoSolicitado >= _config!.MontoRequiereGarante)
            {
                if (tienesGarante)
                    (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                        (true, 10, $"Garante presente (requerido ≥ ${_config!.MontoRequiereGarante:N0})", false);
                else
                    (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                        (false, -15, $"Falta garante (requerido ≥ ${_config!.MontoRequiereGarante:N0})", true);
            }
            else
            {
                if (tienesGarante)
                    (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                        (true, 5, "Garante adicional (opcional)", false);
                else
                    (regla.Cumple, regla.Peso, regla.Detalle, regla.EsCritica) = 
                        (true, 0, "Garante no requerido", false);
            }

            return regla;
        }

        private void DeterminarResultado(EvaluacionCreditoViewModel evaluacion)
        {
            // Validar reglas críticas
            var reglasCriticas = evaluacion.Reglas.Where(r => r.EsCritica && !r.Cumple).ToList();
            
            if (reglasCriticas.Any())
            {
                evaluacion.Resultado = ResultadoEvaluacion.Rechazado;
                return;
            }

            // Usar puntaje para resultado
            if (evaluacion.PuntajeFinal >= _config!.PuntajeMinimoParaAprobacion)
                evaluacion.Resultado = ResultadoEvaluacion.Aprobado;
            else if (evaluacion.PuntajeFinal >= _config.PuntajeMinimoParaAnalisis)
                evaluacion.Resultado = ResultadoEvaluacion.RequiereAnalisis;
            else
                evaluacion.Resultado = ResultadoEvaluacion.Rechazado;
        }

        private void GenerarMotivos(EvaluacionCreditoViewModel evaluacion)
        {
            var reglasCumplidas = evaluacion.Reglas.Where(r => r.Cumple).ToList();
            var reglasIncumplidas = evaluacion.Reglas.Where(r => !r.Cumple).ToList();

            switch (evaluacion.Resultado)
            {
                case ResultadoEvaluacion.Aprobado:
                    evaluacion.Motivo = $"Solicitud aprobada. Puntaje: {evaluacion.PuntajeFinal}/100. " +
                        $"Cumple con criterios principales: {string.Join(", ", reglasCumplidas.Select(r => r.Nombre).Take(3))}";
                    break;

                case ResultadoEvaluacion.RequiereAnalisis:
                    evaluacion.Motivo = $"Requiere análisis manual. Puntaje: {evaluacion.PuntajeFinal}/100. " +
                        $"Revisar: {string.Join(", ", reglasIncumplidas.Select(r => r.Nombre))}";
                    break;

                case ResultadoEvaluacion.Rechazado:
                    evaluacion.Motivo = $"Solicitud rechazada. Puntaje: {evaluacion.PuntajeFinal}/100. " +
                        $"Motivos: {string.Join("; ", reglasIncumplidas.Select(r => $"{r.Nombre}: {r.Detalle}"))}";
                    break;
            }
        }

        private async Task GuardarEvaluacionAsync(EvaluacionCreditoViewModel evaluacion)
        {
            try
            {
                var entidad = new EvaluacionCredito
                {
                    ClienteId = evaluacion.ClienteId,
                    CreditoId = evaluacion.CreditoId,
                    Resultado = evaluacion.Resultado,
                    PuntajeFinal = evaluacion.PuntajeFinal,
                    PuntajeRiesgoCliente = evaluacion.PuntajeRiesgoCliente,
                    MontoSolicitado = evaluacion.MontoSolicitado,
                    SueldoCliente = evaluacion.SueldoCliente,
                    RelacionCuotaIngreso = evaluacion.RelacionCuotaIngreso,
                    TieneDocumentacionCompleta = evaluacion.TieneDocumentacionCompleta,
                    TieneIngresosSuficientes = evaluacion.TieneIngresosSuficientes,
                    TieneBuenHistorial = evaluacion.TieneBuenHistorial,
                    TieneGarante = evaluacion.TieneGarante,
                    Motivo = evaluacion.Motivo,
                    Observaciones = evaluacion.Observaciones,
                    FechaEvaluacion = DateTime.UtcNow
                };

                _context.EvaluacionesCredito.Add(entidad);
                await _context.SaveChangesAsync();

                evaluacion.Id = entidad.Id;
                _logger.LogInformation("Evaluación guardada con Id {Id}", entidad.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar evaluación");
                throw;
            }
        }

        public async Task<EvaluacionCreditoViewModel?> GetEvaluacionByCreditoIdAsync(int creditoId)
        {
            try
            {
                var evaluacion = await _context.EvaluacionesCredito
                    .Include(e => e.Cliente)
                    .Include(e => e.Credito)
                    .Where(e => e.CreditoId == creditoId &&
                               !e.IsDeleted &&
                               e.Cliente != null &&
                               !e.Cliente.IsDeleted &&
                               e.Credito != null &&
                               !e.Credito.IsDeleted)
                    .OrderByDescending(e => e.FechaEvaluacion)
                    .FirstOrDefaultAsync();

                return evaluacion != null ? _mapper.Map<EvaluacionCreditoViewModel>(evaluacion) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener evaluación del crédito {CreditoId}", creditoId);
                throw;
            }
        }

        public async Task<List<EvaluacionCreditoViewModel>> GetEvaluacionesByClienteIdAsync(int clienteId)
        {
            try
            {
                var evaluaciones = await _context.EvaluacionesCredito
                    .Include(e => e.Credito)
                    .Where(e => e.ClienteId == clienteId &&
                               !e.IsDeleted &&
                               e.Cliente != null &&
                               !e.Cliente.IsDeleted &&
                               e.Credito != null &&
                               !e.Credito.IsDeleted)
                    .OrderByDescending(e => e.FechaEvaluacion)
                    .ToListAsync();

                return _mapper.Map<List<EvaluacionCreditoViewModel>>(evaluaciones);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener evaluaciones del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<ConfiguracionEvaluacionViewModel> GetConfiguracionAsync()
        {
            try
            {
                // TODO: Implementar lectura desde BD cuando exista tabla
                return new ConfiguracionEvaluacionViewModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración de evaluación");
                throw;
            }
        }

        public async Task<bool> ActualizarConfiguracionAsync(ConfiguracionEvaluacionViewModel config)
        {
            try
            {
                // TODO: Implementar actualización en BD cuando exista tabla
                _config = config;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar configuración de evaluación");
                throw;
            }
        }
    }
}
