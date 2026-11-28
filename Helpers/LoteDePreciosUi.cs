using System.Globalization;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

/// <summary>Presentación de los cambios de precios por lotes. Solo texto y estilo: las reglas viven en PrecioService.</summary>
public static class LoteDePreciosUi
{
    public static (string Clase, string Texto) Estado(EstadoBatch estado) => estado switch
    {
        EstadoBatch.Simulado => ("inv-pill inv-pill--warning", "Simulado"),
        EstadoBatch.Aprobado => ("inv-pill inv-pill--accent", "Aprobado"),
        EstadoBatch.Aplicado => ("inv-pill inv-pill--ok", "Aplicado"),
        EstadoBatch.Revertido => ("inv-pill inv-pill--muted", "Revertido"),
        EstadoBatch.Rechazado => ("inv-pill inv-pill--danger", "Rechazado"),
        EstadoBatch.Cancelado => ("inv-pill inv-pill--muted", "Cancelado"),
        _ => ("inv-pill", estado.ToString())
    };

    /// <summary>«+10,00 %» o «−$ 500,00».</summary>
    public static string Cambio(TipoCambio tipo, TipoAplicacion aplicacion, decimal valor)
    {
        var signo = aplicacion == TipoAplicacion.Aumento ? "+" : "−";
        var numero = Math.Abs(valor).ToString("N2", CultureInfo.CurrentCulture);
        return tipo == TipoCambio.ValorAbsoluto ? $"{signo}$ {numero}" : $"{signo}{numero} %";
    }

    public static string Tipo(TipoCambio tipo) => tipo switch
    {
        TipoCambio.PorcentajeSobrePrecioActual => "Porcentaje sobre el precio actual",
        TipoCambio.PorcentajeSobreCosto => "Porcentaje sobre el costo",
        TipoCambio.ValorAbsoluto => "Monto fijo",
        TipoCambio.AsignacionDirecta => "Precio asignado",
        _ => tipo.ToString()
    };

    public static string Precios(int cantidad) => cantidad == 1 ? "1 precio" : $"{cantidad} precios";
}
