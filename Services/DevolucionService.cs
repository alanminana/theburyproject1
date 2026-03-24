using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación del servicio de devoluciones, garantías y RMAs
/// </summary>
public class DevolucionService : IDevolucionService
{
    /// <summary>Plazo máximo (días) para aceptar una devolución desde la fecha de venta.</summary>
    private const int DiasLimiteDevolucion = 30;

    private readonly AppDbContext _context;
    private readonly IMovimientoStockService _movimientoStockService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICajaService? _cajaService;
    private readonly ILogger<DevolucionService> _logger;

    public DevolucionService(
        AppDbContext context,
        IMovimientoStockService movimientoStockService,
        ICurrentUserService currentUserService,
        ILogger<DevolucionService> logger,
        ICajaService? cajaService = null)
    {
        _context = context;
        _movimientoStockService = movimientoStockService;
        _currentUserService = currentUserService;
        _logger = logger;
        _cajaService = cajaService;
    }

    #region Devoluciones

    public async Task<List<Devolucion>> ObtenerTodasDevolucionesAsync()
    {
        return await _context.Devoluciones
            .Include(d => d.Cliente)
            .Include(d => d.Venta)
            .Include(d => d.Detalles.Where(dd => !dd.IsDeleted && dd.Producto != null && !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .Where(d => !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
            .OrderByDescending(d => d.FechaDevolucion)
            .ToListAsync();
    }

    public async Task<List<Devolucion>> ObtenerDevolucionesPorClienteAsync(int clienteId)
    {
        return await _context.Devoluciones
            .Include(d => d.Venta)
            .Include(d => d.Detalles.Where(dd => !dd.IsDeleted && dd.Producto != null && !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .Where(d => d.ClienteId == clienteId && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
            .OrderByDescending(d => d.FechaDevolucion)
            .ToListAsync();
    }

    public async Task<List<Devolucion>> ObtenerDevolucionesPorEstadoAsync(EstadoDevolucion estado)
    {
        return await _context.Devoluciones
            .Include(d => d.Cliente)
            .Include(d => d.Venta)
            .Include(d => d.Detalles.Where(dd => !dd.IsDeleted && dd.Producto != null && !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .Where(d => d.Estado == estado && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
            .OrderByDescending(d => d.FechaDevolucion)
            .ToListAsync();
    }

    public async Task<Devolucion?> ObtenerDevolucionAsync(int id)
    {
        return await _context.Devoluciones
            .Include(d => d.Cliente)
            .Include(d => d.Venta)
            .Include(d => d.Detalles.Where(dd => !dd.IsDeleted && dd.Producto != null && !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .Include(d => d.NotaCredito)
            .Include(d => d.RMA).ThenInclude(r => r!.Proveedor)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted);
    }

    public async Task<Devolucion?> ObtenerDevolucionPorNumeroAsync(string numeroDevolucion)
    {
        return await _context.Devoluciones
            .Include(d => d.Cliente)
            .Include(d => d.Venta)
            .Include(d => d.Detalles.Where(dd => !dd.IsDeleted && dd.Producto != null && !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .FirstOrDefaultAsync(d => d.NumeroDevolucion == numeroDevolucion && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted);
    }

    public async Task<Devolucion> CrearDevolucionAsync(Devolucion devolucion, List<DevolucionDetalle> detalles)
    {
        if (detalles == null || detalles.Count == 0)
            throw new InvalidOperationException("Debe incluir al menos un detalle para la devolución");

        // Validar que la venta existe
        var venta = await _context.Ventas
            .FirstOrDefaultAsync(v => v.Id == devolucion.VentaId && !v.IsDeleted);

        if (venta == null)
            throw new InvalidOperationException("La venta no existe");

        // Validaciones básicas de detalles
        if (detalles.Any(d => d.Cantidad <= 0))
            throw new InvalidOperationException("La cantidad a devolver debe ser mayor a 0");

        var duplicatedProduct = detalles
            .GroupBy(d => d.ProductoId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault();

        if (duplicatedProduct != 0)
            throw new InvalidOperationException("No se permiten productos duplicados en la devolución");

        // Ventas: obtener lo vendido por producto (cantidad y total ponderado)
        var vendidoPorProducto = await _context.VentaDetalles
            .Where(vd => vd.VentaId == devolucion.VentaId && !vd.IsDeleted)
            .GroupBy(vd => vd.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                CantidadVendida = g.Sum(x => x.Cantidad),
                TotalVendido = g.Sum(x => x.Cantidad * x.PrecioUnitario)
            })
            .ToDictionaryAsync(x => x.ProductoId, x => (x.CantidadVendida, x.TotalVendido));

        if (vendidoPorProducto.Count == 0)
            throw new InvalidOperationException("La venta no tiene detalles para devolver");

        // Devoluciones previas (incluye pendientes/en revisión/aprobadas/completadas; excluye rechazadas)
        var devueltoPorProducto = await _context.DevolucionDetalles
            .Where(dd => !dd.IsDeleted)
            .Join(
                _context.Devoluciones.Where(d => !d.IsDeleted && d.VentaId == devolucion.VentaId && d.Estado != EstadoDevolucion.Rechazada),
                dd => dd.DevolucionId,
                d => d.Id,
                (dd, d) => new { dd.ProductoId, dd.Cantidad })
            .GroupBy(x => x.ProductoId)
            .Select(g => new { ProductoId = g.Key, Cantidad = g.Sum(x => x.Cantidad) })
            .ToDictionaryAsync(x => x.ProductoId, x => x.Cantidad);

        var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
        await using var transaction = hasAmbientTransaction
            ? null
            : await _context.Database.BeginTransactionAsync();

        try
        {
            // Generar número + estado
            devolucion.NumeroDevolucion = await GenerarNumeroDevolucionAsync();
            devolucion.Estado = EstadoDevolucion.Pendiente;
            devolucion.FechaDevolucion = devolucion.FechaDevolucion == default ? DateTime.UtcNow : devolucion.FechaDevolucion;

            // Calcular precios/subtotales desde la venta y validar cantidades
            decimal total = 0m;
            foreach (var detalle in detalles)
            {
                if (!vendidoPorProducto.TryGetValue(detalle.ProductoId, out var vendido))
                    throw new InvalidOperationException("El producto a devolver no pertenece a la venta");

                var yaDevuelto = devueltoPorProducto.TryGetValue(detalle.ProductoId, out var cantDevuelta)
                    ? cantDevuelta
                    : 0;

                var disponible = vendido.CantidadVendida - yaDevuelto;
                if (detalle.Cantidad > disponible)
                {
                    throw new InvalidOperationException(
                        $"La cantidad a devolver excede lo disponible. Vendido: {vendido.CantidadVendida}, Ya devuelto: {yaDevuelto}, Disponible: {disponible}");
                }

                var precioUnitario = vendido.CantidadVendida > 0
                    ? Math.Round(vendido.TotalVendido / vendido.CantidadVendida, 2)
                    : 0m;

                detalle.PrecioUnitario = precioUnitario;
                detalle.Subtotal = detalle.Cantidad * detalle.PrecioUnitario;
                total += detalle.Subtotal;

                // Relación
                detalle.Devolucion = devolucion;
            }

            devolucion.TotalDevolucion = total;

            _context.Devoluciones.Add(devolucion);
            await _context.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            _logger.LogInformation(
                "Devolución creada - Numero {Numero} - Cliente {ClienteId} - Venta {VentaId} - Total {Total}",
                devolucion.NumeroDevolucion, devolucion.ClienteId, devolucion.VentaId, devolucion.TotalDevolucion);

            return devolucion;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Devolucion> ActualizarDevolucionAsync(Devolucion devolucion)
    {
        var existente = await ObtenerDevolucionAsync(devolucion.Id);
        if (existente == null)
        {
            throw new KeyNotFoundException($"Devolución con ID {devolucion.Id} no encontrada");
        }

        existente.Descripcion = devolucion.Descripcion;
        existente.ObservacionesInternas = devolucion.ObservacionesInternas;
        existente.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return existente;
    }

    public async Task<Devolucion> AprobarDevolucionAsync(int id, string aprobadoPor, byte[] rowVersion)
    {
        var devolucion = await ObtenerDevolucionAsync(id);
        if (devolucion == null)
        {
            throw new KeyNotFoundException($"Devolución con ID {id} no encontrada");
        }

        if (devolucion.Estado != EstadoDevolucion.Pendiente && devolucion.Estado != EstadoDevolucion.EnRevision)
        {
            throw new InvalidOperationException($"No se puede aprobar una devolución en estado {devolucion.Estado}");
        }

        if (rowVersion is null || rowVersion.Length == 0)
            throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la devolución e intentá nuevamente.");

        // Concurrencia optimista
        _context.Entry(devolucion).Property(d => d.RowVersion).OriginalValue = rowVersion;

        // Idempotencia: evitar doble generación de nota de crédito cuando la resolución sí la requiere
        if (devolucion.TipoResolucion == TipoResolucionDevolucion.NotaCredito &&
            (devolucion.NotaCreditoGenerada || devolucion.NotaCredito != null ||
             await _context.NotasCredito.AnyAsync(nc => nc.DevolucionId == devolucion.Id && !nc.IsDeleted)))
        {
            throw new InvalidOperationException("La nota de crédito ya fue generada para esta devolución");
        }

        var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
        await using var transaction = hasAmbientTransaction
            ? null
            : await _context.Database.BeginTransactionAsync();

        try
        {
            devolucion.Estado = EstadoDevolucion.Aprobada;
            devolucion.AprobadoPor = aprobadoPor;
            devolucion.FechaAprobacion = DateTime.UtcNow;
            devolucion.UpdatedAt = DateTime.UtcNow;

            if (devolucion.TipoResolucion == TipoResolucionDevolucion.NotaCredito)
            {
                var notaCredito = new NotaCredito
                {
                    DevolucionId = devolucion.Id,
                    ClienteId = devolucion.ClienteId,
                    NumeroNotaCredito = await GenerarNumeroNotaCreditoAsync(),
                    FechaEmision = DateTime.UtcNow,
                    MontoTotal = devolucion.TotalDevolucion,
                    Estado = EstadoNotaCredito.Vigente,
                    FechaVencimiento = DateTime.UtcNow.AddYears(1)
                };

                _context.NotasCredito.Add(notaCredito);
                devolucion.NotaCreditoGenerada = true;
            }
            else
            {
                devolucion.NotaCreditoGenerada = false;
            }

            await _context.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            _logger.LogInformation(
                "Devolución aprobada - Numero {Numero} - AprobadoPor {AprobadoPor} - Resolucion {Resolucion}",
                devolucion.NumeroDevolucion, aprobadoPor, devolucion.TipoResolucion);

            return devolucion;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw new InvalidOperationException("La devolución fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Devolucion> RechazarDevolucionAsync(int id, string motivo, byte[] rowVersion)
    {
        var devolucion = await ObtenerDevolucionAsync(id);
        if (devolucion == null)
        {
            throw new KeyNotFoundException($"Devolución con ID {id} no encontrada");
        }

        if (devolucion.Estado == EstadoDevolucion.Completada)
            throw new InvalidOperationException("No se puede rechazar una devolución completada");

        if (rowVersion is null || rowVersion.Length == 0)
            throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la devolución e intentá nuevamente.");

        _context.Entry(devolucion).Property(d => d.RowVersion).OriginalValue = rowVersion;

        devolucion.Estado = EstadoDevolucion.Rechazada;
        devolucion.ObservacionesInternas = motivo;
        devolucion.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Devolución rechazada - Numero {Numero} - Motivo {Motivo}",
                devolucion.NumeroDevolucion, motivo);

            return devolucion;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("La devolución fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
        }
    }

    public async Task<Devolucion> CompletarDevolucionAsync(int id, byte[] rowVersion)
    {
        var devolucion = await ObtenerDevolucionAsync(id);
        if (devolucion == null)
        {
            throw new KeyNotFoundException($"Devolución con ID {id} no encontrada");
        }

        if (devolucion.Estado != EstadoDevolucion.Aprobada)
        {
            throw new InvalidOperationException("Solo se pueden completar devoluciones aprobadas");
        }

        if (rowVersion is null || rowVersion.Length == 0)
            throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la devolución e intentá nuevamente.");

        _context.Entry(devolucion).Property(d => d.RowVersion).OriginalValue = rowVersion;

        var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
        await using var transaction = hasAmbientTransaction
            ? null
            : await _context.Database.BeginTransactionAsync();

        var usuario = _currentUserService.GetUsername();

        try
        {
            // Marcar como completada primero (misma transacción) para evitar doble procesamiento
            devolucion.Estado = EstadoDevolucion.Completada;
            devolucion.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Procesar stock según acción recomendada en cada detalle
            var referencia = $"DEV-{devolucion.NumeroDevolucion}";

            var reintegros = devolucion.Detalles
                .Where(d => d.AccionRecomendada == AccionProducto.ReintegrarStock)
                .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                .ToList();

            var cuarentenas = devolucion.Detalles
                .Where(d => d.AccionRecomendada == AccionProducto.Cuarentena)
                .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                .ToList();

            if (reintegros.Count > 0)
            {
                await _movimientoStockService.RegistrarEntradasAsync(
                    reintegros,
                    $"Reintegro por devolución {devolucion.NumeroDevolucion}",
                    usuario);
            }

            if (cuarentenas.Count > 0)
            {
                await _movimientoStockService.RegistrarEntradasAsync(
                    cuarentenas,
                    $"En cuarentena por devolución {devolucion.NumeroDevolucion}",
                    usuario);
            }

            if (devolucion.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero && devolucion.RegistrarEgresoCaja)
            {
                if (_cajaService == null)
                    throw new InvalidOperationException("No hay servicio de caja configurado para registrar el reembolso.");

                await _cajaService.RegistrarMovimientoDevolucionAsync(
                    devolucion.Id,
                    devolucion.VentaId,
                    devolucion.Venta.Numero,
                    devolucion.NumeroDevolucion,
                    devolucion.TotalDevolucion,
                    usuario);
            }

            if (transaction != null)
                await transaction.CommitAsync();

            _logger.LogInformation(
                "Devolución completada - Numero {Numero} - StockReintegrado {Reintegros} - Cuarentenas {Cuarentenas}",
                devolucion.NumeroDevolucion, reintegros.Count, cuarentenas.Count);

            return devolucion;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw new InvalidOperationException("La devolución fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<string> GenerarNumeroDevolucionAsync()
    {
        var ultimaDevolucion = await _context.Devoluciones
            .OrderByDescending(d => d.Id)
            .FirstOrDefaultAsync();

        int siguiente = (ultimaDevolucion?.Id ?? 0) + 1;
        return $"DEV-{DateTime.UtcNow:yyyyMM}-{siguiente:D6}";
    }

    public async Task<bool> PuedeDevolverVentaAsync(int ventaId)
    {
        var diasDesdeVenta = await ObtenerDiasDesdeVentaAsync(ventaId);
        return diasDesdeVenta <= DiasLimiteDevolucion;
    }

    public async Task<int> ObtenerDiasDesdeVentaAsync(int ventaId)
    {
        var venta = await _context.Ventas
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);
        if (venta == null) return int.MaxValue;

        return (DateTime.UtcNow - venta.FechaVenta).Days;
    }

    #endregion

    #region Detalles de Devolución

    public async Task<List<DevolucionDetalle>> ObtenerDetallesDevolucionAsync(int devolucionId)
    {
        return await _context.DevolucionDetalles
            .Include(dd => dd.Producto)
            .Include(dd => dd.Garantia)
            .Where(dd =>
                dd.DevolucionId == devolucionId &&
                !dd.IsDeleted &&
                dd.Producto != null &&
                !dd.Producto.IsDeleted)
            .ToListAsync();
    }

    public async Task<DevolucionDetalle> AgregarDetalleAsync(DevolucionDetalle detalle)
    {
        detalle.Subtotal = detalle.Cantidad * detalle.PrecioUnitario;
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        // Actualizar total de la devolución
        var devolucion = await _context.Devoluciones
            .FirstOrDefaultAsync(d => d.Id == detalle.DevolucionId && !d.IsDeleted);
        if (devolucion != null)
        {
            devolucion.TotalDevolucion = await _context.DevolucionDetalles
                .Where(dd => dd.DevolucionId == detalle.DevolucionId && !dd.IsDeleted)
                .SumAsync(dd => dd.Subtotal);
            await _context.SaveChangesAsync();
        }

        return detalle;
    }

    public async Task<DevolucionDetalle> ActualizarEstadoProductoAsync(int detalleId, EstadoProductoDevuelto estado, AccionProducto accion)
    {
        var detalle = await _context.DevolucionDetalles
            .FirstOrDefaultAsync(d => d.Id == detalleId && !d.IsDeleted);
        if (detalle == null)
        {
            throw new KeyNotFoundException($"Detalle con ID {detalleId} no encontrado");
        }

        detalle.EstadoProducto = estado;
        detalle.AccionRecomendada = accion;
        detalle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return detalle;
    }

    public async Task<bool> VerificarAccesoriosAsync(int detalleId, bool completos, string? faltantes)
    {
        var detalle = await _context.DevolucionDetalles
            .FirstOrDefaultAsync(d => d.Id == detalleId && !d.IsDeleted);
        if (detalle == null) return false;

        detalle.AccesoriosCompletos = completos;
        detalle.AccesoriosFaltantes = faltantes;
        detalle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Garantías

    public async Task<List<Garantia>> ObtenerTodasGarantiasAsync()
    {
        return await _context.Garantias
            .Include(g => g.Cliente)
            .Include(g => g.Producto)
            .Where(g =>
                !g.IsDeleted &&
                g.Cliente != null &&
                !g.Cliente.IsDeleted &&
                g.Producto != null &&
                !g.Producto.IsDeleted)
            .OrderByDescending(g => g.FechaInicio)
            .ToListAsync();
    }

    public async Task<List<Garantia>> ObtenerGarantiasVigentesAsync()
    {
        var hoy = DateTime.UtcNow;
        return await _context.Garantias
            .Include(g => g.Cliente)
            .Include(g => g.Producto)
            .Where(g => !g.IsDeleted &&
                       g.Cliente != null &&
                       !g.Cliente.IsDeleted &&
                       g.Producto != null &&
                       !g.Producto.IsDeleted &&
                       g.Estado == EstadoGarantia.Vigente &&
                       g.FechaVencimiento >= hoy)
            .OrderBy(g => g.FechaVencimiento)
            .ToListAsync();
    }

    public async Task<List<Garantia>> ObtenerGarantiasPorClienteAsync(int clienteId)
    {
        return await _context.Garantias
            .Include(g => g.Producto)
            .Where(g =>
                g.ClienteId == clienteId &&
                !g.IsDeleted &&
                g.Cliente != null &&
                !g.Cliente.IsDeleted &&
                g.Producto != null &&
                !g.Producto.IsDeleted)
            .OrderByDescending(g => g.FechaInicio)
            .ToListAsync();
    }

    public async Task<Garantia?> ObtenerGarantiaAsync(int id)
    {
        return await _context.Garantias
            .Include(g => g.Cliente)
            .Include(g => g.Producto)
            .Include(g => g.VentaDetalle)
            .FirstOrDefaultAsync(g =>
                g.Id == id &&
                !g.IsDeleted &&
                g.Cliente != null &&
                !g.Cliente.IsDeleted &&
                g.Producto != null &&
                !g.Producto.IsDeleted);
    }

    public async Task<Garantia?> ObtenerGarantiaPorNumeroAsync(string numeroGarantia)
    {
        return await _context.Garantias
            .Include(g => g.Cliente)
            .Include(g => g.Producto)
            .FirstOrDefaultAsync(g =>
                g.NumeroGarantia == numeroGarantia &&
                !g.IsDeleted &&
                g.Cliente != null &&
                !g.Cliente.IsDeleted &&
                g.Producto != null &&
                !g.Producto.IsDeleted);
    }

    public async Task<Garantia> CrearGarantiaAsync(Garantia garantia)
    {
        garantia.NumeroGarantia = await GenerarNumeroGarantiaAsync();
        garantia.FechaVencimiento = garantia.FechaInicio.AddMonths(garantia.MesesGarantia);
        garantia.Estado = EstadoGarantia.Vigente;

        _context.Garantias.Add(garantia);
        await _context.SaveChangesAsync();
        return garantia;
    }

    public async Task<Garantia> ActualizarGarantiaAsync(Garantia garantia)
    {
        var existente = await ObtenerGarantiaAsync(garantia.Id);
        if (existente == null)
        {
            throw new KeyNotFoundException($"Garantía con ID {garantia.Id} no encontrada");
        }

        existente.Estado = garantia.Estado;
        existente.ObservacionesActivacion = garantia.ObservacionesActivacion;
        existente.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return existente;
    }

    public async Task<bool> ValidarGarantiaVigenteAsync(int garantiaId)
    {
        var garantia = await ObtenerGarantiaAsync(garantiaId);
        if (garantia == null) return false;

        return garantia.Estado == EstadoGarantia.Vigente &&
               garantia.FechaVencimiento >= DateTime.UtcNow;
    }

    public async Task<List<Garantia>> ObtenerGarantiasProximasVencerAsync(int dias = 30)
    {
        var hoy = DateTime.UtcNow;
        var fechaLimite = hoy.AddDays(dias);

        return await _context.Garantias
            .Include(g => g.Cliente)
            .Include(g => g.Producto)
            .Where(g => !g.IsDeleted &&
                       g.Cliente != null &&
                       !g.Cliente.IsDeleted &&
                       g.Producto != null &&
                       !g.Producto.IsDeleted &&
                       g.Estado == EstadoGarantia.Vigente &&
                       g.FechaVencimiento >= hoy &&
                       g.FechaVencimiento <= fechaLimite)
            .OrderBy(g => g.FechaVencimiento)
            .ToListAsync();
    }

    public async Task<string> GenerarNumeroGarantiaAsync()
    {
        var ultimaGarantia = await _context.Garantias
            .OrderByDescending(g => g.Id)
            .FirstOrDefaultAsync();

        int siguiente = (ultimaGarantia?.Id ?? 0) + 1;
        return $"GAR-{DateTime.UtcNow:yyyyMM}-{siguiente:D6}";
    }

    #endregion

    #region RMAs

    public async Task<List<RMA>> ObtenerTodosRMAsAsync()
    {
        return await _context.RMAs
            .Include(r => r.Proveedor)
            .Include(r => r.Devolucion).ThenInclude(d => d.Cliente)
            .Where(r =>
                !r.IsDeleted &&
                r.Devolucion != null &&
                !r.Devolucion.IsDeleted &&
                r.Devolucion.Cliente != null &&
                !r.Devolucion.Cliente.IsDeleted)
            .OrderByDescending(r => r.FechaSolicitud)
            .ToListAsync();
    }

    public async Task<List<RMA>> ObtenerRMAsPorEstadoAsync(EstadoRMA estado)
    {
        return await _context.RMAs
            .Include(r => r.Proveedor)
            .Include(r => r.Devolucion)
            .Where(r =>
                r.Estado == estado &&
                !r.IsDeleted &&
                r.Devolucion != null &&
                !r.Devolucion.IsDeleted &&
                r.Devolucion.Cliente != null &&
                !r.Devolucion.Cliente.IsDeleted)
            .OrderByDescending(r => r.FechaSolicitud)
            .ToListAsync();
    }

    public async Task<List<RMA>> ObtenerRMAsPorProveedorAsync(int proveedorId)
    {
        return await _context.RMAs
            .Include(r => r.Devolucion).ThenInclude(d => d.Cliente)
            .Where(r =>
                r.ProveedorId == proveedorId &&
                !r.IsDeleted &&
                r.Devolucion != null &&
                !r.Devolucion.IsDeleted &&
                r.Devolucion.Cliente != null &&
                !r.Devolucion.Cliente.IsDeleted)
            .OrderByDescending(r => r.FechaSolicitud)
            .ToListAsync();
    }

    public async Task<RMA?> ObtenerRMAAsync(int id)
    {
        return await _context.RMAs
            .Include(r => r.Proveedor)
            .Include(r => r.Devolucion).ThenInclude(d => d.Cliente)
            .Include(r => r.Devolucion)
                .ThenInclude(d => d.Detalles.Where(dd =>
                    !dd.IsDeleted &&
                    dd.Producto != null &&
                    !dd.Producto.IsDeleted))
                .ThenInclude(dd => dd.Producto)
            .FirstOrDefaultAsync(r =>
                r.Id == id &&
                !r.IsDeleted &&
                r.Devolucion != null &&
                !r.Devolucion.IsDeleted &&
                r.Devolucion.Cliente != null &&
                !r.Devolucion.Cliente.IsDeleted);
    }

    public async Task<RMA?> ObtenerRMAPorNumeroAsync(string numeroRMA)
    {
        return await _context.RMAs
            .Include(r => r.Proveedor)
            .Include(r => r.Devolucion)
            .FirstOrDefaultAsync(r =>
                r.NumeroRMA == numeroRMA &&
                !r.IsDeleted &&
                r.Devolucion != null &&
                !r.Devolucion.IsDeleted &&
                r.Devolucion.Cliente != null &&
                !r.Devolucion.Cliente.IsDeleted);
    }

    public async Task<RMA> CrearRMAAsync(RMA rma, byte[] devolucionRowVersion)
    {
        if (devolucionRowVersion is null || devolucionRowVersion.Length == 0)
            throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la devolución e intentá nuevamente.");

        var devolucion = await _context.Devoluciones
            .FirstOrDefaultAsync(d => d.Id == rma.DevolucionId && !d.IsDeleted);

        if (devolucion == null)
            throw new InvalidOperationException("Devolución no encontrada");

        if (devolucion.Estado != EstadoDevolucion.Aprobada)
            throw new InvalidOperationException("Solo se puede crear RMA para devoluciones aprobadas");

        if (devolucion.RequiereRMA || await _context.RMAs.AnyAsync(x => x.DevolucionId == devolucion.Id && !x.IsDeleted))
            throw new InvalidOperationException("Esta devolución ya tiene un RMA asociado");

        _context.Entry(devolucion).Property(d => d.RowVersion).OriginalValue = devolucionRowVersion;

        var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
        await using var transaction = hasAmbientTransaction
            ? null
            : await _context.Database.BeginTransactionAsync();

        try
        {
            rma.NumeroRMA = await GenerarNumeroRMAAsync();
            rma.Estado = EstadoRMA.Pendiente;

            _context.RMAs.Add(rma);

            devolucion.RequiereRMA = true;
            devolucion.RMAId = rma.Id;
            devolucion.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            _logger.LogInformation(
                "RMA creado - Numero {NumeroRMA} - Devolucion {DevolucionId}",
                rma.NumeroRMA, rma.DevolucionId);

            return rma;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw new InvalidOperationException("La devolución fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<RMA> ActualizarRMAAsync(RMA rma)
    {
        var existente = await ObtenerRMAAsync(rma.Id);
        if (existente == null)
        {
            throw new KeyNotFoundException($"RMA con ID {rma.Id} no encontrado");
        }

        existente.ObservacionesProveedor = rma.ObservacionesProveedor;
        existente.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return existente;
    }

    public async Task<RMA> AprobarRMAProveedorAsync(int rmaId, string numeroRMAProveedor)
    {
        var rma = await ObtenerRMAAsync(rmaId);
        if (rma == null)
        {
            throw new KeyNotFoundException($"RMA con ID {rmaId} no encontrado");
        }

        rma.Estado = EstadoRMA.AprobadoProveedor;
        rma.FechaAprobacion = DateTime.UtcNow;
        rma.NumeroRMAProveedor = numeroRMAProveedor;
        rma.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "RMA aprobado por proveedor - Id {RMAId} - NumeroRMAProveedor {NumeroRMAProveedor}",
            rmaId, numeroRMAProveedor);

        return rma;
    }

    public async Task<RMA> RegistrarEnvioRMAAsync(int rmaId, string numeroGuia)
    {
        var rma = await ObtenerRMAAsync(rmaId);
        if (rma == null)
        {
            throw new KeyNotFoundException($"RMA con ID {rmaId} no encontrado");
        }

        rma.Estado = EstadoRMA.EnTransito;
        rma.FechaEnvio = DateTime.UtcNow;
        rma.NumeroGuiaEnvio = numeroGuia;
        rma.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "RMA enviado a proveedor - Id {RMAId} - Guia {NumeroGuia}",
            rmaId, numeroGuia);

        return rma;
    }

    public async Task<RMA> RegistrarRecepcionProveedorAsync(int rmaId)
    {
        var rma = await ObtenerRMAAsync(rmaId);
        if (rma == null)
        {
            throw new KeyNotFoundException($"RMA con ID {rmaId} no encontrado");
        }

        rma.Estado = EstadoRMA.RecibidoProveedor;
        rma.FechaRecepcionProveedor = DateTime.UtcNow;
        rma.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("RMA recibido por proveedor - Id {RMAId}", rmaId);

        return rma;
    }

    public async Task<RMA> ResolverRMAAsync(int rmaId, TipoResolucionRMA tipoResolucion, decimal? montoReembolso, string detalleResolucion)
    {
        var rma = await ObtenerRMAAsync(rmaId);
        if (rma == null)
        {
            throw new KeyNotFoundException($"RMA con ID {rmaId} no encontrado");
        }

        rma.Estado = EstadoRMA.Resuelto;
        rma.TipoResolucion = tipoResolucion;
        rma.FechaResolucion = DateTime.UtcNow;
        rma.MontoReembolso = montoReembolso;
        rma.DetalleResolucion = detalleResolucion;
        rma.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "RMA resuelto - Id {RMAId} - Resolucion {TipoResolucion} - Monto {MontoReembolso}",
            rmaId, tipoResolucion, montoReembolso);

        return rma;
    }

    public async Task<string> GenerarNumeroRMAAsync()
    {
        var ultimoRMA = await _context.RMAs
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        int siguiente = (ultimoRMA?.Id ?? 0) + 1;
        return $"RMA-{DateTime.UtcNow:yyyyMM}-{siguiente:D6}";
    }

    #endregion

    #region Notas de Crédito

    public async Task<List<NotaCredito>> ObtenerTodasNotasCreditoAsync()
    {
        return await _context.NotasCredito
            .Include(nc => nc.Cliente)
            .Include(nc => nc.Devolucion)
            .Where(nc =>
                !nc.IsDeleted &&
                nc.Cliente != null &&
                !nc.Cliente.IsDeleted &&
                (nc.Devolucion == null || !nc.Devolucion.IsDeleted))
            .OrderByDescending(nc => nc.FechaEmision)
            .ToListAsync();
    }

    public async Task<List<NotaCredito>> ObtenerNotasCreditoPorClienteAsync(int clienteId)
    {
        return await _context.NotasCredito
            .Include(nc => nc.Devolucion)
            .Where(nc =>
                nc.ClienteId == clienteId &&
                !nc.IsDeleted &&
                nc.Cliente != null &&
                !nc.Cliente.IsDeleted &&
                (nc.Devolucion == null || !nc.Devolucion.IsDeleted))
            .OrderByDescending(nc => nc.FechaEmision)
            .ToListAsync();
    }

    public async Task<List<NotaCredito>> ObtenerNotasCreditoVigentesAsync(int clienteId)
    {
        var hoy = DateTime.UtcNow;
        return await _context.NotasCredito
            .Where(nc => nc.ClienteId == clienteId &&
                        !nc.IsDeleted &&
                        nc.Cliente != null &&
                        !nc.Cliente.IsDeleted &&
                        nc.MontoDisponible > 0 &&
                        (nc.FechaVencimiento == null || nc.FechaVencimiento >= hoy) &&
                        nc.Estado == EstadoNotaCredito.Vigente)
            .OrderBy(nc => nc.FechaEmision)
            .ToListAsync();
    }

    public async Task<NotaCredito?> ObtenerNotaCreditoAsync(int id)
    {
        return await _context.NotasCredito
            .Include(nc => nc.Cliente)
            .Include(nc => nc.Devolucion)
            .FirstOrDefaultAsync(nc =>
                nc.Id == id &&
                !nc.IsDeleted &&
                nc.Cliente != null &&
                !nc.Cliente.IsDeleted &&
                (nc.Devolucion == null || !nc.Devolucion.IsDeleted));
    }

    public async Task<NotaCredito?> ObtenerNotaCreditoPorNumeroAsync(string numeroNotaCredito)
    {
        return await _context.NotasCredito
            .Include(nc => nc.Cliente)
            .FirstOrDefaultAsync(nc =>
                nc.NumeroNotaCredito == numeroNotaCredito &&
                !nc.IsDeleted &&
                nc.Cliente != null &&
                !nc.Cliente.IsDeleted);
    }

    public async Task<NotaCredito> CrearNotaCreditoAsync(NotaCredito notaCredito)
    {
        notaCredito.NumeroNotaCredito = await GenerarNumeroNotaCreditoAsync();
        notaCredito.Estado = EstadoNotaCredito.Vigente;
        notaCredito.MontoUtilizado = 0;

        _context.NotasCredito.Add(notaCredito);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Nota de crédito creada - Numero {Numero} - Cliente {ClienteId} - Monto {Monto}",
            notaCredito.NumeroNotaCredito, notaCredito.ClienteId, notaCredito.MontoTotal);

        return notaCredito;
    }

    public async Task<NotaCredito> UtilizarNotaCreditoAsync(int notaCreditoId, decimal monto)
    {
        var notaCredito = await ObtenerNotaCreditoAsync(notaCreditoId);
        if (notaCredito == null)
        {
            throw new KeyNotFoundException($"Nota de crédito con ID {notaCreditoId} no encontrada");
        }

        if (notaCredito.MontoDisponible < monto)
        {
            throw new InvalidOperationException($"Saldo insuficiente. Disponible: ${notaCredito.MontoDisponible:N2}");
        }

        notaCredito.MontoUtilizado += monto;

        if (notaCredito.MontoDisponible == 0)
        {
            notaCredito.Estado = EstadoNotaCredito.UtilizadaTotalmente;
        }
        else
        {
            notaCredito.Estado = EstadoNotaCredito.UtilizadaParcialmente;
        }

        notaCredito.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Nota de crédito utilizada - Id {NotaCreditoId} - MontoUtilizado {Monto} - EstadoResultante {Estado}",
            notaCreditoId, monto, notaCredito.Estado);

        return notaCredito;
    }

    public async Task<decimal> ObtenerCreditoDisponibleClienteAsync(int clienteId)
    {
        var notasVigentes = await ObtenerNotasCreditoVigentesAsync(clienteId);
        return notasVigentes.Sum(nc => nc.MontoDisponible);
    }

    public async Task<string> GenerarNumeroNotaCreditoAsync()
    {
        var ultimaNotaCredito = await _context.NotasCredito
            .OrderByDescending(nc => nc.Id)
            .FirstOrDefaultAsync();

        int siguiente = (ultimaNotaCredito?.Id ?? 0) + 1;
        return $"NC-{DateTime.UtcNow:yyyyMM}-{siguiente:D6}";
    }

    #endregion

    #region Reportes y Estadísticas

    public async Task<Dictionary<MotivoDevolucion, int>> ObtenerEstadisticasMotivoDevolucionAsync(DateTime? desde = null, DateTime? hasta = null)
    {
        var query = _context.Devoluciones.Where(d => !d.IsDeleted);

        if (desde.HasValue)
            query = query.Where(d => d.FechaDevolucion >= desde.Value);

        if (hasta.HasValue)
            query = query.Where(d => d.FechaDevolucion <= hasta.Value);

        return await query
            .GroupBy(d => d.Motivo)
            .Select(g => new { Motivo = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Motivo, x => x.Cantidad);
    }

    public async Task<List<Producto>> ObtenerProductosMasDevueltosAsync(int top = 10)
    {
        var productosIds = await _context.DevolucionDetalles
            .Where(dd => !dd.IsDeleted)
            .GroupBy(dd => dd.ProductoId)
            .OrderByDescending(g => g.Sum(dd => dd.Cantidad))
            .Take(top)
            .Select(g => g.Key)
            .ToListAsync();

        if (productosIds.Count == 0)
            return new List<Producto>();

        var productos = await _context.Productos
            .Where(p => productosIds.Contains(p.Id) && !p.IsDeleted)
            .ToListAsync();

        // Mantener el orden de ranking calculado en productosIds.
        return productos
            .OrderBy(p => productosIds.IndexOf(p.Id))
            .ToList();
    }

    public async Task<decimal> ObtenerTotalDevolucionesPeriodoAsync(DateTime desde, DateTime hasta)
    {
        return await _context.Devoluciones
            .Where(d => !d.IsDeleted &&
                       d.FechaDevolucion >= desde &&
                       d.FechaDevolucion <= hasta &&
                       d.Estado == EstadoDevolucion.Completada)
            .SumAsync(d => d.TotalDevolucion);
    }

    public async Task<int> ObtenerCantidadRMAsPendientesAsync()
    {
        return await _context.RMAs
            .Where(r => !r.IsDeleted &&
                       (r.Estado == EstadoRMA.Pendiente ||
                        r.Estado == EstadoRMA.AprobadoProveedor ||
                        r.Estado == EstadoRMA.EnTransito))
            .CountAsync();
    }

    #endregion

    #region Filtrado y exportación

    public List<Devolucion> FiltrarDevoluciones(
        IEnumerable<Devolucion> devoluciones,
        string search,
        string? estado,
        string? resolucion)
    {
        var query = devoluciones.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d =>
                Contiene(d.NumeroDevolucion, search) ||
                Contiene(d.Cliente != null ? d.Cliente.ToDisplayName() : null, search) ||
                Contiene(d.Cliente?.NumeroDocumento, search) ||
                Contiene(d.Venta?.Numero, search) ||
                Contiene(d.Descripcion, search));
        }

        if (Enum.TryParse<EstadoDevolucion>(estado, true, out var estadoParsed))
        {
            query = query.Where(d => d.Estado == estadoParsed);
        }

        if (Enum.TryParse<TipoResolucionDevolucion>(resolucion, true, out var resolucionParsed))
        {
            query = query.Where(d => d.TipoResolucion == resolucionParsed);
        }

        return query
            .OrderByDescending(d => d.FechaDevolucion)
            .ThenByDescending(d => d.Id)
            .ToList();
    }

    public List<Garantia> FiltrarGarantias(
        IEnumerable<Garantia> garantias,
        string search,
        string? garantiaEstado,
        string? garantiaVentana)
    {
        var hoy = DateTime.UtcNow.Date;
        var query = garantias.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(g =>
                Contiene(g.NumeroGarantia, search) ||
                Contiene(g.Cliente != null ? g.Cliente.ToDisplayName() : null, search) ||
                Contiene(g.Cliente?.NumeroDocumento, search) ||
                Contiene(g.Producto?.Nombre, search) ||
                Contiene(g.Producto?.Codigo, search) ||
                Contiene(g.ObservacionesActivacion, search));
        }

        if (Enum.TryParse<EstadoGarantia>(garantiaEstado, true, out var estadoParsed))
        {
            query = query.Where(g => g.Estado == estadoParsed);
        }

        query = garantiaVentana?.Trim().ToLowerInvariant() switch
        {
            "proximas" => query.Where(g => g.FechaVencimiento.Date >= hoy && g.FechaVencimiento.Date <= hoy.AddDays(30)),
            "vencidas" => query.Where(g => g.FechaVencimiento.Date < hoy || g.Estado == EstadoGarantia.Vencida),
            "enuso" => query.Where(g => g.Estado == EstadoGarantia.EnUso),
            "extendidas" => query.Where(g => g.GarantiaExtendida),
            _ => query
        };

        return query
            .OrderBy(g => g.FechaVencimiento)
            .ThenByDescending(g => g.Id)
            .ToList();
    }

    public byte[] GenerarCsvDevoluciones(IEnumerable<Devolucion> devoluciones)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id;Cliente;Documento;Venta;Motivo;Resolucion;Impacto;Estado;Fecha;Monto");

        foreach (var devolucion in devoluciones)
        {
            var impacto = devolucion.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero
                ? (devolucion.RegistrarEgresoCaja ? "Reembolso por caja" : "Reembolso sin caja")
                : devolucion.TipoResolucion == TipoResolucionDevolucion.NotaCredito
                    ? "Nota de credito"
                    : "Cambio / reposicion";

            sb.AppendLine(string.Join(";",
                EscapeCsv(devolucion.NumeroDevolucion),
                EscapeCsv(devolucion.Cliente != null ? devolucion.Cliente.ToDisplayName() : null),
                EscapeCsv(devolucion.Cliente?.NumeroDocumento),
                EscapeCsv(devolucion.Venta?.Numero),
                EscapeCsv(devolucion.Motivo.GetDisplayName()),
                EscapeCsv(devolucion.TipoResolucion.GetDisplayName()),
                EscapeCsv(impacto),
                EscapeCsv(devolucion.Estado.GetDisplayName()),
                EscapeCsv(devolucion.FechaDevolucion.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
                EscapeCsv(devolucion.TotalDevolucion.ToString("F2", CultureInfo.InvariantCulture))));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] GenerarCsvGarantias(IEnumerable<Garantia> garantias)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Garantia;Cliente;Documento;Producto;Codigo;Estado;Inicio;Vencimiento;CoberturaMeses;Extendida;Observacion");

        foreach (var garantia in garantias)
        {
            sb.AppendLine(string.Join(";",
                EscapeCsv(garantia.NumeroGarantia),
                EscapeCsv(garantia.Cliente != null ? garantia.Cliente.ToDisplayName() : null),
                EscapeCsv(garantia.Cliente?.NumeroDocumento),
                EscapeCsv(garantia.Producto?.Nombre),
                EscapeCsv(garantia.Producto?.Codigo),
                EscapeCsv(garantia.Estado.GetDisplayName()),
                EscapeCsv(garantia.FechaInicio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.FechaVencimiento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.MesesGarantia.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.GarantiaExtendida ? "Si" : "No"),
                EscapeCsv(garantia.ObservacionesActivacion)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static bool Contiene(string? source, string search)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeCsv(string? value)
    {
        var sanitized = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{sanitized}\"";
    }

    #endregion

    #region Reglas de negocio

    public AccionProducto DeterminarAccionRecomendada(EstadoProductoDevuelto estado)
    {
        return estado switch
        {
            EstadoProductoDevuelto.Nuevo => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.NuevoSellado => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.UsadoBuenEstado => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.AbiertoSinUso => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.UsadoConDetalles => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Marcado => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Defectuoso => AccionProducto.DevolverProveedor,
            EstadoProductoDevuelto.Incompleto => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Danado => AccionProducto.Descarte,
            _ => AccionProducto.Cuarentena
        };
    }

    public bool PermiteImpactoCaja(TipoPago tipoPago)
    {
        return tipoPago != TipoPago.CreditoPersonal && tipoPago != TipoPago.CuentaCorriente;
    }

    public string ConstruirObservacionesInternas(
        TipoResolucionDevolucion tipoResolucion,
        bool registrarEgresoCaja,
        TipoPago tipoPagoOriginal)
    {
        var observaciones = new List<string>
        {
            $"Resolución solicitada: {tipoResolucion.GetDisplayName()}",
            $"Pago original: {tipoPagoOriginal.GetDisplayName()}"
        };

        if (tipoResolucion == TipoResolucionDevolucion.ReembolsoDinero)
        {
            observaciones.Add(registrarEgresoCaja
                ? "Registrar egreso en caja al completar."
                : "No registra egreso automático en caja.");
        }
        else
        {
            observaciones.Add("No genera movimiento automático en caja. La reposición se resuelve por circuito operativo.");
        }

        return string.Join(" | ", observaciones);
    }

    #endregion
}
