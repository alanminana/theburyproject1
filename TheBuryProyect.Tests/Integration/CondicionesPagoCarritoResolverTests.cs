using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public sealed class CondicionesPagoCarritoResolverTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;
    private CondicionesPagoCarritoResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        _resolver = new CondicionesPagoCarritoResolver(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ProductoSinCondiciones_DevuelveResultadoGlobalSinBloqueo()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            maxCuotasSinInteresGlobal: 12,
            maxCuotasConInteresGlobal: 18);

        Assert.True(resultado.Permitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuentePermitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuenteRestriccion);
        Assert.Equal(12, resultado.MaxCuotasSinInteres);
        Assert.Equal(18, resultado.MaxCuotasConInteres);
        Assert.False(resultado.TieneRestriccionesPorProducto);
        Assert.Empty(resultado.ProductoIdsBloqueantes);
    }

    [Fact]
    public async Task ProductoConPermitidoFalse_BloqueaMedioEnResultado()
    {
        var producto = await SeedProductoAsync();
        await SeedCondicionAsync(producto.Id, TipoPago.Transferencia, permitido: false);

        var resultado = await _resolver.ResolverAsync(new[] { producto.Id }, TipoPago.Transferencia);

        Assert.False(resultado.Permitido);
        Assert.Equal(AlcanceBloqueoPago.Medio, resultado.AlcanceBloqueo);
        Assert.Equal(FuenteCondicionPagoEfectiva.Producto, resultado.FuentePermitido);
        Assert.Equal(new[] { producto.Id }, resultado.ProductoIdsBloqueantes);
    }

    [Fact]
    public async Task CarritoConVariosProductos_BloqueaSiUnoBloqueaElMedio()
    {
        var permitido = await SeedProductoAsync();
        var bloqueante = await SeedProductoAsync();
        await SeedCondicionAsync(permitido.Id, TipoPago.Efectivo, permitido: true);
        await SeedCondicionAsync(bloqueante.Id, TipoPago.Efectivo, permitido: false);

        var resultado = await _resolver.ResolverAsync(
            new[] { permitido.Id, bloqueante.Id },
            TipoPago.Efectivo);

        Assert.False(resultado.Permitido);
        Assert.Equal(new[] { bloqueante.Id }, resultado.ProductoIdsBloqueantes);
        Assert.Single(resultado.Bloqueos);
    }

    [Fact]
    public async Task ReglaEspecificaTarjetaBloqueada_NoBloqueaOtraTarjeta()
    {
        var producto = await SeedProductoAsync();
        var visa = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var master = await SeedTarjetaAsync("Master", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito, permitido: true);
        await SeedReglaTarjetaAsync(condicion.Id, visa.Id, permitido: false);

        var resultadoVisa = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            visa.Id);
        var resultadoMaster = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            master.Id);

        Assert.False(resultadoVisa.Permitido);
        Assert.True(resultadoMaster.Permitido);
        Assert.Empty(resultadoMaster.ProductoIdsBloqueantes);
    }

    [Fact]
    public async Task ReglaGeneralTarjeta_AplicaCuandoNoHayEspecifica()
    {
        var producto = await SeedProductoAsync();
        var tarjeta = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 12);
        await SeedReglaTarjetaAsync(condicion.Id, null, maxCuotasSinInteres: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            tarjeta.Id);

        Assert.True(resultado.Permitido);
        Assert.Equal(6, resultado.MaxCuotasSinInteres);
        Assert.Contains(resultado.Restricciones, r =>
            r.ProductoId == producto.Id
            && r.TipoRestriccion == TipoRestriccionCuotas.MaxCuotasSinInteres
            && r.Fuente == FuenteCondicionPagoEfectiva.TarjetaGeneral);
    }

    [Fact]
    public async Task ReglaEspecificaTarjeta_AplicaCuandoCorresponde()
    {
        var producto = await SeedProductoAsync();
        var tarjeta = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 12);
        await SeedReglaTarjetaAsync(condicion.Id, null, maxCuotasSinInteres: 9);
        await SeedReglaTarjetaAsync(condicion.Id, tarjeta.Id, maxCuotasSinInteres: 3);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            tarjeta.Id);

        Assert.Equal(3, resultado.MaxCuotasSinInteres);
        Assert.Contains(resultado.Restricciones, r =>
            r.ProductoId == producto.Id
            && r.Valor == 3
            && r.Fuente == FuenteCondicionPagoEfectiva.TarjetaEspecifica);
    }

    [Fact]
    public async Task MaxCuotasSinInteresEfectivo_UsaMinimoRestrictivoDesdeDb()
    {
        var producto12 = await SeedProductoAsync();
        var producto6 = await SeedProductoAsync();
        await SeedCondicionAsync(producto12.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 12);
        await SeedCondicionAsync(producto6.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto12.Id, producto6.Id },
            TipoPago.TarjetaCredito);

        Assert.Equal(6, resultado.MaxCuotasSinInteres);
        Assert.Equal(new[] { producto6.Id }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public async Task MaxCuotasConInteresEfectivo_UsaMinimoRestrictivoDesdeDb()
    {
        var producto24 = await SeedProductoAsync();
        var producto9 = await SeedProductoAsync();
        await SeedCondicionAsync(producto24.Id, TipoPago.TarjetaCredito, maxCuotasConInteres: 24);
        await SeedCondicionAsync(producto9.Id, TipoPago.TarjetaCredito, maxCuotasConInteres: 9);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto24.Id, producto9.Id },
            TipoPago.TarjetaCredito);

        Assert.Equal(9, resultado.MaxCuotasConInteres);
        Assert.Equal(new[] { producto9.Id }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public async Task MaxCuotasCreditoEfectivo_UsaMinimoRestrictivoDesdeDb()
    {
        var producto18 = await SeedProductoAsync();
        var producto12 = await SeedProductoAsync();
        await SeedCondicionAsync(producto18.Id, TipoPago.CreditoPersonal, maxCuotasCredito: 18);
        await SeedCondicionAsync(producto12.Id, TipoPago.CreditoPersonal, maxCuotasCredito: 12);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto18.Id, producto12.Id },
            TipoPago.CreditoPersonal);

        Assert.Equal(12, resultado.MaxCuotasCredito);
        Assert.Equal(new[] { producto12.Id }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public async Task ProductosBloqueantesYRestrictivos_SeDevuelvenSeparados()
    {
        var bloqueante = await SeedProductoAsync();
        var restrictivo = await SeedProductoAsync();
        await SeedCondicionAsync(bloqueante.Id, TipoPago.CreditoPersonal, permitido: false);
        await SeedCondicionAsync(restrictivo.Id, TipoPago.CreditoPersonal, maxCuotasCredito: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { bloqueante.Id, restrictivo.Id },
            TipoPago.CreditoPersonal);

        Assert.Equal(new[] { bloqueante.Id }, resultado.ProductoIdsBloqueantes);
        Assert.Equal(new[] { restrictivo.Id }, resultado.ProductoIdsRestrictivos);
        Assert.Single(resultado.Bloqueos);
        Assert.Single(resultado.Restricciones);
    }

    [Fact]
    public async Task RecargosYDescuentos_SeDevuelvenInformativosYNoAlteranTotales()
    {
        var producto = await SeedProductoAsync();
        await SeedCondicionAsync(
            producto.Id,
            TipoPago.Efectivo,
            porcentajeRecargo: 10m,
            porcentajeDescuentoMaximo: 5m);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.Efectivo,
            totalReferencia: 1_000m);

        var ajuste = Assert.Single(resultado.AjustesInformativos);
        Assert.Equal(10m, ajuste.PorcentajeRecargo);
        Assert.Equal(5m, ajuste.PorcentajeDescuentoMaximo);
        Assert.Equal(1_000m, resultado.TotalReferencia);
        Assert.Equal(1_000m, resultado.TotalSinAplicarAjustes);
    }

    [Fact]
    public async Task TipoPagoTarjetaLegacy_SeConservaSinNormalizarSilenciosamente()
    {
        var producto = await SeedProductoAsync();
        await SeedCondicionAsync(producto.Id, TipoPago.Tarjeta, maxCuotasSinInteres: 6);

        var resultado = await _resolver.ResolverAsync(new[] { producto.Id }, TipoPago.Tarjeta);
        var condicionDb = await _context.ProductoCondicionesPago.SingleAsync(c => c.ProductoId == producto.Id);

        Assert.Equal(TipoPago.Tarjeta, condicionDb.TipoPago);
        Assert.Equal(TipoPago.Tarjeta, resultado.TipoPago);
        Assert.Equal(6, resultado.MaxCuotasSinInteres);
    }

    private async Task<Producto> SeedProductoAsync()
    {
        var codigo = Guid.NewGuid().ToString("N")[..12];
        var categoria = new Categoria { Codigo = "C" + codigo, Nombre = "Categoria test" };
        var marca = new Marca { Codigo = "M" + codigo, Nombre = "Marca test" };
        var producto = new Producto
        {
            Codigo = "P" + codigo,
            Nombre = "Producto test",
            Categoria = categoria,
            Marca = marca,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            StockActual = 1m
        };

        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaAsync(string nombre, TipoTarjeta tipoTarjeta)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tipoPago = tipoTarjeta == TipoTarjeta.Debito ? TipoPago.TarjetaDebito : TipoPago.TarjetaCredito;
        var configuracionPago = await _context.ConfiguracionesPago
            .FirstOrDefaultAsync(c => c.TipoPago == tipoPago);
        if (configuracionPago is null)
        {
            configuracionPago = new ConfiguracionPago
            {
                TipoPago = tipoPago,
                Nombre = $"Config-{tipoPago}-{suffix}"
            };
        }

        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPago = configuracionPago,
            NombreTarjeta = $"{nombre}-{suffix}",
            TipoTarjeta = tipoTarjeta,
            PermiteCuotas = tipoTarjeta == TipoTarjeta.Credito,
            CantidadMaximaCuotas = tipoTarjeta == TipoTarjeta.Credito ? 12 : null
        };

        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ProductoCondicionPago> SeedCondicionAsync(
        int productoId,
        TipoPago tipoPago,
        bool? permitido = null,
        int? maxCuotasSinInteres = null,
        int? maxCuotasConInteres = null,
        int? maxCuotasCredito = null,
        decimal? porcentajeRecargo = null,
        decimal? porcentajeDescuentoMaximo = null)
    {
        var condicion = new ProductoCondicionPago
        {
            ProductoId = productoId,
            TipoPago = tipoPago,
            Permitido = permitido,
            MaxCuotasSinInteres = maxCuotasSinInteres,
            MaxCuotasConInteres = maxCuotasConInteres,
            MaxCuotasCredito = maxCuotasCredito,
            PorcentajeRecargo = porcentajeRecargo,
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo
        };

        _context.ProductoCondicionesPago.Add(condicion);
        await _context.SaveChangesAsync();
        return condicion;
    }

    private async Task<ProductoCondicionPagoTarjeta> SeedReglaTarjetaAsync(
        int productoCondicionPagoId,
        int? configuracionTarjetaId,
        bool? permitido = null,
        int? maxCuotasSinInteres = null,
        int? maxCuotasConInteres = null,
        decimal? porcentajeRecargo = null,
        decimal? porcentajeDescuentoMaximo = null)
    {
        var tarjeta = new ProductoCondicionPagoTarjeta
        {
            ProductoCondicionPagoId = productoCondicionPagoId,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Permitido = permitido,
            MaxCuotasSinInteres = maxCuotasSinInteres,
            MaxCuotasConInteres = maxCuotasConInteres,
            PorcentajeRecargo = porcentajeRecargo,
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo
        };

        _context.ProductoCondicionesPagoTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }
}
