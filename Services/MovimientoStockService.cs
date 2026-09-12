using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class MovimientoStockService : IMovimientoStockService
    {
        /// <summary>Cantidad máxima permitida por movimiento de stock.</summary>
        private const decimal MaxCantidadMovimiento = 999999.99m;
        private const string FuenteCostoProductoActual = "ProductoActual";
        private const string FuenteCostoAjusteManual = "AjusteManual";
        private const string FuenteCostoNoInformado = "NoInformado";

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
                .Where(m => !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<MovimientoStock?> GetByIdAsync(int id)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
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
                .Where(m => m.OrdenCompraId == ordenCompraId && !m.IsDeleted)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => m.Tipo == tipo && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta)
        {
            return await _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => m.CreatedAt >= fechaDesde && m.CreatedAt <= fechaHasta && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Construye la query filtrada y ordenada que usan <see cref="SearchAsync"/> y
        /// <see cref="SearchPaginadoAsync"/> — una sola autoridad de filtrado para no
        /// duplicar criterios entre el listado completo y el paginado.
        /// </summary>
        private IQueryable<MovimientoStock> AplicarFiltros(
            int? productoId,
            TipoMovimiento? tipo,
            DateTime? fechaDesde,
            DateTime? fechaHasta,
            string? orderBy,
            string? orderDirection)
        {
            var query = _context.MovimientosStock
                .AsNoTracking()
                .Include(m => m.Producto)
                .Include(m => m.OrdenCompra)
                .Where(m => !m.IsDeleted)
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

            return query;
        }

        public async Task<IEnumerable<MovimientoStock>> SearchAsync(
            int? productoId = null,
            TipoMovimiento? tipo = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "desc")
        {
            var query = AplicarFiltros(productoId, tipo, fechaDesde, fechaHasta, orderBy, orderDirection);
            return await query.ToListAsync();
        }

        /// <summary>
        /// Igual que <see cref="SearchAsync"/> pero devuelve una sola página (Skip/Take), más
        /// el total de registros y los agregados (entradas/salidas/ajustes) calculados sobre
        /// todo el filtro completo, no solo la página — MovimientoStock/Index no traía todo el
        /// histórico sin límite antes de esto, y sus tarjetas de resumen deben seguir reflejando
        /// el total real filtrado, no solo lo que entra en una página (mismo criterio que
        /// AlertaStockService.ContarPorEstadoAsync).
        /// </summary>
        public async Task<(IEnumerable<MovimientoStock> Items, int Total, decimal TotalEntradas, decimal TotalSalidas, int TotalAjustes)> SearchPaginadoAsync(
            int? productoId = null,
            TipoMovimiento? tipo = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "desc",
            int pageNumber = 1,
            int pageSize = 20)
        {
            var query = AplicarFiltros(productoId, tipo, fechaDesde, fechaHasta, orderBy, orderDirection);

            var total = await query.CountAsync();

            // GroupBy + Sum(decimal) no traduce en el proveedor Sqlite ("cannot apply
            // aggregate operator 'Sum' on expressions of type 'decimal'"); se trae solo
            // Tipo+Cantidad (liviano comparado con las entidades completas con Include de
            // abajo) y se suma en memoria.
            var filasPorTipo = await query
                .Select(m => new { m.Tipo, m.Cantidad })
                .ToListAsync();

            var totalEntradas = filasPorTipo.Where(f => f.Tipo == TipoMovimiento.Entrada).Sum(f => f.Cantidad);
            var totalSalidas = Math.Abs(filasPorTipo.Where(f => f.Tipo == TipoMovimiento.Salida).Sum(f => f.Cantidad));
            var totalAjustes = filasPorTipo.Count(f => f.Tipo == TipoMovimiento.Ajuste);

            var paginaValida = pageNumber < 1 ? 1 : pageNumber;
            var tamanioValido = pageSize < 1 ? 20 : pageSize;

            var items = await query
                .Skip((paginaValida - 1) * tamanioValido)
                .Take(tamanioValido)
                .ToListAsync();

            return (items, total, totalEntradas, totalSalidas, totalAjustes);
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

            Producto? producto = null;
            if (movimiento.CostoUnitarioAlMomento <= 0m || movimiento.CostoTotalAlMomento <= 0m)
            {
                producto = await _context.Productos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == movimiento.ProductoId && !p.IsDeleted);
            }

            CompletarCostoMovimiento(movimiento, producto, FuenteCostoProductoActual);

            _context.MovimientosStock.Add(movimiento);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Movimiento registrado: Producto {ProductoId}, Tipo {Tipo}, Cantidad {Cantidad}",
                movimiento.ProductoId, movimiento.Tipo, movimiento.Cantidad);

            return movimiento;
        }

        /// <summary>
        /// Registrar ajuste con transacción, validación de cantidad y usuario real.
        /// Para Entrada/Salida: cantidad debe ser mayor a 0.
        /// Para Ajuste: cantidad representa el stock absoluto (mayor o igual a 0). El movimiento registra la diferencia (delta).
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
            // Validación de cantidad
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

            await using var transaction = await BeginTransactionIfNeededAsync();

            try
            {
                var producto = await _context.Productos
                    .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

                if (producto == null)
                    throw new InvalidOperationException($"Producto {productoId} no encontrado");

                var stockAnterior = producto.StockActual;

                // Stock insuficiente para salidas
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

                CompletarCostoMovimiento(movimiento, producto, FuenteCostoAjusteManual);

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
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargá e intentá nuevamente.");
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
            int? ordenCompraId = null,
            IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
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

            await using var transaction = await BeginTransactionIfNeededAsync();

            try
            {
                var productosById = await CargarProductosActivosOThrowAsync(entradas.Select(e => e.productoId));

                var movimientos = new List<MovimientoStock>(entradas.Count);

                for (var i = 0; i < entradas.Count; i++)
                {
                    var (productoId, cantidad, referencia) = entradas[i];
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

                    AplicarCostoInformado(movimiento, costos, i);
                    CompletarCostoMovimiento(movimiento, producto, FuenteCostoProductoActual);

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
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargá e intentá nuevamente.");
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
            string? usuarioActual = null,
            IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
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

            await using var transaction = await BeginTransactionIfNeededAsync();

            try
            {
                var productosById = await CargarProductosActivosOThrowAsync(totalsByProducto.Keys);

                foreach (var (productoId, totalCantidad) in totalsByProducto)
                {
                    var producto = productosById[productoId];
                    if (producto.StockActual < totalCantidad)
                        throw new InvalidOperationException(
                            $"Stock insuficiente. ProductoId: {productoId}, Disponible: {producto.StockActual}, Solicitado: {totalCantidad}");
                }

                var movimientos = new List<MovimientoStock>(salidas.Count);

                for (var i = 0; i < salidas.Count; i++)
                {
                    var (productoId, cantidad, referencia) = salidas[i];
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

                    AplicarCostoInformado(movimiento, costos, i);
                    CompletarCostoMovimiento(movimiento, producto, FuenteCostoProductoActual);

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
                throw new InvalidOperationException("El stock fue modificado por otro usuario/proceso. Recargá e intentá nuevamente.");
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Validar que cantidad sea positiva (para Entrada/Salida).
        /// </summary>
        public async Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad)
        {
            return await Task.FromResult(ValidarCantidadSync(cantidad));
        }

        private (bool Valido, string Mensaje) ValidarCantidadSync(decimal cantidad)
        {
            if (cantidad <= 0)
                return (false, "La cantidad debe ser mayor a 0");

            if (cantidad > MaxCantidadMovimiento)
                return (false, $"La cantidad no puede exceder {MaxCantidadMovimiento}");

            return (true, "Cantidad válida");
        }

        private static void CompletarCostoMovimiento(
            MovimientoStock movimiento,
            Producto? producto,
            string fuenteFallback)
        {
            if (movimiento.CostoUnitarioAlMomento > 0m)
            {
                movimiento.CostoUnitarioAlMomento = RedondearMoneda(movimiento.CostoUnitarioAlMomento);

                if (movimiento.CostoTotalAlMomento <= 0m)
                {
                    movimiento.CostoTotalAlMomento = RedondearMoneda(
                        movimiento.CostoUnitarioAlMomento * Math.Abs(movimiento.Cantidad));
                }
                else
                {
                    movimiento.CostoTotalAlMomento = RedondearMoneda(movimiento.CostoTotalAlMomento);
                }

                if (string.IsNullOrWhiteSpace(movimiento.FuenteCosto))
                    movimiento.FuenteCosto = FuenteCostoNoInformado;

                return;
            }

            if (producto?.PrecioCompra > 0m)
            {
                movimiento.CostoUnitarioAlMomento = RedondearMoneda(producto.PrecioCompra);
                movimiento.CostoTotalAlMomento = RedondearMoneda(producto.PrecioCompra * Math.Abs(movimiento.Cantidad));
                movimiento.FuenteCosto = fuenteFallback;
                return;
            }

            movimiento.CostoUnitarioAlMomento = 0m;
            movimiento.CostoTotalAlMomento = 0m;
            movimiento.FuenteCosto = FuenteCostoNoInformado;
        }

        private static decimal RedondearMoneda(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static void AplicarCostoInformado(
            MovimientoStock movimiento,
            IReadOnlyList<MovimientoStockCostoLinea>? costos,
            int index)
        {
            if (costos == null || index >= costos.Count)
                return;

            var costo = costos[index];
            if (costo.ProductoId != movimiento.ProductoId || costo.Cantidad != movimiento.Cantidad)
                return;

            if (costo.CostoUnitario > 0m)
                movimiento.CostoUnitarioAlMomento = costo.CostoUnitario;

            if (!string.IsNullOrWhiteSpace(costo.FuenteCosto))
                movimiento.FuenteCosto = costo.FuenteCosto!;
        }

        /// <summary>
        /// Abre una transacción propia solo si no hay una ambiente en curso (ej. cuando el
        /// caller ya está dentro de una transacción de nivel superior, como VentaService).
        /// Única autoridad de este chequeo — antes se repetía igual en RegistrarAjusteAsync,
        /// RegistrarEntradasAsync y RegistrarSalidasAsync.
        /// </summary>
        private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync()
        {
            var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
            return hasAmbientTransaction ? null : await _context.Database.BeginTransactionAsync();
        }

        /// <summary>
        /// Carga los productos activos correspondientes a <paramref name="productoIds"/> o lanza
        /// si falta alguno. Única autoridad de este fetch — antes se repetía igual (mismo query,
        /// mismo diccionario, mismo chequeo de faltantes) en RegistrarEntradasAsync y
        /// RegistrarSalidasAsync.
        /// </summary>
        private async Task<Dictionary<int, Producto>> CargarProductosActivosOThrowAsync(IEnumerable<int> productoIds)
        {
            var ids = productoIds.Distinct().ToList();

            var productos = await _context.Productos
                .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
                .ToListAsync();

            var productosById = productos.ToDictionary(p => p.Id);

            var missingIds = ids.Where(id => !productosById.ContainsKey(id)).ToList();
            if (missingIds.Count > 0)
                throw new InvalidOperationException($"Producto(s) no encontrado(s): {string.Join(", ", missingIds)}");

            return productosById;
        }

        /// <summary>
        /// Validar disponibilidad de stock (para uso por otros servicios).
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
