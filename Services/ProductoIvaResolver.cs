using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services
{
    internal static class ProductoIvaResolver
    {
        /// <summary>
        /// Alícuota general del sistema (fallback interno). Se puede ajustar por
        /// configuración ("Iva:PorcentajeDefault") vía <see cref="Configurar"/> al inicio.
        /// </summary>
        public static decimal PorcentajeDefault { get; private set; } = 21m;

        /// <summary>
        /// Fija la alícuota general desde configuración. Valores nulos o fuera de rango
        /// (0-100) se ignoran y se conserva el valor vigente.
        /// </summary>
        public static void Configurar(decimal? porcentaje)
        {
            if (porcentaje is >= 0m and <= 100m)
                PorcentajeDefault = porcentaje.Value;
        }

        public static decimal ResolverPorcentajeIVAProducto(Producto producto)
        {
            if (producto.AlicuotaIVA is { Activa: true, IsDeleted: false })
                return producto.AlicuotaIVA.Porcentaje;

            if (producto.Categoria?.AlicuotaIVA is { Activa: true, IsDeleted: false })
                return producto.Categoria.AlicuotaIVA.Porcentaje;

            if (producto.PorcentajeIVA is >= 0m and <= 100m)
                return producto.PorcentajeIVA;

            return PorcentajeDefault;
        }
    }
}
