using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TheBuryProject.Security;

/// <summary>D-05: modo de la Content-Security-Policy. Off = sin cabecera (estado histórico).</summary>
public enum CspMode { Off, ReportOnly, Enforce }

public sealed class CspOptions
{
    public const string Seccion = "Csp";
    public CspMode Mode { get; set; } = CspMode.Off;
    /// <summary>Si es true, se incluye script-src-attr 'none' (inventario de onclick= y similares). Con false se permite 'unsafe-inline' solo en atributos.</summary>
    public bool EstrictoAtributosScript { get; set; } = true;
}

public static class CspPolicy
{
    public const string NonceItemKey = "csp-nonce";
    public const string RutaReporte = "/csp-report";

    public static string NuevoNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    /// <summary>
    /// Política candidata. Scripts y estilos de elemento: solo 'self' + nonce (sin unsafe-inline/unsafe-eval).
    /// style-src-attr 'unsafe-inline' es una excepción acotada: el estilo en atributo no ejecuta código y
    /// el sistema usa más de mil style="" (Razor + JS). SignalR (WebSocket) cae en connect-src 'self'.
    /// </summary>
    public static string Construir(string nonce, CspOptions opciones)
    {
        var sb = new StringBuilder();
        sb.Append("default-src 'self'; ");
        sb.Append($"script-src 'self' 'nonce-{nonce}'; ");
        sb.Append(opciones.EstrictoAtributosScript ? "script-src-attr 'none'; " : "script-src-attr 'unsafe-inline'; ");
        sb.Append($"style-src 'self' 'nonce-{nonce}' https://fonts.googleapis.com; ");
        sb.Append("style-src-attr 'unsafe-inline'; ");
        sb.Append("font-src 'self' https://fonts.gstatic.com data:; ");
        sb.Append("img-src 'self' data: blob:; ");
        sb.Append("connect-src 'self'; ");
        sb.Append("frame-src 'self'; ");
        sb.Append("object-src 'none'; ");
        sb.Append("base-uri 'self'; ");
        sb.Append("form-action 'self'; ");
        sb.Append("frame-ancestors 'self'; ");
        sb.Append($"report-uri {RutaReporte}");
        return sb.ToString();
    }
}

public sealed class CspMiddleware(RequestDelegate next, Microsoft.Extensions.Options.IOptionsMonitor<CspOptions> opciones)
{
    public Task InvokeAsync(HttpContext context)
    {
        var o = opciones.CurrentValue;
        if (o.Mode == CspMode.Off) return next(context);

        var nonce = CspPolicy.NuevoNonce();
        context.Items[CspPolicy.NonceItemKey] = nonce;
        var cabecera = o.Mode == CspMode.Enforce ? "Content-Security-Policy" : "Content-Security-Policy-Report-Only";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[cabecera] = CspPolicy.Construir(nonce, o);
            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>Agrega el nonce de la request a &lt;script&gt; y &lt;style&gt; en línea (sin src) sin tocar cada vista.</summary>
[HtmlTargetElement("script")]
[HtmlTargetElement("style")]
public sealed class CspNonceTagHelper(IHttpContextAccessor accessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (accessor.HttpContext?.Items[CspPolicy.NonceItemKey] is not string nonce) return;
        if (output.Attributes.ContainsName("nonce")) return;
        // Solo inline; los externos se autorizan por 'self'. Excepción: bloques de datos no ejecutables no necesitan nonce.
        if (output.TagName == "script" && output.Attributes.ContainsName("src")) return;
        output.Attributes.SetAttribute("nonce", nonce);
    }
}

public static class CspReportEndpoint
{
    public static void MapCspReport(this IEndpointRouteBuilder app) =>
        app.MapPost(CspPolicy.RutaReporte, async (HttpContext ctx, ILoggerFactory lf) =>
        {
            if (ctx.Request.ContentLength is > 16_384) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var lector = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            var buffer = new char[16_384];
            var n = await lector.ReadBlockAsync(buffer, 0, buffer.Length);
            var texto = new string(buffer, 0, n).Replace('\r', ' ').Replace('\n', ' ');
            lf.CreateLogger("CspReport").LogWarning("CSP violation: {Reporte}", texto);
            return Results.NoContent();
        }).AllowAnonymous().DisableAntiforgery();
}
