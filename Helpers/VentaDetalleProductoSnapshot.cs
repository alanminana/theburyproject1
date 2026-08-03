using TheBuryProject.Models.Entities;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Regla única (Micro-lote 5) para capturar y leer la identidad histórica del producto de una
    /// línea de venta (<see cref="VentaDetalle.ProductoNombreAlMomento"/> /
    /// <see cref="VentaDetalle.ProductoCodigoAlMomento"/>).
    ///
    /// Captura: SIEMPRE desde el <see cref="Producto"/> de base de datos en el momento de la operación,
    /// nunca desde el payload del cliente. Es un snapshot "pegajoso": se toma una vez al agregar la línea
    /// y sólo se recaptura si se reemplaza el producto de la línea.
    ///
    /// Lectura: snapshot histórico → (sólo filas legacy sin snapshot) relación viva con Producto →
    /// último recurso <c>Producto #&lt;id&gt;</c>. No inventar nombres.
    /// </summary>
    public static class VentaDetalleProductoSnapshot
    {
        public const string PrefijoProductoDesconocido = "Producto #";

        /// <summary>
        /// Captura el nombre y código del producto en el detalle, tomándolos del <paramref name="producto"/>
        /// de BD. Si el producto no está disponible, usa los fallbacks provistos (p. ej. el snapshot de la
        /// cotización de origen) y, como último recurso, <c>Producto #&lt;id&gt;</c> para el nombre.
        /// </summary>
        public static void Capturar(
            VentaDetalle detalle,
            Producto? producto,
            string? nombreFallback = null,
            string? codigoFallback = null)
        {
            detalle.ProductoNombreAlMomento =
                PrimerNoVacio(producto?.Nombre, nombreFallback)
                ?? $"{PrefijoProductoDesconocido}{detalle.ProductoId}";

            detalle.ProductoCodigoAlMomento = PrimerNoVacio(producto?.Codigo, codigoFallback);
        }

        /// <summary>True si el detalle todavía no tiene capturada la identidad histórica.</summary>
        public static bool NecesitaCaptura(VentaDetalle detalle) =>
            string.IsNullOrWhiteSpace(detalle.ProductoNombreAlMomento);

        /// <summary>Nombre histórico del producto (snapshot → relación viva legacy → Producto #id).</summary>
        public static string ResolverNombre(VentaDetalle detalle)
        {
            if (!string.IsNullOrWhiteSpace(detalle.ProductoNombreAlMomento))
                return detalle.ProductoNombreAlMomento!;
            if (!string.IsNullOrWhiteSpace(detalle.Producto?.Nombre))
                return detalle.Producto!.Nombre;
            return $"{PrefijoProductoDesconocido}{detalle.ProductoId}";
        }

        /// <summary>Código histórico del producto (snapshot → relación viva legacy → null).</summary>
        public static string? ResolverCodigo(VentaDetalle detalle)
        {
            if (!string.IsNullOrWhiteSpace(detalle.ProductoCodigoAlMomento))
                return detalle.ProductoCodigoAlMomento;
            return detalle.Producto?.Codigo;
        }

        private static string? PrimerNoVacio(string? a, string? b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            if (!string.IsNullOrWhiteSpace(b)) return b;
            return null;
        }
    }
}
