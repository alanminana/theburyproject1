using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using TheBuryProject.Security;

namespace TheBuryProject.Tests.Integration;

/// <summary>D-05: la CSP es opt-in (Off por defecto); en ReportOnly emite cabecera con nonce y el login lo propaga a sus scripts.</summary>
[Collection("HttpIntegration")]
public class CspHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CspHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient Cliente(string? modo) =>
        _factory.WithWebHostBuilder(b => { if (modo != null) b.UseSetting("Csp:Mode", modo); })
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task PorDefecto_NoEmiteCabeceraCsp()
    {
        var r = await Cliente(null).GetAsync("/Identity/Account/Login");
        Assert.False(r.Headers.Contains("Content-Security-Policy"));
        Assert.False(r.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task ReportOnly_EmiteCabeceraSinUnsafeInlineNiEval_YNonceEnScriptsInline()
    {
        var r = await Cliente("ReportOnly").GetAsync("/Identity/Account/Login");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("Content-Security-Policy"));
        var csp = r.Headers.GetValues("Content-Security-Policy-Report-Only").Single();
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("object-src 'none'", csp);
        var nonce = System.Text.RegularExpressions.Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);
        var html = System.Net.WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()); // el nonce base64 puede traer + o / (codificados en el atributo)
        Assert.Contains($"nonce=\"{nonce}\"", html);
    }

    [Fact]
    public async Task Enforce_UsaCabeceraDefinitiva()
    {
        var r = await Cliente("Enforce").GetAsync("/Identity/Account/Login");
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task ReporteCsp_AceptaPostAnonimoYResponde204()
    {
        var cliente = Cliente("ReportOnly");
        var r = await cliente.PostAsync("/csp-report",
            new StringContent("{\"csp-report\":{\"violated-directive\":\"script-src-attr\"}}", Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var basura = await cliente.PostAsync("/csp-report", new StringContent("no es json", Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.BadRequest, basura.StatusCode);
        var enorme = await cliente.PostAsync("/csp-report", new StringContent(new string('x', 20_000), Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, enorme.StatusCode);
    }

    [Fact]
    public async Task ReporteCsp_ConCspApagada_Devuelve404()
    {
        var r = await Cliente(null).PostAsync("/csp-report",
            new StringContent("{\"csp-report\":{}}", Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ---- F2.2 (2026-11-11): configuración que no puede debilitar la política en silencio, nonce por solicitud, errores/parciales y reportes ----

    [Theory]
    [InlineData("Enforced")]
    [InlineData("5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ReportOnly,Enforce")]
    [InlineData("true")]
    public void ModoInvalido_ImpideElArranque_NoDegradaEnSilencio(string valor)
    {
        // Sin validación, "5" caía en Report-Only y "" en Off sin avisar. Con ValidateOnStart el host no arranca.
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Csp:Mode", valor));
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("enforce")]
    [InlineData("ENFORCE")]
    [InlineData("Enforce")]
    public async Task Enforce_NoDependeDeMayusculas(string valor)
    {
        var r = await Cliente(valor).GetAsync("/Identity/Account/Login");
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
        Assert.False(r.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task Nonce_EsDistintoEnCadaSolicitud_YCoincideConElDeLosScripts()
    {
        var c = Cliente("Enforce");
        var nonces = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var r = await c.GetAsync("/Identity/Account/Login");
            var csp = r.Headers.GetValues("Content-Security-Policy").Single();
            var nonce = System.Text.RegularExpressions.Regex.Match(csp, "script-src 'self' 'nonce-([^']+)'").Groups[1].Value;
            Assert.True(nonce.Length >= 16);
            Assert.True(nonces.Add(nonce), "el nonce se repitió entre solicitudes");
            // el nonce de style-src es el mismo de la solicitud (no uno global)
            Assert.Contains($"style-src 'self' 'nonce-{nonce}'", csp);
        }
    }

    [Theory]
    [InlineData("/ruta-que-no-existe", "text/html")]
    [InlineData("/ruta-que-no-existe", "application/json")]
    [InlineData("/api/ruta-que-no-existe", "application/json")]
    [InlineData("/Venta/Index", "text/html")]          // 302 a login (anónimo)
    [InlineData("/Home/Estado?code=404", "text/html")]
    public async Task Enforce_RespuestasDeErrorYRedireccionTambienLlevanLaCabecera(string ruta, string accept)
    {
        var c = Cliente("Enforce");
        using var req = new HttpRequestMessage(HttpMethod.Get, ruta);
        req.Headers.Accept.ParseAdd(accept);
        var r = await c.SendAsync(req);
        Assert.True(r.Headers.Contains("Content-Security-Policy"), $"{ruta} ({(int)r.StatusCode}) sin cabecera CSP");
    }

    [Fact]
    public async Task Enforce_ElEndpointDeReporteYLosPostRechazadosTambienLlevanLaCabecera()
    {
        var c = Cliente("Enforce");
        var r = await c.PostAsync("/csp-report", new StringContent("no es json", Encoding.UTF8, "application/csp-report"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task ReporteCsp_SeLoguea_YElRateLimitCorta_DespuesDelLimite()
    {
        var logs = new CapturaLogs();
        var c = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Csp:Mode", "Enforce");
            b.ConfigureServices(sv => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sv, logs));
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cuerpo = "{\"csp-report\":{\"document-uri\":\"http://x/\",\"violated-directive\":\"script-src-elem\",\"blocked-uri\":\"inline\"}}";
        HttpStatusCode? primero = null, ultimo = null;
        var aceptados = 0;
        for (var i = 0; i < 40; i++)
        {
            var r = await c.PostAsync("/csp-report", new StringContent(cuerpo, Encoding.UTF8, "application/csp-report"));
            primero ??= r.StatusCode;
            ultimo = r.StatusCode;
            if (r.StatusCode == HttpStatusCode.NoContent) aceptados++;
        }
        Assert.Equal(HttpStatusCode.NoContent, primero);
        Assert.Equal(HttpStatusCode.TooManyRequests, ultimo);
        Assert.Equal(30, aceptados); // PermitLimit por IP y minuto
        var avisos = logs.Mensajes.Where(m => m.Contains("CSP violation") && m.Contains("violated-directive=script-src-elem")).ToList();
        Assert.Equal(30, avisos.Count);
    }

    private sealed class CapturaLogs : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Mensajes { get; } = new();
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoria) => new L(categoria, Mensajes);
        public void Dispose() { }
        private sealed class L(string categoria, System.Collections.Concurrent.ConcurrentBag<string> m) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel nivel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel nivel, Microsoft.Extensions.Logging.EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f)
            {
                if (categoria == "CspReport" && nivel == Microsoft.Extensions.Logging.LogLevel.Warning) m.Add(f(state, ex));
            }
        }
    }

    [Fact]
    public async Task Politica_FrameSrcPermiteBlobPorLaVistaPreviaPdf_SinAmpliarAOrigenesExternos()
    {
        // Regresion 2026-11-11: DocumentosConfig muestra el PDF en un iframe con URL blob:; frame-src 'self' lo bloqueaba bajo Enforce.
        var r = await Cliente("Enforce").GetAsync("/Identity/Account/Login");
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        var frameSrc = csp.Split(';', StringSplitOptions.TrimEntries).Single(d => d.StartsWith("frame-src "));
        Assert.Equal("frame-src 'self' blob:", frameSrc);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp.Split(';', StringSplitOptions.TrimEntries).Single(d => d.StartsWith("script-src ")));
    }
}
