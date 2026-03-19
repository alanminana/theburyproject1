using System;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class FinancialCalculationService : IFinancialCalculationService
    {
        public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas)
        {
            if (monto <= 0)
                throw new ArgumentException("El monto debe ser mayor a cero", nameof(monto));

            if (cuotas <= 0)
                throw new ArgumentException("La cantidad de cuotas debe ser mayor a cero", nameof(cuotas));

            if (tasaMensual == 0)
                return monto / cuotas;

            var factor = (decimal)Math.Pow((double)(1 + tasaMensual), cuotas);
            return monto * (tasaMensual * factor) / (factor - 1);
        }

        public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas)
        {
            if (tasaMensual == 0)
                return monto;

            var cuotaMensual = CalcularCuotaSistemaFrances(monto, tasaMensual, cuotas);
            return cuotaMensual * cuotas;
        }

        public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas)
        {
            if (cuotas <= 0 || montoInicial <= 0)
                return 0;

            var baseCFTEA = (double)(totalAPagar / montoInicial);
            var expCFTEA = 12.0 / cuotas;
            return (decimal)(Math.Pow(baseCFTEA, expCFTEA) - 1) * 100;
        }

        public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas)
        {
            var totalConInteres = CalcularTotalConInteres(monto, tasaMensual, cuotas);
            return totalConInteres - monto;
        }

        public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto)
        {
            if (monto < 0)
                throw new ArgumentException("El monto financiado no puede ser negativo", nameof(monto));

            if (cuotas < 1)
                throw new ArgumentException("La cantidad de cuotas debe ser al menos 1", nameof(cuotas));

            if (tasaMensual < 0)
                throw new ArgumentException("La tasa mensual no puede ser negativa", nameof(tasaMensual));

            if (monto == 0)
                return 0;

            if (tasaMensual == 0)
                return Math.Round(monto / cuotas, 2, MidpointRounding.AwayFromZero);

            var factor = (decimal)Math.Pow((double)(1 + tasaMensual), cuotas);
            var cuota = monto * (tasaMensual * factor) / (factor - 1);
            return Math.Round(cuota, 2, MidpointRounding.AwayFromZero);
        }

        public decimal ComputeFinancedAmount(decimal total, decimal anticipo)
        {
            if (total < 0)
                throw new ArgumentException("El total no puede ser negativo", nameof(total));

            if (anticipo < 0)
                throw new ArgumentException("El anticipo no puede ser negativo", nameof(anticipo));

            if (anticipo > total)
                throw new ArgumentException("El anticipo no puede superar el total", nameof(anticipo));

            return total - anticipo;
        }

        public decimal CalcularCFTEADesdeTasa(decimal tasaMensual)
        {
            if (tasaMensual < 0)
                throw new ArgumentException("La tasa mensual no puede ser negativa", nameof(tasaMensual));

            // CFTEA = ((1 + i)^12 - 1) * 100
            var cftea = ((decimal)Math.Pow((double)(1 + tasaMensual), 12) - 1) * 100;
            return Math.Round(cftea, 4);
        }
    }
}
