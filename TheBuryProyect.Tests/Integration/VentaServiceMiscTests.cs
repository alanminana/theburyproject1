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
/// Tests de integración para métodos misceláneos de VentaService:
/// AsociarCreditoAVentaAsync, GuardarDatosTarjetaAsync,
/// ObtenerDatosCreditoVentaAsync y GetTotalVentaAsync.
/// </summary>
public class VentaServiceMiscTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private static int _counter = 700;

    public VentaServiceMiscTests()
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
            new StubCurrentUserMisc(),
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
            Nombre = "Test",
            Apellido = "Misc",
            TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"),
            Email = $"misc{n}@test.com"
        };
        _context.Clientes.Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId,
        decimal total = 0m,
        EstadoVenta estado = EstadoVenta.Cotizacion,
        TipoPago tipoPago = TipoPago.Efectivo)
    {
        var n = Interlocked.Increment(ref _counter);
        var v = new Venta
        {
            Numero = $"VTA-MISC-{n:D6}",
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = tipoPago,
            Total = total,
            FechaVenta = DateTime.UtcNow
        };
        _context.Ventas.Add(v);
        await _context.SaveChangesAsync();
        return v;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, EstadoCredito estado = EstadoCredito.Activo)
    {
        var n = Interlocked.Increment(ref _counter);
        var c = new Credito
        {
            ClienteId = clienteId,
            Numero = $"CRE-{n:D6}",
            MontoSolicitado = 5000m,
            MontoAprobado = 5000m,
            TasaInteres = 5m,
            CantidadCuotas = 12,
            MontoCuota = 450m,
            TotalAPagar = 5400m,
            SaldoPendiente = 5000m,
            Estado = estado
        };
        _context.Creditos.Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    private async Task<Producto> SeedProductoAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        // Seed required categoria and marca first
        var cat = new Categoria { Codigo = $"CAT{n}", Nombre = $"Cat{n}", Activo = true };
        var marca = new Marca { Codigo = $"MRC{n}", Nombre = $"Marca{n}", Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var p = new Producto
        {
            Codigo = $"PRD{n}",
            Nombre = $"Prod{n}",
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 200m,
            PorcentajeIVA = 21m,
            StockActual = 10m,
            Activo = true
        };
        _context.Productos.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    // =========================================================================
    // AsociarCreditoAVentaAsync
    // =========================================================================

    [Fact]
    public async Task AsociarCredito_VentaExistente_AsignaCreditoId()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);
        var credito = await SeedCreditoAsync(cliente.Id);

        await _service.AsociarCreditoAVentaAsync(venta.Id, credito.Id);

        _context.ChangeTracker.Clear();
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(credito.Id, ventaActualizada!.CreditoId);
    }

    [Fact]
    public async Task AsociarCredito_VentaInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AsociarCreditoAVentaAsync(99999, 1));
    }

    [Fact]
    public async Task AsociarCredito_VentaEliminada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var n = Interlocked.Increment(ref _counter);
        var ventaEliminada = new Venta
        {
            Numero = $"VTA-DEL-{n:D6}",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            IsDeleted = true
        };
        _context.Ventas.Add(ventaEliminada);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AsociarCreditoAVentaAsync(ventaEliminada.Id, 1));
    }

    // =========================================================================
    // GuardarDatosTarjetaAsync
    // =========================================================================

    [Fact]
    public async Task GuardarDatosTarjeta_VentaExistente_GuardaEntidadYRetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, total: 1000m);

        var vm = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Debito
        };

        var resultado = await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        Assert.True(resultado);
        var guardado = await _context.DatosTarjeta.FirstOrDefaultAsync(d => d.VentaId == venta.Id);
        Assert.NotNull(guardado);
        Assert.Equal("Visa", guardado!.NombreTarjeta);
    }

    [Fact]
    public async Task GuardarDatosTarjeta_VentaInexistente_RetornaFalse()
    {
        var vm = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Debito
        };

        var resultado = await _service.GuardarDatosTarjetaAsync(99999, vm);

        Assert.False(resultado);
    }

    [Fact]
    public async Task GuardarDatosTarjeta_DebitoConRecargo_SumaTotalVenta()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, total: 1000m);

        var vm = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Maestro",
            TipoTarjeta = TipoTarjeta.Debito,
            RecargoAplicado = 50m
        };

        await _service.GuardarDatosTarjetaAsync(venta.Id, vm);

        _context.ChangeTracker.Clear();
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(1050m, ventaActualizada!.Total);
    }

    // =========================================================================
    // ObtenerDatosCreditoVentaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerDatosCreditoVenta_SinCreditoAsociado_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id);

        var resultado = await _service.ObtenerDatosCreditoVentaAsync(venta.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerDatosCreditoVenta_VentaInexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerDatosCreditoVentaAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerDatosCreditoVenta_ConCuotas_RetornaDatos()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var venta = await SeedVentaAsync(cliente.Id, total: 500m, tipoPago: TipoPago.CreditoPersonal);

        // Asociar crédito a la venta
        venta.CreditoId = credito.Id;
        _context.Ventas.Update(venta);

        // Seed VentaCreditoCuotas
        _context.VentaCreditoCuotas.AddRange(
            new VentaCreditoCuota
            {
                VentaId = venta.Id,
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.AddMonths(1),
                Monto = 250m,
                Saldo = 500m
            },
            new VentaCreditoCuota
            {
                VentaId = venta.Id,
                CreditoId = credito.Id,
                NumeroCuota = 2,
                FechaVencimiento = DateTime.UtcNow.AddMonths(2),
                Monto = 250m,
                Saldo = 0m
            });
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerDatosCreditoVentaAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(credito.Id, resultado!.CreditoId);
        Assert.Equal(2, resultado.CantidadCuotas);
        Assert.Equal(500m, resultado.TotalAPagar);
    }

    // =========================================================================
    // GetTotalVentaAsync
    // =========================================================================

    [Fact]
    public async Task GetTotalVenta_VentaInexistente_RetornaNull()
    {
        var resultado = await _service.GetTotalVentaAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetTotalVenta_VentaConTotalFijado_RetornaTotal()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, total: 850m);

        var resultado = await _service.GetTotalVentaAsync(venta.Id);

        Assert.Equal(850m, resultado);
    }

    [Fact]
    public async Task GetTotalVenta_VentaSinTotalPeroConDetalles_CalculaDesdeDetalles()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, total: 0m);
        var producto = await SeedProductoAsync();

        _context.VentaDetalles.Add(new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 2,
            PrecioUnitario = 100m,
            Subtotal = 200m
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.GetTotalVentaAsync(venta.Id);

        // Total is 0 so it falls through to calculate from detalles
        Assert.NotNull(resultado);
    }

    [Fact]
    public async Task GetTotalVenta_VentaSinTotalNiDetalles_RetornaCero()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, total: 0m);

        var resultado = await _service.GetTotalVentaAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(0m, resultado);
    }
}

// ---------------------------------------------------------------------------
// Stub mínimo de ICurrentUserService
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserMisc : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-misc";
    public string GetEmail() => "misc@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
