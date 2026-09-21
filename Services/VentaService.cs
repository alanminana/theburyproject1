using AutoMapper;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
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
        private readonly IRelojComercial _reloj;

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
            IProductoUnidadService? productoUnidadService = null,
            IRelojComercial? reloj = null)
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
            // PUN-ML7: fuente única de "hoy" para el vencimiento de la 1ª cuota cuando no hay
            // Credito.FechaPrimeraCuota configurada. La inyección obligatoria sería preferible, pero
            // ~18 archivos de test de VentaService (muy fuera del alcance de esta corrección)
            // construyen este servicio sin pasar reloj — el fallback es inerte en producción
            // (Program.cs registra IRelojComercial como Singleton; DI siempre lo resuelve) y solo se
            // alcanza ahí.
            _reloj = reloj ?? RelojComercial.Sistema;
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
                .Include(v => v.Envio)
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
                .Include(v => v.Envio)
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

            // Enriquecer código de unidad para detalles trazables (Fase 8.2.S)
            var unidadIds = viewModel.Detalles
                .Where(d => d.ProductoUnidadId.HasValue)
                .Select(d => d.ProductoUnidadId!.Value)
                .ToList();
            if (unidadIds.Any())
            {
                var unidades = await _context.ProductoUnidades
                    .Where(u => unidadIds.Contains(u.Id) && !u.IsDeleted)
                    .Select(u => new { u.Id, u.CodigoInternoUnidad })
                    .ToListAsync();
                foreach (var det in viewModel.Detalles.Where(d => d.ProductoUnidadId.HasValue))
                {
                    var u = unidades.FirstOrDefault(x => x.Id == det.ProductoUnidadId!.Value);
                    if (u != null) det.ProductoUnidadCodigoInterno = u.CodigoInternoUnidad;
                }
            }

            // Mapear estado del crédito para control de flujo en la vista
            if (venta.Credito != null)
            {
                viewModel.CreditoEstado = venta.Credito.Estado;
            }

            // VENTA-CREDITO-DATOS-HYDRATION: autoridad vigente es Credito (no
            // venta.VentaCreditoCuotas, tabla legacy sin filas en producción — ver
            // VENTA-DETAILS-H2-AUDIT). El gate ya no exige cuotas generadas: un crédito
            // Configurado sin cuotas todavía tiene datos de plan válidos en Credito. Se
            // reutilizan CreditoConfigurado/CreditoGenerado (ya calculados arriba desde
            // CreditoEstado + FechaConfiguracionCredito) en vez de re-derivar el mismo
            // criterio acá: un crédito PendienteConfiguracion NO debe hidratar datos,
            // aunque Credito ya exista (MontoAprobado viene pre-cargado desde la venta
            // antes de configurarse — ver Credito.AnticipoPreseleccionado).
            if (viewModel.CreditoConfigurado || viewModel.CreditoGenerado)
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

            IDbContextTransaction? transaction = null;

            try
            {
                var venta = _mapper.Map<Venta>(viewModel);
                venta.AperturaCajaId = aperturaActiva.Id;

                var vendedorResuelto = await ResolverVendedorAsync(viewModel, currentUserId, currentUserName);
                venta.VendedorUserId = vendedorResuelto.UserId;
                venta.VendedorNombre = vendedorResuelto.Nombre;

                // Enforcement "vendedor vende en su caja": el vendedor debe pertenecer al padrón
                // de la caja de la apertura activa (bypass admin; estricto: sin padrón ⇒ solo admin).
                await ValidarVendedorHabilitadoEnCajaAsync(venta.VendedorUserId, aperturaActiva.CajaId);

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

                    // E2: Si NoViable, rechazar guardado completamente (salvo excepción aplicable).
                    AplicarExcepcionDocumentalSiCorresponde(
                        validacion,
                        viewModel.AplicarExcepcionDocumental,
                        viewModel.MotivoExcepcionDocumentalCreate,
                        currentUserName);

                    await AplicarResultadoValidacionAsync(venta, validacion, currentUserName);
                }
                else
                {
                    await VerificarAutorizacionSiCorrespondeAsync(venta, viewModel);
                }

                transaction = await _context.Database.BeginTransactionAsync();

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
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }

                _logger.LogError(ex, "Error al crear venta");
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
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

            var config = await _context.ClientesCreditoConfiguraciones
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteId == cliente.Id);

            // Eje único: el cupo lo gobierna el puntaje interno de comportamiento (0–5),
            // con override manual opcional. Snapshot del puntaje que definió el cupo al momento de la venta.
            var nivelFinal = PuntajeCreditoEfectivo.Resolver(cliente.PuntajeCliente, config?.NivelCreditoManual);
            venta.PuntajeAlMomento = nivelFinal;

            PuntajeCreditoLimite? preset = null;
            decimal? limiteOverride = null;
            decimal excepcionDelta = 0m;

            if (config?.NivelCreditoManual.HasValue == true)
            {
                preset = await _context.PuntajesCreditoLimite
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Puntaje == config.NivelCreditoManual.Value && p.Activo);
            }
            else
            {
                if (config?.CreditoPresetId.HasValue == true)
                {
                    preset = await _context.PuntajesCreditoLimite
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == config.CreditoPresetId.Value && p.Activo);
                }

                preset ??= await _context.PuntajesCreditoLimite
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Puntaje == cliente.PuntajeCliente && p.Activo);

                limiteOverride = config?.LimiteOverride ?? cliente.LimiteCredito;
                excepcionDelta = ObtenerExcepcionDeltaVigente(config, DateTime.UtcNow);
            }

            var limiteBase = preset?.LimiteMonto ?? 0m;
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
        // VENTA-COTIZACION-REWORK-03: pública (antes privada) para que CotizacionConversionService
        // la reutilice vía IVentaService en vez de duplicar esta lógica — ver XML doc en
        // IVentaService.AplicarResultadoValidacionAsync.
        public async Task AplicarResultadoValidacionAsync(
            Venta venta,
            ValidacionVentaResult validacion,
            string usuarioActual)
        {
            // Seguridad: NoViable nunca debería llegar aquí, pero por si acaso
            if (validacion.NoViable)
            {
                throw new InvalidOperationException(
                    $"Error interno: Se intentó aplicar validación NoViable. {validacion.MensajeResumen}");
            }

            if (validacion.ExcepcionDocumentalAutorizada)
            {
                var fechaAutorizacion = DateTime.UtcNow;
                venta.RequiereAutorizacion = true;
                venta.Estado = EstadoVenta.PendienteFinanciacion;
                venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
                venta.UsuarioAutoriza = usuarioActual;
                venta.FechaAutorizacion = fechaAutorizacion;
                venta.MotivoAutorizacion =
                    $"EXCEPCION_DOC|{fechaAutorizacion:O}|{usuarioActual}|{validacion.MotivoExcepcionDocumentalAutorizada}";
                // PUN-ML10-F: se agrega Unidad/MontoAsociado/DiasAsociado al snapshot de auditoría
                // (aditivo — nadie deserializa este JSON de vuelta a un tipo fuerte hoy) para que el
                // registro conserve con qué unidad se mostró/decidió la razón, sin perder Tipo.
                venta.RazonesAutorizacionJson = System.Text.Json.JsonSerializer.Serialize(
                    validacion.RazonesAutorizacion.Select(r => new
                    {
                        r.Tipo,
                        r.Descripcion,
                        r.DetalleAdicional,
                        r.Unidad,
                        r.MontoAsociado,
                        r.DiasAsociado
                    }));

                _logger.LogWarning(
                    "Venta autorizada al registrar excepción documental. Usuario:{Usuario} Razones:{Razones}",
                    usuarioActual,
                    validacion.MensajeResumen);
            }
            else if (validacion.RequiereAutorizacion)
            {
                // E2: Guardar venta en estado PendienteAutorizacion con razones persistidas
                venta.RequiereAutorizacion = true;
                venta.Estado = EstadoVenta.PendienteFinanciacion; // También PendienteFinanciacion
                venta.EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion;
                venta.FechaSolicitudAutorizacion = DateTime.UtcNow;
                // PUN-ML10-F: se agrega Unidad/MontoAsociado/DiasAsociado al snapshot de auditoría
                // (aditivo — nadie deserializa este JSON de vuelta a un tipo fuerte hoy) para que el
                // registro conserve con qué unidad se mostró/decidió la razón, sin perder Tipo.
                venta.RazonesAutorizacionJson = System.Text.Json.JsonSerializer.Serialize(
                    validacion.RazonesAutorizacion.Select(r => new
                    {
                        r.Tipo,
                        r.Descripcion,
                        r.DetalleAdicional,
                        r.Unidad,
                        r.MontoAsociado,
                        r.DiasAsociado
                    }));

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

        // VENTA-COTIZACION-EXCEPCION-01: desacoplada de VentaViewModel (antes recibía el
        // viewModel completo) para que CotizacionConversionService pueda invocar la MISMA
        // regla desde CotizacionConversionRequest sin depender del modelo de Venta ni
        // duplicar esta decisión — ver AplicarExcepcionDocumentalSiCorresponde más abajo.
        private bool PuedeAplicarExcepcionDocumental(
            bool aplicarExcepcionDocumental,
            string? motivoExcepcionDocumental,
            ValidacionVentaResult validacion)
        {
            if (!aplicarExcepcionDocumental)
            {
                _logger.LogWarning("Excepción documental: AplicarExcepcionDocumental={Valor}", aplicarExcepcionDocumental);
                return false;
            }

            if (string.IsNullOrWhiteSpace(motivoExcepcionDocumental))
            {
                _logger.LogWarning("Excepción documental: MotivoExcepcionDocumentalCreate vacío");
                return false;
            }

            // La excepción es una autorización definitiva, por lo que requiere el mismo
            // permiso que autorizar una venta. El permiso requestexception ya no alcanza.
            var tieneAutorizar = _currentUserService.HasPermission("ventas", "authorize");
            _logger.LogWarning(
                "Excepción documental: usuario={Usuario} ventas.authorize={Autorizar}",
                _currentUserService.GetUsername(), tieneAutorizar);

            if (!tieneAutorizar)
            {
                _logger.LogWarning("Excepción documental: usuario sin ventas.authorize");
                return false;
            }

            var tipos = validacion.RequisitosPendientes.Select(r => r.Tipo.ToString()).ToList();
            _logger.LogWarning("Excepción: RequisitosPendientes tipos=[{Tipos}]", string.Join(", ", tipos));

            // Tipos excepcionables: documentacion faltante y decisiones crediticias
            // autorizables (sin limite o exceso de cupo por puntaje).
            var todosPermitidos = validacion.RequisitosPendientes.Any()
                                  && validacion.RequisitosPendientes.All(EsRequisitoExcepcionableEnCreate);

            _logger.LogWarning("Excepción: TodosPermitidos={Resultado}", todosPermitidos);

            return todosPermitidos;
        }

        private static bool EsRequisitoExcepcionableEnCreate(RequisitoPendiente requisito)
        {
            return requisito.Tipo == TipoRequisitoPendiente.DocumentacionFaltante
                   || requisito.Tipo == TipoRequisitoPendiente.SinLimiteCredito
                   || EsClienteNoAptoPorCupoAutorizable(requisito);
        }

        private static bool EsClienteNoAptoPorCupoAutorizable(RequisitoPendiente requisito)
        {
            if (requisito.Tipo != TipoRequisitoPendiente.ClienteNoApto ||
                string.IsNullOrWhiteSpace(requisito.Descripcion))
            {
                return false;
            }

            var descripcion = requisito.Descripcion.ToLower(CultureInfo.InvariantCulture);
            var mencionaCreditoDisponible = descripcion.Contains("credito disponible") ||
                                             descripcion.Contains("crédito disponible");
            var mencionaCupo = descripcion.Contains("cupo");

            return descripcion.Contains("excede") && (mencionaCreditoDisponible || mencionaCupo)
                   || descripcion.Contains("cupo insuficiente");
        }

        /// <summary>
        /// Convierte los requisitos excepcionados en una autorización registrada en el
        /// mismo acto. No debe volver a pedirse aprobación desde el detalle de la venta.
        /// </summary>
        private static void AplicarExcepcionDocumentalComoAutorizada(
            ValidacionVentaResult validacion,
            string motivoExcepcion)
        {
            var requisitosExcepcionados = validacion.RequisitosPendientes
                .Where(EsRequisitoExcepcionableEnCreate)
                .ToList();

            validacion.NoViable = false;
            validacion.PendienteRequisitos = false;
            validacion.RequiereAutorizacion = false;
            validacion.ExcepcionDocumentalAutorizada = true;
            validacion.MotivoExcepcionDocumentalAutorizada = motivoExcepcion;
            validacion.RequisitosPendientes = validacion.RequisitosPendientes
                .Where(r => !EsRequisitoExcepcionableEnCreate(r))
                .ToList();

            foreach (var requisito in requisitosExcepcionados)
            {
                validacion.RazonesAutorizacion.Add(new RazonAutorizacion
                {
                    Tipo = MapearTipoRazonExcepcionDocumentalCreate(requisito),
                    Descripcion = requisito.Descripcion,
                    DetalleAdicional = $"Excepción solicitada en creación: {motivoExcepcion}"
                });
            }
        }

        private static TipoRazonAutorizacion MapearTipoRazonExcepcionDocumentalCreate(RequisitoPendiente requisito)
        {
            return requisito.Tipo switch
            {
                TipoRequisitoPendiente.DocumentacionFaltante => TipoRazonAutorizacion.DocumentacionVencida,
                _ => TipoRazonAutorizacion.ExcedeCupo
            };
        }

        /// <summary>
        /// VENTA-COTIZACION-EXCEPCION-01: misma decisión que CreateAsync tomaba inline (antes de
        /// esta extracción) al encontrar <c>validacion.NoViable</c> — expuesta en <see
        /// cref="IVentaService"/> para que CotizacionConversionService la reutilice al convertir
        /// una cotización con Crédito personal, en vez de reimplementar el gate de permiso
        /// (ventas.authorize), el alcance excepcionable (documentación/cupo, nunca mora) o el
        /// mensaje de rechazo. Si <paramref name="validacion"/> no es NoViable, no hace nada.
        /// Si lo es y la excepción no corresponde (falta el permiso, falta el motivo, o hay algún
        /// requisito pendiente que no es excepcionable), lanza InvalidOperationException con el
        /// mismo mensaje que ya usa CreateAsync — el caller decide qué hacer con eso (CreateAsync
        /// deja que se propague; CotizacionConversionService la traduce a un resultado Fallido).
        /// </summary>
        public void AplicarExcepcionDocumentalSiCorresponde(
            ValidacionVentaResult validacion,
            bool aplicarExcepcionDocumental,
            string? motivoExcepcionDocumental,
            string usuarioActual)
        {
            if (!validacion.NoViable)
            {
                return;
            }

            if (PuedeAplicarExcepcionDocumental(aplicarExcepcionDocumental, motivoExcepcionDocumental, validacion))
            {
                var motivoExcepcion = motivoExcepcionDocumental!.Trim();
                AplicarExcepcionDocumentalComoAutorizada(validacion, motivoExcepcion);

                _logger.LogWarning(
                    "Venta autorizada por excepción documental. Usuario:{Usuario}",
                    usuarioActual);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No es posible crear la venta con crédito personal. {validacion.MensajeResumen}");
            }
        }

        /// <summary>
        /// Crea el crédito para una venta con CreditoPersonal después de que la venta fue guardada.
        /// VENTA-COTIZACION-REWORK-03: pública (antes privada) — ver XML doc en
        /// IVentaService.CrearCreditoPendienteParaVentaAsync.
        /// </summary>
        public async Task CrearCreditoPendienteParaVentaAsync(Venta venta)
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

            // Si la venta viene de una cotización donde ya se había elegido una cantidad
            // de cuotas y/o un anticipo, precargarlos acá para no perderlos al llegar a
            // ConfigurarVenta (CreditoController.ConfigurarVenta ya usa credito.CantidadCuotas y
            // credito.AnticipoPreseleccionado como defaults). Es intención, no autoridad: se
            // recalcula todo server-side al confirmar el crédito.
            var intencionCotizacion = venta.CotizacionOrigenId.HasValue
                ? await _context.Cotizaciones
                    .Where(c => c.Id == venta.CotizacionOrigenId.Value)
                    .Select(c => new { c.CantidadCuotasSeleccionada, c.Anticipo })
                    .FirstOrDefaultAsync()
                : null;

            var credito = new Credito
            {
                ClienteId = venta.ClienteId,
                Numero = numeroCredito,
                MontoSolicitado = venta.Total,
                MontoAprobado = venta.Total,
                SaldoPendiente = venta.Total,
                TasaInteres = 0, // Se configurará después
                CantidadCuotas = intencionCotizacion?.CantidadCuotasSeleccionada.GetValueOrDefault() ?? 0, // 0 si no viene de cotización
                AnticipoPreseleccionado = intencionCotizacion?.Anticipo ?? 0m,
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
                "Crédito {NumeroCredito} (PendienteConfiguracion) creado y asociado a venta {VentaId}. CuotasPreseleccionadas:{Cuotas} AnticipoPreseleccionado:{Anticipo}",
                numeroCredito, venta.Id, credito.CantidadCuotas, credito.AnticipoPreseleccionado);
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
                .Include(v => v.Envio)
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

            // VENTA-DESCUENTO-LINEA-LEGACY-EDIT-GUARD: corre antes de cualquier operación que
            // toque venta.Detalles (ActualizarDetalles hace soft-delete + recreate total, sin
            // match línea-a-línea). Si bloquea, todavía no se mutó nada.
            ValidarSinDescuentosLegacyAmbiguos(venta);

            if (viewModel.RowVersion == null || viewModel.RowVersion.Length == 0)
                throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la venta e intentá nuevamente.");

            // Diagnóstico de concurrencia: venta.RowVersion es el valor recién leído de la
            // base (línea 706-709, en esta misma request); viewModel.RowVersion es el que
            // trajo el formulario. Si ya difieren acá, algo bumpeó la fila entre que el
            // navegador sembró/sincronizó ese hidden y este submit — útil para diferenciar
            // un conflicto real de otro usuario de un desincronizado propio (p.ej. el
            // configurador de crédito embebido) antes de llegar al catch de más abajo.
            if (!venta.RowVersion.AsSpan().SequenceEqual(viewModel.RowVersion))
            {
                _logger.LogWarning(
                    "UpdateAsync venta {Id} RowVersion ya desincronizado antes de SaveChanges. Actual(DB):{Actual} Enviado(form):{Enviado}",
                    id,
                    Convert.ToBase64String(venta.RowVersion),
                    Convert.ToBase64String(viewModel.RowVersion));
            }

            _context.Entry(venta).Property(v => v.RowVersion).OriginalValue = viewModel.RowVersion;

            ActualizarDatosVenta(venta, viewModel);
            await ValidarTrazabilidadDetallesVMAsync(viewModel.Detalles);
            ActualizarDetalles(venta, viewModel.Detalles);

            await AplicarPrecioVigenteADetallesAsync(venta);

            CalcularTotales(venta);
            // INVARIANTE: CalcularTotales siempre debe preceder a SincronizarDatosTarjetaEdicionAsync.
            // CalcularTotales establece venta.Total desde los ítems (base limpia).
            // Si el orden se invierte o se agrega otra llamada al ajuste después, el ajuste se compone.
            await SincronizarDatosTarjetaEdicionAsync(venta, viewModel);
            SincronizarEnvioEdicion(venta, viewModel);
            await CalcularComisionesAsync(venta);

            // Venta sin crédito asociado todavía (p.ej. convertida desde cotización): este
            // edit es la primera vez que se establece el crédito, así que corre la misma
            // validación de aptitud + excepción documental que CreateAsync, en vez del guard
            // genérico de VerificarAutorizacionSiCorrespondeAsync (que asume crédito ya evaluado).
            if (viewModel.TipoPago == TipoPago.CreditoPersonal && !venta.CreditoId.HasValue)
            {
                await CapturarSnapshotLimiteCreditoAsync(venta);

                var validacion = await _validacionVentaService.ValidarVentaCreditoPersonalAsync(
                    venta.ClienteId, venta.Total, venta.CreditoId);

                if (validacion.NoViable)
                {
                    if (PuedeAplicarExcepcionDocumental(viewModel.AplicarExcepcionDocumental, viewModel.MotivoExcepcionDocumentalCreate, validacion))
                    {
                        var motivoExcepcion = viewModel.MotivoExcepcionDocumentalCreate!.Trim();
                        AplicarExcepcionDocumentalComoAutorizada(validacion, motivoExcepcion);

                        _logger.LogWarning(
                            "UpdateAsync venta {Id} autorizada por excepción documental. Cliente:{ClienteId} Usuario:{Usuario}",
                            id,
                            venta.ClienteId,
                            _currentUserService.GetUsername());
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"No es posible actualizar la venta con crédito personal. {validacion.MensajeResumen}");
                    }
                }

                await AplicarResultadoValidacionAsync(venta, validacion, _currentUserService.GetUsername());
            }
            else
            {
                await VerificarAutorizacionSiCorrespondeAsync(venta, viewModel);
            }

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

            if (viewModel.TipoPago == TipoPago.CreditoPersonal &&
                venta.Estado == EstadoVenta.PendienteFinanciacion &&
                !venta.CreditoId.HasValue)
            {
                await CrearCreditoPendienteParaVentaAsync(venta);
            }

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
                // Se registra TotalACobrar (productos + envío): un único ingreso por venta, así la
                // reversión por cancelación (que espeja ese movimiento) devuelve también el envío.
                if (venta.TipoPago != TipoPago.CreditoPersonal &&
                    venta.TipoPago != TipoPago.CuentaCorriente)
                {
                    var usuario = _currentUserService.GetUsername();
                    await _cajaService.RegistrarMovimientoVentaAsync(
                        venta.Id,
                        venta.Numero,
                        venta.TotalACobrar,
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

                // No se revalida cupo acá: el cupo se reserva y se autoriza (si excede el límite)
                // desde la creación de la venta a crédito, y el crédito ya cuenta como "vigente"
                // desde entonces (docs/credito-fase-3-cuenta-disponible.md, sección F). Volver a
                // bloquear por cupo en este punto duplica -y contradice- el gate canónico de
                // ValidacionVentaService.ValidarConfirmacionVentaAsync, que explícitamente excluye
                // la categoría "Cupo" al confirmar (ValidarSinCupoAsync).

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

                if (credito.TasaInteres < 0m)
                    throw new InvalidOperationException(
                        "La tasa de interés de Crédito Personal es negativa. " +
                        "Configure el valor en Administración → Tipos de Pago antes de confirmar la venta.");

                // CSR-ML4: mismo plan global que ya valida la cantidad de cuotas contra los
                // productos de la venta — se reutiliza para persistir las cuotas (abajo) sin
                // volver a consultar la configuración.
                var planCreditoVenta = await ValidarCuotasCreditoPersonalPorProductoAsync(venta, credito);

                // Validar stock antes de confirmar
                _validator.ValidarStock(venta);
                _validator.ValidarAutorizacion(venta);
                await ValidarContratoCreditoPersonalGeneradoAsync(venta);

                await ValidarUnidadesTrazablesAsync(venta);
                await DescontarStockYRegistrarMovimientos(venta);
                await MarcarUnidadesVendidasAsync(venta);

                // Generar las cuotas del crédito
                await GenerarCuotasCreditoAsync(credito, venta.Total, planCreditoVenta?.CuotasSinRecargo);

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
        /// Genera las cuotas del crédito según la configuración del plan: recargo TOTAL
        /// (no interés compuesto mensual) sobre el monto financiado, vía el cálculo
        /// canónico de <see cref="IFinancialCalculationService.SimularPlanCredito"/>. El
        /// monto financiado ya es neto de anticipo (<see cref="Credito.MontoAprobado"/>),
        /// así que se simula con anticipo 0 — SaldoAFinanciar da igual al monto financiado.
        /// </summary>
        /// <param name="cuotasSinRecargo">
        /// CSR-ML4: vector del plan global resuelto por el caller (<see cref="ValidarCuotasCreditoPersonalPorProductoAsync"/>),
        /// nunca recalculado acá. Se persiste tal cual en el vector devuelto por SimularPlanCredito —
        /// una vez creadas las cuotas, un cambio posterior de la configuración global no las afecta
        /// (no hay ningún camino que vuelva a leer la configuración para una cuota ya persistida).
        /// </param>
        private async Task GenerarCuotasCreditoAsync(
            Credito credito, decimal montoVenta, IReadOnlyList<int>? cuotasSinRecargo)
        {
            var montoFinanciado = credito.MontoAprobado > 0 ? credito.MontoAprobado : montoVenta;
            // PUN-ML7: fecha comercial única (antes DateTime.Today) para el fallback sin
            // Credito.FechaPrimeraCuota configurada.
            var fechaCuota = credito.FechaPrimeraCuota ?? _reloj.InicioDiaComercial.AddMonths(1);

            var plan = _financialService.SimularPlanCredito(
                montoFinanciado,
                0m,
                credito.CantidadCuotas,
                credito.TasaInteres,
                0m,
                fechaCuota,
                cuotasSinRecargo: cuotasSinRecargo);

            credito.MontoCuota = plan.CuotaEstimada;
            credito.TotalAPagar = plan.TotalAPagar;
            credito.SaldoPendiente = plan.MontoFinanciado;

            // Crear las cuotas a partir del vector exacto del plan (última absorbe el residuo)
            foreach (var item in plan.Cuotas)
            {
                var cuota = new Cuota
                {
                    CreditoId = credito.Id,
                    NumeroCuota = item.NumeroCuota,
                    MontoCapital = item.Capital,
                    MontoInteres = item.Interes,
                    MontoTotal = item.Total,
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

        public async Task<bool> PrepararVentaDesdeCotizacionAsync(int id)
        {
            var venta = await _context.Ventas
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (venta == null)
                return false;

            if (venta.Estado != EstadoVenta.Cotizacion)
            {
                throw new InvalidOperationException(
                    $"Solo se pueden preparar ventas en estado Cotización. Estado actual: {venta.Estado}");
            }

            venta.Estado = EstadoVenta.Presupuesto;
            venta.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Venta {Id} preparada desde cotización", id);
            return true;
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

                var estadoOriginal = venta.Estado;

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

                DesvincularUnidadesDeDetallesCancelados(venta);

                if (venta.Estado == EstadoVenta.Facturada)
                {
                    var factura = await _context.Facturas
                        .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted && !f.Anulada);

                    if (factura != null)
                        MarcarFacturaAnulada(factura, $"Venta cancelada: {motivo}");
                    else
                        _logger.LogWarning("Venta {Id} está Facturada pero no tiene factura activa al cancelar.", id);
                }

                venta.Estado = EstadoVenta.Cancelada;
                venta.FechaCancelacion = DateTime.UtcNow;
                venta.MotivoCancelacion = motivo;

                await _context.SaveChangesAsync();

                // Crear contramovimiento de caja para ventas que tuvieron ingreso inmediato
                if (estadoOriginal == EstadoVenta.Confirmada || estadoOriginal == EstadoVenta.Facturada)
                {
                    var usuario = _currentUserService.GetUsername();
                    await _cajaService.RegistrarContramovimientoVentaAsync(
                        venta.Id,
                        venta.Numero,
                        motivo,
                        usuario);
                }

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
                .Include(v => v.Envio)
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
            // El comprobante cubre el total facturable (hoy = Venta.Total, sin envío): ver
            // VentaMontos. Distinto de TotalACobrar, que es lo que se recibe (productos + envío).
            factura.Total = venta.TotalFacturable;

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
                        venta.TotalACobrar,
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

        /// <summary>
        /// Valida el rango/plan de cuotas de Crédito Personal contra los productos de la venta y
        /// devuelve el plan global efectivamente resuelto para <c>credito.CantidadCuotas</c>
        /// (CSR-ML4), para que el caller (<see cref="GenerarCuotasCreditoAsync"/>) persista las
        /// cuotas con el mismo <c>CuotasSinRecargo</c> sin volver a consultar la configuración.
        /// <c>null</c> cuando no hay contexto de productos o rige la configuración única global
        /// (legado, sin tabla de planes): en ambos casos no hay exclusiones que transportar.
        /// </summary>
        private async Task<PlanCuotaCreditoPersonal?> ValidarCuotasCreditoPersonalPorProductoAsync(Venta venta, Credito credito)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
            {
                return null;
            }

            var productoIds = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => d.ProductoId)
                .Distinct()
                .ToArray();

            if (productoIds.Length == 0)
            {
                return null;
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

            // Gate final antes de generar el crédito: la cantidad debe pertenecer a la
            // intersección real de los productos. El rango min/max no alcanza porque describe un
            // intervalo continuo y los planes son un conjunto discreto de cantidades.
            var planesVenta = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(productoIds);

            if (!planesVenta.EsValido)
            {
                throw new CondicionesPagoVentaException(
                    $"No se puede confirmar la venta con CréditoPersonal. {planesVenta.MensajeRechazo}");
            }

            if (planesVenta.RigeConfiguracionUnicaGlobal)
            {
                return null;
            }

            var planCredito = planesVenta.BuscarPlan(credito.CantidadCuotas);
            if (planCredito is null)
            {
                var habilitadas = string.Join(", ", planesVenta.Planes.Select(p => p.CantidadCuotas));
                throw new CondicionesPagoVentaException(
                    $"No se puede confirmar la venta con CréditoPersonal. " +
                    $"La cantidad de cuotas configurada ({credito.CantidadCuotas}) no está habilitada para los " +
                    $"productos de esta venta. Cantidades disponibles: {habilitadas}.");
            }

            return planCredito;
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

        private static void MarcarFacturaAnulada(Factura factura, string motivo)
        {
            factura.Anulada = true;
            factura.FechaAnulacion = DateTime.UtcNow;
            factura.MotivoAnulacion = motivo.Trim();
            factura.UpdatedAt = DateTime.UtcNow;
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

                MarcarFacturaAnulada(factura, motivo);

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

            // FASE 5E/5G: el usuario que creó la venta no puede autorizarla,
            // salvo que además tenga el permiso ventas.authorize (segundo control ya cubierto por el permiso).
            // CreatedBy (interceptor de auditoría) es la fuente confiable del creador;
            // UsuarioSolicita queda null en el flujo real y no sirve para esta validación.
            if (!string.IsNullOrWhiteSpace(venta.CreatedBy) &&
                string.Equals(venta.CreatedBy, usuarioAutoriza, StringComparison.OrdinalIgnoreCase) &&
                !_currentUserService.HasPermission("ventas", "authorize"))
            {
                throw new InvalidOperationException(
                    "La venta debe ser autorizada por un usuario distinto al que la creó.");
            }

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

            // FASE 5F/5G: el usuario que creó la venta no puede registrar la excepción documental,
            // salvo que además tenga el permiso ventas.authorize.
            // Mismo criterio que AutorizarVentaAsync (CreatedBy es la fuente confiable del creador).
            if (!string.IsNullOrWhiteSpace(venta.CreatedBy) &&
                string.Equals(venta.CreatedBy, usuarioAutoriza, StringComparison.OrdinalIgnoreCase) &&
                !_currentUserService.HasPermission("ventas", "authorize"))
            {
                throw new InvalidOperationException(
                    "La excepción documental debe ser registrada por un usuario distinto al que creó la venta.");
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

            // Cancelar el crédito asociado para liberar cupo (evita "créditos fantasma" vigentes)
            if (venta.CreditoId.HasValue)
            {
                var credito = venta.Credito ?? await _context.Creditos
                    .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                    .FirstOrDefaultAsync(c => c.Id == venta.CreditoId.Value && !c.IsDeleted);

                if (credito != null)
                {
                    CancelarCreditoAsociadoAVenta(credito, $"Cancelado por baja de venta {venta.Numero}");
                }
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

            if (planGlobal == null &&
                tipoTarjeta == TipoTarjeta.Debito &&
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

        /// <summary>
        /// Alta/edición/baja del envío 1:1 de la venta al editar. La existencia de
        /// venta.Envio, no un flag en Venta, es la autoridad — igual patrón que
        /// SincronizarDatosTarjetaEdicionAsync. No toca Total/IVA/crédito: el importe de envío
        /// (CostoEnvio) es un concepto separado que se suma en Venta.TotalACobrar. Sólo se
        /// llega acá en estados previos a la confirmación (ValidarEstadoParaEdicion), así que el
        /// importe no puede cambiar después de registrado el cobro.
        /// </summary>
        private void SincronizarEnvioEdicion(Venta venta, VentaViewModel viewModel)
        {
            if (!viewModel.TieneEnvio)
            {
                if (venta.Envio != null)
                {
                    _context.VentaEnvios.Remove(venta.Envio);
                    venta.Envio = null;
                }

                return;
            }

            if (viewModel.Envio == null)
            {
                throw new InvalidOperationException(
                    "Marcaste que la venta tiene envío pero faltan los datos de entrega.");
            }

            var envio = venta.Envio;
            if (envio == null)
            {
                envio = _mapper.Map<VentaEnvio>(viewModel.Envio);
                envio.VentaId = venta.Id;
                envio.Estado = EstadoEnvio.Pendiente;
                venta.Envio = envio;
                _context.VentaEnvios.Add(envio);
            }
            else
            {
                ActualizarEnvioDesdeViewModel(envio, viewModel.Envio);
            }
        }

        private static void ActualizarEnvioDesdeViewModel(VentaEnvio destino, VentaEnvioViewModel origen)
        {
            destino.Destinatario = origen.Destinatario;
            destino.Telefono = origen.Telefono;
            destino.Domicilio = origen.Domicilio;
            destino.Localidad = origen.Localidad;
            destino.Provincia = origen.Provincia;
            destino.CodigoPostal = origen.CodigoPostal;
            destino.Transportista = origen.Transportista;
            destino.CostoEnvio = origen.CostoEnvio.HasValue ? Math.Max(0m, origen.CostoEnvio.Value) : null;
            destino.FechaProgramada = origen.FechaProgramada;
            destino.Observaciones = origen.Observaciones;
            // Estado, NumeroSeguimiento y las fechas de despacho/entrega NO se editan
            // desde acá: son autoridad exclusiva de VentaEnvioService.CambiarEstadoAsync.
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

        /// <summary>
        /// VENTA-CREDITO-DATOS-HYDRATION: autoridad vigente para el detalle de crédito
        /// personal de una venta es <see cref="Credito"/> + <see cref="Credito.Cuotas"/>.
        /// <see cref="VentaCreditoCuota"/> es legacy (sin filas en producción, ver
        /// auditoría VENTA-DETAILS-H2-AUDIT) y no se lee más acá. Único consumidor de este
        /// método es <see cref="GetByIdAsync"/> (Venta/Details), por lo que no se mantiene
        /// fallback a la tabla legacy.
        ///
        /// Antes de que existan cuotas generadas (Credito.Estado == Configurado, aún sin
        /// confirmar la venta) los campos de plan (MontoAprobado, CantidadCuotas,
        /// TasaInteres, FechaPrimeraCuota) ya están persistidos y se devuelven tal cual;
        /// MontoCuota/TotalAPagar/InteresTotal/Cuotas quedan en su valor "aún no
        /// calculado" (0 / vacío) porque solo se completan en
        /// <c>VentaService.GenerarCuotasCreditoAsync</c> al confirmar la venta — no se
        /// recalculan acá para no inventar un cronograma que todavía no existe.
        /// </summary>
        public async Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Credito)
                    .ThenInclude(c => c!.Cliente)
                .Include(v => v.Credito)
                    .ThenInclude(c => c!.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .FirstOrDefaultAsync(v => v.Id == ventaId &&
                                          !v.IsDeleted &&
                                          v.CreditoId != null &&
                                          v.Credito != null &&
                                          !v.Credito.IsDeleted &&
                                          v.Credito.Cliente != null &&
                                          !v.Credito.Cliente.IsDeleted);

            if (venta?.Credito == null)
                return null;

            var credito = venta.Credito;
            var cuotas = credito.Cuotas.OrderBy(c => c.NumeroCuota).ToList();
            var cuotasGeneradas = cuotas.Count > 0;
            var montoAFinanciar = credito.MontoAprobado;

            // Cronograma cuota-a-cuota: Saldo = capital remanente después de aplicar cada
            // cuota (amortización), reconstruido desde Cuota.MontoCapital porque la entidad
            // Cuota no persiste un saldo corrido propio.
            var saldoCorrido = montoAFinanciar;
            var cuotasViewModel = new List<VentaCreditoCuotaViewModel>();
            foreach (var cuota in cuotas)
            {
                saldoCorrido = Math.Max(0m, saldoCorrido - cuota.MontoCapital);
                cuotasViewModel.Add(new VentaCreditoCuotaViewModel
                {
                    Id = cuota.Id,
                    VentaId = venta.Id,
                    CreditoId = cuota.CreditoId,
                    NumeroCuota = cuota.NumeroCuota,
                    FechaVencimiento = cuota.FechaVencimiento,
                    Monto = cuota.MontoTotal,
                    Saldo = saldoCorrido,
                    Pagada = cuota.Estado == EstadoCuota.Pagada,
                    FechaPago = cuota.FechaPago,
                    MontoPagado = cuota.MontoPagado
                });
            }

            var resultado = new DatosCreditoPersonallViewModel
            {
                CreditoId = credito.Id,
                CreditoNumero = credito.Numero,
                CreditoTotalAsignado = credito.MontoAprobado,
                CreditoDisponible = credito.SaldoPendiente,
                MontoAFinanciar = montoAFinanciar,
                CantidadCuotas = credito.CantidadCuotas,
                MontoCuota = credito.MontoCuota,
                TasaInteresMensual = credito.TasaInteres,
                TotalAPagar = credito.TotalAPagar,
                InteresTotal = cuotasGeneradas ? credito.TotalAPagar - montoAFinanciar : 0m,
                SaldoRestante = credito.SaldoPendiente,
                FechaPrimeraCuota = cuotasGeneradas
                    ? cuotas[0].FechaVencimiento
                    : credito.FechaPrimeraCuota ?? DateTime.Today.AddMonths(1),
                Cuotas = cuotasViewModel
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

        /// <summary>
        /// Enforcement "cada usuario sobre su propia caja" para ventas: el vendedor de la venta
        /// debe estar asignado al padrón de la caja de la apertura. Los roles supervisores
        /// (SuperAdmin/Administrador/Gerente) están exentos. Estricto: caja sin padrón ⇒ solo supervisor.
        /// El padrón se gestiona desde la edición de caja (CajaVendedores).
        /// </summary>
        private async Task ValidarVendedorHabilitadoEnCajaAsync(string? vendedorUserId, int cajaId)
        {
            // El vendedor siempre es el usuario logueado. Los roles supervisores —los mismos que
            // antes podían delegar vendedor— quedan exentos del padrón para no bloquearse a sí mismos.
            if (_currentUserService.IsInRole(Roles.SuperAdmin)
                || _currentUserService.IsInRole(Roles.Administrador)
                || _currentUserService.IsInRole(Roles.Gerente))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(vendedorUserId))
            {
                return;
            }

            var habilitado = await _context.CajaVendedores
                .AsNoTracking()
                .AnyAsync(cv => cv.CajaId == cajaId
                                && cv.VendedorUserId == vendedorUserId
                                && !cv.IsDeleted);

            if (!habilitado)
            {
                throw new InvalidOperationException(
                    "El vendedor no está habilitado para vender en esta caja. " +
                    "Asigná el vendedor a la caja desde la edición de caja.");
            }
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

            if (filter.EstadoEnvio.HasValue)
                query = query.Where(v => v.Envio != null && v.Envio.Estado == filter.EstadoEnvio.Value);

            return query;
        }

        private async Task<Venta?> CargarVentaCompleta(int id)
        {
            return await _context.Ventas
                .Include(v => v.Detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)).ThenInclude(d => d.Producto)
                .Include(v => v.DatosTarjeta)
                .Include(v => v.Envio)
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

        // Valida trazabilidad desde el viewmodel antes de persistir (usado en UpdateAsync). Fase 8.2.S.
        // Acumula todos los motivos y lanza una sola excepción: el operador ve de una vez todas
        // las líneas a corregir en lugar de descubrirlas de a una, guardado por guardado.
        private async Task ValidarTrazabilidadDetallesVMAsync(List<VentaDetalleViewModel> detallesVM)
        {
            var motivos = new List<string>();

            var unidadIds = detallesVM
                .Where(d => d.ProductoUnidadId.HasValue)
                .Select(d => d.ProductoUnidadId!.Value)
                .ToList();

            var duplicadas = unidadIds
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicadas.Any())
                motivos.Add(
                    $"La venta contiene unidades duplicadas en distintas líneas: {string.Join(", ", duplicadas)}.");

            foreach (var detalleVM in detallesVM)
            {
                var producto = await _context.Productos
                    .FirstOrDefaultAsync(p => p.Id == detalleVM.ProductoId && !p.IsDeleted);
                if (producto == null)
                    continue;

                var motivo = await ObtenerMotivoTrazabilidadInvalidaAsync(detalleVM, producto);
                if (motivo != null)
                    motivos.Add(motivo);
            }

            if (motivos.Count > 0)
                throw new InvalidOperationException(string.Join(" ", motivos));
        }

        /// <summary>
        /// Devuelve el motivo por el que una línea incumple las reglas de trazabilidad, o null si
        /// es válida. Los mensajes están redactados para el operador: dicen qué falla y cómo
        /// resolverlo desde la pantalla de venta.
        /// </summary>
        private async Task<string?> ObtenerMotivoTrazabilidadInvalidaAsync(
            VentaDetalleViewModel detalleVM,
            Producto producto)
        {
            if (detalleVM.ProductoUnidadId.HasValue)
            {
                // Venta con unidad física: válida tanto para RequiereNumeroSerie=true como false
                var unidad = await _context.ProductoUnidades
                    .FirstOrDefaultAsync(u => u.Id == detalleVM.ProductoUnidadId.Value && !u.IsDeleted);

                if (unidad == null)
                    return "La unidad seleccionada no está disponible para la venta.";

                if (unidad.ProductoId != detalleVM.ProductoId)
                    return $"La unidad '{unidad.CodigoInternoUnidad}' no pertenece al producto '{producto.Nombre}'.";

                if (unidad.Estado != EstadoUnidad.EnStock)
                    return $"La unidad '{unidad.CodigoInternoUnidad}' no está disponible (estado: {unidad.Estado}).";

                if (detalleVM.Cantidad != 1)
                    return $"Una unidad física seleccionada solo puede venderse con cantidad 1. Producto: '{producto.Nombre}'.";

                return null;
            }

            if (producto.RequiereNumeroSerie)
            {
                // Modo estricto: exige unidad física siempre
                return $"Este producto requiere unidad física. Seleccioná una unidad registrada para venderlo. Producto: '{producto.Nombre}'.";
            }

            // Modo flexible sin unidad física: validar stock no trazado
            var unidadesEnStock = await _context.ProductoUnidades
                .CountAsync(u => u.ProductoId == detalleVM.ProductoId
                              && u.Estado == EstadoUnidad.EnStock
                              && !u.IsDeleted);

            var stockNoTrazado = producto.StockActual - unidadesEnStock;

            if (stockNoTrazado >= detalleVM.Cantidad)
                return null;

            var contexto =
                $"Producto: '{producto.Nombre}' (stock sin identificar: {stockNoTrazado:0.##}, " +
                $"solicitado: {detalleVM.Cantidad}, unidades físicas en stock: {unidadesEnStock}).";

            // Con unidades físicas registradas el operador sí tiene salida desde la pantalla:
            // quitar la línea y volver a agregarla eligiendo el origen "Unidad física".
            return unidadesEnStock > 0
                ? $"No hay stock no trazado suficiente. {contexto} Quitá la línea y volvé a agregar el producto eligiendo el origen 'Unidad física'."
                : $"No hay stock no trazado suficiente. {contexto} Registrá el ingreso de stock del producto antes de guardar.";
        }

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

                if (detalle.ProductoUnidadId.HasValue)
                {
                    // Venta con unidad física: válida tanto para RequiereNumeroSerie=true como false
                    var unidad = await _context.ProductoUnidades
                        .FirstOrDefaultAsync(u => u.Id == detalle.ProductoUnidadId.Value && !u.IsDeleted);

                    if (unidad == null)
                        throw new InvalidOperationException(
                            $"La unidad seleccionada no está disponible para la venta.");

                    if (unidad.ProductoId != detalle.ProductoId)
                        throw new InvalidOperationException(
                            $"La unidad '{unidad.CodigoInternoUnidad}' no pertenece al producto '{producto.Nombre}'.");

                    if (unidad.Estado != EstadoUnidad.EnStock)
                        throw new InvalidOperationException(
                            $"La unidad '{unidad.CodigoInternoUnidad}' no está disponible (estado: {unidad.Estado}).");

                    if (detalle.Cantidad != 1)
                        throw new InvalidOperationException(
                            $"Una unidad física seleccionada solo puede venderse con cantidad 1. Producto: '{producto.Nombre}'.");
                }
                else if (producto.RequiereNumeroSerie)
                {
                    // Modo estricto: exige unidad física siempre
                    throw new InvalidOperationException(
                        $"Este producto requiere unidad física. Seleccioná una unidad registrada para venderlo. Producto: '{producto.Nombre}'.");
                }
                else
                {
                    // Modo flexible sin unidad física: validar stock no trazado
                    var unidadesEnStock = await _context.ProductoUnidades
                        .CountAsync(u => u.ProductoId == detalle.ProductoId
                                      && u.Estado == EstadoUnidad.EnStock
                                      && !u.IsDeleted);

                    var stockNoTrazado = producto.StockActual - unidadesEnStock;

                    if (stockNoTrazado < detalle.Cantidad)
                        throw new InvalidOperationException(
                            $"No hay stock no trazado suficiente. Seleccioná una unidad física registrada o ajustá el origen de stock. " +
                            $"Producto: '{producto.Nombre}' (stock no trazado: {stockNoTrazado}, solicitado: {detalle.Cantidad}).");
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
                await _productoUnidadService.RevertirVentaAsync(
                    unidad.Id,
                    motivo,
                    usuario,
                    $"CancelacionVenta:{venta.Id}");
            }
        }

        private static void DesvincularUnidadesDeDetallesCancelados(Venta venta)
        {
            foreach (var detalle in venta.Detalles.Where(d => !d.IsDeleted && d.ProductoUnidadId.HasValue))
            {
                detalle.ProductoUnidadId = null;
            }
        }

        #endregion

        /// <summary>
        /// Bruto de la línea (precio × cantidad) menos el descuento porcentual (0-100) de línea
        /// aplicado sobre ese bruto. VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: única fórmula para
        /// "Descuento" de línea en las cuatro autoridades de VentaService (persistencia,
        /// preview síncrono legacy, preview asíncrono y fallback de GetTotalVentaAsync). El
        /// porcentaje se clampea a [0,100] como defensa adicional a la validación de rango de los
        /// DTOs públicos (DetalleCalculoVentaRequest, VentaDetalleViewModel).
        /// </summary>
        private static decimal CalcularSubtotalLineaConDescuento(decimal precioUnitario, decimal cantidad, decimal descuentoPorcentaje)
        {
            var bruto = precioUnitario * cantidad;
            var porcentaje = Math.Clamp(descuentoPorcentaje, 0m, 100m);
            var descuentoImporte = bruto * porcentaje / 100m;
            return Math.Max(0m, bruto - descuentoImporte);
        }

        private void CalcularTotales(Venta venta)
        {
            var detallesList = venta.Detalles.Where(d => !d.IsDeleted).ToList();

            foreach (var detalle in detallesList)
            {
                detalle.Subtotal = CalcularSubtotalLineaConDescuento(detalle.PrecioUnitario, detalle.Cantidad, detalle.Descuento);
                AplicarSnapshotIvaMontos(detalle);
            }

            AplicarProrrateoDescuentoGeneral(detallesList, venta.Descuento);

            venta.Subtotal = detallesList.Sum(d => d.SubtotalFinalNeto);
            venta.IVA = detallesList.Sum(d => d.SubtotalFinalIVA);
            venta.Total = detallesList.Sum(d => d.SubtotalFinal);
        }

        /// <summary>
        /// VENTA-DESCUENTO-LINEA-LEGACY-EDIT-GUARD: bloquea la edición de una venta si alguna
        /// línea activa persistida (ANTES de este Update, nunca desde viewModel.Detalles) fue
        /// guardada bajo la semántica legacy de "Descuento" como importe absoluto (pre
        /// VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX / 662b499), o si su Subtotal persistido no
        /// se puede reconciliar de forma inequívoca con ningún modelo. El audit previo
        /// (VENTA-DESCUENTO-LINEA-LEGACY-EDIT-GUARD-AUDIT) confirmó que el POST no envía
        /// VentaDetalleId ni existe dirty-tracking por línea — ActualizarDetalles hace
        /// soft-delete total + recreate, así que no hay forma segura de distinguir "valor legacy
        /// reenviado sin tocar" de "valor conscientemente editado por el operador". Debe correr
        /// antes de ActualizarDetalles/CalcularTotales: si bloquea, no se mutó nada.
        /// </summary>
        private static void ValidarSinDescuentosLegacyAmbiguos(Venta venta)
        {
            var haySemanticaBloqueante = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Select(d => ClasificarSemanticaDescuentoLineaPersistida(
                    d.PrecioUnitario, d.Cantidad, d.Descuento, d.Subtotal))
                .Any(s => s is SemanticaDescuentoLinea.LegacyAbsoluto or SemanticaDescuentoLinea.Ambigua);

            if (haySemanticaBloqueante)
            {
                throw new InvalidOperationException(
                    "No se puede guardar esta venta porque contiene descuentos de línea creados con una versión anterior del sistema. Revisá la operación antes de continuar.");
            }
        }

        private enum SemanticaDescuentoLinea
        {
            SinDescuento,
            LegacyAbsoluto,
            Porcentaje,
            Ambigua
        }

        /// <summary>
        /// Clasifica, de forma determinista y fail-closed, la semántica de "Descuento" de una
        /// línea ya persistida — reconciliando su Subtotal guardado contra los dos modelos
        /// posibles: legacy (importe absoluto, pre 662b499) y porcentual (modelo vigente, ver
        /// <see cref="CalcularSubtotalLineaConDescuento"/>). No usa fecha, CotizacionOrigenId ni
        /// magnitud de Descuento como autoridad — el audit demostró que ninguno de esos criterios
        /// es confiable (hay descuentos legacy absolutos con valores dentro de rango 0-100).
        /// </summary>
        private static SemanticaDescuentoLinea ClasificarSemanticaDescuentoLineaPersistida(
            decimal precioUnitario, int cantidad, decimal descuento, decimal subtotalPersistido)
        {
            if (descuento == 0m)
            {
                return SemanticaDescuentoLinea.SinDescuento;
            }

            // Ambos modelos usan Math.Max(0, ...)/clamp para no dar negativo: un Subtotal
            // persistido en 0 es estructuralmente ambiguo (puede venir de un importe absoluto
            // legacy >= bruto, o de un 100% porcentual actual) y no puede reconciliarse contra
            // los valores exactos de PrecioUnitario/Cantidad/Descuento. Fail closed siempre.
            if (subtotalPersistido == 0m)
            {
                return SemanticaDescuentoLinea.Ambigua;
            }

            var bruto = precioUnitario * cantidad;
            var subtotalAbsoluto = Math.Max(0m, bruto - descuento);
            var subtotalPorcentual = RedondearMoneda(
                CalcularSubtotalLineaConDescuento(precioUnitario, cantidad, descuento));

            var coincideAbsoluto = subtotalPersistido == subtotalAbsoluto;
            var coincidePorcentual = subtotalPersistido == subtotalPorcentual;

            if (coincideAbsoluto && coincidePorcentual)
            {
                // p.ej. bruto == 100: ambas fórmulas coinciden algebraicamente para cualquier
                // descuento en [0,100].
                return SemanticaDescuentoLinea.Ambigua;
            }

            if (coincideAbsoluto)
            {
                return SemanticaDescuentoLinea.LegacyAbsoluto;
            }

            if (coincidePorcentual)
            {
                return SemanticaDescuentoLinea.Porcentaje;
            }

            // No reconcilia con ningún modelo — no "arreglar" automáticamente, fail closed.
            return SemanticaDescuentoLinea.Ambigua;
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
                .Select(d => CalcularSubtotalLineaConDescuento(d.PrecioUnitario, d.Cantidad, d.Descuento))
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
                var subtotalFinal = RedondearMoneda(CalcularSubtotalLineaConDescuento(detalle.PrecioUnitario, detalle.Cantidad, detalle.Descuento));
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
            // CreditoId NO se copia del viewModel: es una asociación de sistema, gestionada
            // exclusivamente por CrearCreditoPendienteParaVentaAsync/ConfigurarCreditoAsync/
            // la cancelación de crédito, nunca por un campo de formulario. El wizard de Venta
            // no renderiza ningún <input> para CreditoId (no es editable por el operador), así
            // que viewModel.CreditoId siempre llega null en un submit real. Copiarlo acá pisaba
            // el CreditoId ya persistido en cada guardado, generando un crédito huérfano
            // PendienteConfiguracion nuevo en cada edición de una venta que ya tenía uno
            // configurado (bug real, no sólo del configurador embebido).
        }

        private void ActualizarDetalles(Venta venta, List<VentaDetalleViewModel> detallesVM)
        {
            var existentes = venta.Detalles.Count(d => !d.IsDeleted);
            _logger.LogDebug(
                "ActualizarDetalles venta {Id}. Existentes:{Existentes} Entrantes:{Entrantes}",
                venta.Id,
                existentes,
                detallesVM.Count);

            // Snapshot histórico de identidad de las líneas actuales, para conservarlo al recrear
            // (patrón soft-delete + recreate). Se conserva sólo si la línea entrante corresponde a la
            // misma línea (Id) y al mismo producto; si el producto se reemplaza, se recaptura en
            // AplicarPrecioVigenteADetallesAsync. Micro-lote 5.
            var snapshotIdentidadPrevio = venta.Detalles
                .Where(d => !d.IsDeleted && d.Id != 0)
                .ToDictionary(
                    d => d.Id,
                    d => (d.ProductoId, d.ProductoNombreAlMomento, d.ProductoCodigoAlMomento));

            foreach (var existente in venta.Detalles.Where(d => !d.IsDeleted))
            {
                existente.IsDeleted = true;
            }

            foreach (var detalleVM in detallesVM)
            {
                var detalle = _mapper.Map<VentaDetalle>(detalleVM);
                NormalizarPagoPorItemLegacy(detalle);
                detalle.VentaId = venta.Id;

                if (detalleVM.Id != 0 &&
                    snapshotIdentidadPrevio.TryGetValue(detalleVM.Id, out var previo) &&
                    previo.ProductoId == detalle.ProductoId)
                {
                    detalle.ProductoNombreAlMomento = previo.ProductoNombreAlMomento;
                    detalle.ProductoCodigoAlMomento = previo.ProductoCodigoAlMomento;
                }

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

                // Snapshot histórico de la identidad del producto (Micro-lote 5). Pegajoso: sólo se
                // captura si la línea aún no lo tiene (línea nueva o producto reemplazado). Las líneas
                // conservadas al editar ya traen su snapshot desde ActualizarDetalles.
                if (VentaDetalleProductoSnapshot.NecesitaCaptura(detalle))
                    VentaDetalleProductoSnapshot.Capturar(detalle, productoCosto);
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
            if (!venta.CreditoId.HasValue)
                return;

            var credito = await _context.Creditos
                .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == venta.CreditoId!.Value &&
                                          !c.IsDeleted &&
                                          c.Cliente != null &&
                                          !c.Cliente.IsDeleted);
            if (credito == null)
                return;

            if (venta.VentaCreditoCuotas.Any())
            {
                _context.VentaCreditoCuotas.RemoveRange(venta.VentaCreditoCuotas);
            }

            CancelarCreditoAsociadoAVenta(credito, $"Cancelado por baja de venta {venta.Numero}");

            _logger.LogInformation(
                "Crédito {CreditoId} cancelado por cancelación de venta {VentaId}.",
                credito.Id, venta.Id);
        }

        /// <summary>
        /// Cancela por completo el crédito asociado a una venta dada de baja (cancelada o rechazada),
        /// dejándolo fuera de EstadosVigentes para que libere cupo (CreditoDisponibleService).
        /// </summary>
        private static void CancelarCreditoAsociadoAVenta(Credito credito, string motivo)
        {
            if (credito.Estado == EstadoCredito.Cancelado)
                return;

            credito.Estado = EstadoCredito.Cancelado;
            credito.FechaFinalizacion = DateTime.UtcNow;
            credito.SaldoPendiente = 0m;
            credito.Observaciones = string.IsNullOrWhiteSpace(credito.Observaciones)
                ? motivo
                : $"{credito.Observaciones}\n{motivo}";

            foreach (var cuota in credito.Cuotas.Where(c => c.Estado != EstadoCuota.Pagada && c.Estado != EstadoCuota.Cancelada))
            {
                cuota.Estado = EstadoCuota.Cancelada;
            }
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

            if (viewModel.TieneEnvio && viewModel.Envio != null)
            {
                await GuardarEnvioAsync(ventaId, viewModel.Envio);
            }
        }

        /// <summary>
        /// Alta del envío al crear la venta (equivalente a GuardarDatosTarjetaAsync para
        /// DatosTarjeta). Idempotente igual que su par: si ya existe un envío para la
        /// venta, no lo duplica ni lo pisa en silencio.
        /// </summary>
        private async Task<bool> GuardarEnvioAsync(int ventaId, VentaEnvioViewModel datosEnvio)
        {
            var yaExiste = await _context.VentaEnvios
                .AnyAsync(e => e.VentaId == ventaId && !e.IsDeleted);
            if (yaExiste)
                return false;

            var envio = _mapper.Map<VentaEnvio>(datosEnvio);
            envio.VentaId = ventaId;
            envio.Estado = EstadoEnvio.Pendiente;

            _context.VentaEnvios.Add(envio);
            await _context.SaveChangesAsync();

            return true;
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

                // El crédito propio de esta venta ya cuenta como "vigente" dentro de SaldoVigente
                // (CalcularDisponibleAsync suma todos los créditos no finalizados/rechazados/cancelados
                // del cliente, y ese crédito ya existe en PendienteConfiguracion/Configurado antes de
                // confirmar). Hay que recalcular el disponible excluyendo su propio saldo del total
                // vigente y volver a aplicar el mismo clamp a 0 -no simplemente sumarle el saldo propio
                // al Disponible ya clampeado, porque eso perdería el exceso cuando OTRA deuda ya supera
                // el límite por sí sola-. Sin esto, el crédito se descuenta dos veces (una como saldo
                // vigente, otra como monto requerido de esta misma operación) y el cupo aparece en $0
                // aunque el cliente no tenga ninguna otra deuda.
                var saldoVigenteExcluyendoPropio = disponible.SaldoVigente;
                var creditoPropio = venta.Credito;

                if (creditoPropio != null
                    && creditoPropio.SaldoPendiente > 0m
                    && creditoPropio.Estado is not (EstadoCredito.Finalizado or EstadoCredito.Rechazado or EstadoCredito.Cancelado))
                {
                    saldoVigenteExcluyendoPropio -= creditoPropio.SaldoPendiente;
                }

                var disponibleParaEstaOperacion = Math.Max(0m, disponible.Limite - saldoVigenteExcluyendoPropio);

                if (montoOperacion > disponibleParaEstaOperacion)
                {
                    throw new InvalidOperationException(
                        $"Cupo de crédito insuficiente para confirmar la venta. " +
                        $"Disponible actual: ${disponibleParaEstaOperacion:N2}, requerido: ${montoOperacion:N2}. " +
                        $"Fórmula aplicada: Disponible = LímiteEfectivo - SaldoPendienteVigente (excluyendo el crédito propio de esta venta; no suma cuotas futuras por separado ni mora adicional)."
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
                    : CalcularSubtotalLineaConDescuento(d.PrecioUnitario, d.Cantidad, d.Descuento));

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
