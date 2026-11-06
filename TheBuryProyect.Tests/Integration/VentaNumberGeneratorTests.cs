using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para VentaNumberGenerator.
/// Cubren GenerarNumeroAsync: primer número de la serie (VTA y COT),
/// incremento secuencial, formato con período yyyyMM, base vacía,
/// número con partes mal formadas no rompe el contador.
/// Cubren GenerarNumeroFacturaAsync: prefijos por TipoFactura,
/// primer número, incremento secuencial.
/// </summary>
public class VentaNumberGeneratorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaNumberGenerator _generator;

    public VentaNumberGeneratorTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _generator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Venta> SeedVentaConNumeroAsync(string numero)
    {
        // Necesitamos un cliente para la FK de Venta
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Gen",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = numero,
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.Today,
            Subtotal = 100m,
            Total = 100m
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<Factura> SeedFacturaConNumeroAsync(string numero)
    {
        var venta = await SeedVentaConNumeroAsync($"VTA-SEED-{Guid.NewGuid():N}");
        var factura = new Factura
        {
            VentaId = venta.Id,
            Numero = numero,
            Tipo = TipoFactura.B,
            FechaEmision = DateTime.UtcNow,
            Subtotal = 100m,
            Total = 100m
        };
        _context.Facturas.Add(factura);
        await _context.SaveChangesAsync();
        return factura;
    }

    private static string Periodo => DateTime.UtcNow.ToString("yyyyMM");

    // -------------------------------------------------------------------------
    // GenerarNumeroAsync — Venta (EstadoVenta.Confirmada)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarNumero_BaseVacia_RetornaPrimerNumero()
    {
        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Confirmada);

        // Esperado: VTA-{yyyyMM}-000001
        Assert.Equal($"VTA-{Periodo}-000001", numero);
    }

    [Fact]
    public async Task GenerarNumero_ConVentasExistentes_IncrementaSecuencialmente()
    {
        var periodoActual = Periodo;
        await SeedVentaConNumeroAsync($"VTA-{periodoActual}-000001");
        await SeedVentaConNumeroAsync($"VTA-{periodoActual}-000002");

        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Confirmada);

        Assert.Equal($"VTA-{periodoActual}-000003", numero);
    }

    [Fact]
    public async Task GenerarNumero_VentasDePeriodoDistinto_NoAfectaContador()
    {
        // Ventas de un período anterior — no deben interferir
        await SeedVentaConNumeroAsync("VTA-202401-000099");

        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Confirmada);

        Assert.Equal($"VTA-{Periodo}-000001", numero);
    }

    [Fact]
    public async Task GenerarNumero_FormatoContienePrefijoYPeriodo()
    {
        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Confirmada);

        Assert.StartsWith("VTA-", numero);
        Assert.Contains(Periodo, numero);
    }

    // -------------------------------------------------------------------------
    // GenerarNumeroAsync — Cotización (EstadoVenta.Cotizacion)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarNumero_Cotizacion_UsaPrefijoCorreecto()
    {
        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Cotizacion);

        Assert.StartsWith("COT-", numero);
        Assert.Equal($"COT-{Periodo}-000001", numero);
    }

    [Fact]
    public async Task GenerarNumero_CotizacionYVentaSonSeriesIndependientes()
    {
        var periodoActual = Periodo;
        await SeedVentaConNumeroAsync($"VTA-{periodoActual}-000001");
        await SeedVentaConNumeroAsync($"VTA-{periodoActual}-000002");

        // La serie COT debe empezar en 1 independientemente de las VTA
        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Cotizacion);

        Assert.Equal($"COT-{periodoActual}-000001", numero);
    }

    // -------------------------------------------------------------------------
    // GenerarNumeroAsync — número mal formado no rompe el generador
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarNumero_NumeroMalFormado_ArrancanDesde1()
    {
        var periodoActual = Periodo;
        // Número con prefijo correcto pero sin secuencia numérica parseable
        await SeedVentaConNumeroAsync($"VTA-{periodoActual}-ABCDEF");

        var numero = await _generator.GenerarNumeroAsync(EstadoVenta.Confirmada);

        // No puede parsear, así que "siguiente" queda en 1
        Assert.Equal($"VTA-{periodoActual}-000001", numero);
    }

    // -------------------------------------------------------------------------
    // GenerarNumeroFacturaAsync — prefijos por tipo
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(TipoFactura.A, "FA-A")]
    [InlineData(TipoFactura.B, "FA-B")]
    [InlineData(TipoFactura.C, "FA-C")]
    [InlineData(TipoFactura.NotaCredito, "NC")]
    [InlineData(TipoFactura.NotaDebito, "ND")]
    public async Task GenerarNumeroFactura_UsaPrefijoCorrectoPorTipo(TipoFactura tipo, string prefijoEsperado)
    {
        var numero = await _generator.GenerarNumeroFacturaAsync(tipo);

        Assert.StartsWith(prefijoEsperado + "-", numero);
        Assert.Contains(Periodo, numero);
    }

    [Fact]
    public async Task GenerarNumeroFactura_BaseVacia_RetornaPrimerNumero()
    {
        var numero = await _generator.GenerarNumeroFacturaAsync(TipoFactura.B);

        Assert.Equal($"FA-B-{Periodo}-000001", numero);
    }

    [Fact]
    public async Task GenerarNumeroFactura_BaseVaciaConcreto_IncrementaDesde1()
    {
        // Verifica que con la base vacía el primer número siempre es 000001
        var numero = await _generator.GenerarNumeroFacturaAsync(TipoFactura.A);

        Assert.Equal($"FA-A-{Periodo}-000001", numero);
    }

    [Fact]
    public async Task GenerarNumeroFactura_ConFacturasExistentes_IncrementaSecuencialmenteCorrectamente()
    {
        // Verifica que el parser usa el último segmento (no partes[2]) para prefijos compuestos como FA-B
        var periodoActual = Periodo;
        await SeedFacturaConNumeroAsync($"FA-B-{periodoActual}-000001");
        await SeedFacturaConNumeroAsync($"FA-B-{periodoActual}-000005");

        var numero = await _generator.GenerarNumeroFacturaAsync(TipoFactura.B);

        Assert.Equal($"FA-B-{periodoActual}-000006", numero);
    }

    [Fact]
    public async Task GenerarNumeroFactura_TiposDistintosSeriesIndependientes()
    {
        var periodoActual = Periodo;
        await SeedFacturaConNumeroAsync($"FA-A-{periodoActual}-000003");

        // FA-B debe empezar en 1 independientemente de FA-A
        var numero = await _generator.GenerarNumeroFacturaAsync(TipoFactura.B);

        Assert.Equal($"FA-B-{periodoActual}-000001", numero);
    }
    // ---------------------------------------------------------------------
    // H-01: renumeracion COT- -> VTA- con el COT- conservado como referencia
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AsegurarNumeroDeVenta_VentaCot_SeRenumeraAVtaYConservaElOrigen()
    {
        var venta = await SeedVentaConNumeroAsync($"COT-{Periodo}-000007");
        venta.Estado = EstadoVenta.Cotizacion;

        var renumero = await _generator.AsegurarNumeroDeVentaAsync(venta);

        Assert.True(renumero);
        Assert.Equal($"COT-{Periodo}-000007", venta.NumeroOrigen);
        Assert.Equal($"VTA-{Periodo}-000001", venta.Numero);
    }

    [Fact]
    public async Task AsegurarNumeroDeVenta_EsIdempotente_NoRenumeraDosVeces()
    {
        var venta = await SeedVentaConNumeroAsync($"COT-{Periodo}-000007");
        venta.Estado = EstadoVenta.Cotizacion;
        Assert.True(await _generator.AsegurarNumeroDeVentaAsync(venta));
        var numero = venta.Numero;
        await _context.SaveChangesAsync();

        Assert.False(await _generator.AsegurarNumeroDeVentaAsync(venta));
        Assert.Equal(numero, venta.Numero);
        Assert.Equal($"COT-{Periodo}-000007", venta.NumeroOrigen);
    }

    [Fact]
    public async Task AsegurarNumeroDeVenta_VentaQueYaNacioVta_NoSeToca()
    {
        var venta = await SeedVentaConNumeroAsync($"VTA-{Periodo}-000003");

        Assert.False(await _generator.AsegurarNumeroDeVentaAsync(venta));
        Assert.Equal($"VTA-{Periodo}-000003", venta.Numero);
        Assert.Null(venta.NumeroOrigen);
    }

    [Fact]
    public async Task AsegurarNumeroDeVenta_PrefijosConfiguradosIguales_NoRenumera()
    {
        _context.PrefijosNumeracion.Add(new PrefijoNumeracion { Tipo = TipoPrefijoNumeracion.Venta, Prefijo = "COT" });
        await _context.SaveChangesAsync();
        var venta = await SeedVentaConNumeroAsync($"COT-{Periodo}-000007");

        Assert.False(await _generator.AsegurarNumeroDeVentaAsync(venta));
        Assert.Equal($"COT-{Periodo}-000007", venta.Numero);
        Assert.Null(venta.NumeroOrigen);
    }

    [Fact]
    public async Task AsegurarNumeroDeVenta_UsaElPrefijoDeVentaConfigurado()
    {
        _context.PrefijosNumeracion.Add(new PrefijoNumeracion { Tipo = TipoPrefijoNumeracion.Venta, Prefijo = "VT" });
        await _context.SaveChangesAsync();
        var venta = await SeedVentaConNumeroAsync($"COT-{Periodo}-000002");
        venta.Estado = EstadoVenta.Cotizacion;

        Assert.True(await _generator.AsegurarNumeroDeVentaAsync(venta));
        Assert.Equal($"VT-{Periodo}-000001", venta.Numero);
    }
    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Facturada)]
    [InlineData(EstadoVenta.Entregada)]
    [InlineData(EstadoVenta.Cancelada)]
    public async Task AsegurarNumeroDeVenta_VentaYaConfirmadaOCerrada_ConservaSuNumero(EstadoVenta estado)
    {
        // H-01: no se altera una venta ya emitida/confirmada (hallazgo de la revisión independiente).
        var venta = await SeedVentaConNumeroAsync($"COT-{Periodo}-000007");
        venta.Estado = estado;
        await _context.SaveChangesAsync();

        Assert.False(await _generator.AsegurarNumeroDeVentaAsync(venta));
        Assert.Equal($"COT-{Periodo}-000007", venta.Numero);
        Assert.Null(venta.NumeroOrigen);
    }
}
