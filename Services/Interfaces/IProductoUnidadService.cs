using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

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
        /// Retorna unidades activas de un producto aplicando filtros operativos de consulta.
        /// </summary>
        Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoFiltradoAsync(
            int productoId,
            ProductoUnidadFiltros filtros);

        /// <summary>
        /// Retorna una unidad activa por id con sus datos de consulta.
        /// </summary>
        Task<ProductoUnidad?> ObtenerPorIdAsync(int productoUnidadId);

        /// <summary>
        /// Retorna unidades en estado EnStock y no eliminadas de un producto.
        /// </summary>
        Task<IEnumerable<ProductoUnidad>> ObtenerDisponiblesPorProductoAsync(int productoId);

        /// <summary>
        /// Retorna el historial de movimientos de una unidad, ordenado por FechaCambio ascendente.
        /// </summary>
        Task<IEnumerable<ProductoUnidadMovimiento>> ObtenerHistorialAsync(int productoUnidadId);

        /// <summary>
        /// Marca una unidad como Vendida. Estado previo debe ser EnStock.
        /// Registra VentaDetalleId, ClienteId y FechaVenta. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> MarcarVendidaAsync(
            int productoUnidadId,
            int ventaDetalleId,
            int? clienteId = null,
            string? usuario = null);

        /// <summary>
        /// Marca una unidad como Faltante. Estado previo debe ser EnStock.
        /// Motivo obligatorio. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> MarcarFaltanteAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null);

        /// <summary>
        /// Marca una unidad como Baja. Estado previo debe ser EnStock, Devuelta o Faltante.
        /// Motivo obligatorio. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> MarcarBajaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null);

        /// <summary>
        /// Reintegra una unidad a EnStock. Estado previo debe ser Faltante o Devuelta.
        /// Limpia VentaDetalleId, ClienteId y FechaVenta. Motivo obligatorio. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> ReintegrarAStockAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null);

        /// <summary>
        /// Revierte una unidad de Vendida a EnStock (cancelación de venta).
        /// Limpia VentaDetalleId, ClienteId y FechaVenta. Motivo obligatorio. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> RevertirVentaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null);

        /// <summary>
        /// Marca una unidad como Devuelta. Estado previo debe ser Vendida.
        /// Motivo obligatorio. Crea movimiento de historial.
        /// </summary>
        Task<ProductoUnidad> MarcarDevueltaAsync(
            int productoUnidadId,
            string motivo,
            string? usuario = null);
    }
}
