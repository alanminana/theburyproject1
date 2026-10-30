using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services;

/// <summary>
/// Un cheque (banco + número) no puede cobrarse dos veces. Los cheques de venta viven en <c>DatosCheque</c>; los de cuota no tienen
/// entidad propia y viajan en <c>MovimientoCaja.MedioPagoDetalle</c> con un formato fijo (<see cref="Detalle"/>), que es lo que se
/// busca acá para cubrir el duplicado entre ventas y cuotas sin cambiar el modelo.
/// </summary>
internal static class ChequeDuplicados
{
    private const string Marca = " · vence";

    /// <summary>Texto del cheque en el ingreso de caja de una cuota: «Cheque · Banco N° 123 · vence dd/MM/yyyy».</summary>
    public static string Detalle(string banco, string numero, DateTime vencimiento) =>
        $"{Prefijo(banco, numero)} {vencimiento:dd/MM/yyyy}";

    public static string Prefijo(string banco, string numero) => $"Cheque · {banco.Trim()} N° {numero.Trim()}{Marca}";

    /// <summary>True si el cheque ya cobró una cuota (ingreso vigente: no anulado ni revertido).</summary>
    public static Task<bool> ExisteEnCuotasAsync(AppDbContext context, string banco, string numero)
    {
        var prefijo = Prefijo(banco, numero);
        return context.MovimientosCaja.AsNoTracking().AnyAsync(m =>
            !m.IsDeleted
            && m.Concepto == ConceptoMovimientoCaja.CobroCuota
            && m.TipoPago == TipoPago.Cheque
            && m.EstadoAcreditacion != EstadoAcreditacionMovimientoCaja.Anulado
            && m.EstadoAcreditacion != EstadoAcreditacionMovimientoCaja.Revertido
            && m.MedioPagoDetalle != null
            && m.MedioPagoDetalle.StartsWith(prefijo));
    }
}
