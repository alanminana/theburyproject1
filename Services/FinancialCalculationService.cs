using TheBuryProject.Models.DTOs;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class FinancialCalculationService : IFinancialCalculationService
    {
        private const double MesesPorAnio = 12.0;

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
            var expCFTEA = MesesPorAnio / cuotas;
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

        public SimulacionPlanCreditoDto SimularPlanCredito(
            decimal totalVenta,
            decimal anticipo,
            int cuotas,
            decimal tasaMensual,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m)
        {
            var montoFinanciado = ComputeFinancedAmount(totalVenta, anticipo);
            var tasaDecimal = tasaMensual / 100;
            var cuota = ComputePmt(tasaDecimal, cuotas, montoFinanciado);
            var interesTotal = CalcularInteresTotal(montoFinanciado, tasaDecimal, cuotas);
            var totalCuotas = cuota * cuotas;

            var (estado, mensaje, mostrarIngreso, mostrarAntiguedad) = CalcularSemaforo(
                cuota,
                montoFinanciado,
                semaforoRatioVerdeMax,
                semaforoRatioAmarilloMax);

            return new SimulacionPlanCreditoDto
            {
                MontoFinanciado = montoFinanciado,
                CuotaEstimada = cuota,
                TasaAplicada = tasaMensual,
                InteresTotal = interesTotal,
                TotalAPagar = totalCuotas,
                GastosAdministrativos = gastosAdministrativos,
                TotalPlan = totalCuotas + gastosAdministrativos,
                FechaPrimerPago = fechaPrimeraCuota,
                SemaforoEstado = estado,
                SemaforoMensaje = mensaje,
                MostrarMsgIngreso = mostrarIngreso,
                MostrarMsgAntiguedad = mostrarAntiguedad
            };
        }

        private static (string Estado, string Mensaje, bool MostrarIngreso, bool MostrarAntiguedad)
            CalcularSemaforo(
                decimal cuota,
                decimal montoFinanciado,
                decimal ratioVerdeMax,
                decimal ratioAmarilloMax)
        {
            if (montoFinanciado <= 0 || cuota <= 0)
                return ("sinDatos", "Completa los datos para precalificar.", false, false);

            var ratio = cuota / montoFinanciado;

            if (ratio <= ratioVerdeMax)
                return ("verde", "Condiciones preliminares saludables.", false, false);

            if (ratio <= ratioAmarilloMax)
                return ("amarillo", "Revisar ingresos declarados.", true, false);

            return ("rojo", "Las condiciones requieren ajustes.", true, true);
        }
    }
}
