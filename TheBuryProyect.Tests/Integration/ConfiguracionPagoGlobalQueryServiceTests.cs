using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public sealed class ConfiguracionPagoGlobalQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoGlobalQueryService _service;

    public ConfiguracionPagoGlobalQueryServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _service = new ConfiguracionPagoGlobalQueryService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_DevuelvePlanesActivosYOmitInactivos()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.MercadoPago);
        var activo = await SeedPlanAsync(medio, cuotas: 3, activo: true);
        var inactivo = await SeedPlanAsync(medio, cuotas: 6, activo: false);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var planes = Assert.Single(resultado.Medios).Planes;
        Assert.Contains(planes, p => p.Id == activo.Id);
        Assert.DoesNotContain(planes, p => p.Id == inactivo.Id);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_OmiteSoftDeleted()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var tarjetaActiva = await SeedTarjetaAsync(medio, "Visa", TipoTarjeta.Credito);
        var tarjetaSoftDeleted = await SeedTarjetaAsync(medio, "Mastercard", TipoTarjeta.Credito, isDeleted: true);
        var planActivo = await SeedPlanAsync(medio, cuotas: 1);
        var planSoftDeleted = await SeedPlanAsync(medio, cuotas: 6, isDeleted: true);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var dto = Assert.Single(resultado.Medios);
        Assert.Contains(dto.Tarjetas, t => t.Id == tarjetaActiva.Id);
        Assert.DoesNotContain(dto.Tarjetas, t => t.Id == tarjetaSoftDeleted.Id);
        Assert.Contains(dto.Planes, p => p.Id == planActivo.Id);
        Assert.DoesNotContain(dto.Planes, p => p.Id == planSoftDeleted.Id);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_OrdenaPlanesPorOrdenYLuegoCantidadCuotas()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var planOrden2 = await SeedPlanAsync(medio, cuotas: 1, orden: 2);
        var planOrden1Cuotas6 = await SeedPlanAsync(medio, cuotas: 6, orden: 1);
        var planOrden1Cuotas3 = await SeedPlanAsync(medio, cuotas: 3, orden: 1);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var ids = Assert.Single(resultado.Medios).Planes.Select(p => p.Id).ToArray();
        Assert.Equal(new[] { planOrden1Cuotas3.Id, planOrden1Cuotas6.Id, planOrden2.Id }, ids);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_DevuelvePlanGeneralSinTarjeta()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.Transferencia);
        var planGeneral = await SeedPlanAsync(medio, cuotas: 1, tarjetaId: null, etiqueta: "Transferencia");

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var plan = Assert.Single(Assert.Single(resultado.Medios).Planes);
        Assert.Equal(planGeneral.Id, plan.Id);
        Assert.Null(plan.ConfiguracionTarjetaId);
        Assert.True(plan.EsPlanGeneral);
        Assert.Equal("Transferencia", plan.Etiqueta);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_DevuelvePlanEspecificoPorTarjeta()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaAsync(medio, "Visa", TipoTarjeta.Credito);
        var planTarjeta = await SeedPlanAsync(medio, cuotas: 6, tarjetaId: tarjeta.Id);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var plan = Assert.Single(Assert.Single(resultado.Medios).Planes);
        Assert.Equal(planTarjeta.Id, plan.Id);
        Assert.Equal(tarjeta.Id, plan.ConfiguracionTarjetaId);
        Assert.False(plan.EsPlanGeneral);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_OmiteTarjetaInactivaYSusPlanes()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var activa = await SeedTarjetaAsync(medio, "Visa", TipoTarjeta.Credito);
        var inactiva = await SeedTarjetaAsync(medio, "Mastercard", TipoTarjeta.Credito, activa: false);
        var planTarjetaActiva = await SeedPlanAsync(medio, cuotas: 3, tarjetaId: activa.Id);
        var planTarjetaInactiva = await SeedPlanAsync(medio, cuotas: 6, tarjetaId: inactiva.Id);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var dto = Assert.Single(resultado.Medios);
        Assert.Contains(dto.Tarjetas, t => t.Id == activa.Id);
        Assert.DoesNotContain(dto.Tarjetas, t => t.Id == inactiva.Id);
        Assert.Contains(dto.Planes, p => p.Id == planTarjetaActiva.Id);
        Assert.DoesNotContain(dto.Planes, p => p.Id == planTarjetaInactiva.Id);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_DistingueTarjetaCreditoYDebito()
    {
        var credito = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var debito = await SeedConfiguracionPagoAsync(TipoPago.TarjetaDebito);
        var visaCredito = await SeedTarjetaAsync(credito, "Visa Credito", TipoTarjeta.Credito);
        var visaDebito = await SeedTarjetaAsync(debito, "Visa Debito", TipoTarjeta.Debito);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var tarjetas = resultado.Medios.SelectMany(m => m.Tarjetas).ToList();
        Assert.Contains(tarjetas, t => t.Id == visaCredito.Id && t.TipoTarjeta == TipoTarjeta.Credito);
        Assert.Contains(tarjetas, t => t.Id == visaDebito.Id && t.TipoTarjeta == TipoTarjeta.Debito);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_NoIncluyePagoPorProductoLegacy()
    {
        var medio = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        await SeedProductoCondicionPagoLegacyAsync(TipoPago.TarjetaCredito);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var dto = Assert.Single(resultado.Medios);
        Assert.Equal(medio.Id, dto.Id);
        Assert.Empty(dto.Planes);
        Assert.Empty(dto.Tarjetas);
    }

    [Fact]
    public async Task ConfiguracionPagoGlobal_NoRequiereDatosDeVenta()
    {
        var medio = await SeedConfiguracionPagoAsync(
            TipoPago.Efectivo,
            permiteDescuento: true,
            porcentajeDescuentoMaximo: 10m);
        await SeedPlanAsync(medio, cuotas: 1, ajustePorcentaje: -5m);

        var resultado = await _service.ObtenerActivaParaVentaAsync();

        var dto = Assert.Single(resultado.Medios);
        Assert.Equal(TipoPago.Efectivo, dto.TipoPago);
        Assert.True(dto.Ajuste.PermiteDescuento);
        Assert.Equal(10m, dto.Ajuste.PorcentajeDescuentoMaximo);
        Assert.Equal(-5m, Assert.Single(dto.Planes).AjustePorcentaje);
    }

    private async Task<ConfiguracionPago> SeedConfiguracionPagoAsync(
        TipoPago tipoPago,
        bool activo = true,
        bool permiteDescuento = false,
        decimal? porcentajeDescuentoMaximo = null,
        bool tieneRecargo = false,
        decimal? porcentajeRecargo = null)
    {
        var configuracion = new ConfiguracionPago
        {
            TipoPago = tipoPago,
            Nombre = tipoPago.ToString(),
            Descripcion = $"Config {tipoPago}",
            Activo = activo,
            PermiteDescuento = permiteDescuento,
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo,
            TieneRecargo = tieneRecargo,
            PorcentajeRecargo = porcentajeRecargo
        };

        _context.ConfiguracionesPago.Add(configuracion);
        await _context.SaveChangesAsync();
        return configuracion;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaAsync(
        ConfiguracionPago configuracion,
        string nombre,
        TipoTarjeta tipoTarjeta,
        bool activa = true,
        bool isDeleted = false)
    {
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configuracion.Id,
            NombreTarjeta = nombre,
            TipoTarjeta = tipoTarjeta,
            Activa = activa,
            PermiteCuotas = tipoTarjeta == TipoTarjeta.Credito,
            CantidadMaximaCuotas = tipoTarjeta == TipoTarjeta.Credito ? 12 : null,
            IsDeleted = isDeleted
        };

        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ConfiguracionPagoPlan> SeedPlanAsync(
        ConfiguracionPago configuracion,
        int cuotas,
        int? tarjetaId = null,
        bool activo = true,
        bool isDeleted = false,
        int orden = 0,
        decimal ajustePorcentaje = 0m,
        string? etiqueta = null)
    {
        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            ConfiguracionTarjetaId = tarjetaId,
            TipoPago = configuracion.TipoPago,
            CantidadCuotas = cuotas,
            Activo = activo,
            IsDeleted = isDeleted,
            Orden = orden,
            AjustePorcentaje = ajustePorcentaje,
            Etiqueta = etiqueta,
            Observaciones = etiqueta
        };

        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    private async Task SeedProductoCondicionPagoLegacyAsync(TipoPago tipoPago)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = codigo, Nombre = $"Cat-{codigo}", Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = $"Marca-{codigo}", Activo = true };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..8],
            Nombre = "Producto Legacy",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            PorcentajeIVA = 21m,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        _context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = tipoPago,
            Permitido = true,
            Activo = true,
            Observaciones = "No debe participar en configuracion global"
        });
        await _context.SaveChangesAsync();
    }
}
