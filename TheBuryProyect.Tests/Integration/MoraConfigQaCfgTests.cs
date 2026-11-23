using System.Net;
using System.Text.RegularExpressions;
using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels.Mora;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// QA-CFG — escenarios CFG-01..CFG-16 sobre la configuración de Mora.
// Los tests describen el comportamiento DESEADO. Contra 9504428 algunos fallan a propósito
// (CFG-04/06/13/08b/14b): son defectos del código, no de los tests. Ver docs/qa-cfg-resultados.md.
// Sólo usan la API pública estable (HTTP + MoraService ya existente), de modo que compilan igual
// contra el código corregido sin tocar nada.
// ---------------------------------------------------------------------------

[Collection("HttpIntegration.3")]
[Trait("QA", "CFG")]
public class MoraConfigQaCfgHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string FormUrl = "/Mora/ConfiguracionExpandida";
    private const string PostUrl = "/Mora/GuardarConfiguracionExpandida";

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;

    public MoraConfigQaCfgHttpTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    // CFG-01 — valores iniciales sembrados y legibles
    [Fact]
    public async Task CFG01_ValoresIniciales_SeLeenYSeRegistran()
    {
        var sembrada = await SembrarAsync(ConfiguracionCompleta());
        var leida = await LeerAsync(sembrada.Id);
        Dump("CFG-01 inicial", leida);
        AssertIguales(Snapshot(sembrada), Snapshot(leida));
        Assert.Equal(4.5m, leida.TasaMoraBase);
        Assert.Equal(9, leida.MaximoCuotasAcuerdo);
    }

    // CFG-02 — abrir ConfiguracionExpandida
    [Fact]
    public async Task CFG02_AbrirFormulario_RenderizaCamposEditablesYTokens()
    {
        var c = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();

        var form = await LeerFormularioAsync(client);

        foreach (var campo in new[] { "TasaMoraBase", "DiasGracia", "HoraEjecucionDiaria", "PuntosMaximosARestar",
                                      "Id", "RowVersion", "__RequestVerificationToken" })
            Assert.Contains(form, kv => kv.Key == campo);
        Assert.Equal("4.5", form.First(kv => kv.Key == "TasaMoraBase").Value.Replace(",", "."));
        _out.WriteLine($"CFG-02: formulario publica {form.Count} pares: {string.Join(", ", form.Select(k => k.Key).Distinct())}");
    }

    // CFG-03 + CFG-04 — guardar sin cambios no pierde / nulea / desactiva ningún campo
    [Fact]
    public async Task CFG03_04_GuardarSinCambios_NoPierdeNiNuleaNiDesactivaNingunParametro()
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(client);

        var r = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, r.StatusCode);
        var actual = await LeerAsync(esperado.Id);
        Dump("CFG-03/04 después de guardar sin cambios", actual);
        AssertIguales(Snapshot(esperado), Snapshot(actual));
    }

    // CFG-05 + CFG-06 — editar un campo permitido: sólo cambia ése
    [Fact]
    public async Task CFG05_06_ModificarCampoPermitido_ActualizaSoloLoPrevisto()
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(client);
        Reemplazar(form, "TasaMoraBase", "7.25");
        Reemplazar(form, "DiasGracia", "10");

        var r = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, r.StatusCode);
        var actual = await LeerAsync(esperado.Id);
        Dump("CFG-05/06 después", actual);
        Assert.Equal(7.25m, actual.TasaMoraBase);
        Assert.Equal(10, actual.DiasGracia);
        esperado.TasaMoraBase = 7.25m;
        esperado.DiasGracia = 10;
        AssertIguales(Snapshot(esperado), Snapshot(actual)); // nada más cambió
    }

    // CFG-07 — tasas negativas, fuera de rango, entradas inválidas: rechazo sin escribir
    [Theory]
    [InlineData("TasaMoraBase", "-1")]
    [InlineData("TasaMoraBase", "100.01")]
    [InlineData("TasaMoraBase", "abc")]
    [InlineData("DiasGracia", "-1")]
    [InlineData("DiasGracia", "91")]
    [InlineData("DiasGracia", "1.5")]
    [InlineData("DiasAntesAlertaPreventiva", "0")]
    [InlineData("PuntosMaximosARestar", "1001")]
    [InlineData("PorcentajeRecuperacionScore", "101")]
    [InlineData("HoraEjecucionDiaria", "25:61")]
    public async Task CFG07_ValoresInvalidos_SeRechazanYNoPersisten(string campo, string valor)
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(client);
        Reemplazar(form, campo, valor);

        var r = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, r.StatusCode); // vuelve al formulario
        var actual = await LeerAsync(esperado.Id);
        AssertIguales(Snapshot(esperado), Snapshot(actual));
        _out.WriteLine($"CFG-07 {campo}={valor}: rechazado, configuración intacta");
    }

    // CFG-07 (b) — campos de acuerdos/tasas que el formulario NO publica pero un POST armado sí puede enviar
    [Theory]
    [InlineData("MaximoCuotasAcuerdo", "0")]
    [InlineData("MaximoCuotasAcuerdo", "61")]
    [InlineData("PorcentajeMinimoEntrega", "101")]
    [InlineData("PorcentajeMaximoCondonacion", "-5")]
    [InlineData("TasaPrimerMes", "-0.01")]
    [InlineData("MoraMinima", "-1")]
    public async Task CFG07b_CamposNoPublicados_PostArmado_ValoresInvalidos_NoPersisten(string campo, string valor)
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(client);
        form.Add(new(campo, valor));

        var r = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, r.StatusCode);
        AssertIguales(Snapshot(esperado), Snapshot(await LeerAsync(esperado.Id)));
    }

    // CFG-07c — aísla la validación del ViewModel (independiente de la pérdida de campos del guardado):
    // [Range(0, 100)] con límites enteros sobre decimal TRUNCA el valor antes de comparar.
    [Theory]
    [InlineData("TasaMoraBase", 100.01)]
    [InlineData("TasaMoraBase", -0.01)]
    [InlineData("TasaPrimerMes", 100.5)]
    [InlineData("PorcentajeMaximoCondonacion", 100.99)]
    [InlineData("PorcentajeMinimoEntrega", -0.5)]
    public void CFG07c_Range_DecimalFueraDeRangoPorFraccion_DebeSerInvalido(string propiedad, double valor)
    {
        var vm = new ConfiguracionMoraExpandidaViewModel();
        var prop = typeof(ConfiguracionMoraExpandidaViewModel).GetProperty(propiedad)!;
        prop.SetValue(vm, (decimal?)(decimal)valor);
        var errores = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valido = System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(
            (decimal?)(decimal)valor,
            new System.ComponentModel.DataAnnotations.ValidationContext(vm) { MemberName = propiedad }, errores);

        _out.WriteLine($"CFG-07c {propiedad}={valor}: valido={valido}");
        Assert.False(valido);
    }

    // CFG-09 — acceso a configuración
    [Fact]
    public async Task CFG09_AccesoConfiguracion_AdminAccede_SinPermisosNo()
    {
        await SembrarAsync(ConfiguracionCompleta());
        using var admin = await ClienteAdminAsync();
        using var sinPermisos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var ok = await admin.GetAsync(FormUrl);
        var denegado = await sinPermisos.GetAsync(FormUrl);

        _out.WriteLine($"CFG-09: admin={(int)ok.StatusCode} sinPermisos={(int)denegado.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);
    }

    // CFG-10 — guardar sin autorización
    [Fact]
    public async Task CFG10_GuardarSinAutorizacion_RechazaYNoPersiste()
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var admin = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(admin); // token + Id + RowVersion válidos
        Reemplazar(form, "TasaMoraBase", "99");
        using var sinPermisos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var r = await sinPermisos.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        _out.WriteLine($"CFG-10: POST sin permisos => {(int)r.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        AssertIguales(Snapshot(esperado), Snapshot(await LeerAsync(esperado.Id)));
    }

    // CFG-11 — antiforgery
    [Fact]
    public async Task CFG11_SinTokenAntiforgery_RechazaYNoPersiste()
    {
        var esperado = await SembrarAsync(ConfiguracionCompleta());
        using var client = await ClienteAdminAsync();
        var form = await LeerFormularioAsync(client);
        form.RemoveAll(kv => kv.Key == "__RequestVerificationToken");
        Reemplazar(form, "TasaMoraBase", "99");

        var sinToken = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));
        form.Add(new("__RequestVerificationToken", "token-falsificado"));
        var tokenFalso = await client.PostAsync(PostUrl, new FormUrlEncodedContent(form));

        _out.WriteLine($"CFG-11: sin token={(int)sinToken.StatusCode} token falso={(int)tokenFalso.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tokenFalso.StatusCode);
        AssertIguales(Snapshot(esperado), Snapshot(await LeerAsync(esperado.Id)));
    }

    // CFG-12 + CFG-13 — dos administradores, formulario viejo, RowVersion.
    // Se ejecuta SIN Skip: sirve para demostrar si hay last-write-wins.
    [Fact]
    public async Task CFG12_13_DosAdministradores_SegundoNoSobrescribeEnSilencio()
    {
        var original = await SembrarAsync(ConfiguracionCompleta());
        using var a = await ClienteAdminAsync();
        using var b = _factory.CreateAuthenticatedClient();
        var formA = await LeerFormularioAsync(a);
        var formB = await LeerFormularioAsync(b);
        var rvA = formA.First(kv => kv.Key == "RowVersion").Value;
        var rvB = formB.First(kv => kv.Key == "RowVersion").Value;
        Assert.Equal(rvA, rvB); // ambos abrieron la misma versión

        Reemplazar(formA, "TasaMoraBase", "9");
        var rA = await a.PostAsync(PostUrl, new FormUrlEncodedContent(formA));
        Assert.Equal(HttpStatusCode.Found, rA.StatusCode);
        var trasA = await LeerAsync(original.Id);
        Assert.Equal(9m, trasA.TasaMoraBase);
        _out.WriteLine($"CFG-13: RowVersion antes={rvA} despues de A={Convert.ToBase64String(trasA.RowVersion)}");

        Reemplazar(formB, "DiasGracia", "20"); // B no toca la tasa, pero su formulario viejo trae 4.5
        var rB = await b.PostAsync(PostUrl, new FormUrlEncodedContent(formB));
        var final = await LeerAsync(original.Id);
        _out.WriteLine($"CFG-13: B => {(int)rB.StatusCode}; final TasaMoraBase={final.TasaMoraBase} DiasGracia={final.DiasGracia}");

        // Correcto: B debe ser rechazado (o al menos no revertir lo de A).
        Assert.Equal(9m, final.TasaMoraBase);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private void Dump(string titulo, ConfiguracionMora c)
    {
        _out.WriteLine($"--- {titulo} ---");
        foreach (var kv in Snapshot(c).OrderBy(k => k.Key))
            _out.WriteLine($"{kv.Key} = {kv.Value ?? "null"}");
    }

    private async Task<HttpClient> ClienteAdminAsync()
    {
        await _factory.SeedTestUserAsync();
        return _factory.CreateAuthenticatedClient();
    }

    internal static ConfiguracionMora ConfiguracionCompleta() => new()
    {
        TipoTasaMora = TipoTasaMora.Mensual, TasaMoraBase = 4.5m, BaseCalculoMora = BaseCalculoMora.CapitalMasInteres,
        DiasGracia = 6, EscalonamientoActivo = true, TasaPrimerMes = 1.1m, TasaSegundoMes = 2.2m,
        TasaTercerMesEnAdelante = 3.3m, TopeMaximoMoraActivo = true, TipoTopeMora = TipoTopeMora.MontoFijo,
        ValorTopeMora = 50_000m, MoraMinima = 150m,
        DiasParaPrioridadMedia = 15, DiasParaPrioridadAlta = 45, DiasParaPrioridadCritica = 90,
        MontoParaPrioridadMedia = 10_000m, MontoParaPrioridadAlta = 50_000m, MontoParaPrioridadCritica = 100_000m,
        ProcesoAutomaticoActivo = true, HoraEjecucionDiaria = new TimeSpan(6, 30, 0), AlertasPreventivasActivas = true,
        DiasAntesAlertaPreventiva = 4, CambiarEstadoCuotaAuto = true, ActualizarMoraAutomaticamente = true,
        UltimaEjecucion = new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc),
        NotificacionesActivas = true, WhatsAppActivo = true, EmailActivo = true, CanalPreferido = CanalNotificacion.Ambos,
        NotificarProximoVencimiento = true, DiasAntesNotificacionPreventiva = 3, NotificarCuotaVencida = true,
        NotificarMoraAcumulada = true, FrecuenciaRecordatorioMora = 7, MaximoNotificacionesDiarias = 2,
        MaximoNotificacionesPorCuota = 5, HoraInicioEnvio = new TimeSpan(9, 0, 0), HoraFinEnvio = new TimeSpan(20, 0, 0),
        EnviarFinDeSemana = true,
        DiasMaximosSinGestion = 12, DiasParaCumplirPromesa = 8, MaximoCuotasAcuerdo = 9, PorcentajeMinimoEntrega = 25m,
        PermitirCondonacionMora = true, PorcentajeMaximoCondonacion = 40m,
        BloqueoAutomaticoActivo = true, DiasParaBloquear = 60, CuotasVencidasParaBloquear = 3, MontoMoraParaBloquear = 20_000m,
        TipoBloqueo = TipoBloqueoCliente.NuevosCreditos, DesbloqueoAutomatico = true,
        ImpactarScorePorMora = true, PuntosRestarPorCuotaVencida = 5, PuntosRestarPorDiaMora = 0.5m,
        PuntosMaximosARestar = 50, RecuperarScoreAlPagar = true, PorcentajeRecuperacionScore = 30m
    };

    private async Task<ConfiguracionMora> SembrarAsync(ConfiguracionMora config)
    {
        using var scope = _factory.Services.CreateScope();
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await f.CreateDbContextAsync();
        ctx.ConfiguracionesMora.RemoveRange(await ctx.ConfiguracionesMora.ToListAsync());
        await ctx.SaveChangesAsync();
        ctx.ConfiguracionesMora.Add(config);
        await ctx.SaveChangesAsync();
        return config;
    }

    private async Task<ConfiguracionMora> LeerAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await f.CreateDbContextAsync();
        return await ctx.ConfiguracionesMora.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private static async Task<List<KeyValuePair<string, string>>> LeerFormularioAsync(HttpClient client)
    {
        var response = await client.GetAsync(FormUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var m = Regex.Match(html, "<form[^>]*GuardarConfiguracionExpandida[^>]*>(.*?)</form>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(m.Success, "No se encontró el formulario de configuración expandida.");

        var campos = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(m.Groups[1].Value, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            var name = Atributo(tag, "name");
            if (string.IsNullOrEmpty(name)) continue;
            var type = (Atributo(tag, "type") ?? "text").ToLowerInvariant();
            if (type is "checkbox" or "radio" && !Regex.IsMatch(tag, "\\schecked\\b", RegexOptions.IgnoreCase)) continue;
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

    internal static Dictionary<string, object?> Snapshot(ConfiguracionMora c)
    {
        var excluidas = new HashSet<string>
        {
            nameof(ConfiguracionMora.Id), nameof(ConfiguracionMora.RowVersion), nameof(ConfiguracionMora.CreatedAt),
            nameof(ConfiguracionMora.UpdatedAt), nameof(ConfiguracionMora.CreatedBy), nameof(ConfiguracionMora.UpdatedBy),
            nameof(ConfiguracionMora.IsDeleted)
        };
        return typeof(ConfiguracionMora).GetProperties()
            .Where(p => p.CanWrite && !excluidas.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(c));
    }

    internal static void AssertIguales(Dictionary<string, object?> esperado, Dictionary<string, object?> actual)
    {
        var dif = esperado.Where(kv => !Equals(kv.Value, actual[kv.Key]))
            .Select(kv => $"{kv.Key}: esperado={kv.Value ?? "null"} actual={actual[kv.Key] ?? "null"}").ToList();
        Assert.True(dif.Count == 0, $"{dif.Count} campo(s) alterados:\n" + string.Join("\n", dif));
    }
}

/// <summary>CFG-08, 14, 15, 16 — reglas que consumen la configuración (SQLite en memoria, sin HTTP).</summary>
[Trait("QA", "CFG")]
public class MoraConfigQaCfgServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ITestOutputHelper _out;
    private int _cuota;

    public MoraConfigQaCfgServiceTests(ITestOutputHelper output)
    {
        _out = output;
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
    }

    public void Dispose() { _context.Dispose(); _connection.Dispose(); }

    // Servicio nuevo por operación: GetConfiguracionAsync cachea la config dentro de la instancia.
    private MoraService NuevoServicio() =>
        new(_context, _mapper, NullLogger<MoraService>.Instance, creditoService: CreditoStub.Crear(),
            clienteScoringService: new ClienteScoringService(_context, NullLogger<ClienteScoringService>.Instance),
            RelojComercial.Sistema);

    private async Task<ConfiguracionMora> ConfigAsync(Action<ConfiguracionMora> ajustar)
    {
        var existentes = await _context.ConfiguracionesMora.ToListAsync();
        _context.ConfiguracionesMora.RemoveRange(existentes);
        var c = new ConfiguracionMoraFactory().Crear();
        ajustar(c);
        _context.ConfiguracionesMora.Add(c);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return c;
    }

    private sealed class ConfiguracionMoraFactory
    {
        public ConfiguracionMora Crear() => new()
        {
            TipoTasaMora = TipoTasaMora.Mensual, TasaMoraBase = 3m, DiasGracia = 0,
            MaximoCuotasAcuerdo = 12, PorcentajeMinimoEntrega = 10m,
            PermitirCondonacionMora = true, PorcentajeMaximoCondonacion = 40m
        };
    }

    private async Task<(Cliente cliente, Credito credito, AlertaCobranza alerta)> EscenarioAlertaAsync()
    {
        var cliente = new Cliente { Nombre = "Cfg", Apellido = "Qa", TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Email = "cfg@test.com" };
        _context.Set<Cliente>().Add(cliente);
        await _context.SaveChangesAsync();
        var credito = new Credito { Numero = Guid.NewGuid().ToString("N")[..10], ClienteId = cliente.Id,
            Estado = EstadoCredito.Activo, MontoSolicitado = 10_000m, MontoAprobado = 10_000m, SaldoPendiente = 5_000m,
            TasaInteres = 3m, CantidadCuotas = 12, FechaSolicitud = DateTime.UtcNow };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        var alerta = new AlertaCobranza { ClienteId = cliente.Id, CreditoId = credito.Id };
        _context.Set<AlertaCobranza>().Add(alerta);
        await _context.SaveChangesAsync();
        return (cliente, credito, alerta);
    }

    private CrearAcuerdoViewModel Acuerdo(Cliente cl, Credito cr, AlertaCobranza al, int cuotas = 3,
        decimal deuda = 5_000m, decimal mora = 500m, decimal condonar = 0m, decimal entrega = 1_000m) => new()
    {
        AlertaId = al.Id, ClienteId = cl.Id, CreditoId = cr.Id, MontoDeudaOriginal = deuda, MontoMoraOriginal = mora,
        MontoCondonar = condonar, MontoEntregaInicial = entrega, CantidadCuotas = cuotas,
        FechaPrimeraCuota = TheBuryProject.Services.RelojComercial.Sistema.InicioDiaComercial.AddMonths(1)
    };

    // CFG-08 — límites de acuerdos de pago (cuotas, entrega mínima, condonación permitida)
    [Fact]
    public async Task CFG08a_Acuerdo_LimiteDeCuotas_ExactoPasa_UnoMasRechaza()
    {
        await ConfigAsync(c => c.MaximoCuotasAcuerdo = 6);
        var (cl, cr, al) = await EscenarioAlertaAsync();

        var id = await NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas: 6), "g");
        Assert.True(id > 0);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas: 7), "g"));
        Assert.Contains("6", ex.Message);
    }

    [Fact]
    public async Task CFG08a_Acuerdo_EntregaMinima_ExactaPasa_PorDebajoRechaza()
    {
        await ConfigAsync(c => c.PorcentajeMinimoEntrega = 20m);
        var (cl, cr, al) = await EscenarioAlertaAsync();
        // total = 5000 + 500 - 0 = 5500; 20% = 1100
        Assert.True(await NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, entrega: 1_100m), "g") > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, entrega: 1_099m), "g"));
    }

    [Fact]
    public async Task CFG08a_Acuerdo_CondonacionNoPermitida_Rechaza()
    {
        await ConfigAsync(c => c.PermitirCondonacionMora = false);
        var (cl, cr, al) = await EscenarioAlertaAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, condonar: 100m), "g"));
    }

    // CFG-08 (b) — el tope PorcentajeMaximoCondonacion (40%) se hace cumplir en el servidor
    // (CFG-DEF-02, corregido en la versión de integridad).
    [Fact]
    public async Task CFG08b_Acuerdo_CondonacionSuperaPorcentajeMaximo_DeberiaRechazarse()
    {
        await ConfigAsync(c => { c.PermitirCondonacionMora = true; c.PorcentajeMaximoCondonacion = 40m; });
        var (cl, cr, al) = await EscenarioAlertaAsync();
        // mora original 500; condonar 100% de la mora (500) con tope 40% => debería rechazar.
        var ex = await Record.ExceptionAsync(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, condonar: 500m), "g"));
        _out.WriteLine($"CFG-08b: condonar 500 sobre mora 500 con tope 40% => {(ex == null ? "ACEPTADO (sin control server-side)" : "rechazado: " + ex.Message)}");
        Assert.NotNull(ex);
    }

    // Decisión de negocio: los acuerdos no condonan. Ni el tope configurado ni PermitirCondonacionMora habilitan una quita.
    [Fact]
    public async Task CFG08b_Acuerdo_NuncaCondona_NiDentroDelTopeConfigurado()
    {
        await ConfigAsync(c => { c.PermitirCondonacionMora = true; c.PorcentajeMaximoCondonacion = 40m; });
        var (cl, cr, al) = await EscenarioAlertaAsync();
        // mora 500, 40% = 200: antes pasaba; ahora cualquier condonación se rechaza.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, condonar: 200m), "g"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, condonar: 0.01m), "g"));
        Assert.Equal(0, await _context.AcuerdosPago.CountAsync());
    }

    // CFG-08c — integridad de importes: negativos, cuotas <= 0 y totales en cero no deben llegar a dividir ni persistir.
    [Theory]
    [InlineData(0, 5_000, 500, 0, 1_000)]     // cuotas = 0 -> división por cero en MontoCuotaAcuerdo
    [InlineData(-1, 5_000, 500, 0, 1_000)]    // cuotas negativas
    [InlineData(3, -5_000, 500, 0, 1_000)]    // deuda negativa
    [InlineData(3, 5_000, -500, 0, 1_000)]    // mora negativa
    [InlineData(3, 5_000, 500, -50, 1_000)]   // condonación negativa (aumentaba el total)
    [InlineData(3, 5_000, 500, 0, -1)]        // entrega negativa
    [InlineData(3, 0, 0, 0, 0)]               // total 0 -> división por cero en % de entrega
    [InlineData(3, 5_000, 500, 0, 5_501)]     // entrega mayor al total
    [InlineData(3, 5_000, 500, 501, 1_000)]   // condonar más que la mora
    public async Task CFG08c_Acuerdo_ImportesIncoherentes_SeRechazanSinPersistir(
        int cuotas, decimal deuda, decimal mora, decimal condonar, decimal entrega)
    {
        await ConfigAsync(c => { c.PermitirCondonacionMora = true; c.PorcentajeMinimoEntrega = 10m; c.PorcentajeMaximoCondonacion = 100m; });
        var (cl, cr, al) = await EscenarioAlertaAsync();
        var antes = await _context.AcuerdosPago.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas, deuda, mora, condonar, entrega), "g"));

        Assert.Equal(antes, await _context.AcuerdosPago.CountAsync());
    }

    // CFG-14 — MORA-CONTRACTUAL: las alertas de cobranza NO estiman importes de mora. Antes se calculaba una mora a partir de
    // TasaMoraBase (mensual/30, ignorando TipoTasaMora) que no coincidía con el punitorio real; se retiró. La alerta informa
    // capital vencido, cuotas y días de atraso. TasaMoraBase y TipoTasaMora quedan sin efecto sobre las alertas.
    private async Task<(AlertaCobranza Alerta, LogMora Log)> ProcesarAlertaAsync(TipoTasaMora? tipo, decimal? tasa, int diasVencido, decimal monto)
    {
        await ConfigAsync(c => { c.TipoTasaMora = tipo; c.TasaMoraBase = tasa; c.DiasGracia = 0; });
        var (_, cr, _) = await EscenarioAlertaAsync();
        _context.Set<AlertaCobranza>().RemoveRange(_context.Set<AlertaCobranza>());
        _context.Set<Cuota>().RemoveRange(_context.Set<Cuota>()); // aísla: sólo la cuota de este escenario
        _context.Set<Cuota>().Add(new Cuota
        {
            CreditoId = cr.Id, NumeroCuota = ++_cuota, MontoCapital = monto * 0.8m, MontoInteres = monto * 0.2m,
            MontoTotal = monto, MontoPagado = 0m, Estado = EstadoCuota.Pendiente,
            FechaVencimiento = RelojComercial.Sistema.InicioDiaComercial.AddDays(-diasVencido)
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await NuevoServicio().ProcesarMoraAsync();

        var alerta = await _context.AlertasCobranza.AsNoTracking().SingleAsync(a => a.CreditoId == cr.Id);
        var log = await _context.LogsMora.AsNoTracking().OrderByDescending(l => l.Id).FirstAsync();
        return (alerta, log);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(TipoTasaMora.Mensual, 3)]
    [InlineData(TipoTasaMora.Diaria, 5)]
    public async Task CFG14_Alerta_NoEstimaMora_PeroInformaCapitalYDiasDeAtraso(TipoTasaMora? tipo, int? tasaEntera)
    {
        var (alerta, log) = await ProcesarAlertaAsync(tipo, tasaEntera, 10, 1_000m);

        Assert.Equal(0m, alerta.MontoMoraCalculada);
        Assert.Equal(0m, log.TotalRecargosAplicados);
        Assert.Equal(1_000m, alerta.MontoVencido);
        Assert.Equal(10, alerta.DiasAtraso);
        Assert.Equal(1, alerta.CuotasVencidas);
    }

    [Fact]
    public async Task CFG14_CambiarTasaMoraBase_NoCambiaLoQueInformaLaAlerta()
    {
        var (a1, _) = await ProcesarAlertaAsync(TipoTasaMora.Mensual, 3m, 10, 1_000m);
        var (a2, _) = await ProcesarAlertaAsync(TipoTasaMora.Mensual, 6m, 10, 1_000m);

        Assert.Equal(a1.MontoVencido, a2.MontoVencido);
        Assert.Equal(a1.DiasAtraso, a2.DiasAtraso);
        Assert.Equal(0m, a2.MontoMoraCalculada);
    }

    [Fact]
    public async Task CFG15_AcuerdoNuevo_UsaElMaximoDeCuotasVigente()
    {
        var (cl, cr, al) = await EscenarioAlertaAsync();
        await ConfigAsync(c => c.MaximoCuotasAcuerdo = 12);
        Assert.True(await NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas: 10), "g") > 0);
        await ConfigAsync(c => c.MaximoCuotasAcuerdo = 4);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas: 10), "g"));
    }

    // CFG-16 — operaciones existentes conservan valores históricos al cambiar la configuración
    [Fact]
    public async Task CFG16_OperacionExistente_ConservaValoresHistoricos_TrasCambiarLaTasa()
    {
        var (antes, log1) = await ProcesarAlertaAsync(TipoTasaMora.Mensual, 3m, 10, 1_000m);

        await ConfigAsync(c => { c.TasaMoraBase = 9m; c.DiasGracia = 0; });
        await NuevoServicio().ProcesarMoraAsync(); // reproceso con la tasa nueva
        var despues = await _context.AlertasCobranza.AsNoTracking().SingleAsync(a => a.CreditoId == antes.CreditoId);
        var log1Despues = await _context.LogsMora.AsNoTracking().SingleAsync(l => l.Id == log1.Id);

        Assert.Equal(log1.TotalRecargosAplicados, log1Despues.TotalRecargosAplicados); // el log histórico no se recalcula
        Assert.Equal(antes.MontoVencido, despues.MontoVencido);
        Assert.Equal(antes.Id, despues.Id); // la alerta existente no se duplica
        Assert.Equal(0m, despues.MontoMoraCalculada);
    }

    [Fact]
    public async Task CFG16_AcuerdoExistente_ConservaSusTerminos_TrasEndurecerLaConfiguracion()
    {
        await ConfigAsync(c => c.MaximoCuotasAcuerdo = 12);
        var (cl, cr, al) = await EscenarioAlertaAsync();
        var id = await NuevoServicio().CrearAcuerdoPagoAsync(Acuerdo(cl, cr, al, cuotas: 10), "g");

        await ConfigAsync(c => { c.MaximoCuotasAcuerdo = 2; c.PorcentajeMinimoEntrega = 90m; c.PermitirCondonacionMora = false; });

        var a = await _context.AcuerdosPago.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(10, a.CantidadCuotas);
        Assert.Equal(1_000m, a.MontoEntregaInicial);
        Assert.Equal(5_500m, a.MontoTotalAcuerdo);
    }

    /// <summary>ICreditoService mínimo: ProcesarMoraAsync sólo invoca ActualizarEstadoCuotasAsync (Task).</summary>
    private class CreditoStub : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask
            : throw new NotImplementedException(targetMethod?.Name);

        public static TheBuryProject.Services.Interfaces.ICreditoService Crear() =>
            Create<TheBuryProject.Services.Interfaces.ICreditoService, CreditoStub>();
    }
}
