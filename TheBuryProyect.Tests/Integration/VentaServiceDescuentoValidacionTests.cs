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

file sealed class StubCajaServiceDescuento : ICajaService
{
    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    private readonly AperturaCaja _apertura;
    public StubCajaServiceDescuento(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    // Resto de la interfaz — no usado en estos tests
    public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
    public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotImplementedException();
    public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotImplementedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
    public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario, int? ventaId = null) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}


file sealed class StubCurrentUserDescuentoVenta : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    // DESCUENTO-PERMISO-01: estos tests ejercitan la mecánica de descuentos; el usuario de test tiene
    // el permiso independiente ventas.discount (la política de autorización se prueba en
    // VentaServiceDescuentoPermisoTests).
    public bool HasPermission(string modulo, string accion) => modulo == "ventas" && accion == "discount";
    public string? GetIpAddress() => null;
}

/// <summary>
/// FIN-R2 (PROMPT 70): el descuento general (importe) y el de línea (porcentaje) se validan en
/// el servidor al crear y al editar. Antes, Descuento=999999 producía Total=0 con 999999 persistido,
/// y un descuento negativo se persistía como negativo con el total sin cambios.
///
/// Regla (sin umbrales inventados): 0 ≤ línea ≤ 100 %; 0 ≤ general ≤ subtotal de productos
/// (igual que CotizacionPagoCalculator). Descuento == subtotal sigue permitido (100 %).
/// Precio de lista: producto 1.000 (IVA 0 %), 2 unidades → subtotal elegible 2.000.
/// </summary>
public class VentaServiceDescuentoValidacionTests : IDisposable
{
    private const decimal PrecioProducto = 1_000m;
    private const int Cantidad = 2;
    private const decimal SubtotalElegible = PrecioProducto * Cantidad;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly AperturaCaja _apertura;
    private readonly Cliente _cliente;
    private readonly Producto _producto;

    public VentaServiceDescuentoValidacionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var caja = new Caja { Codigo = "DSC1", Nombre = "Caja Descuento", IsDeleted = false, RowVersion = new byte[8] };
        var categoria = new Categoria { Codigo = "CAT-DSC", Nombre = "Cat DSC", IsDeleted = false, RowVersion = new byte[8] };
        var marca = new Marca { Codigo = "MAR-DSC", Nombre = "Marca DSC", IsDeleted = false, RowVersion = new byte[8] };
        _context.AddRange(caja, categoria, marca);
        _context.SaveChanges();

        _apertura = new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m,
            Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        };
        _cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Descuento", TipoDocumento = "DNI", NumeroDocumento = "30000099",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, IsDeleted = false, RowVersion = new byte[8]
        };
        _producto = new Producto
        {
            Nombre = "Producto DSC", Codigo = "P-DSC", PrecioCompra = 600m, PrecioVenta = PrecioProducto,
            PorcentajeIVA = 0m, StockActual = 100, CategoriaId = categoria.Id, MarcaId = marca.Id,
            IsDeleted = false, RowVersion = new byte[8]
        };
        _context.AddRange(_apertura, _cliente, _producto);
        _context.SaveChanges();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserDescuentoVenta(),
            null!,
            new StubCajaServiceDescuento(_apertura),
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static decimal D(string valor) => decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);

    private VentaViewModel Vm(decimal descuentoGeneral, decimal descuentoLinea = 0m) => new()
    {
        ClienteId = _cliente.Id,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = descuentoGeneral,
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = _producto.Id, Cantidad = Cantidad, PrecioUnitario = 0m, Descuento = descuentoLinea }
        }
    };

    private async Task<Venta> VentaPersistidaAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Ventas.AsNoTracking().Include(v => v.Detalles).SingleAsync(v => v.Id == id);
    }

    private async Task<int> ContarVentasAsync() => await _context.Ventas.IgnoreQueryFilters().CountAsync();

    // =====================================================================
    // Create — rechazos
    // =====================================================================

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-500")]
    public async Task Create_DescuentoGeneralNegativo_RechazaYNoPersiste(string descuento)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(D(descuento))));

        Assert.Contains("negativo", ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
        Assert.Empty(await _context.VentaDetalles.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("2000.01")]
    [InlineData("999999")]
    public async Task Create_DescuentoGeneralMayorAlSubtotal_RechazaYNoPersiste(string descuento)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(D(descuento))));

        Assert.Contains("superar el subtotal", ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
        Assert.Empty(await _context.VentaDetalles.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("-10")]
    [InlineData("100.01")]
    [InlineData("150")]
    public async Task Create_DescuentoDeLineaFueraDe0a100_RechazaYNoPersiste(string descuento)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(0m, D(descuento))));

        Assert.Contains("entre 0 y 100", ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
    }

    [Fact]
    public async Task Create_LineaAl50YGeneralMayorAlRestante_Rechaza()
    {
        // línea 50 % → elegible 1.000; general 1.000,01 supera el elegible tras el descuento de línea
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(1_000.01m, 50m)));
        Assert.Equal(0, await ContarVentasAsync());
    }

    // =====================================================================
    // Create — flujo legítimo
    // =====================================================================

    [Fact]
    public async Task Create_SinDescuento_TotalCompleto()
    {
        var creada = await _service.CreateAsync(Vm(0m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(0m, venta.Descuento);
        Assert.Equal(SubtotalElegible, venta.Total);
    }

    [Fact]
    public async Task Create_DescuentoGeneralValido_RestaDelTotalYPersisteElImporte()
    {
        var creada = await _service.CreateAsync(Vm(500m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(500m, venta.Descuento);
        Assert.Equal(1_500m, venta.Total);
        Assert.Equal(500m, venta.Detalles.Where(d => !d.IsDeleted).Sum(d => d.DescuentoGeneralProrrateado));
    }

    [Fact]
    public async Task Create_DescuentoGeneralIgualAlSubtotal_SigueSiendoPermitido()
    {
        var creada = await _service.CreateAsync(Vm(SubtotalElegible));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(SubtotalElegible, venta.Descuento);
        Assert.Equal(0m, venta.Total);
    }

    [Fact]
    public async Task Create_DescuentoDeLineaValido_AplicaPorcentaje()
    {
        var creada = await _service.CreateAsync(Vm(0m, 10m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(1_800m, venta.Total);
        Assert.Equal(10m, venta.Detalles.Single(d => !d.IsDeleted).Descuento);
    }

    [Fact]
    public async Task Create_DescuentoDeLineaIgual100_SigueSiendoPermitido()
    {
        var creada = await _service.CreateAsync(Vm(0m, 100m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(0m, venta.Total);
    }

    [Fact]
    public async Task Create_LineaAl50YGeneralIgualAlRestante_Permite()
    {
        var creada = await _service.CreateAsync(Vm(1_000m, 50m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(1_000m, venta.Descuento);
        Assert.Equal(0m, venta.Total);
    }

    [Fact]
    public async Task Create_DescuentoValido_NoRequiereAutorizacionNiCambiaElEstado()
    {
        // Preserva el circuito existente: un descuento válido no dispara autorización nueva.
        var creada = await _service.CreateAsync(Vm(500m, 10m));
        var venta = await VentaPersistidaAsync(creada.Id);

        Assert.Equal(EstadoVenta.Cotizacion, venta.Estado);
        Assert.False(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
    }

    // =====================================================================
    // Update
    // =====================================================================

    private async Task<Venta> CrearCotizacionValidaAsync()
    {
        var creada = await _service.CreateAsync(Vm(500m));
        // SQLite no genera RowVersion (en SQL Server lo hace la columna rowversion).
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        return await VentaPersistidaAsync(creada.Id);
    }

    private VentaViewModel VmEdicion(Venta venta, decimal descuentoGeneral, decimal descuentoLinea = 0m)
    {
        var vm = Vm(descuentoGeneral, descuentoLinea);
        vm.Id = venta.Id;
        vm.RowVersion = venta.RowVersion;
        vm.Observaciones = "Editado en test";
        return vm;
    }

    [Theory]
    [InlineData("-300", "negativo")]
    [InlineData("2000.01", "superar el subtotal")]
    [InlineData("999999", "superar el subtotal")]
    public async Task Update_DescuentoGeneralInvalido_RechazaYNoModificaLaVenta(string descuento, string fragmentoMensaje)
    {
        var original = await CrearCotizacionValidaAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, D(descuento))));

        Assert.Contains(fragmentoMensaje, ex.Message);

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(500m, despues.Descuento);
        Assert.Equal(1_500m, despues.Total);
        Assert.Equal(original.Detalles.Count(d => !d.IsDeleted), despues.Detalles.Count(d => !d.IsDeleted));
        Assert.DoesNotContain(despues.Detalles, d => d.IsDeleted);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("100.01")]
    public async Task Update_DescuentoDeLineaInvalido_RechazaYNoModificaLaVenta(string descuento)
    {
        var original = await CrearCotizacionValidaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 500m, D(descuento))));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(1_500m, despues.Total);
        Assert.Equal(0m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.DoesNotContain(despues.Detalles, d => d.IsDeleted);
    }

    [Fact]
    public async Task Update_Rechazado_NoPersisteObservacionesNiOtrosCampos()
    {
        var original = await CrearCotizacionValidaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 999_999m)));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.NotEqual("Editado en test", despues.Observaciones);
        Assert.Equal(original.RowVersion, despues.RowVersion);
    }

    [Theory]
    [InlineData("2000.004", true)]   // redondea a 2000.00 == subtotal → válido
    [InlineData("2000.005", false)]  // redondea a 2000.01 > subtotal → inválido
    [InlineData("1999.99", true)]
    public async Task Create_DescuentoGeneralEnElBordeConRedondeo(string descuento, bool valido)
    {
        if (valido)
        {
            var creada = await _service.CreateAsync(Vm(D(descuento)));
            Assert.True((await VentaPersistidaAsync(creada.Id)).Total <= 0.99m);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(D(descuento))));
            Assert.Equal(0, await ContarVentasAsync());
        }
    }

    [Fact]
    public async Task Create_DescuentoDeLineaNegativoMinimo_Rechaza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(0m, -0.01m)));
        Assert.Equal(0, await ContarVentasAsync());
    }

    [Fact]
    public void ViewModelDeLinea_RangeRechaza100Con01_YAceptaLosExtremos()
    {
        bool Valido(decimal d)
        {
            var vm = new VentaDetalleViewModel { ProductoId = 1, Cantidad = 1, PrecioUnitario = 1m, Descuento = d };
            var resultados = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            return System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(
                vm.Descuento,
                new System.ComponentModel.DataAnnotations.ValidationContext(vm) { MemberName = nameof(VentaDetalleViewModel.Descuento) },
                resultados);
        }

        Assert.False(Valido(100.01m));
        Assert.False(Valido(-0.01m));
        Assert.True(Valido(0m));
        Assert.True(Valido(100m));
    }

    [Fact]
    public async Task Update_DescuentoGeneralValido_ActualizaTotal()
    {
        var original = await CrearCotizacionValidaAsync();

        var resultado = await _service.UpdateAsync(original.Id, VmEdicion(original, 800m));

        Assert.NotNull(resultado);
        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(800m, despues.Descuento);
        Assert.Equal(1_200m, despues.Total);
    }

    [Fact]
    public async Task Update_DescuentoGeneralIgualAlSubtotal_SigueSiendoPermitido()
    {
        var original = await CrearCotizacionValidaAsync();

        await _service.UpdateAsync(original.Id, VmEdicion(original, SubtotalElegible));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(0m, despues.Total);
    }

    [Fact]
    public async Task Update_QuitarDescuento_VuelveAlTotalCompleto()
    {
        var original = await CrearCotizacionValidaAsync();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 0m));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(0m, despues.Descuento);
        Assert.Equal(SubtotalElegible, despues.Total);
    }
}
