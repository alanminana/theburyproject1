using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Threading;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Services
{
    public class CreditoService : ICreditoService
    {
        private const int MinCuotasCredito = 1;
        private const int MaxCuotasCredito = 120;
        private static readonly IReadOnlyDictionary<string, string> MediosPagoPermitidos =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Efectivo"] = "Efectivo",
                ["Transferencia"] = "Transferencia",
                ["Tarjeta Débito"] = "Tarjeta Débito",
                ["Tarjeta Crédito"] = "Tarjeta Crédito",
                ["Cheque"] = "Cheque"
            };

        private static readonly IReadOnlyDictionary<string, TipoPago> MediosPagoTipoPago =
            new Dictionary<string, TipoPago>(StringComparer.OrdinalIgnoreCase)
            {
                ["Efectivo"] = TipoPago.Efectivo,
                ["Transferencia"] = TipoPago.Transferencia,
                ["Tarjeta Débito"] = TipoPago.TarjetaDebito,
                ["Tarjeta Crédito"] = TipoPago.TarjetaCredito,
                ["Cheque"] = TipoPago.Cheque
            };

        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<CreditoService> _logger;
        private readonly IFinancialCalculationService _financialService;
        private readonly ICajaService _cajaService;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IClienteScoringService _clienteScoringService;
        private readonly IConfiguracionPagoService? _configuracionPagoService;

        public CreditoService(
            AppDbContext context,
            IMapper mapper,
            ILogger<CreditoService> logger,
            IFinancialCalculationService financialService,
            ICajaService cajaService,
            ICreditoDisponibleService creditoDisponibleService,
            ICurrentUserService currentUserService,
            IClienteScoringService? clienteScoringService = null,
            IConfiguracionPagoService? configuracionPagoService = null)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _financialService = financialService;
            _cajaService = cajaService;
            _creditoDisponibleService = creditoDisponibleService;
            _currentUserService = currentUserService;
            _clienteScoringService = clienteScoringService ?? new ClienteScoringService(context, NullLogger<ClienteScoringService>.Instance);
            _configuracionPagoService = configuracionPagoService;
        }

        /// <summary>
        /// Resuelve el porcentaje de ajuste vigente del medio de pago (recargo positivo /
        /// descuento negativo) para cobros en un pago, y el TipoPago estructurado del medio.
        /// </summary>
        private async Task<(decimal Porcentaje, TipoPago? TipoPago)> ObtenerAjusteMedioPagoAsync(string medioPago)
        {
            if (_configuracionPagoService == null || !MediosPagoTipoPago.TryGetValue(medioPago, out var tipoPago))
                return (0m, null);

            var porcentaje = await _configuracionPagoService.ObtenerPorcentajeAjusteUnPagoAsync(tipoPago);
            return (porcentaje, tipoPago);
        }

        private static decimal CalcularRecargoMedioPago(decimal montoBase, decimal porcentaje) =>
            montoBase <= 0m || porcentaje == 0m
                ? 0m
                : Math.Round(montoBase * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);

        #region CRUD Básico

        public async Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null)
        {
            try
            {
                var query = _context.Creditos
                    .AsNoTracking()
                    .Where(c => !c.IsDeleted &&
                                c.Cliente != null &&
                                !c.Cliente.IsDeleted)
                    .Include(c => c.Cliente)
                    .Include(c => c.Garante)
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .AsQueryable();

                // Aplicar filtros
                if (filter != null)
                {
                    if (filter.ClienteId.HasValue)
                        query = query.Where(c => c.ClienteId == filter.ClienteId.Value);

                    if (!string.IsNullOrWhiteSpace(filter.Numero))
                        query = query.Where(c => c.Numero.Contains(filter.Numero));

                    if (!string.IsNullOrWhiteSpace(filter.Cliente))
                    {
                        var clienteTerm = filter.Cliente.Trim();
                        query = query.Where(c =>
                            c.Numero.Contains(clienteTerm) ||
                            c.Cliente.NumeroDocumento.Contains(clienteTerm) ||
                            c.Cliente.Nombre.Contains(clienteTerm) ||
                            c.Cliente.Apellido.Contains(clienteTerm));
                    }

                    if (filter.Estado.HasValue)
                        query = query.Where(c => c.Estado == filter.Estado.Value);

                    if (filter.FechaDesde.HasValue)
                        query = query.Where(c => c.FechaSolicitud >= filter.FechaDesde.Value);

                    if (filter.FechaHasta.HasValue)
                        query = query.Where(c => c.FechaSolicitud <= filter.FechaHasta.Value);

                    if (filter.MontoMinimo.HasValue)
                        query = query.Where(c => c.MontoAprobado >= filter.MontoMinimo.Value);

                    if (filter.MontoMaximo.HasValue)
                        query = query.Where(c => c.MontoAprobado <= filter.MontoMaximo.Value);

                    if (filter.SoloCuotasVencidas)
                        query = query.Where(c => c.Cuotas.Any(cu =>
                            !cu.IsDeleted &&
                            (cu.Estado == EstadoCuota.Vencida ||
                             (cu.Estado == EstadoCuota.Pendiente && cu.FechaVencimiento < DateTime.UtcNow))));
                }

                // El listado principal muestra solo creditos generados correctamente.
                // Operaciones en curso (solicitud, configuracion pendiente, plan configurado
                // sin confirmar) o rechazadas aparecen unicamente al filtrar por ese estado.
                if (filter?.Estado == null)
                {
                    query = query.Where(c =>
                        c.Estado != EstadoCredito.Solicitado &&
                        c.Estado != EstadoCredito.PendienteConfiguracion &&
                        c.Estado != EstadoCredito.Configurado &&
                        c.Estado != EstadoCredito.Rechazado);
                }

                var creditos = await query
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                var modelos = _mapper.Map<List<CreditoViewModel>>(creditos);
                await CargarProductosAsociadosAsync(modelos);

                return modelos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener créditos");
                throw;
            }
        }

        public async Task<CreditoViewModel?> GetByIdAsync(int id)
        {
            try
            {
                var credito = await _context.Creditos
                    .AsNoTracking()
                    .Include(c => c.Cliente)
                    .Include(c => c.Garante)
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                    .FirstOrDefaultAsync(c => c.Id == id &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);

                if (credito == null)
                    return null;

                var modelo = _mapper.Map<CreditoViewModel>(credito);
                await CargarProductosAsociadosAsync(new[] { modelo });

                return modelo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener crédito por ID: {Id}", id);
                throw;
            }
        }

        public async Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId)
        {
            try
            {
                var creditos = await _context.Creditos
                    .AsNoTracking()
                    .Include(c => c.Cliente)
                    .Include(c => c.Garante)
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                    .Where(c => c.ClienteId == clienteId &&
                                !c.IsDeleted &&
                                c.Cliente != null &&
                                !c.Cliente.IsDeleted)
                    .OrderByDescending(c => c.FechaSolicitud)
                    .ToListAsync();

                var modelos = _mapper.Map<List<CreditoViewModel>>(creditos);
                await CargarProductosAsociadosAsync(modelos);

                return modelos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener créditos del cliente: {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel)
        {
            try
            {
                // Obtener cliente para validaciones
                var cliente = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Id == viewModel.ClienteId && !c.IsDeleted);
                if (cliente == null)
                    throw new InvalidOperationException("Cliente no encontrado");

                await ValidarMontoDentroDelDisponibleAsync(cliente.Id, viewModel.MontoSolicitado);

                // Generar número de crédito
                viewModel.Numero = await GenerarNumeroCreditoAsync();
                viewModel.PuntajeRiesgoInicial = cliente.PuntajeRiesgo;
                if (viewModel.Estado == 0)
                    viewModel.Estado = EstadoCredito.Solicitado;
                viewModel.FechaSolicitud = DateTime.UtcNow;

                // CAMBIO IMPORTANTE: No calculamos cuotas ni totales
                // El MontoAprobado se iguala al MontoSolicitado
                viewModel.MontoAprobado = viewModel.MontoSolicitado;
                // El SaldoPendiente inicia igual al monto aprobado (disponible completo)
                viewModel.SaldoPendiente = viewModel.MontoAprobado;

                var credito = _mapper.Map<Credito>(viewModel);
                _context.Creditos.Add(credito);
                await _context.SaveChangesAsync();

                viewModel.Id = credito.Id;

                _logger.LogInformation("Línea de crédito {Numero} creada para cliente {ClienteId} por ${Monto}",
                    viewModel.Numero, viewModel.ClienteId, viewModel.MontoAprobado);

                return viewModel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear crédito");
                throw;
            }
        }
        public async Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal)
        {
            var creditoVm = new CreditoViewModel
            {
                ClienteId = clienteId,
                MontoSolicitado = montoTotal,
                MontoAprobado = montoTotal,
                SaldoPendiente = montoTotal,
                TasaInteres = 0,
                CantidadCuotas = 0,
                Estado = EstadoCredito.PendienteConfiguracion,
                FechaSolicitud = DateTime.UtcNow
            };

            return await CreateAsync(creditoVm);
        }

        public async Task<bool> UpdateAsync(CreditoViewModel viewModel)
        {
            try
            {
                var credito = await _context.Creditos.FirstOrDefaultAsync(c => c.Id == viewModel.Id && !c.IsDeleted);
                if (credito == null)
                    return false;

                var clienteActivo = await _context.Clientes
                    .AnyAsync(c => c.Id == credito.ClienteId && !c.IsDeleted);
                if (!clienteActivo)
                    return false;

                _mapper.Map(viewModel, credito);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar crédito: {Id}", viewModel.Id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var credito = await _context.Creditos
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .Include(c => c.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == id &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);

                if (credito == null)
                    return false;

                // Solo se puede eliminar si está en estado Solicitado y no tiene cuotas pagadas
                if (credito.Estado != EstadoCredito.Solicitado)
                    throw new InvalidOperationException("Solo se pueden eliminar créditos en estado Solicitado");

                if (credito.Cuotas.Any(c => c.Estado == EstadoCuota.Pagada))
                    throw new InvalidOperationException("No se puede eliminar un crédito con cuotas pagadas");

                credito.IsDeleted = true;
                foreach (var cuota in credito.Cuotas)
                    cuota.IsDeleted = true;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar crédito: {Id}", id);
                throw;
            }
        }

        #endregion

        #region Operaciones de Crédito

        public Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo)
        {
            try
            {
                // La tasa ya viene como decimal (ejemplo: 0.05 = 5%)
                var tasaDecimal = modelo.TasaInteresMensual;
                modelo.MontoCuota = _financialService.CalcularCuotaSistemaFrances(modelo.MontoSolicitado, tasaDecimal, modelo.CantidadCuotas);
                modelo.TotalAPagar = modelo.MontoCuota * modelo.CantidadCuotas;
                modelo.TotalIntereses = modelo.TotalAPagar - modelo.MontoSolicitado;
                modelo.CFTEA = _financialService.CalcularCFTEADesdeTasa(tasaDecimal);

                // Generar plan de pagos
                modelo.PlanPagos = GenerarPlanAmortizacionFrances(
                    modelo.MontoSolicitado,
                    tasaDecimal,
                    modelo.CantidadCuotas,
                    modelo.MontoCuota,
                    DateTime.UtcNow.AddMonths(1));

                return Task.FromResult(modelo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al simular crédito");
                throw;
            }
        }

        public async Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor)
        {
            try
            {
                var credito = await _context.Creditos
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .Include(c => c.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);

                if (credito == null)
                    return false;

                if (credito.Estado != EstadoCredito.Solicitado)
                    throw new InvalidOperationException("Solo se pueden aprobar créditos en estado Solicitado");

                credito.Estado = EstadoCredito.Aprobado;
                credito.FechaAprobacion = DateTime.UtcNow;
                credito.AprobadoPor = aprobadoPor;
                credito.MontoAprobado = credito.MontoSolicitado;
                credito.SaldoPendiente = credito.MontoAprobado; // Saldo disponible completo

                // CAMBIO IMPORTANTE: NO generamos cuotas aquí
                // Las cuotas se generan cuando el cliente hace una compra

                await _context.SaveChangesAsync();

                _logger.LogInformation("Línea de crédito {Id} aprobada por {Usuario}. Saldo disponible: ${Saldo}",
                    creditoId, aprobadoPor, credito.SaldoPendiente);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aprobar crédito: {Id}", creditoId);
                throw;
            }
        }

        public async Task<bool> RechazarCreditoAsync(int creditoId, string motivo)
        {
            try
            {
                var credito = await _context.Creditos
                    .Include(c => c.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);
                if (credito == null)
                    return false;

                credito.Estado = EstadoCredito.Rechazado;
                credito.Observaciones = $"Rechazado: {motivo}";
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar crédito: {Id}", creditoId);
                throw;
            }
        }

        public async Task<bool> CancelarCreditoAsync(int creditoId, string motivo)
        {
            try
            {
                var credito = await _context.Creditos
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .Include(c => c.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);

                if (credito == null)
                    return false;

                credito.Estado = EstadoCredito.Cancelado;
                credito.FechaFinalizacion = DateTime.UtcNow;
                credito.Observaciones = $"Cancelado: {motivo}";

                // Cancelar cuotas pendientes
                foreach (var cuota in credito.Cuotas.Where(c => c.Estado == EstadoCuota.Pendiente))
                {
                    cuota.Estado = EstadoCuota.Cancelada;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar crédito: {Id}", creditoId);
                throw;
            }
        }

        #endregion

        #region Operaciones de Cuotas

        public async Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId)
        {
            try
            {
                var cuotas = await _context.Cuotas
                    .AsNoTracking()
                    .Where(c => c.CreditoId == creditoId &&
                                !c.IsDeleted &&
                                c.Credito != null &&
                                !c.Credito.IsDeleted &&
                                c.Credito.Cliente != null &&
                                !c.Credito.Cliente.IsDeleted)
                    .OrderBy(c => c.NumeroCuota)
                    .ToListAsync();

                return _mapper.Map<List<CuotaViewModel>>(cuotas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cuotas del crédito: {CreditoId}", creditoId);
                throw;
            }
        }

        public async Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId)
        {
            try
            {
                var cuota = await _context.Cuotas
                    .AsNoTracking()
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == cuotaId &&
                                              !c.IsDeleted &&
                                              !c.Credito.IsDeleted &&
                                              !c.Credito.Cliente.IsDeleted);

                if (cuota == null)
                    return null;

                return _mapper.Map<CuotaViewModel>(cuota);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cuota por ID: {Id}", cuotaId);
                throw;
            }
        }

        public async Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago)
        {
            try
            {
                var medioPago = NormalizarMedioPago(pago.MedioPago);

                var cuota = await _context.Cuotas
                    .Include(c => c.Credito)
                    .FirstOrDefaultAsync(c => c.Id == pago.CuotaId &&
                                              !c.IsDeleted &&
                                              !c.Credito.IsDeleted);

                if (cuota == null)
                    return false;

                if (cuota.Estado == EstadoCuota.Pagada)
                    throw new InvalidOperationException("La cuota ya está pagada");

                var cajaActiva = await _cajaService.ObtenerAperturaActivaParaVentaAsync();
                if (cajaActiva == null)
                    throw new InvalidOperationException("Debe existir una caja abierta para registrar el pago.");

                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                try
                {
                    cuota.MontoPunitorio = CalcularPunitorioActualizado(cuota, DateTime.UtcNow);

                    // Recargo/descuento del medio de pago: concepto separado del valor de la
                    // cuota. No modifica MontoTotal ni MontoPagado (que saldan la cuota).
                    var (ajustePorcentaje, tipoPagoMedio) = await ObtenerAjusteMedioPagoAsync(medioPago);
                    var recargoMedioPago = CalcularRecargoMedioPago(pago.MontoPagado, ajustePorcentaje);

                    cuota.MontoPagado += pago.MontoPagado;
                    cuota.RecargoMedioPago += recargoMedioPago;
                    cuota.FechaPago = pago.FechaPago;
                    cuota.MedioPago = medioPago;
                    cuota.ComprobantePago = pago.ComprobantePago;

                    if (!string.IsNullOrWhiteSpace(pago.Observaciones))
                        cuota.Observaciones = pago.Observaciones;

                    var totalACobrar = cuota.MontoTotal + cuota.MontoPunitorio;

                    cuota.Estado = ResolverEstadoCuota(cuota.MontoPagado, totalACobrar);

                    await _context.SaveChangesAsync();

                    var movimientoCaja = await _cajaService.RegistrarMovimientoCuotaAsync(
                        cuota.Id,
                        cuota.Credito.Numero,
                        cuota.NumeroCuota,
                        pago.MontoPagado,
                        recargoMedioPago,
                        tipoPagoMedio,
                        medioPago,
                        _currentUserService.GetUsername());

                    if (movimientoCaja == null)
                        throw new InvalidOperationException("Debe existir una caja abierta para registrar el pago.");

                    await RecalcularSaldoCreditoAsync(cuota.CreditoId);
                    await RecalcularPuntajeClientePorPagoAsync(cuota.Credito.ClienteId);
                    await transaction.CommitAsync();

                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al pagar cuota: {CuotaId}", pago.CuotaId);
                throw;
            }
        }

        public async Task<PagoMultipleCuotasResult> PagarCuotasAsync(
            PagoMultipleCuotasRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.ClienteId <= 0)
                throw new InvalidOperationException("El cliente es requerido.");

            var cuotaIdsRequest = request.CuotaIds ?? new List<int>();
            var cuotaIds = cuotaIdsRequest
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (!cuotaIds.Any())
                throw new InvalidOperationException("Debe seleccionar al menos una cuota.");

            if (cuotaIds.Count != cuotaIdsRequest.Count)
                throw new InvalidOperationException("La selección contiene cuotas duplicadas o inválidas.");

            var medioPago = NormalizarMedioPago(request.MedioPago);
            var observaciones = request.Observaciones?.Trim();
            var fechaPago = DateTime.UtcNow;
            var usuario = _currentUserService.GetUsername();
            var (ajustePorcentajeMedio, tipoPagoMedio) = await ObtenerAjusteMedioPagoAsync(medioPago);

            var cajaActiva = await _cajaService.ObtenerAperturaActivaParaVentaAsync();
            if (cajaActiva == null)
                throw new InvalidOperationException("Debe existir una caja abierta para registrar el pago múltiple.");

            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var cuotas = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(c => c.Cliente)
                    .Where(c => cuotaIds.Contains(c.Id) &&
                                !c.IsDeleted &&
                                !c.Credito.IsDeleted &&
                                c.Credito.Cliente != null &&
                                !c.Credito.Cliente.IsDeleted)
                    .ToListAsync(cancellationToken);

                var cuotasEncontradas = cuotas.Select(c => c.Id).ToHashSet();
                var cuotasFaltantes = cuotaIds
                    .Where(id => !cuotasEncontradas.Contains(id))
                    .ToList();

                if (cuotasFaltantes.Any())
                    throw new InvalidOperationException($"No se encontraron cuotas seleccionadas: {string.Join(", ", cuotasFaltantes)}.");

                var clienteIds = cuotas
                    .Select(c => c.Credito.ClienteId)
                    .Distinct()
                    .ToList();

                if (clienteIds.Count != 1 || clienteIds[0] != request.ClienteId)
                    throw new InvalidOperationException("Todas las cuotas seleccionadas deben pertenecer al cliente indicado.");

                var pagosPlanificados = new List<(Cuota Cuota, decimal Punitorio, decimal Subtotal, decimal Mora, decimal Total)>();

                foreach (var cuota in cuotas.OrderBy(c => c.CreditoId).ThenBy(c => c.NumeroCuota))
                {
                    if (cuota.Estado == EstadoCuota.Pagada)
                        throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} ya está pagada.");

                    if (cuota.Estado == EstadoCuota.Cancelada)
                        throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} está cancelada.");

                    var punitorio = CalcularPunitorioActualizado(cuota, fechaPago);
                    var saldo = CalcularSaldoPendienteCuota(cuota, punitorio);

                    if (saldo <= 0)
                        throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} no tiene saldo pendiente.");

                    var mora = Math.Min(punitorio > 0 ? punitorio : 0m, saldo);
                    var subtotal = saldo - mora;

                    pagosPlanificados.Add((cuota, punitorio, subtotal, mora, saldo));
                }

                foreach (var pago in pagosPlanificados)
                {
                    pago.Cuota.MontoPunitorio = pago.Punitorio;
                    pago.Cuota.MontoPagado += pago.Total;
                    pago.Cuota.RecargoMedioPago += CalcularRecargoMedioPago(pago.Total, ajustePorcentajeMedio);
                    pago.Cuota.FechaPago = fechaPago;
                    pago.Cuota.MedioPago = medioPago;

                    if (!string.IsNullOrWhiteSpace(observaciones))
                    {
                        pago.Cuota.Observaciones = CombinarObservacionesCuota(
                            pago.Cuota.Observaciones,
                            observaciones);
                    }

                    pago.Cuota.Estado = ResolverEstadoCuota(
                        pago.Cuota.MontoPagado,
                        pago.Cuota.MontoTotal + pago.Cuota.MontoPunitorio);
                }

                await _context.SaveChangesAsync(cancellationToken);

                foreach (var pago in pagosPlanificados)
                {
                    var movimientoCaja = await _cajaService.RegistrarMovimientoCuotaAsync(
                        pago.Cuota.Id,
                        pago.Cuota.Credito.Numero,
                        pago.Cuota.NumeroCuota,
                        pago.Total,
                        CalcularRecargoMedioPago(pago.Total, ajustePorcentajeMedio),
                        tipoPagoMedio,
                        medioPago,
                        usuario);

                    if (movimientoCaja == null)
                        throw new InvalidOperationException("Debe existir una caja abierta para registrar el pago múltiple.");
                }

                var creditoIds = pagosPlanificados
                    .Select(p => p.Cuota.CreditoId)
                    .Distinct()
                    .ToList();

                foreach (var creditoId in creditoIds)
                {
                    await RecalcularSaldoCreditoAsync(creditoId);
                }

                await RecalcularPuntajeClientePorPagoAsync(request.ClienteId, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                var result = new PagoMultipleCuotasResult
                {
                    ClienteId = request.ClienteId,
                    CuotaIds = pagosPlanificados.Select(p => p.Cuota.Id).ToList(),
                    CreditoIds = creditoIds,
                    CantidadCuotas = pagosPlanificados.Count,
                    CantidadCreditos = creditoIds.Count,
                    Subtotal = pagosPlanificados.Sum(p => p.Subtotal),
                    MoraTotal = pagosPlanificados.Sum(p => p.Mora),
                    TotalPagado = pagosPlanificados.Sum(p => p.Total),
                    FechaPago = fechaPago,
                    Cuotas = pagosPlanificados
                        .Select(p => new PagoMultipleCuotaResult
                        {
                            CuotaId = p.Cuota.Id,
                            CreditoId = p.Cuota.CreditoId,
                            CreditoNumero = p.Cuota.Credito.Numero,
                            NumeroCuota = p.Cuota.NumeroCuota,
                            Subtotal = p.Subtotal,
                            Mora = p.Mora,
                            TotalPagado = p.Total,
                            Estado = p.Cuota.Estado.ToString()
                        })
                        .ToList()
                };

                _logger.LogInformation(
                    "Pago múltiple registrado para cliente {ClienteId}: {CantidadCuotas} cuotas, {CantidadCreditos} créditos, total {Total:N2}",
                    request.ClienteId,
                    result.CantidadCuotas,
                    result.CantidadCreditos,
                    result.TotalPagado);

                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    ex,
                    "Conflicto de concurrencia al registrar pago múltiple para cliente {ClienteId}. Cuotas: {CuotaIds}",
                    request.ClienteId,
                    string.Join(", ", cuotaIds));
                throw new InvalidOperationException("Una o más cuotas fueron modificadas por otro usuario. Recargá la cartera e intentá nuevamente.", ex);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(
                    ex,
                    "Error al registrar pago múltiple para cliente {ClienteId}. Cuotas: {CuotaIds}",
                    request.ClienteId,
                    string.Join(", ", cuotaIds));
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago)
        {
            try
            {
                // Al adelantar, se paga la ÚLTIMA cuota pendiente (reduce el plazo)
                var ultimaCuotaPendiente = await _context.Cuotas
                    .Include(c => c.Credito)
                    .Where(c => c.CreditoId == pago.CreditoId &&
                               !c.IsDeleted &&
                               !c.Credito.IsDeleted &&
                               (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Parcial))
                    .OrderByDescending(c => c.NumeroCuota)
                    .FirstOrDefaultAsync();

                if (ultimaCuotaPendiente == null)
                {
                    _logger.LogWarning("No hay cuotas pendientes para adelantar en crédito {CreditoId}", pago.CreditoId);
                    return false;
                }

                // Forzar el pago a la última cuota
                pago.CuotaId = ultimaCuotaPendiente.Id;
                pago.NumeroCuota = ultimaCuotaPendiente.NumeroCuota;

                var ahora = DateTime.UtcNow;

                var (ajusteAdelanto, tipoPagoAdelanto) = await ObtenerAjusteMedioPagoAsync(pago.MedioPago ?? string.Empty);
                var recargoAdelanto = CalcularRecargoMedioPago(pago.MontoPagado, ajusteAdelanto);

                // En adelanto no hay punitorio (se paga antes de vencer)
                ultimaCuotaPendiente.MontoPagado += pago.MontoPagado;
                ultimaCuotaPendiente.RecargoMedioPago += recargoAdelanto;
                ultimaCuotaPendiente.FechaPago = pago.FechaPago;
                ultimaCuotaPendiente.MedioPago = pago.MedioPago;
                ultimaCuotaPendiente.ComprobantePago = pago.ComprobantePago;
                
                var observacionAdelanto = $"[ADELANTO] Cuota adelantada el {ahora:dd/MM/yyyy}";
                ultimaCuotaPendiente.Observaciones = string.IsNullOrWhiteSpace(pago.Observaciones)
                    ? observacionAdelanto
                    : $"{observacionAdelanto}. {pago.Observaciones}";

                ultimaCuotaPendiente.Estado = ResolverEstadoCuota(
                    ultimaCuotaPendiente.MontoPagado,
                    ultimaCuotaPendiente.MontoTotal);

                await _context.SaveChangesAsync();

                // Registrar movimiento de caja
                await _cajaService.RegistrarMovimientoCuotaAsync(
                    ultimaCuotaPendiente.Id,
                    ultimaCuotaPendiente.Credito.Numero,
                    ultimaCuotaPendiente.NumeroCuota,
                    pago.MontoPagado,
                    recargoAdelanto,
                    tipoPagoAdelanto,
                    pago.MedioPago ?? string.Empty,
                    _currentUserService.GetUsername());

                await RecalcularSaldoCreditoAsync(ultimaCuotaPendiente.CreditoId);

                _logger.LogInformation(
                    "Cuota #{NumeroCuota} adelantada en crédito {CreditoId}. Monto: {Monto:C2}",
                    ultimaCuotaPendiente.NumeroCuota, pago.CreditoId, pago.MontoPagado);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al adelantar cuota en crédito: {CreditoId}", pago.CreditoId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId)
        {
            try
            {
                var cuota = await _context.Cuotas
                    .Include(c => c.Credito)
                    .Where(c => c.CreditoId == creditoId &&
                               !c.IsDeleted &&
                               !c.Credito.IsDeleted &&
                               (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Vencida || c.Estado == EstadoCuota.Parcial))
                    .OrderBy(c => c.NumeroCuota)
                    .FirstOrDefaultAsync();

                return cuota == null ? null : _mapper.Map<CuotaViewModel>(cuota);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener primera cuota pendiente para crédito: {CreditoId}", creditoId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId)
        {
            try
            {
                var cuota = await _context.Cuotas
                    .Include(c => c.Credito)
                    .Where(c => c.CreditoId == creditoId &&
                               !c.IsDeleted &&
                               !c.Credito.IsDeleted &&
                               (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Parcial))
                    .OrderByDescending(c => c.NumeroCuota)
                    .FirstOrDefaultAsync();

                return cuota == null ? null : _mapper.Map<CuotaViewModel>(cuota);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener última cuota pendiente para crédito: {CreditoId}", creditoId);
                throw;
            }
        }

        public async Task<List<CuotaViewModel>> GetCuotasVencidasAsync()
        {
            try
            {
                var cuotas = await _context.Cuotas
                    .AsNoTracking()
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr.Cliente)
                    .Where(c => !c.IsDeleted &&
                               !c.Credito.IsDeleted &&
                               !c.Credito.Cliente.IsDeleted &&
                               c.FechaVencimiento < DateTime.UtcNow &&
                               (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Parcial || c.Estado == EstadoCuota.Vencida))
                    .OrderBy(c => c.FechaVencimiento)
                    .ToListAsync();

                return _mapper.Map<List<CuotaViewModel>>(cuotas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cuotas vencidas");
                throw;
            }
        }

        public async Task ActualizarEstadoCuotasAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                await _context.Cuotas
                    .Where(c => !c.IsDeleted &&
                               c.FechaVencimiento < now &&
                               c.Estado == EstadoCuota.Pendiente)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Estado, EstadoCuota.Vencida)
                        .SetProperty(c => c.UpdatedAt, now));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar estado de cuotas");
                throw;
            }
        }

        #endregion

        #region Cálculos Financieros

        public async Task<bool> RecalcularSaldoCreditoAsync(int creditoId)
        {
            try
            {
                var credito = await _context.Creditos
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .Include(c => c.Cliente)
                    .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                              !c.IsDeleted &&
                                              c.Cliente != null &&
                                              !c.Cliente.IsDeleted);

                if (credito == null)
                    return false;

                // Calcular saldo pendiente de capital para liberar cupo en función de amortización real.
                credito.SaldoPendiente = credito.Cuotas
                    .Where(c => c.Estado != EstadoCuota.Cancelada)
                    .Sum(CalcularCapitalPendienteCuota);

                // Verificar si todas las cuotas están pagadas
                if (credito.Cuotas.All(c => c.Estado == EstadoCuota.Pagada || c.Estado == EstadoCuota.Cancelada))
                {
                    credito.Estado = EstadoCredito.Finalizado;
                    credito.FechaFinalizacion = DateTime.UtcNow;
                }
                else if (credito.Estado == EstadoCredito.Aprobado && credito.Cuotas.Any(c => c.Estado == EstadoCuota.Pagada))
                {
                    credito.Estado = EstadoCredito.Activo;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al recalcular saldo del crédito: {CreditoId}", creditoId);
                throw;
            }
        }

        /// <summary>
        /// Recalcula PuntajeCliente reusando ClienteScoringService y audita el cambio en
        /// ClientePuntajeHistorial solo si el puntaje efectivamente cambió.
        /// </summary>
        private async Task RecalcularPuntajeClientePorPagoAsync(int clienteId, CancellationToken cancellationToken = default)
        {
            await _clienteScoringService.RecalcularYAuditarAsync(
                clienteId,
                origen: "RecalculoAutomaticoPago",
                observacion: "Recalculo automático por pago de cuota",
                registradoPor: _currentUserService.GetUsername(),
                ct: cancellationToken);
        }

        #endregion

        #region Métodos Privados

        internal static List<CuotaSimuladaViewModel> GenerarPlanAmortizacionFrances(
            decimal monto,
            decimal tasa,
            int cantidadCuotas,
            decimal montoCuota,
            DateTime fechaInicio)
        {
            var plan = new List<CuotaSimuladaViewModel>();
            var fechaCuota = fechaInicio;
            var saldoCapital = monto;

            for (int i = 1; i <= cantidadCuotas; i++)
            {
                var interes = saldoCapital * tasa;
                var capital = montoCuota - interes;
                saldoCapital -= capital;

                plan.Add(new CuotaSimuladaViewModel
                {
                    NumeroCuota = i,
                    FechaVencimiento = fechaCuota,
                    MontoCapital = Math.Round(capital, 2),
                    MontoInteres = Math.Round(interes, 2),
                    MontoTotal = Math.Round(montoCuota, 2),
                    SaldoCapital = Math.Round(Math.Max(0, saldoCapital), 2)
                });

                fechaCuota = fechaCuota.AddMonths(1);
            }

            return plan;
        }

        internal static EstadoCuota ResolverEstadoCuota(decimal montoPagado, decimal totalACobrar)
        {
            if (montoPagado >= totalACobrar)
                return EstadoCuota.Pagada;

            if (montoPagado > 0)
                return EstadoCuota.Parcial;

            return EstadoCuota.Pendiente;
        }

        private static string NormalizarMedioPago(string? medioPago)
        {
            var valor = medioPago?.Trim();
            if (string.IsNullOrWhiteSpace(valor))
                throw new InvalidOperationException("El medio de pago es requerido.");

            if (MediosPagoPermitidos.TryGetValue(valor, out var medioPagoPermitido))
                return medioPagoPermitido;

            throw new InvalidOperationException(
                $"Medio de pago inválido. Valores permitidos: {string.Join(", ", MediosPagoPermitidos.Values)}.");
        }

        private async Task CargarProductosAsociadosAsync(IReadOnlyCollection<CreditoViewModel> creditos)
        {
            if (creditos.Count == 0)
                return;

            var creditoIds = creditos
                .Select(c => c.Id)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (creditoIds.Count == 0)
                return;

            var detalles = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted &&
                            v.CreditoId.HasValue &&
                            creditoIds.Contains(v.CreditoId.Value))
                .SelectMany(
                    v => v.Detalles.Where(d => !d.IsDeleted),
                    (venta, detalle) => new
                    {
                        CreditoId = venta.CreditoId!.Value,
                        detalle.ProductoId,
                        ProductoNombre = detalle.Producto != null ? detalle.Producto.Nombre : null,
                        ProductoCodigo = detalle.Producto != null ? detalle.Producto.Codigo : null,
                        detalle.Cantidad,
                        Total = detalle.SubtotalFinal != 0 ? detalle.SubtotalFinal : detalle.Subtotal
                    })
                .ToListAsync();

            var productosPorCredito = detalles
                .GroupBy(d => d.CreditoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => grupo
                        .GroupBy(d => new
                        {
                            d.ProductoId,
                            d.ProductoNombre,
                            d.ProductoCodigo
                        })
                        .Select(producto => new CreditoProductoAsociadoViewModel
                        {
                            ProductoId = producto.Key.ProductoId,
                            ProductoNombre = string.IsNullOrWhiteSpace(producto.Key.ProductoNombre)
                                ? $"Producto #{producto.Key.ProductoId}"
                                : producto.Key.ProductoNombre,
                            ProductoCodigo = producto.Key.ProductoCodigo,
                            Cantidad = producto.Sum(x => x.Cantidad),
                            Total = producto.Sum(x => x.Total)
                        })
                        .OrderBy(p => p.ProductoNombre)
                        .ToList());

            foreach (var credito in creditos)
            {
                credito.ProductosAsociados = productosPorCredito.TryGetValue(credito.Id, out var productos)
                    ? productos
                    : new List<CreditoProductoAsociadoViewModel>();
            }
        }

        private static decimal CalcularPunitorioActualizado(Cuota cuota, DateTime ahora)
        {
            if (ahora > cuota.FechaVencimiento && cuota.Estado != EstadoCuota.Pagada)
            {
                var diasAtraso = (ahora - cuota.FechaVencimiento).Days;
                return cuota.MontoTotal * (cuota.Credito.TasaInteres / 100m / 12m) * (diasAtraso / 30m);
            }

            return cuota.MontoPunitorio;
        }

        private static decimal CalcularSaldoPendienteCuota(Cuota cuota, decimal punitorio)
        {
            return cuota.MontoTotal + punitorio - cuota.MontoPagado;
        }

        private static string CombinarObservacionesCuota(string? observacionesActuales, string observacionesNuevas)
        {
            var observaciones = string.IsNullOrWhiteSpace(observacionesActuales)
                ? observacionesNuevas
                : $"{observacionesActuales}{Environment.NewLine}{observacionesNuevas}";

            return observaciones.Length <= 500
                ? observaciones
                : observaciones[..500];
        }

        private static decimal CalcularCapitalPendienteCuota(Cuota cuota)
        {
            if (cuota.MontoCapital <= 0)
            {
                return 0m;
            }

            if (cuota.MontoTotal <= 0)
            {
                return cuota.Estado == EstadoCuota.Pagada ? 0m : cuota.MontoCapital;
            }

            var montoPagado = Math.Max(0m, cuota.MontoPagado);
            var proporcionCapital = cuota.MontoCapital / cuota.MontoTotal;
            var capitalPagadoEstimado = Math.Min(cuota.MontoCapital, montoPagado * proporcionCapital);
            capitalPagadoEstimado = Math.Round(capitalPagadoEstimado, 2, MidpointRounding.AwayFromZero);

            var capitalPendiente = cuota.MontoCapital - capitalPagadoEstimado;
            capitalPendiente = Math.Round(capitalPendiente, 2, MidpointRounding.AwayFromZero);

            return capitalPendiente > 0m ? capitalPendiente : 0m;
        }

        private async Task<string> GenerarNumeroCreditoAsync()
        {
            var ultimoCredito = await _context.Creditos
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            var numero = ultimoCredito != null ? ultimoCredito.Id + 1 : 1;
            return $"CRE-{DateTime.UtcNow:yyyyMM}-{numero:D6}";
        }

        private async Task ValidarMontoDentroDelDisponibleAsync(
            int clienteId,
            decimal montoSolicitado,
            CancellationToken cancellationToken = default)
        {
            if (montoSolicitado <= 0)
                return;

            var disponible = await _creditoDisponibleService.CalcularDisponibleAsync(clienteId, cancellationToken);
            if (montoSolicitado <= disponible.Disponible)
                return;

            throw new CreditoDisponibleException(
                $"Excede el crédito disponible por puntaje. Disponible: {disponible.Disponible:C2}. Ajuste el monto, cambie método de pago o actualice puntaje/límites.");
        }

        #endregion

        #region Configuración de crédito

        public async Task ConfigurarCreditoAsync(ConfiguracionCreditoComando cmd)
        {
            var credito = await _context.Creditos
                .FirstOrDefaultAsync(c => c.Id == cmd.CreditoId && !c.IsDeleted);

            if (credito == null)
                throw new InvalidOperationException($"Crédito {cmd.CreditoId} no encontrado.");

            credito.CantidadCuotas            = cmd.CantidadCuotas;
            credito.TasaInteres               = cmd.TasaMensual;
            credito.FechaPrimeraCuota         = cmd.FechaPrimeraCuota;
            credito.MontoAprobado             = Math.Max(0, cmd.Monto - cmd.Anticipo);
            credito.MontoSolicitado           = credito.MontoAprobado;
            credito.SaldoPendiente            = credito.MontoAprobado;
            credito.Estado                    = EstadoCredito.Configurado;
            credito.MetodoCalculoAplicado     = cmd.MetodoCalculo;
            credito.FuenteConfiguracionAplicada = cmd.FuenteConfiguracion;
            credito.GastosAdministrativos     = cmd.GastosAdministrativos;
            credito.TasaInteresAplicada       = cmd.TasaMensual;
            credito.CuotasMinimasPermitidas       = cmd.CuotasMinPermitidas;
            credito.CuotasMaximasPermitidas       = cmd.CuotasMaxPermitidas;
            credito.FuenteRestriccionCuotasSnap   = cmd.FuenteRestriccionCuotasSnap;
            credito.ProductoIdRestrictivoSnap     = cmd.ProductoIdRestrictivoSnap;
            credito.MaxCuotasBaseSnap             = cmd.MaxCuotasBaseSnap;

            if (cmd.PerfilCreditoAplicadoId.HasValue)
            {
                credito.PerfilCreditoAplicadoId     = cmd.PerfilCreditoAplicadoId;
                credito.PerfilCreditoAplicadoNombre = cmd.PerfilCreditoAplicadoNombre;
            }

            credito.Observaciones = BuildObservaciones(credito.Observaciones, cmd);

            if (cmd.VentaId.HasValue)
            {
                var venta = await _context.Ventas.FindAsync(cmd.VentaId.Value);
                if (venta != null)
                {
                    if (!venta.FechaConfiguracionCredito.HasValue)
                        venta.FechaConfiguracionCredito = DateTime.UtcNow;

                    if (venta.Estado == EstadoVenta.PendienteFinanciacion)
                    {
                        venta.Estado = EstadoVenta.Presupuesto;
                        _logger.LogInformation(
                            "Venta {VentaId} cambiada de PendienteFinanciacion a Presupuesto",
                            cmd.VentaId.Value);
                    }
                }

                var contratoExistente = await _context.ContratosVentaCredito
                    .FirstOrDefaultAsync(c => c.VentaId == cmd.VentaId.Value);
                if (contratoExistente != null)
                {
                    contratoExistente.IsDeleted = true;
                    _logger.LogWarning(
                        "Contrato {NumeroContrato} invalidado por re-configuración del crédito {CreditoId} (venta {VentaId})",
                        contratoExistente.NumeroContrato, cmd.CreditoId, cmd.VentaId.Value);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Crédito {CreditoId} configurado: Método={Metodo}, Fuente={Fuente}, " +
                "Tasa={Tasa:F4}%, Gastos={Gastos:C}, Cuotas=[{Min}-{Max}], Perfil={PerfilId}",
                cmd.CreditoId, cmd.MetodoCalculo, cmd.FuenteConfiguracion,
                cmd.TasaMensual, cmd.GastosAdministrativos,
                cmd.CuotasMinPermitidas, cmd.CuotasMaxPermitidas,
                cmd.PerfilCreditoAplicadoId?.ToString() ?? "N/A");
        }

        private static string? BuildObservaciones(string? observacionesActuales, ConfiguracionCreditoComando cmd)
        {
            var partes = new List<string>();

            if (observacionesActuales != null)
                partes.Add(observacionesActuales);

            if (cmd.GastosAdministrativos > 0)
                partes.Add($"Gastos administrativos declarados: ${cmd.GastosAdministrativos:N2}");

            var fuenteTexto = cmd.FuenteConfiguracion switch
            {
                FuenteConfiguracionCredito.PorCliente => "Configuración del Cliente",
                FuenteConfiguracionCredito.Manual     => "Configuración Manual",
                _                                     => "Configuración Global"
            };

            var metodoTexto = cmd.MetodoCalculo switch
            {
                MetodoCalculoCredito.AutomaticoPorCliente => "Automático (Por Cliente)",
                MetodoCalculoCredito.UsarPerfil           => $"Perfil: {cmd.PerfilCreditoAplicadoNombre ?? "N/A"}",
                MetodoCalculoCredito.UsarCliente          => "Cliente Personalizado",
                MetodoCalculoCredito.Global               => "Global",
                MetodoCalculoCredito.Manual               => "Manual",
                _                                         => "Desconocido"
            };

            partes.Add($"[{metodoTexto} | {fuenteTexto}]");

            return string.Join(" | ", partes);
        }

        #endregion
    }
}
