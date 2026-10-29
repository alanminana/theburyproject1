using System.Linq.Expressions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services;

/// <summary>
/// Única definición de qué es "el ingreso de caja de una venta". <c>MovimientoCaja.ReferenciaId</c> es polimórfico
/// (venta, cuota, crédito, devolución, orden ML…): un mismo número puede ser el id de una venta y el de una cuota, por
/// lo que nunca alcanza por sí solo. La facturación (¿ya se asentó el ingreso?) y la cancelación (¿qué ingreso se
/// revierte?) deben usar este criterio para no divergir.
/// </summary>
internal static class IngresoVentaCaja
{
    /// <summary>Conceptos con los que el ERP asienta el cobro inmediato de una venta (uno por medio de pago).</summary>
    internal static readonly ConceptoMovimientoCaja[] Conceptos =
    {
        ConceptoMovimientoCaja.VentaEfectivo,
        ConceptoMovimientoCaja.VentaTarjeta,
        ConceptoMovimientoCaja.VentaCheque,
        ConceptoMovimientoCaja.VentaTransferencia,
        ConceptoMovimientoCaja.VentaMercadoPago,
        ConceptoMovimientoCaja.VentaNotaCredito
    };

    /// <summary>Ingreso vinculado directamente a la venta (<c>VentaId</c>) con concepto de cobro de venta.</summary>
    internal static Expression<Func<MovimientoCaja, bool>> Directo(int ventaId) =>
        m => m.VentaId == ventaId
          && m.Tipo == TipoMovimientoCaja.Ingreso
          && Conceptos.Contains(m.Concepto)
          && !m.IsDeleted;

    /// <summary>
    /// Ingreso histórico sin <c>VentaId</c>: además del concepto de venta, <c>ReferenciaId</c> es el id de la venta Y la
    /// <c>Referencia</c> textual es el número de ESTA venta (así lo graba <c>RegistrarMovimientoVentaAsync</c>).
    /// </summary>
    internal static Expression<Func<MovimientoCaja, bool>> HistoricoSinVenta(int ventaId, string ventaNumero) =>
        m => m.VentaId == null
          && m.ReferenciaId == ventaId
          && m.Referencia == ventaNumero
          && m.Tipo == TipoMovimientoCaja.Ingreso
          && Conceptos.Contains(m.Concepto)
          && !m.IsDeleted;
}
