using System;

namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Tipos de pago aceptados en ventas
    /// </summary>
    public enum TipoPago
    {
        Efectivo = 0,
        Transferencia = 1,
        TarjetaDebito = 2,
        TarjetaCredito = 3,
        Cheque = 4,
        /// <summary>
        /// Crédito personal del cliente (valor correcto)
        /// </summary>
        CreditoPersonal = 5,
        /// <summary>
        /// DEPRECADO: Usar CreditoPersonal. Alias mantenido por compatibilidad con datos existentes.
        /// </summary>
        [Obsolete("Usar CreditoPersonal en su lugar. Este valor tiene un typo y será removido en futuras versiones.")]
        CreditoPersonall = 5,
        MercadoPago = 6,
        CuentaCorriente = 7,
        Tarjeta = 8
    }
}
