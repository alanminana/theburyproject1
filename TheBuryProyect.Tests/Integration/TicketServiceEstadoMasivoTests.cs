using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Cambio de estado masivo de tickets: si alguno de los seleccionados no admite la transición,
/// el mensaje identifica cuál (id + estados en su nombre visible) y no se modifica ninguno.
/// </summary>
public class TicketServiceEstadoMasivoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly TicketService _service;

    public TicketServiceEstadoMasivoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _service = new TicketService(
            _context, mapper, new StubFileStorageTicket(), new StubCurrentUserTicket(),
            NullLogger<TicketService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Ticket> SeedAsync(EstadoTicket estado)
    {
        var ticket = new Ticket
        {
            Titulo = "Ticket de prueba",
            Descripcion = "Descripción",
            Tipo = TipoTicket.ErrorFuncional,
            Estado = estado,
        };
        _context.Tickets.Add(ticket);
        await _context.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task CambiarEstadoMasivo_ConTransicionInvalida_NombraElTicketYNoModificaNinguno()
    {
        var pendiente = await SeedAsync(EstadoTicket.Pendiente);
        var cancelado = await SeedAsync(EstadoTicket.Cancelado);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CambiarEstadoMasivoAsync([pendiente.Id, cancelado.Id], EstadoTicket.EnCurso, null));

        Assert.Contains($"#{cancelado.Id}", ex.Message);
        Assert.Contains("Cancelado", ex.Message);
        Assert.Contains("En Curso", ex.Message);

        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoTicket.Pendiente, (await _context.Tickets.FindAsync(pendiente.Id))!.Estado);
        Assert.Equal(EstadoTicket.Cancelado, (await _context.Tickets.FindAsync(cancelado.Id))!.Estado);
    }

    [Fact]
    public async Task CambiarEstadoMasivo_ConTransicionesValidas_AplicaATodos()
    {
        var a = await SeedAsync(EstadoTicket.Pendiente);
        var b = await SeedAsync(EstadoTicket.Pendiente);

        var cantidad = await _service.CambiarEstadoMasivoAsync([a.Id, b.Id], EstadoTicket.EnCurso, null);

        Assert.Equal(2, cantidad);
        _context.ChangeTracker.Clear();
        Assert.All(_context.Tickets.ToList(), t => Assert.Equal(EstadoTicket.EnCurso, t.Estado));
    }
}

file sealed class StubFileStorageTicket : IFileStorageService
{
    public Task<string> SaveAsync(IFormFile archivo, string subFolder) => throw new NotImplementedException();
    public Task DeleteAsync(string rutaRelativa) => Task.CompletedTask;
}

file sealed class StubCurrentUserTicket : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}
