using System.Globalization;

namespace TheBuryProject.ViewModels.PagoCuota;

/// <summary>
/// Texto operativo del ajuste del medio de pago al cobrar cuotas. Solo presentación: el importe
/// y el porcentaje los resuelve el servidor (CreditoService); esto no recalcula nada.
/// </summary>
public static class PagoCuotaAjusteTexto
{
    public static string Etiqueta(decimal porcentaje, decimal importe)
    {
        var esDescuento = porcentaje < 0m || (porcentaje == 0m && importe < 0m);
        var nombre = esDescuento ? "Descuento por medio de pago" : "Recargo por medio de pago";
        if (porcentaje == 0m)
            return nombre;

        var pct = Math.Abs(porcentaje).ToString("0.##", new CultureInfo("es-AR"));
        return $"{nombre} ({pct} %)";
    }
}
