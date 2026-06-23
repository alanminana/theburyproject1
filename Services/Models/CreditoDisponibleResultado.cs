using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    public class CreditoDisponibleResultado
    {
        public NivelRiesgoCredito NivelCreditoAutomatico { get; set; }

        public NivelRiesgoCredito? NivelCreditoManual { get; set; }

        public NivelRiesgoCredito NivelCreditoFinal { get; set; }

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
