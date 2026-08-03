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
            decimal porcentajeRecargo,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m)
        {
            if (cuotas < 1)
                throw new ArgumentException("La cantidad de cuotas debe ser al menos 1", nameof(cuotas));

            if (porcentajeRecargo < 0)
                throw new ArgumentException("El porcentaje de recargo no puede ser negativo", nameof(porcentajeRecargo));

            // Recargo TOTAL del plan sobre el saldo posterior al anticipo (no interés
            // compuesto mensual): ver IFinancialCalculationService.SimularPlanCredito.
            var montoFinanciado = ComputeFinancedAmount(totalVenta, anticipo);
            var interesTotal = Math.Round(montoFinanciado * porcentajeRecargo / 100m, 2, MidpointRounding.AwayFromZero);
            var totalFinanciado = montoFinanciado + interesTotal;
            var cuota = Math.Round(totalFinanciado / cuotas, 2, MidpointRounding.AwayFromZero);

            var cuotasPlan = ConstruirVectorCuotas(montoFinanciado, interesTotal, totalFinanciado, cuotas);

            var (estado, mensaje, mostrarIngreso, mostrarAntiguedad) = CalcularSemaforo(
                cuota,
                montoFinanciado,
                semaforoRatioVerdeMax,
                semaforoRatioAmarilloMax);

            return new SimulacionPlanCreditoDto
            {
                MontoFinanciado = montoFinanciado,
                CuotaEstimada = cuota,
                TasaAplicada = porcentajeRecargo,
                InteresTotal = interesTotal,
                TotalAPagar = totalFinanciado,
                GastosAdministrativos = gastosAdministrativos,
                TotalPlan = totalFinanciado + gastosAdministrativos,
                FechaPrimerPago = fechaPrimeraCuota,
                Cuotas = cuotasPlan,
                SemaforoEstado = estado,
                SemaforoMensaje = mensaje,
                MostrarMsgIngreso = mostrarIngreso,
                MostrarMsgAntiguedad = mostrarAntiguedad
            };
        }

        /// <summary>
        /// Construye el vector exacto de cuotas (capital/interés/total) de un plan de
        /// recargo total. Dos vectores independientes de centavos enteros (ver
        /// <see cref="DistribuirEnCentavos"/>), uno para <c>Total</c> y otro para
        /// <c>Interes</c>; <c>Capital</c> se deriva siempre como Total - Interes (nunca
        /// se redondea de forma independiente), así que Capital + Interes == Total en
        /// cada ítem por construcción, y Σ Capital == montoFinanciado se cumple solo.
        ///
        /// Por qué centavos enteros y no <c>Math.Round</c> por cuota: redondear
        /// "total financiado / n" cuota por cuota y restar la suma de las anteriores
        /// para la última puede dejarla negativa cuando el importe es chico frente a
        /// la cantidad de cuotas (ver corrección post-ML3: total financiado 0,05 en 9
        /// cuotas — base redondeada 0,01 × 8 = 0,08, ya supera los 0,05 totales). La
        /// división entera de centavos nunca tiene ese problema: cada cuota vale
        /// <c>baseCentavos</c> o <c>baseCentavos + resto</c>, y ambos son siempre
        /// &gt;= 0 cuando el importe de entrada lo es — no hay resta que pueda cruzar
        /// cero.
        ///
        /// Por qué el interés (no el capital) es el vector independiente: construir el
        /// interés en base a interesTotal (siempre &gt;= 0) evita que la última cuota
        /// termine con interés negativo (ver ML3, corrección post-reporte: caso capital
        /// 100,00 / recargo 0,01 / 3 cuotas).
        ///
        /// Caso extremo restante (recargo muy grande frente al capital financiado): el
        /// interés "crudo" de la última cuota puede superar el Total de esa misma
        /// cuota. Se corrige moviendo el excedente hacia las cuotas anteriores, de
        /// atrás hacia adelante, sin que ninguna supere su propio Total. Esto solo
        /// mueve centavos entre cuotas: Σ Interes sigue siendo exactamente
        /// interesTotal, así que Σ Capital sigue siendo exactamente montoFinanciado.
        /// Es matemáticamente siempre posible porque interesTotal &lt;= totalFinanciado
        /// (el recargo nunca supera al total financiado). El caso simétrico (interés
        /// de la última cuota por debajo de cero) no puede ocurrir: al construirse por
        /// división entera de centavos no negativos, ningún ítem del vector de interés
        /// es nunca negativo.
        /// </summary>
        private static IReadOnlyList<CuotaPlanCreditoDto> ConstruirVectorCuotas(
            decimal montoFinanciado,
            decimal interesTotal,
            decimal totalFinanciado,
            int cuotas)
        {
            var totales = DistribuirEnCentavos(totalFinanciado, cuotas);
            var intereses = DistribuirEnCentavos(interesTotal, cuotas);

            var ultimo = cuotas - 1;
            if (intereses[ultimo] > totales[ultimo])
            {
                // El interés que le "tocaría" a la última cuota no entra en su propio
                // total: se lo devolvemos a las cuotas anteriores (aumenta su interés,
                // reduce su capital), sin que ninguna supere su propio total.
                var exceso = intereses[ultimo] - totales[ultimo];
                intereses[ultimo] = totales[ultimo];
                for (var i = ultimo - 1; i >= 0 && exceso > 0; i--)
                {
                    var capacidad = totales[i] - intereses[i];
                    var incremento = Math.Min(capacidad, exceso);
                    intereses[i] += incremento;
                    exceso -= incremento;
                }
            }

            var plan = new CuotaPlanCreditoDto[cuotas];
            for (var i = 0; i < cuotas; i++)
            {
                plan[i] = new CuotaPlanCreditoDto
                {
                    NumeroCuota = i + 1,
                    Capital = CentavosADecimal(totales[i] - intereses[i]),
                    Interes = CentavosADecimal(intereses[i]),
                    Total = CentavosADecimal(totales[i])
                };
            }

            return plan;
        }

        /// <summary>
        /// Reparte un importe monetario en <paramref name="cuotas"/> partes iguales
        /// mediante división entera de centavos: el importe se convierte a centavos con
        /// un único <see cref="Math.Round(decimal, int, MidpointRounding)"/> en
        /// <c>decimal</c> (sin pasar por <c>double</c>), y el reparto usa división y
        /// resto enteros — la cuota regular es <c>centavos / cuotas</c> y la última
        /// suma el resto completo (<c>centavos % cuotas</c>). Con importe y cuotas no
        /// negativos, todo valor del vector resultante es siempre &gt;= 0.
        /// </summary>
        private static long[] DistribuirEnCentavos(decimal importe, int cuotas)
        {
            var centavos = (long)Math.Round(importe * 100m, 0, MidpointRounding.AwayFromZero);
            var baseCentavos = centavos / cuotas;
            var residuo = centavos % cuotas;

            var vector = new long[cuotas];
            for (var i = 0; i < cuotas - 1; i++)
                vector[i] = baseCentavos;
            vector[cuotas - 1] = baseCentavos + residuo;
            return vector;
        }

        private static decimal CentavosADecimal(long centavos) => centavos / 100m;

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
