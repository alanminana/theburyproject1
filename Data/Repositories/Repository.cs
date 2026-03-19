using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Data.Repositories
{
    /// <summary>
    /// Implementación genérica del repositorio.
    /// </summary>
    public class Repository<T> : IRepository<T>
        where T : AuditableEntity
    {
        private readonly AppDbContext _context;
        private readonly DbSet<T> _dbSet;

        public Repository(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = _context.Set<T>();
        }

        public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _dbSet.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);
        }

        public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
        {
            return await _dbSet.ToListAsync(ct);
        }

        public async Task AddAsync(T entity, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            await _dbSet.AddAsync(entity, ct);
        }

        public void Update(T entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            _dbSet.Update(entity);
        }

        public void Remove(T entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            // Soft delete
            entity.IsDeleted = true;
            _dbSet.Update(entity);
        }

        public async Task<bool> ExistsAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return await _dbSet.AnyAsync(predicate, ct);
        }

        public IQueryable<T> Query()
        {
            return _dbSet.AsQueryable();
        }
    }
}
