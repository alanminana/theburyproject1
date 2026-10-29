using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Canje de una <see cref="NotaCredito"/> como medio de pago de una venta (H4-E2). Una fila por venta (índice único):
/// es a la vez la traza de auditoría (quién, cuándo, cuánto) y el candado de idempotencia del canje y de su reversión.
/// El saldo de la nota (<c>MontoUtilizado</c>) se mueve en la misma transacción que esta fila.
/// </summary>
public class NotaCreditoUso : AuditableEntity
{
    public int NotaCreditoId { get; set; }

    public int VentaId { get; set; }

    /// <summary>Importe consumido de la nota (= total a cobrar de la venta). Si la nota vale más, el remanente queda en ella.</summary>
    public decimal Monto { get; set; }

    public DateTime FechaUso { get; set; } = DateTime.UtcNow;

    public string? Usuario { get; set; }

    /// <summary>Se completa al revertir el canje por cancelación de la venta. Un canje revertido no vuelve a revertirse.</summary>
    public DateTime? FechaReversion { get; set; }

    public string? MotivoReversion { get; set; }

    public virtual NotaCredito NotaCredito { get; set; } = null!;

    public virtual Venta Venta { get; set; } = null!;
}
