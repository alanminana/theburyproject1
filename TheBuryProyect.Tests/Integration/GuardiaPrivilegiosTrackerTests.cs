using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 56-A, punto 10: tras una operación rechazada o fallida, un SaveChanges posterior del MISMO
/// contexto no puede persistir modificaciones que la guardia revirtió (el tracker se descarta).
/// SQLite en memoria (corre siempre); el equivalente contra SQL Server está en
/// <c>UltimoSuperAdminConcurrenciaSqlServerTests</c>.
/// </summary>
public class GuardiaPrivilegiosTrackerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private AppDbContext _context = null!;
    private string _userId = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = Nuevo();
        await _context.Database.EnsureCreatedAsync();

        _userId = Guid.NewGuid().ToString();
        _context.Users.Add(new ApplicationUser
        {
            Id = _userId, UserName = "u", NormalizedUserName = "U", Email = "u@t.test", NormalizedEmail = "U@T.TEST",
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), Activo = true,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AppDbContext Nuevo() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private GuardiaPrivilegiosService Guardia(AppDbContext context) =>
        new(context, new Actor(), new HttpContextAccessor(), new AuditoriaNula(), NullLogger<GuardiaPrivilegiosService>.Instance);

    private async Task<bool> ActivoEnBdAsync()
    {
        await using var otro = Nuevo();
        return await otro.Users.AsNoTracking().Where(u => u.Id == _userId).Select(u => u.Activo).SingleAsync();
    }

    [Fact]
    public async Task OperacionQueDevuelveFalse_RevierteYUnSaveChangesPosteriorNoPersisteLoRevertido()
    {
        var resultado = await Guardia(_context).EjecutarConservandoSuperAdminAsync(async () =>
        {
            var user = await _context.Users.SingleAsync(u => u.Id == _userId);
            user.Activo = false;
            await _context.SaveChangesAsync();
            user.Nombre = "modificado y sin guardar";
            return false;
        });

        Assert.True(resultado.Permitido);
        Assert.Empty(_context.ChangeTracker.Entries());

        await _context.SaveChangesAsync();

        Assert.True(await ActivoEnBdAsync());
    }

    [Fact]
    public async Task OperacionQueLanzaExcepcion_RevierteYDescartaElTracker()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Guardia(_context).EjecutarConservandoSuperAdminAsync(async () =>
        {
            var user = await _context.Users.SingleAsync(u => u.Id == _userId);
            user.Activo = false;
            await _context.SaveChangesAsync();
            user.Nombre = "pendiente";
            throw new InvalidOperationException("fallo inyectado");
        }));

        Assert.Empty(_context.ChangeTracker.Entries());
        await _context.SaveChangesAsync();

        Assert.True(await ActivoEnBdAsync());
    }

    private sealed class Actor : ICurrentUserService
    {
        public string GetUsername() => "actor";
        public string GetUserId() => "actor-id";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => role == Roles.SuperAdmin;
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    private sealed class AuditoriaNula : ISeguridadAuditoriaService
    {
        public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null) => Task.CompletedTask;

        public Task<AuditoriaQueryResult> ConsultarEventosAsync(
            string? usuario = null, string? modulo = null, string? accion = null,
            DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null) =>
            Task.FromResult(new AuditoriaQueryResult());
    }
}
