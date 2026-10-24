using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

/// <summary>
/// Conceptos que un usuario puede cargar a mano en Caja/RegistrarMovimiento, por tipo. Es el mismo catálogo que ofrece
/// el formulario (wwwroot/js/caja-registrar-movimiento.js); el servidor lo exige porque el select es solo una
/// comodidad del navegador. Los conceptos que genera el sistema (transferencia, Mercado Pago, liquidación de Mercado
/// Libre, reversión de venta) y las combinaciones incoherentes (p. ej. un ingreso "Gasto operativo") no se aceptan.
/// </summary>
public static class ConceptosMovimientoCajaManual
{
    private static readonly ConceptoMovimientoCaja[] Ingreso =
    {
        ConceptoMovimientoCaja.VentaEfectivo,
        ConceptoMovimientoCaja.VentaTarjeta,
        ConceptoMovimientoCaja.VentaCheque,
        ConceptoMovimientoCaja.CobroCuota,
        ConceptoMovimientoCaja.CancelacionCredito,
        ConceptoMovimientoCaja.AnticipoCredito,
    };

    private static readonly ConceptoMovimientoCaja[] Egreso =
    {
        ConceptoMovimientoCaja.GastoOperativo,
        ConceptoMovimientoCaja.ExtraccionEfectivo,
        ConceptoMovimientoCaja.DepositoEfectivo,
        ConceptoMovimientoCaja.DevolucionCliente,
        ConceptoMovimientoCaja.AjusteCaja,
        ConceptoMovimientoCaja.Otro,
    };

    public static bool EsPermitido(TipoMovimientoCaja tipo, ConceptoMovimientoCaja concepto) => tipo switch
    {
        TipoMovimientoCaja.Ingreso => Array.IndexOf(Ingreso, concepto) >= 0,
        TipoMovimientoCaja.Egreso => Array.IndexOf(Egreso, concepto) >= 0,
        _ => false,
    };
}
