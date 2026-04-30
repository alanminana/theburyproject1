using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services
{
    internal static class ProductoIvaResolver
    {
        public const decimal PorcentajeDefault = 21m;

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
