using System;
using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Tipos de pago aceptados en ventas
    /// </summary>
    public enum TipoPago
    {
        [Display(Name = "Efectivo")]
        Efectivo = 0,

        [Display(Name = "Transferencia")]
        Transferencia = 1,

        [Display(Name = "Tarjeta Débito")]
        TarjetaDebito = 2,

        [Display(Name = "Tarjeta Crédito")]
        TarjetaCredito = 3,

        [Display(Name = "Cheque")]
        Cheque = 4,

        [Display(Name = "Crédito Personal")]
        CreditoPersonal = 5,

        /// <summary>
        /// DEPRECADO: Usar CreditoPersonal. Alias mantenido por compatibilidad con datos existentes.
        /// </summary>
        [Obsolete("Usar CreditoPersonal en su lugar. Este valor tiene un typo y será removido en futuras versiones.")]
        CreditoPersonall = 5,

        [Display(Name = "Mercado Pago")]
        MercadoPago = 6,

        [Display(Name = "Cuenta Corriente")]
        CuentaCorriente = 7,

        [Display(Name = "Tarjeta")]
        Tarjeta = 8
    }
}
