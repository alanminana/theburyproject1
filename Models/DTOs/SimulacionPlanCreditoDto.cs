namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Resultado de la simulación de un plan de crédito personal.
    /// </summary>
    public class SimulacionPlanCreditoDto
    {
        public decimal MontoFinanciado { get; init; }
        public decimal CuotaEstimada { get; init; }
        public decimal TasaAplicada { get; init; }
        public decimal InteresTotal { get; init; }
        public decimal TotalAPagar { get; init; }
        public decimal GastosAdministrativos { get; init; }
        public decimal TotalPlan { get; init; }
        public DateTime FechaPrimerPago { get; init; }

        // Semáforo de precalificación
        public string SemaforoEstado { get; init; } = "sinDatos";
        public string SemaforoMensaje { get; init; } = string.Empty;
        public bool MostrarMsgIngreso { get; init; }
        public bool MostrarMsgAntiguedad { get; init; }
    }
}
