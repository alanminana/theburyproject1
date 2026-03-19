namespace TheBuryProject.Services.Interfaces
{
    public interface IFinancialCalculationService
    {
        decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas);
        decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas);
        decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas);
        decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas);

        /// <summary>
        /// Calcula la cuota fija mensual utilizando la fórmula PMT del sistema francés.
        /// PMT = P * [ r * (1 + r)^n ] / [ (1 + r)^n - 1 ]
        /// donde P es el monto financiado, r la tasa mensual y n la cantidad de cuotas.
        /// El resultado se redondea a 2 decimales (AwayFromZero).
        /// </summary>
        /// <param name="tasaMensual">Tasa de interés mensual en valor decimal (0.05 = 5%).</param>
        /// <param name="cuotas">Cantidad de cuotas (n &gt;= 1).</param>
        /// <param name="monto">Monto financiado.</param>
        /// <returns>Valor de la cuota mensual.</returns>
        decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto);

        /// <summary>
        /// Calcula el monto a financiar restando el anticipo del total de la venta.
        /// Valida que 0 &lt;= anticipo &lt;= total.
        /// </summary>
        /// <param name="total">Total de la venta.</param>
        /// <param name="anticipo">Anticipo ingresado.</param>
        /// <returns>Monto a financiar.</returns>
        decimal ComputeFinancedAmount(decimal total, decimal anticipo);

        /// <summary>
        /// Calcula el Costo Financiero Total Efectivo Anual (CFTEA) desde la tasa mensual.
        /// CFTEA = ((1 + tasaMensual)^12 - 1) * 100
        /// </summary>
        /// <param name="tasaMensual">Tasa de interés mensual en valor decimal (0.05 = 5%).</param>
        /// <returns>CFTEA como porcentaje.</returns>
        decimal CalcularCFTEADesdeTasa(decimal tasaMensual);
    }
}