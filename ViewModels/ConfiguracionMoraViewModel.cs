using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionMoraViewModel
    {
        public int Id { get; set; }

        // Configuración de días
        // H-29: mismos rangos que ConfiguracionMoraExpandidaViewModel (este endpoint legacy aceptaba valores negativos).
        [Range(0, 90, ErrorMessage = "Los días de gracia deben estar entre 0 y 90")]
        public int DiasGracia { get; set; }
        public int DiasGraciaMora { get; set; } // Alias para DiasGracia
        public int DiasAntesAlertaVencimiento { get; set; } = 7;

        // Configuración de recargos
        [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ErrorMessage = "La tasa debe estar entre 0 y 100")]
        public decimal PorcentajeRecargo { get; set; }
        public decimal TasaMoraDiaria { get; set; }
        public decimal PorcentajeRecargoPrimerMes { get; set; }
        public decimal PorcentajeRecargoSegundoMes { get; set; }
        public decimal PorcentajeRecargoTercerMes { get; set; }

        // Configuración de automatización
        public bool CalculoAutomatico { get; set; }
        public bool NotificacionAutomatica { get; set; }
        public bool JobActivo { get; set; }
        public TimeSpan HoraEjecucion { get; set; }
        public DateTime? UltimaEjecucion { get; set; }
    }
}
