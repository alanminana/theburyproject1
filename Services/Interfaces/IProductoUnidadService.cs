using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface IProductoUnidadService
    {
        /// <summary>
        /// Crea una unidad física individual para un producto.
        /// Genera CodigoInternoUnidad automáticamente.
        /// Registra movimiento inicial en el historial.
        /// </summary>
        Task<ProductoUnidad> CrearUnidadAsync(
            int productoId,
            string? numeroSerie = null,
            string? ubicacionActual = null,
            string? observaciones = null,
            string? usuario = null);

        /// <summary>
        /// Retorna todas las unidades activas de un producto.
        /// </summary>
        Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoAsync(int productoId);

        /// <summary>
        /// Retorna unidades en estado EnStock y no eliminadas de un producto.
        /// </summary>
        Task<IEnumerable<ProductoUnidad>> ObtenerDisponiblesPorProductoAsync(int productoId);

        /// <summary>
        /// Retorna el historial de movimientos de una unidad, ordenado por FechaCambio ascendente.
        /// </summary>
        Task<IEnumerable<ProductoUnidadMovimiento>> ObtenerHistorialAsync(int productoUnidadId);
    }
}
