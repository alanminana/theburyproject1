using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class ProductoUnidadService : IProductoUnidadService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProductoUnidadService> _logger;

        public ProductoUnidadService(AppDbContext context, ILogger<ProductoUnidadService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Crear unidad

        public async Task<ProductoUnidad> CrearUnidadAsync(
            int productoId,
            string? numeroSerie = null,
            string? ubicacionActual = null,
            string? observaciones = null,
            string? usuario = null)
        {
            var producto = await _context.Productos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productoId);

            if (producto == null)
                throw new InvalidOperationException($"No existe el producto con Id {productoId}.");

            if (producto.IsDeleted)
                throw new InvalidOperationException($"El producto con Id {productoId} está eliminado.");

            if (!string.IsNullOrWhiteSpace(numeroSerie))
            {
                var serieDuplicada = await _context.ProductoUnidades
                    .AnyAsync(u => u.ProductoId == productoId
                                && u.NumeroSerie == numeroSerie
                                && !u.IsDeleted);

                if (serieDuplicada)
                    throw new InvalidOperationException(
                        $"Ya existe una unidad activa con el número de serie '{numeroSerie}' para el producto {productoId}.");
            }

            var codigoInterno = await GenerarCodigoInternoAsync(productoId, producto.Codigo);

            var unidad = new ProductoUnidad
            {
                ProductoId = productoId,
                CodigoInternoUnidad = codigoInterno,
                NumeroSerie = string.IsNullOrWhiteSpace(numeroSerie) ? null : numeroSerie,
                Estado = EstadoUnidad.EnStock,
                UbicacionActual = string.IsNullOrWhiteSpace(ubicacionActual) ? null : ubicacionActual,
                Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones,
                FechaIngreso = DateTime.UtcNow
            };

            var movimientoInicial = new ProductoUnidadMovimiento
            {
                EstadoAnterior = EstadoUnidad.EnStock,
                EstadoNuevo = EstadoUnidad.EnStock,
                Motivo = "Ingreso inicial de unidad",
                OrigenReferencia = "AltaUnidad",
                UsuarioResponsable = usuario,
                FechaCambio = DateTime.UtcNow
            };

            unidad.Historial.Add(movimientoInicial);

            _context.ProductoUnidades.Add(unidad);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad creada: {Codigo} para ProductoId {ProductoId}",
                codigoInterno, productoId);

            return unidad;
        }

        #endregion

        #region Consultas

        public async Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoAsync(int productoId)
        {
            return await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => u.ProductoId == productoId && !u.IsDeleted)
                .OrderBy(u => u.CodigoInternoUnidad)
                .ToListAsync();
        }

        public async Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoFiltradoAsync(
            int productoId,
            ProductoUnidadFiltros filtros)
        {
            filtros ??= new ProductoUnidadFiltros();

            var query = _context.ProductoUnidades
                .AsNoTracking()
                .Include(u => u.Cliente)
                .Include(u => u.VentaDetalle)
                .Where(u => u.ProductoId == productoId && !u.IsDeleted);

            if (filtros.SoloDisponibles)
                query = query.Where(u => u.Estado == EstadoUnidad.EnStock);

            if (filtros.SoloVendidas)
                query = query.Where(u => u.Estado == EstadoUnidad.Vendida);

            if (filtros.SoloSinNumeroSerie)
                query = query.Where(u => string.IsNullOrEmpty(u.NumeroSerie));

            if (filtros.Estado.HasValue)
                query = query.Where(u => u.Estado == filtros.Estado.Value);

            if (!string.IsNullOrWhiteSpace(filtros.Texto))
            {
                var texto = filtros.Texto.Trim();
                query = query.Where(u =>
                    u.CodigoInternoUnidad.Contains(texto) ||
                    (u.NumeroSerie != null && u.NumeroSerie.Contains(texto)));
            }

            return await query
                .OrderBy(u => u.CodigoInternoUnidad)
                .ToListAsync();
        }

        public async Task<ProductoUnidad?> ObtenerPorIdAsync(int productoUnidadId)
        {
            return await _context.ProductoUnidades
                .AsNoTracking()
                .Include(u => u.Producto)
                .Include(u => u.Cliente)
                .Include(u => u.VentaDetalle)
                .FirstOrDefaultAsync(u => u.Id == productoUnidadId && !u.IsDeleted);
        }

        public async Task<IEnumerable<ProductoUnidad>> ObtenerDisponiblesPorProductoAsync(int productoId)
        {
            return await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => u.ProductoId == productoId
                         && u.Estado == EstadoUnidad.EnStock
                         && !u.IsDeleted)
                .OrderBy(u => u.CodigoInternoUnidad)
                .ToListAsync();
        }

        public async Task<IEnumerable<ProductoUnidadMovimiento>> ObtenerHistorialAsync(int productoUnidadId)
        {
            return await _context.ProductoUnidadMovimientos
                .AsNoTracking()
                .Where(m => m.ProductoUnidadId == productoUnidadId)
                .OrderBy(m => m.FechaCambio)
                .ToListAsync();
        }

        #endregion

        #region Transiciones de estado

        public async Task<ProductoUnidad> MarcarVendidaAsync(
            int productoUnidadId,
            int ventaDetalleId,
            int? clienteId = null,
            string? usuario = null)
        {
            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.EnStock },
                EstadoUnidad.Vendida);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.Vendida;
            unidad.VentaDetalleId = ventaDetalleId;
            unidad.ClienteId = clienteId;
            unidad.FechaVenta = DateTime.UtcNow;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.Vendida,
                "Venta de unidad",
                $"VentaDetalle:{ventaDetalleId}",
                usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} marcada como Vendida. VentaDetalleId: {VentaDetalleId}",
                unidad.CodigoInternoUnidad, ventaDetalleId);

            return unidad;
        }

        public async Task<ProductoUnidad> MarcarFaltanteAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.EnStock },
                EstadoUnidad.Faltante);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.Faltante;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.Faltante,
                motivo, null, usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} marcada como Faltante. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> MarcarBajaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.EnStock, EstadoUnidad.Devuelta, EstadoUnidad.Faltante },
                EstadoUnidad.Baja);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.Baja;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.Baja,
                motivo, null, usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} marcada como Baja. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> ReintegrarAStockAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.Faltante, EstadoUnidad.Devuelta },
                EstadoUnidad.EnStock);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.EnStock;
            unidad.VentaDetalleId = null;
            unidad.ClienteId = null;
            unidad.FechaVenta = null;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.EnStock,
                motivo, null, usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} reintegrada a stock. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> RevertirVentaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.Vendida },
                EstadoUnidad.EnStock);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.EnStock;
            unidad.VentaDetalleId = null;
            unidad.ClienteId = null;
            unidad.FechaVenta = null;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.EnStock,
                motivo, "CancelacionVenta", usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} revertida a EnStock por cancelación. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> MarcarDevueltaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.Vendida },
                EstadoUnidad.Devuelta);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = EstadoUnidad.Devuelta;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, EstadoUnidad.Devuelta,
                motivo, null, usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} marcada como Devuelta. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        private async Task<ProductoUnidad> CargarYValidarTransicionAsync(
            int productoUnidadId,
            EstadoUnidad[] estadosPermitidos,
            EstadoUnidad estadoDestino)
        {
            var unidad = await _context.ProductoUnidades
                .FirstOrDefaultAsync(u => u.Id == productoUnidadId && !u.IsDeleted);

            if (unidad == null)
                throw new InvalidOperationException(
                    $"No existe la unidad con Id {productoUnidadId}.");

            if (!estadosPermitidos.Contains(unidad.Estado))
                throw new InvalidOperationException(
                    $"La unidad '{unidad.CodigoInternoUnidad}' está en estado '{unidad.Estado}' " +
                    $"y no permite la transición a '{estadoDestino}'.");

            return unidad;
        }

        private static ProductoUnidadMovimiento CrearMovimiento(
            int productoUnidadId,
            EstadoUnidad estadoAnterior,
            EstadoUnidad estadoNuevo,
            string motivo,
            string? origenReferencia,
            string? usuario)
            => new()
            {
                ProductoUnidadId = productoUnidadId,
                EstadoAnterior = estadoAnterior,
                EstadoNuevo = estadoNuevo,
                Motivo = motivo,
                OrigenReferencia = origenReferencia,
                UsuarioResponsable = usuario,
                FechaCambio = DateTime.UtcNow
            };

        #endregion

        #region Generación de código interno

        /// <summary>
        /// Genera el siguiente CodigoInternoUnidad para el producto.
        /// Formato: {ProductoCodigo}-U-{NNNN}
        /// El correlativo se calcula sobre el total histórico (incluye soft-deleted)
        /// para garantizar que el código sea siempre creciente y no colisione con
        /// registros activos previos.
        /// </summary>
        private async Task<string> GenerarCodigoInternoAsync(int productoId, string productoCodigo)
        {
            var total = await _context.ProductoUnidades
                .IgnoreQueryFilters()
                .CountAsync(u => u.ProductoId == productoId);

            var secuencia = total + 1;
            var baseCodigo = string.IsNullOrWhiteSpace(productoCodigo)
                ? productoId.ToString()
                : productoCodigo;

            return $"{baseCodigo}-U-{secuencia:D4}";
        }

        #endregion
    }
}
