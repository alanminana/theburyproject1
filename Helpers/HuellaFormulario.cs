using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TheBuryProject.Helpers;

/// <summary>
/// FIN-S-018. Huella del contenido de un formulario enviado, completa por construcción: incluye TODOS los campos que
/// llegaron (carrito, envío, tarjeta, cheque, crédito, excepción documental, ...) salvo los que cambian legítimamente
/// entre reintentos del mismo formulario o no son decisiones del operador. Así un campo nuevo o desconocido también
/// altera la huella, y reutilizar la clave con otro contenido se detecta sin mantener una lista de campos a mano.
/// </summary>
public static class HuellaFormulario
{
    // Token antiforgery, la propia clave, la versión de concurrencia y la fecha (un reintento que cruza la medianoche
    // no debe rechazarse; la fecha de la venta la fija el servidor dentro de un rango).
    private static readonly HashSet<string> Excluidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "__RequestVerificationToken", "IdempotencyKey", "RowVersion", "FechaVenta"
    };

    public static string De(IFormCollection form)
    {
        // Se ordenan las CLAVES, no los valores: el binder toma el primer valor de un campo escalar repetido, asi que
        // "Cantidad=1&Cantidad=3" y "Cantidad=3&Cantidad=1" son ventas distintas. Los valores de una clave conservan su orden.
        var lineas = new List<string>();
        foreach (var campo in form)
        {
            if (Excluidos.Contains(campo.Key))
            {
                continue;
            }

            lineas.Add(campo.Key + "=" + string.Join('\u001f', campo.Value.Select(v => v ?? string.Empty)));
        }

        lineas.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lineas))));
    }
}
