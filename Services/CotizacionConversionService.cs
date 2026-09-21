using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CotizacionConversionService : ICotizacionConversionService
{
    private readonly AppDbContext _context;
    private readonly VentaNumberGenerator _numberGenerator;
    private readonly IPrecioVigenteResolver _precioResolver;
    // VENTA-COTIZACION-REWORK-03 (auditoría en vivo del usuario, 2026-09-15): Crédito personal
    // reutiliza la MISMA evaluación de autorización y creación de crédito que ya usa
    // VentaService.CreateAsync — antes esta clase hardcodeaba RequiereAutorizacion=false/
    // EstadoAutorizacion=NoRequiere para CUALQUIER medio (incluido Crédito personal) y nunca
    // creaba el Credito real, un segundo camino de autorización no auditado que contradecía lo
    // que la misma venta habría mostrado creada desde Venta/Create. Ningún otro medio de pago
    // cambia de comportamiento.
    private readonly IValidacionVentaService _validacionVentaService;
    private readonly IVentaService _ventaService;
    private readonly ICurrentUserService _currentUserService;
    // COTIZACION-MIVENTA-02: ambos de sólo lectura para el preflight — ninguno persiste ni
    // reserva nada (ValidarStock lee Producto.StockActual en memoria;
    // ObtenerAperturaActivaParaUsuarioAsync ya se usa como chequeo puro en
    // AsegurarCajaAbiertaParaUsuarioActualAsync).
    private readonly IVentaValidator _ventaValidator;
    private readonly ICajaService _cajaService;
    // PROBLEMA 1 (contrato de Crédito personal — pedido del usuario 2026-09-17, §7): sólo
    // lectura, para enriquecer (nunca bloquear más de lo que ya bloquea) el aviso de Crédito
    // personal con los datos contractuales del cliente que YA sabemos que van a faltar.
    private readonly IContratoVentaCreditoService _contratoVentaCreditoService;
    private readonly ILogger<CotizacionConversionService> _logger;

    public CotizacionConversionService(
        AppDbContext context,
        VentaNumberGenerator numberGenerator,
        IPrecioVigenteResolver precioResolver,
        IValidacionVentaService validacionVentaService,
        IVentaService ventaService,
        ICurrentUserService currentUserService,
        IVentaValidator ventaValidator,
        ICajaService cajaService,
        IContratoVentaCreditoService contratoVentaCreditoService,
        ILogger<CotizacionConversionService> logger)
    {
        _context = context;
        _numberGenerator = numberGenerator;
        _precioResolver = precioResolver;
        _validacionVentaService = validacionVentaService;
        _ventaService = ventaService;
        _currentUserService = currentUserService;
        _ventaValidator = ventaValidator;
        _cajaService = cajaService;
        _contratoVentaCreditoService = contratoVentaCreditoService;
        _logger = logger;
    }

    public async Task<CotizacionConversionPreviewResultado> PreviewConversionAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        var cotizacion = await _context.Cotizaciones
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

        if (cotizacion is null)
        {
            return new CotizacionConversionPreviewResultado
            {
                Convertible = false,
                CotizacionId = cotizacionId,
                Errores = { $"La cotización {cotizacionId} no existe." }
            };
        }

        var errores = new List<string>();
        var advertencias = new List<string>();

        ValidarEstadoConvertible(cotizacion, errores);

        var clienteId = cotizacion.ClienteId;
        var clienteFaltante = clienteId is null;
        if (clienteFaltante)
            errores.Add("La cotización no tiene cliente asignado. Asignar un cliente es obligatorio para crear la venta.");

        var productoIds = cotizacion.Detalles.Select(d => d.ProductoId).Distinct().ToList();

        Dictionary<int, Producto> productos = new();
        IReadOnlyDictionary<int, PrecioVigenteResultado> preciosActuales = new Dictionary<int, PrecioVigenteResultado>();

        if (productoIds.Count > 0)
        {
            productos = await _context.Productos
                .Where(p => productoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            if (!errores.Any())
            {
                preciosActuales = await _precioResolver.ResolverBatchAsync(
                    productoIds, listaId: null, fecha: null, cancellationToken);
            }
        }

        var detallesPreview = new List<CotizacionConversionDetallePreview>();
        bool hayCambiosDePrecios = false;
        bool hayProductosTrazables = false;

        foreach (var detalle in cotizacion.Detalles)
        {
            productos.TryGetValue(detalle.ProductoId, out var producto);
            preciosActuales.TryGetValue(detalle.ProductoId, out var precioActualResult);

            var activo = producto?.Activo ?? false;
            if (!activo)
                errores.Add($"El producto '{detalle.NombreProductoSnapshot}' (ID {detalle.ProductoId}) ya no está activo.");

            decimal? precioActual = precioActualResult?.PrecioFinalConIva;
            bool precioCambio = precioActual.HasValue
                && Math.Abs(precioActual.Value - detalle.PrecioUnitarioSnapshot) > 0.01m;

            bool requiereUnidad = producto?.RequiereNumeroSerie ?? false;

            var detalleAdvertencias = new List<string>();
            if (precioCambio)
            {
                hayCambiosDePrecios = true;
                detalleAdvertencias.Add(
                    $"El precio cambió de {detalle.PrecioUnitarioSnapshot:C} a {precioActual:C}.");
            }
            if (requiereUnidad)
            {
                hayProductosTrazables = true;
                detalleAdvertencias.Add("Requiere selección de unidad física antes de confirmar la venta.");
            }

            decimal? diferenciaUnitaria = precioActual.HasValue
                ? precioActual.Value - detalle.PrecioUnitarioSnapshot
                : null;
            decimal? diferenciaTotal = diferenciaUnitaria.HasValue
                ? diferenciaUnitaria.Value * (int)detalle.Cantidad
                : null;

            detallesPreview.Add(new CotizacionConversionDetallePreview
            {
                ProductoId = detalle.ProductoId,
                CodigoProducto = detalle.CodigoProductoSnapshot,
                NombreProducto = detalle.NombreProductoSnapshot,
                Cantidad = (int)detalle.Cantidad,
                PrecioCotizado = detalle.PrecioUnitarioSnapshot,
                PrecioActual = precioActual,
                ProductoActivo = activo,
                PrecioCambio = precioCambio,
                RequiereUnidadFisica = requiereUnidad,
                DiferenciaUnitaria = diferenciaUnitaria,
                DiferenciaTotal = diferenciaTotal,
                Advertencias = detalleAdvertencias
            });
        }

        if (hayCambiosDePrecios)
            advertencias.Add("Uno o más precios cambiaron desde que se emitió la cotización. Revisar antes de confirmar la venta.");

        if (hayProductosTrazables)
            advertencias.Add("Hay productos que requieren selección de unidad física. Deberán asignarse antes de confirmar la venta.");

        var vencida = EsVencida(cotizacion);

        return new CotizacionConversionPreviewResultado
        {
            Convertible = errores.Count == 0,
            Errores = errores,
            Advertencias = advertencias,
            CotizacionId = cotizacionId,
            EstadoCotizacion = cotizacion.Estado,
            ClienteId = clienteId,
            ClienteFaltante = clienteFaltante,
            CotizacionVencida = vencida,
            HayCambiosDePrecios = hayCambiosDePrecios,
            HayProductosTrazables = hayProductosTrazables,
            TotalCotizado = cotizacion.TotalSeleccionado ?? cotizacion.TotalBase,
            ImporteEnvio = cotizacion.ImporteEnvio,
            TotalACobrar = VentaMontos.CalcularTotalACobrar(
                cotizacion.TotalSeleccionado ?? cotizacion.TotalBase, cotizacion.ImporteEnvio),
            AnticipoCotizado = cotizacion.Anticipo,
            Detalles = detallesPreview
        };
    }

    // COTIZACION-MIVENTA-02: sólo lectura — no abre transacción, no persiste nada, no reserva
    // caja ni cupo. Reutiliza las MISMAS reglas que ConfirmarVentaAsync exige después de crear
    // (IVentaValidator.ValidarStock, ICajaService.ObtenerAperturaActivaParaUsuarioAsync) para
    // poder avisar ANTES de crear la Venta — "Confirmar Mi Venta" nunca debe crear una Venta que
    // ya sabíamos no iba a poder confirmarse.
    public async Task<CotizacionMiVentaPreflightResultado> PreflightConversionAsync(
        int cotizacionId,
        CotizacionConversionRequest request,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        var bloqueos = new List<CotizacionMiVentaBloqueo>();

        var cotizacion = await _context.Cotizaciones
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

        if (cotizacion is null)
        {
            bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "no_encontrada", Mensaje = $"La cotización {cotizacionId} no existe." });
            return new CotizacionMiVentaPreflightResultado { Listo = false, Bloqueos = bloqueos };
        }

        var erroresEstado = new List<string>();
        ValidarEstadoConvertible(cotizacion, erroresEstado);
        foreach (var error in erroresEstado)
            bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "cotizacion_invalida", Mensaje = error });

        var clienteId = request.ClienteIdOverride ?? cotizacion.ClienteId;
        if (clienteId is null)
            bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "sin_cliente", Mensaje = "Falta asignar un cliente para poder crear la venta." });

        var tipoPago = MapearTipoPago(cotizacion.MedioPagoSeleccionado);
        var esCreditoPersonal = tipoPago == TipoPago.CreditoPersonal;

        // VentaValidator.ValidarEstadoParaConfirmacion sólo acepta Cotización/Presupuesto/
        // PendienteRequisitos, y AplicarResultadoValidacionAsync deja toda venta de Crédito
        // personal en PendienteFinanciacion — confirmar en el mismo paso es estructuralmente
        // imposible hasta configurar el plan en el wizard (Credito/ConfigurarVenta). No es una
        // ambigüedad de negocio: es una regla real ya existente, se reporta tal cual es.
        if (esCreditoPersonal)
        {
            var mensaje = "Para finalizar este Crédito personal falta completar su configuración de plan. Ese paso sólo está disponible en el wizard tradicional.";

            // §7 del pedido: NO bloquea más de lo que ya bloquea arriba (Crédito personal
            // nunca puede confirmarse en un paso, ver comentario de más arriba) — sólo
            // adelanta, sin inventar nada, que el contrato que el wizard va a exigir después
            // también le va a faltar completar datos del cliente. Misma regla que
            // ContratoVentaCreditoService.ValidarDatosParaGenerarAsync exige sobre el Cliente,
            // corrida acá sobre el cliente ya persistido (todavía no existe Venta/Crédito).
            if (clienteId.HasValue)
            {
                var clienteParaContrato = await _context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == clienteId.Value, cancellationToken);
                var validacionContrato = _contratoVentaCreditoService.ValidarDatosClienteParaContrato(clienteParaContrato);
                if (!validacionContrato.EsValido)
                {
                    mensaje += " Además, para el contrato de Crédito personal también van a faltar estos datos del cliente: "
                        + string.Join(" ", validacionContrato.Errores);
                }
            }

            bloqueos.Add(new CotizacionMiVentaBloqueo
            {
                Codigo = "credito_personal_requiere_wizard",
                Mensaje = mensaje,
                AccionSugerida = "Continuar con wizard"
            });
        }

        // Stock: misma regla que corre ConfirmarVentaAsync después de crear (VentaValidator.
        // ValidarStock), corrida ahora sobre una Venta en memoria, sin persistir nada.
        var productoIds = cotizacion.Detalles.Select(d => d.ProductoId).Distinct().ToList();
        if (productoIds.Count > 0)
        {
            var productosStock = await _context.Productos
                .Where(p => productoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var ventaEnMemoria = new Venta();
            foreach (var detalle in cotizacion.Detalles)
            {
                productosStock.TryGetValue(detalle.ProductoId, out var producto);
                ventaEnMemoria.Detalles.Add(new VentaDetalle
                {
                    ProductoId = detalle.ProductoId,
                    Cantidad = (int)detalle.Cantidad,
                    Producto = producto
                });
            }

            try
            {
                _ventaValidator.ValidarStock(ventaEnMemoria);
            }
            catch (InvalidOperationException ex)
            {
                bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "stock", Mensaje = ex.Message });
            }
        }

        // Caja: mismo chequeo que AsegurarCajaAbiertaParaUsuarioActualAsync corre dentro de
        // ConfirmarVentaAsync, sin lanzar — acá sólo se consulta.
        var apertura = await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(usuario);
        if (apertura is null)
        {
            bloqueos.Add(new CotizacionMiVentaBloqueo
            {
                Codigo = "sin_caja",
                Mensaje = "No hay una caja abierta a tu nombre. Confirmar la venta requiere una caja abierta.",
                AccionSugerida = "Abrir caja"
            });
        }

        // Permisos: mismos que ya exigen los atributos [PermisoRequerido] de VentaController
        // que ConfirmarYFacturarSiCorrespondeAsync termina invocando — se revalidan igual ahí,
        // esto sólo evita crear una Venta que después ese mismo método va a dejar sin confirmar.
        const string modulo = "ventas";
        if (!_currentUserService.HasPermission(modulo, "update"))
            bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "sin_permiso_confirmar", Mensaje = "No tenés permiso para confirmar ventas." });

        if (request.Facturar && !_currentUserService.HasPermission(modulo, "invoice"))
            bloqueos.Add(new CotizacionMiVentaBloqueo { Codigo = "sin_permiso_facturar", Mensaje = "No tenés permiso para facturar." });

        return new CotizacionMiVentaPreflightResultado
        {
            Listo = bloqueos.Count == 0,
            EsCreditoPersonal = esCreditoPersonal,
            Bloqueos = bloqueos
        };
    }

    // COTIZACION-MIVENTA-02: preview de Subtotal/IVA/alícuotas ANTES de crear la Venta, con el
    // mismo cálculo que ConvertirAVentaAsync usa para los VentaDetalle reales (ConstruirDetalles)
    // envuelto en el mismo builder que ya usa VentaController.Facturar GET
    // (FacturaAlicuotaResumenBuilder.Build) — no es una segunda implementación de IVA.
    public async Task<CotizacionFacturaPreviewResultado> PreviewFacturaAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        var cotizacion = await _context.Cotizaciones
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

        if (cotizacion is null)
            return new CotizacionFacturaPreviewResultado { Exitoso = false, Errores = { $"La cotización {cotizacionId} no existe." } };

        var productoIds = cotizacion.Detalles.Select(d => d.ProductoId).Distinct().ToList();
        var productos = new Dictionary<int, Producto>();
        if (productoIds.Count > 0)
        {
            productos = await _context.Productos
                .Include(p => p.AlicuotaIVA)
                .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                .Where(p => productoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);
        }

        var detalles = ConstruirDetalles(
            cotizacion,
            new CotizacionConversionRequest { UsarPrecioCotizado = true },
            new Dictionary<int, PrecioVigenteResultado>(),
            productos);

        var detalleViewModels = detalles.Select(d => new VentaDetalleViewModel
        {
            PorcentajeIVA = d.PorcentajeIVA,
            AlicuotaIVANombre = d.AlicuotaIVANombre,
            Subtotal = d.Subtotal,
            SubtotalNeto = d.SubtotalNeto,
            SubtotalIVA = d.SubtotalIVA,
            SubtotalFinalNeto = d.SubtotalFinalNeto,
            SubtotalFinalIVA = d.SubtotalFinalIVA,
            SubtotalFinal = d.SubtotalFinal,
            DescuentoGeneralProrrateado = d.DescuentoGeneralProrrateado
        }).ToList();

        // Mismo criterio que ConvertirAVentaAsync: Subtotal ya incluye IVA, Total = Subtotal,
        // IVA es sólo el desglose informativo (ver venta.Subtotal/IVA/Total más arriba).
        var subtotal = detalles.Sum(d => d.Subtotal);
        var iva = detalles.Sum(d => d.SubtotalIVA);

        return new CotizacionFacturaPreviewResultado
        {
            Exitoso = true,
            Subtotal = subtotal,
            IVA = iva,
            Total = subtotal,
            ResumenAlicuotas = FacturaAlicuotaResumenBuilder.Build(detalleViewModels)
        };
    }

    public async Task<CotizacionConversionResultado> ConvertirAVentaAsync(
        int cotizacionId,
        CotizacionConversionRequest request,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        // Validación previa fuera de la transacción
        var cotizacion = await _context.Cotizaciones
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

        if (cotizacion is null)
            return CotizacionConversionResultado.Fallido(cotizacionId, [$"La cotización {cotizacionId} no existe."]);

        var erroresEstado = new List<string>();
        ValidarEstadoConvertible(cotizacion, erroresEstado);
        if (erroresEstado.Count > 0)
            return CotizacionConversionResultado.Fallido(cotizacionId, erroresEstado);

        // Resolver cliente: override tiene prioridad sobre cotización
        var clienteId = request.ClienteIdOverride ?? cotizacion.ClienteId;
        if (clienteId is null)
            return CotizacionConversionResultado.Fallido(cotizacionId,
                ["No se puede crear la venta sin cliente. Proveer ClienteIdOverride o asignar un cliente a la cotización."]);

        // Obtener precios actuales
        var productoIds = cotizacion.Detalles.Select(d => d.ProductoId).Distinct().ToList();
        IReadOnlyDictionary<int, PrecioVigenteResultado> preciosActuales = new Dictionary<int, PrecioVigenteResultado>();
        if (productoIds.Count > 0)
            preciosActuales = await _precioResolver.ResolverBatchAsync(productoIds, null, null, cancellationToken);

        // Cargar productos con IVA (necesario para verificación de activos y cálculo de IVA en detalles)
        Dictionary<int, Producto> productos = new();
        var productosInactivos = new List<string>();
        if (productoIds.Count > 0)
        {
            productos = await _context.Productos
                .Include(p => p.AlicuotaIVA)
                .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                .Where(p => productoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var detalle in cotizacion.Detalles)
            {
                if (productos.TryGetValue(detalle.ProductoId, out var prod) && !prod.Activo)
                    productosInactivos.Add($"El producto '{detalle.NombreProductoSnapshot}' ya no está activo.");
            }
        }

        if (productosInactivos.Count > 0)
            return CotizacionConversionResultado.Fallido(cotizacionId, productosInactivos);

        // Evaluar advertencias según política de precios y opciones del request
        var advertencias = EvaluarAdvertencias(cotizacion, preciosActuales, request);
        if (advertencias.Count > 0 && !request.ConfirmarAdvertencias)
            return CotizacionConversionResultado.Fallido(cotizacionId,
                ["Hay advertencias que deben confirmarse antes de continuar. Revisar el preview y enviar ConfirmarAdvertencias = true."]);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Recargar dentro de transacción para evitar doble conversión concurrente
            var cotizacionEnTx = await _context.Cotizaciones
                .Include(c => c.Detalles)
                .Include(c => c.Cliente)
                .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

            if (cotizacionEnTx is null || cotizacionEnTx.Estado != EstadoCotizacion.Emitida)
                return CotizacionConversionResultado.Fallido(cotizacionId,
                    [$"La cotización ya no está en estado Emitida (estado actual: {cotizacionEnTx?.Estado}). Puede haber sido convertida concurrentemente."]);

            var numero = await _numberGenerator.GenerarNumeroAsync(EstadoVenta.Cotizacion);

            var tipoPago = MapearTipoPago(cotizacionEnTx.MedioPagoSeleccionado);

            var venta = new Venta
            {
                Numero = numero,
                ClienteId = clienteId.Value,
                FechaVenta = DateTime.UtcNow,
                Estado = EstadoVenta.Cotizacion,
                TipoPago = tipoPago,
                AperturaCajaId = null,
                VendedorUserId = null,
                VendedorNombre = usuario,
                Observaciones = ConstruirObservaciones(cotizacionEnTx, request),
                EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere,
                RequiereAutorizacion = false,
                CotizacionOrigenId = cotizacionId
            };

            var detalles = ConstruirDetalles(cotizacionEnTx, request, preciosActuales, productos);
            venta.Subtotal = detalles.Sum(d => d.Subtotal);
            venta.Descuento = 0m;
            venta.IVA = detalles.Sum(d => d.SubtotalIVA);
            venta.Total = venta.Subtotal;

            foreach (var detalle in detalles)
                venta.Detalles.Add(detalle);

            // Crédito personal: evaluar con la MISMA fuente de verdad que Venta/Create
            // (IValidacionVentaService) y aplicar el resultado con la misma lógica
            // (IVentaService.AplicarResultadoValidacionAsync) — nunca hardcodear NoRequiere acá.
            // NoViable (ni siquiera autorizable) rechaza la conversión con ESA alternativa, SALVO
            // que el operador haya solicitado la misma excepción documental que ya existe en
            // Venta/Create (request.AplicarExcepcionDocumental) y corresponda según la MISMA regla
            // (permiso ventas.authorize + alcance excepcionable: documentación/cupo, nunca mora —
            // ver IVentaService.AplicarExcepcionDocumentalSiCorresponde, que decide esto en un solo
            // lugar para CreateAsync y para esta conversión, sin duplicar el criterio).
            var excepcionDocumentalAplicada = false;
            if (tipoPago == TipoPago.CreditoPersonal)
            {
                var validacionCredito = await _validacionVentaService.ValidarVentaCreditoPersonalAsync(
                    clienteId.Value, venta.Total, creditoId: null);

                if (validacionCredito.NoViable)
                {
                    try
                    {
                        _ventaService.AplicarExcepcionDocumentalSiCorresponde(
                            validacionCredito,
                            request.AplicarExcepcionDocumental,
                            request.MotivoExcepcionDocumental,
                            usuario);
                    }
                    catch (InvalidOperationException)
                    {
                        return CotizacionConversionResultado.Fallido(cotizacionId,
                            [$"Crédito personal no está disponible para este cliente en este momento: {validacionCredito.MensajeResumen}. Elegí otro medio de pago o resolvé la situación crediticia del cliente antes de continuar."]);
                    }

                    excepcionDocumentalAplicada = validacionCredito.ExcepcionDocumentalAutorizada;
                }

                await _ventaService.AplicarResultadoValidacionAsync(venta, validacionCredito, usuario);
            }

            // La intención de envío declarada en el simulador ("TieneEnvio") produce un
            // VentaEnvio Pendiente precargado con el domicilio del cliente, editable
            // después desde el paso Envío del wizard. El importe (VentaEnvio.CostoEnvio) NO se
            // suma a venta.Total: es un concepto separado que se suma en Venta.TotalACobrar.
            if (cotizacionEnTx.TieneEnvio)
            {
                var cliente = cotizacionEnTx.Cliente;
                // COTIZACION-MIVENTA-01: el modal de envío del Cotizador puede mandar overrides
                // reales (mismos campos de VentaEnvio/VentaEnvioViewModel que ya usa el paso
                // Envío de Venta/Create); si no llegan (o llegan vacíos) se completa con el
                // domicilio del Cliente, igual que antes.
                venta.Envio = new VentaEnvio
                {
                    Estado = EstadoEnvio.Pendiente,
                    Destinatario = !string.IsNullOrWhiteSpace(request.EnvioDestinatario)
                        ? request.EnvioDestinatario!.Trim()
                        : (cliente != null ? $"{cliente.Apellido}, {cliente.Nombre}" : string.Empty),
                    Telefono = !string.IsNullOrWhiteSpace(request.EnvioTelefono)
                        ? request.EnvioTelefono!.Trim()
                        : cliente?.Telefono,
                    Domicilio = !string.IsNullOrWhiteSpace(request.EnvioDomicilio)
                        ? request.EnvioDomicilio!.Trim()
                        : (cliente?.Domicilio ?? string.Empty),
                    Localidad = !string.IsNullOrWhiteSpace(request.EnvioLocalidad)
                        ? request.EnvioLocalidad!.Trim()
                        : cliente?.Localidad,
                    Provincia = !string.IsNullOrWhiteSpace(request.EnvioProvincia)
                        ? request.EnvioProvincia!.Trim()
                        : cliente?.Provincia,
                    CodigoPostal = !string.IsNullOrWhiteSpace(request.EnvioCodigoPostal)
                        ? request.EnvioCodigoPostal!.Trim()
                        : cliente?.CodigoPostal,
                    Transportista = string.IsNullOrWhiteSpace(request.EnvioTransportista)
                        ? null
                        : request.EnvioTransportista!.Trim(),
                    // El importe viaja desde la Cotización persistida (fuente de verdad); el request
                    // (modal del Cotizador, misma sesión) sólo lo sobreescribe si llega explícito.
                    // Nunca negativo: un envío no puede restar del total a cobrar.
                    CostoEnvio = ResolverCostoEnvioConversion(request.EnvioCostoEnvio, cotizacionEnTx.CostoEnvio),
                    FechaProgramada = request.EnvioFechaProgramada,
                    Observaciones = string.IsNullOrWhiteSpace(request.EnvioObservaciones)
                        ? null
                        : request.EnvioObservaciones!.Trim()
                };
            }

            _context.Ventas.Add(venta);
            cotizacionEnTx.Estado = EstadoCotizacion.ConvertidaAVenta;

            await _context.SaveChangesAsync(cancellationToken);

            // Igual que CreateAsync: el Credito real recién puede crearse con venta.Id ya
            // asignado, y sólo si la venta quedó aprobable (no si quedó pendiente de
            // autorización) — misma condición, misma lógica, sin duplicarla. Precarga cuotas/
            // anticipo intencionados desde esta cotización (venta.CotizacionOrigenId, seteado
            // arriba) automáticamente, igual que cuando la venta se crea directo desde
            // Venta/Create con una cotización de origen.
            if (tipoPago == TipoPago.CreditoPersonal
                && venta.Estado == EstadoVenta.PendienteFinanciacion
                && !venta.RequiereAutorizacion
                && !venta.CreditoId.HasValue)
            {
                await _ventaService.CrearCreditoPendienteParaVentaAsync(venta);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Cotización {CotizacionId} convertida a Venta {VentaId} ({Numero}) por {Usuario}",
                cotizacionId, venta.Id, venta.Numero, usuario);

            // COTIZACION-MIVENTA-01: Confirmar/Facturar son pasos POSTERIORES a la creación
            // (misma secuencia y mismos métodos que VentaController.EjecutarConfirmarYFacturarAsync
            // usa hoy desde Venta/Edit), cada uno en su propia transacción — no se abre una nueva
            // transacción acá para no romper ese mismo patrón. Nunca se intenta si el estado de la
            // venta no lo permite (crédito personal siempre queda PendienteFinanciacion, igual que
            // Venta/Create) ni si el usuario no tiene el permiso real que ya exige VentaController.
            var (ventaConfirmada, facturada, mensajeConfirmacion) = await ConfirmarYFacturarSiCorrespondeAsync(
                venta, request);

            return new CotizacionConversionResultado
            {
                Exitoso = true,
                CotizacionId = cotizacionId,
                VentaId = venta.Id,
                NumeroVenta = venta.Numero,
                EstadoVenta = venta.Estado,
                Advertencias = advertencias,
                ExcepcionDocumentalAplicada = excepcionDocumentalAplicada,
                VentaConfirmada = ventaConfirmada,
                Facturada = facturada,
                MensajeConfirmacion = mensajeConfirmacion
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error al convertir cotización {CotizacionId} a venta", cotizacionId);
            return CotizacionConversionResultado.Fallido(cotizacionId,
                ["Ocurrió un error interno al crear la venta. Intente nuevamente."]);
        }
    }

    // COTIZACION-MIVENTA-01: mismo par de llamadas y mismo orden que
    // VentaController.EjecutarConfirmarYFacturarAsync (Confirmar → Facturar), reutilizando
    // los dos métodos ya existentes de IVentaService sin reimplementar ninguna regla de
    // negocio. Nunca lanza: cualquier motivo por el que no se pudo confirmar/facturar
    // (estado de la venta, falta de permiso) se devuelve como mensaje, para que la cotización
    // recién convertida nunca quede en un estado ambiguo para el frontend.
    private async Task<(bool VentaConfirmada, bool Facturada, string? Mensaje)> ConfirmarYFacturarSiCorrespondeAsync(
        Venta venta,
        CotizacionConversionRequest request)
    {
        if (!request.ConfirmarVenta)
            return (false, false, null);

        const string modulo = "ventas";
        const string accionActualizar = "update";
        const string accionFacturar = "invoice";

        if (!_currentUserService.HasPermission(modulo, accionActualizar))
            return (false, false, "No tenés permiso para confirmar la venta. Quedó creada; podés confirmarla desde Venta/Edit.");

        bool ventaConfirmada;
        try
        {
            ventaConfirmada = await _ventaService.ConfirmarVentaAsync(venta.Id);
        }
        catch (InvalidOperationException ex)
        {
            // Ej.: crédito personal pendiente de configurar el plan (mismo camino que
            // Venta/Create, que en ese caso redirige a Credito/ConfigurarVenta en vez de
            // confirmar) o autorización pendiente — no es un error, es el estado real.
            return (false, false, ex.Message);
        }

        if (!ventaConfirmada)
            return (false, false, "No se pudo confirmar la venta.");

        if (!request.Facturar)
            return (true, false, null);

        if (!_currentUserService.HasPermission(modulo, accionFacturar))
            return (true, false, "La venta se confirmó pero no tenés permiso para facturar. Podés facturarla desde Venta/Details.");

        try
        {
            var facturaViewModel = new FacturaViewModel
            {
                VentaId = venta.Id,
                FechaEmision = DateTime.Today,
                Tipo = request.TipoFactura
            };
            var facturada = await _ventaService.FacturarVentaAsync(venta.Id, facturaViewModel);
            return (true, facturada, facturada ? null : "La venta se confirmó pero no se pudo generar la factura. Reintente facturar.");
        }
        catch (InvalidOperationException ex)
        {
            return (true, false, ex.Message);
        }
    }

    private static decimal? ResolverCostoEnvioConversion(decimal? costoRequest, decimal? costoCotizacion)
    {
        var importe = VentaMontos.NormalizarImporteEnvio(costoRequest ?? costoCotizacion);
        return importe > 0m ? importe : null;
    }

    private static List<string> EvaluarAdvertencias(
        Cotizacion cotizacion,
        IReadOnlyDictionary<int, PrecioVigenteResultado> preciosActuales,
        CotizacionConversionRequest request)
    {
        var advertencias = new List<string>();

        // Cambio de precios solo es advertencia "fuerte" si vamos a usar el precio cotizado
        if (request.UsarPrecioCotizado)
        {
            bool hayCambios = cotizacion.Detalles.Any(d =>
                preciosActuales.TryGetValue(d.ProductoId, out var p)
                && Math.Abs(p.PrecioFinalConIva - d.PrecioUnitarioSnapshot) > 0.01m);

            if (hayCambios)
                advertencias.Add("Uno o más precios cambiaron desde que se emitió la cotización. Revisar antes de confirmar la venta.");
        }

        return advertencias;
    }

    private static void ValidarEstadoConvertible(Cotizacion cotizacion, List<string> errores)
    {
        switch (cotizacion.Estado)
        {
            case EstadoCotizacion.ConvertidaAVenta:
                errores.Add("La cotización ya fue convertida a venta.");
                return;
            case EstadoCotizacion.Cancelada:
                errores.Add("La cotización está cancelada y no puede convertirse.");
                return;
            case EstadoCotizacion.Vencida:
                errores.Add("La cotización está vencida y no puede convertirse.");
                return;
            case EstadoCotizacion.Borrador:
                errores.Add("La cotización es un borrador. Solo se pueden convertir cotizaciones en estado Emitida.");
                return;
        }

        if (EsVencida(cotizacion))
            errores.Add("La cotización ha vencido (fecha de vencimiento superada) y no puede convertirse.");
    }

    private static bool EsVencida(Cotizacion cotizacion) =>
        cotizacion.FechaVencimiento.HasValue && cotizacion.FechaVencimiento.Value < DateTime.UtcNow;

    private static TipoPago MapearTipoPago(CotizacionMedioPagoTipo? medioPago) =>
        medioPago switch
        {
            CotizacionMedioPagoTipo.Efectivo => TipoPago.Efectivo,
            CotizacionMedioPagoTipo.Transferencia => TipoPago.Transferencia,
            CotizacionMedioPagoTipo.TarjetaCredito => TipoPago.TarjetaCredito,
            CotizacionMedioPagoTipo.TarjetaDebito => TipoPago.TarjetaDebito,
            CotizacionMedioPagoTipo.MercadoPago => TipoPago.MercadoPago,
            CotizacionMedioPagoTipo.CreditoPersonal => TipoPago.CreditoPersonal,
            _ => TipoPago.Efectivo
        };

    private static string? ConstruirObservaciones(Cotizacion cotizacion, CotizacionConversionRequest request)
    {
        var partes = new List<string>();
        partes.Add($"Convertido desde cotización #{cotizacion.Numero}.");

        if (cotizacion.CantidadCuotasSeleccionada.HasValue)
        {
            var cuotaInfo = $"Plan cotizado: {cotizacion.CantidadCuotasSeleccionada} cuota(s)";
            if (cotizacion.ValorCuotaSeleccionada.HasValue)
                cuotaInfo += $" de {cotizacion.ValorCuotaSeleccionada:C}";
            if (cotizacion.Anticipo > 0m)
                cuotaInfo += $", anticipo {cotizacion.Anticipo:C}";
            partes.Add(cuotaInfo + " (referencial — revisar antes de confirmar).");
        }

        if (!string.IsNullOrWhiteSpace(cotizacion.Observaciones))
            partes.Add(cotizacion.Observaciones);

        if (!string.IsNullOrWhiteSpace(request.ObservacionesAdicionales))
            partes.Add(request.ObservacionesAdicionales);

        var resultado = string.Join(" ", partes);
        return resultado.Length > 500 ? resultado[..500] : resultado;
    }

    private static List<VentaDetalle> ConstruirDetalles(
        Cotizacion cotizacion,
        CotizacionConversionRequest request,
        IReadOnlyDictionary<int, PrecioVigenteResultado> preciosActuales,
        IReadOnlyDictionary<int, Producto> productos)
    {
        var detalles = new List<VentaDetalle>();

        foreach (var detalle in cotizacion.Detalles)
        {
            decimal precioUnitario = detalle.PrecioUnitarioSnapshot;

            if (!request.UsarPrecioCotizado
                && preciosActuales.TryGetValue(detalle.ProductoId, out var actual)
                && actual.PrecioFinalConIva > 0)
            {
                precioUnitario = actual.PrecioFinalConIva;
            }

            int cantidad = (int)detalle.Cantidad;
            decimal descuentoImporte = detalle.DescuentoImporteSnapshot ?? 0m;
            decimal bruto = precioUnitario * cantidad;
            decimal subtotal = Redondear(bruto - descuentoImporte);

            // VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: VentaDetalle.Descuento es porcentaje
            // (0-100) en toda la autoridad de VentaService (CalcularSubtotalLineaConDescuento).
            // DescuentoImporteSnapshot es un contrato propio de Cotización (pesos ya resueltos por
            // CotizacionPagoCalculator) que no se toca acá; se normaliza a su porcentaje equivalente
            // sólo al escribirlo en VentaDetalle.Descuento, para que una edición posterior de la
            // venta (VentaService.CalcularTotales) no reinterprete el mismo número como otra unidad.
            decimal descuento = bruto > 0m
                ? Math.Min(100m, Redondear(descuentoImporte / bruto * 100m))
                : 0m;

            // Resolver IVA desde la fuente canónica del producto
            productos.TryGetValue(detalle.ProductoId, out var producto);
            decimal porcentajeIva = producto is not null
                ? ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto)
                : ProductoIvaResolver.PorcentajeDefault;

            int? alicuotaId = null;
            string? alicuotaNombre = null;
            if (producto?.AlicuotaIVA is { Activa: true, IsDeleted: false })
            {
                alicuotaId = producto.AlicuotaIVAId;
                alicuotaNombre = producto.AlicuotaIVA.Nombre;
            }
            else if (producto?.Categoria?.AlicuotaIVA is { Activa: true, IsDeleted: false })
            {
                alicuotaId = producto.Categoria.AlicuotaIVAId;
                alicuotaNombre = producto.Categoria.AlicuotaIVA.Nombre;
            }

            // Descomponer precio (ya incluye IVA) en neto + IVA
            decimal precioNeto, ivaUnitario, subtotalNeto, subtotalIva;
            if (porcentajeIva > 0m)
            {
                var divisor = 1m + porcentajeIva / 100m;
                precioNeto = Redondear(precioUnitario / divisor);
                ivaUnitario = Redondear(precioUnitario - precioNeto);
                subtotalNeto = Redondear(subtotal / divisor);
                subtotalIva = Redondear(subtotal - subtotalNeto);
            }
            else
            {
                precioNeto = precioUnitario;
                ivaUnitario = 0m;
                subtotalNeto = subtotal;
                subtotalIva = 0m;
            }

            var nuevoDetalle = new VentaDetalle
            {
                ProductoId = detalle.ProductoId,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                Descuento = descuento,
                Subtotal = subtotal,
                PorcentajeIVA = porcentajeIva,
                AlicuotaIVAId = alicuotaId,
                AlicuotaIVANombre = alicuotaNombre,
                PrecioUnitarioNeto = precioNeto,
                IVAUnitario = ivaUnitario,
                SubtotalNeto = subtotalNeto,
                SubtotalIVA = subtotalIva,
                DescuentoGeneralProrrateado = 0m,
                SubtotalFinalNeto = subtotalNeto,
                SubtotalFinalIVA = subtotalIva,
                SubtotalFinal = subtotal,
                CostoUnitarioAlMomento = 0m,
                CostoTotalAlMomento = 0m
            };

            // Snapshot histórico de identidad (Micro-lote 5): del Producto de BD al momento de convertir;
            // si ya no existe, cae al snapshot que la propia cotización conservó.
            VentaDetalleProductoSnapshot.Capturar(
                nuevoDetalle, producto, detalle.NombreProductoSnapshot, detalle.CodigoProductoSnapshot);

            detalles.Add(nuevoDetalle);
        }

        return detalles;
    }

    private static decimal Redondear(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
