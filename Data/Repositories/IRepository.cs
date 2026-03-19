using System.Linq.Expressions;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Data.Repositories
{
    /// <summary>
    /// Interfaz genérica para repositorio de entidades.
    /// </summary>
    public interface IRepository<T> where T : AuditableEntity
    {
        Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

        Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);

        Task AddAsync(T entity, CancellationToken ct = default);

        void Update(T entity);

        void Remove(T entity);

        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

        IQueryable<T> Query();
    }
}
