using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

public sealed class PrefijosNumeracionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrefijoNumeracionService _service;

    public PrefijosNumeracionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new PrefijoNumeracionService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Listar_SinConfiguracion_DevuelveTodosLosTiposConSuPrefijoPorDefecto()
    {
        var lista = await _service.ListarAsync();

        Assert.Equal(Enum.GetValues<TipoPrefijoNumeracion>().Length, lista.Count);
        Assert.All(lista, p => Assert.Equal(p.PrefijoPorDefecto, p.Prefijo));
        Assert.Equal("COT", lista.Single(p => p.Tipo == TipoPrefijoNumeracion.Cotizacion).Prefijo);
        Assert.Equal("VTA", lista.Single(p => p.Tipo == TipoPrefijoNumeracion.Venta).Prefijo);
        Assert.Equal("FA-A", lista.Single(p => p.Tipo == TipoPrefijoNumeracion.FacturaA).Prefijo);
    }

    [Fact]
    public async Task Guardar_NormalizaAMayusculas_YVacioVuelveAlDefecto()
    {
        await _service.GuardarAsync([new(TipoPrefijoNumeracion.Venta, " pedido ")]);
        Assert.Equal("PEDIDO", (await _service.ListarAsync()).Single(p => p.Tipo == TipoPrefijoNumeracion.Venta).Prefijo);

        await _service.GuardarAsync([new(TipoPrefijoNumeracion.Venta, "  ")]);
        Assert.Equal("VTA", (await _service.ListarAsync()).Single(p => p.Tipo == TipoPrefijoNumeracion.Venta).Prefijo);
    }

    [Theory]
    [InlineData("AB CD")]
    [InlineData("-AB")]
    [InlineData("AB-")]
    [InlineData("A--B")]
    [InlineData("A/B")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("PEDIDOS")]  // 7: "PEDIDOS-202610-000001" no entra en Ventas.Numero (20)
    public async Task Guardar_FormatoInvalido_SeRechazaSinGuardarNada(string invalido)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GuardarAsync(
        [
            new(TipoPrefijoNumeracion.Cotizacion, "PRES"),
            new(TipoPrefijoNumeracion.Venta, invalido)
        ]));

        Assert.Contains("Venta", ex.Message);
        _context.ChangeTracker.Clear();
        Assert.Equal("COT", (await _service.ListarAsync()).Single(p => p.Tipo == TipoPrefijoNumeracion.Cotizacion).Prefijo);
    }

    [Fact]
    public async Task Guardar_PrefijoLargo_SePermiteDondeLaColumnaEsDe50()
    {
        await _service.GuardarAsync([new(TipoPrefijoNumeracion.OrdenCompra, "COMPRAS-OK")]);

        Assert.Equal("COMPRAS-OK", (await _service.ListarAsync()).Single(p => p.Tipo == TipoPrefijoNumeracion.OrdenCompra).Prefijo);
    }

    [Fact]
    public void LongitudMaxima_HaceQueElNumeroCompletoEntreEnLaColumna()
    {
        // Ventas, Facturas, Devoluciones, Notas de crédito y Garantías guardan el número en 20 caracteres.
        foreach (var tipo in Enum.GetValues<TipoPrefijoNumeracion>().Where(t => t.LongitudMaxima() == 6))
            Assert.True(tipo.Ejemplo(new string('X', 6)).Length <= 20, tipo.ToString());
    }

    [Fact]
    public async Task Guardar_TipoRepetido_SeRechaza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GuardarAsync(
        [
            new(TipoPrefijoNumeracion.Venta, "A"),
            new(TipoPrefijoNumeracion.Venta, "B")
        ]));
    }

    [Fact]
    public async Task NumeroDeVenta_UsaElPrefijoConfigurado_SoloParaLosNumerosNuevos()
    {
        var generador = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);
        var anterior = await generador.GenerarNumeroAsync(EstadoVenta.Confirmada);
        Assert.StartsWith("VTA-", anterior);

        await _service.GuardarAsync([
            new(TipoPrefijoNumeracion.Venta, "pedido"),
            new(TipoPrefijoNumeracion.Cotizacion, "pres")]);

        Assert.StartsWith("PEDIDO-", await generador.GenerarNumeroAsync(EstadoVenta.Confirmada));
        Assert.StartsWith("PRES-", await generador.GenerarNumeroAsync(EstadoVenta.Cotizacion));
        Assert.StartsWith("VTA-", anterior);
    }

    [Fact]
    public async Task NumeroDeFactura_UsaElPrefijoConfiguradoPorTipo_ConGuiones()
    {
        await _service.GuardarAsync([new(TipoPrefijoNumeracion.FacturaB, "fac-b")]);
        var generador = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);

        var numero = await generador.GenerarNumeroFacturaAsync(TipoFactura.B);
        var otra = await generador.GenerarNumeroFacturaAsync(TipoFactura.A);

        Assert.StartsWith("FAC-B-", numero);
        Assert.StartsWith("FA-A-", otra);
    }

    [Fact]
    public async Task NumeroDeOrdenDeCompra_UsaElPrefijoConfigurado()
    {
        await _service.GuardarAsync([new(TipoPrefijoNumeracion.OrdenCompra, "compra")]);

        var numero = await new OrdenCompraService(_context, NullLogger<OrdenCompraService>.Instance, null!, null!).GenerarNumeroOrdenAsync();

        Assert.Equal($"COMPRA-{DateTime.UtcNow.Year}-0001", numero);
    }
}
