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
    public const string PoliticaRateLimit = "csp-report";

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
        // blob: solo para la vista previa PDF de DocumentosConfig (documentos-preview.js crea un blob: same-origin y lo muestra en un iframe);
        // un blob: solo lo puede crear JavaScript del propio origen (script-src: 'self' + nonce), no amplía a terceros. Medido con
        // Enforce en QA (2026-11-11): sin esto el iframe quedaba bloqueado (violación frame-src | blob).
        sb.Append("frame-src 'self' blob:; ");
        sb.Append("object-src 'none'; ");
        sb.Append("base-uri 'self'; ");
        sb.Append("form-action 'self'; ");
        sb.Append("frame-ancestors 'self'; ");
        sb.Append($"report-uri {RutaReporte}");
        return sb.ToString();
    }
}

public static class CspOptionsRegistration
{
    /// <summary>
    /// Registra <see cref="CspOptions"/> con validación al arrancar: un <c>Csp:Mode</c> inválido (typo, vacío, número fuera del enum,
    /// lista "A,B") impide el inicio de la aplicación en lugar de degradarse en silencio (p. ej. a Report-Only o a Off).
    /// El valor ausente sigue siendo Off (defecto histórico, opt-in).
    /// </summary>
    public static IServiceCollection AddCspOptions(this IServiceCollection services, IConfiguration configuration)
    {
        var seccion = configuration.GetSection(CspOptions.Seccion);
        services.AddOptions<CspOptions>()
            .Bind(seccion)
            .Validate(o => Enum.IsDefined(o.Mode), "Csp:Mode inválido: use Off, ReportOnly o Enforce.")
            .Validate(_ => ModoCrudoValido(seccion["Mode"]), "Csp:Mode presente pero vacío o inválido: use Off, ReportOnly o Enforce (o quite la clave).")
            .ValidateOnStart();
        return services;
    }

    // El binder ignora los strings vacíos (dejaría Off en silencio) y Enum.Parse acepta números y listas "A,B": se exige un nombre exacto.
    private static bool ModoCrudoValido(string? crudo) =>
        crudo is null || Enum.GetNames<CspMode>().Contains(crudo.Trim(), StringComparer.OrdinalIgnoreCase);
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
    private static readonly string[] CamposPermitidos =
        ["document-uri", "blocked-uri", "effective-directive", "violated-directive", "source-file", "line-number", "disposition"];

    public static void MapCspReport(this IEndpointRouteBuilder app) =>
        app.MapPost(CspPolicy.RutaReporte, async (HttpContext ctx, ILoggerFactory lf, Microsoft.Extensions.Options.IOptionsMonitor<CspOptions> opciones) =>
        {
            // Sin CSP activa el endpoint no existe de cara al exterior.
            if (opciones.CurrentValue.Mode == CspMode.Off) return Results.NotFound();
            if (ctx.Request.ContentLength is null or > 8_192) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var lector = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            var buffer = new char[8_193];
            var n = await lector.ReadBlockAsync(buffer, 0, buffer.Length);
            if (n > 8_192) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(new string(buffer, 0, n));
                if (!doc.RootElement.TryGetProperty("csp-report", out var rep) || rep.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return Results.BadRequest();
                var partes = new List<string>();
                foreach (var campo in CamposPermitidos)
                    if (rep.TryGetProperty(campo, out var v))
                        partes.Add($"{campo}={Sanear(v.ToString())}");
                lf.CreateLogger("CspReport").LogWarning("CSP violation: {Reporte}", string.Join(" | ", partes));
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest();
            }
            return Results.NoContent();
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(CspPolicy.PoliticaRateLimit);

    private static string Sanear(string valor)
    {
        var sb = new StringBuilder(Math.Min(valor.Length, 200));
        foreach (var c in valor.Take(200)) sb.Append(char.IsControl(c) ? '?' : c);
        return sb.ToString();
    }
}
