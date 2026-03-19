using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación del servicio de Marcas.
    /// Contiene toda la lógica de negocio relacionada con marcas.
    /// </summary>
    public class MarcaService : IMarcaService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MarcaService> _logger;

        public MarcaService(AppDbContext context, ILogger<MarcaService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Marca>> GetAllAsync()
        {
            try
            {
                return await _context.Marcas
                    .AsNoTracking()
                    .Where(m => !m.IsDeleted)
                    .Include(m => m.Parent)
                    .OrderBy(m => m.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todas las marcas");
                throw;
            }
        }

        public async Task<Marca?> GetByIdAsync(int id)
        {
            try
            {
                return await _context.Marcas
                    .AsNoTracking()
                    .Include(m => m.Parent)
                    .Include(m => m.Children)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener marca con Id {Id}", id);
                throw;
            }
        }

        public async Task<Marca?> GetByCodigoAsync(string codigo)
        {
            try
            {
                return await _context.Marcas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Codigo == codigo && !m.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener marca con código {Codigo}", codigo);
                throw;
            }
        }

        public async Task<Marca> CreateAsync(Marca marca)
        {
            try
            {
                // Validación de string vacío
                if (string.IsNullOrWhiteSpace(marca.Codigo))
                {
                    throw new InvalidOperationException("El código no puede estar vacío");
                }

                if (string.IsNullOrWhiteSpace(marca.Nombre))
                {
                    throw new InvalidOperationException("El nombre no puede estar vacío");
                }

                // Validaciones de negocio
                if (await ExistsCodigoAsync(marca.Codigo))
                {
                    throw new InvalidOperationException($"Ya existe una marca con el código {marca.Codigo}");
                }

                // Validar que el ParentId exista si se especifica
                if (marca.ParentId.HasValue)
                {
                    var parentExists = await _context.Marcas.AnyAsync(m => m.Id == marca.ParentId.Value && !m.IsDeleted);
                    if (!parentExists)
                    {
                        throw new InvalidOperationException($"La marca padre con Id {marca.ParentId.Value} no existe");
                    }

                    // Validar que no se está creando un ciclo
                    if (await WouldCreateCycleAsync(null, marca.ParentId.Value))
                    {
                        throw new InvalidOperationException("No se puede establecer esta relación porque crearía un ciclo");
                    }
                }

                _context.Marcas.Add(marca);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Marca creada: {Codigo} - {Nombre}", marca.Codigo, marca.Nombre);

                return marca;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear marca {Codigo}", marca.Codigo);
                throw;
            }
        }

        public async Task<Marca> UpdateAsync(Marca marca)
        {
            try
            {
                // Validación de string vacío
                if (string.IsNullOrWhiteSpace(marca.Codigo))
                {
                    throw new InvalidOperationException("El código no puede estar vacío");
                }

                if (string.IsNullOrWhiteSpace(marca.Nombre))
                {
                    throw new InvalidOperationException("El nombre no puede estar vacío");
                }

                // Verificar que existe (usando IgnoreQueryFilters para manejar entidades soft-deleted)
                var existing = await _context.Marcas
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Id == marca.Id);
                if (existing == null)
                {
                    throw new InvalidOperationException($"No se encontró la marca con Id {marca.Id}");
                }

                // Validar que no esté eliminada
                if (existing.IsDeleted)
                {
                    throw new InvalidOperationException($"No se puede actualizar una marca eliminada (Id {marca.Id})");
                }

                // Validar código único (excluyendo el registro actual)
                if (await ExistsCodigoAsync(marca.Codigo, marca.Id))
                {
                    throw new InvalidOperationException($"Ya existe otra marca con el código {marca.Codigo}");
                }

                // Validar que el ParentId exista si se especifica
                if (marca.ParentId.HasValue)
                {
                    var parentExists = await _context.Marcas.AnyAsync(m => m.Id == marca.ParentId.Value && !m.IsDeleted);
                    if (!parentExists)
                    {
                        throw new InvalidOperationException($"La marca padre con Id {marca.ParentId.Value} no existe");
                    }

                    // Validar que no se crea un ciclo
                    if (await WouldCreateCycleAsync(marca.Id, marca.ParentId.Value))
                    {
                        throw new InvalidOperationException("No se puede establecer esta relación porque crearía un ciclo jerárquico");
                    }
                }

                // Actualizar propiedades
                existing.Codigo = marca.Codigo;
                existing.Nombre = marca.Nombre;
                existing.Descripcion = marca.Descripcion;
                existing.ParentId = marca.ParentId;
                existing.PaisOrigen = marca.PaisOrigen;

                // IMPORTANTE: Copiar el RowVersion para que funcione el control de concurrencia
                if (marca.RowVersion != null)
                {
                    _context.Entry(existing).OriginalValues["RowVersion"] = marca.RowVersion;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Marca actualizada: {Codigo} - {Nombre}", marca.Codigo, marca.Nombre);

                return existing;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al actualizar marca {Id}", marca.Id);
                throw new InvalidOperationException("La marca fue modificada por otro usuario. Por favor, recargue los datos.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar marca {Id}", marca.Id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var marca = await _context.Marcas
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (marca == null || marca.IsDeleted)
                    return false;

                // Verificar si tiene submarcas
                var hasChildren = await _context.Marcas.AnyAsync(m => m.ParentId == id && !m.IsDeleted);
                if (hasChildren)
                {
                    throw new InvalidOperationException("No se puede eliminar una marca que tiene submarcas");
                }

                // Verificar si tiene productos asociados
                var hasProductos = await _context.Productos.AnyAsync(p => p.MarcaId == id && !p.IsDeleted);
                if (hasProductos)
                {
                    throw new InvalidOperationException("No se puede eliminar una marca que tiene productos asociados");
                }

                // Soft delete
                marca.IsDeleted = true;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Marca eliminada (soft delete): {Codigo} - {Nombre}", marca.Codigo, marca.Nombre);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar marca {Id}", id);
                throw;
            }
        }

        public async Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null)
        {
            try
            {
                var query = _context.Marcas.Where(m => m.Codigo == codigo && !m.IsDeleted);

                if (excludeId.HasValue)
                {
                    query = query.Where(m => m.Id != excludeId.Value);
                }

                return await query.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar existencia de código {Codigo}", codigo);
                throw;
            }
        }

        /// <summary>
        /// Verifica si establecer una relación padre-hijo crearía un ciclo en la jerarquía.
        /// </summary>
        /// <param name="marcaId">ID de la marca (null para nuevas marcas)</param>
        /// <param name="parentId">ID del padre propuesto</param>
        /// <returns>True si se crearía un ciclo, False en caso contrario</returns>
        private async Task<bool> WouldCreateCycleAsync(int? marcaId, int parentId)
        {
            // Si no hay marcaId, es una creación nueva, no puede haber ciclo
            if (!marcaId.HasValue)
            {
                return false;
            }

            // Si intenta ser su propio padre
            if (marcaId.Value == parentId)
            {
                return true;
            }

            // Recorrer la jerarquía hacia arriba desde el parent propuesto
            var currentParentId = (int?)parentId;
            var visitedIds = new HashSet<int> { marcaId.Value };

            while (currentParentId.HasValue)
            {
                // Si encontramos la marca original, hay un ciclo
                if (visitedIds.Contains(currentParentId.Value))
                {
                    return true;
                }

                visitedIds.Add(currentParentId.Value);

                // Obtener el padre del padre
                var parent = await _context.Marcas
                    .Where(m => m.Id == currentParentId.Value)
                    .Select(m => new { m.ParentId })
                    .FirstOrDefaultAsync();

                if (parent == null)
                {
                    break;
                }

                currentParentId = parent.ParentId;
            }

            return false;
        }

        public async Task<IEnumerable<Marca>> SearchAsync(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            try
            {
                var query = _context.Marcas
                    .AsNoTracking()
                    .Include(m => m.Parent)
                    .Where(m => !m.IsDeleted)
                    .AsQueryable();

                // Búsqueda por texto
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(m =>
                        m.Nombre.ToLower().Contains(searchTerm) ||
                        (m.Descripcion != null && m.Descripcion.ToLower().Contains(searchTerm))
                    );
                }

                // Filtro solo activos
                if (soloActivos)
                {
                    query = query.Where(m => m.Activo);
                }

                // Ordenamiento dinámico
                if (!string.IsNullOrWhiteSpace(orderBy))
                {
                    var ascending = orderDirection?.ToLower() != "desc";
                    query = orderBy.ToLower() switch
                    {
                        "nombre" => ascending ? query.OrderBy(m => m.Nombre) : query.OrderByDescending(m => m.Nombre),
                        "descripcion" => ascending ? query.OrderBy(m => m.Descripcion) : query.OrderByDescending(m => m.Descripcion),
                        _ => query.OrderBy(m => m.Nombre)
                    };
                }
                else
                {
                    query = query.OrderBy(m => m.Nombre);
                }

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar marcas con filtros");
                throw;
            }
        }

        public async Task<IEnumerable<Marca>> GetChildrenAsync(int parentId)
        {
            return await _context.Marcas
                .AsNoTracking()
                .Where(m => m.ParentId == parentId && !m.IsDeleted && m.Activo)
                .OrderBy(m => m.Nombre)
                .ToListAsync();
        }
    }
}