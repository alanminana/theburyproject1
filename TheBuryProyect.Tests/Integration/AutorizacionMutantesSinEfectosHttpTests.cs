using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.Tests.Infrastructure;
using Xunit.Abstractions;
using static TheBuryProject.Tests.Helpers.MutantesInventario;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// F3.1 (2026-11-11): autorización de TODAS las acciones que modifican datos, con aserción de AUSENCIA DE EFECTOS en base.
/// Complementa a <c>AutorizacionMatrizPorRolHttpTests</c> (que solo mira el código de respuesta y con ids que no existen) con:
///  - una base con datos reales (cliente, crédito+cuota, caja abierta, venta, devolución, usuario SuperAdmin como objetivo);
///  - una huella (conteo + hash de todas las filas) de TODAS las tablas antes y después de cada tanda de POST denegados;
///  - CSRF (sin token, token de otra sesión) sobre las acciones que el rol SÍ puede ejecutar;
///  - SuperAdmin como control positivo y como prueba de "nunca 500" con ids inexistentes, negativos y fuera de rango.
/// El inventario sale de los descriptores del runtime (<see cref="MutantesInventario"/>), así que una acción nueva queda cubierta sola.
/// </summary>
[Collection("HttpIntegration.3")]
public class AutorizacionMutantesSinEfectosHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string SinPermisos = "F31_SinPermisos";
    private const string SoloView = "F31_SoloView";
    private const string SuperAdmin = Roles.SuperAdmin;

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, string> _userIdPorRol = new();
    private readonly Dictionary<string, HashSet<string>> _grants = new();
    private string NombreRol(string rol) => rol is SinPermisos or SoloView ? $"{rol}_{_sfx}" : rol;

    /// <summary>Tablas que cambian por la propia mecánica de las peticiones (no por la lógica de negocio denegada).</summary>
    private static readonly HashSet<string> TablasDeRuido = new(StringComparer.OrdinalIgnoreCase) { "__EFMigrationsHistory" };

    /// <summary>Configuración singleton que el servicio crea con sus valores por defecto en el primer acceso (no depende del id enviado).</summary>
    private static readonly string[] SingletonsDeConfiguracion = { "ConfiguracionesCredito" };

    /// <summary>Módulos con llamadas HTTP externas o herramientas de desarrollo: se prueban solo por autorización (denegados), no en la tanda de SuperAdmin.</summary>
    private static readonly HashSet<string> ExcluidosDeLaTandaDeSuperAdmin = new() { "MercadoLibre", "Diagnostico" };

    public AutorizacionMutantesSinEfectosHttpTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        var catalogo = await context.AccionesModulo.Include(a => a.Modulo).ToListAsync();
        await CrearRolAsync(context, NombreRol(SinPermisos), Array.Empty<AccionModulo>());
        await CrearRolAsync(context, NombreRol(SoloView), catalogo.Where(a => a.Clave.StartsWith("view", StringComparison.Ordinal)).ToArray());

        foreach (var rol in RolesAProbarNombres())
        {
            var roleId = await context.Roles.Where(r => r.Name == NombreRol(rol)).Select(r => r.Id).SingleAsync();
            _grants[rol] = (await context.RolPermisos.Where(p => p.RoleId == roleId && !p.IsDeleted)
                .Select(p => p.ClaimValue).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _userIdPorRol[rol] = await CrearUsuarioAsync(scope, $"f31_{rol}_{_sfx}".ToLowerInvariant(), NombreRol(rol));
        }

        await SembrarDatosAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string[] RolesAProbarNombres() => new[]
    {
        Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Cajero, Roles.Repositor, Roles.Tecnico, Roles.Contador,
        SinPermisos, SoloView, SuperAdmin,
    };

    public static IEnumerable<object[]> RolesSinSuperAdmin() => RolesAProbarNombres().Where(r => r != SuperAdmin).Select(r => new object[] { r });

    // ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(RolesSinSuperAdmin))]
    public async Task ElRol_NoEjecutaAccionesMutantesQueSusPermisosNoCubren_YLaBaseNoCambia(string rol)
    {
        var claims = _grants[rol];
        var objetivo = Descubrir(_factory.Services).Where(a => !a.ExcluidaPorDiseno && DebeDenegar(a, claims, rol)).ToList();
        Assert.NotEmpty(objetivo);

        var client = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        var token = await ObtenerTokenAsync(client);
        var antes = await HuellaAsync();

        var fallas = new List<string>();
        foreach (var a in objetivo)
        {
            using var resp = await EnviarAsync(client, a, token, ValorValido);
            if (!Denegado(resp)) fallas.Add($"{a.Verbo} {a.Ruta} [{a.Id}] -> {(int)resp.StatusCode}");
        }

        var despues = await HuellaAsync();
        var cambios = Diferencias(antes, despues);
        _out.WriteLine($"{rol}: {objetivo.Count} acciones mutantes denegadas esperadas; fallas={fallas.Count}; tablas con cambios={cambios.Count}.");

        Assert.True(fallas.Count == 0, $"{rol} no fue denegado en:\n" + string.Join("\n", fallas));
        Assert.True(cambios.Count == 0, $"{rol}: las peticiones denegadas modificaron la base: " + string.Join(", ", cambios));
    }

    [Fact]
    public async Task SuperAdmin_AtraviesaTodasLasAcciones_YNuncaRespondeCincoCientos_ConIdsInexistentesNegativosOFueraDeRango()
    {
        var client = _factory.CreateClientWithUserId(_userIdPorRol[SuperAdmin]);
        var token = await ObtenerTokenAsync(client);
        var acciones = Descubrir(_factory.Services)
            .Where(a => !a.ExcluidaPorDiseno && !ExcluidosDeLaTandaDeSuperAdmin.Contains(a.Controller)).ToList();
        Assert.True(acciones.Count > 150, $"Inventario insuficiente: {acciones.Count} acciones.");

        // Las acciones SIN parámetros de identificación (configuración, recálculos y procesos globales) actúan sobre toda la base por
        // diseño y se comprueban solo por autorización y "nunca 500"; las que reciben un id deben ser no-op con ids inexistentes.
        static bool RecibeId(Accion a) => a.Parametros.Any(p => p.Fuente != "Body" && Regex.IsMatch(p.Nombre, "(^|[a-z])(id|ids)$", RegexOptions.IgnoreCase));
        var problemas = new List<string>();
        var cambios = new List<string>();
        foreach (var (etiqueta, valor) in new (string, Func<Parametro, string>)[]
                 {
                     ("inexistente", p => EsEntero(p) ? "99999999" : "id-inexistente-" + _sfx),
                     ("negativo", p => EsEntero(p) ? "-1" : EsDecimal(p) ? "-5" : "id-inexistente-" + _sfx),
                     ("fuera-de-rango", p => EsEntero(p) ? "2147483647" : EsDecimal(p) ? "99999999999999999999" : new string('x', 600)),
                 })
        {
            foreach (var grupo in new[] { acciones.Where(a => !RecibeId(a)).ToList(), acciones.Where(RecibeId).ToList() })
            {
                var conId = grupo.Count > 0 && RecibeId(grupo[0]);
                var antes = await HuellaAsync();
                foreach (var a in grupo)
                {
                    using var resp = await EnviarAsync(client, a, token, valor);
                    if (Denegado(resp)) problemas.Add($"[{etiqueta}] DENEGADO al SuperAdmin: {a.Verbo} {a.Ruta} [{a.Id}] -> {(int)resp.StatusCode}");
                    else if ((int)resp.StatusCode >= 500) problemas.Add($"[{etiqueta}] 5xx: {a.Verbo} {a.Ruta} [{a.Id}] -> {(int)resp.StatusCode}");
                }
                if (conId)
                    cambios.AddRange(Diferencias(antes, await HuellaAsync()).Where(c => !SingletonsDeConfiguracion.Any(t => c.StartsWith(t + " ", StringComparison.Ordinal)))
                        .Select(c => $"[{etiqueta}] {c}"));
            }
        }

        _out.WriteLine($"SuperAdmin: {acciones.Count} acciones x 3 variantes ({acciones.Count(RecibeId)} con id); problemas={problemas.Count}; cambios en acciones con id={cambios.Count}");
        Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
        Assert.True(cambios.Count == 0, "Acciones con ids inexistentes/negativos/fuera de rango modificaron la base: " + string.Join(", ", cambios));
    }

    [Fact]
    public async Task CajaCerrar_ConAperturaInexistente_NoDevuelve500()
    {
        // F3.1: regresión del 500 que producía TryPopulateCerrarModelAsync ("Apertura no encontrada") para un supervisor.
        var client = _factory.CreateClientWithUserId(_userIdPorRol[SuperAdmin]);
        var token = await ObtenerTokenAsync(client);
        using var resp = await client.PostAsync("/Caja/Cerrar", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("AperturaCajaId", "99999999"),
            new KeyValuePair<string, string>("MontoRealEnCaja", "0"),
        }));
        Assert.True((int)resp.StatusCode < 500, $"POST /Caja/Cerrar con apertura inexistente -> {(int)resp.StatusCode}");
    }

    [Fact]
    public async Task Csrf_SinTokenOConTokenDeOtraSesion_ElRolQueSiTienePermisoRecibe400YLaBaseNoCambia()
    {
        var rol = Roles.Administrador;
        var claims = _grants[rol];
        var acciones = Descubrir(_factory.Services)
            .Where(a => !a.ExcluidaPorDiseno && !ExcluidosDeLaTandaDeSuperAdmin.Contains(a.Controller) && !DebeDenegar(a, claims, rol)).ToList();
        Assert.True(acciones.Count > 100, $"Inventario insuficiente: {acciones.Count} acciones que el Administrador puede ejecutar.");

        var admin = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        var otro = _factory.CreateClientWithUserId(_userIdPorRol[Roles.Vendedor]);
        var tokenAjeno = await ObtenerTokenAsync(otro);
        await ObtenerTokenAsync(admin); // cookie antiforgery propia ya emitida: el token ajeno no corresponde a ella

        var antes = await HuellaAsync();
        var fallas = new List<string>();
        foreach (var a in acciones)
        {
            using (var sin = await EnviarAsync(admin, a, token: null, ValorValido))
                if (sin.StatusCode != HttpStatusCode.BadRequest) fallas.Add($"sin token: {a.Verbo} {a.Ruta} [{a.Id}] -> {(int)sin.StatusCode}");
            using (var ajeno = await EnviarAsync(admin, a, tokenAjeno, ValorValido))
                if (ajeno.StatusCode != HttpStatusCode.BadRequest) fallas.Add($"token ajeno: {a.Verbo} {a.Ruta} [{a.Id}] -> {(int)ajeno.StatusCode}");
        }

        var cambios = Diferencias(antes, await HuellaAsync());
        _out.WriteLine($"CSRF: {acciones.Count} acciones x 2 variantes; fallas={fallas.Count}; tablas con cambios={string.Join(",", cambios)}");
        Assert.True(fallas.Count == 0, string.Join("\n", fallas));
        Assert.True(cambios.Count == 0, "Un POST sin token CSRF válido modificó la base: " + string.Join(", ", cambios));
    }

    [Fact]
    public void ElInventarioVersionado_EsElActual()
    {
        // El sondeo en vivo (scripts/qa/seguridad/sondeo-post-autorizacion.js) lee este JSON. Si cambia una acción mutante (ruta, verbo, permiso,
        // parámetros), hay que regenerarlo:  BURY_INVENTARIO_OUT=<ruta del repo>/scripts/qa/seguridad/inventario-acciones-mutantes.json dotnet test --filter ElInventarioVersionado
        var actual = ASerializado(Descubrir(_factory.Services));
        var salida = Environment.GetEnvironmentVariable("BURY_INVENTARIO_OUT");
        if (!string.IsNullOrWhiteSpace(salida))
        {
            File.WriteAllText(salida, actual, new UTF8Encoding(false));
            return;
        }
        var ruta = Path.Combine(RaizDelRepo(), "scripts", "qa", "seguridad", "inventario-acciones-mutantes.json");
        Assert.True(File.Exists(ruta), $"Falta {ruta}: generarlo con BURY_INVENTARIO_OUT.");
        var versionado = string.Concat(File.ReadAllText(ruta).Split((char)13));
        Assert.True(versionado == actual, "scripts/qa/seguridad/inventario-acciones-mutantes.json está desactualizado respecto de las acciones mutantes reales: regenerarlo con BURY_INVENTARIO_OUT.");
    }

    [Fact]
    public void ElInventario_CubreLosModulosCriticosDelFrenteF3()
    {
        var porControlador = Descubrir(_factory.Services).GroupBy(a => a.Controller).ToDictionary(g => g.Key, g => g.Count());
        foreach (var c in new[] { "Venta", "Caja", "Credito", "Devolucion", "Usuarios", "Seguridad", "ConfiguracionPago", "Mora", "DocumentosConfig" })
            Assert.True(porControlador.GetValueOrDefault(c) > 0, $"El inventario no contiene acciones mutantes de {c}.");
    }

    // ---------------------------------------------------------------------------------------------------------------------

    private static bool EsEntero(Parametro p) => p.Tipo is "Int32" or "Int32?" or "Int64" or "Int64?";
    private static bool EsDecimal(Parametro p) => p.Tipo is "Decimal" or "Decimal?" or "Double" or "Double?";

    /// <summary>Valores que resuelven a datos reales de la base sembrada (la autorización es lo único que debería frenar la acción).</summary>
    private string ValorValido(Parametro p) => p.Tipo switch
    {
        "String" => TestAuthHandler.UserId, // ids de usuario/rol: el objetivo más sensible es la cuenta SuperAdmin
        "Boolean" or "Boolean?" => "true",
        "Decimal" or "Decimal?" or "Double" or "Double?" => "1",
        _ => "1",
    };

    private async Task<HttpResponseMessage> EnviarAsync(HttpClient client, Accion a, string? token, Func<Parametro, string> valor)
    {
        var ruta = a.Ruta;
        foreach (var p in a.Parametros) ruta = Regex.Replace(ruta, @"\{" + Regex.Escape(p.Nombre) + @"(:[^}]*)?\??\}", Uri.EscapeDataString(valor(p)), RegexOptions.IgnoreCase);
        ruta = Regex.Replace(ruta, @"\{[^}]+\}", "1");

        var simples = a.Parametros.Where(p => p.Fuente != "Body" && p.Tipo != "IFormFile" && p.Tipo != "IFormCollection" && !p.Tipo.Contains("ViewModel", StringComparison.Ordinal)).ToList();
        var campos = simples.Select(p => new KeyValuePair<string, string>(p.Nombre, valor(p))).ToList();
        if (token != null) campos.Add(new("__RequestVerificationToken", token));

        var esJson = a.ContentTypes.Contains("json", StringComparison.OrdinalIgnoreCase) || a.Parametros.Any(p => p.Fuente == "Body");
        HttpResponseMessage? resp = null;
        for (var intento = 0; intento < 3; intento++)
        {
            resp?.Dispose();
            using var req = new HttpRequestMessage(new HttpMethod(a.Verbo), ruta);
            if (token == null) req.Headers.Add(AntiforgeryApiHandler.SinTokenHeader, "1");
            else req.Headers.Add("RequestVerificationToken", token);
            var modo = esJson ? (intento == 0 ? 2 : intento == 1 ? 0 : 1) : intento;
            req.Content = modo switch
            {
                0 => new FormUrlEncodedContent(campos),
                1 => Multipart(campos),
                _ => new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            resp = await client.SendAsync(req);
            if (resp.StatusCode != HttpStatusCode.UnsupportedMediaType) break;
        }
        return resp!;
    }

    private static MultipartFormDataContent Multipart(IEnumerable<KeyValuePair<string, string>> campos)
    {
        var m = new MultipartFormDataContent();
        foreach (var c in campos) m.Add(new StringContent(c.Value), c.Key);
        return m;
    }

    private static bool Denegado(HttpResponseMessage r) =>
        r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
        || ((int)r.StatusCode is >= 300 and < 400
            && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

    private static async Task<string> ObtenerTokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    // --- huella de la base -------------------------------------------------------------------------------------------------

    private async Task<Dictionary<string, string>> HuellaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conexion = context.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open) await conexion.OpenAsync();

        var tablas = new List<string>();
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) tablas.Add(rd.GetString(0));
        }

        var huella = new Dictionary<string, string>();
        foreach (var t in tablas.Where(t => !TablasDeRuido.Contains(t)))
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{t}\"";
            await using var rd = await cmd.ExecuteReaderAsync();
            using var sha = SHA256.Create();
            var n = 0;
            while (await rd.ReadAsync())
            {
                n++;
                var sb = new StringBuilder();
                for (var i = 0; i < rd.FieldCount; i++)
                {
                    var v = rd.IsDBNull(i) ? "∅" : rd.GetValue(i) is byte[] b ? Convert.ToHexString(b) : Convert.ToString(rd.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append(v).Append('\u001f');
                }
                sha.TransformBlock(Encoding.UTF8.GetBytes(sb.ToString()), 0, Encoding.UTF8.GetByteCount(sb.ToString()), null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            huella[t] = $"{n}:{Convert.ToHexString(sha.Hash!)}";
        }
        return huella;
    }

    private static List<string> Diferencias(Dictionary<string, string> antes, Dictionary<string, string> despues) =>
        antes.Keys.Union(despues.Keys).Where(k => antes.GetValueOrDefault(k) != despues.GetValueOrDefault(k))
            .Select(k => $"{k} ({antes.GetValueOrDefault(k)?.Split(':')[0]}->{despues.GetValueOrDefault(k)?.Split(':')[0]} filas)").OrderBy(x => x).ToList();

    // --- datos ---------------------------------------------------------------------------------------------------------------

    private static async Task SembrarDatosAsync(AppDbContext context)
    {
        if (await context.Cajas.AnyAsync(c => c.Id == 1)) return; // el fixture se comparte entre tests: sembrar una sola vez
        var cliente = new Cliente
        {
            Id = 1, TipoDocumento = "DNI", NumeroDocumento = "F31-0001", Apellido = "Sondeo", Nombre = "F31", NombreCompleto = "Sondeo F31",
            Telefono = "0000", Domicilio = "Base de integración", Activo = true
        };
        var credito = new Credito
        {
            Id = 1, ClienteId = 1, Cliente = cliente, Numero = "F31-CRED-1", MontoSolicitado = 1_000m, MontoAprobado = 1_000m, TasaInteres = 0m,
            CantidadCuotas = 1, MontoCuota = 1_000m, TotalAPagar = 1_000m, SaldoPendiente = 1_000m, Estado = EstadoCredito.Activo,
            FechaSolicitud = DateTime.UtcNow.AddMonths(-1), PuntajeRiesgoInicial = 100m
        };
        var cuota = new Cuota
        {
            Id = 1, CreditoId = 1, Credito = credito, NumeroCuota = 1, MontoCapital = 1_000m, MontoInteres = 0m, MontoTotal = 1_000m,
            MontoPagado = 0m, MontoPunitorio = 0m, FechaVencimiento = DateTime.UtcNow.AddDays(5), Estado = EstadoCuota.Pendiente
        };
        var caja = new Caja { Id = 1, Codigo = "F31", Nombre = "Caja F31", IsDeleted = false };
        var apertura = new AperturaCaja { Id = 1, Caja = caja, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "otro.usuario", Cerrada = false, IsDeleted = false };
        var venta = new Venta
        {
            Id = 1, Numero = "F31-V-1", ClienteId = 1, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            Subtotal = 1_000m, Total = 1_000m
        };
        context.AddRange(cliente, credito, cuota, caja, apertura, venta);
        await context.SaveChangesAsync();
        context.Devoluciones.Add(new Devolucion
        {
            Id = 1, VentaId = 1, ClienteId = 1, NumeroDevolucion = "F31-D-1", Motivo = MotivoDevolucion.Otro, Descripcion = "sondeo",
            Estado = EstadoDevolucion.Pendiente, TipoResolucion = TipoResolucionDevolucion.NotaCredito, TotalDevolucion = 100m
        });
        await context.SaveChangesAsync();
    }

    private static async Task CrearRolAsync(AppDbContext context, string nombre, AccionModulo[] acciones)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();
        foreach (var a in acciones)
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId, ModuloId = a.ModuloId, AccionId = a.Id, ClaimValue = $"{a.Modulo.Clave}.{a.Clave}", IsDeleted = false, RowVersion = new byte[8]
            });
        await context.SaveChangesAsync();
    }

    private static async Task<string> CrearUsuarioAsync(IServiceScope scope, string userName, string rol)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.com", EmailConfirmed = true, Activo = true };
        Assert.True((await userManager.CreateAsync(user, "Test123!")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, rol)).Succeeded);
        return user.Id;
    }
}
