namespace TheBuryProject.Services.Models
{
    public class CreditoDisponibleResultado
    {
        /// <summary>Puntaje interno automático (0–5) que gobierna el cupo.</summary>
        public int NivelCreditoAutomatico { get; set; }

        /// <summary>Puntaje interno manual (0–5) si hay override. Null = usa el automático.</summary>
        public int? NivelCreditoManual { get; set; }

        /// <summary>Puntaje interno final aplicado (manual ?? automático).</summary>
        public int NivelCreditoFinal { get; set; }

        public string FuenteNivelCredito { get; set; } = "Automatico";

        public string? MotivoNivelCreditoManual { get; set; }

        public string? NivelCreditoManualAsignadoPor { get; set; }

        public DateTime? NivelCreditoManualAsignadoEnUtc { get; set; }

        public int? LimitePresetId { get; set; }

        public decimal Limite { get; set; }

        public string OrigenLimite { get; set; } = "Nivel crediticio";

        public decimal SaldoVigente { get; set; }

        public decimal Disponible { get; set; }
    }
}
