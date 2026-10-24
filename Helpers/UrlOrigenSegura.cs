namespace TheBuryProject.Helpers;

/// <summary>
/// COM-S-003: la URL de origen de un ticket es texto libre persistido y se muestra como enlace. Solo se acepta una ruta
/// relativa a la app ("/Venta/Details/5?x=1") o una URL absoluta http/https; cualquier otro esquema (javascript:, data:,
/// vbscript:, file:) o ruta protocolo-relativa ("//host", "/ + barra invertida + host") se descarta, tanto al guardar como al renderizar.
/// </summary>
public static class UrlOrigenSegura
{
    public static bool EsSegura(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var valor = url.Trim();
        if (valor.Any(char.IsControl))
            return false;

        if (valor[0] == '/')
            return valor.Length == 1 || (valor[1] != '/' && valor[1] != (char)92);

        return Uri.TryCreate(valor, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>La URL tal cual si es segura; si no, null (se descarta sin rechazar el ticket).</summary>
    public static string? Normalizar(string? url) => EsSegura(url) ? url!.Trim() : null;
}
