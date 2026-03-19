using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionMoraCompletaViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Interés Punitorio Diario (%)")]
        [Range(0, 100, ErrorMessage = "El porcentaje debe estar entre 0 y 100")]
        public decimal TasaMoraDiaria { get; set; } = 0.1m;

        [Display(Name = "Días de Gracia")]
        [Range(0, 90, ErrorMessage = "Los días de gracia deben estar entre 0 y 90")]
        public int DiasGracia { get; set; } = 3;

        [Display(Name = "Proceso Automático Activo")]
        public bool ProcesoAutomaticoActivo { get; set; } = true;

        [Display(Name = "Hora de Ejecución")]
        public TimeSpan HoraEjecucionDiaria { get; set; } = new TimeSpan(8, 0, 0);

        public List<AlertaMoraViewModel> Alertas { get; set; } = new List<AlertaMoraViewModel>();
    }

    public class AlertaMoraViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Días desde Vencimiento")]
        [Required(ErrorMessage = "Los días son requeridos")]
        public int DiasRelativoVencimiento { get; set; }

        [Display(Name = "Color")]
        [Required(ErrorMessage = "El color es requerido")]
        [StringLength(7, MinimumLength = 7)]
        [RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "El color debe estar en formato hexadecimal (#RRGGBB)")]
        public string ColorAlerta { get; set; } = "#FF0000";

        [Display(Name = "Descripción")]
        [StringLength(100)]
        public string? Descripcion { get; set; }

        [Display(Name = "Nivel de Prioridad")]
        [Range(1, 5)]
        public int NivelPrioridad { get; set; } = 1;

        [Display(Name = "Activa")]
        public bool Activa { get; set; } = true;

        [Display(Name = "Orden")]
        public int Orden { get; set; }
    }
}
