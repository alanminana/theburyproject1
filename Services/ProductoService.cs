using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para productos
    /// - Validaciones centralizadas
    /// - Logging mejorado
    /// </summary>
    public class ProductoService : IProductoService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProductoService> _logger;
        private readonly IPrecioHistoricoService _precioHistoricoService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IPrecioVigenteResolver _precioVigenteResolver;

        public ProductoService(
            AppDbContext context,
            ILogger<ProductoService> logger,
            IPrecioHistoricoService precioHistoricoService,
            ICurrentUserService currentUserService,
            IPrecioVigenteResolver precioVigenteResolver)
        {
            _context = context;
            _logger = logger;
            _precioHistoricoService = precioHistoricoService;
            _currentUserService = currentUserService;
            _precioVigenteResolver = precioVigenteResolver;
        }

        #region CRUD Básico

        public async Task<IEnumerable<Producto>> GetAllAsync()
        {
            try
            {
                return await _context.Productos
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted)
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Marca)
                    .Include(p => p.AlicuotaIVA)
                    .Include(p => p.Caracteristicas.Where(c => !c.IsDeleted))
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los productos");
                throw;
            }
        }

        public async Task<Producto?> GetByIdAsync(int id)
        {
            try
            {
                return await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Marca)
                    .Include(p => p.AlicuotaIVA)
                    .Include(p => p.Caracteristicas.Where(c => !c.IsDeleted))
                    .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener producto {Id}", id);
                throw;
            }
        }

        public async Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId)
        {
            try
            {
                return await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Marca)
                    .Include(p => p.AlicuotaIVA)
                    .Where(p => p.CategoriaId == categoriaId && !p.IsDeleted)
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos por categoría {CategoriaId}", categoriaId);
                throw;
            }
        }

        public async Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId)
        {
            try
            {
                return await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Marca)
                    .Include(p => p.AlicuotaIVA)
                    .Where(p => p.MarcaId == marcaId && !p.IsDeleted)
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos por marca {MarcaId}", marcaId);
                throw;
            }
        }

        public async Task<IEnumerable<Producto>> GetProductosConStockBajoAsync()
        {
            try
            {
                return await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Marca)
                    .Include(p => p.AlicuotaIVA)
                    .Where(p => p.StockActual <= p.StockMinimo && !p.IsDeleted)
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos con stock bajo");
                throw;
            }
        }

        #endregion

        #region Búsqueda

        public async Task<IEnumerable<Producto>> SearchAsync(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            try
            {
                var query = _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                    .Include(p => p.Subcategoria)
                    .Include(p => p.Marca)
                    .Include(p => p.Submarca)
                    .Include(p => p.AlicuotaIVA)
                    .Include(p => p.Caracteristicas.Where(c => !c.IsDeleted))
                    .Where(p => !p.IsDeleted)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.ToLower();
                    query = query.Where(p =>
                        p.Codigo.ToLower().Contains(term) ||
                        p.Nombre.ToLower().Contains(term) ||
                        (p.Descripcion != null && p.Descripcion.ToLower().Contains(term)) ||
                        (p.Marca != null && p.Marca.Nombre.ToLower().Contains(term)) ||
                        (p.Submarca != null && p.Submarca.Nombre.ToLower().Contains(term)) ||
                        (p.Categoria != null && p.Categoria.Nombre.ToLower().Contains(term)) ||
                        (p.Subcategoria != null && p.Subcategoria.Nombre.ToLower().Contains(term)) ||
                        p.Caracteristicas.Any(c => !c.IsDeleted &&
                            (c.Nombre.ToLower().Contains(term) || c.Valor.ToLower().Contains(term))));
                }

                if (categoriaId.HasValue)
                    query = query.Where(p => p.CategoriaId == categoriaId.Value);

                if (marcaId.HasValue)
                    query = query.Where(p => p.MarcaId == marcaId.Value);

                if (stockBajo)
                    query = query.Where(p => p.StockActual <= p.StockMinimo);

                if (soloActivos)
                    query = query.Where(p => p.Activo);

                var ascending = orderDirection?.ToLower() != "desc";
                query = orderBy?.ToLower() switch
                {
                    "codigo" => ascending ? query.OrderBy(p => p.Codigo) : query.OrderByDescending(p => p.Codigo),
                    "nombre" => ascending ? query.OrderBy(p => p.Nombre) : query.OrderByDescending(p => p.Nombre),
                    "preciocompra" => ascending ? query.OrderBy(p => p.PrecioCompra) : query.OrderByDescending(p => p.PrecioCompra),
                    "precioventa" => ascending ? query.OrderBy(p => p.PrecioVenta) : query.OrderByDescending(p => p.PrecioVenta),
                    "stock" => ascending ? query.OrderBy(p => p.StockActual) : query.OrderByDescending(p => p.StockActual),
                    "categoria" => ascending ? query.OrderBy(p => p.Categoria != null ? p.Categoria.Nombre : string.Empty) : query.OrderByDescending(p => p.Categoria != null ? p.Categoria.Nombre : string.Empty),
                    "marca" => ascending ? query.OrderBy(p => p.Marca != null ? p.Marca.Nombre : string.Empty) : query.OrderByDescending(p => p.Marca != null ? p.Marca.Nombre : string.Empty),
                    _ => query.OrderBy(p => p.Nombre)
                };

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar productos");
                throw;
            }
        }

        public async Task<List<int>> SearchIdsAsync(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false)
        {
            var query = _context.Productos
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(p =>
                    p.Codigo.ToLower().Contains(term) ||
                    p.Nombre.ToLower().Contains(term) ||
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(term)));
            }

            if (categoriaId.HasValue)
                query = query.Where(p => p.CategoriaId == categoriaId.Value);

            if (marcaId.HasValue)
                query = query.Where(p => p.MarcaId == marcaId.Value);

            if (stockBajo)
                query = query.Where(p => p.StockActual <= p.StockMinimo);

            if (soloActivos)
                query = query.Where(p => p.Activo);

            return await query.Select(p => p.Id).ToListAsync();
        }

        #endregion

        #region Crear / Actualizar

        public async Task<Producto> CreateAsync(Producto producto)
        {
            try
            {
                ValidarProducto(producto);

                if (await ExistsCodigoAsync(producto.Codigo))
                    throw new InvalidOperationException($"Ya existe un producto con el código '{producto.Codigo}'");

                if (producto.AlicuotaIVAId.HasValue)
                    await AsegurarAlicuotaIVAActivaAsync(producto.AlicuotaIVAId.Value);

                producto.CreatedAt = DateTime.UtcNow;
                producto.Caracteristicas = NormalizarCaracteristicas(producto.Caracteristicas)
                    .Select(c => new ProductoCaracteristica
                    {
                        Nombre = c.Nombre,
                        Valor = c.Valor
                    })
                    .ToList();

                var usuario = _currentUserService.GetUsername();

                // Si viene stock inicial > 0, dejamos trazabilidad en MovimientosStock.
                using var tx = await _context.Database.BeginTransactionAsync();

                _context.Productos.Add(producto);
                await _context.SaveChangesAsync();

                if (producto.StockActual > 0)
                {
                    var movimientoInicial = new MovimientoStock
                    {
                        ProductoId = producto.Id,
                        Tipo = TipoMovimiento.Entrada,
                        Cantidad = producto.StockActual,
                        StockAnterior = 0,
                        StockNuevo = producto.StockActual,
                        CostoUnitarioAlMomento = Math.Round(producto.PrecioCompra, 2, MidpointRounding.AwayFromZero),
                        CostoTotalAlMomento = Math.Round(producto.PrecioCompra * Math.Abs(producto.StockActual), 2, MidpointRounding.AwayFromZero),
                        FuenteCosto = "ProductoActual",
                        Referencia = "Stock inicial",
                        Motivo = "Stock inicial al crear producto",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = usuario
                    };

                    _context.MovimientosStock.Add(movimientoInicial);
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();

                _logger.LogInformation("Producto creado: {Codigo} - {Nombre}", producto.Codigo, producto.Nombre);
                return producto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear producto {Codigo}", producto.Codigo);
                throw;
            }
        }

        public async Task PrepararPrecioVentaConIvaAsync(Producto producto)
        {
            producto.PorcentajeIVA = await ResolverPorcentajeIVAFormularioAsync(
                producto.AlicuotaIVAId,
                producto.PorcentajeIVA);

            producto.PrecioVenta = PrecioIvaCalculator.AplicarIVA(
                producto.PrecioVenta,
                producto.PorcentajeIVA);
        }

        public decimal ObtenerPrecioVentaSinIva(decimal precioVentaConIva, decimal porcentajeIVA)
            => PrecioIvaCalculator.QuitarIVA(precioVentaConIva, porcentajeIVA);

        public async Task<Producto> UpdateAsync(Producto producto)
        {
            try
            {
                var existing = await _context.Productos
                    .Include(p => p.Caracteristicas.Where(c => !c.IsDeleted))
                    .FirstOrDefaultAsync(p => p.Id == producto.Id && !p.IsDeleted);

                if (existing == null)
                    throw new InvalidOperationException($"No se encontró el producto con ID {producto.Id}");

                ValidarProducto(producto);

                if (await ExistsCodigoAsync(producto.Codigo, producto.Id))
                    throw new InvalidOperationException($"Ya existe otro producto con el código '{producto.Codigo}'");

                if (producto.AlicuotaIVAId.HasValue)
                    await AsegurarAlicuotaIVAActivaAsync(producto.AlicuotaIVAId.Value);

                var preciosCambiaron =
                    existing.PrecioCompra != producto.PrecioCompra ||
                    existing.PrecioVenta != producto.PrecioVenta;

                if (preciosCambiaron)
                {
                    var usuario = _currentUserService.GetUsername();

                    await _precioHistoricoService.RegistrarCambioAsync(
                        productoId: existing.Id,
                        precioCompraAnterior: existing.PrecioCompra,
                        precioCompraNuevo: producto.PrecioCompra,
                        precioVentaAnterior: existing.PrecioVenta,
                        precioVentaNuevo: producto.PrecioVenta,
                        motivoCambio: null,
                        usuarioModificacion: usuario
                    );

                    _logger.LogInformation(
                        "Precio actualizado para {Codigo}: Compra {PC_Ant} → {PC_Nuevo}, Venta {PV_Ant} → {PV_Nuevo}",
                        existing.Codigo, existing.PrecioCompra, producto.PrecioCompra, existing.PrecioVenta, producto.PrecioVenta);
                }

                // Concurrencia optimista: RowVersion es obligatorio para evitar actualizaciones perdidas.
                if (producto.RowVersion is null || producto.RowVersion.Length == 0)
                    throw new InvalidOperationException("No se recibió la versión de fila (RowVersion). Recargá los datos e intentá nuevamente.");

                _context.Entry(existing).Property(e => e.RowVersion).OriginalValue = producto.RowVersion;

                existing.Codigo = producto.Codigo;
                existing.Nombre = producto.Nombre;
                existing.Descripcion = producto.Descripcion;
                existing.CategoriaId = producto.CategoriaId;
                existing.MarcaId = producto.MarcaId;
                existing.PrecioCompra = producto.PrecioCompra;
                existing.PrecioVenta = producto.PrecioVenta;
                existing.PorcentajeIVA = producto.PorcentajeIVA;
                existing.AlicuotaIVAId = producto.AlicuotaIVAId;
                existing.ComisionPorcentaje = producto.ComisionPorcentaje;
                existing.MaxCuotasSinInteresPermitidas = producto.MaxCuotasSinInteresPermitidas;
                existing.RequiereNumeroSerie = producto.RequiereNumeroSerie;
                existing.StockMinimo = producto.StockMinimo;

                // IMPORTANTe: no permitir que UpdateAsync cambie StockActual “por edición de producto”.
                // Si el cliente manda un valor distinto, lo ignoramos y dejamos evidencia en logs.
                if (producto.StockActual != existing.StockActual)
                {
                    _logger.LogWarning(
                        "Se ignoró intento de modificar StockActual desde UpdateAsync (ProductoId {Id}). " +
                        "Use Movimientos de Stock/Ajuste. Valor enviado: {Enviado}, Valor actual: {Actual}",
                        producto.Id, producto.StockActual, existing.StockActual);
                }

                existing.UnidadMedida = producto.UnidadMedida;
                existing.Activo = producto.Activo;
                existing.UpdatedAt = DateTime.UtcNow;

                var caracteristicasNuevas = NormalizarCaracteristicas(producto.Caracteristicas).ToList();
                SincronizarCaracteristicas(existing, caracteristicasNuevas);

                await _context.SaveChangesAsync();

                _logger.LogInformation("Producto actualizado: {Codigo} - {Nombre}", existing.Codigo, existing.Nombre);
                return existing;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al actualizar producto {Id}", producto.Id);
                throw new InvalidOperationException(
                    "El producto fue modificado por otro usuario. Recargá los datos e intentá nuevamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar producto {Id}", producto.Id);
                throw;
            }
        }

        public async Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje)
        {
            if (porcentaje < 0 || porcentaje > 100)
                throw new InvalidOperationException("El porcentaje de comisión debe estar entre 0 y 100.");

            try
            {
                var producto = await _context.Productos.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
                if (producto == null)
                    throw new InvalidOperationException($"No se encontró el producto con ID {id}");

                producto.ComisionPorcentaje = porcentaje;
                producto.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Comisión actualizada para producto {Id}: {Porcentaje}%", id, porcentaje);
                return producto;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error al actualizar comisión para producto {Id}", id);
                throw;
            }
        }

        public async Task<bool> ToggleDestacadoAsync(int id)
        {
            try
            {
                var producto = await _context.Productos.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
                if (producto == null)
                    throw new InvalidOperationException($"No se encontró el producto con ID {id}");

                producto.EsDestacado = !producto.EsDestacado;
                producto.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("EsDestacado toggled para producto {Id}: {Valor}", id, producto.EsDestacado);
                return producto.EsDestacado;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Error al alternar destacado para producto {Id}", id);
                throw;
            }
        }

        #endregion

        #region Eliminar / Stock

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var producto = await _context.Productos.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
                if (producto == null)
                {
                    _logger.LogWarning("Intento de eliminar producto inexistente {Id}", id);
                    return false;
                }

                producto.IsDeleted = true;
                producto.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Producto eliminado (soft delete): {Codigo}", producto.Codigo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar producto {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Actualiza stock por delta (cantidad puede ser positiva o negativa) y registra MovimientoStock.
        /// </summary>
        public async Task<Producto> ActualizarStockAsync(int id, decimal cantidad)
        {
            try
            {
                var producto = await _context.Productos.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
                if (producto == null)
                    throw new InvalidOperationException($"No se encontró el producto con ID {id}");

                if (cantidad == 0)
                {
                    _logger.LogInformation("ActualizarStockAsync sin cambios (cantidad 0) para ProductoId {Id}", id);
                    return producto;
                }

                var tipo = cantidad > 0 ? TipoMovimiento.Entrada : TipoMovimiento.Salida;
                var cantidadAbs = Math.Abs(cantidad);

                var stockAnterior = producto.StockActual;
                var nuevoStock = tipo == TipoMovimiento.Entrada
                    ? stockAnterior + cantidadAbs
                    : stockAnterior - cantidadAbs;

                if (nuevoStock < 0)
                {
                    throw new InvalidOperationException(
                        $"No se puede reducir el stock. Actual: {stockAnterior}, " +
                        $"Solicitado: {cantidadAbs}. Sería negativo: {nuevoStock}");
                }

                var usuario = _currentUserService.GetUsername();

                var hasAmbientTransaction = _context.Database.CurrentTransaction != null;
                await using var tx = hasAmbientTransaction
                    ? null
                    : await _context.Database.BeginTransactionAsync();

                producto.StockActual = nuevoStock;
                producto.UpdatedAt = DateTime.UtcNow;

                var movimiento = new MovimientoStock
                {
                    ProductoId = producto.Id,
                    Tipo = tipo,
                    Cantidad = cantidadAbs,
                    StockAnterior = stockAnterior,
                    StockNuevo = nuevoStock,
                    CostoUnitarioAlMomento = Math.Round(producto.PrecioCompra, 2, MidpointRounding.AwayFromZero),
                    CostoTotalAlMomento = Math.Round(producto.PrecioCompra * Math.Abs(cantidadAbs), 2, MidpointRounding.AwayFromZero),
                    FuenteCosto = "AjusteManual",
                    Referencia = "ProductoService.ActualizarStockAsync",
                    Motivo = "Actualización de stock (delta)",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = usuario
                };

                _context.MovimientosStock.Add(movimiento);
                await _context.SaveChangesAsync();

                if (tx != null)
                    await tx.CommitAsync();

                _logger.LogInformation(
                    "Stock actualizado para {Codigo}: {StockAnterior} → {StockNuevo} (Tipo {Tipo}, Cantidad {Cantidad})",
                    producto.Codigo, stockAnterior, nuevoStock, tipo, cantidadAbs);

                return producto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar stock del producto {Id}", id);
                throw;
            }
        }

        #endregion

        #region Validaciones

        public async Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null)
        {
            try
            {
                var query = _context.Productos.Where(p => p.Codigo == codigo && !p.IsDeleted);

                if (excludeId.HasValue)
                    query = query.Where(p => p.Id != excludeId.Value);

                return await query.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar código {Codigo}", codigo);
                throw;
            }
        }

        private void ValidarProducto(Producto producto)
        {
            if (string.IsNullOrWhiteSpace(producto.Codigo))
                throw new InvalidOperationException("El código del producto no puede estar vacío");

            if (string.IsNullOrWhiteSpace(producto.Nombre))
                throw new InvalidOperationException("El nombre del producto no puede estar vacío");

            if (producto.CategoriaId <= 0)
                throw new InvalidOperationException("La categoría es obligatoria");

            if (producto.MarcaId <= 0)
                throw new InvalidOperationException("La marca es obligatoria");

            if (producto.PrecioCompra < 0)
                throw new InvalidOperationException("El precio de costo no puede ser negativo");

            if (producto.PrecioVenta < 0)
                throw new InvalidOperationException("El precio de venta no puede ser negativo");

            if (producto.PrecioVenta < producto.PrecioCompra)
            {
                _logger.LogWarning(
                    "Producto {Codigo}: Precio venta ({PV}) < Precio compra ({PC})",
                    producto.Codigo, producto.PrecioVenta, producto.PrecioCompra);
            }

            if (producto.PorcentajeIVA < 0 || producto.PorcentajeIVA > 100)
                throw new InvalidOperationException("El porcentaje de IVA debe estar entre 0 y 100");

            if (producto.ComisionPorcentaje < 0 || producto.ComisionPorcentaje > 100)
                throw new InvalidOperationException("La comisión vendedor debe estar entre 0 y 100");

            if (producto.StockActual < 0)
                throw new InvalidOperationException("El stock actual no puede ser negativo");

            if (producto.StockMinimo < 0)
                throw new InvalidOperationException("El stock mínimo no puede ser negativo");
        }

        private async Task AsegurarAlicuotaIVAActivaAsync(int alicuotaIVAId)
        {
            var existe = await _context.AlicuotasIVA
                .AnyAsync(a => a.Id == alicuotaIVAId && a.Activa && !a.IsDeleted);

            if (!existe)
                throw new InvalidOperationException("La alícuota de IVA seleccionada no existe o no está activa");
        }

        private async Task<decimal> ResolverPorcentajeIVAFormularioAsync(int? alicuotaIVAId, decimal porcentajeManual)
        {
            if (alicuotaIVAId.HasValue)
            {
                var porcentaje = await _context.AlicuotasIVA
                    .AsNoTracking()
                    .Where(a => a.Id == alicuotaIVAId.Value && a.Activa && !a.IsDeleted)
                    .Select(a => (decimal?)a.Porcentaje)
                    .FirstOrDefaultAsync();

                if (porcentaje.HasValue)
                    return porcentaje.Value;
            }

            return porcentajeManual;
        }

        private static IEnumerable<ProductoCaracteristica> NormalizarCaracteristicas(IEnumerable<ProductoCaracteristica>? caracteristicas)
        {
            if (caracteristicas == null)
                return Enumerable.Empty<ProductoCaracteristica>();

            return caracteristicas
                .Where(c => !string.IsNullOrWhiteSpace(c.Nombre) && !string.IsNullOrWhiteSpace(c.Valor))
                .Select(c => new ProductoCaracteristica
                {
                    Id = c.Id,
                    Nombre = c.Nombre.Trim(),
                    Valor = c.Valor.Trim()
                });
        }

        private static void SincronizarCaracteristicas(Producto existing, IEnumerable<ProductoCaracteristica> caracteristicasNuevas)
        {
            var nuevasPorId = caracteristicasNuevas
                .Where(c => c.Id > 0)
                .ToDictionary(c => c.Id, c => c);

            foreach (var caracteristicaExistente in existing.Caracteristicas.ToList())
            {
                if (!nuevasPorId.ContainsKey(caracteristicaExistente.Id))
                {
                    caracteristicaExistente.IsDeleted = true;
                    caracteristicaExistente.UpdatedAt = DateTime.UtcNow;
                }
            }

            foreach (var nueva in caracteristicasNuevas)
            {
                if (nueva.Id > 0)
                {
                    var existente = existing.Caracteristicas.FirstOrDefault(c => c.Id == nueva.Id && !c.IsDeleted);
                    if (existente != null)
                    {
                        existente.Nombre = nueva.Nombre;
                        existente.Valor = nueva.Valor;
                        continue;
                    }
                }

                existing.Caracteristicas.Add(new ProductoCaracteristica
                {
                    Nombre = nueva.Nombre,
                    Valor = nueva.Valor
                });
            }
        }

        #endregion

        #region Búsqueda para venta

        public async Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(
            string term,
            int take = 20,
            int? categoriaId = null,
            int? marcaId = null,
            bool soloConStock = true,
            decimal? precioMin = null,
            decimal? precioMax = null)
        {
            var limite = Math.Clamp(take, 1, 50);
            var termino = term.Trim();

            var productos = (await SearchAsync(
                    searchTerm: termino,
                    categoriaId: categoriaId,
                    marcaId: marcaId,
                    soloActivos: true,
                    orderBy: "nombre"))
                .ToList();

            var preciosVigentes = await ResolverPreciosVigentesAsync(productos);

            var resultado = new List<ProductoVentaDto>(productos.Count);

            foreach (var producto in productos)
            {
                var precioVenta = preciosVigentes.TryGetValue(producto.Id, out var precioVigente)
                    ? precioVigente
                    : producto.PrecioVenta;

                if (soloConStock && producto.StockActual <= 0) continue;
                if (precioMin.HasValue && precioVenta < precioMin.Value) continue;
                if (precioMax.HasValue && precioVenta > precioMax.Value) continue;

                var caracteristicas = producto.Caracteristicas?
                    .Where(c => !c.IsDeleted)
                    .Select(c => new ProductoCaracteristicaDto { Nombre = c.Nombre, Valor = c.Valor })
                    .ToList() ?? new List<ProductoCaracteristicaDto>();

                var caracteristicasResumen = producto.Caracteristicas?
                    .Where(c => !c.IsDeleted)
                    .Take(3)
                    .Select(c => c.Valor)
                    .ToList() ?? new List<string>();

                resultado.Add(new ProductoVentaDto
                {
                    Id                    = producto.Id,
                    Codigo                = producto.Codigo,
                    Nombre                = producto.Nombre,
                    Marca                 = producto.Marca?.Nombre,
                    Submarca              = producto.Submarca?.Nombre,
                    Categoria             = producto.Categoria?.Nombre,
                    Subcategoria          = producto.Subcategoria?.Nombre,
                    Descripcion           = producto.Descripcion,
                    StockActual           = producto.StockActual,
                    PrecioVenta           = precioVenta,
                    Caracteristicas       = caracteristicas,
                    CaracteristicasResumen = string.Join(" · ", caracteristicasResumen),
                    CodigoExacto          = string.Equals(producto.Codigo, termino, StringComparison.OrdinalIgnoreCase)
                });
            }

            return resultado.Take(limite);
        }

        public async Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId)
        {
            var producto = await GetByIdAsync(productoId);
            if (producto == null || !producto.Activo)
                return null;

            var precioVigente = await _precioVigenteResolver.ResolverAsync(producto.Id);
            var precioVenta = precioVigente?.PrecioFinalConIva ?? producto.PrecioVenta;

            return new ProductoPrecioVentaResultado
            {
                ProductoId = producto.Id,
                PrecioVenta = precioVenta,
                FuentePrecio = precioVigente?.FuentePrecio ?? FuentePrecioVigente.ProductoPrecioBase,
                ListaId = precioVigente?.ListaId,
                Codigo = producto.Codigo,
                Nombre = producto.Nombre,
                StockActual = producto.StockActual
            };
        }

        private async Task<Dictionary<int, decimal>> ResolverPreciosVigentesAsync(IEnumerable<Producto> productos)
        {
            var productosList = productos.ToList();
            var precios = await _precioVigenteResolver.ResolverBatchAsync(productosList.Select(p => p.Id));

            return precios.ToDictionary(
                p => p.Key,
                p => p.Value.PrecioFinalConIva);
        }

        #endregion
    }
}
