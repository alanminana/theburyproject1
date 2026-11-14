using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services
{
    internal static class ProductoIvaResolver
    {
        /// <summary>
        /// Alícuota general del sistema (fallback interno). Se puede ajustar por
        /// configuración ("Iva:PorcentajeDefault") vía <see cref="Configurar"/> al inicio.
        /// </summary>
        public static decimal PorcentajeDefault => Sobrescritura.Value ?? ConfiguradoGlobal;

        private static decimal ConfiguradoGlobal { get; set; } = 21m;

        private static readonly AsyncLocal<decimal?> Sobrescritura = new();

        /// <summary>SOLO PARA TESTS: simula un cambio de configuración "Iva:PorcentajeDefault" dentro del contexto asíncrono actual.</summary>
        internal static IDisposable UsarPorcentajeDefaultParaPruebas(decimal porcentaje)
        {
            var previo = Sobrescritura.Value;
            Sobrescritura.Value = porcentaje;
            return new Restaurar(previo);
        }

        private sealed class Restaurar(decimal? previo) : IDisposable
        {
            public void Dispose() => Sobrescritura.Value = previo;
        }

        /// <summary>
        /// Fija la alícuota general desde configuración. Valores nulos o fuera de rango
        /// (0-100) se ignoran y se conserva el valor vigente.
        /// </summary>
        public static void Configurar(decimal? porcentaje)
        {
            if (porcentaje is >= 0m and <= 100m)
                ConfiguradoGlobal = porcentaje.Value;
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
