// ProveedorService.cs
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ProveedorService : IProveedorService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProveedorService> _logger;

        public ProveedorService(AppDbContext context, ILogger<ProveedorService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Proveedor>> GetAllAsync()
        {
            try
            {
                return await _context.Proveedores
                    .Where(p => !p.IsDeleted)
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(p => p.ProveedorProductos.Where(pp =>
                        !pp.IsDeleted &&
                        pp.Producto != null &&
                        !pp.Producto.IsDeleted))
                        .ThenInclude(pp => pp.Producto)
                    .Include(p => p.ProveedorMarcas)
                        .ThenInclude(pm => pm.Marca)
                    .Include(p => p.ProveedorCategorias)
                        .ThenInclude(pc => pc.Categoria)
                    // Necesario para los campos calculados del ProveedorViewModel (AutoMapperProfile)
                    .Include(p => p.OrdenesCompra.Where(o => !o.IsDeleted))
                    .Include(p => p.Cheques.Where(c => !c.IsDeleted))
                    .OrderBy(p => p.RazonSocial)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los proveedores");
                throw;
            }
        }

        public async Task<Proveedor?> GetByIdAsync(int id)
        {
            try
            {
                return await _context.Proveedores
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(p => p.ProveedorProductos.Where(pp =>
                        !pp.IsDeleted &&
                        pp.Producto != null &&
                        !pp.Producto.IsDeleted))
                        .ThenInclude(pp => pp.Producto)
                    .Include(p => p.ProveedorMarcas)
                        .ThenInclude(pm => pm.Marca)
                    .Include(p => p.ProveedorCategorias)
                        .ThenInclude(pc => pc.Categoria)
                    // Necesario para los campos calculados del ProveedorViewModel (AutoMapperProfile)
                    .Include(p => p.OrdenesCompra.Where(o => !o.IsDeleted))
                    .Include(p => p.Cheques.Where(c => !c.IsDeleted))
                    .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener proveedor {Id}", id);
                throw;
            }
        }

        public async Task CreateAsync(Proveedor proveedor)
        {
            try
            {
                if (proveedor == null) throw new ArgumentNullException(nameof(proveedor));

                // Validar CUIT único
                if (await ExistsCuitAsync(proveedor.Cuit))
                {
                    throw new InvalidOperationException($"Ya existe un proveedor con el CUIT {proveedor.Cuit}");
                }

                // Normalizar / deduplicar asociaciones para evitar duplicados (índices únicos)
                PrepareAssociationsForCreate(proveedor);

                _context.Proveedores.Add(proveedor);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Proveedor creado: {Id} - {RazonSocial} con {ProductosCount} productos, {MarcasCount} marcas, {CategoriasCount} categorías",
                    proveedor.Id,
                    proveedor.RazonSocial,
                    proveedor.ProveedorProductos.Count,
                    proveedor.ProveedorMarcas.Count,
                    proveedor.ProveedorCategorias.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear proveedor");
                throw;
            }
        }

        public async Task UpdateAsync(Proveedor proveedor)
        {
            try
            {
                if (proveedor == null) throw new ArgumentNullException(nameof(proveedor));

                if (proveedor.RowVersion == null || proveedor.RowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá el proveedor e intentá nuevamente.");

                // Validar CUIT único (excluyendo el registro actual)
                if (await ExistsCuitAsync(proveedor.Cuit, proveedor.Id))
                {
                    throw new InvalidOperationException($"Ya existe otro proveedor con el CUIT {proveedor.Cuit}");
                }

                var existingProveedor = await _context.Proveedores
                    .Include(p => p.ProveedorProductos)
                    .Include(p => p.ProveedorMarcas)
                    .Include(p => p.ProveedorCategorias)
                    .FirstOrDefaultAsync(p => p.Id == proveedor.Id && !p.IsDeleted);

                if (existingProveedor == null)
                {
                    throw new InvalidOperationException("Proveedor no encontrado");
                }

                // Actualizar propiedades básicas sin pisar auditoría/concurrencia (si existen)
                _context.Entry(existingProveedor).CurrentValues.SetValues(proveedor);

                // Concurrencia optimista
                _context.Entry(existingProveedor).Property(p => p.RowVersion).OriginalValue = proveedor.RowVersion;

                var entry = _context.Entry(existingProveedor);
                MarkNotModifiedIfExists(entry, "CreatedAt");
                MarkNotModifiedIfExists(entry, "CreatedBy");
                MarkNotModifiedIfExists(entry, "IsDeleted");

                // Reemplazar asociaciones (hard delete de relaciones para evitar inconsistencias con índices únicos)
                _context.RemoveRange(existingProveedor.ProveedorProductos);
                _context.RemoveRange(existingProveedor.ProveedorMarcas);
                _context.RemoveRange(existingProveedor.ProveedorCategorias);

                PrepareAssociationsForUpdate(existingProveedor.Id, proveedor);

                existingProveedor.ProveedorProductos = proveedor.ProveedorProductos;
                existingProveedor.ProveedorMarcas = proveedor.ProveedorMarcas;
                existingProveedor.ProveedorCategorias = proveedor.ProveedorCategorias;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "El proveedor fue modificado por otro usuario. Recargá la página y volvé a intentar.");
                }

                _logger.LogInformation("Proveedor actualizado: {Id} - {RazonSocial}", proveedor.Id, proveedor.RazonSocial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar proveedor {Id}", proveedor?.Id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                // Respetar query filters (soft delete): si ya está eliminado, no debería aparecer
                var proveedor = await _context.Proveedores.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
                if (proveedor == null)
                {
                    return false;
                }

                // Verificar si tiene órdenes de compra asociadas
                var tieneOrdenes = await _context.OrdenesCompra.AnyAsync(o => o.ProveedorId == id && !o.IsDeleted);
                if (tieneOrdenes)
                {
                    throw new InvalidOperationException("No se puede eliminar el proveedor porque tiene órdenes de compra asociadas");
                }

                // Verificar si tiene cheques vigentes asociados (alineado a AutoMapperProfile)
                var tieneChequesVigentes = await _context.Cheques.AnyAsync(c =>
                    c.ProveedorId == id &&
                    !c.IsDeleted &&
                    c.Estado != EstadoCheque.Cobrado &&
                    c.Estado != EstadoCheque.Rechazado &&
                    c.Estado != EstadoCheque.Anulado
                );

                if (tieneChequesVigentes)
                {
                    throw new InvalidOperationException("No se puede eliminar el proveedor porque tiene cheques vigentes asociados");
                }

                // Soft delete
                proveedor.IsDeleted = true;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Proveedor eliminado: {Id} - {RazonSocial}", id, proveedor.RazonSocial);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar proveedor {Id}", id);
                throw;
            }
        }

        public async Task<bool> ExistsCuitAsync(string cuit, int? excludeId = null)
        {
            try
            {
                // Debe coincidir con el índice único filtrado (solo no eliminados)
                var query = _context.Proveedores.Where(p => p.Cuit == cuit && !p.IsDeleted);

                if (excludeId.HasValue)
                {
                    query = query.Where(p => p.Id != excludeId.Value);
                }

                return await query.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar existencia de CUIT {Cuit}", cuit);
                throw;
            }
        }

        public async Task<IEnumerable<Proveedor>> SearchAsync(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            try
            {
                var query = _context.Proveedores
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(p => p.ProveedorProductos.Where(pp =>
                        !pp.IsDeleted &&
                        pp.Producto != null &&
                        !pp.Producto.IsDeleted))
                        .ThenInclude(pp => pp.Producto)
                    .Include(p => p.ProveedorMarcas)
                        .ThenInclude(pm => pm.Marca)
                    .Include(p => p.ProveedorCategorias)
                        .ThenInclude(pc => pc.Categoria)
                    // Necesario para los campos calculados del ProveedorViewModel (AutoMapperProfile)
                    .Include(p => p.OrdenesCompra.Where(o => !o.IsDeleted))
                    .Include(p => p.Cheques.Where(c => !c.IsDeleted))
                    .AsQueryable();

                // Soft delete
                query = query.Where(p => !p.IsDeleted);

                // Búsqueda por texto
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(p =>
                        p.Cuit.Contains(searchTerm) ||
                        p.RazonSocial.ToLower().Contains(searchTerm) ||
                        (p.NombreFantasia != null && p.NombreFantasia.ToLower().Contains(searchTerm)) ||
                        (p.Email != null && p.Email.ToLower().Contains(searchTerm))
                    );
                }

                // Filtro solo activos
                if (soloActivos)
                {
                    query = query.Where(p => p.Activo);
                }

                // Ordenamiento dinámico
                if (!string.IsNullOrWhiteSpace(orderBy))
                {
                    var ascending = orderDirection?.ToLower() != "desc";
                    query = orderBy.ToLower() switch
                    {
                        "cuit" => ascending ? query.OrderBy(p => p.Cuit) : query.OrderByDescending(p => p.Cuit),
                        "razonsocial" => ascending ? query.OrderBy(p => p.RazonSocial) : query.OrderByDescending(p => p.RazonSocial),
                        "email" => ascending ? query.OrderBy(p => p.Email) : query.OrderByDescending(p => p.Email),
                        "telefono" => ascending ? query.OrderBy(p => p.Telefono) : query.OrderByDescending(p => p.Telefono),
                        _ => query.OrderBy(p => p.RazonSocial)
                    };
                }
                else
                {
                    query = query.OrderBy(p => p.RazonSocial);
                }

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar proveedores con filtros");
                throw;
            }
        }

        private static void MarkNotModifiedIfExists(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string propertyName)
        {
            var prop = entry.Metadata.FindProperty(propertyName);
            if (prop != null)
            {
                entry.Property(propertyName).IsModified = false;
            }
        }

        private static void PrepareAssociationsForCreate(Proveedor proveedor)
        {
            // Deduplicar por ID y asegurar navegación al proveedor
            var productoIds = proveedor.ProveedorProductos.Select(x => x.ProductoId).Distinct().ToList();
            proveedor.ProveedorProductos = productoIds
                .Select(id => new ProveedorProducto { ProductoId = id, Proveedor = proveedor })
                .ToList();

            var marcaIds = proveedor.ProveedorMarcas.Select(x => x.MarcaId).Distinct().ToList();
            proveedor.ProveedorMarcas = marcaIds
                .Select(id => new ProveedorMarca { MarcaId = id, Proveedor = proveedor })
                .ToList();

            var categoriaIds = proveedor.ProveedorCategorias.Select(x => x.CategoriaId).Distinct().ToList();
            proveedor.ProveedorCategorias = categoriaIds
                .Select(id => new ProveedorCategoria { CategoriaId = id, Proveedor = proveedor })
                .ToList();
        }

        private static void PrepareAssociationsForUpdate(int proveedorId, Proveedor proveedor)
        {
            // Deduplicar por ID y setear FK (sin setear navegación para evitar efectos colaterales)
            var productoIds = proveedor.ProveedorProductos.Select(x => x.ProductoId).Distinct().ToList();
            proveedor.ProveedorProductos = productoIds
                .Select(id => new ProveedorProducto { ProveedorId = proveedorId, ProductoId = id })
                .ToList();

            var marcaIds = proveedor.ProveedorMarcas.Select(x => x.MarcaId).Distinct().ToList();
            proveedor.ProveedorMarcas = marcaIds
                .Select(id => new ProveedorMarca { ProveedorId = proveedorId, MarcaId = id })
                .ToList();

            var categoriaIds = proveedor.ProveedorCategorias.Select(x => x.CategoriaId).Distinct().ToList();
            proveedor.ProveedorCategorias = categoriaIds
                .Select(id => new ProveedorCategoria { ProveedorId = proveedorId, CategoriaId = id })
                .ToList();
        }
    }
}
