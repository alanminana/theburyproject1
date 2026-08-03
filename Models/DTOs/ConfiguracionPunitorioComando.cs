namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Parámetros para crear una nueva versión de <see cref="Entities.ConfiguracionPunitorio"/>.
    /// </summary>
    public class ConfiguracionPunitorioComando
    {
        public decimal Porcentaje { get; init; }
        public int PeriodoDias { get; init; }
        public int DiasGracia { get; init; }
        public bool ProrrateoDiario { get; init; } = true;
        public bool AplicacionRetroactiva { get; init; }
        public DateOnly VigenteDesde { get; init; }
        public bool Activa { get; init; } = true;
        public string? MotivoCambio { get; init; }

        /// <summary>
        /// Autorización administrativa para crear una versión con <see cref="VigenteDesde"/>
        /// retroactiva (anterior a hoy comercial), ya verificada por el caller contra el permiso
        /// correspondiente. El servicio NO evalúa claims de ASP.NET Identity — esa verificación es
        /// responsabilidad de la capa de autorización (controller), fuera de alcance en PUN-ML3
        /// porque este lote no agrega UI. Default false: sin caller explícito, no hay autorización.
        /// </summary>
        public bool AutorizadoParaRetroactivo { get; init; }
    }
}
