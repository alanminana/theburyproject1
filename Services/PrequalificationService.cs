using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class PrequalificationService : IPrequalificationService
    {
        // Política de cuota/ingreso: la cuota no puede superar el 30% del ingreso efectivo.
        // 3.33 = 1 / 0.30 — el ingreso debe ser al menos 3.33x la cuota.
        private const decimal RatioIngresoMinimoPorCuota = 3.33m;

        public PrequalificationResult Evaluate(decimal installmentAmount, decimal? declaredNetIncome, decimal? declaredDebt, int? employmentSeniorityMonths)
        {
            if (installmentAmount < 0)
                throw new ArgumentException("La cuota estimada no puede ser negativa", nameof(installmentAmount));

            var result = new PrequalificationResult
            {
                Status = PrequalificationStatus.Indeterminate,
                Recommendation = "Solicitar documentación mínima"
            };

            if (!declaredNetIncome.HasValue)
            {
                result.Flags.Add("falta_ingreso");
            }

            if (!employmentSeniorityMonths.HasValue)
            {
                result.Flags.Add("falta_antiguedad");
            }

            if (!declaredNetIncome.HasValue)
            {
                return result;
            }

            var netIncome = declaredNetIncome.Value;
            var effectiveIncome = declaredDebt.HasValue ? netIncome - declaredDebt.Value : netIncome;
            var meetsPolicy = effectiveIncome >= installmentAmount * RatioIngresoMinimoPorCuota;
            result.MeetsThirtyPercentPolicy = meetsPolicy;

            if (!meetsPolicy)
            {
                result.Status = PrequalificationStatus.Red;
                result.Recommendation = "Incrementar anticipo o reducir plazo";
                return result;
            }

            var hasPendingData = result.Flags.Contains("falta_antiguedad");
            result.Status = hasPendingData ? PrequalificationStatus.Yellow : PrequalificationStatus.Green;
            result.Recommendation = hasPendingData ? "Validar antigüedad laboral" : "OK 30%";
            return result;
        }
    }
}
