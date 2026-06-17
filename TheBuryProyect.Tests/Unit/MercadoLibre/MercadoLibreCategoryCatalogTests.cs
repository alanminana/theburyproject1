using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Modules.MercadoLibre.Services;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests del catálogo local de categorías ML: importador por streaming (json/gz),
/// detección de hojas/atributos requeridos y servicio de consulta. Usa fixtures
/// chicos escritos a disco temporal; nunca el archivo gigante real ni la API.
/// </summary>
public class MercadoLibreCategoryCatalogTests : IDisposable
{
    private readonly List<string> _temporales = new();

    // Fixture chico: una raíz no publicable + dos hojas publicables con atributos.
    private const string CatalogoChico = """
    {
    "MLA1403":{"id":"MLA1403","name":"Alimentos y Bebidas","total_items_in_this_category":2713932,"path_from_root":[{"id":"MLA1403","name":"Alimentos y Bebidas"}],"children_categories":[{"id":"MLA1423","name":"Almacén"}],"attribute_types":"attributes","settings":{"listing_allowed":false,"buying_allowed":true,"item_conditions":["new"],"max_title_length":60,"status":"enabled"}},
    "MLA416632":{"id":"MLA416632","name":"Papeles para Impresión","total_items_in_this_category":35199,"path_from_root":[{"id":"MLA5982","name":"Librería"},{"id":"MLA455746","name":"Comercial y Oficina"},{"id":"MLA416632","name":"Papeles para Impresión"}],"children_categories":[],"attribute_types":"attributes","settings":{"listing_allowed":true,"buying_allowed":true,"item_conditions":["new"],"max_title_length":60,"status":"enabled"},"attributes":[{"id":"BRAND","name":"Marca","tags":{"catalog_required":true,"required":true},"hierarchy":"PARENT_PK","relevance":1,"value_type":"string","value_max_length":255,"hint":"Escribe la marca o 'Genérica'."},{"id":"PAPER_SIZE","name":"Tamaño del papel","tags":{"required":true},"value_type":"list","values":[{"id":"93217","name":"A4"},{"id":"93221","name":"Carta"}]},{"id":"PAPER_TYPE","name":"Tipo de papel","tags":{"required":true},"value_type":"list","values":[{"id":"5732603","name":"Bond"}]},{"id":"GRAMMAGE","name":"Gramaje","tags":{},"value_type":"number_unit","allowed_units":[{"id":"g","name":"g"}],"default_unit":"g"},{"id":"UNITS_PER_PACK","name":"Unidades por pack","tags":{"conditional_required":true},"value_type":"number"},{"id":"GTIN","name":"Código universal","tags":{"read_only":true},"value_type":"string"}]},
    "MLA3530":{"id":"MLA3530","name":"Papel Glasé","total_items_in_this_category":1200,"path_from_root":[{"id":"MLA5982","name":"Librería"},{"id":"MLA3530","name":"Papel Glasé"}],"children_categories":[],"attribute_types":"attributes","settings":{"listing_allowed":true,"buying_allowed":true,"item_conditions":["new"],"max_title_length":60,"status":"enabled"},"attributes":[{"id":"BRAND","name":"Marca","tags":{"required":true},"value_type":"string"}]}
    }
    """;

    private string EscribirArchivo(string contenido, bool gzip)
    {
        var path = Path.Combine(Path.GetTempPath(),
            "ml-cat-" + Guid.NewGuid().ToString("N") + (gzip ? ".json.gz" : ".json"));

        if (gzip)
        {
            using var fs = File.Create(path);
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            var bytes = Encoding.UTF8.GetBytes(contenido);
            gz.Write(bytes, 0, bytes.Length);
        }
        else
        {
            File.WriteAllText(path, contenido, Encoding.UTF8);
        }

        _temporales.Add(path);
        return path;
    }

    private static MercadoLibreCategoryCatalogImportService Importador(TestDbContextFactory factory)
        => new(factory, NullLogger<MercadoLibreCategoryCatalogImportService>.Instance);

    private static MercadoLibreCategoryCatalogService Consulta(TestDbContextFactory factory)
        => new(factory);

    [Fact]
    public async Task Importa_categorias_y_atributos_desde_archivo_json()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var path = EscribirArchivo(CatalogoChico, gzip: false);
            var resultado = await Importador(factory).ImportFromFileAsync(path);

            Assert.True(resultado.Ok, resultado.Error);
            Assert.Equal(3, resultado.ImportedCategories);
            Assert.Equal(2, resultado.LeafCategories);
            Assert.Equal(2, resultado.ListingAllowedCategories);
            Assert.Equal(7, resultado.ImportedAttributes); // 6 + 1

            await using var ctx = factory.CreateDbContext();
            var hoja = await ctx.MercadoLibreCategories.SingleAsync(c => c.CategoryId == "MLA416632");
            Assert.True(hoja.IsLeaf);
            Assert.True(hoja.ListingAllowed);
            Assert.Equal("MLA455746", hoja.ParentCategoryId); // penúltimo del path
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task Importa_desde_gzip()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var path = EscribirArchivo(CatalogoChico, gzip: true);
            var resultado = await Importador(factory).ImportFromFileAsync(path);

            Assert.True(resultado.Ok, resultado.Error);
            Assert.Equal("gzip", resultado.SourceKind);
            Assert.Equal(3, resultado.ImportedCategories);
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task Detecta_required_desde_tags_y_excluye_read_only()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var path = EscribirArchivo(CatalogoChico, gzip: false);
            await Importador(factory).ImportFromFileAsync(path);

            var requeridos = await Consulta(factory).GetRequiredAttributesAsync("MLA416632", "new");
            var ids = requeridos.Select(a => a.AttributeId).ToList();

            // BRAND/PAPER_SIZE/PAPER_TYPE son bloqueantes; UNITS_PER_PACK (conditional) recomendado.
            Assert.Contains("BRAND", ids);
            Assert.Contains("PAPER_SIZE", ids);
            Assert.Contains("PAPER_TYPE", ids);
            Assert.Contains("UNITS_PER_PACK", ids);

            // GTIN es read_only ⇒ nunca se muestra como requerido.
            Assert.DoesNotContain("GTIN", ids);
            // GRAMMAGE no tiene flag de obligatoriedad ⇒ no aparece.
            Assert.DoesNotContain("GRAMMAGE", ids);

            var brand = requeridos.Single(a => a.AttributeId == "BRAND");
            Assert.True(brand.EsBloqueante);
            var units = requeridos.Single(a => a.AttributeId == "UNITS_PER_PACK");
            Assert.False(units.EsBloqueante);
            Assert.True(units.EsRecomendado);

            var size = requeridos.Single(a => a.AttributeId == "PAPER_SIZE");
            Assert.Equal(2, size.Values.Count); // A4 / Carta
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task IsLeafListingAllowed_refleja_el_catalogo()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var path = EscribirArchivo(CatalogoChico, gzip: false);
            await Importador(factory).ImportFromFileAsync(path);
            var consulta = Consulta(factory);

            var hoja = await consulta.IsLeafListingAllowedAsync("MLA416632");
            Assert.Equal((true, true, true), hoja);

            var raiz = await consulta.IsLeafListingAllowedAsync("MLA1403");
            Assert.Equal((true, false, false), raiz); // existe, no es hoja, no publicable

            var inexistente = await consulta.IsLeafListingAllowedAsync("MLA999999");
            Assert.Equal((false, false, false), inexistente);
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task Buscar_prioriza_hojas_publicables()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var path = EscribirArchivo(CatalogoChico, gzip: false);
            await Importador(factory).ImportFromFileAsync(path);

            // "Papel" matchea MLA416632 (hoja publicable) y MLA3530 (hoja publicable);
            // la raíz "Alimentos" no matchea. La hoja con más items va primero.
            var resultados = await Consulta(factory).BuscarCategoriasAsync("Papel", 10);
            Assert.NotEmpty(resultados);
            Assert.All(resultados, r => Assert.True(r.EsHoja && r.ListingAllowed));
            Assert.Equal("MLA416632", resultados.First().CategoryId); // 35199 > 1200 items
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task Reimportar_reemplaza_el_catalogo()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var importador = Importador(factory);
            var path = EscribirArchivo(CatalogoChico, gzip: false);

            await importador.ImportFromFileAsync(path);
            var segunda = await importador.ImportFromFileAsync(path);

            Assert.True(segunda.Ok, segunda.Error);

            await using var ctx = factory.CreateDbContext();
            // No se duplican filas al reimportar (reemplazo wholesale).
            Assert.Equal(3, await ctx.MercadoLibreCategories.CountAsync());
            Assert.Equal(7, await ctx.MercadoLibreCategoryAttributes.CountAsync());
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public async Task Archivo_inexistente_devuelve_error_sin_excepcion()
    {
        var (factory, conn) = MercadoLibreTestDb.Create();
        try
        {
            var resultado = await Importador(factory)
                .ImportFromFileAsync(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N") + ".json"));

            Assert.False(resultado.Ok);
            Assert.NotNull(resultado.Error);
        }
        finally { conn.Dispose(); }
    }

    public void Dispose()
    {
        foreach (var p in _temporales)
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }
}
