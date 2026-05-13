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
            new PrecioVigenteResolver(_context),
            new StubCurrentUserDelUpd(),
            null!,
            null!,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
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

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 100m)
    {
        var n = Interlocked.Increment(ref _counter);
        var categoria = new Categoria
        {
            Codigo = $"CAT-DU-{n}",
            Nombre = $"Categoria DU {n}",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        var marca = new Marca
        {
            Codigo = $"MAR-DU-{n}",
            Nombre = $"Marca DU {n}",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PROD-DU-{n}",
            Nombre = $"Producto DU {n}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioVenta / 2m,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 0m,
            StockActual = 100,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
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

    [Fact]
    public async Task UpdateAsync_IgnoraCamposLegacyPagoPorDetalle()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);
        var producto = await SeedProductoAsync(precioVenta: 100m);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas
            .AsNoTracking()
            .SingleAsync(v => v.Id == venta.Id);

        var vm = new VentaViewModel
        {
            Id = ventaOriginal.Id,
            ClienteId = cliente.Id,
            FechaVenta = ventaOriginal.FechaVenta,
            Estado = ventaOriginal.Estado,
            TipoPago = TipoPago.Transferencia,
            RowVersion = ventaOriginal.RowVersion,
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 1,
                    PrecioUnitario = 999m,
                    Descuento = 0m,
                    TipoPago = TipoPago.TarjetaCredito,
                    ProductoCondicionPagoPlanId = 987_654,
                    MontoAjustePlanAplicado = 999m
                }
            }
        };

        var resultado = await _service.UpdateAsync(ventaOriginal.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal(TipoPago.Transferencia, resultado!.TipoPago);
        Assert.Equal(100m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => d.VentaId == ventaOriginal.Id && !d.IsDeleted);

        Assert.Null(detalle.TipoPago);
        Assert.Null(detalle.ProductoCondicionPagoPlanId);
        Assert.Null(detalle.PorcentajeAjustePlanAplicado);
        Assert.Null(detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task UpdateAsync_EfectivoAMercadoPagoConPlanGlobal_PersisteDatosTarjeta()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);
        var producto = await SeedProductoAsync(precioVenta: 1_000m);
        var plan = await SeedPlanGlobalAsync(TipoPago.MercadoPago, ajustePorcentaje: 10m, cantidadCuotas: 2);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var vm = CrearVentaUpdateVm(
            ventaOriginal,
            cliente.Id,
            producto.Id,
            TipoPago.MercadoPago,
            new DatosTarjetaViewModel
            {
                NombreTarjeta = "Mercado Pago",
                TipoTarjeta = TipoTarjeta.Debito,
                CantidadCuotas = 2,
                ConfiguracionPagoPlanId = plan.Id
            });

        var resultado = await _service.UpdateAsync(ventaOriginal.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal(TipoPago.MercadoPago, resultado!.TipoPago);
        Assert.Equal(1_100m, resultado.Total);

        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal("Mercado Pago", datos.NombreTarjeta);
        Assert.Equal(TipoTarjeta.Debito, datos.TipoTarjeta);
        Assert.NotEqual(TipoTarjeta.Credito, datos.TipoTarjeta);
        Assert.Equal(plan.Id, datos.ConfiguracionPagoPlanId);
        Assert.Equal(10m, datos.PorcentajeAjustePagoAplicado);
        Assert.Equal(100m, datos.MontoAjustePagoAplicado);
        Assert.Null(datos.ProductoCondicionPagoPlanId);
    }

    [Theory]
    [InlineData(TipoPago.TarjetaCredito, TipoTarjeta.Credito)]
    [InlineData(TipoPago.TarjetaDebito, TipoTarjeta.Debito)]
    public async Task UpdateAsync_EfectivoATarjeta_PersisteDatosTarjeta(TipoPago tipoPago, TipoTarjeta tipoTarjeta)
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);
        var producto = await SeedProductoAsync(precioVenta: 1_000m);
        var tarjeta = await SeedConfiguracionTarjetaAsync(tipoPago, tipoTarjeta);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var vm = CrearVentaUpdateVm(
            ventaOriginal,
            cliente.Id,
            producto.Id,
            tipoPago,
            new DatosTarjetaViewModel
            {
                ConfiguracionTarjetaId = tarjeta.Id,
                NombreTarjeta = tarjeta.NombreTarjeta,
                TipoTarjeta = tipoTarjeta,
                CantidadCuotas = 1
            });

        var resultado = await _service.UpdateAsync(ventaOriginal.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal(tipoPago, resultado!.TipoPago);

        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(tarjeta.Id, datos.ConfiguracionTarjetaId);
        Assert.Equal(tarjeta.NombreTarjeta, datos.NombreTarjeta);
        Assert.Equal(tipoTarjeta, datos.TipoTarjeta);
        Assert.Null(datos.ProductoCondicionPagoPlanId);
    }

    [Fact]
    public async Task UpdateAsync_EfectivoATarjetaHistorica_RechazaCambioNuevo()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);
        var producto = await SeedProductoAsync(precioVenta: 1_000m);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var vm = CrearVentaUpdateVm(
            ventaOriginal,
            cliente.Id,
            producto.Id,
            TipoPago.Tarjeta,
            datosTarjeta: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(ventaOriginal.Id, vm));

        Assert.Contains("historico", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tarjeta Credito", ex.Message);
        Assert.Contains("Tarjeta Debito", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_TarjetaHistoricaAPermaneceTarjeta_PreservaCompatibilidad()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);
        var producto = await SeedProductoAsync(precioVenta: 1_000m);

        venta.TipoPago = TipoPago.Tarjeta;
        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var vm = CrearVentaUpdateVm(
            ventaOriginal,
            cliente.Id,
            producto.Id,
            TipoPago.Tarjeta,
            datosTarjeta: null);

        var resultado = await _service.UpdateAsync(ventaOriginal.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal(TipoPago.Tarjeta, resultado!.TipoPago);
    }

    [Theory]
    [InlineData(TipoPago.Efectivo)]
    [InlineData(TipoPago.Transferencia)]
    public async Task UpdateAsync_EfectivoTransferencia_NoMantienenDatosTarjeta(TipoPago tipoPago)
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Presupuesto);
        var producto = await SeedProductoAsync(precioVenta: 1_000m);
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = venta.Id,
            NombreTarjeta = "Snapshot anterior",
            TipoTarjeta = TipoTarjeta.Credito,
            CantidadCuotas = 1
        });

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var vm = CrearVentaUpdateVm(ventaOriginal, cliente.Id, producto.Id, tipoPago, datosTarjeta: null);

        var resultado = await _service.UpdateAsync(ventaOriginal.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal(tipoPago, resultado!.TipoPago);
        Assert.False(await _context.DatosTarjeta.AsNoTracking().AnyAsync(d => d.VentaId == venta.Id));
    }

    private async Task<ConfiguracionTarjeta> SeedConfiguracionTarjetaAsync(TipoPago tipoPago, TipoTarjeta tipoTarjeta)
    {
        var medio = new ConfiguracionPago
        {
            TipoPago = tipoPago,
            Nombre = $"Medio {tipoPago} {Interlocked.Increment(ref _counter)}",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();

        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = medio.Id,
            NombreTarjeta = $"Tarjeta {tipoPago}",
            TipoTarjeta = tipoTarjeta,
            TipoCuota = TipoCuotaTarjeta.SinInteres,
            PermiteCuotas = tipoTarjeta == TipoTarjeta.Credito,
            Activa = true,
            IsDeleted = false
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ConfiguracionPagoPlan> SeedPlanGlobalAsync(
        TipoPago tipoPago,
        decimal ajustePorcentaje,
        int cantidadCuotas)
    {
        var medio = new ConfiguracionPago
        {
            TipoPago = tipoPago,
            Nombre = $"Medio {tipoPago} {Interlocked.Increment(ref _counter)}",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();

        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id,
            TipoPago = tipoPago,
            CantidadCuotas = cantidadCuotas,
            AjustePorcentaje = ajustePorcentaje,
            Etiqueta = $"{cantidadCuotas} cuotas",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    private static VentaViewModel CrearVentaUpdateVm(
        Venta venta,
        int clienteId,
        int productoId,
        TipoPago tipoPago,
        DatosTarjetaViewModel? datosTarjeta)
    {
        return new VentaViewModel
        {
            Id = venta.Id,
            ClienteId = clienteId,
            FechaVenta = venta.FechaVenta,
            Estado = venta.Estado,
            TipoPago = tipoPago,
            RowVersion = venta.RowVersion,
            DatosTarjeta = datosTarjeta,
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = 1,
                    PrecioUnitario = 1_000m,
                    Descuento = 0m,
                    ProductoCondicionPagoPlanId = 987_654
                }
            }
        };
    }

    // =========================================================================
    // ValidarDisponibilidadCreditoAsync
    // =========================================================================

    private async Task<Credito> SeedCreditoAsync(int clienteId, decimal saldo = 5_000m, EstadoCredito estado = EstadoCredito.Activo)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = estado,
            MontoSolicitado = saldo,
            MontoAprobado = saldo,
            SaldoPendiente = saldo,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    [Fact]
    public async Task ValidarDisponibilidad_SaldoSuficiente_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, saldo: 5_000m);

        var resultado = await _service.ValidarDisponibilidadCreditoAsync(credito.Id, 3_000m);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ValidarDisponibilidad_SaldoInsuficiente_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, saldo: 1_000m);

        var resultado = await _service.ValidarDisponibilidadCreditoAsync(credito.Id, 5_000m);

        Assert.False(resultado);
    }

    [Fact]
    public async Task ValidarDisponibilidad_CreditoInexistente_RetornaFalse()
    {
        var resultado = await _service.ValidarDisponibilidadCreditoAsync(99999, 100m);
        Assert.False(resultado);
    }

    [Fact]
    public async Task ValidarDisponibilidad_CreditoNoActivo_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, saldo: 5_000m, estado: EstadoCredito.Cancelado);

        var resultado = await _service.ValidarDisponibilidadCreditoAsync(credito.Id, 1_000m);

        Assert.False(resultado);
    }

    // =========================================================================
    // GuardarDatosChequeAsync
    // =========================================================================

    [Fact]
    public async Task GuardarDatosCheque_VentaExistente_PersisteDatos()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        var datosCheque = new DatosChequeViewModel
        {
            NumeroCheque = "00012345",
            Banco = "Banco Nación",
            Titular = "Juan Pérez",
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Monto = 1_500m
        };

        var resultado = await _service.GuardarDatosChequeAsync(venta.Id, datosCheque);

        Assert.True(resultado);
        var chequeDb = await _context.DatosCheque.FirstOrDefaultAsync(c => c.VentaId == venta.Id);
        Assert.NotNull(chequeDb);
        Assert.Equal("00012345", chequeDb!.NumeroCheque);
        Assert.Equal("Banco Nación", chequeDb.Banco);
    }

    [Fact]
    public async Task GuardarDatosCheque_VentaInexistente_RetornaFalse()
    {
        var datosCheque = new DatosChequeViewModel
        {
            NumeroCheque = "00000001",
            Banco = "Test",
            Titular = "Test",
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Monto = 100m
        };

        var resultado = await _service.GuardarDatosChequeAsync(99999, datosCheque);

        Assert.False(resultado);
    }

    // =========================================================================
    // CalcularCuotasTarjetaAsync
    // =========================================================================

    private async Task<ConfiguracionTarjeta> SeedConfiguracionTarjetaAsync(
        TipoCuotaTarjeta tipoCuota = TipoCuotaTarjeta.SinInteres,
        decimal? tasaMensual = null)
    {
        var configPago = new ConfiguracionPago
        {
            TipoPago = TipoPago.TarjetaCredito,
            Nombre = "Config Tarjeta Test",
            Activo = true
        };
        _context.Set<ConfiguracionPago>().Add(configPago);
        await _context.SaveChangesAsync();

        var config = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPago.Id,
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Credito,
            TipoCuota = tipoCuota,
            TasaInteresesMensual = tasaMensual,
            PermiteCuotas = true
        };
        _context.Set<ConfiguracionTarjeta>().Add(config);
        await _context.SaveChangesAsync();
        return config;
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_SinInteres_MontoCuotaIgualMontoSobreCuotas()
    {
        var config = await SeedConfiguracionTarjetaAsync(TipoCuotaTarjeta.SinInteres);

        var resultado = await _service.CalcularCuotasTarjetaAsync(config.Id, 1_200m, 4);

        Assert.Equal(0, resultado.TasaInteres);
        Assert.Equal(300m, resultado.MontoCuota);
        Assert.Equal(1_200m, resultado.MontoTotalConInteres);
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_ConInteres_MontoTotalMayorAlOriginal()
    {
        var config = await SeedConfiguracionTarjetaAsync(TipoCuotaTarjeta.ConInteres, tasaMensual: 5m);

        var resultado = await _service.CalcularCuotasTarjetaAsync(config.Id, 1_000m, 3);

        Assert.Equal(5m, resultado.TasaInteres);
        Assert.NotNull(resultado.MontoCuota);
        Assert.True(resultado.MontoTotalConInteres > 1_000m);
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_ConfiguracionInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularCuotasTarjetaAsync(99999, 1_000m, 3));
    }

    // =========================================================================
    // GuardarDatosTarjetaAsync
    // =========================================================================

    [Fact]
    public async Task GuardarDatosTarjeta_VentaExistente_PersisteDatos()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        var datos = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Debito,
            NumeroAutorizacion = "AUTH001"
        };

        var resultado = await _service.GuardarDatosTarjetaAsync(venta.Id, datos);

        Assert.True(resultado);
        var db = await _context.DatosTarjeta.FirstOrDefaultAsync(d => d.VentaId == venta.Id);
        Assert.NotNull(db);
        Assert.Equal("Visa", db!.NombreTarjeta);
    }

    [Fact]
    public async Task GuardarDatosTarjeta_VentaInexistente_RetornaFalse()
    {
        var datos = new DatosTarjetaViewModel
        {
            NombreTarjeta = "Mastercard",
            TipoTarjeta = TipoTarjeta.Debito
        };

        var resultado = await _service.GuardarDatosTarjetaAsync(99999, datos);

        Assert.False(resultado);
    }

    // =========================================================================
    // CalcularCreditoPersonallAsync
    // =========================================================================

    private async Task<Credito> SeedCreditoActivoAsync(int clienteId, decimal saldo = 10_000m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = saldo,
            MontoAprobado = saldo,
            SaldoPendiente = saldo,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    [Fact]
    public async Task CalcularCreditoPersonall_CreditoActivo_RetornaCuotasCalculadas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoActivoAsync(cliente.Id, 10_000m);

        var resultado = await _service.CalcularCreditoPersonallAsync(
            credito.Id, 3_000m, 3, DateTime.Today.AddMonths(1));

        Assert.NotNull(resultado);
        Assert.Equal(credito.Id, resultado.CreditoId);
        Assert.Equal(3, resultado.CantidadCuotas);
        Assert.True(resultado.MontoCuota > 0);
    }

    [Fact]
    public async Task CalcularCreditoPersonall_MontoSuperiorAlSaldo_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoActivoAsync(cliente.Id, 1_000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularCreditoPersonallAsync(
                credito.Id, 5_000m, 3, DateTime.Today.AddMonths(1)));
    }

    [Fact]
    public async Task CalcularCreditoPersonall_CreditoInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularCreditoPersonallAsync(99999, 1_000m, 3, DateTime.Today));
    }

    [Fact]
    public async Task CalcularCreditoPersonall_CreditoCancelado_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, saldo: 5_000m, estado: EstadoCredito.Cancelado);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularCreditoPersonallAsync(
                credito.Id, 1_000m, 3, DateTime.Today.AddMonths(1)));
    }

    // =========================================================================
    // ObtenerDatosCreditoVentaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerDatosCreditoVenta_VentaSinCuotas_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

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
        var credito = await SeedCreditoActivoAsync(cliente.Id, 10_000m);
        var venta = await SeedVentaAsync(cliente.Id, EstadoVenta.Cotizacion);

        // Asociar crédito y agregar cuotas
        var ventaEnt = await _context.Ventas.FindAsync(venta.Id);
        ventaEnt!.CreditoId = credito.Id;
        _context.Set<VentaCreditoCuota>().Add(new VentaCreditoCuota
        {
            VentaId = venta.Id,
            CreditoId = credito.Id,
            NumeroCuota = 1,
            FechaVencimiento = DateTime.Today.AddMonths(1),
            Monto = 1_000m,
            Saldo = 3_000m
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerDatosCreditoVentaAsync(venta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(credito.Id, resultado!.CreditoId);
        Assert.Equal(1, resultado.CantidadCuotas);
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
    public async Task GetTotalVenta_VentaConTotalPreCalculado_RetornaTotal()
    {
        var cliente = await SeedClienteAsync();
        var n = Interlocked.Increment(ref _counter);
        var venta = new Venta
        {
            Numero = $"VTA-TOT-{n:D6}",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Total = 1_500m
        };
        _context.Set<Venta>().Add(venta);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetTotalVentaAsync(venta.Id);

        Assert.Equal(1_500m, resultado);
    }

    [Fact]
    public async Task GetTotalVenta_VentaSinDetalles_RetornaCero()
    {
        var cliente = await SeedClienteAsync();
        var n = Interlocked.Increment(ref _counter);
        var venta = new Venta
        {
            Numero = $"VTA-NOTOT-{n:D6}",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Total = 0m
        };
        _context.Set<Venta>().Add(venta);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetTotalVentaAsync(venta.Id);

        Assert.Equal(0m, resultado);
    }
}
