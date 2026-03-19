namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Tipo de notificación
    /// </summary>
    public enum TipoNotificacion
    {
        // Stock y Productos
        StockBajo = 0,
        StockAgotado = 1,

        // Créditos y Cobranza
        CuotaProximaVencer = 10,
        CuotaVencida = 11,
        CreditoAprobado = 12,
        CreditoRechazado = 13,
        PagoRecibido = 14,

        // Autorizaciones
        AutorizacionPendiente = 20,
        AutorizacionAprobada = 21,
        AutorizacionRechazada = 22,

        // Ventas y Devoluciones
        VentaCompletada = 30,
        DevolucionCreada = 31,
        DevolucionAprobada = 32,
        NotaCreditoGenerada = 33,

        // RMA y Garantías
        RMAPendiente = 40,
        RMAAprobado = 41,
        RMARechazado = 42,
        GarantiaProximaVencer = 43,

        // Cajas
        CajaAbierta = 50,
        CajaCerrada = 51,
        CierreConDiferencia = 52,


        // Cheques
        ChequeProximoVencer = 60,
        ChequeVencido = 61,

        // Sistema
        SistemaError = 90,
        SistemaMantenimiento = 91,

        Otro = 99
    }
}