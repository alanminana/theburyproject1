using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Punitorio
{
    /// <summary>
    /// Estado efectivo de <c>ConfiguracionPunitorio</c> para <see cref="TheBuryProject.Services.IRelojComercial.HoyComercial"/> (PUN-ML8).
    /// Los cuatro booleanos son mutuamente excluyentes salvo <see cref="EsCero"/>, que solo puede
    /// darse junto con <see cref="EsActiva"/> — nunca se infiere el estado en Razor ni en JS.
    /// </summary>
    public class ConfiguracionPunitorioActualViewModel
    {
        public bool TieneConfiguracion { get; set; }
        public bool EsActiva { get; set; }
        public bool EsInactiva { get; set; }

        /// <summary>Activa con <c>Porcentaje == 0</c>: distinto de ausencia e inactividad (contrato PUN-ML3).</summary>
        public bool EsCero { get; set; }

        public string EstadoTexto { get; set; } = "Sin configuración";

        /// <summary>Clase <c>badge-erp-*</c> a aplicar. Nunca es la única señal de estado (siempre acompaña a <see cref="EstadoTexto"/>).</summary>
        public string EstadoBadgeClass { get; set; } = "badge-erp";

        public DateOnly? VigenteDesde { get; set; }
        public decimal? Porcentaje { get; set; }
        public int? PeriodoDias { get; set; }
        public int? DiasGracia { get; set; }

        /// <summary>Explicación de la regla en lenguaje llano, armada 100% server-side.</summary>
        public string? ReglaTexto { get; set; }

        public ConfiguracionPunitorioVersionViewModel? ProximaVersion { get; set; }
    }

    /// <summary>
    /// Una fila de solo lectura: se reusa tanto para "próxima versión" como para cada fila del
    /// historial. No expone la entidad EF (contrato PUN-ML8: "la UI no edita ni elimina versiones").
    /// </summary>
    public class ConfiguracionPunitorioVersionViewModel
    {
        public int Id { get; set; }
        public DateOnly VigenteDesde { get; set; }
        public bool Activa { get; set; }
        public decimal Porcentaje { get; set; }
        public int PeriodoDias { get; set; }
        public int DiasGracia { get; set; }
        public string? MotivoCambio { get; set; }
        public bool EsRetroactiva { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool EsVigenteHoy { get; set; }
        public bool EsFutura { get; set; }
    }

    /// <summary>
    /// Form bindable de "Crear nueva versión" (PUN-ML8). Deliberadamente NO incluye
    /// <c>ProrrateoDiario</c>, <c>AplicacionRetroactiva</c> ni <c>AutorizadoParaRetroactivo</c>:
    /// esos tres los deriva/resuelve el controller server-side, nunca el navegador.
    /// </summary>
    public class CrearConfiguracionPunitorioViewModel
    {
        [Required(ErrorMessage = "La fecha de vigencia es obligatoria.")]
        [DataType(DataType.Date)]
        [Display(Name = "Vigente desde")]
        public DateOnly? VigenteDesde { get; set; }

        [Display(Name = "Activa")]
        public bool Activa { get; set; } = true;

        [Range(0, 1_000_000, ErrorMessage = "El porcentaje debe ser mayor o igual a 0.")]
        [Display(Name = "Porcentaje")]
        public decimal Porcentaje { get; set; }

        [Range(1, 3650, ErrorMessage = "El período debe ser de al menos 1 día.")]
        [Display(Name = "Período (días)")]
        public int PeriodoDias { get; set; } = 30;

        [Range(0, 3650, ErrorMessage = "Los días de gracia no pueden ser negativos.")]
        [Display(Name = "Días de gracia")]
        public int DiasGracia { get; set; }

        [StringLength(500, ErrorMessage = "El motivo no puede superar los 500 caracteres.")]
        [Display(Name = "Motivo del cambio")]
        public string? MotivoCambio { get; set; }
    }

    /// <summary>Agrupa todo lo que necesita la pestaña "Punitorios por mora" de CreditoPersonal_tw (PUN-ML8).</summary>
    public class ConfiguracionPunitorioPageViewModel
    {
        public ConfiguracionPunitorioActualViewModel Actual { get; set; } = new();
        public List<ConfiguracionPunitorioVersionViewModel> Historial { get; set; } = new();
        public CrearConfiguracionPunitorioViewModel CrearForm { get; set; } = new();

        /// <summary>
        /// <see cref="TheBuryProject.Services.IRelojComercial.HoyComercial"/> tal como la resolvió el
        /// controller. La vista la usa para detectar retroactividad en el cliente (solo UX: abrir el
        /// modal de confirmación) — nunca <c>DateTime.Today</c>/<c>new Date()</c> del navegador. La
        /// derivación autoritativa de <c>AplicacionRetroactiva</c> siempre la vuelve a hacer el servidor.
        /// </summary>
        public DateOnly HoyComercial { get; set; }
    }

    /// <summary>Un punto de la vista previa (no autoritativa) devuelta por <c>PreviewPunitorio</c>.</summary>
    public class PunitorioPreviewPuntoViewModel
    {
        public string Etiqueta { get; set; } = string.Empty;
        public int Dias { get; set; }
        public string Estado { get; set; } = string.Empty;
        public decimal? Importe { get; set; }
    }

    /// <summary>
    /// Salida JSON de <c>PreviewPunitorio</c>. Los importes vienen exclusivamente de
    /// <see cref="TheBuryProject.Services.Interfaces.IPunitorioCalculator"/> — nunca se recalculan en JS.
    /// </summary>
    public class PunitorioPreviewViewModel
    {
        public decimal MontoReferencia { get; set; }
        public List<PunitorioPreviewPuntoViewModel> Puntos { get; set; } = new();
    }
}
