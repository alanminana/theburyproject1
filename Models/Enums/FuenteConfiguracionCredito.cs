namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Define el origen de los valores de configuración para un crédito personal
    /// </summary>
    public enum FuenteConfiguracionCredito
    {
        /// <summary>
        /// Usa los valores globales por defecto del sistema (ConfiguracionPago)
        /// </summary>
        Global = 0,

        /// <summary>
        /// Usa los valores específicos configurados en el perfil del cliente
        /// </summary>
        PorCliente = 1,

        /// <summary>
        /// Usa valores ingresados manualmente por el operador para esta venta específica
        /// </summary>
        Manual = 2,

        /// <summary>
        /// (Futuro) Usa valores de un plan/perfil predefinido (Conservador/Estándar/Riesgoso)
        /// </summary>
        PorPlan = 3
    }
}
