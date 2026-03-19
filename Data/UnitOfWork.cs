namespace TheBuryProject.Data
{
    /// <summary>
    /// Interfaz para el patrón Unit of Work.
    /// Coordina el trabajo de múltiples repositorios y maneja transacciones.
    /// </summary>
    public interface IUnitOfWork
    {
        /// <summary>
        /// Guarda todos los cambios pendientes en la base de datos
        /// </summary>
        Task<int> SaveChangesAsync(CancellationToken ct = default);

        /// <summary>
        /// Inicia una transacción explícita
        /// </summary>
        Task BeginTransactionAsync(CancellationToken ct = default);

        /// <summary>
        /// Confirma la transacción actual
        /// </summary>
        Task CommitTransactionAsync(CancellationToken ct = default);

        /// <summary>
        /// Revierte la transacción actual
        /// </summary>
        Task RollbackTransactionAsync();
    }
}