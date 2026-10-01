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
    public async Task CambiarEstadoAsync_PendienteAEntregado_PermiteEntregaDirecta()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Entregado, null, "tester");

        Assert.True(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Entregado, envio!.Estado);
        Assert.NotNull(envio.FechaEntregaReal);
    }

    [Fact]
    public async Task CambiarEstadoAsync_SaltoDePendienteADespachado_Rechaza()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Despachado, null, "tester");

        Assert.False(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Pendiente, envio!.Estado);
    }

    [Fact]
    public async Task CambiarEstadoAsync_FallidoAPendiente_PermiteReprogramar()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Fallido);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Pendiente, null, "tester");

        Assert.True(resultado.Exitoso);
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
    // Reprogramado (ticket #24)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(EstadoEnvio.Pendiente)]
    [InlineData(EstadoEnvio.Preparando)]
    [InlineData(EstadoEnvio.Despachado)]
    [InlineData(EstadoEnvio.EnCamino)]
    [InlineData(EstadoEnvio.Fallido)]
    [InlineData(EstadoEnvio.Reprogramado)]
    public async Task CambiarEstadoAsync_Reprogramar_DesdeCualquierEstadoNoTerminal_FijaLaNuevaFecha(EstadoEnvio origen)
    {
        var venta = await SeedVentaConEnvioAsync(origen);
        var nuevaFecha = DateTime.Today.AddDays(5);

        var resultado = await _service.CambiarEstadoAsync(
            venta.Id, EstadoEnvio.Reprogramado, "El cliente no estaba", "tester", nuevaFecha);

        Assert.True(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Reprogramado, envio!.Estado);
        Assert.Equal(nuevaFecha, envio.FechaProgramada);
        Assert.Contains("Reprogramado para el", envio.Observaciones);
        Assert.Contains("El cliente no estaba", envio.Observaciones);
    }

    [Fact]
    public async Task CambiarEstadoAsync_ReprogramarSinFecha_Rechaza()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Reprogramado, null, "tester");

        Assert.False(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Pendiente, envio!.Estado);
    }

    [Fact]
    public async Task CambiarEstadoAsync_ReprogramarConFechaPasada_Rechaza()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);

        var resultado = await _service.CambiarEstadoAsync(
            venta.Id, EstadoEnvio.Reprogramado, null, "tester", DateTime.Today.AddDays(-1));

        Assert.False(resultado.Exitoso);
        var envio = await _service.GetByVentaIdAsync(venta.Id);
        Assert.Equal(EstadoEnvio.Pendiente, envio!.Estado);
        Assert.Null(envio.FechaProgramada);
    }

    [Theory]
    [InlineData(EstadoEnvio.Entregado)]
    [InlineData(EstadoEnvio.Cancelado)]
    public async Task CambiarEstadoAsync_ReprogramarDesdeTerminal_Rechaza(EstadoEnvio terminal)
    {
        var venta = await SeedVentaConEnvioAsync(terminal);

        var resultado = await _service.CambiarEstadoAsync(
            venta.Id, EstadoEnvio.Reprogramado, null, "tester", DateTime.Today.AddDays(2));

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task CambiarEstadoAsync_ReprogramadoAEntregado_Aplica()
    {
        var venta = await SeedVentaConEnvioAsync(EstadoEnvio.Reprogramado);

        var resultado = await _service.CambiarEstadoAsync(venta.Id, EstadoEnvio.Entregado, null, "tester");

        Assert.True(resultado.Exitoso);
        Assert.NotNull((await _service.GetByVentaIdAsync(venta.Id))!.FechaEntregaReal);
    }

    [Fact]
    public async Task GetPendientesAsync_IncluyeReprogramado_YExcluyeVentaCancelada()
    {
        await SeedVentaConEnvioAsync(EstadoEnvio.Reprogramado);
        var cancelada = await SeedVentaConEnvioAsync(EstadoEnvio.Pendiente);
        var ventaDb = await _context.Ventas.FirstAsync(v => v.Id == cancelada.Id);
        ventaDb.Estado = EstadoVenta.Cancelada;
        await _context.SaveChangesAsync();

        var pendientes = await _service.GetPendientesAsync();

        Assert.Single(pendientes);
        Assert.Equal(EstadoEnvio.Reprogramado, pendientes[0].Estado);
    }

    // -------------------------------------------------------------------------
    // GetCerradosPorMesAsync: historial por mes calendario (ticket #24)
    // -------------------------------------------------------------------------

    private async Task<Venta> SeedEnvioCerradoAsync(EstadoEnvio estado, DateTime? entregaReal, DateTime fechaVenta, DateTime? updatedAt = null)
    {
        var venta = await SeedVentaConEnvioAsync(estado);
        var ventaDb = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        ventaDb.FechaVenta = fechaVenta;
        var envio = await _context.VentaEnvios.FirstAsync(e => e.VentaId == venta.Id);
        envio.FechaEntregaReal = entregaReal;
        envio.UpdatedAt = updatedAt;
        await _context.SaveChangesAsync();
        return venta;
    }

    [Fact]
    public async Task GetCerradosPorMesAsync_DevuelveElMesCompletoYNoElAnterior()
    {
        var hoy = DateTime.Today;
        var primeroDelMes = new DateTime(hoy.Year, hoy.Month, 1);
        var principioDeMes = await SeedEnvioCerradoAsync(EstadoEnvio.Entregado, primeroDelMes.AddHours(9), primeroDelMes.AddDays(-20));
        var finMesAnterior = await SeedEnvioCerradoAsync(EstadoEnvio.Entregado, primeroDelMes.AddMinutes(-30), primeroDelMes.AddDays(-25));
        await SeedEnvioCerradoAsync(EstadoEnvio.Pendiente, null, primeroDelMes.AddDays(1)); // no es cerrado

        var mesEnCurso = await _service.GetCerradosPorMesAsync(hoy.Year, hoy.Month);
        var anterior = primeroDelMes.AddMonths(-1);
        var mesAnterior = await _service.GetCerradosPorMesAsync(anterior.Year, anterior.Month);

        Assert.Equal(new[] { principioDeMes.Id }, mesEnCurso.Select(e => e.VentaId).ToArray());
        Assert.Equal(new[] { finMesAnterior.Id }, mesAnterior.Select(e => e.VentaId).ToArray());
        Assert.NotNull(mesEnCurso[0].Venta);
    }

    [Fact]
    public async Task GetCerradosPorMesAsync_IncluyeVentaCanceladaConEnvioSinCerrar()
    {
        var hoy = DateTime.Today;
        var primeroDelMes = new DateTime(hoy.Year, hoy.Month, 1);
        var venta = await SeedEnvioCerradoAsync(EstadoEnvio.Pendiente, null, primeroDelMes.AddDays(1));
        var ventaDb = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        ventaDb.Estado = EstadoVenta.Cancelada;
        ventaDb.FechaCancelacion = primeroDelMes.AddHours(12);
        await _context.SaveChangesAsync();

        var cerrados = await _service.GetCerradosPorMesAsync(hoy.Year, hoy.Month);

        Assert.Single(cerrados);
        Assert.Equal(venta.Id, cerrados[0].VentaId);
    }

    [Fact]
    public async Task GetCerradosPorMesAsync_EnvioCanceladoUsaLaFechaDeLaUltimaActualizacion()
    {
        var hoy = DateTime.Today;
        var primeroDelMes = new DateTime(hoy.Year, hoy.Month, 1);
        // Venta vieja (otro mes) cuyo envío se canceló este mes: figura en el mes de la cancelación.
        var venta = await SeedEnvioCerradoAsync(EstadoEnvio.Cancelado, null, primeroDelMes.AddMonths(-3), updatedAt: primeroDelMes.AddDays(2));

        var cerrados = await _service.GetCerradosPorMesAsync(hoy.Year, hoy.Month);

        Assert.Equal(new[] { venta.Id }, cerrados.Select(e => e.VentaId).ToArray());
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
    [InlineData(EstadoEnvio.Pendiente, EstadoEnvio.Reprogramado, true)]
    [InlineData(EstadoEnvio.Reprogramado, EstadoEnvio.Reprogramado, true)]
    [InlineData(EstadoEnvio.Reprogramado, EstadoEnvio.Fallido, false)]
    [InlineData(EstadoEnvio.Entregado, EstadoEnvio.Reprogramado, false)]
    [InlineData(EstadoEnvio.Cancelado, EstadoEnvio.Reprogramado, false)]
    public void EsTransicionValida_ReflejaLaMaquinaDeEstados(EstadoEnvio actual, EstadoEnvio nuevo, bool esperado)
    {
        Assert.Equal(esperado, _service.EsTransicionValida(actual, nuevo));
    }
}
