using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Hubs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para NotificacionService.
/// Cubren CrearNotificacionAsync (persistencia, icono por tipo),
/// ObtenerNotificacionesUsuarioAsync (filtro usuario, soloNoLeidas, limite),
/// ObtenerCantidadNoLeidasAsync, ObtenerResumenNotificacionesAsync,
/// MarcarComoLeidaAsync (happy path, RowVersion vacío, ya leída no lanza),
/// MarcarTodasComoLeidasAsync (bulk update),
/// EliminarNotificacionAsync (soft delete, RowVersion vacío),
/// LimpiarNotificacionesAntiguasAsync (elimina leídas antiguas, no toca recientes).
/// </summary>
public class NotificacionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly NotificacionService _service;

    public NotificacionServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var userStore = new NoOpUserStoreNotif();
        var userManager = new UserManager<ApplicationUser>(
            userStore,
            null!, null!, null!, null!, null!, null!, null!, null!);

        _service = new NotificacionService(
            _context,
            userManager,
            NullLogger<NotificacionService>.Instance,
            new NoOpHubContextNotif());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private async Task<Notificacion> SeedNotificacionAsync(
        string usuario = "user1",
        TipoNotificacion tipo = TipoNotificacion.SistemaError,
        bool leida = false,
        DateTime? fecha = null)
    {
        var n = new Notificacion
        {
            UsuarioDestino = usuario,
            Tipo = tipo,
            Prioridad = PrioridadNotificacion.Media,
            Titulo = "Título test",
            Mensaje = "Mensaje test",
            IconoCss = "bi-bell",
            Leida = leida,
            FechaNotificacion = fecha ?? DateTime.UtcNow
        };
        _context.Notificaciones.Add(n);
        await _context.SaveChangesAsync();
        await _context.Entry(n).ReloadAsync();
        return n;
    }

    // -------------------------------------------------------------------------
    // CrearNotificacionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearNotificacion_DatosValidos_Persiste()
    {
        var model = new CrearNotificacionViewModel
        {
            UsuarioDestino = "ana",
            Tipo = TipoNotificacion.StockBajo,
            Prioridad = PrioridadNotificacion.Alta,
            Titulo = "Stock bajo",
            Mensaje = "Producto X casi sin stock"
        };

        var result = await _service.CrearNotificacionAsync(model);

        Assert.True(result.Id > 0);
        Assert.Equal("ana", result.UsuarioDestino);
        Assert.False(result.Leida);
    }

    [Fact]
    public async Task CrearNotificacion_StockBajo_AsignaIconoCorrecto()
    {
        var model = new CrearNotificacionViewModel
        {
            UsuarioDestino = "user1",
            Tipo = TipoNotificacion.StockBajo,
            Prioridad = PrioridadNotificacion.Media,
            Titulo = "T", Mensaje = "M"
        };

        var result = await _service.CrearNotificacionAsync(model);

        Assert.Equal("bi-box-seam text-warning", result.IconoCss);
    }

    [Fact]
    public async Task CrearNotificacion_CreditoAprobado_AsignaIconoCorrecto()
    {
        var model = new CrearNotificacionViewModel
        {
            UsuarioDestino = "user1",
            Tipo = TipoNotificacion.CreditoAprobado,
            Prioridad = PrioridadNotificacion.Media,
            Titulo = "T", Mensaje = "M"
        };

        var result = await _service.CrearNotificacionAsync(model);

        Assert.Equal("bi-check-circle text-success", result.IconoCss);
    }

    // -------------------------------------------------------------------------
    // ObtenerNotificacionesUsuarioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerNotificaciones_FiltraByUsuario()
    {
        await SeedNotificacionAsync(usuario: "ana");
        await SeedNotificacionAsync(usuario: "bob");

        var result = await _service.ObtenerNotificacionesUsuarioAsync("ana");

        Assert.Single(result);
        Assert.Equal("ana", result[0].Titulo == "Título test" ? "ana" : "");
    }

    [Fact]
    public async Task ObtenerNotificaciones_SoloNoLeidas_FiltranLeidas()
    {
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user1", leida: true);

        var result = await _service.ObtenerNotificacionesUsuarioAsync("user1", soloNoLeidas: true);

        Assert.Single(result);
        Assert.False(result[0].Leida);
    }

    [Fact]
    public async Task ObtenerNotificaciones_LimiteRespetado()
    {
        for (int i = 0; i < 5; i++)
            await SeedNotificacionAsync(usuario: "user1");

        var result = await _service.ObtenerNotificacionesUsuarioAsync("user1", limite: 3);

        Assert.Equal(3, result.Count);
    }

    // -------------------------------------------------------------------------
    // ObtenerCantidadNoLeidasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerCantidadNoLeidas_CuentaSoloNoLeidas()
    {
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user1", leida: true);

        var count = await _service.ObtenerCantidadNoLeidasAsync("user1");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ObtenerCantidadNoLeidas_OtroUsuarioNoAfecta()
    {
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user2", leida: false);

        var count = await _service.ObtenerCantidadNoLeidasAsync("user1");

        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // ObtenerResumenNotificacionesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerResumen_RetornaTotalesCorrectos()
    {
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user1", leida: true);

        var resumen = await _service.ObtenerResumenNotificacionesAsync("user1");

        Assert.Equal(2, resumen.TotalNotificaciones);
        Assert.Equal(1, resumen.TotalNoLeidas);
    }

    // -------------------------------------------------------------------------
    // MarcarComoLeidaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarComoLeida_RowVersionVacio_LanzaExcepcion()
    {
        var n = await SeedNotificacionAsync(usuario: "user1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MarcarComoLeidaAsync(n.Id, "user1", null));
    }

    [Fact]
    public async Task MarcarComoLeida_HappyPath_MarcaLeida()
    {
        var n = await SeedNotificacionAsync(usuario: "user1", leida: false);

        await _service.MarcarComoLeidaAsync(n.Id, "user1", n.RowVersion);

        _context.ChangeTracker.Clear();
        var bd = await _context.Notificaciones.FindAsync(n.Id);
        Assert.True(bd!.Leida);
        Assert.NotNull(bd.FechaLeida);
    }

    [Fact]
    public async Task MarcarComoLeida_OtroUsuario_NoLanzaYNoMarca()
    {
        var n = await SeedNotificacionAsync(usuario: "user1", leida: false);

        // usuario diferente — no lanza pero tampoco modifica
        await _service.MarcarComoLeidaAsync(n.Id, "user2", n.RowVersion);

        _context.ChangeTracker.Clear();
        var bd = await _context.Notificaciones.FindAsync(n.Id);
        Assert.False(bd!.Leida);
    }

    // -------------------------------------------------------------------------
    // MarcarTodasComoLeidasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarTodasComoLeidas_MarcaTodasDelUsuario()
    {
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user1", leida: false);
        await SeedNotificacionAsync(usuario: "user2", leida: false); // otro usuario

        await _service.MarcarTodasComoLeidasAsync("user1");

        _context.ChangeTracker.Clear();
        var noLeidasUser1 = await _context.Notificaciones
            .CountAsync(n => n.UsuarioDestino == "user1" && !n.Leida);
        var noLeidasUser2 = await _context.Notificaciones
            .CountAsync(n => n.UsuarioDestino == "user2" && !n.Leida);

        Assert.Equal(0, noLeidasUser1);
        Assert.Equal(1, noLeidasUser2); // user2 no afectado
    }

    // -------------------------------------------------------------------------
    // EliminarNotificacionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EliminarNotificacion_RowVersionVacio_LanzaExcepcion()
    {
        var n = await SeedNotificacionAsync(usuario: "user1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.EliminarNotificacionAsync(n.Id, "user1", null));
    }

    [Fact]
    public async Task EliminarNotificacion_HappyPath_SoftDelete()
    {
        var n = await SeedNotificacionAsync(usuario: "user1");

        await _service.EliminarNotificacionAsync(n.Id, "user1", n.RowVersion);

        _context.ChangeTracker.Clear();
        var bd = await _context.Notificaciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == n.Id);
        Assert.True(bd!.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // LimpiarNotificacionesAntiguasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LimpiarAntiguos_EliminaLeidasAnterioresAFechaLimite()
    {
        // Leída y antigua → debe eliminarse
        await SeedNotificacionAsync(leida: true, fecha: DateTime.UtcNow.AddDays(-40));
        // No leída y antigua → no debe eliminarse
        await SeedNotificacionAsync(leida: false, fecha: DateTime.UtcNow.AddDays(-40));
        // Leída y reciente → no debe eliminarse
        await SeedNotificacionAsync(leida: true, fecha: DateTime.UtcNow.AddDays(-5));

        await _service.LimpiarNotificacionesAntiguasAsync(diasAntiguedad: 30);

        _context.ChangeTracker.Clear();
        var deletedCount = await _context.Notificaciones
            .IgnoreQueryFilters()
            .CountAsync(n => n.IsDeleted);
        Assert.Equal(1, deletedCount);
    }

    [Fact]
    public async Task LimpiarAntiguos_NadaParaLimpiar_NoLanza()
    {
        await SeedNotificacionAsync(leida: false, fecha: DateTime.UtcNow);

        await _service.LimpiarNotificacionesAntiguasAsync(); // no debe lanzar
    }
}

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------

internal sealed class NoOpUserStoreNotif :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>
{
    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id ?? "");
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) => Task.CompletedTask;
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) => Task.CompletedTask;
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(false);
    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken ct) => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class NoOpHubContextNotif : IHubContext<NotificacionesHub>
{
    public IHubClients Clients => new NoOpHubClients();
    public IGroupManager Groups => null!;
}

internal sealed class NoOpHubClients : IHubClients
{
    public IClientProxy All => new NoOpClientProxy();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new NoOpClientProxy();
    public IClientProxy Client(string connectionId) => new NoOpClientProxy();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new NoOpClientProxy();
    public IClientProxy Group(string groupName) => new NoOpClientProxy();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new NoOpClientProxy();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new NoOpClientProxy();
    public IClientProxy User(string userId) => new NoOpClientProxy();
    public IClientProxy Users(IReadOnlyList<string> userIds) => new NoOpClientProxy();
}

internal sealed class NoOpClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
