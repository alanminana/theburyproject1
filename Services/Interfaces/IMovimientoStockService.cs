using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// ✅ Servicio centralizado para gestión de movimientos y stock
    /// </summary>
    public interface IMovimientoStockService
    {
        // Obtener movimientos
        Task<IEnumerable<MovimientoStock>> GetAllAsync();
        Task<MovimientoStock?> GetByIdAsync(int id);
        Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId);
        Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId);
        Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo);
        Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta);
        Task<IEnumerable<MovimientoStock>> SearchAsync(
            int? productoId = null,
            TipoMovimiento? tipo = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "desc");

        // Crear movimiento (genérico)
        Task<MovimientoStock> CreateAsync(MovimientoStock movimiento);

        /// <summary>
        /// ✅ NUEVO: Registrar ajuste con TRANSACCIÓN y usuario real
        /// </summary>
        Task<MovimientoStock> RegistrarAjusteAsync(
            int productoId,
            TipoMovimiento tipo,
            decimal cantidad,
            string? referencia,
            string motivo,
            string? usuarioActual = null,
            int? ordenCompraId = null);

        /// <summary>
        /// ✅ NUEVO: Registrar múltiples ENTRADAS de stock en un solo SaveChanges.
        /// Útil para recepciones de orden de compra u operaciones batch donde
        /// se requiere crear un movimiento por ítem y actualizar stock por producto.
        /// </summary>
        Task<List<MovimientoStock>> RegistrarEntradasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> entradas,
            string motivo,
            string? usuarioActual = null,
            int? ordenCompraId = null);

        /// <summary>
        /// ✅ NUEVO: Registrar múltiples SALIDAS de stock en un solo SaveChanges.
        /// Valida stock suficiente por producto considerando el total del batch.
        /// </summary>
        Task<List<MovimientoStock>> RegistrarSalidasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> salidas,
            string motivo,
            string? usuarioActual = null);

        /// <summary>
        /// ✅ NUEVO: Validar disponibilidad de stock
        /// </summary>
        Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad);

        /// <summary>
        /// ✅ NUEVO: Validar cantidad sea positiva
        /// </summary>
        Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad);
    }
}
