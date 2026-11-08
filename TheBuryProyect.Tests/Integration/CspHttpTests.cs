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
}
