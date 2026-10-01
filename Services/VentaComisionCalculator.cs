namespace TheBuryProject.Services
{
    /// <summary>
    /// Fórmula única de comisión por línea de venta: porcentaje del producto sobre el importe final de la
    /// línea. La usan VentaService (alta/edición/facturación) y la conversión de cotización a venta para
    /// que el reporte de comisiones por vendedor vea el mismo importe sin importar por dónde nació la venta.
    /// </summary>
    internal static class VentaComisionCalculator
    {
        public static decimal Calcular(decimal baseImporte, decimal porcentaje) =>
            Math.Round(baseImporte * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
