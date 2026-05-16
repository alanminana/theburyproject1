using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class CotizacionConversionService : ICotizacionConversionService
{
    private readonly AppDbContext _context;
    private readonly VentaNumberGenerator _numberGenerator;
    private readonly IPrecioVigenteResolver _precioResolver;
    private readonly ILogger<CotizacionConversionService> _logger;

    public CotizacionConversionService(
        AppDbContext context,
        VentaNumberGenerator numberGenerator,
        IPrecioVigenteResolver precioResolver,
        ILogger<CotizacionConversionService> logger)
    {
        _context = context;
        _numberGenerator = numberGenerator;
        _precioResolver = precioResolver;
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
            Detalles = detallesPreview
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
                .FirstOrDefaultAsync(c => c.Id == cotizacionId, cancellationToken);

            if (cotizacionEnTx is null || cotizacionEnTx.Estado != EstadoCotizacion.Emitida)
                return CotizacionConversionResultado.Fallido(cotizacionId,
                    [$"La cotización ya no está en estado Emitida (estado actual: {cotizacionEnTx?.Estado}). Puede haber sido convertida concurrentemente."]);

            var numero = await _numberGenerator.GenerarNumeroAsync(EstadoVenta.Cotizacion);

            var venta = new Venta
            {
                Numero = numero,
                ClienteId = clienteId.Value,
                FechaVenta = DateTime.UtcNow,
                Estado = EstadoVenta.Cotizacion,
                TipoPago = MapearTipoPago(cotizacionEnTx.MedioPagoSeleccionado),
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

            _context.Ventas.Add(venta);
            cotizacionEnTx.Estado = EstadoCotizacion.ConvertidaAVenta;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Cotización {CotizacionId} convertida a Venta {VentaId} ({Numero}) por {Usuario}",
                cotizacionId, venta.Id, venta.Numero, usuario);

            return new CotizacionConversionResultado
            {
                Exitoso = true,
                CotizacionId = cotizacionId,
                VentaId = venta.Id,
                NumeroVenta = venta.Numero,
                EstadoVenta = venta.Estado,
                Advertencias = advertencias
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
            decimal descuento = detalle.DescuentoImporteSnapshot ?? 0m;
            decimal subtotal = Redondear(precioUnitario * cantidad - descuento);

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

            detalles.Add(new VentaDetalle
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
            });
        }

        return detalles;
    }

    private static decimal Redondear(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
