namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Helper para cálculos financieros de créditos.
    /// [DEPRECATED] Usar IFinancialCalculationService en su lugar.
    /// </summary>
    public static class CreditoHelper
    {
        /// <summary>
        /// Calcula el monto de cuota usando el sistema francés (cuota fija)
        /// Fórmula: Cuota = Capital * (i * (1 + i)^n) / ((1 + i)^n - 1)
        /// donde i = tasa mensual (decimal), n = cantidad de cuotas
        /// </summary>
        [Obsolete("Usar IFinancialCalculationService.CalcularCuotaSistemaFrances en su lugar")]
        public static decimal CalcularMontoCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cantidadCuotas)
        {
            if (cantidadCuotas <= 0)
                throw new ArgumentException("La cantidad de cuotas debe ser mayor a 0");

            if (tasaMensual < 0)
                throw new ArgumentException("La tasa mensual no puede ser negativa");

            // Si tasa es 0, dividir en partes iguales
            if (tasaMensual == 0)
                return monto / cantidadCuotas;

            // Sistema Franc�s: C = M * [i(1+i)^n] / [(1+i)^n - 1]
            var numerador = tasaMensual * (decimal)Math.Pow((double)(1 + tasaMensual), cantidadCuotas);
            var denominador = (decimal)Math.Pow((double)(1 + tasaMensual), cantidadCuotas) - 1;

            var cuota = monto * (numerador / denominador);
            return Math.Round(cuota, 2);
        }

        /// <summary>
        /// Calcula la Costo Financiero Total Efectivo Anual (CFTEA)
        /// CFTEA = [(1 + i)^12 - 1] * 100
        /// donde i = tasa mensual (decimal)
        /// </summary>
        [Obsolete("Usar IFinancialCalculationService.CalcularCFTEADesdeTasa en su lugar")]
        public static decimal CalcularCFTEA(decimal tasaMensual)
        {
            if (tasaMensual < 0)
                throw new ArgumentException("La tasa mensual no puede ser negativa");

            // CFTEA = ((1 + i)^12 - 1) * 100
            var cftea = ((decimal)Math.Pow((double)(1 + tasaMensual), 12) - 1) * 100;
            return Math.Round(cftea, 4);
        }

        /// <summary>
        /// Calcula el saldo adeudado en un momento específico del crédito
        /// </summary>
        [Obsolete("Usar IFinancialCalculationService en su lugar")]
        public static decimal CalcularSaldoPendiente(decimal montoOriginal, decimal tasaMensual, int cuotasPagadas, int totalCuotas)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var montoCuota = CalcularMontoCuotaSistemaFrances(montoOriginal, tasaMensual, totalCuotas);
#pragma warning restore CS0618
            var saldoPendiente = montoOriginal;

            for (int i = 0; i < cuotasPagadas; i++)
            {
                var interesCuota = saldoPendiente * tasaMensual;
                var capitalCuota = montoCuota - interesCuota;
                saldoPendiente -= capitalCuota;
            }

            return Math.Max(0, Math.Round(saldoPendiente, 2));
        }

        /// <summary>
        /// Calcula el interés de una cuota específica en el sistema francés
        /// </summary>
        [Obsolete("Usar IFinancialCalculationService en su lugar")]
        public static decimal CalcularInteresCuota(decimal montoOriginal, decimal tasaMensual, int numeroCuota, int totalCuotas)
        {
            if (numeroCuota < 1 || numeroCuota > totalCuotas)
                throw new ArgumentException("Número de cuota inválido");

#pragma warning disable CS0618 // Type or member is obsolete
            var montoCuota = CalcularMontoCuotaSistemaFrances(montoOriginal, tasaMensual, totalCuotas);
#pragma warning restore CS0618
            var saldoPendiente = montoOriginal;

            for (int i = 1; i < numeroCuota; i++)
            {
                var interesCuota = saldoPendiente * tasaMensual;
                var capitalCuota = montoCuota - interesCuota;
                saldoPendiente -= capitalCuota;
            }

            var interes = saldoPendiente * tasaMensual;
            return Math.Round(interes, 2);
        }

        /// <summary>
        /// Calcula el capital de una cuota específica en el sistema francés
        /// </summary>
        [Obsolete("Usar IFinancialCalculationService en su lugar")]
        public static decimal CalcularCapitalCuota(decimal montoOriginal, decimal tasaMensual, int numeroCuota, int totalCuotas)
        {
            if (numeroCuota < 1 || numeroCuota > totalCuotas)
                throw new ArgumentException("Número de cuota inválido");

#pragma warning disable CS0618 // Type or member is obsolete
            var montoCuota = CalcularMontoCuotaSistemaFrances(montoOriginal, tasaMensual, totalCuotas);
            var interes = CalcularInteresCuota(montoOriginal, tasaMensual, numeroCuota, totalCuotas);
#pragma warning restore CS0618
            var capital = montoCuota - interes;

            return Math.Round(capital, 2);
        }
    }
}