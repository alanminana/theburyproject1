using System.Text.RegularExpressions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

/// <summary>Identidad de una venta sin dar de alta ni vincular un cliente, para cualquier medio de
/// pago salvo Crédito personal (que exige un cliente real para evaluar riesgo/BCRA y firmar el
/// contrato).</summary>
public static class VentaContactoLibre
{
    public static string? NormalizarDni(string? dni) =>
        string.IsNullOrWhiteSpace(dni) ? null : dni.Trim().Replace(".", "").Replace(" ", "");

    public static string? Validar(bool medioPermiteContactoLibre, string? nombre, string? dni, string? telefono)
    {
        if (!medioPermiteContactoLibre)
            return "Sin cliente registrado no se puede vender con Crédito personal. Seleccioná un cliente para ese medio de pago.";
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(telefono) || telefono.Trim().Length > 30 || !telefono.Any(char.IsDigit) ||
            !Regex.IsMatch(NormalizarDni(dni) ?? "", @"\A[0-9]{7,8}\z"))
            return "Para vender sin registrar cliente completá nombre, DNI válido de 7 u 8 dígitos y teléfono.";
        return null;
    }

    public static void ValidarVenta(Venta venta)
    {
        if (venta.ClienteId.HasValue) return;
        var medioPermiteContactoLibre = venta.TipoPago != TipoPago.CreditoPersonal && !venta.CreditoId.HasValue &&
            !venta.Detalles.Any(d => !d.IsDeleted && d.TipoPago.HasValue && d.TipoPago == TipoPago.CreditoPersonal);
        var error = Validar(medioPermiteContactoLibre,
            venta.NombreClienteLibre, venta.DniClienteLibre, venta.TelefonoClienteLibre);
        if (error != null) throw new InvalidOperationException(error);
    }
}
