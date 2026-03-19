namespace TheBuryProject.ViewModels.Shared
{
    /// <summary>
    /// Modelo para el partial _FormSection
    /// Define una sección visual dentro de un formulario
    /// </summary>
    public class FormSectionModel
    {
        /// <summary>
        /// Título de la sección
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Icono Bootstrap Icons (sin prefijo bi-)
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// Clase CSS para el color del título (default: text-info)
        /// </summary>
        public string TitleColorClass { get; set; } = "text-info";

        /// <summary>
        /// Agregar margin-top
        /// </summary>
        public bool MarginTop { get; set; }

        /// <summary>
        /// Factory method para sección de información básica
        /// </summary>
        public static FormSectionModel InfoBasica() => new()
        {
            Title = "Información Básica",
            Icon = "info-circle",
            TitleColorClass = "text-info"
        };

        /// <summary>
        /// Factory method para sección de precios
        /// </summary>
        public static FormSectionModel Precios() => new()
        {
            Title = "Precios",
            Icon = "cash-coin",
            TitleColorClass = "text-success",
            MarginTop = true
        };

        /// <summary>
        /// Factory method para sección de stock
        /// </summary>
        public static FormSectionModel Stock() => new()
        {
            Title = "Stock",
            Icon = "box-seam",
            TitleColorClass = "text-warning",
            MarginTop = true
        };

        /// <summary>
        /// Factory method para sección de configuración
        /// </summary>
        public static FormSectionModel Configuracion() => new()
        {
            Title = "Configuración",
            Icon = "gear",
            TitleColorClass = "text-secondary",
            MarginTop = true
        };
    }
}
