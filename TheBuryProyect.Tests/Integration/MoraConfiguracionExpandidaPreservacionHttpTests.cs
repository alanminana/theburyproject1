using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// MORA-CONFIG-PRESERVACION — /Mora/ConfiguracionExpandida, extremo a extremo vía HTTP real.
//
// El formulario sólo publica 14 campos editables (+ Id/RowVersion), pero
// UpdateConfiguracionExpandidaAsync mapeaba ~49 campos del ViewModel: los no publicados
// llegaban con el default del binder (null/false) y pisaban la configuración persistida.
// Estos tests renderizan el formulario real, lo reenvían como lo haría un navegador
// (inputs + hidden "false" de los checkboxes) y verifican campo por campo.
// ---------------------------------------------------------------------------

[Collection("HttpIntegration")]
public class MoraConfiguracionExpandidaPreservacionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string FormUrl = "/Mora/ConfiguracionExpandida";
    private const string PostUrl = "/Mora/GuardarConfiguracionExpandida";

    private readonly CustomWebApplicationFactory _factory;

    public MoraConfiguracionExpandidaPreservacionHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GuardarSinCambios_PreservaTodosLosCamposDeConfiguracion()
    {
        var esperado = await SembrarConfiguracionCompletaAsync();
        using var client = await CrearClienteAdminAsync();

        var form = await LeerFormularioAsync(client);
        var response = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var actual = await LeerConfiguracionAsync(esperado.Id);
        AssertCamposIguales(Snapshot(esperado), Snapshot(actual));
    }

    [Fact]
    public async Task GuardarConCambios_PersisteLosEditadosYPreservaElResto()
    {
        var esperado = await SembrarConfiguracionCompletaAsync();
        using var client = await CrearClienteAdminAsync();

        var form = await LeerFormularioAsync(client);
        Reemplazar(form, "TasaMoraBase", "7.25");
        Reemplazar(form, "DiasGracia", "10");
        Reemplazar(form, "PuntosMaximosARestar", "");
        // Destildar un checkbox: el navegador deja de enviar "true" y sólo viaja el hidden "false".
        form.RemoveAll(kv => kv.Key == "ImpactarScorePorMora" && kv.Value == "true");
        Assert.Contains(form, kv => kv.Key == "ImpactarScorePorMora" && kv.Value == "false");

        var response = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var actual = await LeerConfiguracionAsync(esperado.Id);

        Assert.Equal(7.25m, actual.TasaMoraBase);
        Assert.Equal(10, actual.DiasGracia);
        Assert.Null(actual.PuntosMaximosARestar); // vaciar un campo enviado es una edición explícita
        Assert.False(actual.ImpactarScorePorMora);

        esperado.TasaMoraBase = 7.25m;
        esperado.DiasGracia = 10;
        esperado.PuntosMaximosARestar = null;
        esperado.ImpactarScorePorMora = false;
        AssertCamposIguales(Snapshot(esperado), Snapshot(actual));
    }

    [Fact]
    public async Task GuardarSinCambios_ConfiguracionConNulls_NoInventaValores()
    {
        // Simétrico: una configuración mayormente vacía (histórica) no debe "rellenarse"
        // con defaults del ViewModel al guardar sin cambios.
        var esperado = await SembrarConfiguracionAsync(new ConfiguracionMora
        {
            DiasGracia = null,
            TasaMoraBase = 3m,
            HoraEjecucionDiaria = null
        });
        using var client = await CrearClienteAdminAsync();

        var form = await LeerFormularioAsync(client);
        var response = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var actual = await LeerConfiguracionAsync(esperado.Id);
        AssertCamposIguales(Snapshot(esperado), Snapshot(actual));
    }

    // Concurrencia optimista: el segundo administrador conserva un formulario con un RowVersion
    // obsoleto; el guardado se rechaza y no revierte lo que guardó el primero.
    [Fact]
    public async Task DosAdministradores_FormularioViejo_NoRevierteElGuardadoAnterior()
    {
        var original = await SembrarConfiguracionCompletaAsync();
        using var clienteA = await CrearClienteAdminAsync();
        using var clienteB = _factory.CreateAuthenticatedClient();

        // Ambos abren el formulario con la misma versión.
        var formA = await LeerFormularioAsync(clienteA);
        var formB = await LeerFormularioAsync(clienteB);

        Reemplazar(formA, "TasaMoraBase", "9");
        Assert.Equal(HttpStatusCode.Found, (await clienteA.PostAsync(PostUrl, new FormUrlEncodedContent(formA))).StatusCode);

        Reemplazar(formB, "DiasGracia", "20");
        var respuestaB = await clienteB.PostAsync(PostUrl, new FormUrlEncodedContent(formB));
        Assert.Equal(HttpStatusCode.Found, respuestaB.StatusCode); // redirige con TempData de error

        var actual = await LeerConfiguracionAsync(original.Id);
        Assert.Equal(9m, actual.TasaMoraBase);   // el cambio de A sobrevive
        Assert.Equal(6, actual.DiasGracia);      // el cambio de B no se persistió
    }

    [Fact]
    public async Task FormularioSinRowVersion_NoEludeLaProteccion()
    {
        var original = await SembrarConfiguracionCompletaAsync();
        using var client = await CrearClienteAdminAsync();

        var form = await LeerFormularioAsync(client);
        form.RemoveAll(kv => string.Equals(kv.Key, "RowVersion", StringComparison.OrdinalIgnoreCase));
        Reemplazar(form, "TasaMoraBase", "9");

        var response = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var actual = await LeerConfiguracionAsync(original.Id);
        Assert.Equal(4.5m, actual.TasaMoraBase);
    }

    [Fact]
    public async Task FormularioConRowVersionAjeno_SeRechazaSinModificarDatos()
    {
        var original = await SembrarConfiguracionCompletaAsync();
        using var client = await CrearClienteAdminAsync();

        var form = await LeerFormularioAsync(client);
        Reemplazar(form, "RowVersion", Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Reemplazar(form, "TasaMoraBase", "9");

        await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        var actual = await LeerConfiguracionAsync(original.Id);
        Assert.Equal(4.5m, actual.TasaMoraBase);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<HttpClient> CrearClienteAdminAsync()
    {
        await _factory.SeedTestUserAsync();
        return _factory.CreateAuthenticatedClient();
    }

    private static ConfiguracionMora ConfiguracionCompleta() => new()
    {
        // Cálculo
        TipoTasaMora = TipoTasaMora.Mensual,
        TasaMoraBase = 4.5m,
        BaseCalculoMora = BaseCalculoMora.CapitalMasInteres,
        DiasGracia = 6,
        EscalonamientoActivo = true,
        TasaPrimerMes = 1.1m,
        TasaSegundoMes = 2.2m,
        TasaTercerMesEnAdelante = 3.3m,
        TopeMaximoMoraActivo = true,
        TipoTopeMora = TipoTopeMora.MontoFijo,
        ValorTopeMora = 50_000m,
        MoraMinima = 150m,
        // Clasificación
        DiasParaPrioridadMedia = 15,
        DiasParaPrioridadAlta = 45,
        DiasParaPrioridadCritica = 90,
        MontoParaPrioridadMedia = 10_000m,
        MontoParaPrioridadAlta = 50_000m,
        MontoParaPrioridadCritica = 100_000m,
        // Automatización
        ProcesoAutomaticoActivo = true,
        HoraEjecucionDiaria = new TimeSpan(6, 30, 0),
        AlertasPreventivasActivas = true,
        DiasAntesAlertaPreventiva = 4,
        CambiarEstadoCuotaAuto = true,
        ActualizarMoraAutomaticamente = true,
        UltimaEjecucion = new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc),
        // Notificaciones
        NotificacionesActivas = true,
        WhatsAppActivo = true,
        EmailActivo = true,
        CanalPreferido = CanalNotificacion.Ambos,
        NotificarProximoVencimiento = true,
        DiasAntesNotificacionPreventiva = 3,
        NotificarCuotaVencida = true,
        NotificarMoraAcumulada = true,
        FrecuenciaRecordatorioMora = 7,
        MaximoNotificacionesDiarias = 2,
        MaximoNotificacionesPorCuota = 5,
        HoraInicioEnvio = new TimeSpan(9, 0, 0),
        HoraFinEnvio = new TimeSpan(20, 0, 0),
        EnviarFinDeSemana = true,
        // Gestión
        DiasMaximosSinGestion = 12,
        DiasParaCumplirPromesa = 8,
        MaximoCuotasAcuerdo = 9,
        PorcentajeMinimoEntrega = 25m,
        PermitirCondonacionMora = true,
        PorcentajeMaximoCondonacion = 40m,
        // Bloqueos
        BloqueoAutomaticoActivo = true,
        DiasParaBloquear = 60,
        CuotasVencidasParaBloquear = 3,
        MontoMoraParaBloquear = 20_000m,
        TipoBloqueo = TipoBloqueoCliente.NuevosCreditos,
        DesbloqueoAutomatico = true,
        // Score
        ImpactarScorePorMora = true,
        PuntosRestarPorCuotaVencida = 5,
        PuntosRestarPorDiaMora = 0.5m,
        PuntosMaximosARestar = 50,
        RecuperarScoreAlPagar = true,
        PorcentajeRecuperacionScore = 30m
    };

    private Task<ConfiguracionMora> SembrarConfiguracionCompletaAsync() =>
        SembrarConfiguracionAsync(ConfiguracionCompleta());

    /// <summary>Deja una única ConfiguracionMora activa en la BD de test (la del factory, nunca la real).</summary>
    private async Task<ConfiguracionMora> SembrarConfiguracionAsync(ConfiguracionMora config)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        context.ConfiguracionesMora.RemoveRange(await context.ConfiguracionesMora.ToListAsync());
        await context.SaveChangesAsync();

        context.ConfiguracionesMora.Add(config);
        await context.SaveChangesAsync();
        return config;
    }

    private async Task<ConfiguracionMora> LeerConfiguracionAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        return await context.ConfiguracionesMora.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    /// <summary>
    /// Serializa el formulario renderizado como lo haría un navegador: cada input con name,
    /// los checkboxes sólo si están tildados, y los hidden "false" que agrega el tag helper.
    /// </summary>
    private static async Task<List<KeyValuePair<string, string>>> LeerFormularioAsync(HttpClient client)
    {
        var response = await client.GetAsync(FormUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var formMatch = Regex.Match(html, "<form[^>]*GuardarConfiguracionExpandida[^>]*>(.*?)</form>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(formMatch.Success, "No se encontró el formulario de configuración expandida.");

        var campos = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(formMatch.Groups[1].Value, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            var name = Atributo(tag, "name");
            if (string.IsNullOrEmpty(name))
                continue;

            var type = (Atributo(tag, "type") ?? "text").ToLowerInvariant();
            if (type is "checkbox" or "radio" && !Regex.IsMatch(tag, "\\schecked\\b", RegexOptions.IgnoreCase))
                continue;

            campos.Add(new(name, WebUtility.HtmlDecode(Atributo(tag, "value") ?? (type == "checkbox" ? "on" : ""))));
        }

        Assert.Contains(campos, kv => kv.Key == "__RequestVerificationToken");
        return campos;
    }

    private static string? Atributo(string tag, string nombre)
    {
        var m = Regex.Match(tag, $"\\s{nombre}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void Reemplazar(List<KeyValuePair<string, string>> form, string campo, string valor)
    {
        var i = form.FindIndex(kv => kv.Key == campo);
        Assert.True(i >= 0, $"El formulario no publica {campo}.");
        form[i] = new(campo, valor);
    }

    /// <summary>Todas las propiedades de configuración (excluye auditoría, Id y RowVersion).</summary>
    private static Dictionary<string, object?> Snapshot(ConfiguracionMora c)
    {
        var excluidas = new HashSet<string>
        {
            nameof(ConfiguracionMora.Id), nameof(ConfiguracionMora.RowVersion),
            nameof(ConfiguracionMora.CreatedAt), nameof(ConfiguracionMora.UpdatedAt),
            nameof(ConfiguracionMora.CreatedBy), nameof(ConfiguracionMora.UpdatedBy),
            nameof(ConfiguracionMora.IsDeleted)
        };
        return typeof(ConfiguracionMora).GetProperties()
            .Where(p => p.CanWrite && !excluidas.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(c));
    }

    private static void AssertCamposIguales(Dictionary<string, object?> esperado, Dictionary<string, object?> actual)
    {
        var diferencias = esperado
            .Where(kv => !Equals(kv.Value, actual[kv.Key]))
            .Select(kv => $"{kv.Key}: esperado={kv.Value ?? "null"} actual={actual[kv.Key] ?? "null"}")
            .ToList();

        Assert.True(diferencias.Count == 0,
            $"{diferencias.Count} campo(s) de configuración alterados:\n" + string.Join("\n", diferencias));
    }
}
