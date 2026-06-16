using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests del servicio de categorías ML (consumo online, sin persistir el árbol).
/// Verifican mapeo del predictor, cálculo de hoja/ruta en el detalle y la
/// resolución de token (público vs. cuenta conectada).
/// </summary>
public class MercadoLibreCategoriaServiceTests
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public int TokenCalls { get; private set; }
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
        {
            TokenCalls++;
            return Task.FromResult("token-test");
        }
    }

    private static (MercadoLibreCategoriaService Servicio, FakeMercadoLibreApiClient Api,
        FakeAuthService Auth, TestDbContextFactory Factory) BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var api = new FakeMercadoLibreApiClient();
        var auth = new FakeAuthService();

        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var servicio = new MercadoLibreCategoriaService(
            api, configService, auth,
            Options.Create(new MercadoLibreOptions { SiteId = "MLA" }),
            NullLogger<MercadoLibreCategoriaService>.Instance);

        return (servicio, api, auth, factory);
    }

    private static async Task<int> SembrarCuentaConfiguradaAsync(TestDbContextFactory factory)
    {
        await using var ctx = factory.CreateDbContext();

        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
            Nickname = "TEST",
            SiteId = "MLA",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };
        ctx.MercadoLibreAccounts.Add(cuenta);
        await ctx.SaveChangesAsync();

        ctx.MercadoLibreConfiguraciones.Add(new MercadoLibreConfiguracion { AccountId = cuenta.Id });
        await ctx.SaveChangesAsync();

        return cuenta.Id;
    }

    [Fact]
    public async Task SugerirAsync_TextoCorto_DevuelveVacioSinLlamarApi()
    {
        var (servicio, api, _, _) = BuildServicio();
        api.Predicciones.Add(new MeliCategoryPredictionDto { CategoryId = "MLA1055", CategoryName = "X" });

        var resultado = await servicio.SugerirAsync("a");

        Assert.Empty(resultado);
        Assert.Null(api.UltimaQueryPredictor); // no se llamó al predictor
    }

    [Fact]
    public async Task SugerirAsync_MapeaPrediccionesDelPredictor()
    {
        var (servicio, api, _, _) = BuildServicio();
        api.Predicciones.Add(new MeliCategoryPredictionDto
        {
            CategoryId = "MLA1055",
            CategoryName = "Celulares y Smartphones",
            DomainName = "Celulares"
        });

        var resultado = await servicio.SugerirAsync("iphone 15");

        var sugerencia = Assert.Single(resultado);
        Assert.Equal("MLA1055", sugerencia.CategoryId);
        Assert.Equal("Celulares y Smartphones", sugerencia.Nombre);
        Assert.Equal("Celulares", sugerencia.Dominio);
        Assert.Equal("iphone 15", api.UltimaQueryPredictor);
    }

    [Fact]
    public async Task ResolverAsync_CategoriaHoja_CalculaHojaRutaYListingAllowed()
    {
        var (servicio, api, _, _) = BuildServicio();
        api.Categorias["MLA1055"] = new MeliCategoryDto
        {
            Id = "MLA1055",
            Name = "Celulares y Smartphones",
            PathFromRoot = new()
            {
                new MeliCategoryNodeDto { Id = "MLA1051", Name = "Celulares y Teléfonos" },
                new MeliCategoryNodeDto { Id = "MLA1055", Name = "Celulares y Smartphones" }
            },
            ChildrenCategories = new(),
            Settings = new MeliCategorySettingsDto { ListingAllowed = true, MaxTitleLength = 60 }
        };

        var resultado = await servicio.ResolverAsync("MLA1055");

        Assert.True(resultado.EsHoja);
        Assert.True(resultado.ListingAllowed);
        Assert.Equal(60, resultado.MaxTitleLength);
        Assert.Equal("Celulares y Teléfonos > Celulares y Smartphones", resultado.Path);
    }

    [Fact]
    public async Task ResolverAsync_CategoriaConHijos_NoEsHoja()
    {
        var (servicio, api, _, _) = BuildServicio();
        api.Categorias["MLA1051"] = new MeliCategoryDto
        {
            Id = "MLA1051",
            Name = "Celulares y Teléfonos",
            ChildrenCategories = new() { new MeliCategoryNodeDto { Id = "MLA1055", Name = "Celulares y Smartphones" } },
            Settings = new MeliCategorySettingsDto { ListingAllowed = false }
        };

        var resultado = await servicio.ResolverAsync("MLA1051");

        Assert.False(resultado.EsHoja);
        Assert.False(resultado.ListingAllowed);
    }

    [Fact]
    public async Task ListarHijosAsync_SinId_DevuelveCategoriasRaiz()
    {
        var (servicio, api, _, _) = BuildServicio();
        api.SiteCategories.Add(new MeliCategoryNodeDto { Id = "MLA1051", Name = "Celulares y Teléfonos" });
        api.SiteCategories.Add(new MeliCategoryNodeDto { Id = "MLA1000", Name = "Electrónica" });

        var nivel = await servicio.ListarHijosAsync(null);

        Assert.False(nivel.EsHoja);
        Assert.Equal(2, nivel.Hijos.Count);
        Assert.Contains(nivel.Hijos, h => h.CategoryId == "MLA1051");
    }

    [Fact]
    public async Task ResolverAsync_SinCuenta_LlamaApiSinToken()
    {
        var (servicio, api, auth, _) = BuildServicio();
        api.Categorias["MLA1055"] = new MeliCategoryDto { Id = "MLA1055", Name = "X" };

        await servicio.ResolverAsync("MLA1055");

        Assert.True(api.UltimoTokenCategoriaRecibido);
        Assert.Null(api.UltimoTokenCategoria);
        Assert.Equal(0, auth.TokenCalls);
    }

    [Fact]
    public async Task ResolverAsync_ConCuenta_EnviaTokenDeLaCuenta()
    {
        var (servicio, api, auth, factory) = BuildServicio();
        await SembrarCuentaConfiguradaAsync(factory);
        api.Categorias["MLA1055"] = new MeliCategoryDto { Id = "MLA1055", Name = "X" };

        await servicio.ResolverAsync("MLA1055");

        Assert.Equal("token-test", api.UltimoTokenCategoria);
        Assert.Equal(1, auth.TokenCalls);
    }
}
