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

        public static string NombreAlicuota => Porcentaje > 0m ? $"IVA {Porcentaje:0.##}%" : "Sin IVA";

        public static (decimal Neto, decimal Iva) Separar(decimal bruto)
        {
            if (bruto <= 0m)
                return (0m, 0m);

            if (Porcentaje <= 0m)
                return (Redondear(bruto), 0m);

            var neto = Redondear(bruto / (1m + (Porcentaje / 100m)));
            return (neto, Redondear(bruto - neto));
        }

        private static decimal Redondear(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
