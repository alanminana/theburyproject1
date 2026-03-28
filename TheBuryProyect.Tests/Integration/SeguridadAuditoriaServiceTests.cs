using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para SeguridadAuditoriaService.
/// Cubren RegistrarEventoAsync (persistencia, modulo vacío usa "Seguridad", trim detalle,
/// detalle null queda null) y ConsultarEventosAsync (base vacía, sin filtros retorna todo,
/// filtro por usuario, por módulo, por acción, por rango de fechas desde/hasta,
/// dropdowns de valores distintos).
/// </summary>
public class SeguridadAuditoriaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly SeguridadAuditoriaService _service;

    public SeguridadAuditoriaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new SeguridadAuditoriaService(_context, new StubCurrentUserServiceAuditoria(), NullLogger<SeguridadAuditoriaService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private async Task SeedEventoAsync(
        string usuario = "user1", string modulo = "Seguridad",
        string accion = "Login", string entidad = "Usuario",
        string? detalle = null, DateTime? fecha = null)
    {
        var evento = new SeguridadEventoAuditoria
        {
            UsuarioNombre = usuario,
            UsuarioId = "uid-" + usuario,
            Modulo = modulo,
            Accion = accion,
            Entidad = entidad,
            Detalle = detalle,
            FechaEvento = fecha ?? DateTime.UtcNow,
            DireccionIp = "127.0.0.1"
        };
        _context.SeguridadEventosAuditoria.Add(evento);
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // RegistrarEventoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarEvento_DatosValidos_Persiste()
    {
        await _service.RegistrarEventoAsync("Ventas", "Crear", "Venta", "detalle test");

        var evento = await _context.SeguridadEventosAuditoria
            .FirstOrDefaultAsync(e => e.Modulo == "Ventas" && e.Accion == "Crear");
        Assert.NotNull(evento);
        Assert.Equal("Venta", evento!.Entidad);
        Assert.Equal("detalle test", evento.Detalle);
    }

    [Fact]
    public async Task RegistrarEvento_ModuloVacio_UsaSeguridad()
    {
        await _service.RegistrarEventoAsync("", "Login", "Usuario");

        var evento = await _context.SeguridadEventosAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(evento);
        Assert.Equal("Seguridad", evento!.Modulo);
    }

    [Fact]
    public async Task RegistrarEvento_ModuloWhitespace_UsaSeguridad()
    {
        await _service.RegistrarEventoAsync("   ", "Login", "Usuario");

        var evento = await _context.SeguridadEventosAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(evento);
        Assert.Equal("Seguridad", evento!.Modulo);
    }

    [Fact]
    public async Task RegistrarEvento_DetalleNull_QuedaNull()
    {
        await _service.RegistrarEventoAsync("Ventas", "Eliminar", "Producto", null);

        var evento = await _context.SeguridadEventosAuditoria
            .FirstOrDefaultAsync(e => e.Accion == "Eliminar");
        Assert.NotNull(evento);
        Assert.Null(evento!.Detalle);
    }

    [Fact]
    public async Task RegistrarEvento_DetalleConEspacios_SeHaceTrim()
    {
        await _service.RegistrarEventoAsync("Ventas", "Actualizar", "Producto", "  valor  ");

        var evento = await _context.SeguridadEventosAuditoria
            .FirstOrDefaultAsync(e => e.Accion == "Actualizar");
        Assert.NotNull(evento);
        Assert.Equal("valor", evento!.Detalle);
    }

    [Fact]
    public async Task RegistrarEvento_UsaUsernameDelCurrentUser()
    {
        await _service.RegistrarEventoAsync("Seguridad", "Login", "Usuario");

        var evento = await _context.SeguridadEventosAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(evento);
        Assert.Equal("testuser", evento!.UsuarioNombre);
    }

    // -------------------------------------------------------------------------
    // ConsultarEventosAsync — base vacía
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEventos_BaseVacia_RetornaVacio()
    {
        var resultado = await _service.ConsultarEventosAsync();

        Assert.Empty(resultado.Registros);
        Assert.Empty(resultado.Usuarios);
        Assert.Empty(resultado.Modulos);
        Assert.Empty(resultado.Acciones);
    }

    // -------------------------------------------------------------------------
    // ConsultarEventosAsync — sin filtros retorna todos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEventos_SinFiltros_RetornaTodos()
    {
        await SeedEventoAsync(usuario: "user1", modulo: "Ventas");
        await SeedEventoAsync(usuario: "user2", modulo: "Creditos");

        var resultado = await _service.ConsultarEventosAsync();

        Assert.Equal(2, resultado.Registros.Count);
    }

    // -------------------------------------------------------------------------
    // ConsultarEventosAsync — filtros individuales
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEventos_FiltroUsuario_RetornaSoloEseUsuario()
    {
        await SeedEventoAsync(usuario: "ana");
        await SeedEventoAsync(usuario: "bob");

        var resultado = await _service.ConsultarEventosAsync(usuario: "ana");

        Assert.Single(resultado.Registros);
        Assert.Equal("ana", resultado.Registros[0].Usuario);
    }

    [Fact]
    public async Task ConsultarEventos_FiltroModulo_RetornaSoloEseModulo()
    {
        await SeedEventoAsync(modulo: "Ventas");
        await SeedEventoAsync(modulo: "Creditos");

        var resultado = await _service.ConsultarEventosAsync(modulo: "Ventas");

        Assert.Single(resultado.Registros);
        Assert.Equal("Ventas", resultado.Registros[0].Modulo);
    }

    [Fact]
    public async Task ConsultarEventos_FiltroAccion_RetornaSoloEsaAccion()
    {
        await SeedEventoAsync(accion: "Login");
        await SeedEventoAsync(accion: "Logout");

        var resultado = await _service.ConsultarEventosAsync(accion: "Login");

        Assert.Single(resultado.Registros);
        Assert.Equal("Login", resultado.Registros[0].Accion);
    }

    // -------------------------------------------------------------------------
    // ConsultarEventosAsync — filtros de fecha
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEventos_FiltroDesdeFecha_ExcluyeAnteriores()
    {
        var ayer = DateTime.UtcNow.AddDays(-1);
        var semanaAtras = DateTime.UtcNow.AddDays(-7);

        await SeedEventoAsync(fecha: semanaAtras); // debe quedar excluido
        await SeedEventoAsync(fecha: ayer);        // debe incluirse

        var desde = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var resultado = await _service.ConsultarEventosAsync(desde: desde);

        Assert.Single(resultado.Registros);
    }

    [Fact]
    public async Task ConsultarEventos_FiltroHastaFecha_ExcluyePosteriores()
    {
        var ayer = DateTime.UtcNow.AddDays(-1);
        var manana = DateTime.UtcNow.AddDays(1);

        await SeedEventoAsync(fecha: ayer);    // debe incluirse
        await SeedEventoAsync(fecha: manana);  // debe quedar excluido

        var hasta = DateOnly.FromDateTime(DateTime.UtcNow);
        var resultado = await _service.ConsultarEventosAsync(hasta: hasta);

        Assert.Single(resultado.Registros);
    }

    // -------------------------------------------------------------------------
    // ConsultarEventosAsync — dropdowns de valores distintos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEventos_Dropdowns_ContienenValoresDistintos()
    {
        await SeedEventoAsync(usuario: "ana", modulo: "Ventas", accion: "Crear");
        await SeedEventoAsync(usuario: "bob", modulo: "Creditos", accion: "Login");
        await SeedEventoAsync(usuario: "ana", modulo: "Ventas", accion: "Crear"); // duplicado

        var resultado = await _service.ConsultarEventosAsync();

        Assert.Equal(2, resultado.Usuarios.Count);   // ana, bob
        Assert.Equal(2, resultado.Modulos.Count);    // Ventas, Creditos
        Assert.Equal(2, resultado.Acciones.Count);   // Crear, Login
    }

    [Fact]
    public async Task ConsultarEventos_Dropdowns_EstaenOrdenAlfabetico()
    {
        await SeedEventoAsync(usuario: "zeta");
        await SeedEventoAsync(usuario: "alfa");

        var resultado = await _service.ConsultarEventosAsync();

        Assert.Equal("alfa", resultado.Usuarios[0]);
        Assert.Equal("zeta", resultado.Usuarios[1]);
    }
}

file sealed class StubCurrentUserServiceAuditoria : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
