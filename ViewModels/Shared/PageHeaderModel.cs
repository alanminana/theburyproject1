namespace TheBuryProject.ViewModels.Shared
{
    /// <summary>
    /// Modelo para el partial _PageHeader
    /// Define el header unificado de páginas con breadcrumb, título, icono y navegación
    /// </summary>
    public class PageHeaderModel
    {
        /// <summary>
        /// Texto de breadcrumb (ej: "Gestión de precios · Cambios masivos")
        /// </summary>
        public string? Breadcrumb { get; set; }

        /// <summary>
        /// Título principal de la página
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Subtítulo o descripción breve
        /// </summary>
        public string? Subtitle { get; set; }

        /// <summary>
        /// Nombre del icono Bootstrap Icons (sin prefijo bi-)
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// URL directa para el botón "Volver" (tiene prioridad sobre BackAction)
        /// </summary>
        public string? BackUrl { get; set; }

        /// <summary>
        /// Acción del controlador para el botón "Volver"
        /// </summary>
        public string? BackAction { get; set; }

        /// <summary>
        /// Controlador para el botón "Volver" (opcional, usa el actual si no se especifica)
        /// </summary>
        public string? BackController { get; set; }

        /// <summary>
        /// Route ID para el botón "Volver"
        /// </summary>
        public int? BackRouteId { get; set; }

        /// <summary>
        /// Texto del botón "Volver" (default: "Volver")
        /// </summary>
        public string? BackText { get; set; }

        /// <summary>
        /// Mostrar link hacia Catálogo/Index
        /// </summary>
        public bool ShowCatalogoLink { get; set; }

        // ───────────────────────────────────────────────────────────
        // Acción primaria opcional (botón junto al header)
        // ───────────────────────────────────────────────────────────

        /// <summary>
        /// Texto del botón de acción primaria (si está definido, se muestra)
        /// </summary>
        public string? PrimaryActionText { get; set; }

        /// <summary>
        /// Icono del botón de acción primaria (sin prefijo bi-)
        /// </summary>
        public string? PrimaryActionIcon { get; set; }

        /// <summary>
        /// Acción del controlador para el botón primario
        /// </summary>
        public string? PrimaryAction { get; set; }

        /// <summary>
        /// Controlador para el botón primario (opcional)
        /// </summary>
        public string? PrimaryController { get; set; }

        /// <summary>
        /// Clase CSS del botón primario (default: btn-primary btn-sm)
        /// </summary>
        public string PrimaryActionClass { get; set; } = "btn-primary btn-sm";

        /// <summary>
        /// Claim requerido para mostrar el botón primario (ej: "precios:simulate")
        /// </summary>
        public string? PrimaryActionRequiredClaim { get; set; }

        /// <summary>
        /// Factory method para crear un header simple
        /// </summary>
        public static PageHeaderModel Create(
            string title,
            string? icon = null,
            string? breadcrumb = null,
            string? subtitle = null,
            string? backAction = "Index")
        {
            return new PageHeaderModel
            {
                Title = title,
                Icon = icon,
                Breadcrumb = breadcrumb,
                Subtitle = subtitle,
                BackAction = backAction
            };
        }
    }
}
