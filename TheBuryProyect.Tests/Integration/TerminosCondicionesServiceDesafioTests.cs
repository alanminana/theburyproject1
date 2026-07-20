using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para la vía alternativa "desafiar a los dioses" de
/// TerminosCondicionesService.ActivarDesafioALosDiosesAsync.
/// </summary>
public class TerminosCondicionesServiceDesafioTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly TerminosCondicionesService _service;

    public TerminosCondicionesServiceDesafioTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new TerminosCondicionesService(
            _context,
            new StubCurrentUserServiceTerminos(),
            NullLogger<TerminosCondicionesService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task SeedUsuarioAsync(string id, string userName)
    {
        _context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.com",
            NormalizedEmail = $"{userName}@test.com".ToUpperInvariant(),
            Activo = true,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Activar_UsuarioNuevo_ActivaFlagYRegistraFechaUtc()
    {
        await SeedUsuarioAsync("user-javier", "javier");

        var antes = DateTime.UtcNow;
        var resultado = await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");
        var despues = DateTime.UtcNow;

        Assert.True(resultado);

        var fila = await _context.TerminosCondicionesAceptaciones
            .FirstOrDefaultAsync(a => a.UsuarioId == "user-javier");
        Assert.NotNull(fila);
        Assert.True(fila!.DesafioALosDiosesActivado);
        Assert.NotNull(fila.DesafioALosDiosesActivadoEnUtc);
        Assert.InRange(fila.DesafioALosDiosesActivadoEnUtc!.Value, antes.AddSeconds(-1), despues.AddSeconds(1));
    }

    [Fact]
    public async Task Activar_PermiteContinuar_MarcaVersionComoAceptada()
    {
        await SeedUsuarioAsync("user-javier", "javier");

        await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");

        Assert.True(await _service.UsuarioAceptoVersionActualAsync("user-javier"));
    }

    [Fact]
    public async Task Activar_LlamadoDosVeces_EsIdempotente_NoDuplicaRegistro()
    {
        await SeedUsuarioAsync("user-javier", "javier");

        var r1 = await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");
        var r2 = await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");

        Assert.True(r1);
        Assert.True(r2);

        var cantidad = await _context.TerminosCondicionesAceptaciones
            .CountAsync(a => a.UsuarioId == "user-javier");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Activar_NoAfectaFlagDeOtroUsuario()
    {
        await SeedUsuarioAsync("user-javier", "javier");
        await SeedUsuarioAsync("user-otro", "otro");

        await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");

        Assert.False(await _service.UsuarioAceptoVersionActualAsync("user-otro"));
        var filaOtro = await _context.TerminosCondicionesAceptaciones
            .FirstOrDefaultAsync(a => a.UsuarioId == "user-otro");
        Assert.Null(filaOtro);
    }

    [Fact]
    public async Task Activar_AceptacionNormalPrevia_NoDuplicaNiActivaFlag()
    {
        await SeedUsuarioAsync("user-javier", "javier");
        await _service.RegistrarAceptacionAsync("user-javier", "javier", "Javier Martinez");

        var resultado = await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");

        Assert.True(resultado); // idempotente: ya estaba aceptado por la vía normal
        var cantidad = await _context.TerminosCondicionesAceptaciones.CountAsync(a => a.UsuarioId == "user-javier");
        Assert.Equal(1, cantidad);
        var fila = await _context.TerminosCondicionesAceptaciones.FirstAsync(a => a.UsuarioId == "user-javier");
        Assert.False(fila.DesafioALosDiosesActivado);
    }

    [Fact]
    public async Task Activar_FallaPersistencia_DevuelveFalseSinLanzarYSinDejarRegistro()
    {
        await SeedUsuarioAsync("user-javier", "javier");

        _connection.Close(); // fuerza que cualquier acceso a datos falle

        var resultado = await _service.ActivarDesafioALosDiosesAsync("user-javier", "javier");

        Assert.False(resultado);
    }
}

file sealed class StubCurrentUserServiceTerminos : ICurrentUserService
{
    public string GetUsername() => "javier";
    public string GetUserId() => "user-javier";
    public string? GetEmail() => "javier@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
    public string? GetUserAgent() => "TestAgent/1.0";
}
