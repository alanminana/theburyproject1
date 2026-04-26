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

/// <summary>
/// Tests de integración para los flujos de autorización de VentaService:
/// SolicitarAutorizacionAsync, AutorizarVentaAsync, RechazarVentaAsync
/// y ValidarStockAsync.
/// No requieren caja abierta ni configuración de precios — sólo contexto + entidades.
/// </summary>
public class VentaServiceAutorizacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;

    public VentaServiceAutorizacionTests()
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

        var validator = new VentaValidator();
        var numberGenerator = new VentaNumberGenerator(
            _context,
            NullLogger<VentaNumberGenerator>.Instance);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService — no alcanzado en estos tests
            null!,   // IMovimientoStockService
            new FinancialCalculationService(),
            validator,
            numberGenerator,
            null!,   // IPrecioService
            new StubCurrentUserServiceVenta(),
            null!,   // IValidacionVentaService
            null!,   // ICajaService
            null!,   // ICreditoDisponibleService
            new StubContratoVentaCreditoService());
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
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "test@test.com"
        };
        _context.Set<Cliente>().Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Producto> SeedProductoAsync(decimal stockActual = 100m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        _context.Set<Categoria>().Add(categoria);
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 50m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            Activo = true
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId,
        EstadoVenta estado = EstadoVenta.Cotizacion,
        EstadoAutorizacionVenta estadoAutorizacion = EstadoAutorizacionVenta.NoRequiere,
        bool requiereAutorizacion = false)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            EstadoAutorizacion = estadoAutorizacion,
            RequiereAutorizacion = requiereAutorizacion,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>()
        };
        _context.Set<Venta>().Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<Venta> SeedVentaConDetalleAsync(
        int clienteId,
        int productoId,
        int cantidad,
        EstadoAutorizacionVenta estadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            EstadoAutorizacion = estadoAutorizacion,
            RequiereAutorizacion = true,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = 50m,
                    Descuento = 0m,
                    Subtotal = cantidad * 50m
                }
            }
        };
        _context.Set<Venta>().Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    // -------------------------------------------------------------------------
    // SolicitarAutorizacionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SolicitarAutorizacion_VentaExistente_MarcaPendienteAutorizacion()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        var resultado = await _service.SolicitarAutorizacionAsync(
            venta.Id, "supervisor1", "Precio fuera de rango");

        Assert.True(resultado);
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.True(ventaBd.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, ventaBd.EstadoAutorizacion);
        Assert.Equal("supervisor1", ventaBd.UsuarioSolicita);
    }

    [Fact]
    public async Task SolicitarAutorizacion_VentaNoExiste_RetornaFalse()
    {
        var resultado = await _service.SolicitarAutorizacionAsync(
            99999, "supervisor1", "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // AutorizarVentaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AutorizarVenta_PendienteAutorizacion_MarcaAutorizada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id,
            estadoAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion,
            requiereAutorizacion: true);

        var resultado = await _service.AutorizarVentaAsync(
            venta.Id, "gerente1", "Aprobado por excepción");

        Assert.True(resultado);
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, ventaBd.EstadoAutorizacion);
        Assert.Equal("gerente1", ventaBd.UsuarioAutoriza);
        Assert.NotNull(ventaBd.FechaAutorizacion);
        Assert.Equal("Aprobado por excepción", ventaBd.MotivoAutorizacion);
    }

    [Fact]
    public async Task AutorizarVenta_SinMotivo_LanzaArgumentException()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id,
            estadoAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AutorizarVentaAsync(venta.Id, "gerente1", "  "));
    }

    [Fact]
    public async Task AutorizarVenta_EstadoNoEsPendiente_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        // Venta con estado NoRequiere — no está pendiente
        var venta = await SeedVentaAsync(cliente.Id,
            estadoAutorizacion: EstadoAutorizacionVenta.NoRequiere);

        // Validator lanza InvalidOperationException → ObtenerVentaPendienteAutorizacionAsync retorna false
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AutorizarVentaAsync(venta.Id, "gerente1", "Motivo"));
    }

    [Fact]
    public async Task AutorizarVenta_VentaNoExiste_RetornaFalse()
    {
        var resultado = await _service.AutorizarVentaAsync(
            99999, "gerente1", "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // RechazarVentaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RechazarVenta_PendienteAutorizacion_MarcaRechazada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id,
            estadoAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion,
            requiereAutorizacion: true);

        var resultado = await _service.RechazarVentaAsync(
            venta.Id, "gerente1", "Descuento excesivo");

        Assert.True(resultado);
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoAutorizacionVenta.Rechazada, ventaBd.EstadoAutorizacion);
        Assert.Equal("gerente1", ventaBd.UsuarioAutoriza);
        Assert.Equal("Descuento excesivo", ventaBd.MotivoRechazo);
    }

    [Fact]
    public async Task RechazarVenta_VentaNoExiste_RetornaFalse()
    {
        var resultado = await _service.RechazarVentaAsync(
            99999, "gerente1", "Motivo");
        Assert.False(resultado);
    }

    [Fact]
    public async Task RechazarVenta_EstadoNoEsPendiente_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id,
            estadoAutorizacion: EstadoAutorizacionVenta.Autorizada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarVentaAsync(venta.Id, "gerente1", "Motivo"));
    }

    // -------------------------------------------------------------------------
    // ValidarStockAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarStock_StockSuficiente_ReturnsTrue()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 50m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 5,
            estadoAutorizacion: EstadoAutorizacionVenta.NoRequiere);

        var resultado = await _service.ValidarStockAsync(venta.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ValidarStock_StockInsuficiente_ReturnsFalse()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 2m); // solo 2 en stock
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 10,
            estadoAutorizacion: EstadoAutorizacionVenta.NoRequiere);

        var resultado = await _service.ValidarStockAsync(venta.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task ValidarStock_VentaNoExiste_ReturnsFalse()
    {
        var resultado = await _service.ValidarStockAsync(99999);
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // CancelarVentaAsync — estado Cotizacion (sin devolver stock)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_EstadoCotizacion_CambiaEstadoACancelada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, estado: EstadoVenta.Cotizacion);

        var resultado = await _service.CancelarVentaAsync(venta.Id, "Solicitud del cliente");

        Assert.True(resultado);
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);
        Assert.Equal("Solicitud del cliente", ventaBd.MotivoCancelacion);
        Assert.NotNull(ventaBd.FechaCancelacion);
    }

    [Fact]
    public async Task CancelarVenta_YaCancelada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, estado: EstadoVenta.Cancelada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CancelarVentaAsync(venta.Id, "Doble cancelación"));
    }

    [Fact]
    public async Task CancelarVenta_VentaNoExiste_RetornaFalse()
    {
        var resultado = await _service.CancelarVentaAsync(99999, "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // RegistrarExcepcionDocumentalAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarExcepcionDocumental_DatosValidos_GuardaTrazaYRetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        var resultado = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, "auditor1", "Documento vencido presentado");

        Assert.True(resultado);
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal("auditor1", ventaBd.UsuarioAutoriza);
        Assert.NotNull(ventaBd.FechaAutorizacion);
        Assert.Contains("EXCEPCION_DOC", ventaBd.MotivoAutorizacion);
        Assert.Contains("auditor1", ventaBd.MotivoAutorizacion);
    }

    [Fact]
    public async Task RegistrarExcepcionDocumental_UsuarioVacio_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        var resultado = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, "  ", "Motivo");

        Assert.False(resultado);
    }

    [Fact]
    public async Task RegistrarExcepcionDocumental_MotivoVacio_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        var resultado = await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, "auditor1", "");

        Assert.False(resultado);
    }

    [Fact]
    public async Task RegistrarExcepcionDocumental_VentaNoExiste_RetornaFalse()
    {
        var resultado = await _service.RegistrarExcepcionDocumentalAsync(
            99999, "auditor1", "Motivo");

        Assert.False(resultado);
    }

    [Fact]
    public async Task RegistrarExcepcionDocumental_SegundaExcepcion_AcumulaTraza()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, "auditor1", "Primera excepción");
        await _service.RegistrarExcepcionDocumentalAsync(
            venta.Id, "auditor2", "Segunda excepción");

        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        // El motivo debe contener ambas trazas concatenadas
        Assert.Contains("Primera excepción", ventaBd.MotivoAutorizacion);
        Assert.Contains("Segunda excepción", ventaBd.MotivoAutorizacion);
    }

    // -------------------------------------------------------------------------
    // CancelarVentaAsync — estados sin devolución de stock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_EstadoPresupuesto_CambiaEstadoACancelada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, estado: EstadoVenta.Presupuesto);

        var resultado = await _service.CancelarVentaAsync(venta.Id, "Cambió de idea");

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);
    }

    [Fact]
    public async Task CancelarVenta_EstadoPendienteFinanciacion_CambiaEstadoACancelada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, estado: EstadoVenta.PendienteFinanciacion);

        var resultado = await _service.CancelarVentaAsync(venta.Id, "Crédito rechazado");

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);
        Assert.Equal("Crédito rechazado", ventaBd.MotivoCancelacion);
        Assert.NotNull(ventaBd.FechaCancelacion);
    }

    [Fact]
    public async Task CancelarVenta_EstadoPendienteRequisitos_CambiaEstadoACancelada()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, estado: EstadoVenta.PendienteRequisitos);

        var resultado = await _service.CancelarVentaAsync(venta.Id, "Documentación incompleta");

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Set<Venta>().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);
    }
}

// ---------------------------------------------------------------------------
// Stub de ICurrentUserService para estos tests
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceVenta : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
