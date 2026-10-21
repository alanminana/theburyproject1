using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// COM-S-005: los adjuntos de tickets se guardan bajo wwwroot/uploads/tickets y UseStaticFiles (antes de
/// la autenticación) los entregaba a cualquiera que conociera la URL, sin tickets.view ni comprobar que el
/// adjunto siguiera vigente. El acceso es ahora exclusivamente la descarga autenticada
/// /Ticket/DescargarAdjunto/{id} (tickets.view), por identificador y con la ruta tomada de la DB.
/// </summary>
[Collection("HttpIntegration")]
public class TicketAdjuntosAccesoHttpTests :
    IClassFixture<CustomWebApplicationFactory>, IClassFixture<CookieAuthWebApplicationFactory>, IDisposable
{
    private static readonly byte[] PdfSintetico = "%PDF-1.4\n% COM-S-005 adjunto sintetico\n%%EOF\n"u8.ToArray();
    private static readonly byte[] PngSintetico =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] TxtSintetico = "adjunto de texto sintetico <script>alert(1)</script>"u8.ToArray();
    private static readonly byte[] Secreto = "SECRETO-FUERA-DE-LA-CARPETA-DE-TICKETS"u8.ToArray();

    private readonly CustomWebApplicationFactory _factory;
    private readonly CookieAuthWebApplicationFactory _cookieFactory;
    private readonly List<string> _archivosAEliminar = [];

    public TicketAdjuntosAccesoHttpTests(
        CustomWebApplicationFactory factory, CookieAuthWebApplicationFactory cookieFactory)
    {
        _factory = factory;
        _cookieFactory = cookieFactory;
    }

    public void Dispose()
    {
        foreach (var archivo in _archivosAEliminar)
        {
            try { File.Delete(archivo); } catch (IOException) { /* limpieza best-effort */ }
        }
    }

    // ── Sin sesión ────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchivoExistente_ClienteSinSesion_UrlEstatica_Devuelve404()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var rutaRelativa = (await SubirAsync(admin, "sintetico.pdf", "application/pdf", PdfSintetico)).RutaRelativa;

        using var anonimo = _cookieFactory.CreateClient(new() { AllowAutoRedirect = false });
        var respuesta = await anonimo.GetAsync("/" + rutaRelativa);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain(PdfSintetico, new[] { await respuesta.Content.ReadAsByteArrayAsync() });
    }

    [Theory]
    [InlineData("/UPLOADS/Tickets/{0}")]
    [InlineData("/uploads/tickets//{0}")]
    [InlineData("/uploads/./tickets/{0}")]
    [InlineData("/uploads/documentos-clientes/../tickets/{0}")]
    [InlineData("/uploads/tickets./{0}")]
    [InlineData("/uploads/tickets../{0}")]
    [InlineData("/uploads/tickets%20/{0}")]
    [InlineData("/uploads/tickets.%20./{0}")]
    [InlineData("/uploads/TICKET~1/{0}")]
    public async Task ArchivoExistente_VariantesDeUrlEstatica_NoSeEntregan(string plantilla)
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var rutaRelativa = (await SubirAsync(admin, "variante.pdf", "application/pdf", PdfSintetico)).RutaRelativa;
        var nombre = Path.GetFileName(rutaRelativa);

        using var cliente = _factory.CreateAuthenticatedClient();
        var respuesta = await cliente.GetAsync(string.Format(plantilla, nombre));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Descarga_ClienteSinSesion_NoEntregaElArchivo()
    {
        var adjuntoId = await SembrarAdjuntoAsync(_cookieFactory, PdfSintetico, "sin-sesion.pdf");

        using var anonimo = _cookieFactory.CreateClient(new() { AllowAutoRedirect = false });
        var respuesta = await anonimo.GetAsync($"/Ticket/DescargarAdjunto/{adjuntoId}");

        Assert.True(respuesta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,
            $"Se esperaba redirección a login o 401, se obtuvo {(int)respuesta.StatusCode}.");
        Assert.NotEqual(PdfSintetico.Length, (await respuesta.Content.ReadAsByteArrayAsync()).Length);
    }

    // ── Sin permiso ───────────────────────────────────────────────────────

    [Fact]
    public async Task Descarga_UsuarioAutenticadoSinTicketsView_EsRechazado()
    {
        var adjuntoId = await SembrarAdjuntoAsync(_factory, PdfSintetico, "sin-permiso.pdf");
        using var sinPermisos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var respuesta = await sinPermisos.GetAsync($"/Ticket/DescargarAdjunto/{adjuntoId}");

        Assert.True(respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Se esperaba rechazo, se obtuvo {(int)respuesta.StatusCode}.");
        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
    }

    // ── Usuario autorizado ────────────────────────────────────────────────

    [Theory]
    [InlineData("informe.pdf", "application/pdf", "application/pdf")]
    [InlineData("foto.png", "image/png", "image/png")]
    [InlineData("notas.txt", "text/plain", "text/plain")]
    public async Task SubidaYDescarga_UsuarioAutorizado_EntregaLosMismosBytesComoAttachment(
        string nombre, string mimeSubido, string mimeEsperado)
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var contenido = nombre.EndsWith(".pdf") ? PdfSintetico : nombre.EndsWith(".png") ? PngSintetico : TxtSintetico;
        var (adjuntoId, rutaRelativa, urlDescarga) = await SubirAsync(admin, nombre, mimeSubido, contenido);

        var respuesta = await admin.GetAsync(urlDescarga);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal($"/Ticket/DescargarAdjunto/{adjuntoId}", urlDescarga);
        Assert.Equal(contenido, await respuesta.Content.ReadAsByteArrayAsync());
        Assert.Equal(mimeEsperado, respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", respuesta.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(nombre, respuesta.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString());
        Assert.StartsWith("uploads/tickets/", rutaRelativa);
    }

    [Fact]
    public async Task MimeDeclaradoPorElCliente_NoDeterminaElContentTypeEntregado()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var url = (await SubirAsync(admin, "falso.txt", "text/html", TxtSintetico)).UrlDescarga;

        var respuesta = await admin.GetAsync(url);

        Assert.Equal("text/plain", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", respuesta.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task RespuestasDeApi_NoExponenRutaFisicaDelAdjunto()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var ticketId = await CrearTicketAsync(admin);
        var (adjuntoId, rutaRelativa, _) = await SubirAsync(admin, "api.pdf", "application/pdf", PdfSintetico, ticketId);

        var detalle = await (await admin.GetAsync($"/api/tickets/{ticketId}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(rutaRelativa, detalle);
        Assert.DoesNotContain(Path.GetFileName(rutaRelativa), detalle);
        Assert.DoesNotContain("rutaArchivo", detalle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"id\":{adjuntoId}", detalle);
    }

    [Fact]
    public async Task VistaDetalle_EnlazaALaDescargaAutenticada_YNoAUnaUrlEstatica()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var ticketId = await CrearTicketAsync(admin);
        var (adjuntoId, rutaRelativa, _) = await SubirAsync(admin, "vista.pdf", "application/pdf", PdfSintetico, ticketId);

        var html = await (await admin.GetAsync($"/Ticket/Details/{ticketId}")).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"/Ticket/DescargarAdjunto/{adjuntoId}\"", html);
        Assert.DoesNotContain("uploads/tickets", html);
        Assert.DoesNotContain(Path.GetFileName(rutaRelativa), html);
    }

    // ── 404 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("999999")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("99999999999999999999")]
    [InlineData("1;2")]
    public async Task Descarga_IdentificadorInexistenteOManipulado_Devuelve404(string id)
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();

        var respuesta = await admin.GetAsync($"/Ticket/DescargarAdjunto/{id}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Descarga_TicketEliminado_Devuelve404()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var ticketId = await CrearTicketAsync(admin);
        var url = (await SubirAsync(admin, "ticket-borrado.pdf", "application/pdf", PdfSintetico, ticketId)).UrlDescarga;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);

        await EjecutarAsync(_factory, async ctx =>
        {
            var ticket = await ctx.Tickets.SingleAsync(t => t.Id == ticketId);
            ticket.IsDeleted = true; // el adjunto queda vigente: debe bastar con que el ticket esté eliminado
            await ctx.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Descarga_AdjuntoEliminado_Devuelve404()
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var (adjuntoId, _, url) = await SubirAsync(admin, "adjunto-borrado.pdf", "application/pdf", PdfSintetico);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);

        var baja = await admin.DeleteAsync($"/api/tickets/adjuntos/{adjuntoId}");
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Descarga_AdjuntoVigenteConArchivoFaltante_Devuelve404()
    {
        var adjuntoId = await SembrarAdjuntoAsync(_factory, PdfSintetico, "huerfano.pdf", crearArchivo: false);
        using var admin = _factory.CreateAuthenticatedClient();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/Ticket/DescargarAdjunto/{adjuntoId}")).StatusCode);
    }

    // ── Traversal / rutas manipuladas en la DB ────────────────────────────

    [Theory]
    [InlineData("uploads/tickets/../com-s-005-secreto.txt")]
    [InlineData("uploads/tickets/../../com-s-005-secreto.txt")]
    [InlineData("uploads\\tickets\\..\\com-s-005-secreto.txt")]
    [InlineData("uploads/tickets-otro/com-s-005-secreto.txt")]
    [InlineData("uploads/com-s-005-secreto.txt")]
    [InlineData("/uploads/com-s-005-secreto.txt")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("appsettings.json")]
    [InlineData("uploads/tickets/\0evil.pdf")]
    public async Task Descarga_RutaPersistidaFueraDeLaCarpetaDeTickets_NoSeEntrega(string rutaPersistida)
    {
        await _factory.SeedTestUserAsync();
        var webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var secreto = Path.Combine(webRoot, "uploads", "com-s-005-secreto.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(secreto)!);
        await File.WriteAllBytesAsync(secreto, Secreto);
        _archivosAEliminar.Add(secreto);

        var adjuntoId = await SembrarAdjuntoAsync(_factory, TxtSintetico, "traversal.txt",
            crearArchivo: false, rutaPersistida: rutaPersistida);
        using var admin = _factory.CreateAuthenticatedClient();

        var respuesta = await admin.GetAsync($"/Ticket/DescargarAdjunto/{adjuntoId}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("SECRETO", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Descarga_RutaLegacyConSeparadoresDeWindows_SigueFuncionando()
    {
        var webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var nombre = $"legacy-{Guid.NewGuid()}.pdf";
        var carpeta = Path.Combine(webRoot, "uploads", "tickets");
        Directory.CreateDirectory(carpeta);
        var fisico = Path.Combine(carpeta, nombre);
        await File.WriteAllBytesAsync(fisico, PdfSintetico);
        _archivosAEliminar.Add(fisico);

        var adjuntoId = await SembrarAdjuntoAsync(_factory, PdfSintetico, "legacy.pdf",
            crearArchivo: false, rutaPersistida: $"uploads\\tickets\\{nombre}");
        using var admin = _factory.CreateAuthenticatedClient();

        var respuesta = await admin.GetAsync($"/Ticket/DescargarAdjunto/{adjuntoId}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(PdfSintetico, await respuesta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Descarga_NoAceptaRutasFisicasEnQueryNiRuta()
    {
        var adjuntoId = await SembrarAdjuntoAsync(_factory, PdfSintetico, "ruta-cliente.pdf");
        using var admin = _factory.CreateAuthenticatedClient();

        var porQuery = await admin.GetAsync(
            $"/Ticket/DescargarAdjunto/{adjuntoId}?ruta=../../appsettings.json&RutaArchivo=appsettings.json");
        var porSegmento = await admin.GetAsync("/Ticket/DescargarAdjunto/..%2f..%2fappsettings.json");

        Assert.Equal(PdfSintetico, await porQuery.Content.ReadAsByteArrayAsync());
        Assert.NotEqual(HttpStatusCode.OK, porSegmento.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<int> CrearTicketAsync(HttpClient admin)
    {
        var crear = await admin.PostAsJsonAsync("/api/tickets", new
        {
            titulo = "COM-S-005 sintetico",
            descripcion = "ticket sintetico",
            tipo = (int)TipoTicket.ErrorFuncional
        });
        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);
        return (await crear.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private sealed record Subido(int AdjuntoId, string RutaRelativa, string UrlDescarga);

    private async Task<Subido> SubirAsync(
        HttpClient admin, string nombre, string mime, byte[] contenido, int? ticketId = null)
    {
        var id = ticketId ?? await CrearTicketAsync(admin);
        using var form = new MultipartFormDataContent();
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new(mime);
        form.Add(parte, "archivo", nombre);

        var subida = await admin.PostAsync($"/api/tickets/{id}/adjuntos", form);
        Assert.Equal(HttpStatusCode.OK, subida.StatusCode);
        var json = await subida.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("rutaRelativa", out _), "La API no debe exponer la ruta física.");

        var adjuntoId = json.GetProperty("id").GetInt32();
        var rutaRelativa = await RutaPersistidaAsync(adjuntoId);
        _archivosAEliminar.Add(Path.Combine(
            _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath,
            rutaRelativa.Replace('/', Path.DirectorySeparatorChar)));
        return new Subido(adjuntoId, rutaRelativa, json.GetProperty("urlDescarga").GetString()!);
    }

    private async Task<string> RutaPersistidaAsync(int adjuntoId)
    {
        string ruta = "";
        await EjecutarAsync(_factory, async ctx =>
            ruta = await ctx.TicketAdjuntos.Where(a => a.Id == adjuntoId).Select(a => a.RutaArchivo).SingleAsync());
        return ruta;
    }

    /// <summary>Siembra ticket + adjunto en la BD del factory (y opcionalmente el archivo físico).</summary>
    private async Task<int> SembrarAdjuntoAsync(
        CustomWebApplicationFactory factory, byte[] contenido, string nombre,
        bool crearArchivo = true, string? rutaPersistida = null)
    {
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var ruta = rutaPersistida ?? $"uploads/tickets/{Guid.NewGuid()}{Path.GetExtension(nombre)}";
        if (crearArchivo)
        {
            var fisico = Path.Combine(webRoot, ruta.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fisico)!);
            await File.WriteAllBytesAsync(fisico, contenido);
            _archivosAEliminar.Add(fisico);
        }

        var id = 0;
        await EjecutarAsync(factory, async ctx =>
        {
            var ticket = new Ticket
            {
                Titulo = "COM-S-005 sembrado",
                Descripcion = "sembrado",
                Tipo = TipoTicket.ErrorFuncional,
                Estado = EstadoTicket.Pendiente,
                RowVersion = new byte[8]
            };
            ctx.Tickets.Add(ticket);
            await ctx.SaveChangesAsync();
            var adjunto = new TicketAdjunto
            {
                TicketId = ticket.Id,
                NombreArchivo = nombre,
                RutaArchivo = ruta,
                TipoMIME = "application/octet-stream",
                TamanoBytes = contenido.Length,
                RowVersion = new byte[8]
            };
            ctx.TicketAdjuntos.Add(adjunto);
            await ctx.SaveChangesAsync();
            id = adjunto.Id;
        });
        return id;
    }

    private static async Task EjecutarAsync(CustomWebApplicationFactory factory, Func<AppDbContext, Task> accion)
    {
        using var scope = factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await contextFactory.CreateDbContextAsync();
        await accion(ctx);
    }
}
