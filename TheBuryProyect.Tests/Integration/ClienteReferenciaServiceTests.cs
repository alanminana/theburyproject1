using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02: referencias informativas de clientes. Servicio real sobre SQLite.
/// Cubre AC-02-1 (alta/edición/baja lógica con auditoría), AC-02-2 (validaciones) y AC-02-5 (aislamiento entre clientes).
/// </summary>
public class ClienteReferenciaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteReferenciaService _service;

    public ClienteReferenciaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new ClienteReferenciaService(_context, NullLogger<ClienteReferenciaService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static int _dni = 30_000_000;

    private async Task<Cliente> SeedClienteAsync(string telefono = "341 555-1000", string? alternativo = "341 555-2000", string? laboral = "341 555-3000")
    {
        var cliente = new Cliente
        {
            Nombre = "Ana", Apellido = "Titular", TipoDocumento = "DNI",
            NumeroDocumento = Interlocked.Increment(ref _dni).ToString(),
            Telefono = telefono, TelefonoAlternativo = alternativo, TelefonoLaboral = laboral,
            NivelRiesgo = NivelRiesgoCredito.AprobadoCondicional, Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private static ClienteReferenciaViewModel Modelo(string nombre = "Juan Perez", string telefono = "341 444-0001",
        TipoReferenciaCliente tipo = TipoReferenciaCliente.Personal) => new()
    {
        Tipo = tipo, Nombre = nombre, Telefono = telefono, Relacion = "Hermano"
    };

    private async Task<byte[]> RvAsync(int id) =>
        (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id)).RowVersion;

    private async Task<ClienteReferenciaViewModel> ConRvAsync(int id, ClienteReferenciaViewModel modelo)
    {
        modelo.RowVersion = await RvAsync(id);
        return modelo;
    }

    // ── AC-02-1 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_PersisteReferenciaPendienteConAuditoria()
    {
        var cliente = await SeedClienteAsync();

        var (ok, error, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        Assert.True(ok, error);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(cliente.Id, bd.ClienteId);
        Assert.Equal("Juan Perez", bd.Nombre);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);
        Assert.NotEqual(default, bd.CreatedAt);
        Assert.Equal("3414440001", bd.TelefonoNormalizado);
    }

    [Fact]
    public async Task Editar_ActualizaCamposYSellaUpdatedAt()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var edicion = Modelo("Juan P. Perez", "341 444-0001", TipoReferenciaCliente.Personal);
        edicion.Relacion = "Primo";
        edicion.Email = "juan@example.com";
        var (ok, error) = await _service.EditarAsync(cliente.Id, id!.Value, await ConRvAsync(id.Value, edicion), "operador");

        Assert.True(ok, error);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("Juan P. Perez", bd.Nombre);
        Assert.Equal("Primo", bd.Relacion);
        Assert.Equal("juan@example.com", bd.Email);
        Assert.NotNull(bd.UpdatedAt);
    }

    [Fact]
    public async Task Editar_CambiandoElTelefono_ReiniciaLaVerificacion()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");
        await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, EstadoVerificacionReferencia.Verificada, null, "operador", await RvAsync(id.Value));

        await _service.EditarAsync(cliente.Id, id.Value, await ConRvAsync(id.Value, Modelo(telefono: "341 444-0099")), "operador");

        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.Null(bd.FechaVerificacion);
        Assert.Null(bd.VerificadaPor);
    }

    [Fact]
    public async Task DarDeBaja_EsLogica_ConservaElRegistroYSaleDelListado()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, error) = await _service.DarDeBajaAsync(cliente.Id, id!.Value, "operador", await RvAsync(id.Value));

        Assert.True(ok, error);
        Assert.Empty(await _service.ListarPorClienteAsync(cliente.Id));
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.True(bd.IsDeleted);
        Assert.NotNull(bd.UpdatedAt);
    }

    [Fact]
    public async Task DarDeBaja_LiberaElTelefonoParaUnaNuevaReferencia()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");
        await _service.DarDeBajaAsync(cliente.Id, id!.Value, "operador", await RvAsync(id.Value));

        var (ok, error, _) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        Assert.True(ok, error);
    }

    [Theory]
    [InlineData(EstadoVerificacionReferencia.Verificada)]
    [InlineData(EstadoVerificacionReferencia.NoLocalizada)]
    [InlineData(EstadoVerificacionReferencia.Rechazada)]
    public async Task CambiarVerificacion_RegistraFechaYResponsable(EstadoVerificacionReferencia estado)
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, error) = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, estado, "Atendió su madre", "verificador", await RvAsync(id.Value));

        Assert.True(ok, error);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(estado, bd.EstadoVerificacion);
        Assert.NotNull(bd.FechaVerificacion);
        Assert.Equal("verificador", bd.VerificadaPor);
        Assert.EndsWith("] Atendió su madre", bd.Observaciones);
    }

    [Fact]
    public async Task CambiarVerificacion_AEstadoNoDefinido_SeRechaza()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, _) = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, (EstadoVerificacionReferencia)99, null, "operador", await RvAsync(id.Value));

        Assert.False(ok);
    }

    [Fact]
    public async Task Crear_EmpresaSoloSeGuardaEnLaboralOComercial()
    {
        var cliente = await SeedClienteAsync();
        var personal = Modelo("Personal", "341 444-0010");
        personal.Empresa = "Ignorada SA";
        var laboral = Modelo("Laboral", "341 444-0011", TipoReferenciaCliente.Laboral);
        laboral.Empresa = "Acme SA";

        var (_, _, idP) = await _service.CrearAsync(cliente.Id, personal, "operador");
        var (_, _, idL) = await _service.CrearAsync(cliente.Id, laboral, "operador");

        Assert.Null((await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == idP)).Empresa);
        Assert.Equal("Acme SA", (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == idL)).Empresa);
    }

    // ── AC-02-2 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "341 444-0001")]
    [InlineData("   ", "341 444-0001")]
    [InlineData("Juan", "")]
    [InlineData("Juan", "   ")]
    public async Task Crear_SinNombreOSinTelefono_SeRechaza(string nombre, string telefono)
    {
        var cliente = await SeedClienteAsync();

        var (ok, error, _) = await _service.CrearAsync(cliente.Id, Modelo(nombre, telefono), "operador");

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(await _context.ClienteReferencias.ToListAsync());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abc-defg-hij")]
    [InlineData("1234567890123456789")]
    public async Task Crear_ConTelefonoInvalido_SeRechaza(string telefono)
    {
        var cliente = await SeedClienteAsync();

        var (ok, _, _) = await _service.CrearAsync(cliente.Id, Modelo(telefono: telefono), "operador");

        Assert.False(ok);
    }

    [Fact]
    public async Task Crear_Duplicada_MismoClienteTipoYTelefonoNormalizado_SeRechaza()
    {
        var cliente = await SeedClienteAsync();
        await _service.CrearAsync(cliente.Id, Modelo(telefono: "341 444-0001"), "operador");

        // Mismo número escrito distinto: debe detectarse por la forma normalizada.
        var (ok, error, _) = await _service.CrearAsync(cliente.Id, Modelo("Otro Nombre", "(0341) 444 0001"), "operador");

        Assert.False(ok);
        Assert.Contains("Ya existe", error);
        Assert.Single(await _context.ClienteReferencias.ToListAsync());
    }

    [Fact]
    public async Task Crear_MismoTelefonoConOtroTipo_EsValida()
    {
        var cliente = await SeedClienteAsync();
        await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, error, _) = await _service.CrearAsync(cliente.Id, Modelo(tipo: TipoReferenciaCliente.Comercial), "operador");

        Assert.True(ok, error);
    }

    [Fact]
    public async Task Editar_AOtroTelefonoYaUsadoPorOtraReferencia_SeRechaza()
    {
        var cliente = await SeedClienteAsync();
        await _service.CrearAsync(cliente.Id, Modelo("Uno", "341 444-0001"), "operador");
        var (_, _, id2) = await _service.CrearAsync(cliente.Id, Modelo("Dos", "341 444-0002"), "operador");

        var (ok, _) = await _service.EditarAsync(cliente.Id, id2!.Value, await ConRvAsync(id2.Value, Modelo("Dos", "341 444-0001")), "operador");

        Assert.False(ok);
    }

    [Fact]
    public async Task Editar_SinCambiarNada_NoSeTomaPorDuplicadaDeSiMisma()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, error) = await _service.EditarAsync(cliente.Id, id!.Value, await ConRvAsync(id.Value, Modelo()), "operador");

        Assert.True(ok, error);
    }

    [Theory]
    [InlineData("341 555-1000")]        // teléfono
    [InlineData("0341 5552000")]        // alternativo (celular), con 0 de acceso
    [InlineData("+54 9 341 5553000")]   // laboral, con prefijo internacional y 9
    public async Task Crear_ConTelefonoDelPropioCliente_SeRechaza(string telefono)
    {
        var cliente = await SeedClienteAsync();

        var (ok, error, _) = await _service.CrearAsync(cliente.Id, Modelo(telefono: telefono), "operador");

        Assert.False(ok);
        Assert.Contains("propio cliente", error);
    }

    [Fact]
    public async Task Editar_ATelefonoDelPropioCliente_SeRechaza()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, _) = await _service.EditarAsync(cliente.Id, id!.Value, await ConRvAsync(id.Value, Modelo(telefono: "341 555-1000")), "operador");

        Assert.False(ok);
    }

    [Fact]
    public async Task Crear_ParaClienteInexistenteOEliminado_SeRechaza()
    {
        var eliminado = await SeedClienteAsync();
        eliminado.IsDeleted = true;
        await _context.SaveChangesAsync();

        Assert.False((await _service.CrearAsync(999_999, Modelo(), "operador")).Ok);
        Assert.False((await _service.CrearAsync(eliminado.Id, Modelo(), "operador")).Ok);
    }

    [Fact]
    public async Task Crear_ConCamposQueSuperanElLargo_SeRechazaSinTruncar()
    {
        var cliente = await SeedClienteAsync();
        var modelo = Modelo(new string('N', 151));

        var (ok, _, _) = await _service.CrearAsync(cliente.Id, modelo, "operador");

        Assert.False(ok);
    }

    // ── AC-02-5 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Aislamiento_ListarSoloDevuelveLasReferenciasDelCliente()
    {
        var a = await SeedClienteAsync();
        var b = await SeedClienteAsync();
        await _service.CrearAsync(a.Id, Modelo("De A", "341 444-0001"), "operador");
        await _service.CrearAsync(b.Id, Modelo("De B", "341 444-0001"), "operador"); // mismo teléfono en otro cliente: válido

        var deA = await _service.ListarPorClienteAsync(a.Id);
        var deB = await _service.ListarPorClienteAsync(b.Id);

        Assert.Equal("De A", Assert.Single(deA).Nombre);
        Assert.Equal("De B", Assert.Single(deB).Nombre);
    }

    [Fact]
    public async Task Aislamiento_NoSePuedeEditarVerificarNiDarDeBajaUnaReferenciaDeOtroCliente()
    {
        var a = await SeedClienteAsync();
        var b = await SeedClienteAsync();
        var (_, _, idDeA) = await _service.CrearAsync(a.Id, Modelo("De A"), "operador");

        var editar = await _service.EditarAsync(b.Id, idDeA!.Value, await ConRvAsync(idDeA.Value, Modelo("Hackeada", "341 444-0777")), "operador");
        var verificar = await _service.CambiarVerificacionAsync(b.Id, idDeA.Value, EstadoVerificacionReferencia.Verificada, null, "operador", await RvAsync(idDeA.Value));
        var baja = await _service.DarDeBajaAsync(b.Id, idDeA.Value, "operador", await RvAsync(idDeA.Value));

        Assert.False(editar.Ok);
        Assert.False(verificar.Ok);
        Assert.False(baja.Ok);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == idDeA);
        Assert.Equal("De A", bd.Nombre);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);
    }

    // ── Hallazgos de la revisión independiente ───────────────────────────

    [Fact]
    public async Task Listar_ExcluyeLasReferenciasDeUnClienteEliminado()
    {
        var cliente = await SeedClienteAsync();
        await _service.CrearAsync(cliente.Id, Modelo(), "operador");
        Assert.Single(await _service.ListarPorClienteAsync(cliente.Id));

        cliente = await _context.Clientes.SingleAsync(c => c.Id == cliente.Id);
        cliente.IsDeleted = true;
        await _context.SaveChangesAsync();

        Assert.Empty(await _service.ListarPorClienteAsync(cliente.Id));
    }

    [Fact]
    public async Task Editar_SinRowVersion_SeRechazaYNoCambiaNada()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");

        var (ok, error) = await _service.EditarAsync(cliente.Id, id!.Value, Modelo("Cambiada", "341 444-0001"), "operador");

        Assert.False(ok);
        Assert.Contains("versión", error);
        Assert.Equal("Juan Perez", (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id)).Nombre);
    }

    [Fact]
    public async Task Editar_ConRowVersionVieja_SeRechazaPorConcurrencia()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");
        var modelo = Modelo("Cambiada", "341 444-0001");
        modelo.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 };

        var (ok, error) = await _service.EditarAsync(cliente.Id, id!.Value, modelo, "operador");

        Assert.False(ok);
        Assert.Contains("otro usuario", error);
        Assert.Equal("Juan Perez", (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id)).Nombre);
    }

    [Fact]
    public async Task Verificar_YBaja_ExigenRowVersionVigente()
    {
        var cliente = await SeedClienteAsync();
        var (_, _, id) = await _service.CrearAsync(cliente.Id, Modelo(), "operador");
        var vieja = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 };

        var sinVersionV = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, EstadoVerificacionReferencia.Verificada, null, "operador", null);
        var viejaV = await _service.CambiarVerificacionAsync(cliente.Id, id.Value, EstadoVerificacionReferencia.Verificada, null, "operador", vieja);
        var sinVersionB = await _service.DarDeBajaAsync(cliente.Id, id.Value, "operador", Array.Empty<byte>());
        var viejaB = await _service.DarDeBajaAsync(cliente.Id, id.Value, "operador", vieja);

        Assert.False(sinVersionV.Ok);
        Assert.False(viejaV.Ok);
        Assert.Contains("otro usuario", viejaV.Error);
        Assert.False(sinVersionB.Ok);
        Assert.False(viejaB.Ok);
        Assert.Contains("otro usuario", viejaB.Error);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);
    }

    [Fact]
    public async Task CambiarVerificacion_ConservaLasObservacionesGeneralesYAgregaLaNota()
    {
        var cliente = await SeedClienteAsync();
        var modelo = Modelo();
        modelo.Observaciones = "Prefiere que lo llamen a la tarde";
        var (_, _, id) = await _service.CrearAsync(cliente.Id, modelo, "operador");

        var (ok, error) = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, EstadoVerificacionReferencia.Verificada, "Confirmó datos", "verificador", await RvAsync(id.Value));

        Assert.True(ok, error);
        var obs = (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id)).Observaciones!;
        Assert.StartsWith("Prefiere que lo llamen a la tarde\n[Verificación ", obs);
        Assert.EndsWith("] Confirmó datos", obs);
    }

    [Fact]
    public async Task CambiarVerificacion_SinNota_NoTocaLasObservaciones()
    {
        var cliente = await SeedClienteAsync();
        var modelo = Modelo();
        modelo.Observaciones = "Nota original";
        var (_, _, id) = await _service.CrearAsync(cliente.Id, modelo, "operador");

        var (ok, _) = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, EstadoVerificacionReferencia.NoLocalizada, "  ", "verificador", await RvAsync(id.Value));

        Assert.True(ok);
        Assert.Equal("Nota original", (await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id)).Observaciones);
    }

    [Fact]
    public async Task CambiarVerificacion_SiLaNotaAcumuladaSuperaElMaximo_SeRechazaSinPerderNada()
    {
        var cliente = await SeedClienteAsync();
        var modelo = Modelo();
        modelo.Observaciones = new string('x', 990);
        var (_, _, id) = await _service.CrearAsync(cliente.Id, modelo, "operador");

        var (ok, error) = await _service.CambiarVerificacionAsync(cliente.Id, id!.Value, EstadoVerificacionReferencia.Verificada, "una nota larga", "verificador", await RvAsync(id.Value));

        Assert.False(ok);
        Assert.Contains("1000", error);
        var bd = await _context.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(new string('x', 990), bd.Observaciones);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
    }

    // ── Normalizador ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("341 444-0001", "3414440001")]
    [InlineData("(0341) 444-0001", "3414440001")]
    [InlineData("+54 341 444 0001", "3414440001")]
    [InlineData("+54 9 341 444 0001", "3414440001")]
    [InlineData("0054 9 341 444 0001", "3414440001")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TelefonoNormalizador_ReduceALaFormaCanonica(string? entrada, string esperado) =>
        Assert.Equal(esperado, TelefonoNormalizador.Normalizar(entrada));
}
