using AutoMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Data.Common;
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

        /// <summary>
        /// Única tolerancia admitida al comparar el importe enviado por el cliente contra el
        /// saldo calculado por el servidor. Cubre exclusivamente el redondeo a dos decimales:
        /// no habilita diferencias de importe.
        /// </summary>
        private const decimal ToleranciaImportePago = 0.01m;

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
        private readonly IRelojComercial _reloj;
        private readonly IPunitorioService _punitorioService;

        public CreditoService(
            AppDbContext context,
            IMapper mapper,
            ILogger<CreditoService> logger,
            IFinancialCalculationService financialService,
            ICajaService cajaService,
            ICreditoDisponibleService creditoDisponibleService,
            ICurrentUserService currentUserService,
            IClienteScoringService? clienteScoringService = null,
            IConfiguracionPagoService? configuracionPagoService = null,
            IRelojComercial? reloj = null,
            IPunitorioService? punitorioService = null)
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
            // Fuente temporal única. En DI llega RelojComercial; el fallback al reloj del sistema
            // conserva la compatibilidad de los constructores de tests que no lo inyectan.
            _reloj = reloj ?? RelojComercial.Sistema;
            // PUN-ML6: fuente única del punitorio aplicado pendiente a cobrar. Mismo AppDbContext que
            // este servicio (alcance de request): comparte transacción sin round-trips adicionales.
            // El fallback solo evita romper tests preexistentes que no lo inyectan y no ejercitan
            // punitorio (cuota siempre no vencida): en ese camino nunca se invoca.
            _punitorioService = punitorioService ?? new PunitorioService(
                context, new PunitorioCalculator(), _reloj, currentUserService, NullLogger<PunitorioService>.Instance);
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
                    {
                        // Vencida = anterior a la fecha comercial actual (Argentina). Comparar contra
                        // el inicio del día comercial y no contra UtcNow evita marcar como vencida una
                        // cuota que vence hoy cuando el instante UTC ya cruzó de día (21:00 en −03:00).
                        var inicioHoy = _reloj.InicioDiaComercial;
                        query = query.Where(c => c.Cuotas.Any(cu =>
                            !cu.IsDeleted &&
                            (cu.Estado == EstadoCuota.Vencida ||
                             (cu.Estado == EstadoCuota.Pendiente && cu.FechaVencimiento < inicioHoy))));
                    }
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
                modelo.CfteaPresentacion = ResolverCfteaPresentacion(modelo);

                return modelo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener crédito por ID: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Deriva el CFTEA para Details desde saldo financiado + total financiado real
        /// (cuotas persistidas si existen, si no <see cref="Credito.TotalAPagar"/>) —
        /// nunca desde <see cref="Credito.TasaInteres"/> (CalcularCFTEADesdeTasa asume
        /// interés compuesto mensual, no el modelo de recargo total). Null cuando no hay
        /// datos suficientes (p. ej. crédito configurado pero aún no confirmado, sin
        /// cuotas ni total persistido).
        /// </summary>
        private decimal? ResolverCfteaPresentacion(CreditoViewModel credito)
        {
            var saldoFinanciado = credito.MontoAprobado;
            if (saldoFinanciado <= 0)
                return null;

            if (credito.Cuotas is { Count: > 0 } cuotas)
            {
                var totalDesdeCuotas = cuotas.Sum(c => c.MontoTotal);
                return _financialService.CalcularCFTEA(totalDesdeCuotas, saldoFinanciado, cuotas.Count);
            }

            if (credito.CantidadCuotas > 0 && credito.TotalAPagar > 0)
                return _financialService.CalcularCFTEA(credito.TotalAPagar, saldoFinanciado, credito.CantidadCuotas);

            return null;
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

        public async Task<PagoCuotaContextoResultado?> ObtenerContextoPagoCuotaAsync(
            int cuotaId,
            CancellationToken cancellationToken = default)
        {
            if (cuotaId <= 0)
                return null;

            var cuota = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Credito)
                    .ThenInclude(c => c.Cliente)
                .FirstOrDefaultAsync(c => c.Id == cuotaId &&
                                          !c.IsDeleted &&
                                          !c.Credito.IsDeleted &&
                                          !c.Credito.Cliente.IsDeleted,
                    cancellationToken);

            if (cuota is null)
                return null;

            PunitorioCuotaDetalleResultado detalle;
            try
            {
                detalle = await _punitorioService.ObtenerDetalleCuotaAsync(cuotaId, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }

            if (detalle.CreditoId != cuota.CreditoId)
                return null;

            var punitorioAplicadoPendiente = detalle.CalculoActual.PunitorioAplicadoPendienteReal;
            decimal? totalCobrable = punitorioAplicadoPendiente.HasValue
                ? RedondearImporte(detalle.CapitalPendiente + punitorioAplicadoPendiente.Value)
                : null;
            var clienteNombre = string.Join(' ', new[]
            {
                cuota.Credito.Cliente.Nombre,
                cuota.Credito.Cliente.Apellido
            }.Where(valor => !string.IsNullOrWhiteSpace(valor)));

            return new PagoCuotaContextoResultado(
                cuota.Id,
                cuota.CreditoId,
                cuota.NumeroCuota,
                cuota.Credito.Numero,
                clienteNombre,
                DateOnly.FromDateTime(cuota.FechaVencimiento),
                _reloj.HoyComercial,
                cuota.Estado,
                EstadoCuotaResolver.DiasAtrasoDerivado(
                    cuota.Estado, cuota.FechaVencimiento, _reloj.HoyComercial),
                detalle.CapitalPendiente,
                detalle.CalculoActual.ImporteCalculado,
                detalle.CalculoActual.Estado,
                detalle.CalculoActual.MotivoNoCalculo,
                punitorioAplicadoPendiente,
                totalCobrable,
                detalle.HistorialCompleto,
                detalle.MotivoHistorialIncompleto,
                detalle.CuotaRowVersionBase64);
        }

        public async Task<PagoCuotaPreviewResultado?> PrevisualizarPagoCuotaAsync(
            PagoCuotaIndividualComando comando,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);
            ValidarRowVersionEsperada(comando.CuotaRowVersionEsperada);

            var contexto = await ObtenerContextoPagoCuotaAsync(comando.CuotaId, cancellationToken);
            if (contexto is null)
                return null;

            var cuota = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Credito)
                .FirstOrDefaultAsync(c => c.Id == comando.CuotaId &&
                                          !c.IsDeleted &&
                                          !c.Credito.IsDeleted,
                    cancellationToken);
            if (cuota is null)
                return null;

            VerificarRowVersion(cuota.RowVersion, comando.CuotaRowVersionEsperada);

            if (!contexto.PunitorioAplicadoPendiente.HasValue)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "No puede determinarse el punitorio aplicado pendiente porque el historial es incompleto.");

            var medioPago = NormalizarMedioPago(comando.MedioPago);
            await ValidarMedioPagoHabilitadoAsync(medioPago);
            var cobro = await ResolverCobroCuotaAsync(
                cuota,
                contexto.PunitorioAplicadoPendiente.Value,
                comando.MontoIngresado,
                medioPago,
                ModoCobroCuota.PagoManual);

            var punitorioRestante = Math.Max(
                0m,
                contexto.PunitorioAplicadoPendiente.Value - cobro.AplicadoPunitorio);
            var capitalRestante = Math.Max(
                0m,
                contexto.CapitalPendiente - cobro.AplicadoCuota);
            var estadoEstimado = EstadoCuotaResolver.Resolver(
                cuota.Estado,
                cuota.FechaVencimiento,
                _reloj.HoyComercial,
                cuota.MontoPagado + cobro.AplicadoCuota,
                cuota.MontoTotal,
                punitorioRestante);

            return new PagoCuotaPreviewResultado(
                cuota.Id,
                cobro.MontoBase,
                cobro.AplicadoPunitorio,
                cobro.AplicadoCuota,
                cobro.Excedente,
                cobro.RecargoMedioPago,
                cobro.MontoBase + cobro.RecargoMedioPago,
                punitorioRestante,
                capitalRestante,
                estadoEstimado,
                _reloj.HoyComercial,
                Convert.ToBase64String(cuota.RowVersion));
        }

        public async Task<PagoCuotaResultado?> RegistrarPagoCuotaIndividualAsync(
            PagoCuotaIndividualComando comando,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);
            ValidarRowVersionEsperada(comando.CuotaRowVersionEsperada);

            // Solo se usa para resolver la relación autoritativa cuota→crédito. La confirmación
            // vuelve a cargar y recalcular todo dentro de la transacción serializable.
            var contexto = await ObtenerContextoPagoCuotaAsync(comando.CuotaId, cancellationToken);
            if (contexto is null)
                return null;

            var aplicado = await RegistrarPagoCuotaAsync(
                new PagarCuotaViewModel
                {
                    CreditoId = contexto.CreditoId,
                    CuotaId = comando.CuotaId,
                    MontoPagado = comando.MontoIngresado,
                    MedioPago = comando.MedioPago,
                    ComprobantePago = comando.Comprobante,
                    Observaciones = comando.Observaciones
                },
                ModoCobroCuota.PagoManual,
                comando.CuotaRowVersionEsperada,
                exigirPendienteAplicadoAutoritativo: true);

            if (aplicado is null)
                return null;

            return new PagoCuotaResultado(
                aplicado.CuotaId,
                aplicado.CreditoId,
                aplicado.NumeroCuota,
                aplicado.MontoBase,
                aplicado.AplicadoPunitorio,
                aplicado.AplicadoCuota,
                aplicado.RecargoMedioPago,
                aplicado.MontoBase + aplicado.RecargoMedioPago,
                aplicado.PunitorioRestante,
                aplicado.CapitalRestante,
                aplicado.Estado,
                aplicado.FechaComercial,
                aplicado.MovimientoCajaId,
                aplicado.PagoCuotaId,
                aplicado.MedioPago,
                aplicado.CuotaRowVersionBase64);
        }

        public async Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago)
        {
            return await RegistrarPagoCuotaAsync(pago) != null;
        }

        /// <inheritdoc/>
        public async Task<PagoCuotaContextoResultado?> ObtenerContextoAdelantoAsync(
            int creditoId,
            CancellationToken cancellationToken = default)
        {
            if (creditoId <= 0)
                return null;

            // Misma resolución de "última cuota pendiente" que usa la confirmación
            // (ResolverCuotaAdelantableAsync, cuotaIdSolicitada=0 nunca dispara el rechazo por
            // "no es la última pendiente"): read-only, sin abrir transacción.
            var adelantable = await ResolverCuotaAdelantableAsync(creditoId, 0);
            if (adelantable is null)
                return null;

            return await ObtenerContextoPagoCuotaAsync(adelantable.Id, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<PagoCuotaPreviewResultado?> PrevisualizarAdelantoAsync(
            AdelantoCuotaComando comando,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);
            ValidarRowVersionEsperada(comando.CuotaRowVersionEsperada);

            var contexto = await ObtenerContextoAdelantoAsync(comando.CreditoId, cancellationToken);
            if (contexto is null)
                return null;

            var cuota = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Credito)
                .FirstOrDefaultAsync(c => c.Id == contexto.CuotaId &&
                                          !c.IsDeleted &&
                                          !c.Credito.IsDeleted,
                    cancellationToken);
            if (cuota is null)
                return null;

            VerificarRowVersion(cuota.RowVersion, comando.CuotaRowVersionEsperada);

            if (!contexto.PunitorioAplicadoPendiente.HasValue)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "No puede determinarse el punitorio aplicado pendiente porque el historial es incompleto.");

            if (contexto.TotalCobrableActual is not > 0m)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota no tiene saldo pendiente.");

            var medioPago = NormalizarMedioPago(comando.MedioPago);
            await ValidarMedioPagoHabilitadoAsync(medioPago);

            // El adelanto no negocia importe: el "monto solicitado" que valida ResolverCobroCuotaAsync
            // lo fija el servidor (total autoritativo), nunca el navegador.
            var cobro = await ResolverCobroCuotaAsync(
                cuota,
                contexto.PunitorioAplicadoPendiente.Value,
                contexto.TotalCobrableActual.Value,
                medioPago,
                ModoCobroCuota.AdelantoUltimaCuota);

            var punitorioRestante = Math.Max(
                0m,
                contexto.PunitorioAplicadoPendiente.Value - cobro.AplicadoPunitorio);
            var capitalRestante = Math.Max(
                0m,
                contexto.CapitalPendiente - cobro.AplicadoCuota);
            var estadoEstimado = EstadoCuotaResolver.Resolver(
                cuota.Estado,
                cuota.FechaVencimiento,
                _reloj.HoyComercial,
                cuota.MontoPagado + cobro.AplicadoCuota,
                cuota.MontoTotal,
                punitorioRestante);

            return new PagoCuotaPreviewResultado(
                cuota.Id,
                cobro.MontoBase,
                cobro.AplicadoPunitorio,
                cobro.AplicadoCuota,
                cobro.Excedente,
                cobro.RecargoMedioPago,
                cobro.MontoBase + cobro.RecargoMedioPago,
                punitorioRestante,
                capitalRestante,
                estadoEstimado,
                _reloj.HoyComercial,
                Convert.ToBase64String(cuota.RowVersion));
        }

        /// <inheritdoc/>
        public async Task<PagoCuotaResultado?> RegistrarAdelantoAsync(
            AdelantoCuotaComando comando,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(comando);
            ValidarRowVersionEsperada(comando.CuotaRowVersionEsperada);

            // Sólo se usa para fijar cuál es la cuota adelantable y su total autoritativo "de
            // referencia". La confirmación vuelve a resolver y recalcular todo dentro de la
            // transacción serializable — este valor no es lo que finalmente se cobra si cambió.
            var contexto = await ObtenerContextoAdelantoAsync(comando.CreditoId, cancellationToken);
            if (contexto is null || contexto.TotalCobrableActual is not > 0m)
                return null;

            var aplicado = await RegistrarPagoCuotaAsync(
                new PagarCuotaViewModel
                {
                    CreditoId = comando.CreditoId,
                    CuotaId = contexto.CuotaId,
                    MontoPagado = contexto.TotalCobrableActual.Value,
                    MedioPago = comando.MedioPago,
                    ComprobantePago = comando.Comprobante,
                    Observaciones = comando.Observaciones
                },
                ModoCobroCuota.AdelantoUltimaCuota,
                comando.CuotaRowVersionEsperada,
                exigirPendienteAplicadoAutoritativo: true);

            if (aplicado is null)
                return null;

            return new PagoCuotaResultado(
                aplicado.CuotaId,
                aplicado.CreditoId,
                aplicado.NumeroCuota,
                aplicado.MontoBase,
                aplicado.AplicadoPunitorio,
                aplicado.AplicadoCuota,
                aplicado.RecargoMedioPago,
                aplicado.MontoBase + aplicado.RecargoMedioPago,
                aplicado.PunitorioRestante,
                aplicado.CapitalRestante,
                aplicado.Estado,
                aplicado.FechaComercial,
                aplicado.MovimientoCajaId,
                aplicado.PagoCuotaId,
                aplicado.MedioPago,
                aplicado.CuotaRowVersionBase64);
        }

        /// <summary>
        /// Camino canónico único de validación, cálculo y registro del cobro de una cuota.
        /// Lo usan tanto el pago normal (<see cref="PagarCuotaAsync"/>) como el cobro de la
        /// primera cuota al generar el crédito (<see cref="CobrarPrimeraCuotaAlGenerarAsync"/>).
        ///
        /// Reglas de autoridad: el servidor resuelve la cuota desde la relación con el crédito,
        /// recalcula punitorio, saldo pendiente, recargo/descuento del medio de pago, total y
        /// estado. Del cliente solo se acepta el importe a imputar, y únicamente si cae dentro
        /// de (0, saldo]; nunca se toman recargo, descuento, total, saldo ni estado enviados.
        /// </summary>
        /// <param name="pago">Datos del cobro. <c>MontoPagado</c> es la intención del operador.</param>
        /// <param name="modo">
        /// Determina cómo se resuelve la cuota y qué autoridad tiene <c>pago.MontoPagado</c>.
        /// Ver <see cref="ModoCobroCuota"/>.
        /// </param>
        /// <returns><c>null</c> si el crédito o la cuota no existen o la cuota no pertenece al crédito.</returns>
        private async Task<PagoCuotaAplicado?> RegistrarPagoCuotaAsync(
            PagarCuotaViewModel pago,
            ModoCobroCuota modo = ModoCobroCuota.PagoManual,
            byte[]? cuotaRowVersionEsperada = null,
            bool exigirPendienteAplicadoAutoritativo = false)
        {
            if (pago == null)
                throw new ArgumentNullException(nameof(pago));

            var medioPago = NormalizarMedioPago(pago.MedioPago);
            await ValidarMedioPagoHabilitadoAsync(medioPago);

            var cajaActiva = await _cajaService.ObtenerAperturaActivaParaVentaAsync();
            if (cajaActiva == null)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "Debe existir una caja abierta para registrar el pago.");

            // La cuota se lee DENTRO de la transacción serializable: es lo que impide que dos
            // envíos simultáneos del mismo cobro vean ambos la cuota con saldo.
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                // El adelanto no acepta la cuota que indique el cliente: la resuelve el servidor
                // aplicando la regla funcional (última cuota pendiente del plan).
                var cuota = modo == ModoCobroCuota.AdelantoUltimaCuota
                    ? await ResolverCuotaAdelantableAsync(pago.CreditoId, pago.CuotaId)
                    : await ObtenerCuotaDelCreditoAsync(pago.CreditoId, pago.CuotaId);

                if (cuota == null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _logger.LogWarning(
                        "Cobro rechazado: la cuota {CuotaId} no existe o no pertenece al crédito {CreditoId}.",
                        pago.CuotaId,
                        pago.CreditoId);
                    return null;
                }

                if (cuotaRowVersionEsperada is not null)
                    VerificarRowVersion(cuota.RowVersion, cuotaRowVersionEsperada);

                // PUN-ML6: el punitorio pendiente a cobrar viene EXCLUSIVAMENTE de una aplicación
                // autorizada previa (PunitorioAplicado), nunca de una fórmula recalculada acá. Sin
                // aplicación activa esto es 0 en los tres modos por igual — incluido el adelanto, que
                // así "no inventa punitorio" (nada que aplicar automáticamente) pero tampoco condona
                // uno ya aplicado por inconsistencia (se cobra igual que cualquier otro camino, según
                // la misma regla canónica).
                var infoPunitorio = await _punitorioService.ObtenerAplicacionActivaConProgresoAsync(cuota.Id);
                var punitorioPendiente = infoPunitorio?.MontoPendiente ?? 0m;

                if (exigirPendienteAplicadoAutoritativo)
                {
                    var detalle = await _punitorioService.ObtenerDetalleCuotaAsync(cuota.Id);
                    var pendienteReal = detalle.CalculoActual.PunitorioAplicadoPendienteReal;
                    if (!pendienteReal.HasValue)
                    {
                        throw new PagoCuotaRechazadoException(
                            MotivoRechazoPagoCuota.Conflicto,
                            "No puede determinarse el punitorio aplicado pendiente porque el historial es incompleto.");
                    }

                    if (Math.Abs(pendienteReal.Value - punitorioPendiente) > ToleranciaImportePago)
                    {
                        throw new PagoCuotaRechazadoException(
                            MotivoRechazoPagoCuota.Conflicto,
                            "El punitorio aplicado cambió desde la consulta. Recargá la cuota e intentá nuevamente.");
                    }

                    punitorioPendiente = pendienteReal.Value;
                }

                var cobro = await ResolverCobroCuotaAsync(cuota, punitorioPendiente, pago.MontoPagado, medioPago, modo);

                // Solo el componente de cuota mueve MontoPagado: es lo único que reduce capital y
                // libera cupo (CalcularCapitalPendienteCuota deriva de MontoPagado). El componente de
                // punitorio nunca toca este campo.
                cuota.MontoPagado += cobro.AplicadoCuota;
                cuota.RecargoMedioPago += cobro.RecargoMedioPago;
                cuota.MedioPago = medioPago;
                cuota.ComprobantePago = pago.ComprobantePago;

                var observaciones = modo == ModoCobroCuota.AdelantoUltimaCuota
                    ? ComponerObservacionAdelanto(pago.Observaciones)
                    : pago.Observaciones;

                if (!string.IsNullOrWhiteSpace(observaciones))
                    cuota.Observaciones = observaciones;

                var punitorioPendienteDespues = Math.Max(0m, punitorioPendiente - cobro.AplicadoPunitorio);

                // PUN-ML7 (corrección): única autoridad de estado — EstadoCuotaResolver.Resolver,
                // con contexto completo (estado/vencimiento/fecha comercial). Antes se usaba un
                // resolver de 3 args sin noción de vencimiento: si un pago se imputaba íntegramente
                // a punitorio (MontoPagado queda en 0) sobre una cuota ya vencida, ese resolver
                // devolvía Pendiente en lugar de Vencida, ocultando la mora real.
                cuota.Estado = EstadoCuotaResolver.Resolver(
                    cuota.Estado, cuota.FechaVencimiento, _reloj.HoyComercial,
                    cuota.MontoPagado, cuota.MontoTotal, punitorioPendienteDespues);

                // PUN-ML7: FechaPago es "fecha comercial en que la cuota quedó saldada" — nunca la
                // fecha que haya enviado el navegador (pago.FechaPago es un campo editable del
                // formulario, ignorado a propósito) ni se toca en un pago parcial o que imputa solo a
                // punitorio (Estado != Pagada acá).
                if (cuota.Estado == EstadoCuota.Pagada)
                    cuota.FechaPago = FechaComercialComoDateTime();

                await _context.SaveChangesAsync();

                var movimientoCaja = await _cajaService.RegistrarMovimientoCuotaAsync(
                    cuota.Id,
                    cuota.Credito.Numero,
                    cuota.NumeroCuota,
                    cobro.MontoBase,
                    cobro.RecargoMedioPago,
                    cobro.TipoPago,
                    medioPago,
                    _currentUserService.GetUsername());

                if (movimientoCaja == null)
                    throw new PagoCuotaRechazadoException(
                        MotivoRechazoPagoCuota.Conflicto,
                        "Debe existir una caja abierta para registrar el pago.");

                var aplicacionCobrada = cobro.AplicadoPunitorio > 0m ? infoPunitorio!.Aplicacion : null;

                var pagoCuota = new PagoCuota
                {
                    CuotaId = cuota.Id,
                    FechaPagoComercial = _reloj.HoyComercial,
                    ImporteTotal = cobro.MontoBase,
                    ImporteAplicadoCuota = cobro.AplicadoCuota,
                    ImporteAplicadoPunitorio = cobro.AplicadoPunitorio,
                    PunitorioAplicadoId = aplicacionCobrada?.Id,
                    MovimientoCajaId = movimientoCaja.Id,
                    MedioPago = medioPago,
                    Origen = OrigenPagoCuota.RegistradoPorSistema,
                    Estado = EstadoPagoCuota.Aplicado,
                    // Con la prioridad punitorio→cuota decidida (PUN-ML6) la composición de un pago
                    // nuevo siempre es evidencia exacta: ya no existe el caso ambiguo de PUN-ML2.
                    HistorialCompleto = true,
                    MotivoIncompleto = null
                };
                _context.PagosCuota.Add(pagoCuota);
                await _context.SaveChangesAsync();

                if (aplicacionCobrada != null)
                {
                    var montoPagadoAplicacion = infoPunitorio!.MontoPagado + cobro.AplicadoPunitorio;
                    aplicacionCobrada.Estado = PunitorioService.ResolverEstadoAplicado(
                        aplicacionCobrada.Importe, montoPagadoAplicacion);
                    await _context.SaveChangesAsync();
                }

                await RecalcularSaldoCreditoAsync(cuota.CreditoId);
                await RecalcularPuntajeClientePorPagoAsync(cuota.Credito.ClienteId);
                await transaction.CommitAsync();

                var capitalRestante = Math.Max(0m, cuota.MontoTotal - cuota.MontoPagado);

                return new PagoCuotaAplicado(
                    cuota.Id,
                    cuota.CreditoId,
                    cuota.NumeroCuota,
                    cobro.MontoBase,
                    cobro.AplicadoPunitorio,
                    cobro.AplicadoCuota,
                    cobro.RecargoMedioPago,
                    medioPago,
                    cuota.Estado,
                    punitorioPendienteDespues,
                    capitalRestante,
                    _reloj.HoyComercial,
                    movimientoCaja.Id,
                    pagoCuota.Id,
                    Convert.ToBase64String(cuota.RowVersion));
            }
            catch (PagoCuotaRechazadoException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Cobro de cuota {CuotaId} (crédito {CreditoId}) rechazado: {Motivo}",
                    pago.CuotaId,
                    pago.CreditoId,
                    ex.Message);
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    ex,
                    "Conflicto de concurrencia al cobrar la cuota {CuotaId} del crédito {CreditoId}.",
                    pago.CuotaId,
                    pago.CreditoId);
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota fue modificada por otro usuario. Recargá el crédito e intentá nuevamente.");
            }
            catch (Exception ex) when (EsConflictoTransitorioDeBase(ex))
            {
                // Dos cobros simultáneos sobre la misma cuota: la transacción serializable que
                // pierde es rechazo por concurrencia, no un error interno que deba filtrarse.
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    ex,
                    "Conflicto de concurrencia en base al cobrar la cuota {CuotaId} del crédito {CreditoId}.",
                    pago.CuotaId,
                    pago.CreditoId);
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "El cobro se cruzó con otra operación sobre la misma cuota. Recargá el crédito e intentá nuevamente.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(ex, "Error al pagar cuota: {CuotaId}", pago.CuotaId);
                throw;
            }
        }

        /// <summary>
        /// Reconoce el fallo que reporta el motor cuando la transacción serializable pierde
        /// frente a otra simultánea. EF lo envuelve en un <see cref="InvalidOperationException"/>,
        /// por eso hay que recorrer la cadena interna.
        /// </summary>
        private static bool EsConflictoTransitorioDeBase(Exception excepcion)
        {
            for (Exception? actual = excepcion; actual != null; actual = actual.InnerException)
            {
                // 1205: elegido víctima de un interbloqueo. 3960: conflicto de actualización
                // bajo aislamiento por instantáneas. Ambos son "reintentá", no error interno.
                if (actual is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number is 1205 or 3960))
                    return true;

                if (actual is DbException { IsTransient: true })
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resuelve la cuota exigiendo que pertenezca al crédito indicado. La pertenencia se
        /// valida en el propio predicado (<c>CreditoId</c>), no con dos consultas independientes.
        /// Devuelve <c>null</c> —sin distinguir el motivo— para no revelar la existencia de
        /// cuotas o créditos ajenos.
        /// </summary>
        private async Task<Cuota?> ObtenerCuotaDelCreditoAsync(int creditoId, int cuotaId)
        {
            if (creditoId <= 0 || cuotaId <= 0)
                return null;

            return await _context.Cuotas
                .Include(c => c.Credito)
                .FirstOrDefaultAsync(c => c.Id == cuotaId &&
                                          c.CreditoId == creditoId &&
                                          !c.IsDeleted &&
                                          !c.Credito.IsDeleted);
        }

        /// <summary>
        /// Resuelve, con autoridad exclusiva del servidor, cuál es la cuota que puede adelantarse:
        /// la última pendiente o parcial del plan, porque el adelanto reduce el plazo del crédito.
        /// </summary>
        /// <param name="cuotaIdSolicitada">
        /// Cuota que el cliente creyó estar adelantando. Cuando viene informada debe coincidir con
        /// la que resuelve el servidor: si la cartera cambió entre el GET y el POST se rechaza en
        /// lugar de cobrar silenciosamente una cuota distinta de la que vio el operador.
        /// </param>
        /// <returns><c>null</c> si no hay cuota adelantable o la indicada no pertenece al crédito.</returns>
        private async Task<Cuota?> ResolverCuotaAdelantableAsync(int creditoId, int cuotaIdSolicitada)
        {
            if (creditoId <= 0)
                return null;

            // PUN-ML9-E (fix): Vencida es un estado cobrable como cualquier otro (mismo criterio
            // que el gate de "Pagar" individual en Details_tw y que PlanificarPagoMultipleAsync,
            // que sólo rechaza Pagada/Cancelada) — omitirla acá dejaba una cuota recién vencida
            // (p.ej. por el efecto colateral de EstadoCuotaResolver al aplicar un punitorio)
            // invisible para "última cuota pendiente", saltando en silencio a una cuota anterior.
            var adelantable = await _context.Cuotas
                .Include(c => c.Credito)
                .Where(c => c.CreditoId == creditoId &&
                            !c.IsDeleted &&
                            !c.Credito.IsDeleted &&
                            (c.Estado == EstadoCuota.Pendiente ||
                             c.Estado == EstadoCuota.Vencida ||
                             c.Estado == EstadoCuota.Parcial))
                .OrderByDescending(c => c.NumeroCuota)
                .FirstOrDefaultAsync();

            if (adelantable == null)
                return null;

            if (cuotaIdSolicitada <= 0 || cuotaIdSolicitada == adelantable.Id)
                return adelantable;

            // La cuota indicada no es la adelantable. Si además no pertenece al crédito se
            // devuelve null (mismo tratamiento que el pago normal: no se revela su existencia).
            var solicitada = await ObtenerCuotaDelCreditoAsync(creditoId, cuotaIdSolicitada);
            if (solicitada == null)
                return null;

            throw new PagoCuotaRechazadoException(
                MotivoRechazoPagoCuota.Conflicto,
                $"Solo puede adelantarse la última cuota pendiente del plan (cuota #{adelantable.NumeroCuota}).");
        }

        /// <summary>
        /// Calcula, con autoridad exclusiva del servidor, el importe base a imputar y el
        /// recargo/descuento del medio de pago, rechazando toda combinación inconsistente.
        /// </summary>
        private async Task<CobroCuotaCalculado> ResolverCobroCuotaAsync(
            Cuota cuota,
            decimal punitorioPendiente,
            decimal montoSolicitado,
            string medioPago,
            ModoCobroCuota modo)
        {
            if (cuota.Estado == EstadoCuota.Pagada)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota ya está pagada");

            if (cuota.Estado == EstadoCuota.Cancelada)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota está cancelada y no admite cobros.");

            // PUN-ML6: el total server-side a cobrar es saldo de cuota + punitorio aplicado
            // pendiente — nunca Cuota.MontoPunitorio (legacy, ya no participa de ninguna decisión).
            var saldoCuota = RedondearImporte(CalcularSaldoPendienteCuota(cuota));
            var totalACobrar = RedondearImporte(saldoCuota + punitorioPendiente);

            if (totalACobrar <= 0m)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota no tiene saldo pendiente.");

            decimal montoBase;

            if (modo == ModoCobroCuota.CobroPrimeraCuota)
            {
                montoBase = totalACobrar;
            }
            else
            {
                var solicitado = RedondearImporte(montoSolicitado);

                if (solicitado <= 0m)
                    throw new PagoCuotaRechazadoException(
                        MotivoRechazoPagoCuota.SolicitudInvalida,
                        "El monto a pagar debe ser mayor a cero.");

                if (solicitado - totalACobrar > ToleranciaImportePago)
                    throw new PagoCuotaRechazadoException(
                        MotivoRechazoPagoCuota.SolicitudInvalida,
                        $"El monto a pagar no puede superar el saldo pendiente de la cuota ({totalACobrar:N2}).");

                // El adelanto cancela la cuota para reducir el plazo: no admite imputación
                // parcial. El pago normal sí, porque es una funcionalidad vigente del módulo.
                if (modo == ModoCobroCuota.AdelantoUltimaCuota &&
                    totalACobrar - solicitado > ToleranciaImportePago)
                    throw new PagoCuotaRechazadoException(
                        MotivoRechazoPagoCuota.SolicitudInvalida,
                        $"El adelanto cancela la cuota completa: el importe debe ser el saldo pendiente ({totalACobrar:N2}).");

                // Dentro de la tolerancia de redondeo se cobra el saldo exacto del servidor, para
                // que una diferencia decimal del cliente no deje la cuota impaga por centavos.
                montoBase = totalACobrar - solicitado <= ToleranciaImportePago ? totalACobrar : solicitado;
            }

            var (aplicadoPunitorio, aplicadoCuota, excedente) =
                DistribuirPago(montoBase, punitorioPendiente, saldoCuota);

            var (ajustePorcentaje, tipoPagoMedio) = await ObtenerAjusteMedioPagoAsync(medioPago);
            var recargoMedioPago = CalcularRecargoMedioPago(montoBase, ajustePorcentaje);

            return new CobroCuotaCalculado(
                montoBase,
                aplicadoPunitorio,
                aplicadoCuota,
                excedente,
                recargoMedioPago,
                tipoPagoMedio);
        }

        /// <summary>
        /// Distribuidor canónico de un pago entre punitorio aplicado pendiente y saldo de cuota
        /// (PUN-ML6, decisión de negocio congelada): prioridad estricta punitorio → cuota. Puro y
        /// reutilizado por los cuatro caminos de cobro (pago individual, múltiple, primera cuota,
        /// adelanto) — no se duplica esta lógica en ninguno.
        /// </summary>
        internal static (decimal AplicadoPunitorio, decimal AplicadoCuota, decimal Excedente) DistribuirPago(
            decimal importeDisponible, decimal punitorioPendiente, decimal saldoCuota)
        {
            var aplicadoPunitorio = Math.Min(importeDisponible, Math.Max(0m, punitorioPendiente));
            var remanente = importeDisponible - aplicadoPunitorio;
            var aplicadoCuota = Math.Min(remanente, Math.Max(0m, saldoCuota));
            var excedente = remanente - aplicadoCuota;
            return (aplicadoPunitorio, aplicadoCuota, excedente);
        }

        /// <summary>
        /// Verifica que el medio de pago esté habilitado. Si no existe configuración para el
        /// medio se mantiene el comportamiento vigente (ajuste 0%); solo se rechaza cuando la
        /// configuración existe y está desactivada.
        /// </summary>
        private async Task ValidarMedioPagoHabilitadoAsync(string medioPago)
        {
            if (!MediosPagoTipoPago.TryGetValue(medioPago, out var tipoPago))
                return;

            var activo = await _context.ConfiguracionesPago
                .AsNoTracking()
                .Where(c => c.TipoPago == tipoPago && !c.IsDeleted)
                .Select(c => (bool?)c.Activo)
                .FirstOrDefaultAsync();

            if (activo == false)
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.SolicitudInvalida,
                    $"El medio de pago {medioPago} no está habilitado.");
        }

        private static decimal RedondearImporte(decimal valor) =>
            Math.Round(valor, 2, MidpointRounding.AwayFromZero);

        private static void ValidarRowVersionEsperada(byte[]? rowVersion)
        {
            if (rowVersion is not { Length: > 0 })
            {
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.SolicitudInvalida,
                    "La versión de la cuota es inválida. Recargá la pantalla.");
            }
        }

        private static void VerificarRowVersion(byte[] actual, byte[] esperada)
        {
            ValidarRowVersionEsperada(esperada);
            if (actual is null || !actual.AsSpan().SequenceEqual(esperada))
            {
                throw new PagoCuotaRechazadoException(
                    MotivoRechazoPagoCuota.Conflicto,
                    "La cuota cambió desde que fue consultada. Recargá e intentá nuevamente.");
            }
        }

        /// <summary>
        /// Fecha comercial actual expresada como <see cref="DateTime"/> a medianoche, para
        /// persistir en <see cref="Cuota.FechaPago"/> (columna <see cref="DateTime"/>, no
        /// <see cref="DateOnly"/>). Único punto de conversión — evita repetir
        /// <c>_reloj.HoyComercial.ToDateTime(TimeOnly.MinValue)</c> en cada call site.
        /// </summary>
        private DateTime FechaComercialComoDateTime() => _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

        /// <summary>
        /// Marca de trazabilidad del adelanto sobre la cuota, respetando el límite de la columna.
        /// </summary>
        private static string ComponerObservacionAdelanto(string? observacionesOperador)
        {
            var etiqueta = $"[ADELANTO] Cuota adelantada el {DateTime.UtcNow:dd/MM/yyyy}";

            var observaciones = string.IsNullOrWhiteSpace(observacionesOperador)
                ? etiqueta
                : $"{etiqueta}. {observacionesOperador}";

            return observaciones.Length <= 500 ? observaciones : observaciones[..500];
        }

        /// <summary>
        /// Variantes del cobro de una cuota. Todas comparten validación, cálculo, transacción y
        /// registro en caja; solo cambian cómo se elige la cuota y qué autoridad tiene el importe
        /// enviado por el cliente.
        /// </summary>
        private enum ModoCobroCuota
        {
            /// <summary>Pago de una cuota indicada por el operador. Admite imputación parcial dentro de (0, saldo].</summary>
            PagoManual = 0,

            /// <summary>Cobro automático de la primera cuota al generar el crédito. Ignora el importe recibido.</summary>
            CobroPrimeraCuota = 1,

            /// <summary>Adelanto de la última cuota pendiente. Exige cancelar el saldo completo.</summary>
            AdelantoUltimaCuota = 2
        }

        /// <summary>
        /// Importe base, su distribución punitorio/cuota (PUN-ML6, <see cref="DistribuirPago"/>) y el
        /// ajuste del medio de pago, todo resuelto por el servidor.
        /// </summary>
        private sealed record CobroCuotaCalculado(
            decimal MontoBase,
            decimal AplicadoPunitorio,
            decimal AplicadoCuota,
            decimal Excedente,
            decimal RecargoMedioPago,
            TipoPago? TipoPago);

        /// <summary>Resultado de un cobro efectivamente aplicado y confirmado.</summary>
        private sealed record PagoCuotaAplicado(
            int CuotaId,
            int CreditoId,
            int NumeroCuota,
            decimal MontoBase,
            decimal AplicadoPunitorio,
            decimal AplicadoCuota,
            decimal RecargoMedioPago,
            string MedioPago,
            EstadoCuota Estado,
            decimal PunitorioRestante,
            decimal CapitalRestante,
            DateOnly FechaComercial,
            int MovimientoCajaId,
            int PagoCuotaId,
            string CuotaRowVersionBase64);

        /// <inheritdoc />
        public async Task<CobroPrimeraCuotaResultado> CobrarPrimeraCuotaAlGenerarAsync(
            int creditoId,
            string? medioPago = null,
            string? comprobante = null,
            string? observaciones = null)
        {
            var credito = await _context.Creditos
                .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == creditoId && !c.IsDeleted);

            if (credito == null)
                return CobroPrimeraCuotaResultado.NoAplica("El crédito no existe.");

            // F2 server-authoritative: el medio se toma del parámetro explícito o, si viene vacío,
            // de la decisión persistida en la configuración del crédito. Sin medio no hay cobro.
            var medioElegido = !string.IsNullOrWhiteSpace(medioPago)
                ? medioPago
                : credito.MedioPagoPrimeraCuota;

            if (string.IsNullOrWhiteSpace(medioElegido))
                return CobroPrimeraCuotaResultado.NoAplica("No se definió el medio de pago de la primera cuota.");

            var medioNormalizado = NormalizarMedioPago(medioElegido);

            var primeraCuota = credito.Cuotas
                .OrderBy(c => c.NumeroCuota)
                .FirstOrDefault();

            if (primeraCuota == null)
                return CobroPrimeraCuotaResultado.NoAplica("El crédito no tiene cuotas generadas.");

            if (primeraCuota.Estado != EstadoCuota.Pendiente)
                return CobroPrimeraCuotaResultado.NoAplica("La primera cuota no está pendiente de cobro.");

            // "Vence hoy" según la fecha comercial de Argentina (autoridad única). No usar
            // DateTime.Today (zona del proceso) ni comparar contra UtcNow: la cuota se almacena a
            // medianoche y el punitorio se calcula con la misma fecha comercial (coherencia total).
            if (DateOnly.FromDateTime(primeraCuota.FechaVencimiento) != _reloj.HoyComercial)
                return CobroPrimeraCuotaResultado.NoAplica("La primera cuota no vence hoy.");

            // Precondición del cobro automático: si la cuota ya no tiene saldo, no aplica. Chequeo
            // rápido y suficiente (una primera cuota recién generada nunca tiene punitorio aplicado);
            // el importe efectivo y la autoridad real los resuelve RegistrarPagoCuotaAsync.
            if (CalcularSaldoPendienteCuota(primeraCuota) <= 0m)
                return CobroPrimeraCuotaResultado.NoAplica("La primera cuota no tiene saldo por cobrar.");

            var pago = new PagarCuotaViewModel
            {
                CreditoId = credito.Id,
                CuotaId = primeraCuota.Id,
                NumeroCuota = primeraCuota.NumeroCuota,
                FechaPago = _reloj.AhoraUtc,
                MedioPago = medioNormalizado,
                ComprobantePago = comprobante,
                Observaciones = observaciones
            };

            // CobroPrimeraCuota: el servidor ignora cualquier importe de entrada y cobra el
            // saldo real de la cuota, aplicando las mismas validaciones que el pago manual.
            var aplicado = await RegistrarPagoCuotaAsync(pago, ModoCobroCuota.CobroPrimeraCuota);

            if (aplicado == null)
            {
                return new CobroPrimeraCuotaResultado
                {
                    Estado = EstadoCobroPrimeraCuota.Error,
                    CuotaId = primeraCuota.Id,
                    NumeroCuota = primeraCuota.NumeroCuota,
                    MedioPago = medioNormalizado,
                    Mensaje = "No se pudo registrar el cobro de la primera cuota."
                };
            }

            return new CobroPrimeraCuotaResultado
            {
                Estado = EstadoCobroPrimeraCuota.Cobrada,
                CuotaId = aplicado.CuotaId,
                NumeroCuota = aplicado.NumeroCuota,
                MontoBase = aplicado.MontoBase,
                RecargoMedioPago = aplicado.RecargoMedioPago,
                MedioPago = aplicado.MedioPago
            };
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

            var rowVersionsEsperadas = DecodificarRowVersionsPorCuota(request.RowVersionsPorCuota);

            var medioPago = NormalizarMedioPago(request.MedioPago);
            var observaciones = request.Observaciones?.Trim();
            var fechaPago = _reloj.AhoraUtc;
            var hoyComercial = _reloj.HoyComercial;
            var fechaPagoComercial = hoyComercial.ToDateTime(TimeOnly.MinValue);
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
                // Una sola pasada de lectura+validación (incluye RowVersion) ANTES de tocar nada:
                // si una sola cuota falla acá, no se mutó ninguna todavía (rollback total real).
                var pagosPlanificados = await PlanificarPagoMultipleAsync(
                    request.ClienteId, cuotaIds, rowVersionsEsperadas, cancellationToken);

                foreach (var pago in pagosPlanificados)
                {
                    // Solo el componente de cuota mueve MontoPagado (libera cupo/capital); el de
                    // punitorio nunca lo toca.
                    pago.Cuota.MontoPagado += pago.Subtotal;
                    pago.Cuota.RecargoMedioPago += CalcularRecargoMedioPago(pago.Total, ajustePorcentajeMedio);
                    pago.Cuota.MedioPago = medioPago;

                    if (!string.IsNullOrWhiteSpace(observaciones))
                    {
                        pago.Cuota.Observaciones = CombinarObservacionesCuota(
                            pago.Cuota.Observaciones,
                            observaciones);
                    }

                    var punitorioPendienteDespues = Math.Max(0m, (pago.InfoPunitorio?.MontoPendiente ?? 0m) - pago.Mora);

                    // PUN-ML7 (corrección): misma autoridad única que el pago individual —
                    // EstadoCuotaResolver.Resolver, nunca una regla propia de este método.
                    pago.Cuota.Estado = EstadoCuotaResolver.Resolver(
                        pago.Cuota.Estado, pago.Cuota.FechaVencimiento, hoyComercial,
                        pago.Cuota.MontoPagado, pago.Cuota.MontoTotal, punitorioPendienteDespues);

                    // PUN-ML7: misma regla que el pago individual — FechaPago solo se establece al
                    // quedar Pagada, con la fecha comercial del servidor.
                    if (pago.Cuota.Estado == EstadoCuota.Pagada)
                        pago.Cuota.FechaPago = fechaPagoComercial;
                }

                await _context.SaveChangesAsync(cancellationToken);

                var pagosCuotaPorCuotaId = new Dictionary<int, PagoCuota>();
                var recargoPorCuotaId = new Dictionary<int, decimal>();

                foreach (var pago in pagosPlanificados)
                {
                    var recargoCuota = CalcularRecargoMedioPago(pago.Total, ajustePorcentajeMedio);
                    recargoPorCuotaId[pago.Cuota.Id] = recargoCuota;

                    var movimientoCaja = await _cajaService.RegistrarMovimientoCuotaAsync(
                        pago.Cuota.Id,
                        pago.Cuota.Credito.Numero,
                        pago.Cuota.NumeroCuota,
                        pago.Total,
                        recargoCuota,
                        tipoPagoMedio,
                        medioPago,
                        usuario);

                    if (movimientoCaja == null)
                        throw new InvalidOperationException("Debe existir una caja abierta para registrar el pago múltiple.");

                    var aplicacionCobrada = pago.Mora > 0m ? pago.InfoPunitorio!.Aplicacion : null;

                    // PagarCuotasAsync siempre cancela el saldo completo de cada cuota (no admite
                    // imputación parcial): a diferencia del pago manual, acá la composición
                    // cuota/punitorio es siempre evidencia directa, nunca ambigua.
                    var pagoCuota = new PagoCuota
                    {
                        CuotaId = pago.Cuota.Id,
                        FechaPagoComercial = hoyComercial,
                        ImporteTotal = pago.Total,
                        ImporteAplicadoCuota = pago.Subtotal,
                        ImporteAplicadoPunitorio = pago.Mora,
                        PunitorioAplicadoId = aplicacionCobrada?.Id,
                        MovimientoCajaId = movimientoCaja.Id,
                        MedioPago = medioPago,
                        Origen = OrigenPagoCuota.RegistradoPorSistema,
                        Estado = EstadoPagoCuota.Aplicado,
                        HistorialCompleto = true,
                        MotivoIncompleto = null
                    };
                    _context.PagosCuota.Add(pagoCuota);
                    pagosCuotaPorCuotaId[pago.Cuota.Id] = pagoCuota;
                }

                await _context.SaveChangesAsync(cancellationToken);

                foreach (var pago in pagosPlanificados.Where(p => p.Mora > 0m))
                {
                    var aplicacion = pago.InfoPunitorio!.Aplicacion;
                    var montoPagadoAplicacion = pago.InfoPunitorio.MontoPagado + pago.Mora;
                    aplicacion.Estado = PunitorioService.ResolverEstadoAplicado(aplicacion.Importe, montoPagadoAplicacion);
                }

                if (pagosPlanificados.Any(p => p.Mora > 0m))
                    await _context.SaveChangesAsync(cancellationToken);

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

                var recargoTotal = recargoPorCuotaId.Values.Sum();

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
                    RecargoTotal = recargoTotal,
                    TotalCaja = pagosPlanificados.Sum(p => p.Total) + recargoTotal,
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
                            Estado = p.Cuota.Estado.ToString(),
                            PagoCuotaId = pagosCuotaPorCuotaId[p.Cuota.Id].Id,
                            RecargoMedioPago = recargoPorCuotaId[p.Cuota.Id],
                            TotalCaja = p.Total + recargoPorCuotaId[p.Cuota.Id]
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
            catch (PagoCuotaRechazadoException ex)
            {
                // RowVersion faltante/desactualizada (409) o dato inválido (400) de alguna de las
                // cuotas: nada se persistió todavía (el rechazo ocurre en PlanificarPagoMultipleAsync,
                // antes de la primera escritura), así que el rollback no revierte nada además de la
                // propia transacción vacía — cero pagos parciales, cero movimientos, cero cupo liberado.
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Pago múltiple rechazado para cliente {ClienteId}: {Motivo}",
                    request.ClienteId,
                    ex.Message);
                throw;
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
            catch (Exception ex) when (EsConflictoTransitorioDeBase(ex))
            {
                // Dos pagos múltiples simultáneos sobre alguna cuota compartida: la transacción
                // serializable que pierde es rechazo por concurrencia, no un error interno.
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    ex,
                    "Conflicto de concurrencia en base al registrar pago múltiple para cliente {ClienteId}. Cuotas: {CuotaIds}",
                    request.ClienteId,
                    string.Join(", ", cuotaIds));
                throw new InvalidOperationException("El pago se cruzó con otra operación sobre alguna de las cuotas. Recargá la cartera e intentá nuevamente.", ex);
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
        public async Task<PagoMultiplePreviewResultado> PrevisualizarPagoMultipleAsync(
            int clienteId,
            List<int> cuotaIds,
            string medioPago,
            CancellationToken cancellationToken = default)
        {
            if (clienteId <= 0)
                throw new InvalidOperationException("El cliente es requerido.");

            var cuotaIdsFiltrados = (cuotaIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (!cuotaIdsFiltrados.Any())
                throw new InvalidOperationException("Debe seleccionar al menos una cuota.");

            var medioNormalizado = NormalizarMedioPago(medioPago);
            await ValidarMedioPagoHabilitadoAsync(medioNormalizado);
            var (ajustePorcentajeMedio, _) = await ObtenerAjusteMedioPagoAsync(medioNormalizado);

            // Read-only: mismo cálculo que la confirmación (misma autoridad), sin RowVersion (el
            // preview no exige haber leído antes) y sin abrir transacción — no muta nada.
            var planificados = await PlanificarPagoMultipleAsync(
                clienteId, cuotaIdsFiltrados, rowVersionsEsperadas: null, cancellationToken);

            var cuotasPreview = planificados
                .Select(p =>
                {
                    var recargo = CalcularRecargoMedioPago(p.Total, ajustePorcentajeMedio);
                    return new PagoMultiplePreviewCuotaResultado(
                        p.Cuota.Id,
                        p.Cuota.CreditoId,
                        p.Cuota.Credito.Numero,
                        p.Cuota.NumeroCuota,
                        p.Subtotal,
                        p.Mora,
                        p.Total,
                        recargo,
                        p.Total + recargo,
                        Convert.ToBase64String(p.Cuota.RowVersion));
                })
                .ToList();

            return new PagoMultiplePreviewResultado(
                clienteId,
                cuotasPreview,
                cuotasPreview.Sum(c => c.CapitalPendiente),
                cuotasPreview.Sum(c => c.PunitorioAplicadoPendiente),
                cuotasPreview.Sum(c => c.RecargoMedioPago),
                cuotasPreview.Sum(c => c.TotalCaja),
                _reloj.HoyComercial);
        }

        /// <summary>
        /// Camino canónico único de lectura y validación del pago múltiple: resuelve las cuotas,
        /// valida pertenencia/estado/RowVersion y calcula la composición punitorio→capital de cada
        /// una (mismo <see cref="DistribuirPago"/> que el resto de los caminos de cobro). Usado por
        /// <see cref="PagarCuotasAsync"/> (dentro de la transacción) y por
        /// <see cref="PrevisualizarPagoMultipleAsync"/> (read-only, sin transacción) — cero
        /// duplicación de la regla de negocio entre preview y confirmación.
        /// </summary>
        /// <param name="rowVersionsEsperadas">
        /// <c>null</c> en el preview (no exige haber leído antes). En la confirmación, obligatorio
        /// para cada cuota: falta una entrada o no coincide con el valor real → rechazo total, sin
        /// mutar nada (esta validación corre antes de la primera escritura).
        /// </param>
        private async Task<List<PagoMultiplePlanItem>> PlanificarPagoMultipleAsync(
            int clienteId,
            List<int> cuotaIds,
            IReadOnlyDictionary<int, byte[]>? rowVersionsEsperadas,
            CancellationToken cancellationToken)
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

            if (clienteIds.Count != 1 || clienteIds[0] != clienteId)
                throw new InvalidOperationException("Todas las cuotas seleccionadas deben pertenecer al cliente indicado.");

            var pagosPlanificados = new List<PagoMultiplePlanItem>();

            foreach (var cuota in cuotas.OrderBy(c => c.CreditoId).ThenBy(c => c.NumeroCuota))
            {
                if (rowVersionsEsperadas is not null)
                {
                    if (!rowVersionsEsperadas.TryGetValue(cuota.Id, out var esperado))
                        throw new PagoCuotaRechazadoException(
                            MotivoRechazoPagoCuota.SolicitudInvalida,
                            $"Falta la versión de la cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero}. Recargá la cartera e intentá nuevamente.");

                    VerificarRowVersion(cuota.RowVersion, esperado);
                }

                if (cuota.Estado == EstadoCuota.Pagada)
                    throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} ya está pagada.");

                if (cuota.Estado == EstadoCuota.Cancelada)
                    throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} está cancelada.");

                // PUN-ML6: punitorio pendiente exclusivamente desde una aplicación autorizada
                // previa — nunca recalculado acá. PagarCuotasAsync siempre cancela el saldo
                // completo de cada cuota (no admite imputación parcial), así que el importe
                // disponible para el distribuidor es directamente el total de esa cuota.
                var infoPunitorio = await _punitorioService.ObtenerAplicacionActivaConProgresoAsync(cuota.Id, cancellationToken);
                var punitorioPendiente = infoPunitorio?.MontoPendiente ?? 0m;
                var saldoCuota = CalcularSaldoPendienteCuota(cuota);
                var total = RedondearImporte(saldoCuota + punitorioPendiente);

                if (total <= 0)
                    throw new InvalidOperationException($"La cuota #{cuota.NumeroCuota} del crédito {cuota.Credito.Numero} no tiene saldo pendiente.");

                var (mora, subtotal, _) = DistribuirPago(total, punitorioPendiente, saldoCuota);

                pagosPlanificados.Add(new PagoMultiplePlanItem(cuota, infoPunitorio, subtotal, mora, total));
            }

            return pagosPlanificados;
        }

        /// <summary>
        /// Decodifica y valida el mapa RowVersion del pago múltiple. Una entrada con Base64
        /// malformado se trata como versión inválida (mismo criterio que
        /// <see cref="ValidarRowVersionEsperada"/> en el pago individual/adelanto) — se detecta acá,
        /// antes de abrir la transacción, para no depender de que el mensaje de error identifique la
        /// cuota exacta más adelante.
        /// </summary>
        private static Dictionary<int, byte[]> DecodificarRowVersionsPorCuota(
            IReadOnlyDictionary<int, string>? rowVersionsPorCuota)
        {
            var resultado = new Dictionary<int, byte[]>();
            foreach (var (cuotaId, base64) in rowVersionsPorCuota ?? new Dictionary<int, string>())
            {
                if (string.IsNullOrWhiteSpace(base64))
                    continue;

                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    if (bytes.Length == 8)
                        resultado[cuotaId] = bytes;
                }
                catch (FormatException)
                {
                    // Queda sin entrada válida: PlanificarPagoMultipleAsync lo rechaza como
                    // "falta la versión de la cuota" al no encontrarla en el diccionario.
                }
            }

            return resultado;
        }

        /// <summary>Cuota planificada dentro de un pago múltiple, con su composición punitorio→capital ya resuelta.</summary>
        private sealed record PagoMultiplePlanItem(
            Cuota Cuota,
            PunitorioAplicadoProgreso? InfoPunitorio,
            decimal Subtotal,
            decimal Mora,
            decimal Total);

        /// <inheritdoc/>
        public async Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago)
        {
            if (pago == null)
                throw new ArgumentNullException(nameof(pago));

            // Mismo camino canónico que el pago normal: el servidor resuelve la cuota (la última
            // pendiente del plan), recalcula saldo, recargo, total y estado, y registra caja
            // dentro de la misma transacción. Del cliente no se toma ningún importe derivado.
            var aplicado = await RegistrarPagoCuotaAsync(pago, ModoCobroCuota.AdelantoUltimaCuota);

            if (aplicado == null)
            {
                _logger.LogWarning("No hay cuota adelantable en el crédito {CreditoId}", pago.CreditoId);
                return false;
            }

            // Se devuelve al modelo la cuota realmente adelantada para que la confirmación al
            // operador no repita el número que había enviado el formulario.
            pago.CuotaId = aplicado.CuotaId;
            pago.NumeroCuota = aplicado.NumeroCuota;

            _logger.LogInformation(
                "Cuota #{NumeroCuota} adelantada en crédito {CreditoId}. Monto: {Monto:C2}",
                aplicado.NumeroCuota, pago.CreditoId, aplicado.MontoBase);

            return true;
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
                // Vencidas = vencimiento anterior a la fecha comercial actual (Argentina).
                var inicioHoy = _reloj.InicioDiaComercial;
                var cuotas = await _context.Cuotas
                    .AsNoTracking()
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr.Cliente)
                    .Where(c => !c.IsDeleted &&
                               !c.Credito.IsDeleted &&
                               !c.Credito.Cliente.IsDeleted &&
                               c.FechaVencimiento < inicioHoy &&
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

        /// <summary>
        /// PUN-ML7: sincronización persistente Pendiente→Vencida, centralizada sobre
        /// <see cref="EstadoCuotaResolver.Resolver"/> — no por invocarlo por fila (sería un
        /// <c>SELECT</c>+<c>N</c> updates para una tabla potencialmente grande), sino porque su
        /// condición SQL es la traducción exacta de la misma regla: para una cuota <c>Pendiente</c>
        /// el resolver garantiza <c>montoPagado==0</c> (invariante mantenido por todo escritor de
        /// <see cref="Cuota.Estado"/> en este servicio y en <see cref="PunitorioService"/>), así que
        /// <c>Resolver(Pendiente, fv, fc, 0, total, 0)</c> vale <c>Vencida</c> exactamente cuando
        /// <see cref="EstadoCuotaResolver.EsVencidaPorFecha"/> es verdadero — la misma frontera que
        /// expresa <c>FechaVencimiento &lt; InicioDiaComercial</c> abajo (ver
        /// <c>EstadoCuotaResolverActualizarEstadoCuotasEquivalenciaTests</c> para la prueba de esta
        /// equivalencia). No se recategorizan cuotas <c>Parcial</c>: por diseño (ver
        /// <see cref="EstadoCuotaResolver.Resolver"/>) una cuota con pago parcial nunca pasa a
        /// <c>Vencida</c>, sea cual sea la fecha — sigue <c>Parcial</c>, que ya se incluye junto con
        /// <c>Vencida</c> en todo query de "cuotas impagas" del sistema (evidencia: <c>GetCuotasVencidasAsync</c>,
        /// <c>CreditoUiQueryService</c>, etc.). No toca estados terminales (<c>Cancelada</c> ni
        /// <c>Pagada</c> quedan fuera del <c>WHERE</c>), no recalcula punitorios ni escribe
        /// <see cref="Cuota.MontoPunitorio"/>. Idempotente y segura ante concurrencia: repetir la
        /// misma corrida el mismo día no cambia nada (el <c>WHERE</c> ya no matchea las filas que
        /// actualizó la corrida anterior) y dos ejecuciones simultáneas son un <c>UPDATE</c> por
        /// filas SQL estándar, sin condición de carrera con un cobro (que exige <c>Estado</c> no
        /// terminal y recalcula su propio estado dentro de su propia transacción serializable).
        /// </summary>
        public async Task ActualizarEstadoCuotasAsync()
        {
            try
            {
                // Solo cuotas cuyo vencimiento es anterior a la fecha comercial actual (Argentina).
                var inicioHoy = _reloj.InicioDiaComercial;
                var now = _reloj.AhoraUtc;

                await _context.Cuotas
                    .Where(c => !c.IsDeleted &&
                               c.FechaVencimiento < inicioHoy &&
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

        /// <summary>
        /// PUN-ML7: consulta histórica derivada — reconstruye el <see cref="EstadoCuota"/> que
        /// habría resuelto el sistema en <paramref name="fecha"/>, ignorando cualquier pago o
        /// aplicación/anulación de punitorio posterior. Solo lectura (<c>AsNoTracking</c>): nunca
        /// escribe <see cref="Cuota.Estado"/>/<see cref="Cuota.FechaPago"/> ni aplica/anula punitorios
        /// — no debe mezclarse con <see cref="ActualizarEstadoCuotasAsync"/> (esa es la única
        /// sincronización persistente).
        ///
        /// Limitaciones documentadas: (1) una cuota actualmente <see cref="EstadoCuota.Cancelada"/>
        /// se reporta Cancelada en cualquier fecha histórica — el modelo no guarda cuándo se canceló,
        /// y tratarla como reabierta hacia el pasado sería peor (contrato: terminal no se reabre).
        /// (2) filas de <see cref="PagoCuota"/> con <c>HistorialCompleto=false</c> (backfill sin
        /// composición confiable, PUN-ML2) aportan 0 a capital histórico — mismo criterio conservador
        /// que el resto del ledger, documentado ahí.
        /// </summary>
        public async Task<EstadoCuota> ResolverEstadoCuotaHistoricoAsync(int cuotaId, DateOnly fecha)
        {
            var cuota = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Pagos)
                .FirstOrDefaultAsync(c => c.Id == cuotaId && !c.IsDeleted)
                ?? throw new KeyNotFoundException($"Cuota #{cuotaId} no encontrada.");

            if (cuota.Estado == EstadoCuota.Cancelada)
                return EstadoCuota.Cancelada;

            var montoPagadoHistorico = cuota.Pagos
                .Where(p => p.Estado == EstadoPagoCuota.Aplicado && p.FechaPagoComercial <= fecha)
                .Sum(p => p.ImporteAplicadoCuota ?? 0m);

            var aplicaciones = await _context.PunitoriosAplicados
                .AsNoTracking()
                .Where(p => p.CuotaId == cuotaId && p.FechaCalculo <= fecha)
                .ToListAsync();

            decimal punitorioPendienteHistorico = 0m;
            foreach (var aplicacion in aplicaciones)
            {
                // Vigente a esa fecha: no fue anulada, o la anulación ocurrió después (en UTC — se
                // compara contra el cierre del día comercial consultado, igual que FechaAplicacion).
                var limiteFecha = fecha.ToDateTime(TimeOnly.MaxValue);
                var vigenteEnFecha = aplicacion.Estado != EstadoPunitorioAplicado.Anulado ||
                    (aplicacion.FechaAnulacion.HasValue && aplicacion.FechaAnulacion.Value > limiteFecha);

                if (!vigenteEnFecha)
                    continue;

                var pagadoHistorico = cuota.Pagos
                    .Where(p => p.Estado == EstadoPagoCuota.Aplicado &&
                                p.FechaPagoComercial <= fecha &&
                                p.PunitorioAplicadoId == aplicacion.Id)
                    .Sum(p => p.ImporteAplicadoPunitorio ?? 0m);

                punitorioPendienteHistorico += Math.Max(0m, aplicacion.Importe - pagadoHistorico);
            }

            return EstadoCuotaResolver.Resolver(
                cuota.Estado, cuota.FechaVencimiento, fecha, montoPagadoHistorico, cuota.MontoTotal, punitorioPendienteHistorico);
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
                        // Identidad histórica: snapshot al momento de la venta → relación viva sólo
                        // para filas legacy. El fallback "Producto #<id>" se aplica abajo. Micro-lote 5.
                        ProductoNombre = detalle.ProductoNombreAlMomento != null
                            ? detalle.ProductoNombreAlMomento
                            : (detalle.Producto != null ? detalle.Producto.Nombre : null),
                        ProductoCodigo = detalle.ProductoCodigoAlMomento != null
                            ? detalle.ProductoCodigoAlMomento
                            : (detalle.Producto != null ? detalle.Producto.Codigo : null),
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

        /// <summary>
        /// Saldo pendiente de la cuota en sí (capital + interés originales, sin punitorio: PUN-ML6
        /// lo trackea por separado vía <c>PunitorioAplicado</c>/<c>IPunitorioService</c>).
        /// </summary>
        private static decimal CalcularSaldoPendienteCuota(Cuota cuota)
        {
            return cuota.MontoTotal - cuota.MontoPagado;
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

            // CFTEA desde el plan canónico de recargo total (no CalcularCFTEADesdeTasa,
            // que asume interés compuesto mensual): mismo recargo de un solo pago que ya
            // aplica FinancialCalculationService.SimularPlanCredito, anualizado sobre la
            // cantidad de cuotas real. Antes quedaba en el default 0 de la entidad (nunca
            // se seteaba acá), indistinguible de "0% real" con TasaMensual=0.
            var recargoTotal = Math.Round(credito.MontoAprobado * cmd.TasaMensual / 100m, 2, MidpointRounding.AwayFromZero);
            var totalAPagarPlan = credito.MontoAprobado + recargoTotal;
            credito.CFTEA = _financialService.CalcularCFTEA(totalAPagarPlan, credito.MontoAprobado, cmd.CantidadCuotas);
            credito.MetodoCalculoAplicado     = cmd.MetodoCalculo;
            credito.FuenteConfiguracionAplicada = cmd.FuenteConfiguracion;
            credito.GastosAdministrativos     = cmd.GastosAdministrativos;
            credito.TasaInteresAplicada       = cmd.TasaMensual;
            credito.CuotasMinimasPermitidas       = cmd.CuotasMinPermitidas;
            credito.CuotasMaximasPermitidas       = cmd.CuotasMaxPermitidas;
            credito.FuenteRestriccionCuotasSnap   = cmd.FuenteRestriccionCuotasSnap;
            credito.ProductoIdRestrictivoSnap     = cmd.ProductoIdRestrictivoSnap;
            credito.MaxCuotasBaseSnap             = cmd.MaxCuotasBaseSnap;
            // F2: decisión de cobro de la 1ª cuota (ya validada server-side en la configuración).
            credito.CobrarPrimeraCuotaSolicitada  = cmd.CobrarPrimeraCuota;
            credito.MedioPagoPrimeraCuota         = cmd.CobrarPrimeraCuota ? cmd.MedioPagoPrimeraCuota : null;

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
