// ✅ REFACTORIZADO: Transacciones, sin duplicación, optimizado
// ✅ AJUSTE ADICIONAL: GetAllAsync también filtra soft-delete (IsDeleted)

using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class MovimientoStockService : IMovimientoStockService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MovimientoStockService> _logger;

        public MovimientoStockService(
            AppDbContext context,
            ILogger<MovimientoStockService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Obtener Movimientos

        public async Task<IEnumerable<MovimientoStock>> GetAllAsync()
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<MovimientoStock?> GetByIdAsync(int id)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted);
        }

        public async Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.OrdenCompra)
                .Where(m => m.ProductoId == productoId && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Where(m => m.OrdenCompraId == ordenCompraId && !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => m.Tipo == tipo && !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => m.CreatedAt >= fechaDesde && m.CreatedAt <= fechaHasta && !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> SearchAsync(
            int? productoId = null,
            TipoMovimiento? tipo = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "desc")
        {
            var query = _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => !m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted)
                .AsQueryable();

            if (productoId.HasValue)
                query = query.Where(m => m.ProductoId == productoId.Value);

            if (tipo.HasValue)
                query = query.Where(m => m.Tipo == tipo.Value);

            if (fechaDesde.HasValue)
                query = query.Where(m => m.CreatedAt >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(m => m.CreatedAt <= fechaHasta.Value);

            var desc = string.Equals(orderDirection, "desc", StringComparison.OrdinalIgnoreCase);

            query = orderBy?.ToLower() switch
            {
                "fecha" => desc ? query.OrderByDescending(m => m.CreatedAt) : query.OrderBy(m => m.CreatedAt),
                "producto" => desc ? query.OrderByDescending(m => m.Producto.Nombre) : query.OrderBy(m => m.Producto.Nombre),
                "tipo" => desc ? query.OrderByDescending(m => m.Tipo) : query.OrderBy(m => m.Tipo),
                "cantidad" => desc ? query.OrderByDescending(m => m.Cantidad) : query.OrderBy(m => m.Cantidad),
                _ => query.OrderByDescending(m => m.CreatedAt)
            };

            return await query.ToListAsync();
        }

        #endregion

        #region Crear / Actualizar Stock

        public async Task<MovimientoStock> CreateAsync(MovimientoStock movimiento)
        {
            // Defaults defensivos (si no vienen seteados por el llamador)
            if (movimiento.CreatedAt == default)
                movimiento.CreatedAt = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(movimiento.CreatedBy))
                movimiento.CreatedBy = "Sistema";

            _context.MovimientosStock.Add(movimiento);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Movimiento registrado: Producto {ProductoId}, Tipo {Tipo}, Cantidad {Cantidad}",
                movimiento.ProductoId, movimiento.Tipo, movimiento.Cantidad);

            return movimiento;
        }

        /// <summary>
        /// ✅ Con TRANSACCIÓN, validación de cantidad y usuario real.
        /// - Para Entrada/Salida: cantidad debe ser > 0
        /// - Para Ajuste: cantidad representa el stock absoluto (>= 0). El movimiento registra la diferencia (delta).
        /// </summary>
        public async Task<MovimientoStock> RegistrarAjusteAsync(
            int productoId,
            TipoMovimiento tipo,
            decimal cantidad,
            string? referencia,
            string motivo,
            string? usuarioActual = null,
            int? ordenCompraId = null)
        {
            // ✅ VALIDACIÓN 1: Cantidad
            // - Entrada/Salida: debe ser > 0
            // - Ajuste: representa el stock absoluto, permite 0 (no permite negativo)
            if (tipo == TipoMovimiento.Ajuste)
            {
                if (cantidad < 0)
                    throw new InvalidOperationException("La cantidad de ajuste no puede ser negativa");
            }
            else
            {
                var (valido, mensaje) = await ValidarCantidadAsync(cantidad);
                if (!valido)
                    throw new InvalidOperationException(mensaje);
            }

            var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
            await using var transaction = hasAmbientTransaction
                ? null
                : await _context.Database.BeginTransactionAsync();

            try
            {
                var producto = await _context.Productos
                    .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

                if (producto == null)
                    throw new InvalidOperationException($"Producto {productoId} no encontrado");

                var stockAnterior = producto.StockActual;

                // ✅ VALIDACIÓN 2: Stock insuficiente para salidas
                if (tipo == TipoMovimiento.Salida && producto.StockActual < cantidad)
                {
                    throw new InvalidOperationException(
                        $"Stock insuficiente. Disponible: {producto.StockActual}, Solicitado: {cantidad}");
                }

                // Actualizar stock según tipo
                switch (tipo)
                {
                    case TipoMovimiento.Entrada:
                        producto.StockActual += cantidad;
                        break;
                    case TipoMovimiento.Salida:
                        producto.StockActual -= cantidad;
                        break;
                    case TipoMovimiento.Ajuste:
                        producto.StockActual = cantidad; // stock absoluto
                        break;
                    default:
                        throw new InvalidOperationException($"Tipo de movimiento no soportado: {tipo}");
                }

                if (producto.StockActual < 0)
                    throw new InvalidOperationException("El stock resultante no puede ser negativo");

                producto.UpdatedAt = DateTime.UtcNow;
                producto.UpdatedBy = string.IsNullOrWhiteSpace(usuarioActual) ? "Sistema" : usuarioActual;

                // Crear movimiento con usuario real
                var movimiento = new MovimientoStock
                {
                    ProductoId = productoId,
                    Tipo = tipo,
                    // Ajuste: registra delta; Entrada/Salida: registra cantidad tal cual
                    Cantidad = tipo == TipoMovimiento.Ajuste ? (cantidad - stockAnterior) : cantidad,
                    StockAnterior = stockAnterior,
                    StockNuevo = producto.StockActual,
                    Referencia = referencia,
                    OrdenCompraId = ordenCompraId,
                    Motivo = motivo,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = string.IsNullOrWhiteSpace(usuarioActual) ? "Sistema" : usuarioActual
                };

                _context.MovimientosStock.Add(movimiento);
                await _context.SaveChangesAsync();

                if (transaction != null)
                    await transaction.CommitAsync();

                _logger.LogInformation(
                    "Movimiento (TRANSACCIÓN): Producto {ProductoId}, Tipo {Tipo}, Stock {Anterior} → {Nuevo}, Usuario {Usuario}",
                    productoId, tipo, stockAnterior, producto.StockActual, movimiento.CreatedBy);

                return movimiento;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Conflicto de concurrencia al ajustar stock - ProductoId {ProductoId}", productoId);
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargue e intente nuevamente.");
            }
            catch (Exception ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                _logger.LogError(ex, "Error en RegistrarAjusteAsync - Transacción revertida");
                throw;
            }
        }

        public async Task<List<MovimientoStock>> RegistrarEntradasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> entradas,
            string motivo,
            string? usuarioActual = null,
            int? ordenCompraId = null)
        {
            if (entradas == null || entradas.Count == 0)
                return new List<MovimientoStock>();

            var usuario = string.IsNullOrWhiteSpace(usuarioActual) ? "Sistema" : usuarioActual;
            var ahora = DateTime.UtcNow;

            foreach (var (_, cantidad, _) in entradas)
            {
                var (valido, mensaje) = ValidarCantidadSync(cantidad);
                if (!valido)
                    throw new InvalidOperationException(mensaje);
            }

            var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
            await using var transaction = hasAmbientTransaction
                ? null
                : await _context.Database.BeginTransactionAsync();

            try
            {
                var productoIds = entradas
                    .Select(e => e.productoId)
                    .Distinct()
                    .ToList();

                var productos = await _context.Productos
                    .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                    .ToListAsync();

                var productosById = productos.ToDictionary(p => p.Id);

                var missingIds = productoIds.Where(id => !productosById.ContainsKey(id)).ToList();
                if (missingIds.Count > 0)
                    throw new InvalidOperationException($"Producto(s) no encontrado(s): {string.Join(", ", missingIds)}");

                var movimientos = new List<MovimientoStock>(entradas.Count);

                foreach (var (productoId, cantidad, referencia) in entradas)
                {
                    if (cantidad <= 0)
                        continue;

                    var producto = productosById[productoId];
                    var stockAnterior = producto.StockActual;

                    producto.StockActual += cantidad;

                    if (producto.StockActual < 0)
                        throw new InvalidOperationException("El stock resultante no puede ser negativo");

                    producto.UpdatedAt = ahora;
                    producto.UpdatedBy = usuario;

                    var movimiento = new MovimientoStock
                    {
                        ProductoId = productoId,
                        Tipo = TipoMovimiento.Entrada,
                        Cantidad = cantidad,
                        StockAnterior = stockAnterior,
                        StockNuevo = producto.StockActual,
                        Referencia = referencia,
                        OrdenCompraId = ordenCompraId,
                        Motivo = motivo,
                        CreatedAt = ahora,
                        CreatedBy = usuario
                    };

                    movimientos.Add(movimiento);
                }

                if (movimientos.Count > 0)
                {
                    _context.MovimientosStock.AddRange(movimientos);
                    await _context.SaveChangesAsync();
                }

                if (transaction != null)
                    await transaction.CommitAsync();

                _logger.LogInformation(
                    "Entradas registradas (BATCH): {Cantidad} movimientos, OrdenCompraId {OrdenCompraId}, Usuario {Usuario}",
                    movimientos.Count, ordenCompraId, usuario);

                return movimientos;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();

                _logger.LogWarning(ex, "Conflicto de concurrencia al registrar entradas batch - OrdenCompraId {OrdenCompraId}", ordenCompraId);
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargue e intente nuevamente.");
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<MovimientoStock>> RegistrarSalidasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> salidas,
            string motivo,
            string? usuarioActual = null)
        {
            if (salidas == null || salidas.Count == 0)
                return new List<MovimientoStock>();

            var usuario = string.IsNullOrWhiteSpace(usuarioActual) ? "Sistema" : usuarioActual;
            var ahora = DateTime.UtcNow;

            foreach (var (_, cantidad, _) in salidas)
            {
                var (valido, mensaje) = ValidarCantidadSync(cantidad);
                if (!valido)
                    throw new InvalidOperationException(mensaje);
            }

            var totalsByProducto = salidas
                .GroupBy(s => s.productoId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.cantidad));

            var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
            await using var transaction = hasAmbientTransaction
                ? null
                : await _context.Database.BeginTransactionAsync();

            try
            {
                var productoIds = totalsByProducto.Keys.ToList();

                var productos = await _context.Productos
                    .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                    .ToListAsync();

                var productosById = productos.ToDictionary(p => p.Id);

                var missingIds = productoIds.Where(id => !productosById.ContainsKey(id)).ToList();
                if (missingIds.Count > 0)
                    throw new InvalidOperationException($"Producto(s) no encontrado(s): {string.Join(", ", missingIds)}");

                foreach (var (productoId, totalCantidad) in totalsByProducto)
                {
                    var producto = productosById[productoId];
                    if (producto.StockActual < totalCantidad)
                        throw new InvalidOperationException(
                            $"Stock insuficiente. ProductoId: {productoId}, Disponible: {producto.StockActual}, Solicitado: {totalCantidad}");
                }

                var movimientos = new List<MovimientoStock>(salidas.Count);

                foreach (var (productoId, cantidad, referencia) in salidas)
                {
                    if (cantidad <= 0)
                        continue;

                    var producto = productosById[productoId];
                    var stockAnterior = producto.StockActual;

                    producto.StockActual -= cantidad;

                    if (producto.StockActual < 0)
                        throw new InvalidOperationException("El stock resultante no puede ser negativo");

                    producto.UpdatedAt = ahora;
                    producto.UpdatedBy = usuario;

                    var movimiento = new MovimientoStock
                    {
                        ProductoId = productoId,
                        Tipo = TipoMovimiento.Salida,
                        Cantidad = cantidad,
                        StockAnterior = stockAnterior,
                        StockNuevo = producto.StockActual,
                        Referencia = referencia,
                        Motivo = motivo,
                        CreatedAt = ahora,
                        CreatedBy = usuario
                    };

                    movimientos.Add(movimiento);
                }

                if (movimientos.Count > 0)
                {
                    _context.MovimientosStock.AddRange(movimientos);
                    await _context.SaveChangesAsync();
                }

                if (transaction != null)
                    await transaction.CommitAsync();

                _logger.LogInformation(
                    "Salidas registradas (BATCH): {Cantidad} movimientos, Usuario {Usuario}",
                    movimientos.Count, usuario);

                return movimientos;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();

                _logger.LogWarning(ex, "Conflicto de concurrencia al registrar salidas batch");
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargue e intente nuevamente.");
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// ✅ Validar que cantidad sea positiva (para Entrada/Salida).
        /// </summary>
        public async Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad)
        {
            return await Task.FromResult(ValidarCantidadSync(cantidad));
        }

        private (bool Valido, string Mensaje) ValidarCantidadSync(decimal cantidad)
        {
            if (cantidad <= 0)
                return (false, "La cantidad debe ser mayor a 0");

            if (cantidad > 999999.99m)
                return (false, "La cantidad no puede exceder 999999.99");

            return (true, "Cantidad válida");
        }

        /// <summary>
        /// ✅ Validar disponibilidad de stock (para uso por otros servicios).
        /// </summary>
        public async Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad)
        {
            var producto = await _context.Productos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

            return producto != null && producto.StockActual >= cantidad;
        }

        #endregion
    }
}
