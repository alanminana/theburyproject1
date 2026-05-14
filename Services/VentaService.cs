using AutoMapper;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Services
{
    public class VentaService : IVentaService
    {
        /// <summary>Longitud máxima del campo MotivoAutorizacion en la entidad Venta.</summary>
        private const int MaxLongitudMotivoAutorizacion = 1000;

        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<VentaService> _logger;
        private readonly IPrecioVigenteResolver _precioVigenteResolver;
        private readonly IAlertaStockService _alertaStockService;
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly IFinancialCalculationService _financialService;
        private readonly IVentaValidator _validator;
        private readonly VentaNumberGenerator _numberGenerator;
        private readonly ICurrentUserService _currentUserService;
        private readonly IValidacionVentaService _validacionVentaService;
        private readonly ICajaService _cajaService;
        private readonly ICreditoDisponibleService _creditoDisponibleService;
        private readonly IContratoVentaCreditoService _contratoVentaCreditoService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IProductoCreditoRestriccionService _productoCreditoRestriccionService;
        private readonly IProductoUnidadService? _productoUnidadService;

        public VentaService(
            AppDbContext context,
            IMapper mapper,
            ILogger<VentaService> logger,
            IAlertaStockService alertaStockService,
            IMovimientoStockService movimientoStockService,
            IFinancialCalculationService financialService,
            IVentaValidator validator,
            VentaNumberGenerator numberGenerator,
            IPrecioVigenteResolver precioVigenteResolver,
            ICurrentUserService currentUserService,
            IValidacionVentaService validacionVentaService,
            ICajaService cajaService,
            ICreditoDisponibleService creditoDisponibleService,
            IContratoVentaCreditoService contratoVentaCreditoService,
            IConfiguracionPagoService configuracionPagoService,
            IProductoCreditoRestriccionService? productoCreditoRestriccionService = null,
            IProductoUnidadService? productoUnidadService = null)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _alertaStockService = alertaStockService;
            _movimientoStockService = movimientoStockService;
            _financialService = financialService;
            _validator = validator;
            _numberGenerator = numberGenerator;
            _precioVigenteResolver = precioVigenteResolver;
            _currentUserService = currentUserService;
            _validacionVentaService = validacionVentaService;
            _cajaService = cajaService;
            _creditoDisponibleService = creditoDisponibleService;
            _contratoVentaCreditoService = contratoVentaCreditoService;
            _configuracionPagoService = configuracionPagoService;
            _productoCreditoRestriccionService =
                productoCreditoRestriccionService ?? new ProductoCreditoRestriccionService(context);
            _productoUnidadService = productoUnidadService;
        }

        #region Consultas

        public async Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null)
        {
            var query = _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Credito)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)).ThenInclude(d => d.Producto)
                .Include(v => v.DatosTarjeta)
                .Include(v => v.DatosCheque)
                .Where(v =>
                    !v.IsDeleted &&
                    (v.Cliente == null || !v.Cliente.IsDeleted) &&
                    (v.Credito == null || (!v.Credito.IsDeleted && v.Credito.Cliente != null && !v.Credito.Cliente.IsDeleted)))
                .AsQueryable();

            query = AplicarFiltros(query, filter);

            var ventas = await query
                .OrderByDescending(v => v.FechaVenta)
                .ThenByDescending(v => v.Id)
                .ToListAsync();

            return _mapper.Map<List<VentaViewModel>>(ventas);
        }

        public async Task<VentaViewModel?> GetByIdAsync(int id)
        {
            _logger.LogDebug("GetByIdAsync venta {Id} requested", id);
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Credito)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto)
                .Include(v => v.Facturas)
                .Include(v => v.DatosTarjeta).ThenInclude(dt => dt!.ConfiguracionTarjeta)
                .Include(v => v.DatosCheque)
                .Include(v => v.VentaCreditoCuotas.OrderBy(c => c.NumeroCuota))
                .FirstOrDefaultAsync(v =>
                    v.Id == id &&
                    !v.IsDeleted &&
                    (v.Cliente == null || !v.Cliente.IsDeleted) &&
                    (v.Credito == null || (!v.Credito.IsDeleted && v.Credito.Cliente != null && !v.Credito.Cliente.IsDeleted)));

            if (venta == null)
            {
                _logger.LogWarning("GetByIdAsync venta {Id} not found or deleted", id);
                return null;
            }

            var viewModel = _mapper.Map<VentaViewModel>(venta);

            if (viewModel.Facturas.Any(f => !f.Anulada))
            {
                viewModel.ResumenAlicuotasFactura = FacturaAlicuotaResumenBuilder.Build(viewModel.Detalles);
            }

            // Mapear estado del crédito para control de flujo en la vista
            if (venta.Credito != null)
            {
                viewModel.CreditoEstado = venta.Credito.Estado;
            }

            if (venta.TipoPago == TipoPago.CreditoPersonal &&
                venta.CreditoId.HasValue &&
                venta.VentaCreditoCuotas.Any())
            {
                viewModel.DatosCreditoPersonall = await ObtenerDatosCreditoVentaAsync(id);
            }

            _logger.LogDebug(
                "GetByIdAsync venta {Id} loaded. Detalles:{Detalles} Facturas:{Facturas} Cuotas:{Cuotas} TipoPago:{TipoPago}",
                id,
                venta.Detalles.Count(d => !d.IsDeleted),
                venta.Facturas.Count(f => !f.IsDeleted),
                venta.VentaCreditoCuotas.Count,
                venta.TipoPago);

            return viewModel;
        }

        #endregion

        #region Crear y Actualizar

        public async Task<VentaViewModel> CreateAsync(VentaViewModel viewModel)
        {
            ValidarTipoPagoTarjetaNoPermitidoEnVentaNueva(viewModel.TipoPago);

            var currentUserName = _currentUserService.GetUsername();
            var currentUserId = await ObtenerUserIdActualAsync();
            var aperturaActiva = await AsegurarCajaAbiertaParaUsuarioActualAsync(
                "No se puede registrar la venta: no hay una caja abierta para el usuario actual. Abra una caja antes de realizar ventas.");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var venta = _mapper.Map<Venta>(viewModel);
                venta.AperturaCajaId = aperturaActiva.Id;

                var vendedorResuelto = await ResolverVendedorAsync(viewModel, currentUserId, currentUserName);
                venta.VendedorUserId = vendedorResuelto.UserId;
                venta.VendedorNombre = vendedorResuelto.Nombre;

                venta.Numero = await _numberGenerator.GenerarNumeroAsync(viewModel.Estado);

                AgregarDetalles(venta, viewModel.Detalles);

                await AplicarPrecioVigenteADetallesAsync(venta);

                CalcularTotales(venta);
                await CalcularComisionesAsync(venta);

                if (venta.TipoPago == TipoPago.CreditoPersonal)
                {
                    await ValidarCondicionesPagoCarritoAsync(venta);
                }

                await CapturarSnapshotLimiteCreditoAsync(venta);

                // Validación unificada para crédito personal
                ValidacionVentaResult? validacion = null;
                if (viewModel.TipoPago == TipoPago.CreditoPersonal)
                {
                    validacion = await _validacionVentaService.ValidarVentaCreditoPersonalAsync(
                        viewModel.ClienteId, 
                        venta.Total, 
                        viewModel.CreditoId);

                    // E2: Si NoViable, rechazar guardado completamente
                    if (validacion.NoViable)
                    {
                        if (PuedeAplicarExcepcionDocumentalCreate(viewModel, validacion))
                        {
                            var motivoExcepcion = viewModel.MotivoExcepcionDocumentalCreate!.Trim();
                            AplicarAuditoriaExcepcionDocumentalEnCreate(venta, currentUserName, motivoExcepcion);

                            validacion.NoViable = false;
                            validacion.PendienteRequisitos = false;
                            validacion.RequisitosPendientes = validacion.RequisitosPendientes
                                .Where(r => r.Tipo != TipoRequisitoPendiente.DocumentacionFaltante
                                         && r.Tipo != TipoRequisitoPendiente.SinLimiteCredito)
                                .ToList();

                            _logger.LogWarning(
                                "CreateAsync venta por excepción autorizada. Cliente:{ClienteId} Usuario:{Usuario}",
                                viewModel.ClienteId,
                                currentUserName);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"No es posible crear la venta con crédito personal. {validacion.MensajeResumen}");
                        }
                    }

                    await AplicarResultadoValidacionAsync(venta, validacion);
                }
                else
                {
                    await VerificarAutorizacionSiCorrespondeAsync(venta, viewModel);
                }

                _context.Ventas.Add(venta);
                await GuardarVentaConReintentoNumeroAsync(venta, viewModel.Estado);

                // Para crédito personal, crear el crédito inmediatamente después de guardar la venta
                if (viewModel.TipoPago == TipoPago.CreditoPersonal && 
                    venta.Estado == EstadoVenta.PendienteFinanciacion &&
                    !venta.CreditoId.HasValue)
                {
                    await CrearCreditoPendienteParaVentaAsync(venta);
                }

                // Solo guardar datos adicionales de crédito si no hay requisitos pendientes
                if (venta.Estado != EstadoVenta.PendienteRequisitos && 
                    venta.Estado != EstadoVenta.PendienteFinanciacion)
                {
                    await GuardarDatosAdicionales(venta.Id, viewModel);
                }

                await transaction.CommitAsync();

                var resultado = _mapper.Map<VentaViewModel>(venta);
                resultado.ValidacionCredito = validacion;
                resultado.CreditoId = venta.CreditoId; // Asegurar que el CreditoId se propague

                _logger.LogInformation("Venta {Numero} creada exitosamente. Estado: {Estado}", venta.Numero, venta.Estado);
                return resultado;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al crear venta");
                throw;
            }
        }

        private async Task GuardarVentaConReintentoNumeroAsync(Venta venta, EstadoVenta estado)
        {
            const int maxIntentos = 5;

            for (var intento = 1; intento <= maxIntentos; intento++)
            {
                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (EsErrorNumeroVentaDuplicado(ex) && intento < maxIntentos)
                {
                    var numeroAnterior = venta.Numero;
                    venta.Numero = await _numberGenerator.GenerarNumeroAsync(estado);

                    _logger.LogWarning(
                        ex,
                        "Colisión de número de venta detectada. Reintentando con nuevo número. Intento:{Intento} NumeroAnterior:{NumeroAnterior} NumeroNuevo:{NumeroNuevo}",
                        intento,
                        numeroAnterior,
                        venta.Numero);
                }
            }

            throw new InvalidOperationException(
                "No se pudo generar un número de venta único tras varios reintentos. Intentá nuevamente.");
        }

        private async Task CapturarSnapshotLimiteCreditoAsync(Venta venta)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return;
            }

            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == venta.ClienteId && !c.IsDeleted);

            if (cliente == null)
            {
                return;
            }

            venta.PuntajeAlMomento = cliente.PuntajeRiesgo;

            var config = await _context.ClientesCreditoConfiguraciones
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteId == cliente.Id);

            PuntajeCreditoLimite? preset = null;

            if (config?.CreditoPresetId.HasValue == true)
            {
                preset = await _context.PuntajesCreditoLimite
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == config.CreditoPresetId.Value && p.Activo);
            }

            preset ??= await _context.PuntajesCreditoLimite
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Puntaje == cliente.NivelRiesgo && p.Activo);

            var limiteBase = preset?.LimiteMonto ?? 0m;
            var limiteOverride = config?.LimiteOverride ?? cliente.LimiteCredito;
            var excepcionDelta = ObtenerExcepcionDeltaVigente(config, DateTime.UtcNow);

            var limiteEfectivo = CreditoDisponibleService
                .CalcularLimiteEfectivo(limiteBase, limiteOverride, excepcionDelta)
                .Limite;

            venta.PresetIdAlMomento = preset?.Id;
            venta.OverrideAlMomento = limiteOverride;
            venta.ExcepcionAlMomento = excepcionDelta > 0m ? excepcionDelta : null;
            venta.LimiteAplicado = limiteEfectivo > 0m ? limiteEfectivo : null;
        }

        private static decimal ObtenerExcepcionDeltaVigente(ClienteCreditoConfiguracion? config, DateTime fechaUtc)
        {
            if (config?.ExcepcionDelta is null || config.ExcepcionDelta.Value <= 0)
            {
                return 0m;
            }

            if (config.ExcepcionDesde.HasValue && config.ExcepcionDesde.Value > fechaUtc)
            {
                return 0m;
            }

            if (config.ExcepcionHasta.HasValue && config.ExcepcionHasta.Value < fechaUtc)
            {
                return 0m;
            }

            return config.ExcepcionDelta.Value;
        }

        private static bool EsErrorNumeroVentaDuplicado(DbUpdateException ex)
        {
            var mensaje = ex.InnerException?.Message ?? ex.Message;
            if (string.IsNullOrWhiteSpace(mensaje))
            {
                return false;
            }

            var mensajeNormalizado = mensaje.ToLower(CultureInfo.InvariantCulture);
            return mensajeNormalizado.Contains("ix_ventas_numero")
                   || mensajeNormalizado.Contains("duplicate key")
                   || mensajeNormalizado.Contains("duplicado")
                   || mensajeNormalizado.Contains("2601")
                   || mensajeNormalizado.Contains("2627");
        }

        /// <summary>
        /// Aplica el resultado de la validación de crédito a la venta.
        /// NOTA: Si NoViable=true, esta función no debe ser llamada (se rechaza antes).
        /// Para crédito personal aprobable:
        /// - Crea el crédito en estado PendienteConfiguracion
        /// - Pone la venta en estado PendienteFinanciacion
        /// </summary>
        private async Task AplicarResultadoValidacionAsync(Venta venta, ValidacionVentaResult validacion)
        {
            // Seguridad: NoViable nunca debería llegar aquí, pero por si acaso
            if (validacion.NoViable)
            {
                throw new InvalidOperationException(
                    $"Error interno: Se intentó aplicar validación NoViable. {validacion.MensajeResumen}");
            }

            if (validacion.RequiereAutorizacion)
            {
                // E2: Guardar venta en estado PendienteAutorizacion con razones persistidas
                venta.RequiereAutorizacion = true;
                venta.Estado = EstadoVenta.PendienteFinanciacion; // También PendienteFinanciacion
                venta.EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion;
                venta.FechaSolicitudAutorizacion = DateTime.UtcNow;
                venta.RazonesAutorizacionJson = System.Text.Json.JsonSerializer.Serialize(
                    validacion.RazonesAutorizacion.Select(r => new { r.Tipo, r.Descripcion, r.DetalleAdicional }));

                _logger.LogInformation(
                    "Venta requiere autorización. Razones: {Razones}",
                    validacion.MensajeResumen);
            }
            else
            {
                // Aprobable - Crear crédito y poner venta en PendienteFinanciacion
                venta.RequiereAutorizacion = false;
                venta.EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere;
                venta.Estado = EstadoVenta.PendienteFinanciacion;
                
                _logger.LogInformation(
                    "Venta con crédito personal aprobable. Estado: {Estado}. Crédito será creado después de persistir.",
                    venta.Estado);
            }

            await Task.CompletedTask;
        }

        private bool PuedeAplicarExcepcionDocumentalCreate(
            VentaViewModel viewModel,
            ValidacionVentaResult validacion)
        {
            if (!viewModel.AplicarExcepcionDocumental)
            {
                _logger.LogWarning("Excepción documental: AplicarExcepcionDocumental={Valor}", viewModel.AplicarExcepcionDocumental);
                return false;
            }

            if (string.IsNullOrWhiteSpace(viewModel.MotivoExcepcionDocumentalCreate))
            {
                _logger.LogWarning("Excepción documental: MotivoExcepcionDocumentalCreate vacío");
                return false;
            }

            // El acceso al botón en la view ya está gateado por ventas.authorize.
            // Acá verificamos que el usuario tenga al menos ventas.create (su permiso mínimo
            // para estar en este POST) o ventas.authorize si ya está en DB.
            var tieneAutorizar = _currentUserService.HasPermission("ventas", "authorize");
            var tieneCreate    = _currentUserService.HasPermission("ventas", "create");
            _logger.LogWarning(
                "Excepción documental: usuario={Usuario} ventas.authorize={Autorizar} ventas.create={Create}",
                _currentUserService.GetUsername(), tieneAutorizar, tieneCreate);

            if (!tieneAutorizar && !tieneCreate)
            {
                _logger.LogWarning("Excepción documental: usuario sin permisos suficientes");
                return false;
            }

            var tipos = validacion.RequisitosPendientes.Select(r => r.Tipo.ToString()).ToList();
            _logger.LogWarning("Excepción: RequisitosPendientes tipos=[{Tipos}]", string.Join(", ", tipos));

            // Tipos bypassables: documentación faltante (cualquier usuario autorizado) y sin límite de crédito
            // (solo ventas.authorize, ya que implica una decisión crediticia)
            var tiposPermitidos = new HashSet<TipoRequisitoPendiente>
            {
                TipoRequisitoPendiente.DocumentacionFaltante,
                TipoRequisitoPendiente.SinLimiteCredito
            };

            var todosPermitidos = validacion.RequisitosPendientes.Any()
                                  && validacion.RequisitosPendientes.All(r => tiposPermitidos.Contains(r.Tipo));

            _logger.LogWarning("Excepción: TodosPermitidos={Resultado}", todosPermitidos);

            if (!todosPermitidos)
                return false;

            // SinLimiteCredito es una decisión crediticia: requiere ventas.authorize
            var tieneSinLimite = validacion.RequisitosPendientes.Any(r =>
                r.Tipo == TipoRequisitoPendiente.SinLimiteCredito);

            if (tieneSinLimite && !tieneAutorizar)
            {
                _logger.LogWarning("Excepción: SinLimiteCredito requiere ventas.authorize — usuario no lo tiene");
                return false;
            }

            return true;
        }

        private static void AplicarAuditoriaExcepcionDocumentalEnCreate(
            Venta venta,
            string usuarioAutoriza,
            string motivo)
        {
            var fechaUtc = DateTime.UtcNow;
            var traza = $"EXCEPCION_DOC|{fechaUtc:O}|{usuarioAutoriza}|{motivo}";

            venta.UsuarioAutoriza = usuarioAutoriza;
            venta.FechaAutorizacion = fechaUtc;

            if (string.IsNullOrWhiteSpace(venta.MotivoAutorizacion))
            {
                venta.MotivoAutorizacion = traza;
            }
            else
            {
                var compuesto = $"{venta.MotivoAutorizacion}\n{traza}";
                venta.MotivoAutorizacion = compuesto.Length <= MaxLongitudMotivoAutorizacion
                    ? compuesto
                    : traza.Length <= MaxLongitudMotivoAutorizacion
                        ? traza
                        : traza.Substring(0, MaxLongitudMotivoAutorizacion);
            }
        }

        /// <summary>
        /// Crea el crédito para una venta con CreditoPersonal después de que la venta fue guardada.
        /// </summary>
        private async Task CrearCreditoPendienteParaVentaAsync(Venta venta)
        {
            // Generar número de crédito
            var ultimoCredito = await _context.Creditos
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();
            var numeroSecuencial = ultimoCredito != null ? ultimoCredito.Id + 1 : 1;
            var numeroCredito = $"CRE-{DateTime.UtcNow:yyyyMM}-{numeroSecuencial:D6}";

            // Obtener puntaje de riesgo del cliente
            var cliente = await _context.Clientes.FindAsync(venta.ClienteId);
            var puntajeRiesgo = cliente?.PuntajeRiesgo ?? 0;

            var credito = new Credito
            {
                ClienteId = venta.ClienteId,
                Numero = numeroCredito,
                MontoSolicitado = venta.Total,
                MontoAprobado = venta.Total,
                SaldoPendiente = venta.Total,
                TasaInteres = 0, // Se configurará después
                CantidadCuotas = 0, // Se configurará después
                Estado = EstadoCredito.PendienteConfiguracion,
                FechaSolicitud = DateTime.UtcNow,
                PuntajeRiesgoInicial = puntajeRiesgo
            };

            _context.Creditos.Add(credito);
            await _context.SaveChangesAsync();

            // Asociar crédito a la venta
            venta.CreditoId = credito.Id;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Crédito {NumeroCredito} (PendienteConfiguracion) creado y asociado a venta {VentaId}",
                numeroCredito, venta.Id);
        }

        public async Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel)
        {
            _logger.LogDebug(
                "UpdateAsync venta {Id} start. Detalles:{Detalles} RowVersion:{RowVersionLength} TipoPago:{TipoPago}",
                id,
                viewModel.Detalles.Count,
                viewModel.RowVersion?.Length ?? 0,
                viewModel.TipoPago);

            var venta = await _context.Ventas
                .Include(v => v.Detalles)
                .Include(v => v.DatosTarjeta)
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (venta == null)
            {
                _logger.LogWarning("UpdateAsync venta {Id} not found or deleted", id);
                return null;
            }

            ValidarTipoPagoTarjetaNoPermitidoEnEdicion(venta.TipoPago, viewModel.TipoPago);

            _logger.LogDebug(
                "UpdateAsync venta {Id} loaded. Estado:{Estado} Autorizacion:{EstadoAutorizacion} Detalles:{Detalles}",
                id,
                venta.Estado,
                venta.EstadoAutorizacion,
                venta.Detalles.Count(d => !d.IsDeleted));

            _validator.ValidarEstadoParaEdicion(venta);

            if (viewModel.RowVersion == null || viewModel.RowVersion.Length == 0)
                throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la venta e intentá nuevamente.");

            _context.Entry(venta).Property(v => v.RowVersion).OriginalValue = viewModel.RowVersion;

            ActualizarDatosVenta(venta, viewModel);
            ActualizarDetalles(venta, viewModel.Detalles);

            await AplicarPrecioVigenteADetallesAsync(venta);

            CalcularTotales(venta);
            // INVARIANTE: CalcularTotales siempre debe preceder a SincronizarDatosTarjetaEdicionAsync.
            // CalcularTotales establece venta.Total desde los ítems (base limpia).
            // Si el orden se invierte o se agrega otra llamada al ajuste después, el ajuste se compone.
            await SincronizarDatosTarjetaEdicionAsync(venta, viewModel);
            await CalcularComisionesAsync(venta);

            await VerificarAutorizacionSiCorrespondeAsync(venta, viewModel);

            try
            {
                _logger.LogDebug(
                    "UpdateAsync venta {Id} before SaveChanges. Subtotal:{Subtotal} Total:{Total} DetallesTotal:{DetallesTotal} DetallesActivos:{DetallesActivos}",
                    id,
                    venta.Subtotal,
                    venta.Total,
                    venta.Detalles.Count,
                    venta.Detalles.Count(d => !d.IsDeleted));
                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} antes de SaveChanges. Estado:{Estado}",
                    id,
                    venta.Estado);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning("UpdateAsync venta {Id} concurrency conflict", id);
                throw new InvalidOperationException(
                    "La venta fue modificada por otro usuario. Recargá la página y volvé a intentar.");
            }

            _logger.LogInformation("Venta {Id} actualizada exitosamente", id);
            return _mapper.Map<VentaViewModel>(venta);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
            if (venta == null)
                return false;

            _validator.ValidarEstadoParaEliminacion(venta);

            venta.IsDeleted = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Venta {Id} eliminada", id);
            return true;
        }

        #endregion

        #region Flujo de Venta

        public async Task<bool> ConfirmarVentaAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var aperturaActiva = await AsegurarCajaAbiertaParaUsuarioActualAsync(
                    "No se puede confirmar la venta sin una caja abierta para el usuario actual.");
                _logger.LogInformation("ConfirmarVentaAsync inicio venta {Id}", id);
                var venta = await CargarVentaCompleta(id);
                if (venta == null)
                {
                    _logger.LogWarning("ConfirmarVentaAsync venta {Id} no encontrada", id);
                    return false;
                }

                _logger.LogInformation(
                    "ConfirmarVentaAsync venta {Id} cargada. Estado:{Estado} TipoPago:{TipoPago} Detalles:{Detalles}",
                    id,
                    venta.Estado,
                    venta.TipoPago,
                    venta.Detalles.Count(d => !d.IsDeleted));

                // Guard: medios que requieren snapshot de datos de pago (Fase 7.1)
                if ((venta.TipoPago is TipoPago.TarjetaCredito or TipoPago.TarjetaDebito or TipoPago.MercadoPago)
                    && venta.DatosTarjeta == null)
                {
                    throw new InvalidOperationException(
                        $"No se puede confirmar la venta: el medio de pago '{venta.TipoPago}' requiere datos de tarjeta y no han sido completados.");
                }

                // Validación previa del estado
                _validator.ValidarEstadoParaConfirmacion(venta);
                _validator.ValidarStock(venta);

                // Para crédito personal, re-validar requisitos antes de confirmar
                if (venta.TipoPago == TipoPago.CreditoPersonal)
                {
                    await AsegurarSnapshotLimiteCreditoAsync(venta);

                    var validacion = await _validacionVentaService.ValidarConfirmacionVentaAsync(id);
                    
                    if (validacion.PendienteRequisitos)
                    {
                        _logger.LogWarning(
                            "ConfirmarVentaAsync venta {Id} pendiente requisitos. Motivo:{Motivo}",
                            id,
                            validacion.MensajeResumen);
                        throw new InvalidOperationException(
                            $"No se puede confirmar la venta. {validacion.MensajeResumen}");
                    }

                    if (validacion.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
                    {
                        _logger.LogWarning(
                            "ConfirmarVentaAsync venta {Id} requiere autorizacion y no esta autorizada. EstadoAutorizacion:{EstadoAutorizacion}",
                            id,
                    venta.EstadoAutorizacion);
                        throw new InvalidOperationException(
                            $"La venta requiere autorización. {validacion.MensajeResumen}");
                    }

                    await ValidarCupoDisponibleEnConfirmacionAsync(venta, venta.Total);
                    await ValidarContratoCreditoPersonalGeneradoAsync(venta);
                }
                else
                {
                    _validator.ValidarAutorizacion(venta);
                }

                venta.AperturaCajaId = aperturaActiva.Id;

                await ValidarUnidadesTrazablesAsync(venta);
                await DescontarStockYRegistrarMovimientos(venta);
                await MarcarUnidadesVendidasAsync(venta);

                // E4: Procesar crédito personal solo si hay datos JSON y la venta está autorizada
                if (venta.TipoPago == TipoPago.CreditoPersonal &&
                    !string.IsNullOrEmpty(venta.DatosCreditoPersonallJson))
                {
                    // Verificar que la venta esté autorizada (o no requiera autorización)
                    if (venta.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
                    {
                        throw new InvalidOperationException(
                            "No se puede crear el crédito: la venta requiere autorización y no está autorizada.");
                    }

                    // E4: Crear cuotas y asignar CreditoId desde JSON (solo al confirmar post-autorización)
                    await CrearCreditoDefinitivoDesdeJsonAsync(venta);
                }

                await GenerarAlertasStockBajo(venta);

                venta.Estado = EstadoVenta.Confirmada;
                venta.FechaConfirmacion = DateTime.UtcNow;
                // Limpiar requisitos pendientes y datos temporales al confirmar
                venta.RequisitosPendientesJson = null;
                venta.DatosCreditoPersonallJson = null;

                _logger.LogInformation(
                    "ConfirmarVentaAsync venta {Id} antes de SaveChanges. Estado:{Estado}",
                    id,
                    venta.Estado);
                await _context.SaveChangesAsync();

                // Registrar movimiento de caja al confirmar para ventas con cobro inmediato
                // (Efectivo, Tarjeta, Cheque, Transferencia, MercadoPago).
                // Las ventas a crédito personal y cuenta corriente no generan ingreso inmediato.
                if (venta.TipoPago != TipoPago.CreditoPersonal &&
                    venta.TipoPago != TipoPago.CuentaCorriente)
                {
                    var usuario = _currentUserService.GetUsername();
                    await _cajaService.RegistrarMovimientoVentaAsync(
                        venta.Id,
                        venta.Numero,
                        venta.Total,
                        venta.TipoPago,
                        usuario);
                }

                await transaction.CommitAsync();

                _logger.LogInformation("ConfirmarVentaAsync venta {Id} confirmada", id);
                _logger.LogInformation("Venta {Id} confirmada exitosamente", id);
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "ConfirmarVentaAsync error venta {Id}", id);
                _logger.LogError(ex, "Error al confirmar venta {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Confirma una venta con crédito personal ya configurado: genera cuotas y marca crédito como Generado.
        /// Este método asume que el crédito ya pasó por ConfigurarVenta (estado = Configurado).
        /// </summary>
        public async Task<bool> ConfirmarVentaCreditoAsync(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var aperturaActiva = await AsegurarCajaAbiertaParaUsuarioActualAsync(
                    "No se puede confirmar la venta sin una caja abierta para el usuario actual.");
                _logger.LogInformation("ConfirmarVentaCreditoAsync inicio venta {Id}", id);
                var venta = await CargarVentaCompleta(id);
                if (venta == null)
                {
                    _logger.LogWarning("ConfirmarVentaCreditoAsync venta {Id} no encontrada", id);
                    return false;
                }

                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} cargada. Estado:{Estado} TipoPago:{TipoPago} CreditoId:{CreditoId}",
                    id,
                    venta.Estado,
                    venta.TipoPago,
                    venta.CreditoId);

                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} tipo pago {TipoPago}",
                    id,
                    venta.TipoPago);
                if (venta.TipoPago != TipoPago.CreditoPersonal)
                    throw new InvalidOperationException("Esta venta no es de tipo Crédito Personal.");

                venta.AperturaCajaId = aperturaActiva.Id;

                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} CreditoId:{CreditoId}",
                    id,
                    venta.CreditoId);
                if (!venta.CreditoId.HasValue)
                    throw new InvalidOperationException("La venta no tiene un crédito asociado.");

                var credito = await _context.Creditos.FindAsync(venta.CreditoId.Value);
                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} credito encontrado:{Encontrado} Estado:{Estado}",
                    id,
                    credito != null,
                    credito?.Estado);
                if (credito == null)
                    throw new InvalidOperationException("Crédito no encontrado.");

                await AsegurarSnapshotLimiteCreditoAsync(venta);

                var montoOperacion = credito.MontoAprobado > 0m ? credito.MontoAprobado : venta.Total;
                await ValidarCupoDisponibleEnConfirmacionAsync(venta, montoOperacion);

                // Permitir confirmar si el crédito está Configurado O si la venta tiene el flag
                if (credito.Estado != EstadoCredito.Configurado && !venta.FechaConfiguracionCredito.HasValue)
                    throw new InvalidOperationException(
                        $"El crédito debe estar en estado Configurado para confirmar. Estado actual: {credito.Estado}");

                // Si el crédito no está en Configurado pero la venta tiene el flag, corregir el estado
                if (credito.Estado != EstadoCredito.Configurado && venta.FechaConfiguracionCredito.HasValue)
                {
                    _logger.LogWarning(
                        "Corrigiendo estado de crédito {CreditoId} de {EstadoActual} a Configurado (flag presente en venta)",
                        credito.Id, credito.Estado);
                    credito.Estado = EstadoCredito.Configurado;
                }

                if (credito.TasaInteres <= 0m)
                    throw new InvalidOperationException(
                        "La tasa de interés de Crédito Personal no está configurada. " +
                        "Configure el valor en Administración → Tipos de Pago antes de confirmar la venta.");

                await ValidarCuotasCreditoPersonalPorProductoAsync(venta, credito);

                // Validar stock antes de confirmar
                _validator.ValidarStock(venta);
                _validator.ValidarAutorizacion(venta);
                await ValidarContratoCreditoPersonalGeneradoAsync(venta);

                await ValidarUnidadesTrazablesAsync(venta);
                await DescontarStockYRegistrarMovimientos(venta);
                await MarcarUnidadesVendidasAsync(venta);

                // Generar las cuotas del crédito
                await GenerarCuotasCreditoAsync(credito, venta.Total);

                // Marcar crédito como Generado
                credito.Estado = EstadoCredito.Generado;
                credito.FechaAprobacion = DateTime.UtcNow;

                await GenerarAlertasStockBajo(venta);

                venta.Estado = EstadoVenta.Confirmada;
                venta.FechaConfirmacion = DateTime.UtcNow;
                venta.RequisitosPendientesJson = null;

                _logger.LogInformation(
                    "ConfirmarVentaCreditoAsync venta {Id} antes de SaveChanges. Estado:{Estado}",
                    id,
                    venta.Estado);
                await _context.SaveChangesAsync();

                // Registrar anticipo en caja si lo hay (anticipo = total venta - monto financiado)
                var anticipo = venta.Total - credito.MontoAprobado;
                if (anticipo > 0)
                {
                    var usuario = _currentUserService.GetUsername();
                    await _cajaService.RegistrarMovimientoAnticipoAsync(
                        credito.Id,
                        credito.Numero,
                        anticipo,
                        usuario);
                }

                await transaction.CommitAsync();
                _logger.LogInformation("ConfirmarVentaCreditoAsync venta {Id} confirmada", id);

                _logger.LogInformation(
                    "Venta {VentaId} confirmada con crédito {CreditoId} generado ({Cuotas} cuotas)",
                    id, credito.Id, credito.CantidadCuotas);
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "ConfirmarVentaCreditoAsync error venta {Id}", id);
                _logger.LogError(ex, "Error al confirmar venta con crédito {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Genera las cuotas del crédito según la configuración del plan
        /// </summary>
        private async Task GenerarCuotasCreditoAsync(Credito credito, decimal montoVenta)
        {
            // Calcular cuota usando el servicio financiero
            var montoFinanciado = credito.MontoAprobado > 0 ? credito.MontoAprobado : montoVenta;
            var tasaDecimal = credito.TasaInteres / 100m;
            var cuotaMensual = _financialService.ComputePmt(tasaDecimal, credito.CantidadCuotas, montoFinanciado);

            // Calcular componentes de la cuota (sistema francés simplificado)
            var interesTotal = (cuotaMensual * credito.CantidadCuotas) - montoFinanciado;
            var capitalPorCuota = montoFinanciado / credito.CantidadCuotas;
            var interesPorCuota = interesTotal / credito.CantidadCuotas;

            credito.MontoCuota = cuotaMensual;
            credito.TotalAPagar = cuotaMensual * credito.CantidadCuotas;
            credito.SaldoPendiente = montoFinanciado;

            // Crear las cuotas
            var fechaCuota = credito.FechaPrimeraCuota ?? DateTime.Today.AddMonths(1);
            for (int i = 1; i <= credito.CantidadCuotas; i++)
            {
                var cuota = new Cuota
                {
                    CreditoId = credito.Id,
                    NumeroCuota = i,
                    MontoCapital = capitalPorCuota,
                    MontoInteres = interesPorCuota,
                    MontoTotal = cuotaMensual,
                    FechaVencimiento = fechaCuota,
                    Estado = EstadoCuota.Pendiente
                };
                _context.Cuotas.Add(cuota);
                fechaCuota = fechaCuota.AddMonths(1);
            }

            await Task.CompletedTask;
        }

        public async Task AsociarCreditoAVentaAsync(int ventaId, int creditoId)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);
            if (venta == null)
                throw new InvalidOperationException(VentaConstants.ErrorMessages.VENTA_NO_ENCONTRADA);

            venta.CreditoId = creditoId;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> CancelarVentaAsync(int id, string motivo)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var venta = await CargarVentaCompleta(id);
                if (venta == null)
                    return false;

                _validator.ValidarNoEstaCancelada(venta);

                if (venta.Estado == EstadoVenta.Confirmada || venta.Estado == EstadoVenta.Facturada)
                {
                    await DevolverStock(venta, motivo);
                    await RevertirUnidadesVentaAsync(venta, motivo);
                }

                if (venta.TipoPago == TipoPago.CreditoPersonal)
                {
                    // Si la venta fue confirmada, restaurar el crédito
                    if (venta.Estado == EstadoVenta.Confirmada || venta.Estado == EstadoVenta.Facturada)
                    {
                        await RestaurarCreditoPersonall(venta);
                    }
                    else
                    {
                        // Si la venta nunca fue confirmada, solo limpiar datos temporales
                        await LimpiarDatosCreditoVentaAsync(venta);
                    }
                }

                venta.Estado = EstadoVenta.Cancelada;
                venta.FechaCancelacion = DateTime.UtcNow;
                venta.MotivoCancelacion = motivo;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Venta {Id} cancelada. Motivo: {Motivo}", id, motivo);
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al cancelar venta {Id}", id);
                throw;
            }
        }

        public async Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel)
        {
            var aperturaActiva = await AsegurarCajaAbiertaParaUsuarioActualAsync(
                "No se puede facturar la venta sin una caja abierta para el usuario actual.");
            var venta = await _context.Ventas
                .Include(v => v.Facturas)
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (venta == null)
                return false;

            _validator.ValidarEstadoParaFacturacion(venta);
            _validator.ValidarAutorizacion(venta);
            await ValidarContratoCreditoPersonalGeneradoAsync(venta);
            venta.AperturaCajaId = aperturaActiva.Id;

            var factura = _mapper.Map<Factura>(facturaViewModel);
            factura.VentaId = venta.Id;
            factura.Numero = await _numberGenerator.GenerarNumeroFacturaAsync(factura.Tipo);
            factura.Subtotal = venta.Subtotal;
            factura.IVA = venta.IVA;
            factura.Total = venta.Total;

            _context.Facturas.Add(factura);

            venta.Estado = EstadoVenta.Facturada;
            venta.FechaFacturacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Fallback: si la venta se confirmó antes del fix que registra al confirmar,
            // registrar ahora para no perder el movimiento.
            if (venta.TipoPago != TipoPago.CreditoPersonal &&
                venta.TipoPago != TipoPago.CuentaCorriente)
            {
                var yaRegistrado = await _context.MovimientosCaja
                    .AnyAsync(m => m.VentaId == venta.Id
                                && m.Tipo == TipoMovimientoCaja.Ingreso
                                && !m.IsDeleted);

                if (!yaRegistrado)
                {
                    yaRegistrado = await _context.MovimientosCaja
                        .AnyAsync(m => m.VentaId == null
                                    && m.ReferenciaId == venta.Id
                                    && m.Tipo == TipoMovimientoCaja.Ingreso
                                    && !m.IsDeleted);
                }

                if (!yaRegistrado)
                {
                    var usuario = _currentUserService.GetUsername();
                    await _cajaService.RegistrarMovimientoVentaAsync(
                        venta.Id,
                        venta.Numero,
                        venta.Total,
                        venta.TipoPago,
                        usuario);
                }
            }

            _logger.LogInformation("Venta {Id} facturada con factura {NumeroFactura}", id, factura.Numero);
            return true;
        }

        private async Task<AperturaCaja> AsegurarCajaAbiertaParaUsuarioActualAsync(string mensajeError)
        {
            if (!_currentUserService.IsAuthenticated())
            {
                throw new InvalidOperationException(mensajeError);
            }

            var currentUserName = _currentUserService.GetUsername();
            var aperturaActiva = await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(currentUserName);
            if (aperturaActiva == null)
            {
                throw new InvalidOperationException(mensajeError);
            }

            return aperturaActiva;
        }

        private async Task ValidarContratoCreditoPersonalGeneradoAsync(Venta venta)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
                return;

            if (!await _contratoVentaCreditoService.ExisteContratoGeneradoAsync(venta.Id))
            {
                throw new InvalidOperationException(
                    "Debe generar e imprimir el Contrato de Venta antes de continuar.");
            }
        }

        private async Task ValidarCondicionesPagoCarritoAsync(Venta venta)
        {
            var productoIds = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => d.ProductoId)
                .Distinct()
                .ToArray();

            if (productoIds.Length == 0)
            {
                return;
            }

            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return;
            }

            var creditoResultado = await _productoCreditoRestriccionService.ResolverAsync(productoIds);
            if (!creditoResultado.Permitido)
            {
                throw new CondicionesPagoVentaException(
                    await CrearMensajeBloqueoCreditoProductoAsync(creditoResultado));
            }
        }

        private async Task ValidarCuotasCreditoPersonalPorProductoAsync(Venta venta, Credito credito)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return;
            }

            var productoIds = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => d.ProductoId)
                .Distinct()
                .ToArray();

            if (productoIds.Length == 0)
            {
                return;
            }

            var metodo = credito.MetodoCalculoAplicado ?? MetodoCalculoCredito.Global;
            var (minBase, maxBase, descripcionMetodo, _) =
                await _configuracionPagoService.ResolverRangoCuotasAsync(
                    metodo,
                    credito.PerfilCreditoAplicadoId,
                    venta.ClienteId);

            var resultado = await _productoCreditoRestriccionService.ResolverAsync(productoIds);

            if (!resultado.Permitido)
            {
                throw new CondicionesPagoVentaException(
                    await CrearMensajeBloqueoCreditoProductoAsync(resultado));
            }

            var maxEfectivo = resultado.MaxCuotasCredito.HasValue
                ? Math.Min(maxBase, resultado.MaxCuotasCredito.Value)
                : maxBase;

            if (minBase > maxEfectivo)
            {
                throw new CondicionesPagoVentaException(
                    $"No se puede confirmar la venta con CréditoPersonal. " +
                    $"El rango de cuotas queda inválido: mínimo {minBase}, máximo efectivo {maxEfectivo}.");
            }

            if (credito.CantidadCuotas < minBase || credito.CantidadCuotas > maxEfectivo)
            {
                throw new CondicionesPagoVentaException(
                    $"No se puede confirmar la venta con CréditoPersonal. " +
                    $"La cantidad de cuotas configurada ({credito.CantidadCuotas}) debe estar entre {minBase} y {maxEfectivo} " +
                    $"según el método '{descripcionMetodo}' y las restricciones por producto.");
            }
        }

        private async Task<string> CrearMensajeBloqueoCreditoProductoAsync(
            ProductoCreditoRestriccionResultado resultado)
        {
            var nombres = await ObtenerNombresProductosAsync(resultado.ProductoIdsBloqueantes);
            var detalles = nombres.Count == 0
                ? "Hay productos incompatibles con CreditoPersonal."
                : string.Join(", ", nombres.Values.Select(nombre => $"{nombre}: bloquea CreditoPersonal."));

            return $"No se puede crear o confirmar la venta con {TipoPago.CreditoPersonal}. {detalles}";
        }

        private async Task<Dictionary<int, string>> ObtenerNombresProductosAsync(IEnumerable<int> productoIds)
        {
            var ids = productoIds
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _context.Productos
                .AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Nombre);
        }

        public async Task<int?> AnularFacturaAsync(int facturaId, string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                throw new ArgumentException("Debe indicar el motivo de anulación.", nameof(motivo));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var factura = await _context.Facturas
                    .Include(f => f.Venta)
                    .FirstOrDefaultAsync(f => f.Id == facturaId && !f.IsDeleted);

                if (factura == null)
                {
                    return null;
                }

                if (factura.Anulada)
                {
                    throw new InvalidOperationException("La factura ya fue anulada.");
                }

                factura.Anulada = true;
                factura.FechaAnulacion = DateTime.UtcNow;
                factura.MotivoAnulacion = motivo.Trim();
                factura.UpdatedAt = DateTime.UtcNow;

                var venta = factura.Venta;
                if (venta != null && !venta.IsDeleted && venta.Estado == EstadoVenta.Facturada)
                {
                    var tieneOtrasFacturasActivas = await _context.Facturas
                        .AnyAsync(f =>
                            f.VentaId == venta.Id &&
                            f.Id != factura.Id &&
                            !f.IsDeleted &&
                            !f.Anulada);

                    if (!tieneOtrasFacturasActivas)
                    {
                        venta.Estado = EstadoVenta.Confirmada;
                        venta.FechaFacturacion = null;
                        venta.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Factura {FacturaId} anulada para venta {VentaId}. Motivo: {Motivo}",
                    factura.Id,
                    factura.VentaId,
                    factura.MotivoAnulacion);

                return factura.VentaId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }


        #endregion

        #region Autorización

        public async Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
            if (venta == null)
                return false;

            venta.RequiereAutorizacion = true;
            venta.EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion;
            venta.UsuarioSolicita = usuarioSolicita;
            venta.FechaSolicitudAutorizacion = DateTime.UtcNow;
            venta.MotivoAutorizacion = motivo;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Solicitud de autorización creada para venta {Id} por {Usuario}", id, usuarioSolicita);
            return true;
        }

        public async Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo)
        {
            // E3: Motivo/observación es obligatorio
            if (string.IsNullOrWhiteSpace(motivo))
            {
                throw new ArgumentException(
                    "El motivo/observación es obligatorio para autorizar una venta.",
                    nameof(motivo));
            }

            // E3: Solo se puede autorizar si está en PendienteAutorizacion
            var venta = await ObtenerVentaPendienteAutorizacionAsync(id);
            if (venta == null)
                return false;

            // E3: Registrar auditoría completa
            venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
            venta.UsuarioAutoriza = usuarioAutoriza;
            venta.FechaAutorizacion = DateTime.UtcNow;
            venta.MotivoAutorizacion = motivo.Trim();
            
            // Las razones autorizadas ya están en RazonesAutorizacionJson (guardadas al crear)
            // No se modifican, quedan como registro de qué se autorizó

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Venta {Id} autorizada por {Usuario}. Motivo: {Motivo}. Razones: {Razones}",
                id, usuarioAutoriza, motivo, venta.RazonesAutorizacionJson ?? "N/A");
            return true;
        }

        public async Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo)
        {
            var venta = await ObtenerVentaPendienteAutorizacionAsync(id);
            if (venta == null)
                return false;

            venta.EstadoAutorizacion = EstadoAutorizacionVenta.Rechazada;
            venta.UsuarioAutoriza = usuarioAutoriza;
            venta.FechaAutorizacion = DateTime.UtcNow;
            venta.MotivoRechazo = motivo;

            // Limpiar datos de crédito al rechazar para evitar "créditos fantasma"
            await LimpiarDatosCreditoVentaAsync(venta);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Venta {Id} rechazada por {Usuario}. Motivo: {Motivo}", id, usuarioAutoriza, motivo);
            return true;
        }

        public async Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo)
        {
            if (string.IsNullOrWhiteSpace(usuarioAutoriza) || string.IsNullOrWhiteSpace(motivo))
            {
                return false;
            }

            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (venta == null)
            {
                return false;
            }

            var fechaUtc = DateTime.UtcNow;
            var motivoNormalizado = motivo.Trim();
            var traza = $"EXCEPCION_DOC|{fechaUtc:O}|{usuarioAutoriza}|{motivoNormalizado}";

            venta.UsuarioAutoriza = usuarioAutoriza;
            venta.FechaAutorizacion = fechaUtc;

            if (string.IsNullOrWhiteSpace(venta.MotivoAutorizacion))
            {
                venta.MotivoAutorizacion = traza;
            }
            else
            {
                var compuesto = $"{venta.MotivoAutorizacion}\n{traza}";
                venta.MotivoAutorizacion = compuesto.Length <= MaxLongitudMotivoAutorizacion
                    ? compuesto
                    : traza.Length <= MaxLongitudMotivoAutorizacion
                        ? traza
                        : traza.Substring(0, MaxLongitudMotivoAutorizacion);
            }

            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "Excepción documental registrada en venta {Id} por {Usuario}. Motivo: {Motivo}",
                id,
                usuarioAutoriza,
                motivoNormalizado);

            return true;
        }

        /// <summary>
        /// Limpia todos los datos de crédito asociados a una venta rechazada o cancelada.
        /// </summary>
        private async Task LimpiarDatosCreditoVentaAsync(Venta venta)
        {
            // Limpiar plan de crédito JSON temporal
            venta.DatosCreditoPersonallJson = null;

            // Eliminar cuotas si existen (no deberían existir si el flujo es correcto)
            var cuotasExistentes = await _context.VentaCreditoCuotas
                .Where(c => c.VentaId == venta.Id)
                .ToListAsync();

            if (cuotasExistentes.Any())
            {
                _context.VentaCreditoCuotas.RemoveRange(cuotasExistentes);
                _logger.LogWarning(
                    "Se eliminaron {Count} cuotas huérfanas de la venta rechazada {VentaId}",
                    cuotasExistentes.Count, venta.Id);
            }

            // Limpiar asociación con crédito
            venta.CreditoId = null;

            _logger.LogInformation("Datos de crédito limpiados para venta {VentaId}", venta.Id);
        }

        public async Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel)
        {
            if (viewModel.TipoPago != TipoPago.CreditoPersonal)
                return false;

            // Usar el servicio de validación unificado
            var validacion = await _validacionVentaService.ValidarVentaCreditoPersonalAsync(
                viewModel.ClienteId, 
                viewModel.Total, 
                viewModel.CreditoId);

            return validacion.RequiereAutorizacion;
        }

        #endregion

        #region Métodos de Cálculo - Tarjetas

        public async Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas)
        {
            var configuracion = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tarjetaId && !t.IsDeleted);
            if (configuracion == null)
                throw new InvalidOperationException("Configuración de tarjeta no encontrada");

            if (!configuracion.Activa)
                throw new InvalidOperationException("La tarjeta seleccionada no está disponible");

            var resultado = new DatosTarjetaViewModel
            {
                ConfiguracionTarjetaId = tarjetaId,
                NombreTarjeta = configuracion.NombreTarjeta,
                TipoTarjeta = configuracion.TipoTarjeta,
                CantidadCuotas = cuotas,
                TipoCuota = configuracion.TipoCuota
            };

            if (configuracion.TipoCuota == TipoCuotaTarjeta.SinInteres)
            {
                resultado.TasaInteres = 0;
                resultado.MontoCuota = monto / cuotas;
                resultado.MontoTotalConInteres = monto;
            }
            else if (configuracion.TipoCuota == TipoCuotaTarjeta.ConInteres &&
                     configuracion.TasaInteresesMensual.HasValue)
            {
                var tasaDecimal = configuracion.TasaInteresesMensual.Value / 100;
                resultado.TasaInteres = configuracion.TasaInteresesMensual.Value;

                resultado.MontoCuota = _financialService.CalcularCuotaSistemaFrances(
                    monto, tasaDecimal, cuotas);
                resultado.MontoTotalConInteres = resultado.MontoCuota.Value * cuotas;
            }
            else if (configuracion.TipoCuota == TipoCuotaTarjeta.ConInteres)
            {
                throw new InvalidOperationException("La tarjeta con interés no tiene tasa configurada");
            }

            return resultado;
        }

        public async Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);
            if (venta == null)
                return false;

            // Guard: evita duplicar recargo de débito o reemplazar snapshot silenciosamente
            // en una segunda llamada (bug confirmado en Fase 6.2).
            var yaExiste = await _context.DatosTarjeta
                .AnyAsync(d => d.VentaId == ventaId && !d.IsDeleted);
            if (yaExiste)
                return false;

            var datosTarjetaEntity = _mapper.Map<DatosTarjeta>(datosTarjeta);
            datosTarjetaEntity.VentaId = ventaId;
            await AplicarSnapshotDatosTarjetaAsync(venta, datosTarjetaEntity, datosTarjeta);

            _context.DatosTarjeta.Add(datosTarjetaEntity);
            await _context.SaveChangesAsync();

            return true;
        }

        private async Task AplicarSnapshotDatosTarjetaAsync(
            Venta venta,
            DatosTarjeta datosTarjetaEntity,
            DatosTarjetaViewModel datosTarjeta)
        {
            LimpiarSnapshotDatosTarjeta(datosTarjetaEntity);

            ConfiguracionTarjeta? configuracionTarjeta = null;
            if (datosTarjeta.ConfiguracionTarjetaId.HasValue)
            {
                configuracionTarjeta = await _context.ConfiguracionesTarjeta
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == datosTarjeta.ConfiguracionTarjetaId.Value && !t.IsDeleted);

                if (configuracionTarjeta == null)
                    throw new InvalidOperationException("Configuracion de tarjeta no encontrada");

                if (!configuracionTarjeta.Activa)
                    throw new InvalidOperationException("La tarjeta seleccionada no esta disponible");
            }

            var planGlobal = await ValidarYObtenerPlanPagoGlobalAsync(
                datosTarjeta.ConfiguracionPagoPlanId,
                venta.TipoPago,
                datosTarjeta.ConfiguracionTarjetaId);

            ProductoCondicionPagoPlan? planSeleccionado = null;
            if (planGlobal != null)
            {
                // Fase 7.7: el plan global es la fuente canonica; cualquier plan por producto
                // que llegue por compatibilidad legacy no debe validar, bloquear ni ajustar la venta nueva.
                datosTarjetaEntity.ProductoCondicionPagoPlanId = null;
            }
            else if (datosTarjeta.ProductoCondicionPagoPlanId.HasValue)
            {
                planSeleccionado = await ValidarYObtenerPlanPagoAsync(datosTarjeta.ProductoCondicionPagoPlanId.Value, venta.TipoPago);
            }

            var tipoTarjeta = configuracionTarjeta?.TipoTarjeta ?? datosTarjeta.TipoTarjeta;
            if (configuracionTarjeta != null)
            {
                ValidarTarjetaCorrespondeATipoPago(configuracionTarjeta, venta.TipoPago);
                datosTarjetaEntity.ConfiguracionTarjetaId = configuracionTarjeta.Id;
                datosTarjetaEntity.NombreTarjeta = configuracionTarjeta.NombreTarjeta;
                datosTarjetaEntity.TipoTarjeta = configuracionTarjeta.TipoTarjeta;
                datosTarjetaEntity.TipoCuota = configuracionTarjeta.TipoCuota;
            }

            if (tipoTarjeta == TipoTarjeta.Credito &&
                datosTarjeta.CantidadCuotas.HasValue &&
                datosTarjeta.ConfiguracionTarjetaId.HasValue)
            {
                var calculado = await CalcularCuotasTarjetaAsync(
                    datosTarjeta.ConfiguracionTarjetaId.Value,
                    venta.Total,
                    datosTarjeta.CantidadCuotas.Value
                );

                datosTarjetaEntity.TasaInteres = calculado.TasaInteres;
                datosTarjetaEntity.MontoCuota = calculado.MontoCuota;
                datosTarjetaEntity.MontoTotalConInteres = calculado.MontoTotalConInteres;
            }

            if (tipoTarjeta == TipoTarjeta.Debito &&
                configuracionTarjeta is
                {
                    TieneRecargoDebito: true,
                    PorcentajeRecargoDebito: > 0m
                })
            {
                var recargo = RedondearMoneda(
                    venta.Total * (configuracionTarjeta.PorcentajeRecargoDebito.Value / 100m));

                datosTarjetaEntity.RecargoAplicado = recargo;
                venta.Total += recargo;
            }

            // Skip global plan if per-item plans already applied on detalles (avoid double adjustment).
            var tieneAjustesPorItem = venta.Detalles.Any(d =>
                !d.IsDeleted && d.ProductoCondicionPagoPlanId != null);

            if (planSeleccionado != null && !tieneAjustesPorItem)
            {
                var montoAjuste = RedondearMoneda(venta.Total * planSeleccionado.AjustePorcentaje / 100m);
                venta.Total += montoAjuste;
                datosTarjetaEntity.PorcentajeAjustePlanAplicado = planSeleccionado.AjustePorcentaje;
                datosTarjetaEntity.MontoAjustePlanAplicado = montoAjuste;
            }

            if (planGlobal != null)
            {
                AplicarAjustePagoGlobal(venta, datosTarjetaEntity, planGlobal);
            }
        }

        private static void LimpiarSnapshotDatosTarjeta(DatosTarjeta datosTarjeta)
        {
            datosTarjeta.TipoCuota = null;
            datosTarjeta.TasaInteres = null;
            datosTarjeta.MontoCuota = null;
            datosTarjeta.MontoTotalConInteres = null;
            datosTarjeta.RecargoAplicado = null;
            datosTarjeta.PorcentajeAjustePlanAplicado = null;
            datosTarjeta.MontoAjustePlanAplicado = null;
            datosTarjeta.PorcentajeAjustePagoAplicado = null;
            datosTarjeta.MontoAjustePagoAplicado = null;
            datosTarjeta.NombrePlanPagoSnapshot = null;
        }

        private async Task<ConfiguracionPagoPlan?> ValidarYObtenerPlanPagoGlobalAsync(
            int? planId,
            TipoPago tipoPagoVenta,
            int? configuracionTarjetaId)
        {
            if (!TiposPagoConPlanes.Contains(tipoPagoVenta))
            {
                if (planId.HasValue)
                    throw new InvalidOperationException("El medio de pago elegido no admite plan global.");

                return null;
            }

            if (!planId.HasValue)
            {
                if (await ExistenPlanesGlobalesActivosAsync(tipoPagoVenta, configuracionTarjetaId))
                    throw new InvalidOperationException("Debe seleccionar un plan de pago para el medio elegido.");

                return null;
            }

            var plan = await _context.ConfiguracionPagoPlanes
                .Include(p => p.ConfiguracionPago)
                .Include(p => p.ConfiguracionTarjeta)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId.Value && !p.IsDeleted);

            if (plan == null)
                throw new InvalidOperationException("El plan global de pago seleccionado no existe.");

            if (!plan.Activo)
                throw new InvalidOperationException("El plan global de pago seleccionado no esta disponible.");

            if (plan.ConfiguracionPago == null || plan.ConfiguracionPago.IsDeleted || !plan.ConfiguracionPago.Activo)
                throw new InvalidOperationException("El medio de pago del plan global no esta disponible.");

            if (plan.TipoPago != tipoPagoVenta || plan.ConfiguracionPago.TipoPago != tipoPagoVenta)
                throw new InvalidOperationException("El plan global seleccionado no corresponde al medio de pago elegido.");

            if (plan.ConfiguracionTarjetaId.HasValue)
            {
                if (!configuracionTarjetaId.HasValue || plan.ConfiguracionTarjetaId.Value != configuracionTarjetaId.Value)
                    throw new InvalidOperationException("El plan global seleccionado no corresponde a la tarjeta elegida.");

                if (plan.ConfiguracionTarjeta == null || plan.ConfiguracionTarjeta.IsDeleted || !plan.ConfiguracionTarjeta.Activa)
                    throw new InvalidOperationException("La tarjeta del plan global seleccionado no esta disponible.");

                ValidarTarjetaCorrespondeATipoPago(plan.ConfiguracionTarjeta, tipoPagoVenta);
            }

            return plan;
        }

        private async Task<bool> ExistenPlanesGlobalesActivosAsync(TipoPago tipoPagoVenta, int? configuracionTarjetaId)
        {
            var query = _context.ConfiguracionPagoPlanes
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Activo && p.TipoPago == tipoPagoVenta)
                .Where(p => p.ConfiguracionPago.Activo && !p.ConfiguracionPago.IsDeleted);

            if (configuracionTarjetaId.HasValue)
            {
                query = query.Where(p => p.ConfiguracionTarjetaId == null || p.ConfiguracionTarjetaId == configuracionTarjetaId.Value);
            }
            else
            {
                query = query.Where(p => p.ConfiguracionTarjetaId == null);
            }

            return await query.AnyAsync();
        }

        private static void ValidarTarjetaCorrespondeATipoPago(ConfiguracionTarjeta tarjeta, TipoPago tipoPagoVenta)
        {
            var tarjetaEsperada = tipoPagoVenta switch
            {
                TipoPago.TarjetaCredito => TipoTarjeta.Credito,
                TipoPago.TarjetaDebito => TipoTarjeta.Debito,
                _ => (TipoTarjeta?)null
            };

            if (tarjetaEsperada.HasValue && tarjeta.TipoTarjeta != tarjetaEsperada.Value)
                throw new InvalidOperationException("La tarjeta seleccionada no corresponde al medio de pago elegido.");
        }

        // INVARIANTE: este método debe llamarse una sola vez por operación de guardado/actualización.
        // Usa venta.Total como base para el cálculo. Una segunda llamada sin reset previo
        // (CalcularTotales) compoundría el ajuste: e.g. +10% sobre 1000 → 1100, luego +10% sobre 1100 → 1210.
        // En UpdateAsync, CalcularTotales garantiza la base limpia antes de cada llamada.
        private static void AplicarAjustePagoGlobal(
            Venta venta,
            DatosTarjeta datosTarjeta,
            ConfiguracionPagoPlan plan)
        {
            var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
            {
                BaseVenta = venta.Total,
                PorcentajeAjuste = plan.AjustePorcentaje,
                CantidadCuotas = plan.CantidadCuotas,
                MedioActivo = plan.ConfiguracionPago.Activo,
                TarjetaActiva = plan.ConfiguracionTarjeta?.Activa,
                PlanActivo = plan.Activo
            });

            if (!resultado.EsValido)
                throw new InvalidOperationException(resultado.Mensaje ?? "El plan global de pago no es valido.");

            venta.Total = resultado.TotalFinal;
            datosTarjeta.ConfiguracionPagoPlanId = plan.Id;
            datosTarjeta.CantidadCuotas = plan.CantidadCuotas;
            datosTarjeta.PorcentajeAjustePagoAplicado = resultado.PorcentajeAjuste;
            datosTarjeta.MontoAjustePagoAplicado = resultado.MontoAjuste;
            datosTarjeta.MontoCuota = resultado.ValorCuota;
            datosTarjeta.MontoTotalConInteres = resultado.TotalFinal;
            datosTarjeta.NombrePlanPagoSnapshot = CrearNombrePlanPagoSnapshot(plan);
        }

        // Diseñado para llamarse exactamente una vez en UpdateAsync, siempre después de CalcularTotales.
        // Ese orden garantiza que venta.Total es la base limpia de ítems, no un total ya ajustado.
        private async Task AplicarAjustePagoGlobalPersistidoAsync(Venta venta)
        {
            if (venta.DatosTarjeta?.ConfiguracionPagoPlanId is not int planId)
                return;

            var plan = await ValidarYObtenerPlanPagoGlobalAsync(
                planId,
                venta.TipoPago,
                venta.DatosTarjeta.ConfiguracionTarjetaId);

            if (plan == null)
                return;

            AplicarAjustePagoGlobal(venta, venta.DatosTarjeta, plan);
        }

        private async Task SincronizarDatosTarjetaEdicionAsync(Venta venta, VentaViewModel viewModel)
        {
            if (!TipoPagoRequiereDatosTarjeta(viewModel.TipoPago))
            {
                if (venta.DatosTarjeta != null)
                {
                    _context.DatosTarjeta.Remove(venta.DatosTarjeta);
                    venta.DatosTarjeta = null;
                }

                return;
            }

            if (viewModel.DatosTarjeta == null)
            {
                throw new InvalidOperationException(
                    $"El medio de pago '{viewModel.TipoPago}' requiere datos de tarjeta.");
            }

            var datosTarjeta = venta.DatosTarjeta;
            if (datosTarjeta == null)
            {
                datosTarjeta = _mapper.Map<DatosTarjeta>(viewModel.DatosTarjeta);
                datosTarjeta.VentaId = venta.Id;
                venta.DatosTarjeta = datosTarjeta;
                _context.DatosTarjeta.Add(datosTarjeta);
            }
            else
            {
                ActualizarDatosTarjetaDesdeViewModel(datosTarjeta, viewModel.DatosTarjeta);
            }

            await AplicarSnapshotDatosTarjetaAsync(venta, datosTarjeta, viewModel.DatosTarjeta);
        }

        private static bool TipoPagoRequiereDatosTarjeta(TipoPago tipoPago) =>
            tipoPago is TipoPago.TarjetaCredito or TipoPago.TarjetaDebito or TipoPago.MercadoPago;

        private static void ValidarTipoPagoTarjetaNoPermitidoEnVentaNueva(TipoPago tipoPago)
        {
            if (tipoPago == TipoPago.Tarjeta)
            {
                throw new InvalidOperationException(
                    "El medio de pago Tarjeta es historico y ambiguo. Use Tarjeta Credito o Tarjeta Debito.");
            }
        }

        private static void ValidarTipoPagoTarjetaNoPermitidoEnEdicion(
            TipoPago tipoPagoActual,
            TipoPago tipoPagoSolicitado)
        {
            if (tipoPagoSolicitado == TipoPago.Tarjeta && tipoPagoActual != TipoPago.Tarjeta)
            {
                throw new InvalidOperationException(
                    "El medio de pago Tarjeta es historico y ambiguo. Use Tarjeta Credito o Tarjeta Debito.");
            }
        }

        private static void ActualizarDatosTarjetaDesdeViewModel(
            DatosTarjeta destino,
            DatosTarjetaViewModel origen)
        {
            destino.ConfiguracionTarjetaId = origen.ConfiguracionTarjetaId;
            destino.ConfiguracionPagoPlanId = origen.ConfiguracionPagoPlanId;
            destino.NombreTarjeta = origen.NombreTarjeta;
            destino.TipoTarjeta = origen.TipoTarjeta;
            destino.CantidadCuotas = origen.CantidadCuotas;
            destino.ProductoCondicionPagoPlanId = origen.ProductoCondicionPagoPlanId;
            destino.TipoCuota = origen.TipoCuota;
            destino.NumeroAutorizacion = origen.NumeroAutorizacion;
            destino.Observaciones = origen.Observaciones;
        }

        private static string CrearNombrePlanPagoSnapshot(ConfiguracionPagoPlan plan)
        {
            if (!string.IsNullOrWhiteSpace(plan.Etiqueta))
                return plan.Etiqueta.Trim();

            return plan.CantidadCuotas == 1
                ? "1 pago"
                : $"{plan.CantidadCuotas} cuotas";
        }

        private static readonly TipoPago[] TiposPagoConPlanes =
        {
            TipoPago.TarjetaCredito,
            TipoPago.TarjetaDebito,
            TipoPago.MercadoPago
        };

        private async Task<ProductoCondicionPagoPlan> ValidarYObtenerPlanPagoAsync(int planId, TipoPago tipoPagoVenta)
        {
            var plan = await _context.ProductoCondicionPagoPlanes
                .Include(p => p.ProductoCondicionPago)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && !p.IsDeleted);

            if (plan == null)
                throw new InvalidOperationException("El plan de pago seleccionado no existe.");

            if (!plan.Activo)
                throw new InvalidOperationException("El plan de pago seleccionado no está disponible.");

            if (!TiposPagoConPlanes.Contains(plan.ProductoCondicionPago.TipoPago))
                throw new InvalidOperationException("El plan seleccionado no corresponde a un medio de pago de tarjeta.");

            if (plan.ProductoCondicionPago.TipoPago != tipoPagoVenta)
                throw new InvalidOperationException("El plan seleccionado no corresponde al medio de pago elegido.");

            return plan;
        }

        #endregion

        #region Métodos de Cálculo - Crédito Personal

        public async Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(
            int creditoId,
            decimal montoAFinanciar,
            int cuotas,
            DateTime fechaPrimeraCuota)
        {
            var credito = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cuotas)
                .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                          !c.IsDeleted &&
                                          c.Cliente != null &&
                                          !c.Cliente.IsDeleted);

            if (credito == null)
                throw new InvalidOperationException(VentaConstants.ErrorMessages.CREDITO_NO_ENCONTRADO);

            if (credito.Estado != EstadoCredito.Activo && credito.Estado != EstadoCredito.Aprobado)
                throw new InvalidOperationException("El crédito debe estar en estado Activo o Aprobado");

            var creditoDisponible = credito.SaldoPendiente;

            if (montoAFinanciar > creditoDisponible)
                throw new InvalidOperationException(
                    string.Format(VentaConstants.ErrorMessages.CREDITO_INSUFICIENTE,
                        montoAFinanciar, creditoDisponible));

            var tasaDecimal = credito.TasaInteres / 100;

            var montoCuota = _financialService.CalcularCuotaSistemaFrances(
                montoAFinanciar, tasaDecimal, cuotas);
            var totalAPagar = _financialService.CalcularTotalConInteres(
                montoAFinanciar, tasaDecimal, cuotas);

            return GenerarDatosCreditoPersonall(
                credito, montoAFinanciar, cuotas, montoCuota,
                totalAPagar, fechaPrimeraCuota);
        }

        public async Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Credito)
                    .ThenInclude(c => c!.Cliente)
                .Include(v => v.VentaCreditoCuotas.OrderBy(c => c.NumeroCuota))
                .FirstOrDefaultAsync(v => v.Id == ventaId &&
                                          !v.IsDeleted &&
                                          v.CreditoId != null &&
                                          v.Credito != null &&
                                          !v.Credito.IsDeleted &&
                                          v.Credito.Cliente != null &&
                                          !v.Credito.Cliente.IsDeleted);

            if (venta == null || !venta.VentaCreditoCuotas.Any())
                return null;

            var credito = venta.Credito!;
            var totalCuotas = venta.VentaCreditoCuotas.Sum(c => c.Monto);
            var primeraCuota = venta.VentaCreditoCuotas.OrderBy(c => c.NumeroCuota).First();

            var resultado = new DatosCreditoPersonallViewModel
            {
                CreditoId = credito.Id,
                CreditoNumero = credito.Numero,
                CreditoTotalAsignado = credito.MontoAprobado,
                CreditoDisponible = credito.SaldoPendiente + primeraCuota.Saldo,
                MontoAFinanciar = primeraCuota.Saldo,
                CantidadCuotas = venta.VentaCreditoCuotas.Count,
                MontoCuota = primeraCuota.Monto,
                TasaInteresMensual = credito.TasaInteres,
                TotalAPagar = totalCuotas,
                InteresTotal = totalCuotas - primeraCuota.Saldo,
                SaldoRestante = credito.SaldoPendiente,
                FechaPrimeraCuota = primeraCuota.FechaVencimiento,
                Cuotas = venta.VentaCreditoCuotas.Select(c => new VentaCreditoCuotaViewModel
                {
                    NumeroCuota = c.NumeroCuota,
                    FechaVencimiento = c.FechaVencimiento,
                    Monto = c.Monto,
                    Saldo = c.Saldo,
                    Pagada = c.Pagada,
                    FechaPago = c.FechaPago
                }).ToList()
            };

            return resultado;
        }

        public async Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto)
        {
            var credito = await _context.Creditos
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == creditoId &&
                                          !c.IsDeleted &&
                                          c.Cliente != null &&
                                          !c.Cliente.IsDeleted);

            if (credito == null || credito.Estado != EstadoCredito.Activo)
                return false;

            return credito.SaldoPendiente >= monto;
        }

        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje)
        {
            return CalcularTotalesInterno(detalles, descuentoGeneral, descuentoEsPorcentaje);
        }

        public async Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje)
        {
            return await CalcularTotalesInternoAsync(detalles, descuentoGeneral, descuentoEsPorcentaje);
        }

        public async Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewConPagoGlobalAsync(
            List<DetalleCalculoVentaRequest> detalles,
            decimal descuentoGeneral,
            bool descuentoEsPorcentaje,
            TipoPago tipoPago,
            int? configuracionTarjetaId,
            int? configuracionPagoPlanId)
        {
            var response = await CalcularTotalesInternoAsync(detalles, descuentoGeneral, descuentoEsPorcentaje);
            var plan = await ValidarYObtenerPlanPagoGlobalAsync(configuracionPagoPlanId, tipoPago, configuracionTarjetaId);

            if (plan == null)
                return response;

            var resultado = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
            {
                BaseVenta = response.Total,
                PorcentajeAjuste = plan.AjustePorcentaje,
                CantidadCuotas = plan.CantidadCuotas,
                MedioActivo = plan.ConfiguracionPago.Activo,
                TarjetaActiva = plan.ConfiguracionTarjeta?.Activa,
                PlanActivo = plan.Activo
            });

            if (!resultado.EsValido)
                throw new InvalidOperationException(resultado.Mensaje ?? "El plan global de pago no es valido.");

            response.AjustePagoGlobalAplicado = resultado.MontoAjuste;
            response.PorcentajeAjustePagoGlobalAplicado = resultado.PorcentajeAjuste;
            response.TotalConAjustePagoGlobal = resultado.TotalFinal;
            response.CantidadCuotasPagoGlobal = resultado.CantidadCuotas;
            response.ValorCuotaPagoGlobal = resultado.ValorCuota;
            response.NombrePlanPagoGlobal = CrearNombrePlanPagoSnapshot(plan);
            response.Total = resultado.TotalFinal;

            return response;
        }

        #endregion

        #region Métodos Auxiliares - Cheques

        public async Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);
            if (venta == null)
                return false;

            var yaExiste = await _context.DatosCheque
                .AnyAsync(d => d.VentaId == ventaId && !d.IsDeleted);
            if (yaExiste)
                return false;

            var datosChequeEntity = _mapper.Map<DatosCheque>(datosCheque);
            datosChequeEntity.VentaId = ventaId;

            _context.DatosCheque.Add(datosChequeEntity);
            await _context.SaveChangesAsync();

            return true;
        }

        #endregion

        #region Métodos Privados - Helpers

        private bool PuedeDelegarVendedor()
        {
            return _currentUserService.IsInRole(Roles.SuperAdmin) ||
                   _currentUserService.IsInRole(Roles.Administrador) ||
                   _currentUserService.IsInRole(Roles.Gerente);
        }

        private async Task<string?> ObtenerUserIdActualAsync()
        {
            var userId = _currentUserService.GetUserId();

            // Fallback: si no hay claim de ID, buscar por username en DB
            if (userId == "system")
            {
                var userName = _currentUserService.GetUsername();
                if (userName != "Sistema")
                {
                    userId = await _context.Users
                        .AsNoTracking()
                        .Where(u => u.UserName == userName)
                        .Select(u => u.Id)
                        .FirstOrDefaultAsync();
                }
            }

            return userId;
        }

        private async Task<(string? UserId, string Nombre)> ResolverVendedorAsync(
            VentaViewModel viewModel,
            string? currentUserId,
            string currentUserName)
        {
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                currentUserId = await ObtenerUserIdActualAsync();
            }

            var puedeDelegar = PuedeDelegarVendedor();
            var vendedorSeleccionadoId = viewModel.VendedorUserId;

            if (!puedeDelegar ||
                string.IsNullOrWhiteSpace(vendedorSeleccionadoId) ||
                vendedorSeleccionadoId == currentUserId)
            {
                return (currentUserId, currentUserName);
            }

            var vendedor = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == vendedorSeleccionadoId);

            if (vendedor == null)
            {
                throw new InvalidOperationException("El vendedor seleccionado no existe.");
            }

            var esVendedor = await (
                from userRole in _context.UserRoles
                join role in _context.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == vendedorSeleccionadoId && role.Name == Roles.Vendedor
                select userRole).AnyAsync();

            if (!esVendedor)
            {
                throw new InvalidOperationException("El usuario seleccionado no tiene el rol de vendedor.");
            }

            var nombre = !string.IsNullOrWhiteSpace(vendedor.UserName)
                ? vendedor.UserName
                : vendedor.Email ?? "Sin asignar";

            return (vendedor.Id, nombre);
        }

        private IQueryable<Venta> AplicarFiltros(IQueryable<Venta> query, VentaFilterViewModel? filter)
        {
            if (filter == null)
                return query;

            if (filter.ClienteId.HasValue)
                query = query.Where(v => v.ClienteId == filter.ClienteId.Value);

            if (!string.IsNullOrEmpty(filter.Numero))
                query = query.Where(v => v.Numero.Contains(filter.Numero));

            if (filter.FechaDesde.HasValue)
                query = query.Where(v => v.FechaVenta >= filter.FechaDesde.Value);

            if (filter.FechaHasta.HasValue)
                query = query.Where(v => v.FechaVenta <= filter.FechaHasta.Value);

            if (filter.Estado.HasValue)
                query = query.Where(v => v.Estado == filter.Estado.Value);

            if (filter.TipoPago.HasValue)
                query = query.Where(v => v.TipoPago == filter.TipoPago.Value);

            if (filter.EstadoAutorizacion.HasValue)
                query = query.Where(v => v.EstadoAutorizacion == filter.EstadoAutorizacion.Value);

            return query;
        }

        private async Task<Venta?> CargarVentaCompleta(int id)
        {
            return await _context.Ventas
                .Include(v => v.Detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)).ThenInclude(d => d.Producto)
                .Include(v => v.DatosTarjeta)
                .Include(v => v.Credito)
                .Include(v => v.Cliente)
                .Include(v => v.VentaCreditoCuotas)
                .FirstOrDefaultAsync(v =>
                    v.Id == id &&
                    !v.IsDeleted &&
                    (v.Cliente == null || !v.Cliente.IsDeleted) &&
                    (v.Credito == null || (!v.Credito.IsDeleted && v.Credito.Cliente != null && !v.Credito.Cliente.IsDeleted)));
        }

        private async Task<Venta?> ObtenerVentaPendienteAutorizacionAsync(int id)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
            if (venta == null)
                return null;

            _validator.ValidarEstadoAutorizacion(venta, EstadoAutorizacionVenta.PendienteAutorizacion);
            return venta;
        }

        /// <summary>
        /// Aplica ajustes de plan por ítem (Fase 16.4).
        /// Valida cada plan referenciado en VentaDetalle, calcula el ajuste sobre SubtotalFinal de la línea
        /// y acumula el total en Venta.Total.
        /// Si ningún ítem tiene plan, retorna sin modificar nada (comportamiento legacy preservado).
        /// CréditoPersonal ignora planes por ítem según regla de negocio.
        /// </summary>
        private async Task AplicarAjustesPorItemAsync(Venta venta)
        {
            var detalles = venta.Detalles.Where(d => !d.IsDeleted).ToList();

            if (!detalles.Any(d => d.ProductoCondicionPagoPlanId.HasValue))
                return;

            var planIds = detalles
                .Where(d => d.ProductoCondicionPagoPlanId.HasValue)
                .Select(d => d.ProductoCondicionPagoPlanId!.Value)
                .Distinct()
                .ToList();

            var planes = await _context.ProductoCondicionPagoPlanes
                .Include(p => p.ProductoCondicionPago)
                .AsNoTracking()
                .Where(p => planIds.Contains(p.Id) && !p.IsDeleted)
                .ToDictionaryAsync(p => p.Id);

            // Paso 1: validar cada plan y asignar el porcentaje correspondiente
            foreach (var detalle in detalles)
            {
                var tipoPagoItem = detalle.TipoPago ?? venta.TipoPago;

                // CréditoPersonal nunca aplica ajuste por plan
                if (tipoPagoItem == TipoPago.CreditoPersonal)
                {
                    detalle.ProductoCondicionPagoPlanId = null;
                    detalle.PorcentajeAjustePlanAplicado = null;
                    detalle.MontoAjustePlanAplicado = null;
                    continue;
                }

                if (!detalle.ProductoCondicionPagoPlanId.HasValue)
                {
                    detalle.PorcentajeAjustePlanAplicado = null;
                    detalle.MontoAjustePlanAplicado = null;
                    continue;
                }

                var planId = detalle.ProductoCondicionPagoPlanId.Value;

                if (!planes.TryGetValue(planId, out var plan))
                    throw new InvalidOperationException(
                        $"El plan de pago #{planId} seleccionado para el producto #{detalle.ProductoId} no existe.");

                if (!plan.Activo)
                    throw new InvalidOperationException(
                        $"El plan de pago #{planId} seleccionado para el producto #{detalle.ProductoId} no está disponible.");

                if (plan.ProductoCondicionPago.ProductoId != detalle.ProductoId)
                    throw new InvalidOperationException(
                        $"El plan #{planId} no corresponde al producto #{detalle.ProductoId}.");

                var tipoPagoPlan = plan.ProductoCondicionPago.TipoPago;
                if (tipoPagoPlan != tipoPagoItem)
                    throw new InvalidOperationException(
                        $"El plan #{planId} no corresponde al medio de pago del ítem " +
                        $"(ítem: {tipoPagoItem}, plan: {tipoPagoPlan}).");

                if (!TiposPagoConPlanes.Contains(tipoPagoPlan))
                    throw new InvalidOperationException(
                        $"El plan #{planId} no corresponde a un medio de pago que admita planes.");

                detalle.PorcentajeAjustePlanAplicado = plan.AjustePorcentaje;
            }

            // Paso 2: agrupar por porcentaje y aplicar el ajuste una vez por grupo,
            // luego prorratear MontoAjustePlanAplicado a cada ítem del grupo.
            // Esto evita diferencias de centavos por redondeo acumulado línea a línea.
            var detallesConAjuste = detalles
                .Where(d => d.PorcentajeAjustePlanAplicado.HasValue)
                .ToList();

            decimal totalAjuste = 0m;

            foreach (var grupo in detallesConAjuste.GroupBy(d => d.PorcentajeAjustePlanAplicado!.Value))
            {
                var itemsGrupo = grupo.ToList();
                var subtotalGrupo = itemsGrupo.Sum(d => d.SubtotalFinal);
                var ajusteGrupo = RedondearMoneda(subtotalGrupo * grupo.Key / 100m);
                ProrratearAjusteGrupoEnDetalles(itemsGrupo, ajusteGrupo);
                totalAjuste += ajusteGrupo;
            }

            venta.Total += totalAjuste;
        }

        /// <summary>
        /// Distribuye un ajuste de grupo entre los ítems usando el método de resto mayor
        /// para garantizar que la suma de MontoAjustePlanAplicado coincida con ajusteGrupo.
        /// </summary>
        private static void ProrratearAjusteGrupoEnDetalles(List<VentaDetalle> detalles, decimal ajusteGrupo)
        {
            if (detalles.Count == 1)
            {
                detalles[0].MontoAjustePlanAplicado = ajusteGrupo;
                return;
            }

            var totalSubtotal = detalles.Sum(d => d.SubtotalFinal);

            if (totalSubtotal == 0m)
            {
                foreach (var d in detalles) d.MontoAjustePlanAplicado = 0m;
                return;
            }

            foreach (var detalle in detalles)
                detalle.MontoAjustePlanAplicado = RedondearMoneda(ajusteGrupo * detalle.SubtotalFinal / totalSubtotal);

            // Ajustar diferencia de centavos al ítem de mayor subtotal (método de resto mayor)
            var diferencia = RedondearMoneda(ajusteGrupo - detalles.Sum(d => d.MontoAjustePlanAplicado!.Value));
            if (diferencia != 0m)
            {
                var mayor = detalles.OrderByDescending(d => d.SubtotalFinal).First();
                mayor.MontoAjustePlanAplicado = RedondearMoneda(mayor.MontoAjustePlanAplicado!.Value + diferencia);
            }
        }

        #region Trazabilidad individual (Fase 8.2.E)

        private async Task ValidarUnidadesTrazablesAsync(Venta venta)
        {
            var detallesActivos = venta.Detalles.Where(d => !d.IsDeleted).ToList();

            var unidadesInformadas = detallesActivos
                .Where(d => d.ProductoUnidadId.HasValue)
                .Select(d => d.ProductoUnidadId!.Value)
                .ToList();

            var duplicadas = unidadesInformadas
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicadas.Any())
                throw new InvalidOperationException(
                    $"La venta contiene unidades duplicadas en distintas líneas: {string.Join(", ", duplicadas)}.");

            foreach (var detalle in detallesActivos)
            {
                var producto = detalle.Producto;
                if (producto == null)
                    continue;

                if (producto.RequiereNumeroSerie)
                {
                    if (!detalle.ProductoUnidadId.HasValue)
                        throw new InvalidOperationException(
                            $"El producto '{producto.Nombre}' requiere selección de unidad individual (número de serie).");

                    var unidad = await _context.ProductoUnidades
                        .FirstOrDefaultAsync(u => u.Id == detalle.ProductoUnidadId.Value && !u.IsDeleted);

                    if (unidad == null)
                        throw new InvalidOperationException(
                            $"La unidad {detalle.ProductoUnidadId.Value} no existe o está eliminada.");

                    if (unidad.ProductoId != detalle.ProductoId)
                        throw new InvalidOperationException(
                            $"La unidad '{unidad.CodigoInternoUnidad}' no pertenece al producto '{producto.Nombre}'.");

                    if (unidad.Estado != EstadoUnidad.EnStock)
                        throw new InvalidOperationException(
                            $"La unidad '{unidad.CodigoInternoUnidad}' no está disponible (estado: {unidad.Estado}).");

                    if (detalle.Cantidad != 1)
                        throw new InvalidOperationException(
                            $"Para productos trazables, la cantidad por línea debe ser 1. Producto: '{producto.Nombre}'.");
                }
                else
                {
                    if (detalle.ProductoUnidadId.HasValue)
                        throw new InvalidOperationException(
                            $"El producto '{producto.Nombre}' no requiere unidad individual. No debe informar ProductoUnidadId.");
                }
            }
        }

        private async Task MarcarUnidadesVendidasAsync(Venta venta)
        {
            if (_productoUnidadService == null)
                return;

            var usuario = _currentUserService.GetUsername();

            foreach (var detalle in venta.Detalles.Where(d => !d.IsDeleted && d.ProductoUnidadId.HasValue))
            {
                await _productoUnidadService.MarcarVendidaAsync(
                    detalle.ProductoUnidadId!.Value,
                    detalle.Id,
                    venta.ClienteId,
                    usuario);
            }
        }

        private async Task RevertirUnidadesVentaAsync(Venta venta, string motivo)
        {
            if (_productoUnidadService == null)
                return;

            var detalleIds = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => d.Id)
                .ToList();

            if (!detalleIds.Any())
                return;

            var usuario = _currentUserService.GetUsername();

            var unidades = await _context.ProductoUnidades
                .Where(u => u.VentaDetalleId.HasValue
                         && detalleIds.Contains(u.VentaDetalleId.Value)
                         && u.Estado == EstadoUnidad.Vendida
                         && !u.IsDeleted)
                .ToListAsync();

            foreach (var unidad in unidades)
            {
                await _productoUnidadService.RevertirVentaAsync(unidad.Id, motivo, usuario);
            }
        }

        #endregion

        private void CalcularTotales(Venta venta)
        {
            var detallesList = venta.Detalles.Where(d => !d.IsDeleted).ToList();

            foreach (var detalle in detallesList)
            {
                var subtotalDetalle = Math.Max(0, (detalle.PrecioUnitario * detalle.Cantidad) - detalle.Descuento);
                detalle.Subtotal = subtotalDetalle;
                AplicarSnapshotIvaMontos(detalle);
            }

            AplicarProrrateoDescuentoGeneral(detallesList, venta.Descuento);

            venta.Subtotal = detallesList.Sum(d => d.SubtotalFinalNeto);
            venta.IVA = detallesList.Sum(d => d.SubtotalFinalIVA);
            venta.Total = detallesList.Sum(d => d.SubtotalFinal);
        }

        private async Task CalcularComisionesAsync(Venta venta)
        {
            var detalles = venta.Detalles.Where(d => !d.IsDeleted).ToList();
            if (detalles.Count == 0)
            {
                return;
            }

            var productoIds = detalles.Select(d => d.ProductoId).Distinct().ToList();
            var comisionesPorProductoId = await _context.Productos
                .AsNoTracking()
                .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                .Select(p => new { p.Id, p.ComisionPorcentaje })
                .ToDictionaryAsync(p => p.Id, p => p.ComisionPorcentaje);

            foreach (var detalle in detalles)
            {
                comisionesPorProductoId.TryGetValue(detalle.ProductoId, out var porcentaje);
                var baseComision = detalle.SubtotalFinal > 0m ? detalle.SubtotalFinal : detalle.Subtotal;

                detalle.ComisionPorcentajeAplicada = porcentaje;
                detalle.ComisionMonto = Math.Round(
                    baseComision * porcentaje / 100m,
                    2,
                    MidpointRounding.AwayFromZero);
            }
        }

        private CalculoTotalesVentaResponse CalcularTotalesInterno(IEnumerable<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje)
        {
            // Legacy sync fallback: no debe usarse como fuente fiscal para UI/API de venta.
            // El endpoint activo usa CalcularTotalesPreviewAsync y resuelve IVA por producto.
            // Se conserva para compatibilidad interna y pruebas históricas sin acceso async.
            var subtotalConIVA = detalles
                .Select(d => Math.Max(0, (d.PrecioUnitario * d.Cantidad) - d.Descuento))
                .Sum();

            var descuentoCalculado = descuentoEsPorcentaje
                ? subtotalConIVA * (descuentoGeneral / 100)
                : descuentoGeneral;

            var total = Math.Max(0, subtotalConIVA - descuentoCalculado);

            var subtotalSinIVA = RedondearMoneda(total / VentaConstants.IVA_DIVISOR);
            var iva = RedondearMoneda(total - subtotalSinIVA);

            return new CalculoTotalesVentaResponse
            {
                Subtotal = subtotalSinIVA,
                DescuentoGeneralAplicado = descuentoCalculado,
                IVA = iva,
                Total = total
            };
        }

        private async Task<CalculoTotalesVentaResponse> CalcularTotalesInternoAsync(IEnumerable<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje)
        {
            var detallesList = detalles.ToList();
            var productoIds = detallesList
                .Where(d => d.ProductoId > 0)
                .Select(d => d.ProductoId)
                .Distinct()
                .ToList();

            var productos = productoIds.Count == 0
                ? new Dictionary<int, Producto>()
                : await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.AlicuotaIVA)
                    .Include(p => p.Categoria)
                        .ThenInclude(c => c.AlicuotaIVA)
                    .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                    .ToDictionaryAsync(p => p.Id);

            var detallesCalculados = new List<DetalleCalculoTotalesVentaResponse>();

            foreach (var detalle in detallesList)
            {
                var subtotalFinal = RedondearMoneda(Math.Max(0, (detalle.PrecioUnitario * detalle.Cantidad) - detalle.Descuento));
                var ivaSnapshot = ResolverSnapshotIvaPreview(detalle.ProductoId, productos);
                var subtotalNeto = subtotalFinal;
                var subtotalIva = 0m;

                if (ivaSnapshot.Porcentaje > 0m)
                {
                    var divisor = 1m + (ivaSnapshot.Porcentaje / 100m);
                    subtotalNeto = RedondearMoneda(subtotalFinal / divisor);
                    subtotalIva = RedondearMoneda(subtotalFinal - subtotalNeto);
                }

                detallesCalculados.Add(new DetalleCalculoTotalesVentaResponse
                {
                    ProductoId = detalle.ProductoId,
                    PorcentajeIVA = ivaSnapshot.Porcentaje,
                    AlicuotaIVAId = ivaSnapshot.AlicuotaId,
                    AlicuotaIVANombre = ivaSnapshot.AlicuotaNombre,
                    SubtotalNeto = subtotalNeto,
                    SubtotalIVA = subtotalIva,
                    Subtotal = subtotalFinal
                });
            }

            var total = detallesCalculados.Sum(d => d.Subtotal);
            var descuentoCalculado = descuentoEsPorcentaje
                ? RedondearMoneda(total * (descuentoGeneral / 100m))
                : RedondearMoneda(descuentoGeneral);

            descuentoCalculado = AplicarProrrateoDescuentoGeneral(detallesCalculados, descuentoCalculado);

            var totalBase = detallesCalculados.Sum(d => d.SubtotalFinal);
            var response = new CalculoTotalesVentaResponse
            {
                Subtotal = detallesCalculados.Sum(d => d.SubtotalFinalNeto),
                DescuentoGeneralAplicado = descuentoCalculado,
                IVA = detallesCalculados.Sum(d => d.SubtotalFinalIVA),
                Total = totalBase,
                Detalles = detallesCalculados,
                AjusteItemsAplicado = 0m
            };

            return response;
        }

        /// <summary>
        /// Calcula el ajuste agrupado por porcentaje de plan para el preview.
        /// Agrupa ítems con el mismo AjustePorcentaje, aplica el porcentaje una vez
        /// sobre el subtotal del grupo y acumula. CréditoPersonal se ignora.
        /// </summary>
        private async Task<decimal> CalcularAjusteItemsPreviewAsync(
            List<DetalleCalculoVentaRequest> solicitudes,
            List<DetalleCalculoTotalesVentaResponse> calculados)
        {
            var planIds = solicitudes
                .Where(d => d.ProductoCondicionPagoPlanId.HasValue
                    && d.TipoPago != TipoPago.CreditoPersonal)
                .Select(d => d.ProductoCondicionPagoPlanId!.Value)
                .Distinct()
                .ToList();

            if (planIds.Count == 0)
                return 0m;

            var planes = await _context.ProductoCondicionPagoPlanes
                .AsNoTracking()
                .Where(p => planIds.Contains(p.Id) && p.Activo && !p.IsDeleted)
                .Select(p => new { p.Id, p.AjustePorcentaje })
                .ToDictionaryAsync(p => p.Id, p => p.AjustePorcentaje);

            // Construir grupos: porcentaje → suma de SubtotalFinal de los ítems del grupo
            var grupos = new Dictionary<decimal, decimal>();

            for (var i = 0; i < solicitudes.Count && i < calculados.Count; i++)
            {
                var sol = solicitudes[i];
                if (!sol.ProductoCondicionPagoPlanId.HasValue
                    || sol.TipoPago == TipoPago.CreditoPersonal)
                    continue;

                if (!planes.TryGetValue(sol.ProductoCondicionPagoPlanId.Value, out var pct))
                    continue;

                grupos.TryGetValue(pct, out var acum);
                grupos[pct] = acum + calculados[i].SubtotalFinal;
            }

            return grupos.Sum(g => RedondearMoneda(g.Value * g.Key / 100m));
        }

        private (decimal Porcentaje, int? AlicuotaId, string? AlicuotaNombre) ResolverSnapshotIvaPreview(
            int productoId,
            IReadOnlyDictionary<int, Producto> productos)
        {
            if (productoId <= 0 || !productos.TryGetValue(productoId, out var producto))
            {
                _logger.LogWarning(
                    "Preview de totales de venta usando IVA legacy 21%. ProductoId:{ProductoId} no informado o no encontrado.",
                    productoId);
                return (ProductoIvaResolver.PorcentajeDefault, null, "IVA 21% (legacy)");
            }

            return CrearSnapshotIva(producto);
        }

        private static void AplicarSnapshotIvaMontos(VentaDetalle detalle)
        {
            var porcentaje = TieneSnapshotIva(detalle)
                ? detalle.PorcentajeIVA
                : ProductoIvaResolver.PorcentajeDefault;

            detalle.PorcentajeIVA = porcentaje;

            var precioFinal = RedondearMoneda(detalle.PrecioUnitario);
            var subtotalFinal = RedondearMoneda(detalle.Subtotal);

            if (porcentaje <= 0m)
            {
                detalle.PrecioUnitarioNeto = precioFinal;
                detalle.IVAUnitario = 0m;
                detalle.SubtotalNeto = subtotalFinal;
                detalle.SubtotalIVA = 0m;
                return;
            }

            var divisor = 1m + (porcentaje / 100m);
            detalle.PrecioUnitarioNeto = RedondearMoneda(precioFinal / divisor);
            detalle.IVAUnitario = RedondearMoneda(precioFinal - detalle.PrecioUnitarioNeto);
            detalle.SubtotalNeto = RedondearMoneda(subtotalFinal / divisor);
            detalle.SubtotalIVA = RedondearMoneda(subtotalFinal - detalle.SubtotalNeto);
        }

        private static decimal AplicarProrrateoDescuentoGeneral(List<VentaDetalle> detalles, decimal descuentoGeneral)
        {
            var totalBruto = detalles.Sum(d => d.Subtotal);
            var descuento = totalBruto > 0m
                ? RedondearMoneda(Math.Min(Math.Max(0m, descuentoGeneral), totalBruto))
                : 0m;

            if (descuento <= 0m || totalBruto <= 0m)
            {
                foreach (var detalle in detalles)
                {
                    detalle.DescuentoGeneralProrrateado = 0m;
                    detalle.SubtotalFinalNeto = detalle.SubtotalNeto;
                    detalle.SubtotalFinalIVA = detalle.SubtotalIVA;
                    detalle.SubtotalFinal = detalle.Subtotal;
                }

                return 0m;
            }

            foreach (var detalle in detalles)
            {
                detalle.DescuentoGeneralProrrateado = RedondearMoneda(descuento * detalle.Subtotal / totalBruto);
            }

            AjustarDiferenciaProrrateo(
                detalles,
                descuento,
                d => d.Subtotal,
                d => d.DescuentoGeneralProrrateado,
                (d, value) => d.DescuentoGeneralProrrateado = value);

            foreach (var detalle in detalles)
            {
                detalle.SubtotalFinal = RedondearMoneda(Math.Max(0m, detalle.Subtotal - detalle.DescuentoGeneralProrrateado));
                AplicarMontosFinalesIva(detalle);
            }

            return descuento;
        }

        private static decimal AplicarProrrateoDescuentoGeneral(List<DetalleCalculoTotalesVentaResponse> detalles, decimal descuentoGeneral)
        {
            var totalBruto = detalles.Sum(d => d.Subtotal);
            var descuento = totalBruto > 0m
                ? RedondearMoneda(Math.Min(Math.Max(0m, descuentoGeneral), totalBruto))
                : 0m;

            if (descuento <= 0m || totalBruto <= 0m)
            {
                foreach (var detalle in detalles)
                {
                    detalle.DescuentoGeneralProrrateado = 0m;
                    detalle.SubtotalFinalNeto = detalle.SubtotalNeto;
                    detalle.SubtotalFinalIVA = detalle.SubtotalIVA;
                    detalle.SubtotalFinal = detalle.Subtotal;
                }

                return 0m;
            }

            foreach (var detalle in detalles)
            {
                detalle.DescuentoGeneralProrrateado = RedondearMoneda(descuento * detalle.Subtotal / totalBruto);
            }

            AjustarDiferenciaProrrateo(
                detalles,
                descuento,
                d => d.Subtotal,
                d => d.DescuentoGeneralProrrateado,
                (d, value) => d.DescuentoGeneralProrrateado = value);

            foreach (var detalle in detalles)
            {
                detalle.SubtotalFinal = RedondearMoneda(Math.Max(0m, detalle.Subtotal - detalle.DescuentoGeneralProrrateado));
                AplicarMontosFinalesIva(detalle);
            }

            return descuento;
        }

        private static void AplicarMontosFinalesIva(VentaDetalle detalle)
        {
            if (detalle.PorcentajeIVA <= 0m)
            {
                detalle.SubtotalFinalNeto = detalle.SubtotalFinal;
                detalle.SubtotalFinalIVA = 0m;
                return;
            }

            var divisor = 1m + (detalle.PorcentajeIVA / 100m);
            detalle.SubtotalFinalNeto = RedondearMoneda(detalle.SubtotalFinal / divisor);
            detalle.SubtotalFinalIVA = RedondearMoneda(detalle.SubtotalFinal - detalle.SubtotalFinalNeto);
        }

        private static void AplicarMontosFinalesIva(DetalleCalculoTotalesVentaResponse detalle)
        {
            if (detalle.PorcentajeIVA <= 0m)
            {
                detalle.SubtotalFinalNeto = detalle.SubtotalFinal;
                detalle.SubtotalFinalIVA = 0m;
                return;
            }

            var divisor = 1m + (detalle.PorcentajeIVA / 100m);
            detalle.SubtotalFinalNeto = RedondearMoneda(detalle.SubtotalFinal / divisor);
            detalle.SubtotalFinalIVA = RedondearMoneda(detalle.SubtotalFinal - detalle.SubtotalFinalNeto);
        }

        private static void AjustarDiferenciaProrrateo<T>(
            List<T> detalles,
            decimal descuento,
            Func<T, decimal> subtotalSelector,
            Func<T, decimal> descuentoSelector,
            Action<T, decimal> setDescuento)
        {
            if (detalles.Count == 0)
            {
                return;
            }

            var diferencia = RedondearMoneda(descuento - detalles.Sum(descuentoSelector));
            if (diferencia == 0m)
            {
                return;
            }

            var ajuste = detalles
                .OrderByDescending(subtotalSelector)
                .First();

            setDescuento(ajuste, RedondearMoneda(descuentoSelector(ajuste) + diferencia));
        }

        private static bool TieneSnapshotIva(VentaDetalle detalle)
        {
            return detalle.PorcentajeIVA > 0m
                   || detalle.PrecioUnitarioNeto > 0m
                   || detalle.SubtotalNeto > 0m
                   || detalle.SubtotalIVA > 0m
                   || detalle.AlicuotaIVAId.HasValue
                   || !string.IsNullOrWhiteSpace(detalle.AlicuotaIVANombre);
        }

        private static decimal RedondearMoneda(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private void ActualizarDatosVenta(Venta venta, VentaViewModel viewModel)
        {
            _logger.LogDebug(
                "ActualizarDatosVenta venta {Id}. ClienteId:{ClienteId} TipoPago:{TipoPago} CreditoId:{CreditoId} Descuento:{Descuento}",
                venta.Id,
                viewModel.ClienteId,
                viewModel.TipoPago,
                viewModel.CreditoId,
                viewModel.Descuento);

            venta.ClienteId = viewModel.ClienteId;
            venta.FechaVenta = viewModel.FechaVenta;
            venta.TipoPago = viewModel.TipoPago;
            venta.Descuento = viewModel.Descuento;
            venta.Observaciones = viewModel.Observaciones;
            venta.CreditoId = viewModel.CreditoId;
        }

        private void ActualizarDetalles(Venta venta, List<VentaDetalleViewModel> detallesVM)
        {
            var existentes = venta.Detalles.Count(d => !d.IsDeleted);
            _logger.LogDebug(
                "ActualizarDetalles venta {Id}. Existentes:{Existentes} Entrantes:{Entrantes}",
                venta.Id,
                existentes,
                detallesVM.Count);

            foreach (var existente in venta.Detalles.Where(d => !d.IsDeleted))
            {
                existente.IsDeleted = true;
            }

            foreach (var detalleVM in detallesVM)
            {
                var detalle = _mapper.Map<VentaDetalle>(detalleVM);
                NormalizarPagoPorItemLegacy(detalle);
                detalle.VentaId = venta.Id;
                venta.Detalles.Add(detalle);
            }
        }

        private void AgregarDetalles(Venta venta, List<VentaDetalleViewModel> detallesVM)
        {
            foreach (var detalleVM in detallesVM)
            {
                var detalle = _mapper.Map<VentaDetalle>(detalleVM);
                NormalizarPagoPorItemLegacy(detalle);
                detalle.Venta = venta;
                venta.Detalles.Add(detalle);
            }
        }

        private static void NormalizarPagoPorItemLegacy(VentaDetalle detalle)
        {
            detalle.TipoPago = null;
            detalle.ProductoCondicionPagoPlanId = null;
            detalle.PorcentajeAjustePlanAplicado = null;
            detalle.MontoAjustePlanAplicado = null;
        }

        private async Task DescontarStockYRegistrarMovimientos(Venta venta)
        {
            var usuario = _currentUserService.GetUsername();

            var referencia = $"Venta {venta.Numero}";
            var motivo = $"Confirmación de venta - Cliente: {venta.Cliente?.Nombre ?? "(sin cliente)"}";

            var salidas = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                .ToList();
            var costos = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => new MovimientoStockCostoLinea(
                    d.ProductoId,
                    d.Cantidad,
                    referencia,
                    d.CostoUnitarioAlMomento,
                    "VentaDetalleSnapshot"))
                .ToList();

            await _movimientoStockService.RegistrarSalidasAsync(
                salidas,
                motivo,
                usuario,
                costos);
        }

        private async Task DevolverStock(Venta venta, string motivo)
        {
            var usuario = _currentUserService.GetUsername();

            var referencia = $"Cancelación Venta {venta.Numero}";

            var entradas = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                .ToList();
            var costos = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => new MovimientoStockCostoLinea(
                    d.ProductoId,
                    d.Cantidad,
                    referencia,
                    d.CostoUnitarioAlMomento,
                    "VentaDetalleSnapshot"))
                .ToList();

            await _movimientoStockService.RegistrarEntradasAsync(
                entradas,
                motivo,
                usuario,
                costos: costos);
        }

        private async Task AplicarPrecioVigenteADetallesAsync(Venta venta)
        {
            var detalles = venta.Detalles.Where(d => !d.IsDeleted).ToList();
            if (detalles.Count == 0)
            {
                _logger.LogDebug("AplicarPrecioVigenteADetallesAsync venta {Id} sin detalles activos", venta.Id);
                return;
            }

            var productoIds = detalles.Select(d => d.ProductoId).Distinct().ToList();
            var preciosVigentes = await _precioVigenteResolver.ResolverBatchAsync(productoIds);
            _logger.LogDebug(
                "AplicarPrecioVigenteADetallesAsync venta {Id} productos:{Productos}",
                venta.Id,
                productoIds.Count);

            var productos = await _context.Productos
                .AsNoTracking()
                .Include(p => p.AlicuotaIVA)
                .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                .ToDictionaryAsync(p => p.Id);

            var cache = new Dictionary<int, decimal>();
            var ivaCache = new Dictionary<int, (decimal Porcentaje, int? AlicuotaId, string? AlicuotaNombre)>();

            foreach (var detalle in detalles)
            {
                if (!cache.TryGetValue(detalle.ProductoId, out var precioUnitario))
                {
                    precioUnitario = preciosVigentes.TryGetValue(detalle.ProductoId, out var precioVigente)
                        ? precioVigente.PrecioFinalConIva
                        : productos.GetValueOrDefault(detalle.ProductoId)?.PrecioVenta ?? 0m;
                    cache[detalle.ProductoId] = precioUnitario;
                }

                detalle.PrecioUnitario = precioUnitario;

                if (!ivaCache.TryGetValue(detalle.ProductoId, out var ivaSnapshot))
                {
                    productos.TryGetValue(detalle.ProductoId, out var producto);
                    ivaSnapshot = CrearSnapshotIva(producto);
                    ivaCache[detalle.ProductoId] = ivaSnapshot;
                }

                detalle.PorcentajeIVA = ivaSnapshot.Porcentaje;
                detalle.AlicuotaIVAId = ivaSnapshot.AlicuotaId;
                detalle.AlicuotaIVANombre = ivaSnapshot.AlicuotaNombre;

                productos.TryGetValue(detalle.ProductoId, out var productoCosto);
                var costoUnitario = RedondearMoneda(productoCosto?.PrecioCompra ?? 0m);
                detalle.CostoUnitarioAlMomento = costoUnitario;
                detalle.CostoTotalAlMomento = RedondearMoneda(costoUnitario * detalle.Cantidad);
            }
        }

        private static (decimal Porcentaje, int? AlicuotaId, string? AlicuotaNombre) CrearSnapshotIva(Producto? producto)
        {
            if (producto == null)
                return (ProductoIvaResolver.PorcentajeDefault, null, "IVA 21% (legacy)");

            var porcentaje = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

            if (producto.AlicuotaIVA is { Activa: true, IsDeleted: false })
                return (porcentaje, producto.AlicuotaIVAId, producto.AlicuotaIVA.Nombre);

            if (producto.Categoria?.AlicuotaIVA is { Activa: true, IsDeleted: false })
                return (porcentaje, producto.Categoria.AlicuotaIVAId, producto.Categoria.AlicuotaIVA.Nombre);

            return (porcentaje, null, $"IVA {porcentaje:0.##}%");
        }

        private async Task RestaurarCreditoPersonall(Venta venta)
        {
            if (!venta.CreditoId.HasValue || !venta.VentaCreditoCuotas.Any())
                return;

            var credito = venta.Credito ?? await _context.Creditos
                .FirstOrDefaultAsync(c => c.Id == venta.CreditoId!.Value &&
                                          !c.IsDeleted &&
                                          c.Cliente != null &&
                                          !c.Cliente.IsDeleted);
            if (credito == null)
                return;

            var montoFinanciado = venta.VentaCreditoCuotas.First().Saldo;
            credito.SaldoPendiente += montoFinanciado;
            _context.Creditos.Update(credito);

            _context.VentaCreditoCuotas.RemoveRange(venta.VentaCreditoCuotas);

            _logger.LogInformation(
                "Crédito {CreditoId} restaurado por cancelación de venta {VentaId}. Monto: ${Monto}",
                credito.Id, venta.Id, montoFinanciado);
        }

        private async Task GenerarAlertasStockBajo(Venta venta)
        {
            var productoIds = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => d.ProductoId)
                .Distinct()
                .ToList();

            await _alertaStockService.VerificarYGenerarAlertasAsync(productoIds);
        }

        private async Task VerificarAutorizacionSiCorrespondeAsync(Venta venta, VentaViewModel viewModel)
        {
            if (viewModel.TipoPago == TipoPago.CreditoPersonal)
            {
                // Usar el servicio de validación unificado
                var validacion = await _validacionVentaService.ValidarVentaCreditoPersonalAsync(
                    viewModel.ClienteId, 
                    venta.Total, 
                    viewModel.CreditoId);

                venta.RequiereAutorizacion = validacion.RequiereAutorizacion;

                if (venta.RequiereAutorizacion &&
                    venta.EstadoAutorizacion == EstadoAutorizacionVenta.NoRequiere)
                {
                    venta.EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion;
                    venta.FechaSolicitudAutorizacion = DateTime.UtcNow;
                }
            }
        }

        private async Task GuardarDatosAdicionales(int ventaId, VentaViewModel viewModel)
        {
            if (viewModel.DatosTarjeta != null &&
                (viewModel.TipoPago == TipoPago.TarjetaCredito ||
                 viewModel.TipoPago == TipoPago.TarjetaDebito ||
                 viewModel.TipoPago == TipoPago.MercadoPago))
            {
                await GuardarDatosTarjetaAsync(ventaId, viewModel.DatosTarjeta);
            }

            if (viewModel.DatosCheque != null && viewModel.TipoPago == TipoPago.Cheque)
            {
                await GuardarDatosChequeAsync(ventaId, viewModel.DatosCheque);
            }

            // Para crédito personal: guardar plan como JSON, NO crear cuotas todavía
            // Las cuotas se crean solo al confirmar la venta
            if (viewModel.DatosCreditoPersonall != null && viewModel.TipoPago == TipoPago.CreditoPersonal)
            {
                await GuardarPlanCreditoPersonallAsync(ventaId, viewModel.DatosCreditoPersonall);
            }
        }

        /// <summary>
        /// Guarda el plan de crédito personal como JSON para usarlo al confirmar.
        /// NO crea cuotas ni modifica el saldo del crédito.
        /// </summary>
        private async Task GuardarPlanCreditoPersonallAsync(int ventaId, DatosCreditoPersonallViewModel datos)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);
            if (venta == null)
                throw new InvalidOperationException(VentaConstants.ErrorMessages.VENTA_NO_ENCONTRADA);

            // Serializar el plan de crédito para usarlo al confirmar
            venta.DatosCreditoPersonallJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                datos.CreditoId,
                datos.MontoAFinanciar,
                datos.CantidadCuotas,
                datos.MontoCuota,
                datos.TotalAPagar,
                datos.TasaInteresMensual,
                datos.FechaPrimeraCuota,
                datos.InteresTotal
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Plan de crédito personal guardado para venta {VentaId}. CreditoId: {CreditoId}, Monto: {Monto}, Cuotas: {Cuotas}",
                ventaId, datos.CreditoId, datos.MontoAFinanciar, datos.CantidadCuotas);
        }

        /// <summary>
        /// E4: Crea el crédito definitivo, cuotas y descuenta del cupo.
        /// Solo se llama al confirmar una venta autorizada (o que no requiere autorización).
        /// </summary>
        private async Task CrearCreditoDefinitivoDesdeJsonAsync(Venta venta)
        {
            if (string.IsNullOrEmpty(venta.DatosCreditoPersonallJson))
            {
                throw new InvalidOperationException(
                    "No hay datos del plan de crédito para crear el crédito definitivo.");
            }

            try
            {
                var planJson = System.Text.Json.JsonDocument.Parse(venta.DatosCreditoPersonallJson);
                var root = planJson.RootElement;

                var creditoId = root.GetProperty("CreditoId").GetInt32();
                var montoAFinanciar = root.GetProperty("MontoAFinanciar").GetDecimal();
                var cantidadCuotas = root.GetProperty("CantidadCuotas").GetInt32();
                var montoCuota = root.GetProperty("MontoCuota").GetDecimal();
                var fechaPrimeraCuota = root.GetProperty("FechaPrimeraCuota").GetDateTime();

                // Obtener el crédito y validar saldo disponible
                var credito = await _context.Creditos
                    .FirstOrDefaultAsync(c => c.Id == creditoId && !c.IsDeleted);
                
                if (credito == null)
                {
                    throw new InvalidOperationException(VentaConstants.ErrorMessages.CREDITO_NO_ENCONTRADO);
                }

                if (credito.SaldoPendiente < montoAFinanciar)
                {
                    throw new InvalidOperationException(
                        $"Saldo de crédito insuficiente. Disponible: ${credito.SaldoPendiente:N2}, Requerido: ${montoAFinanciar:N2}");
                }

                await ValidarCupoDisponibleEnConfirmacionAsync(venta, montoAFinanciar);

                // E4: Asignar CreditoId a la venta (ahora sí, post-autorización)
                venta.CreditoId = creditoId;

                // Crear las cuotas
                for (int i = 0; i < cantidadCuotas; i++)
                {
                    var cuota = new VentaCreditoCuota
                    {
                        VentaId = venta.Id,
                        CreditoId = creditoId,
                        NumeroCuota = i + 1,
                        FechaVencimiento = fechaPrimeraCuota.AddMonths(i),
                        Monto = montoCuota,
                        Saldo = montoAFinanciar,
                        Pagada = false
                    };
                    _context.VentaCreditoCuotas.Add(cuota);
                }

                // E4: Descontar del cupo del crédito
                credito.SaldoPendiente -= montoAFinanciar;
                _context.Creditos.Update(credito);

                _logger.LogInformation(
                    "E4: Crédito definitivo creado para venta {VentaId}. CreditoId: {CreditoId}, " +
                    "Monto: {Monto:C2}, Cuotas: {Cuotas}, Nuevo saldo disponible: {SaldoDisponible:C2}",
                    venta.Id, creditoId, montoAFinanciar, cantidadCuotas, credito.SaldoPendiente);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Error al deserializar datos de crédito JSON para venta {VentaId}", venta.Id);
                throw new InvalidOperationException("Error al procesar los datos del plan de crédito");
            }
        }

        private async Task AsegurarSnapshotLimiteCreditoAsync(Venta venta)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return;
            }

            if (venta.LimiteAplicado.HasValue)
            {
                return;
            }

            await CapturarSnapshotLimiteCreditoAsync(venta);
        }

        private async Task ValidarCupoDisponibleEnConfirmacionAsync(Venta venta, decimal montoOperacion)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal || montoOperacion <= 0m)
            {
                return;
            }

            try
            {
                var disponible = await _creditoDisponibleService.CalcularDisponibleAsync(venta.ClienteId);

                if (disponible.Limite <= 0m)
                {
                    _logger.LogDebug(
                        "Validación de cupo omitida en venta {VentaId}: límite efectivo no configurado (<= 0).",
                        venta.Id);
                    return;
                }

                if (montoOperacion > disponible.Disponible)
                {
                    throw new InvalidOperationException(
                        $"Cupo de crédito insuficiente para confirmar la venta. " +
                        $"Disponible actual: ${disponible.Disponible:N2}, requerido: ${montoOperacion:N2}. " +
                        $"Fórmula aplicada: Disponible = LímiteEfectivo - SaldoPendienteVigente (saldo de créditos vigentes; no suma cuotas futuras por separado ni mora adicional)."
                    );
                }
            }
            catch (CreditoDisponibleException ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo calcular cupo disponible para validar venta {VentaId}. Se mantiene validación legacy por saldo de crédito asociado.",
                    venta.Id);
            }
        }

        private DatosCreditoPersonallViewModel GenerarDatosCreditoPersonall(
            Credito credito,
            decimal montoAFinanciar,
            int cuotas,
            decimal montoCuota,
            decimal totalAPagar,
            DateTime fechaPrimeraCuota)
        {
            var resultado = new DatosCreditoPersonallViewModel
            {
                CreditoId = credito.Id,
                CreditoNumero = credito.Numero,
                CreditoTotalAsignado = credito.MontoAprobado,
                CreditoDisponible = credito.SaldoPendiente,
                MontoAFinanciar = montoAFinanciar,
                CantidadCuotas = cuotas,
                MontoCuota = montoCuota,
                FechaPrimeraCuota = fechaPrimeraCuota,
                TasaInteresMensual = credito.TasaInteres,
                TotalAPagar = totalAPagar,
                InteresTotal = totalAPagar - montoAFinanciar,
                SaldoRestante = credito.SaldoPendiente - montoAFinanciar,
                Cuotas = new List<VentaCreditoCuotaViewModel>()
            };

            decimal saldoRestante = totalAPagar;
            DateTime fechaVencimiento = fechaPrimeraCuota;

            for (int i = 1; i <= cuotas; i++)
            {
                resultado.Cuotas.Add(new VentaCreditoCuotaViewModel
                {
                    NumeroCuota = i,
                    FechaVencimiento = fechaVencimiento,
                    Monto = montoCuota,
                    Saldo = saldoRestante,
                    Pagada = false
                });

                saldoRestante -= montoCuota;
                fechaVencimiento = fechaVencimiento.AddMonths(1);
            }

            return resultado;
        }

        #endregion

        #region Resolución de Totales

        public async Task<decimal?> GetTotalVentaAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .Include(v => v.Detalles)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);

            if (venta == null)
                return null;

            if (venta.Total > 0)
                return venta.Total;

            var detalles = (venta.Detalles ?? new List<VentaDetalle>())
                .Where(d => !d.IsDeleted)
                .ToList();

            if (detalles.Count == 0)
            {
                // Último recurso: consulta directa si la navegación no trajo datos
                detalles = await _context.VentaDetalles
                    .AsNoTracking()
                    .Where(d => d.VentaId == ventaId && !d.IsDeleted)
                    .ToListAsync();
            }

            if (detalles.Count == 0)
                return 0m;

            var subtotal = detalles.Sum(d =>
                d.Subtotal > 0
                    ? d.Subtotal
                    : Math.Max(0, (d.Cantidad * d.PrecioUnitario) - d.Descuento));

            var subtotalConDescuento = subtotal - venta.Descuento;
            return Math.Max(0m, subtotalConDescuento);
        }

        #endregion

        #region Stock

        public async Task<bool> ValidarStockAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .Include(v => v.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                .FirstOrDefaultAsync(v => v.Id == ventaId);

            if (venta == null)
                return false;

            try
            {
                _validator.ValidarStock(venta);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        #endregion
    }
}
