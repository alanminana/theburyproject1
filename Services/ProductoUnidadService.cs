using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

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
