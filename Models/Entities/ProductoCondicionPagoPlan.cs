using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Plan individual de cuotas para una condicion de pago por producto.
/// ProductoCondicionPagoTarjetaId null representa plan del medio en general.
/// AjustePorcentaje negativo = descuento, cero = sin ajuste, positivo = recargo.
/// </summary>
public class ProductoCondicionPagoPlan : AuditableEntity
{
    public int ProductoCondicionPagoId { get; set; }

    public int? ProductoCondicionPagoTarjetaId { get; set; }

    public int CantidadCuotas { get; set; }

    public bool Activo { get; set; } = true;

    public decimal AjustePorcentaje { get; set; }

    public TipoAjustePagoPlan TipoAjuste { get; set; } = TipoAjustePagoPlan.Porcentaje;

    [StringLength(500)]
    public string? Observaciones { get; set; }

    public virtual ProductoCondicionPago ProductoCondicionPago { get; set; } = null!;

    public virtual ProductoCondicionPagoTarjeta? ProductoCondicionPagoTarjeta { get; set; }
}
