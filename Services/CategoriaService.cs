using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación del servicio de Categorías
    /// </summary>
    public class CategoriaService : ICategoriaService
    {
        #region Constructor y dependencias

        private readonly AppDbContext _context;
        private readonly ILogger<CategoriaService> _logger;

        public CategoriaService(AppDbContext context, ILogger<CategoriaService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #endregion

        #region CRUD

        public async Task<IEnumerable<Categoria>> GetAllAsync()
        {
            try
            {
                return await _context.Categorias
                    .AsNoTracking()
                    .Where(c => !c.IsDeleted)
                    .Include(c => c.Parent)
                    .Include(c => c.AlicuotaIVA)
                    .OrderBy(c => c.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todas las categorías");
                throw;
            }
        }

        public async Task<Categoria?> GetByIdAsync(int id)
        {
            try
            {
                return await _context.Categorias
                    .AsNoTracking()
                    .Include(c => c.Parent)
                    .Include(c => c.Children)
                    .Include(c => c.AlicuotaIVA)
                    .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener categoría con Id {Id}", id);
                throw;
            }
        }

        public async Task<Categoria?> GetByCodigoAsync(string codigo)
        {
            try
            {
                return await _context.Categorias
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Codigo == codigo && !c.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener categoría con código {Codigo}", codigo);
                throw;
            }
        }

        public async Task<Categoria> CreateAsync(Categoria categoria)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(categoria.Codigo))
                    throw new InvalidOperationException("El código de la categoría es obligatorio");

                if (string.IsNullOrWhiteSpace(categoria.Nombre))
                    throw new InvalidOperationException("El nombre de la categoría es obligatorio");

                if (await ExistsCodigoAsync(categoria.Codigo))
                    throw new InvalidOperationException($"Ya existe una categoría con el código {categoria.Codigo}");

                if (categoria.AlicuotaIVAId.HasValue)
                    await AsegurarAlicuotaIVAActivaAsync(categoria.AlicuotaIVAId.Value);

                // Validar que el ParentId exista si se especifica
                if (categoria.ParentId.HasValue &&
                    !await _context.Categorias.AnyAsync(c => c.Id == categoria.ParentId.Value && !c.IsDeleted))
                {
                    throw new InvalidOperationException($"La categoría padre con Id {categoria.ParentId.Value} no existe");
                }

                // Validar que no cree un ciclo
                if (await WouldCreateCycleAsync(null, categoria.ParentId))
                    throw new InvalidOperationException("No se puede establecer esta relación porque crearía un ciclo");

                _context.Categorias.Add(categoria);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Categoría creada: {Codigo} - {Nombre}", categoria.Codigo, categoria.Nombre);
                return categoria;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear categoría {Codigo}", categoria.Codigo);
                throw;
            }
        }

        public async Task<Categoria> UpdateAsync(Categoria categoria)
        {
            try
            {
                var existing = await _context.Categorias
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == categoria.Id);

                if (existing == null)
                    throw new InvalidOperationException($"No se encontró la categoría con Id {categoria.Id}");

                if (existing.IsDeleted)
                    throw new InvalidOperationException("No se puede actualizar una categoría eliminada.");

                if (string.IsNullOrWhiteSpace(categoria.Codigo))
                    throw new InvalidOperationException("El código de la categoría es obligatorio");

                if (string.IsNullOrWhiteSpace(categoria.Nombre))
                    throw new InvalidOperationException("El nombre de la categoría es obligatorio");

                // Validar código único (excluyendo el registro actual)
                if (await ExistsCodigoAsync(categoria.Codigo, categoria.Id))
                    throw new InvalidOperationException($"Ya existe otra categoría con el código {categoria.Codigo}");

                if (categoria.AlicuotaIVAId.HasValue)
                    await AsegurarAlicuotaIVAActivaAsync(categoria.AlicuotaIVAId.Value);

                // Validar que el ParentId exista si se especifica
                if (categoria.ParentId.HasValue &&
                    !await _context.Categorias.AnyAsync(c => c.Id == categoria.ParentId.Value && !c.IsDeleted))
                {
                    throw new InvalidOperationException($"La categoría padre con Id {categoria.ParentId.Value} no existe");
                }

                // Validar que no cree un ciclo
                if (await WouldCreateCycleAsync(categoria.Id, categoria.ParentId))
                    throw new InvalidOperationException("No se puede establecer esta relación porque crearía un ciclo jerárquico");

                // Concurrencia optimista
                if (categoria.RowVersion != null)
                {
                    _context.Entry(existing).Property(e => e.RowVersion).OriginalValue = categoria.RowVersion;
                }
                else
                {
                    _logger.LogWarning(
                        "RowVersion no provisto al actualizar categoría {Id}. La operación no podrá detectar conflictos de concurrencia.",
                        categoria.Id);
                }

                existing.Codigo = categoria.Codigo;
                existing.Nombre = categoria.Nombre;
                existing.Descripcion = categoria.Descripcion;
                existing.ParentId = categoria.ParentId;
                existing.ControlSerieDefault = categoria.ControlSerieDefault;
                existing.AlicuotaIVAId = categoria.AlicuotaIVAId;
                existing.Activo = categoria.Activo;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Categoría actualizada: {Codigo} - {Nombre}", existing.Codigo, existing.Nombre);
                return existing;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al actualizar categoría {Id}", categoria.Id);
                throw new InvalidOperationException("La categoría fue modificada por otro usuario. Recargá los datos e intentá nuevamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar categoría {Id}", categoria.Id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var categoria = await _context.Categorias
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (categoria == null || categoria.IsDeleted)
                    return false;

                // Verificar si tiene categorías hijas
                if (await _context.Categorias.AnyAsync(c => c.ParentId == id && !c.IsDeleted))
                    throw new InvalidOperationException("No se puede eliminar una categoría que tiene subcategorías");

                // Verificar si tiene productos asociados
                if (await _context.Productos.AnyAsync(p => p.CategoriaId == id && !p.IsDeleted))
                    throw new InvalidOperationException("No se puede eliminar una categoría que tiene productos asociados");

                categoria.IsDeleted = true;
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Categoría eliminada (soft delete): {Codigo} - {Nombre}",
                    categoria.Codigo,
                    categoria.Nombre);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar categoría {Id}", id);
                throw;
            }
        }

        #endregion

        #region Validación y búsqueda

        public async Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null)
        {
            try
            {
                var query = _context.Categorias
                    .AsNoTracking()
                    .Where(c => c.Codigo == codigo && !c.IsDeleted);

                if (excludeId.HasValue)
                    query = query.Where(c => c.Id != excludeId.Value);

                return await query.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar existencia de código {Codigo}", codigo);
                throw;
            }
        }

        public async Task<IEnumerable<Categoria>> SearchAsync(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            try
            {
                var query = _context.Categorias
                    .AsNoTracking()
                    .Include(c => c.Parent)
                    .Where(c => !c.IsDeleted)
                    .AsQueryable();

                // Búsqueda por texto
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var pattern = $"%{searchTerm.Trim()}%";
                    query = query.Where(c =>
                        EF.Functions.Like(c.Nombre, pattern) ||
                        (c.Descripcion != null && EF.Functions.Like(c.Descripcion, pattern)));
                }

                // Filtro solo activos
                if (soloActivos)
                    query = query.Where(c => c.Activo);

                // Ordenamiento dinámico
                var ascending = orderDirection?.ToLower() != "desc";
                query = orderBy?.ToLower() switch
                {
                    "nombre" => ascending ? query.OrderBy(c => c.Nombre) : query.OrderByDescending(c => c.Nombre),
                    "descripcion" => ascending ? query.OrderBy(c => c.Descripcion) : query.OrderByDescending(c => c.Descripcion),
                    "parent" => ascending ? query.OrderBy(c => c.Parent!.Nombre) : query.OrderByDescending(c => c.Parent!.Nombre),
                    _ => query.OrderBy(c => c.Nombre)
                };

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar categorías con filtros");
                throw;
            }
        }

        /// <summary>Valida ciclos sin incluir el registro actual.</summary>
        private async Task<bool> WouldCreateCycleAsync(int? categoryId, int? parentId)
        {
            if (!parentId.HasValue)
                return false;

            if (categoryId.HasValue && categoryId.Value == parentId.Value)
                return true;

            var visitedIds = new HashSet<int>();
            if (categoryId.HasValue)
                visitedIds.Add(categoryId.Value);

            var currentParentId = parentId;

            while (currentParentId.HasValue)
            {
                if (visitedIds.Contains(currentParentId.Value))
                    return true;

                visitedIds.Add(currentParentId.Value);

                var nextParentId = await _context.Categorias
                    .AsNoTracking()
                    .Where(c => c.Id == currentParentId.Value && !c.IsDeleted)
                    .Select(c => c.ParentId)
                    .FirstOrDefaultAsync();

                currentParentId = nextParentId;
            }

            return false;
        }

        private async Task AsegurarAlicuotaIVAActivaAsync(int alicuotaIVAId)
        {
            var existe = await _context.AlicuotasIVA
                .AnyAsync(a => a.Id == alicuotaIVAId && a.Activa && !a.IsDeleted);

            if (!existe)
                throw new InvalidOperationException("La alícuota de IVA seleccionada no existe o no está activa");
        }

        public async Task<IEnumerable<Categoria>> GetChildrenAsync(int parentId)
        {
            return await _context.Categorias
                .AsNoTracking()
                .Where(c => c.ParentId == parentId && !c.IsDeleted && c.Activo)
                .OrderBy(c => c.Nombre)
                .ToListAsync();
        }

        #endregion
    }
}
