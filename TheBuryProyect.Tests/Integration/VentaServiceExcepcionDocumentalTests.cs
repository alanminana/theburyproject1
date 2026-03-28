using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Validators;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para VentaService.RegistrarExcepcionDocumentalAsync.
///
/// El método no usa servicios externos — solo _context y _logger.
/// Se construye VentaService pasando null! en las dependencias no requeridas.
///
/// Contratos verificados:
/// - Inputs inválidos (usuario/motivo vacíos) → false sin tocar DB
/// - Venta inexistente → false
/// - Happy path: retorna true, persiste traza EXCEPCION_DOC|..., UsuarioAutoriza, FechaAutorizacion
/// - MotivoAutorizacion previo → se concatena separado por newline
/// - Motivo compuesto que excede 1000 chars → se trunca a la traza nueva
/// - Motivo previo existente, compuesto <= 1000 chars → se preservan ambos
/// </summary>
public class VentaServiceExcepcionDocumentalTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;

    private static int _counter = 300;

    public VentaServiceExcepcionDocumentalTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService — no se usa en RegistrarExcepcionDocumentalAsync
            null!,   // IMovimientoStockService
            null!,   // IFinancialCalculationService
            new VentaValidator(),
            null!,   // VentaNumberGenerator
            null!,   // IPrecioService
            null!,   // ICurrentUserService
            null!,   // IValidacionVentaService
            null!,   // ICajaService
            null!);  // ICreditoDisponibleService
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Venta> SeedVenta(string? motivoAutorizacionPrevio = null)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Excepcion",
            NumeroDocumento = $"5{suffix}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VX{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Total = 1_000m,
            MotivoAutorizacion = motivoAutorizacionPrevio,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    // -------------------------------------------------------------------------
    // Tests — guards de input
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarExcepcion_UsuarioVacio_RetornaFalse()
    {
        var venta = await SeedVenta();

        var result = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza: "", motivo: "Motivo válido");

        Assert.False(result);
    }

    [Fact]
    public async Task RegistrarExcepcion_MotivoVacio_RetornaFalse()
    {
        var venta = await SeedVenta();

        var result = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"admin", motivo: "");

        Assert.False(result);
    }

    [Fact]
    public async Task RegistrarExcepcion_MotivoSoloEspacios_RetornaFalse()
    {
        var venta = await SeedVenta();

        var result = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"admin", motivo: "   ");

        Assert.False(result);
    }

    [Fact]
    public async Task RegistrarExcepcion_VentaInexistente_RetornaFalse()
    {
        var result = await _service.RegistrarExcepcionDocumentalAsync(
            id: 99_999, usuarioAutoriza:"admin", motivo: "Motivo");

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Tests — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarExcepcion_HappyPath_RetornaTrue()
    {
        var venta = await SeedVenta();

        var result = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"supervisor", motivo: "Documentación pendiente por trámite");

        Assert.True(result);
    }

    [Fact]
    public async Task RegistrarExcepcion_HappyPath_PersisteTrazaConPrefijo()
    {
        var venta = await SeedVenta();

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"supervisor", motivo: "Trámite en curso");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.NotNull(ventaActualizada!.MotivoAutorizacion);
        Assert.StartsWith("EXCEPCION_DOC|", ventaActualizada.MotivoAutorizacion);
    }

    [Fact]
    public async Task RegistrarExcepcion_HappyPath_PersisteMotivoNormalizado()
    {
        var venta = await SeedVenta();
        const string motivo = "  Motivo con espacios  ";

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"supervisor", motivo: motivo);

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Contains("Motivo con espacios", ventaActualizada!.MotivoAutorizacion);
        Assert.DoesNotContain("  Motivo", ventaActualizada.MotivoAutorizacion); // sin espacios leading
    }

    [Fact]
    public async Task RegistrarExcepcion_HappyPath_PersistePersistirUsuarioYFecha()
    {
        var venta = await SeedVenta();
        var antes = DateTime.UtcNow;

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"auditor01", motivo: "Excepción aprobada");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Equal("auditor01", ventaActualizada!.UsuarioAutoriza);
        Assert.NotNull(ventaActualizada.FechaAutorizacion);
        Assert.True(ventaActualizada.FechaAutorizacion >= antes);
    }

    // -------------------------------------------------------------------------
    // Tests — concatenación de motivos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarExcepcion_SinMotivosPrevios_AsignaTrazaDirecta()
    {
        var venta = await SeedVenta(motivoAutorizacionPrevio: null);

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"sup", motivo: "Primera excepción");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        // Sin previo → solo la traza nueva, sin \n al inicio
        Assert.False(ventaActualizada!.MotivoAutorizacion!.StartsWith("\n"));
    }

    [Fact]
    public async Task RegistrarExcepcion_ConMotivoPrevio_ConcatenaSeparadoPorNewline()
    {
        const string motivoPrevio = "Nota anterior del evaluador";
        var venta = await SeedVenta(motivoAutorizacionPrevio: motivoPrevio);

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"sup", motivo: "Segunda excepción");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Contains(motivoPrevio, ventaActualizada!.MotivoAutorizacion);
        Assert.Contains("EXCEPCION_DOC|", ventaActualizada.MotivoAutorizacion);
        Assert.Contains("\n", ventaActualizada.MotivoAutorizacion);
    }

    [Fact]
    public async Task RegistrarExcepcion_CompuestoExcede1000Chars_GuardaSoloTrazaNueva()
    {
        // Rellena el campo existente con 980 chars → al concatenar supera 1000
        var motivoPrevioLargo = new string('X', 980);
        var venta = await SeedVenta(motivoAutorizacionPrevio: motivoPrevioLargo);

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, usuarioAutoriza:"sup", motivo: "Excepción que no cabe");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        // El compuesto excede 1000 → se descarta el previo y se guarda solo la traza nueva
        Assert.StartsWith("EXCEPCION_DOC|", ventaActualizada!.MotivoAutorizacion);
        Assert.DoesNotContain(motivoPrevioLargo, ventaActualizada.MotivoAutorizacion);
    }
}
