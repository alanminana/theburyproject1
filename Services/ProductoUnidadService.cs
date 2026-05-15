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

        public async Task<IReadOnlyList<ProductoUnidad>> CrearUnidadesAsync(
            int productoId,
            IReadOnlyCollection<string?> numerosSerie,
            string? ubicacionActual = null,
            string? observaciones = null,
            string? usuario = null)
        {
            if (numerosSerie == null || numerosSerie.Count == 0)
                throw new ArgumentException("Debe informar al menos una unidad para crear.", nameof(numerosSerie));

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var unidades = new List<ProductoUnidad>();

            try
            {
                foreach (var numeroSerie in numerosSerie)
                {
                    unidades.Add(await CrearUnidadAsync(
                        productoId,
                        numeroSerie,
                        ubicacionActual,
                        observaciones,
                        usuario));
                }

                await transaction.CommitAsync();
                return unidades;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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

        public async Task<ProductoUnidadConciliacionReadModel> ObtenerConciliacionPorProductoAsync(int productoId)
        {
            var producto = await _context.Productos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

            if (producto == null)
                throw new InvalidOperationException($"No existe el producto con Id {productoId}.");

            var conteosPorEstado = await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => u.ProductoId == productoId && !u.IsDeleted)
                .GroupBy(u => u.Estado)
                .Select(g => new { Estado = g.Key, Cantidad = g.Count() })
                .ToListAsync();

            var conteos = conteosPorEstado.ToDictionary(x => x.Estado, x => x.Cantidad);
            var unidadesEnStock = ObtenerConteo(conteos, EstadoUnidad.EnStock);

            var ultimoMovimientoStockFecha = await _context.MovimientosStock
                .AsNoTracking()
                .Where(m => m.ProductoId == productoId && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (DateTime?)m.CreatedAt)
                .FirstOrDefaultAsync();

            var ultimoMovimientoUnidadFecha = await _context.ProductoUnidadMovimientos
                .AsNoTracking()
                .Where(m => !m.IsDeleted
                         && !m.ProductoUnidad.IsDeleted
                         && m.ProductoUnidad.ProductoId == productoId)
                .OrderByDescending(m => m.FechaCambio)
                .Select(m => (DateTime?)m.FechaCambio)
                .FirstOrDefaultAsync();

            return new ProductoUnidadConciliacionReadModel
            {
                ProductoId = producto.Id,
                ProductoNombre = producto.Nombre,
                ProductoCodigo = producto.Codigo,
                RequiereNumeroSerie = producto.RequiereNumeroSerie,
                StockActual = producto.StockActual,
                UnidadesEnStock = unidadesEnStock,
                UnidadesVendidas = ObtenerConteo(conteos, EstadoUnidad.Vendida),
                UnidadesFaltantes = ObtenerConteo(conteos, EstadoUnidad.Faltante),
                UnidadesBaja = ObtenerConteo(conteos, EstadoUnidad.Baja),
                UnidadesDevueltas = ObtenerConteo(conteos, EstadoUnidad.Devuelta),
                UnidadesReservadas = ObtenerConteo(conteos, EstadoUnidad.Reservada),
                UnidadesEnReparacion = ObtenerConteo(conteos, EstadoUnidad.EnReparacion),
                TotalUnidadesActivas = conteos.Values.Sum(),
                DiferenciaStockVsUnidadesEnStock = producto.StockActual - unidadesEnStock,
                UltimoMovimientoStockFecha = ultimoMovimientoStockFecha,
                UltimoMovimientoUnidadFecha = ultimoMovimientoUnidadFecha
            };
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

        private static int ObtenerConteo(
            IReadOnlyDictionary<EstadoUnidad, int> conteos,
            EstadoUnidad estado)
            => conteos.TryGetValue(estado, out var cantidad) ? cantidad : 0;

        public async Task<ProductoUnidadesGlobalResultado> BuscarUnidadesGlobalAsync(
            ProductoUnidadesGlobalFiltros filtros)
        {
            filtros ??= new ProductoUnidadesGlobalFiltros();

            var query = _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => !u.IsDeleted && !u.Producto.IsDeleted);

            if (filtros.ProductoId.HasValue)
                query = query.Where(u => u.ProductoId == filtros.ProductoId.Value);

            if (filtros.Estado.HasValue)
                query = query.Where(u => u.Estado == filtros.Estado.Value);

            if (filtros.SoloDisponibles)
                query = query.Where(u => u.Estado == EstadoUnidad.EnStock);

            if (filtros.SoloVendidas)
                query = query.Where(u => u.Estado == EstadoUnidad.Vendida);

            if (filtros.SoloFaltantes)
                query = query.Where(u => u.Estado == EstadoUnidad.Faltante);

            if (filtros.SoloBaja)
                query = query.Where(u => u.Estado == EstadoUnidad.Baja);

            if (filtros.SoloDevueltas)
                query = query.Where(u => u.Estado == EstadoUnidad.Devuelta);

            if (filtros.SoloSinNumeroSerie)
                query = query.Where(u => string.IsNullOrEmpty(u.NumeroSerie));

            if (!string.IsNullOrWhiteSpace(filtros.Texto))
            {
                var texto = filtros.Texto.Trim();
                query = query.Where(u =>
                    u.CodigoInternoUnidad.Contains(texto) ||
                    (u.NumeroSerie != null && u.NumeroSerie.Contains(texto)) ||
                    u.Producto.Nombre.Contains(texto) ||
                    u.Producto.Codigo.Contains(texto));
            }

            var items = await query
                .OrderBy(u => u.Producto.Nombre)
                .ThenBy(u => u.CodigoInternoUnidad)
                .Select(u => new ProductoUnidadGlobalItem
                {
                    Id = u.Id,
                    ProductoId = u.ProductoId,
                    ProductoCodigo = u.Producto.Codigo,
                    ProductoNombre = u.Producto.Nombre,
                    CodigoInternoUnidad = u.CodigoInternoUnidad,
                    NumeroSerie = u.NumeroSerie,
                    Estado = u.Estado,
                    UbicacionActual = u.UbicacionActual,
                    FechaIngreso = u.FechaIngreso,
                    ClienteId = u.ClienteId,
                    ClienteNombre = u.Cliente != null
                        ? u.Cliente.Apellido + ", " + u.Cliente.Nombre
                        : null,
                    VentaDetalleId = u.VentaDetalleId,
                    FechaVenta = u.FechaVenta
                })
                .ToListAsync();

            return new ProductoUnidadesGlobalResultado
            {
                TotalUnidades = items.Count,
                TotalEnStock = items.Count(i => i.Estado == EstadoUnidad.EnStock),
                TotalVendidas = items.Count(i => i.Estado == EstadoUnidad.Vendida),
                TotalFaltantes = items.Count(i => i.Estado == EstadoUnidad.Faltante),
                TotalBaja = items.Count(i => i.Estado == EstadoUnidad.Baja),
                TotalDevueltas = items.Count(i => i.Estado == EstadoUnidad.Devuelta),
                TotalEnReparacion = items.Count(i => i.Estado == EstadoUnidad.EnReparacion),
                TotalAnuladas = items.Count(i => i.Estado == EstadoUnidad.Anulada),
                TotalReservadas = items.Count(i => i.Estado == EstadoUnidad.Reservada),
                Items = items
            };
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
                motivo, "AjusteUnidad:Faltante", usuario));

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
                motivo, "AjusteUnidad:Baja", usuario));

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
                motivo, "AjusteUnidad:Reintegro", usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} reintegrada a stock. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> RevertirVentaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null,
            string? origenReferencia = null)
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
                motivo, origenReferencia ?? "CancelacionVenta", usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Unidad {Codigo} revertida a EnStock por cancelación. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, motivo);

            return unidad;
        }

        public async Task<ProductoUnidad> FinalizarReparacionAsync(
            int productoUnidadId,
            EstadoUnidad estadoDestino,
            string motivo,
            string? usuario = null)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));

            var destinosPermitidos = new[] { EstadoUnidad.EnStock, EstadoUnidad.Baja, EstadoUnidad.Devuelta };
            if (!destinosPermitidos.Contains(estadoDestino))
                throw new ArgumentException(
                    $"El estado destino '{estadoDestino}' no es válido para finalizar una reparación. " +
                    $"Los estados permitidos son: EnStock, Baja, Devuelta.",
                    nameof(estadoDestino));

            var unidad = await CargarYValidarTransicionAsync(
                productoUnidadId,
                new[] { EstadoUnidad.EnReparacion },
                estadoDestino);

            var estadoAnterior = unidad.Estado;
            unidad.Estado = estadoDestino;

            _context.ProductoUnidadMovimientos.Add(CrearMovimiento(
                unidad.Id, estadoAnterior, estadoDestino,
                motivo,
                $"FinalizacionReparacion:{unidad.Id}",
                usuario));

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Reparación finalizada: unidad {Codigo} pasó de {EstadoAnterior} a {EstadoDestino}. Motivo: {Motivo}",
                unidad.CodigoInternoUnidad, estadoAnterior, estadoDestino, motivo);

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
