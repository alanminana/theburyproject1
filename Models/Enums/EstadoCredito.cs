namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estados posibles de un crdito
    /// </summary>
    public enum EstadoCredito
    {
        /// <summary>
        /// Crdito solicitado, pendiente de evaluacin
        /// </summary>
        Solicitado = 0,

        /// <summary>
        /// Crdito aprobado, pendiente de desembolso
        /// </summary>
        Aprobado = 1,

        /// <summary>
        /// Crdito activo con cuotas en pago
        /// </summary>
        Activo = 2,

        /// <summary>
        /// Crdito finalizado, todas las cuotas pagadas
        /// </summary>
        Finalizado = 3,

        /// <summary>
        /// Crdito rechazado
        /// </summary>
        Rechazado = 4,

        /// <summary>
        /// Crdito cancelado
        /// </summary>
        Cancelado = 5,

        /// <summary>
        /// Crédito creado desde venta y pendiente de configuración de plan
        /// </summary>
        PendienteConfiguracion = 6,

        /// <summary>
        /// Crédito con plan configurado, listo para confirmar venta
        /// </summary>
        Configurado = 7,

        /// <summary>
        /// Crédito generado (cuotas creadas) al confirmar la venta
        /// </summary>
        Generado = 8
    }
}