using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Fase 20 (preparación producción segura). Guardas de regresión sobre los
/// defaults de seguridad del canal: ninguna automatización ni escritura real
/// debe quedar habilitada por defecto, y las credenciales no deben venir
/// precargadas. Si alguno de estos defaults cambia, estos tests deben fallar
/// para forzar una decisión explícita.
/// </summary>
public class MercadoLibreProductionDefaultsTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public MercadoLibreProductionDefaultsTests()
    {
        (_factory, _) = MercadoLibreTestDb.Create();
    }

    public void Dispose() { }

    // ------------------------------------------------------------------
    // Defaults de la entidad (sin DB): el constructor debe nacer seguro.
    // ------------------------------------------------------------------

    [Fact]
    public void ConfiguracionNueva_NaceEnModoSimulacion()
    {
        var config = new MercadoLibreConfiguracion();

        Assert.True(config.ModoSimulacion, "ModoSimulacion debe nacer en true (no escribir a ML real).");
    }

    [Fact]
    public void ConfiguracionNueva_NoHabilitaPublicacionDesdeErp()
    {
        var config = new MercadoLibreConfiguracion();

        Assert.False(config.PermitirPublicacionDesdeErp);
    }

    [Fact]
    public void ConfiguracionNueva_NoHabilitaSyncNiVentaAutomatica()
    {
        var config = new MercadoLibreConfiguracion();

        Assert.False(config.SyncAutomaticaStock, "SyncAutomaticaStock no debe habilitarse por defecto.");
        Assert.False(config.SyncAutomaticaPrecio, "SyncAutomaticaPrecio no debe habilitarse por defecto.");
        Assert.False(config.CrearVentaAutomatica, "CrearVentaAutomatica no debe habilitarse por defecto.");
    }

    // ------------------------------------------------------------------
    // Defaults persistidos: GetAsync sobre DB vacía crea la fila con defaults
    // seguros (no depende del seed ni de la UI).
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_SobreDbVacia_PersisteDefaultsSeguros()
    {
        var service = new MercadoLibreConfiguracionService(
            _factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var config = await service.GetAsync();

        Assert.True(config.ModoSimulacion);
        Assert.False(config.PermitirPublicacionDesdeErp);
        Assert.False(config.SyncAutomaticaStock);
        Assert.False(config.SyncAutomaticaPrecio);
        Assert.False(config.CrearVentaAutomatica);
    }

    // ------------------------------------------------------------------
    // Credenciales: no deben venir precargadas; EstaConfigurado solo es true
    // cuando ClientId, ClientSecret y RedirectUri están presentes.
    // ------------------------------------------------------------------

    [Fact]
    public void Options_SinCredenciales_NoEstaConfigurado()
    {
        var options = new MercadoLibreOptions();

        Assert.False(options.EstaConfigurado,
            "Sin ClientId/ClientSecret/RedirectUri el módulo no debe considerarse configurado.");
        Assert.Equal(string.Empty, options.ClientId);
        Assert.Equal(string.Empty, options.ClientSecret);
        Assert.Equal(string.Empty, options.RedirectUri);
    }

    [Fact]
    public void Options_ConCredencialesParciales_NoEstaConfigurado()
    {
        var soloClientId = new MercadoLibreOptions { ClientId = "123" };
        var sinRedirect = new MercadoLibreOptions { ClientId = "123", ClientSecret = "secret" };

        Assert.False(soloClientId.EstaConfigurado);
        Assert.False(sinRedirect.EstaConfigurado);
    }

    [Fact]
    public void Options_BaseUrlMercadoLibre_EsConfigurableYApuntaAProduccion()
    {
        var options = new MercadoLibreOptions();

        // Default productivo, no hardcodeado a ngrok ni a un host de dev.
        Assert.Equal("https://api.mercadolibre.com", options.ApiBaseUrl);
        Assert.Equal("https://auth.mercadolibre.com.ar", options.AuthorizationBaseUrl);
        Assert.Equal(string.Empty, options.RedirectUri);
    }
}
