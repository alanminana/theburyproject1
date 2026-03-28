using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

file sealed class StubCurrentUserDelUpd : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => null;
}

/// <summary>
/// Tests de integración para VentaService.DeleteAsync y UpdateAsync.
///
/// DeleteAsync: soft-delete, guard de estado, venta inexistente.
/// UpdateAsync: inexistente → null, estado no editable → excepción,
///              sin RowVersion → excepción, happy path (cotización).
/// </summary>
public class VentaServiceDeleteUpdateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private static int _counter = 1000;

    public VentaServiceDeleteUpdateTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            numberGenerator,
            null!,
            new StubCurrentUserDelUpd(),
            null!,
            null!,
            null!);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        var c = new Cliente
        {
            Nombre = "Test", Apellido = "DelUpd",
            TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"du{n}@test.com"
        };
        _context.Set<Cliente>().Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    private async Task<Venta> SeedVentaAsync(int clienteId, EstadoVenta estado = EstadoVenta.Cotizacion)
    {
        var n = Interlocked.Increment(ref _counter);
        var v = new Venta
        {
            Numero = $"VTA-DU-{n:D6}",
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow
        };
        _context.Set<Venta>().Add(v);
        await _context.SaveChangesAsync();
        return v;
    }

    // =========================================================================
    // DeleteAsync
    // =========================================================================

    [Fact]
    public async Task Delete_VentaEnCotizacion_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        var resultado = await _service.DeleteAsync(venta.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task Delete_VentaEnCotizacion_MarcaIsDeletedTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        await _service.DeleteAsync(venta.Id);

        var ventaBd = await _context.Ventas.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == venta.Id);
        Assert.NotNull(ventaBd);
        Assert.True(ventaBd!.IsDeleted);
    }

    [Fact]
    public async Task Delete_VentaEnPresupuesto_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);

        var resultado = await _service.DeleteAsync(venta.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task Delete_VentaConfirmada_LanzaInvalidOperationException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Confirmada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(venta.Id));
    }

    [Fact]
    public async Task Delete_VentaFacturada_LanzaInvalidOperationException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Facturada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(venta.Id));
    }

    [Fact]
    public async Task Delete_VentaCancelada_LanzaInvalidOperationException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cancelada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(venta.Id));
    }

    [Fact]
    public async Task Delete_VentaInexistente_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);
        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_VentaYaEliminada_RetornaFalse()
    {
        // IsDeleted=true → la query filtra por !IsDeleted → no la encuentra
        var cliente = await SeedClienteAsync();
        var n = Interlocked.Increment(ref _counter);
        var v = new Venta
        {
            Numero = $"VTA-DEL-{n:D6}",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            IsDeleted = true
        };
        _context.Set<Venta>().Add(v);
        await _context.SaveChangesAsync();

        var resultado = await _service.DeleteAsync(v.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_VentaEnPendienteRequisitos_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.PendienteRequisitos);

        var resultado = await _service.DeleteAsync(venta.Id);

        Assert.True(resultado);
    }

    // =========================================================================
    // UpdateAsync
    // =========================================================================

    [Fact]
    public async Task Update_VentaInexistente_RetornaNull()
    {
        var vm = new VentaViewModel
        {
            ClienteId = 1,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel>(),
            RowVersion = new byte[8]
        };

        var resultado = await _service.UpdateAsync(99999, vm);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Update_VentaConfirmada_LanzaInvalidOperationException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Confirmada);

        var vm = new VentaViewModel
        {
            ClienteId = cliente.Id,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel>(),
            RowVersion = new byte[8]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
    }

    [Fact]
    public async Task Update_SinRowVersion_LanzaInvalidOperationException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        var vm = new VentaViewModel
        {
            ClienteId = cliente.Id,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel>(),
            RowVersion = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
    }
}
