using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración de VentaEnvioService: máquina de estados fail-closed y que el
/// cambio de estado logístico nunca toca Total/Subtotal/IVA/caja/crédito de la venta.
/// </summary>
public class VentaEnvioServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaEnvioService _service;
    private static int _counter = 5000;

    public VentaEnvioServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new VentaEnvioService(_context, NullLogger<VentaEnvioService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Venta> SeedVentaConEnvioAsync(EstadoEnvio estado = EstadoEnvio.Pendiente)
    {
        var n = Interlocked.Increment(ref _counter);
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Envio",
            TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"),
            Email = $"envio{n}@test.com"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = $"VTA-{n:D6}",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 1000m,
            IVA = 210m,
            Total = 1210m
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var envio = new VentaEnvio
        {
            VentaId = venta.Id,
            Estado = estado,
            Destinatario = "Juan Pérez",
            Domicilio = "Calle Falsa 123"
        };
        _context.VentaEnvios.Add(envio);
        await _context.SaveChangesAsync();

        return venta;
    }

    // -------------------------------------------------------------------------
    // Transiciones válidas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstadoAsync_PendienteAPreparando_Aplica()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Preparando, null, "tester");

        Assert.True(resultado.Exitoso);
        Assert.Equal(EstadoEnvio.Preparando, resultado.Envio!.Estado);
    }

    [Fact]
    public async Task CambiarEstadoAsync_ADespachado_SellaFechaDespacho()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Preparando);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Despachado, null, "tester");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.Envio!.FechaDespacho);
    }

    [Fact]
    public async Task CambiarEstadoAsync_AEntregado_SellaFechaEntregaRealYFechaEntregaDeLaVenta()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Despachado);
        Assert.Null(venta.FechaEntrega);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Entregado, null, "tester");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.Envio!.FechaEntregaReal);

        var ventaActualizada = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        Assert.NotNull(ventaActualizada.FechaEntrega);
    }

    [Fact]
    public async Task CambiarEstadoAsync_FallidoDesdeEnCaminoConMotivo_Aplica()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.EnCamino);

        var resultado = await _service.CambiarEstadoAsync(
            venta.Id, EstadoEnvio.Fallido, "No había nadie en el domicilio", "tester");

        Assert.True(resultado.Exitoso);
        Assert.Equal("No había nadie en el domicilio", resultado.Envio!.MotivoNoEntrega);
    }

    [Fact]
    public async Task CambiarEstadoAsync_FallidoAPreparando_PermiteReintento()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Fallido);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Preparando, null, "tester");

        Assert.True(resultado.Exitoso);
        Assert.Equal(EstadoEnvio.Preparando, resultado.Envio!.Estado);
    }

    // -------------------------------------------------------------------------
    // Transiciones inválidas (fail-closed): rechaza y no escribe nada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstadoAsync_SaltoDePendienteAEntregado_Rechaza()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Entregado, null, "tester");

        Assert.False(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Pendiente, envio!.Estado);
    }

    [Fact]
    public async Task CambiarEstadoAsync_FallidoSinMotivo_Rechaza()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Despachado);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Fallido, "   ", "tester");

        Assert.False(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Despachado, envio!.Estado);
        Assert.Null(envio.MotivoNoEntrega);
    }

    [Theory]
    [InlineData(EstadoEnvio.Entregado)]
    [InlineData(EstadoEnvio.Cancelado)]
    public async Task CambiarEstadoAsync_DesdeEstadoTerminal_Rechaza(EstadoEnvio terminal)
    {
        var venta = await SeedVentaConEnvioAsync(terminal);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Preparando, null, "tester");

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task CambiarEstadoAsync_VentaSinEnvio_Rechaza()
    {
        var n = Interlocked.Increment(ref _counter);
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "SinEnvio",
            TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"),
            Email = $"sinenvio{n}@test.com"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta { Numero = $"VTA-{n:D6}", ClienteId = cliente.Id, Estado = EstadoVenta.Confirmada };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Preparando, null, "tester");

        Assert.False(resultado.Exitoso);
        Assert.Contains("no tiene envío", resultado.Error);
    }

    // -------------------------------------------------------------------------
    // El costo de envío es un concepto separado (se suma sólo en Venta.TotalACobrar, ver
    // VentaEnvioTotalACobrarTests): cambiar el estado logístico nunca toca los totales de la venta.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstadoAsync_NoModificaTotalesDeLaVenta()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Despachado);
        var totalAntes = venta.Total;
        var subtotalAntes = venta.Subtotal;
        var ivaAntes = venta.IVA;

        await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Entregado, null, "tester");

        var ventaActualizada = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(totalAntes, ventaActualizada.Total);
        Assert.Equal(subtotalAntes, ventaActualizada.Subtotal);
        Assert.Equal(ivaAntes, ventaActualizada.IVA);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada.Estado); // no toca EstadoVenta
    }

    // -------------------------------------------------------------------------
    // GetPendientesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendientesAsync_ExcluyeEntregadoYCancelado()
    {
        await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);
        await SeedVentaConEnvioAsync(EstadoEnvio.Despachado);
        await SeedVentaConEnvioAsync(EstadoEnvio.Entregado);
        await SeedVentaConEnvioAsync(EstadoEnvio.Cancelado);

        var pendientes = await _service.GetPendientesAsync();

        Assert.Equal(2, pendientes.Count);
        Assert.All(pendientes, e => Assert.True(e.Estado is EstadoEnvio.Pendiente or EstadoEnvio.Despachado));
    }

    // -------------------------------------------------------------------------
    // EsTransicionValida (usado también para poblar el <select> del modal)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(EstadoEnvio.Pendiente, EstadoEnvio.Preparando, true)]
    [InlineData(EstadoEnvio.Pendiente, EstadoEnvio.Despachado, false)]
    [InlineData(EstadoEnvio.Despachado, EstadoEnvio.Entregado, true)]
    [InlineData(EstadoEnvio.Entregado, EstadoEnvio.Preparando, false)]
    [InlineData(EstadoEnvio.Pendiente, EstadoEnvio.Pendiente, false)]
    public void EsTransicionValida_ReflejaLaMaquinaDeEstados(EstadoEnvio actual, EstadoEnvio nuevo, bool esperado)
    {
        Assert.Equal(esperado, _service.EsTransicionValida(actual, nuevo));
    }
}
