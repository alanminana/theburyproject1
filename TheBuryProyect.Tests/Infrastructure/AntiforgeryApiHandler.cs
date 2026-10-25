using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Infrastructure;

/// <summary>
/// Hace en los tests lo que csrf-fetch.js hace en el navegador (COM-S-010): agrega el header RequestVerificationToken a
/// los pedidos mutantes hacia /api/ y Cliente/ActualizarBcra. Sin esto, cada test de autorización/negocio que llama a
/// una API debería obtener el token a mano y el objeto de la prueba se perdería. El token sale de una página real, por
/// el mismo cliente (misma cookie y mismo usuario). Un test que quiera probar la EXIGENCIA del token envía el header
/// <see cref="SinTokenHeader"/> (se retira antes de salir) o un RequestVerificationToken propio.
/// </summary>
public sealed class AntiforgeryApiHandler : DelegatingHandler
{
    public const string SinTokenHeader = "X-Test-Sin-Antiforgery";
    private const string Header = "RequestVerificationToken";
    private static readonly Regex Meta = new("name=\"csrf-token\"[^>]*content=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex Input = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    private string? _token;

    /// <summary>El cliente que usa este handler; se asigna tras crearlo para pedir páginas por el mismo pipeline.</summary>
    public HttpClient? Cliente { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Contains(SinTokenHeader))
        {
            request.Headers.Remove(SinTokenHeader);
            return await base.SendAsync(request, ct);
        }

        if (EsMutante(request.Method) && ApuntaAApi(request.RequestUri) && !request.Headers.Contains(Header) && Cliente != null)
        {
            var token = await ObtenerTokenAsync(ct);
            if (token != null)
                request.Headers.TryAddWithoutValidation(Header, token);
        }

        return await base.SendAsync(request, ct);
    }

    private static bool EsMutante(HttpMethod m) =>
        m != HttpMethod.Get && m != HttpMethod.Head && m != HttpMethod.Options && m != HttpMethod.Trace;

    private static bool ApuntaAApi(Uri? uri)
    {
        var path = uri?.AbsolutePath ?? string.Empty;
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/Cliente/ActualizarBcra", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ObtenerTokenAsync(CancellationToken ct)
    {
        if (_token != null) return _token;

        foreach (var url in new[] { "/Identity/Account/Login", "/Identity/Account/AccessDenied" })
        {
            using var respuesta = await Cliente!.GetAsync(url, ct);
            var html = await respuesta.Content.ReadAsStringAsync(ct);
            var m = Meta.Match(html);
            if (!m.Success) m = Input.Match(html);
            if (m.Success)
                return _token = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        }

        return null;
    }
}

/// <summary>
/// Adapta un HttpClient (con su cookie container y redirecciones) como handler terminal de otro cliente. HttpClient no
/// admite reenviar el mismo HttpRequestMessage, por eso se envía una copia (con el contenido en memoria).
/// </summary>
internal sealed class ClienteComoHandler(HttpClient cliente) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var copia = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var h in request.Headers)
            copia.Headers.TryAddWithoutValidation(h.Key, h.Value);

        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            var contenido = new ByteArrayContent(bytes);
            foreach (var h in request.Content.Headers)
                contenido.Headers.TryAddWithoutValidation(h.Key, h.Value);
            copia.Content = contenido;
        }

        return await cliente.SendAsync(copia, ct);
    }
}
