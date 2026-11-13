using TheBuryProject.Services;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// IVA de los servicios de venta (armados y envío). Los precios globales son finales (IVA incluido) y
    /// tributan a la alícuota general del sistema; una única regla compartida por el cálculo de la venta y
    /// el resumen de alícuotas del comprobante para que Subtotal + IVA cierren contra Total.
    /// </summary>
    internal static class ServiciosVentaIva
    {
        public static decimal Porcentaje => ProductoIvaResolver.PorcentajeDefault;

        public static string NombreAlicuota => NombreDe(Porcentaje);

        public static string NombreDe(decimal porcentaje) => porcentaje > 0m ? $"IVA {porcentaje:0.##}%" : "Sin IVA";

        public static (decimal Neto, decimal Iva) Separar(decimal bruto) => Separar(bruto, Porcentaje);

        /// <summary>Misma regla que <see cref="Separar(decimal)"/> pero con una alícuota explícita (la congelada en la operación).</summary>
        public static (decimal Neto, decimal Iva) Separar(decimal bruto, decimal porcentaje)
        {
            if (bruto <= 0m)
                return (0m, 0m);

            if (porcentaje <= 0m)
                return (Redondear(bruto), 0m);

            var neto = Redondear(bruto / (1m + (porcentaje / 100m)));
            return (neto, Redondear(bruto - neto));
        }

        private static decimal Redondear(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
