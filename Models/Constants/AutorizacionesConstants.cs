namespace TheBuryProject.Models.Constants;

/// <summary>
/// Constantes para el módulo de autorizaciones y sus acciones.
/// </summary>
public static class AutorizacionesConstants
{
    /// <summary>
    /// Clave del módulo de autorizaciones.
    /// </summary>
    public const string Modulo = "autorizaciones";

    /// <summary>
    /// Acciones disponibles dentro del módulo de autorizaciones.
    /// </summary>
    public static class Acciones
    {
        /// <summary>
        /// Ver solicitudes y panel de autorizaciones.
        /// </summary>
        public const string Ver = "view";

        /// <summary>
        /// Aprobar solicitudes de autorización.
        /// </summary>
        public const string Aprobar = "approve";

        /// <summary>
        /// Rechazar solicitudes de autorización.
        /// </summary>
        public const string Rechazar = "reject";

        /// <summary>
        /// Gestionar umbrales configurables del módulo.
        /// </summary>
        public const string GestionarUmbrales = "managethresholds";

        /// <summary>
        /// Devuelve todas las acciones del módulo de autorizaciones.
        /// </summary>
        public static string[] Todas => new[] { Ver, Aprobar, Rechazar, GestionarUmbrales };
    }
}
