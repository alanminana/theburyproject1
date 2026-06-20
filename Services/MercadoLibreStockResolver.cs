using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Stock que el ERP considera "publicable" hacia Mercado Libre según el
    /// origen configurado. Es una consulta: nunca modifica stock ni unidades.
    /// </summary>
    public sealed record MercadoLibreStockDisponible(
        int Stock,
        MercadoLibreOrigenStock Origen,
        string? Advertencia,
        bool BloqueaSync = false);

    /// <summary>
    /// Resuelve el stock disponible de una publicación según su origen de stock
    /// (global de la configuración o override de la publicación).
    /// Estático y sin estado, mismo patrón que ProductoIvaResolver.
    /// </summary>
    public static class MercadoLibreStockResolver
    {
        public static MercadoLibreOrigenStock ResolverOrigen(
            MercadoLibreListing listing, MercadoLibreConfiguracion config)
            => listing.OrigenStockOverride ?? config.OrigenStock;

        public static MercadoLibreOrigenStock ResolverOrigen(
            MercadoLibreListing listing, MercadoLibreListingVariation variacion, MercadoLibreConfiguracion config)
            => variacion.OrigenStockOverride ?? listing.OrigenStockOverride ?? config.OrigenStock;

        /// <summary>
        /// Stock publicable de la publicación según el origen efectivo.
        /// Requiere listing.Producto cargado (o devuelve 0 con advertencia).
        /// </summary>
        public static async Task<MercadoLibreStockDisponible> ResolverStockDisponibleAsync(
            AppDbContext context,
            MercadoLibreListing listing,
            MercadoLibreConfiguracion config,
            CancellationToken ct = default)
        {
            var origen = ResolverOrigen(listing, config);

            if (listing.Producto is null)
                return new MercadoLibreStockDisponible(0, origen, "Sin producto vinculado.");

            return await ResolverParaProductoAsync(context, listing.Producto, origen, listing.ProductoUnidadId, ct);
        }

        /// <summary>
        /// Stock publicable de una variación. El producto propio de la variación
        /// tiene prioridad; el fallback al producto de la publicación se permite
        /// solo cuando el caller ya determinó que no hay ambigüedad.
        /// </summary>
        public static async Task<MercadoLibreStockDisponible> ResolverStockDisponibleParaVariacionAsync(
            AppDbContext context,
            MercadoLibreListing listing,
            MercadoLibreListingVariation variacion,
            MercadoLibreConfiguracion config,
            bool permitirProductoListing,
            CancellationToken ct = default)
        {
            var origen = ResolverOrigen(listing, variacion, config);
            var producto = variacion.Producto ?? (permitirProductoListing ? listing.Producto : null);

            if (producto is null)
            {
                return new MercadoLibreStockDisponible(
                    0,
                    origen,
                    "Esta variación requiere vinculación/origen de stock antes de sincronizar.",
                    BloqueaSync: true);
            }

            var productoUnidadId = variacion.ProductoUnidadId
                ?? (listing.ProductoId == producto.Id ? listing.ProductoUnidadId : null);

            return await ResolverParaProductoAsync(context, producto, origen, productoUnidadId, ct);
        }

        /// <summary>
        /// Stock publicable de un Producto según un origen dado (sin publicación,
        /// ej: validación de borradores con el origen global).
        /// </summary>
        public static async Task<MercadoLibreStockDisponible> ResolverParaProductoAsync(
            AppDbContext context,
            Producto producto,
            MercadoLibreOrigenStock origen,
            int? productoUnidadId = null,
            CancellationToken ct = default)
        {
            switch (origen)
            {
                case MercadoLibreOrigenStock.StockFisicoDisponible:
                {
                    var fisico = await ContarUnidadesEnStockAsync(context, producto.Id, ct);

                    string? advertencia = null;
                    if (fisico == 0 && producto.StockActual > 0)
                        advertencia = "El producto tiene stock lógico pero ninguna unidad física registrada: con este origen se publica 0.";

                    return new MercadoLibreStockDisponible(fisico, origen, advertencia);
                }

                case MercadoLibreOrigenStock.UnidadFisicaEspecifica:
                {
                    if (productoUnidadId is null)
                        return new MercadoLibreStockDisponible(0, origen,
                            "Origen 'unidad física específica' sin unidad configurada en la publicación.",
                            BloqueaSync: true);

                    var unidad = await context.ProductoUnidades
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == productoUnidadId.Value && !u.IsDeleted, ct);

                    if (unidad is null || unidad.ProductoId != producto.Id)
                        return new MercadoLibreStockDisponible(0, origen,
                            "La unidad física configurada no existe o no pertenece al producto vinculado.",
                            BloqueaSync: true);

                    return unidad.Estado == EstadoUnidad.EnStock
                        ? new MercadoLibreStockDisponible(1, origen, null)
                        : new MercadoLibreStockDisponible(0, origen,
                            $"La unidad '{unidad.CodigoInternoUnidad}' no está disponible (estado: {unidad.Estado}).");
                }

                case MercadoLibreOrigenStock.DepositoSucursal:
                    // El ERP no maneja stock por sucursal: degradar a stock lógico
                    // con advertencia explícita (la config rechaza elegirlo, esto
                    // es solo defensa ante datos viejos).
                    return new MercadoLibreStockDisponible(
                        StockLogico(producto),
                        origen,
                        "Origen 'depósito/sucursal' no soportado por el ERP: se usó el stock lógico.");

                case MercadoLibreOrigenStock.StockLogicoProducto:
                default:
                {
                    string? advertencia = null;
                    if (producto.StockActual < 0)
                        advertencia = "El stock ERP es negativo; se publica 0.";

                    return new MercadoLibreStockDisponible(StockLogico(producto), origen, advertencia);
                }
            }
        }

        public static Task<int> ContarUnidadesEnStockAsync(
            AppDbContext context, int productoId, CancellationToken ct = default)
            => context.ProductoUnidades
                .AsNoTracking()
                .CountAsync(u => u.ProductoId == productoId
                              && u.Estado == EstadoUnidad.EnStock
                              && !u.IsDeleted, ct);

        private static int StockLogico(Producto producto)
            => Math.Max((int)Math.Floor(producto.StockActual), 0);
    }
}
