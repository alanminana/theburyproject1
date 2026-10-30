using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Los adjuntos de ticket validan, además del tamaño y la extensión, que el contenido real corresponda al tipo declarado
/// (un ejecutable renombrado a .pdf se rechaza), igual que ya hacían los documentos de cliente.
/// </summary>
[Collection("HttpIntegration")]
public class TicketAdjuntosContenidoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime, IDisposable
{
    private static readonly byte[] Pdf = "%PDF-1.4\n% adjunto sintetico\n%%EOF\n"u8.ToArray();
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
    private static readonly byte[] Ejecutable = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }; // MZ

    private readonly CustomWebApplicationFactory _factory;
    private readonly List<string> _archivosAEliminar = new();

    public TicketAdjuntosContenidoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        foreach (var archivo in _archivosAEliminar)
        {
            try { File.Delete(archivo); } catch (IOException) { /* limpieza best-effort */ }
        }
    }

    private async Task<int> CrearTicketAsync(HttpClient client)
    {
        var crear = await client.PostAsJsonAsync("/api/tickets", new
        {
            titulo = "Contenido de adjuntos",
            descripcion = "ticket sintetico",
            tipo = (int)TipoTicket.ErrorFuncional
        });
        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);
        return (await crear.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<HttpResponseMessage> SubirAsync(string nombre, string mime, byte[] contenido)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var id = await CrearTicketAsync(client);
        using var form = new MultipartFormDataContent();
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new(mime);
        form.Add(parte, "archivo", nombre);

        var respuesta = await client.PostAsync($"/api/tickets/{id}/adjuntos", form);
        if (respuesta.StatusCode == HttpStatusCode.OK)
        {
            var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var adjuntoId = json.GetProperty("id").GetInt32();
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<TheBuryProject.Data.AppDbContext>();
            var ruta = ctx.TicketAdjuntos.Where(a => a.Id == adjuntoId).Select(a => a.RutaArchivo).First();
            _archivosAEliminar.Add(Path.Combine(
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>().WebRootPath,
                ruta.Replace('/', Path.DirectorySeparatorChar)));
        }

        return respuesta;
    }

    [Fact]
    public async Task Adjunto_PdfReal_SeAcepta()
        => Assert.Equal(HttpStatusCode.OK, (await SubirAsync("informe.pdf", "application/pdf", Pdf)).StatusCode);

    [Fact]
    public async Task Adjunto_PngReal_SeAcepta()
        => Assert.Equal(HttpStatusCode.OK, (await SubirAsync("captura.png", "image/png", Png)).StatusCode);

    [Fact]
    public async Task Adjunto_TextoPlano_SeAcepta()
        => Assert.Equal(HttpStatusCode.OK, (await SubirAsync("nota.txt", "text/plain", "hola mundo\n"u8.ToArray())).StatusCode);

    [Theory]
    [InlineData("factura.pdf", "application/pdf")]
    [InlineData("captura.png", "image/png")]
    [InlineData("foto.jpg", "image/jpeg")]
    [InlineData("planilla.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("notas.txt", "text/plain")]
    public async Task Adjunto_EjecutableRenombrado_SeRechaza(string nombre, string mime)
    {
        var respuesta = await SubirAsync(nombre, mime, Ejecutable);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("contenido", await respuesta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adjunto_PdfConContenidoDePng_SeRechaza()
        => Assert.Equal(HttpStatusCode.BadRequest, (await SubirAsync("falso.pdf", "application/pdf", Png)).StatusCode);

    // ── Reglas puras de la firma ──────────────────────────────────────────

    private static bool Coincide(byte[] bytes, string ext)
        => DocumentoValidationHelper.ContenidoCoincideConExtension(new MemoryStream(bytes), ext);

    [Theory]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, true)]
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(".jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, true)]
    [InlineData(".gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData(".doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, true)]
    [InlineData(".xls", new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, true)]
    [InlineData(".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true)]
    [InlineData(".zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true)]
    [InlineData(".pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]
    [InlineData(".gif", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, false)]
    [InlineData(".zip", new byte[] { 0x25, 0x50, 0x44, 0x46 }, false)]
    [InlineData(".pdf", new byte[] { 0x25, 0x50 }, false)]
    [InlineData(".pdf", new byte[0], false)]
    public void Firma_PorExtension(string ext, byte[] bytes, bool esperado)
        => Assert.Equal(esperado, Coincide(bytes, ext));

    [Fact]
    public void Firma_Webp_RequiereRiffYWebp()
    {
        var webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
        var wav = "RIFF\0\0\0\0WAVEfmt "u8.ToArray();

        Assert.True(Coincide(webp, ".webp"));
        Assert.False(Coincide(wav, ".webp"));
    }

    [Fact]
    public void Firma_Txt_RechazaBinarios()
    {
        Assert.True(Coincide("texto común con tildes: áé"u8.ToArray(), ".txt"));
        Assert.False(Coincide(new byte[] { 0x41, 0x00, 0x42 }, ".txt"));
        Assert.False(Coincide(Array.Empty<byte>(), ".txt"));
    }

    [Fact]
    public void Firma_ExtensionSinFirmaConocida_NoBloquea()
        => Assert.True(Coincide(new byte[] { 1, 2, 3 }, ".csv"));
}
