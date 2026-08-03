namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Un ítem del vector exacto de cuotas de un plan de crédito personal (recargo total,
    /// ver <see cref="SimulacionPlanCreditoDto"/>). La suma de <see cref="Total"/> de todos
    /// los ítems es exactamente igual al total financiado del plan y la suma de
    /// <see cref="Interes"/> es exactamente igual al recargo total: ambos vectores se
    /// construyen por división entera de centavos (cuota regular + resto completo en el
    /// último ítem, ver <c>ConstruirVectorCuotas</c> y <c>DistribuirEnCentavos</c> en
    /// <c>FinancialCalculationService</c>), con una corrección adicional si el interés es
    /// extremo frente al capital. Por construcción, Capital + Interes == Total en cada
    /// ítem, ninguno de los tres es negativo (ni siquiera en importes muy chicos frente a
    /// la cantidad de cuotas), y la suma de <see cref="Capital"/> es siempre exactamente
    /// igual al monto financiado.
    /// </summary>
    public sealed class CuotaPlanCreditoDto
    {
        public int NumeroCuota { get; init; }
        public decimal Capital { get; init; }
        public decimal Interes { get; init; }
        public decimal Total { get; init; }
    }
}
