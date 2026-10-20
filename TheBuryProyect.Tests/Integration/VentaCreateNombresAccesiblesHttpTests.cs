using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VENTA-CREATE-ACCESIBILIDAD — HTML REAL generado por /Venta/Create (pipeline HTTP + Razor + parciales
/// compartidos de Cotización y modales). Verifica que cada control de usuario documentado en la
/// auditoría tenga un nombre accesible por un mecanismo estable (<c>&lt;label for&gt;</c> que apunta al
/// control o <c>aria-label</c>), que no haya ids duplicados, que ningún <c>&lt;label for&gt;</c> apunte
/// al vacío y que los <c>name</c> que viajan en el POST no hayan cambiado.
/// El nombre calculado por el navegador (lo que lee un lector de pantalla) lo cubre el E2E
/// <c>e2e/venta-create-nombres-accesibles.spec.js</c>.
/// </summary>
[Collection("HttpIntegration")]
public class VentaCreateNombresAccesiblesHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string TestUser = "testuser";
    private readonly CustomWebApplicationFactory _factory;

    public VentaCreateNombresAccesiblesHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Controles con etiqueta visible: deben quedar asociados con <c>label for</c> al propio control.</summary>
    private static readonly string[] ConEtiquetaVisible =
    {
        "txt-producto-seleccionado", "txt-cantidad", "select-producto-unidad", "txt-descuento-item",
        "txt-num-cheque", "txt-banco-cheque", "txt-titular-cheque", "txt-cuit-cheque",
        "txt-fecha-emision-cheque", "txt-fecha-vencimiento-cheque", "txt-monto-cheque",
        "txt-excepcion-documental", "plan-excepcion-motivo", "select-tipo-documento"
    };

    /// <summary>Controles sin etiqueta visible (filtros, buscador, nota del modal): nombre por aria-label.</summary>
    private static readonly string[] ConAriaLabel =
    {
        "filtro-categoria", "filtro-marca", "filtro-precio-min", "filtro-precio-max",
        "input-buscar-producto", "confirmModalNote"
    };

    [Fact]
    public async Task Create_ControlesConEtiquetaVisible_TienenLabelForQueApuntaAlPropioControl()
    {
        var html = await GetCreateHtmlAsync();

        foreach (var id in ConEtiquetaVisible)
        {
            Assert.True(ExisteControl(html, id), $"El control #{id} debe existir en /Venta/Create.");

            var etiquetas = EtiquetasPara(html, id);
            Assert.True(etiquetas.Count == 1,
                $"#{id} debe tener exactamente un <label for=\"{id}\"> (hay {etiquetas.Count}).");

            var texto = TextoVisible(etiquetas[0]);
            Assert.False(string.IsNullOrWhiteSpace(texto),
                $"El <label for=\"{id}\"> de #{id} no puede estar vacío.");
        }
    }

    [Fact]
    public async Task Create_ControlesSinEtiquetaVisible_TienenAriaLabelDescriptivo()
    {
        var html = await GetCreateHtmlAsync();

        foreach (var id in ConAriaLabel)
        {
            var tag = TagDelControl(html, id);
            Assert.False(tag is null, $"El control #{id} debe existir en /Venta/Create.");

            var ariaLabel = Atributo(tag!, "aria-label");
            Assert.False(string.IsNullOrWhiteSpace(ariaLabel), $"#{id} debe declarar aria-label.");
            // Un nombre útil tiene más de una palabra suelta y no repite solo el placeholder genérico.
            Assert.True(ariaLabel!.Trim().Length >= 6, $"El aria-label de #{id} es demasiado corto: '{ariaLabel}'.");
        }
    }

    [Fact]
    public async Task Create_EtiquetasEspecificas_DiferencianCamposEquivalentes()
    {
        var html = await GetCreateHtmlAsync();

        // Min/Max de precio y los filtros no pueden compartir el mismo nombre.
        var nombres = new[] { "filtro-precio-min", "filtro-precio-max", "filtro-categoria", "filtro-marca" }
            .Select(id => Atributo(TagDelControl(html, id)!, "aria-label")!)
            .ToArray();
        Assert.Equal(nombres.Length, nombres.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // La etiqueta de descuento conserva el texto visible "Desc. %" y agrega contexto solo para lectores.
        var descuento = EtiquetasPara(html, "txt-descuento-item").Single();
        Assert.Contains("Desc.", TextoVisible(descuento));
        Assert.Contains("sr-only", descuento);
    }

    [Fact]
    public async Task Create_NoHayIdsDuplicados_NiLabelForHuerfanos()
    {
        var html = await GetCreateHtmlAsync();

        var ids = Regex.Matches(html, "<[a-zA-Z][^>]*?\\sid=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(id => !id.Contains('@'))
            .ToList();
        var duplicados = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicados.Count == 0, $"IDs duplicados: {string.Join(", ", duplicados)}");

        var idSet = ids.ToHashSet();
        var huerfanos = Regex.Matches(html, "<label\\b[^>]*?\\sfor=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(f => !idSet.Contains(f))
            .ToList();
        Assert.True(huerfanos.Count == 0, $"<label for> sin control: {string.Join(", ", huerfanos)}");
    }

    [Fact]
    public async Task Create_NombresDeCamposEnviadosNoCambian()
    {
        var html = await GetCreateHtmlAsync();

        // Contrato de binding: los name que viajan en el POST son los de siempre.
        var esperados = new Dictionary<string, string>
        {
            ["txt-num-cheque"] = "DatosCheque.NumeroCheque",
            ["txt-banco-cheque"] = "DatosCheque.Banco",
            ["txt-titular-cheque"] = "DatosCheque.Titular",
            ["txt-cuit-cheque"] = "DatosCheque.CUIT",
            ["txt-fecha-emision-cheque"] = "DatosCheque.FechaEmision",
            ["txt-fecha-vencimiento-cheque"] = "DatosCheque.FechaVencimiento",
            ["txt-monto-cheque"] = "DatosCheque.Monto",
            ["txt-excepcion-documental"] = "MotivoExcepcionDocumentalCreate",
        };

        foreach (var (id, name) in esperados)
        {
            Assert.Equal(name, Atributo(TagDelControl(html, id)!, "name"));
        }

        // Los controles de la línea en edición (se leen por JS, nunca se envían) siguen sin `name`.
        foreach (var id in new[] { "txt-cantidad", "txt-descuento-item", "select-producto-unidad", "filtro-categoria", "filtro-marca" })
        {
            Assert.Null(Atributo(TagDelControl(html, id)!, "name"));
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task<string> GetCreateHtmlAsync()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/Venta/Create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"venta-form\"", html);
        return html;
    }

    private static string? TagDelControl(string html, string id)
    {
        var m = Regex.Match(html,
            "<(?:input|select|textarea)\\b[^>]*?\\sid=\"" + Regex.Escape(id) + "\"[^>]*>",
            RegexOptions.Singleline);
        return m.Success ? m.Value : null;
    }

    private static bool ExisteControl(string html, string id) => TagDelControl(html, id) is not null;

    private static string? Atributo(string tag, string atributo)
    {
        var m = Regex.Match(tag, "\\s" + Regex.Escape(atributo) + "=\"([^\"]*)\"", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>HTML completo de cada &lt;label for="id"&gt;...&lt;/label&gt;.</summary>
    private static List<string> EtiquetasPara(string html, string id) =>
        Regex.Matches(html,
                "<label\\b[^>]*?\\sfor=\"" + Regex.Escape(id) + "\"[^>]*>.*?</label>",
                RegexOptions.Singleline)
            .Select(m => m.Value)
            .ToList();

    private static string TextoVisible(string labelHtml)
    {
        // Texto del label sin el contenido sr-only ni etiquetas: lo que ve la persona.
        var sinSrOnly = Regex.Replace(labelHtml, "<span[^>]*sr-only[^>]*>.*?</span>", string.Empty, RegexOptions.Singleline);
        var sinTags = Regex.Replace(sinSrOnly, "<[^>]+>", " ");
        return Regex.Replace(sinTags, "\\s+", " ").Trim();
    }

    private async Task SeedCajaAbiertaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser))
        {
            return;
        }

        var caja = new Caja
        {
            Codigo = "CAJA-A11Y",
            Nombre = "Caja Nombres Accesibles",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        context.Cajas.Add(caja);
        await context.SaveChangesAsync();

        context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }
}
