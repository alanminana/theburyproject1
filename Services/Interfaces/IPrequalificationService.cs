using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    public interface IPrequalificationService
    {
        /// <summary>
        /// Evalúa la precalificación rápida aplicando la política del 30% cuando exista ingreso declarado.
        /// Retorna un semáforo preliminar, una recomendación y los flags de datos faltantes.
        /// </summary>
        /// <param name="installmentAmount">Monto de la cuota estimada.</param>
        /// <param name="declaredNetIncome">Ingreso neto declarado (opcional).</param>
        /// <param name="declaredDebt">Endeudamiento declarado (opcional).</param>
        /// <param name="employmentSeniorityMonths">Antigüedad laboral en meses (opcional).</param>
        PrequalificationResult Evaluate(decimal installmentAmount, decimal? declaredNetIncome, decimal? declaredDebt, int? employmentSeniorityMonths);
    }
}
