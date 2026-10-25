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
        // La serializacion lleva longitudes por delante (sin separadores): ningun contenido (saltos de linea, separadores,
        // "=") puede hacerse pasar por otra estructura de campos.
        var campos = form
            .Where(c => !Excluidos.Contains(c.Key))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        foreach (var campo in campos)
        {
            sb.Append(campo.Key.Length).Append(':').Append(campo.Key).Append(campo.Value.Count).Append('#');
            foreach (var valor in campo.Value)
            {
                var v = valor ?? string.Empty;
                sb.Append(v.Length).Append(':').Append(v);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
