using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class CotizacionService : ICotizacionService
{
    private readonly AppDbContext _context;
    private readonly ICotizacionPagoCalculator _calculator;
    private readonly ILogger<CotizacionService> _logger;

    public CotizacionService(
        AppDbContext context,
        ICotizacionPagoCalculator calculator,
        ILogger<CotizacionService> logger)
    {
        _context = context;
        _calculator = calculator;
        _logger = logger;
    }

    public async Task<CotizacionResultado> CrearAsync(
        CotizacionCrearRequest request,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Simulacion);

        if (request.Simulacion.Productos.Count == 0)
            throw new InvalidOperationException("Debe agregar al menos un producto para guardar la cotizacion.");

        if (request.Simulacion.ClienteId.HasValue)
        {
            var clienteExiste = await _context.Clientes
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.Simulacion.ClienteId.Value && !c.IsDeleted, cancellationToken);

            if (!clienteExiste)
                throw new InvalidOperationException("El cliente seleccionado no existe o esta eliminado.");
        }

        var simulacion = await _calculator.SimularAsync(request.Simulacion, cancellationToken);
        if (!simulacion.Exitoso)
        {
            var error = simulacion.Errores.FirstOrDefault() ?? "La cotizacion no pudo ser recalculada.";
            throw new InvalidOperationException(error);
        }

        var seleccion = ResolverSeleccion(simulacion, request.OpcionSeleccionada);
        var cotizacion = new Cotizacion
        {
            Numero = await GenerarNumeroAsync(cancellationToken),
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            ClienteId = request.Simulacion.ClienteId,
            NombreClienteLibre = NormalizarTexto(request.NombreClienteLibre ?? request.Simulacion.NombreClienteLibre, 200),
            TelefonoClienteLibre = NormalizarTexto(request.TelefonoClienteLibre, 30),
            Observaciones = NormalizarTexto(request.Observaciones, 1000),
            Subtotal = simulacion.Subtotal,
            DescuentoTotal = simulacion.DescuentoTotal,
            TotalBase = simulacion.TotalBase,
            MedioPagoSeleccionado = seleccion?.Opcion.MedioPago,
            PlanSeleccionado = NormalizarTexto(seleccion?.Plan?.Plan, 200),
            CantidadCuotasSeleccionada = seleccion?.Plan?.CantidadCuotas,
            TotalSeleccionado = seleccion?.Plan?.Total,
            ValorCuotaSeleccionada = seleccion?.Plan?.ValorCuota,
            FechaVencimiento = request.FechaVencimiento,
            CreatedBy = string.IsNullOrWhiteSpace(usuario) ? "System" : usuario.Trim()
        };

        foreach (var producto in simulacion.Productos)
        {
            var original = request.Simulacion.Productos.FirstOrDefault(p => p.ProductoId == producto.ProductoId);
            cotizacion.Detalles.Add(new CotizacionDetalle
            {
                ProductoId = producto.ProductoId,
                CodigoProductoSnapshot = producto.Codigo,
                NombreProductoSnapshot = producto.Nombre,
                Cantidad = producto.Cantidad,
                PrecioUnitarioSnapshot = producto.PrecioUnitario,
                DescuentoPorcentajeSnapshot = original?.DescuentoPorcentaje,
                DescuentoImporteSnapshot = original?.DescuentoImporte,
                Subtotal = producto.Subtotal
            });
        }

        foreach (var opcion in simulacion.OpcionesPago)
        {
            if (opcion.Planes.Count == 0)
            {
                cotizacion.OpcionesPago.Add(CrearPagoSimulado(opcion, null, seleccion));
                continue;
            }

            foreach (var plan in opcion.Planes)
            {
                cotizacion.OpcionesPago.Add(CrearPagoSimulado(opcion, plan, seleccion));
            }
        }

        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Cotizacion {Numero} guardada por {Usuario} sin crear venta.",
            cotizacion.Numero,
            cotizacion.CreatedBy);

        return (await ObtenerAsync(cotizacion.Id, cancellationToken))!;
    }

    public async Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default)
    {
        var cotizacion = await _context.Cotizaciones
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Detalles.Where(d => !d.IsDeleted))
            .Include(c => c.OpcionesPago.Where(o => !o.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);

        if (cotizacion is null)
            return null;

        // Resolver venta generada por conversión si corresponde
        int? ventaConvertidaId = null;
        string? numeroVentaConvertida = null;
        if (cotizacion.Estado == EstadoCotizacion.ConvertidaAVenta)
        {
            var ventaLink = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.CotizacionOrigenId == id && !v.IsDeleted)
                .Select(v => new { v.Id, v.Numero })
                .FirstOrDefaultAsync(cancellationToken);

            ventaConvertidaId = ventaLink?.Id;
            numeroVentaConvertida = ventaLink?.Numero;
        }

        return MapDetalle(cotizacion, ventaConvertidaId, numeroVentaConvertida);
    }

    public async Task<CotizacionListadoResultado> ListarAsync(
        CotizacionFiltros filtros,
        CancellationToken cancellationToken = default)
    {
        filtros ??= new CotizacionFiltros();

        var query = _context.Cotizaciones
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Where(c => !c.IsDeleted);

        if (filtros.ClienteId.HasValue)
            query = query.Where(c => c.ClienteId == filtros.ClienteId.Value);

        if (filtros.Estado.HasValue)
            query = query.Where(c => c.Estado == filtros.Estado.Value);

        if (filtros.FechaDesde.HasValue)
            query = query.Where(c => c.Fecha >= filtros.FechaDesde.Value.Date);

        if (filtros.FechaHasta.HasValue)
        {
            var hastaExclusivo = filtros.FechaHasta.Value.Date.AddDays(1);
            query = query.Where(c => c.Fecha < hastaExclusivo);
        }

        if (!string.IsNullOrWhiteSpace(filtros.Busqueda))
        {
            var term = filtros.Busqueda.Trim();
            query = query.Where(c =>
                c.Numero.Contains(term) ||
                (c.NombreClienteLibre != null && c.NombreClienteLibre.Contains(term)) ||
                (c.Cliente != null && (c.Cliente.Nombre.Contains(term) || c.Cliente.Apellido.Contains(term))));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Max(filtros.Page, 1);
        var pageSize = Math.Clamp(filtros.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(c => c.Fecha)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CotizacionListadoItem
            {
                Id = c.Id,
                Numero = c.Numero,
                Fecha = c.Fecha,
                Estado = c.Estado,
                Cliente = c.Cliente != null
                    ? (c.Cliente.Apellido + ", " + c.Cliente.Nombre)
                    : (c.NombreClienteLibre ?? "Cliente mostrador"),
                TotalBase = c.TotalBase,
                TotalSeleccionado = c.TotalSeleccionado,
                MedioPagoSeleccionado = c.MedioPagoSeleccionado
            })
            .ToListAsync(cancellationToken);

        return new CotizacionListadoResultado
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<string> GenerarNumeroAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow;
        var prefix = $"COT-{today:yyyyMMdd}-";
        var last = await _context.Cotizaciones
            .AsNoTracking()
            .Where(c => c.Numero.StartsWith(prefix))
            .OrderByDescending(c => c.Numero)
            .Select(c => c.Numero)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(last) &&
            int.TryParse(last[(last.LastIndexOf('-') + 1)..], out var parsed))
        {
            next = parsed + 1;
        }

        return $"{prefix}{next:0000}";
    }

    private static (CotizacionMedioPagoResultado Opcion, CotizacionPlanPagoResultado? Plan)? ResolverSeleccion(
        CotizacionSimulacionResultado simulacion,
        CotizacionOpcionPagoSeleccionadaRequest? request)
    {
        if (request is null)
            return null;

        foreach (var opcion in simulacion.OpcionesPago.Where(o => o.MedioPago == request.MedioPago))
        {
            var plan = opcion.Planes.FirstOrDefault(p =>
                string.Equals(p.Plan, request.Plan, StringComparison.OrdinalIgnoreCase) &&
                p.CantidadCuotas == request.CantidadCuotas);

            if (plan is not null)
                return (opcion, plan);
        }

        throw new InvalidOperationException("La opcion de pago seleccionada no coincide con la simulacion recalculada.");
    }

    private static CotizacionPagoSimulado CrearPagoSimulado(
        CotizacionMedioPagoResultado opcion,
        CotizacionPlanPagoResultado? plan,
        (CotizacionMedioPagoResultado Opcion, CotizacionPlanPagoResultado? Plan)? seleccion)
    {
        var advertencias = new List<string>();
        if (!string.IsNullOrWhiteSpace(opcion.MotivoNoDisponible))
            advertencias.Add(opcion.MotivoNoDisponible);
        if (plan?.Advertencias.Count > 0)
            advertencias.AddRange(plan.Advertencias);

        return new CotizacionPagoSimulado
        {
            MedioPago = opcion.MedioPago,
            NombreMedioPago = opcion.NombreMedioPago,
            Estado = opcion.Estado,
            Plan = NormalizarTexto(plan?.Plan, 200),
            CantidadCuotas = plan?.CantidadCuotas,
            RecargoPorcentaje = plan?.RecargoPorcentaje ?? 0m,
            DescuentoPorcentaje = plan?.DescuentoPorcentaje ?? 0m,
            InteresPorcentaje = plan?.InteresPorcentaje ?? 0m,
            TasaMensual = plan?.TasaMensual,
            CostoFinancieroTotal = plan?.CostoFinancieroTotal,
            Total = plan?.Total ?? 0m,
            ValorCuota = plan?.ValorCuota,
            Recomendado = plan?.Recomendado ?? false,
            Seleccionado = seleccion.HasValue &&
                seleccion.Value.Opcion.MedioPago == opcion.MedioPago &&
                string.Equals(seleccion.Value.Plan?.Plan, plan?.Plan, StringComparison.OrdinalIgnoreCase) &&
                seleccion.Value.Plan?.CantidadCuotas == plan?.CantidadCuotas,
            AdvertenciasJson = advertencias.Count == 0 ? null : JsonSerializer.Serialize(advertencias)
        };
    }

    private static CotizacionResultado MapDetalle(
        Cotizacion cotizacion,
        int? ventaConvertidaId = null,
        string? numeroVentaConvertida = null)
    {
        var clienteNombre = cotizacion.Cliente is null
            ? null
            : $"{cotizacion.Cliente.Apellido}, {cotizacion.Cliente.Nombre}";

        return new CotizacionResultado
        {
            Id = cotizacion.Id,
            Numero = cotizacion.Numero,
            Fecha = cotizacion.Fecha,
            Estado = cotizacion.Estado,
            ClienteId = cotizacion.ClienteId,
            ClienteNombre = clienteNombre,
            NombreClienteLibre = cotizacion.NombreClienteLibre,
            TelefonoClienteLibre = cotizacion.TelefonoClienteLibre,
            Observaciones = cotizacion.Observaciones,
            Subtotal = cotizacion.Subtotal,
            DescuentoTotal = cotizacion.DescuentoTotal,
            TotalBase = cotizacion.TotalBase,
            MedioPagoSeleccionado = cotizacion.MedioPagoSeleccionado,
            PlanSeleccionado = cotizacion.PlanSeleccionado,
            CantidadCuotasSeleccionada = cotizacion.CantidadCuotasSeleccionada,
            TotalSeleccionado = cotizacion.TotalSeleccionado,
            ValorCuotaSeleccionada = cotizacion.ValorCuotaSeleccionada,
            FechaVencimiento = cotizacion.FechaVencimiento,
            VentaConvertidaId = ventaConvertidaId,
            NumeroVentaConvertida = numeroVentaConvertida,
            Detalles = cotizacion.Detalles
                .OrderBy(d => d.Id)
                .Select(d => new CotizacionDetalleResultado
                {
                    ProductoId = d.ProductoId,
                    CodigoProductoSnapshot = d.CodigoProductoSnapshot,
                    NombreProductoSnapshot = d.NombreProductoSnapshot,
                    Cantidad = d.Cantidad,
                    PrecioUnitarioSnapshot = d.PrecioUnitarioSnapshot,
                    DescuentoPorcentajeSnapshot = d.DescuentoPorcentajeSnapshot,
                    DescuentoImporteSnapshot = d.DescuentoImporteSnapshot,
                    Subtotal = d.Subtotal
                })
                .ToList(),
            OpcionesPago = cotizacion.OpcionesPago
                .OrderByDescending(o => o.Seleccionado)
                .ThenBy(o => o.MedioPago)
                .ThenBy(o => o.CantidadCuotas)
                .Select(o => new CotizacionPagoSimuladoResultado
                {
                    MedioPago = o.MedioPago,
                    NombreMedioPago = o.NombreMedioPago,
                    Estado = o.Estado,
                    Plan = o.Plan,
                    CantidadCuotas = o.CantidadCuotas,
                    RecargoPorcentaje = o.RecargoPorcentaje,
                    DescuentoPorcentaje = o.DescuentoPorcentaje,
                    InteresPorcentaje = o.InteresPorcentaje,
                    TasaMensual = o.TasaMensual,
                    CostoFinancieroTotal = o.CostoFinancieroTotal,
                    Total = o.Total,
                    ValorCuota = o.ValorCuota,
                    Recomendado = o.Recomendado,
                    Seleccionado = o.Seleccionado,
                    AdvertenciasJson = o.AdvertenciasJson
                })
                .ToList()
        };
    }

    private static string? NormalizarTexto(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
