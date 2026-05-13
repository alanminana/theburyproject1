using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Caracterizacion legacy/admin del resolver por producto; no define contrato canonico de Nueva Venta.
/// </summary>
[Trait("Area", "LegacyPagoPorProducto")]
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
    public async Task CondicionInactiva_NoParticipaYDevuelveFallbackGlobal()
    {
        var producto = await SeedProductoAsync();
        await SeedCondicionAsync(
            producto.Id,
            TipoPago.CreditoPersonal,
            permitido: false,
            maxCuotasCredito: 3,
            activo: false);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.CreditoPersonal,
            maxCuotasCreditoGlobal: 24);

        Assert.True(resultado.Permitido);
        Assert.Equal(24, resultado.MaxCuotasCredito);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuentePermitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuenteRestriccion);
        Assert.Empty(resultado.Bloqueos);
        Assert.Empty(resultado.Restricciones);
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
    public async Task ReglaEspecificaTarjetaInactiva_NoParticipaYUsaGeneral()
    {
        var producto = await SeedProductoAsync();
        var tarjeta = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 12);
        await SeedReglaTarjetaAsync(condicion.Id, null, maxCuotasSinInteres: 9);
        await SeedReglaTarjetaAsync(
            condicion.Id,
            tarjeta.Id,
            permitido: false,
            maxCuotasSinInteres: 3,
            activo: false);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            tarjeta.Id);

        Assert.True(resultado.Permitido);
        Assert.Equal(9, resultado.MaxCuotasSinInteres);
        Assert.Empty(resultado.Bloqueos);
        Assert.Contains(resultado.Restricciones, r =>
            r.ProductoId == producto.Id
            && r.Valor == 9
            && r.Fuente == FuenteCondicionPagoEfectiva.TarjetaGeneral);
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
        decimal? porcentajeDescuentoMaximo = null,
        bool activo = true)
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
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo,
            Activo = activo
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
        decimal? porcentajeDescuentoMaximo = null,
        bool activo = true)
    {
        var tarjeta = new ProductoCondicionPagoTarjeta
        {
            ProductoCondicionPagoId = productoCondicionPagoId,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Permitido = permitido,
            MaxCuotasSinInteres = maxCuotasSinInteres,
            MaxCuotasConInteres = maxCuotasConInteres,
            PorcentajeRecargo = porcentajeRecargo,
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo,
            Activo = activo
        };

        _context.ProductoCondicionesPagoTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ProductoCondicionPagoPlan> SeedPlanAsync(
        int productoCondicionPagoId,
        int? productoCondicionPagoTarjetaId,
        int cantidadCuotas,
        decimal ajustePorcentaje = 0m,
        bool activo = true)
    {
        var plan = new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = productoCondicionPagoId,
            ProductoCondicionPagoTarjetaId = productoCondicionPagoTarjetaId,
            CantidadCuotas = cantidadCuotas,
            AjustePorcentaje = ajustePorcentaje,
            Activo = activo
        };

        _context.ProductoCondicionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    // --- Tests Fase 15.4: planes disponibles ---

    [Fact]
    public async Task PlanActivoGeneralDelMedio_AparecePlanesDisponibles()
    {
        var producto = await SeedProductoAsync();
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 3);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito);

        Assert.True(resultado.UsaPlanesEspecificos);
        Assert.False(resultado.UsaFallbackGlobalPlanes);
        Assert.Equal(2, resultado.PlanesDisponibles.Count);
        Assert.Contains(resultado.PlanesDisponibles, p => p.CantidadCuotas == 3);
        Assert.Contains(resultado.PlanesDisponibles, p => p.CantidadCuotas == 6);
    }

    [Fact]
    public async Task PlanInactivo_NoAparecePlanesDisponibles()
    {
        var producto = await SeedProductoAsync();
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 3, activo: true);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 6, activo: false);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito);

        Assert.Single(resultado.PlanesDisponibles);
        Assert.Equal(3, resultado.PlanesDisponibles[0].CantidadCuotas);
        Assert.DoesNotContain(resultado.PlanesDisponibles, p => p.CantidadCuotas == 6);
    }

    [Fact]
    public async Task PlanEspecificoTarjeta_TienePrioridadSobreGeneralDelMedio()
    {
        var producto = await SeedProductoAsync();
        var visa = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);
        var reglaTarjeta = await SeedReglaTarjetaAsync(condicion.Id, visa.Id);

        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 3, ajustePorcentaje: 0m);
        await SeedPlanAsync(condicion.Id, reglaTarjeta.Id, cantidadCuotas: 3, ajustePorcentaje: 5m);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            visa.Id);

        Assert.Single(resultado.PlanesDisponibles);
        Assert.Equal(3, resultado.PlanesDisponibles[0].CantidadCuotas);
        Assert.Equal(5m, resultado.PlanesDisponibles[0].AjustePorcentaje);
    }

    [Fact]
    public async Task PlanesDeOtraTarjeta_NoAparecen()
    {
        var producto = await SeedProductoAsync();
        var visa = await SeedTarjetaAsync("Visa", TipoTarjeta.Credito);
        var master = await SeedTarjetaAsync("Master", TipoTarjeta.Credito);
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);
        var reglaVisa = await SeedReglaTarjetaAsync(condicion.Id, visa.Id);
        var reglaMaster = await SeedReglaTarjetaAsync(condicion.Id, master.Id);

        await SeedPlanAsync(condicion.Id, reglaVisa.Id, cantidadCuotas: 3);
        await SeedPlanAsync(condicion.Id, reglaMaster.Id, cantidadCuotas: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            visa.Id);

        Assert.Single(resultado.PlanesDisponibles);
        Assert.Equal(3, resultado.PlanesDisponibles[0].CantidadCuotas);
        Assert.DoesNotContain(resultado.PlanesDisponibles, p => p.CantidadCuotas == 6);
    }

    [Fact]
    public async Task SinPlanesActivos_UsaFallbackEscalarYPlanesDisponiblesVacio()
    {
        var producto = await SeedProductoAsync();
        await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 6);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            maxCuotasSinInteresGlobal: 12);

        Assert.False(resultado.UsaPlanesEspecificos);
        Assert.True(resultado.UsaFallbackGlobalPlanes);
        Assert.Empty(resultado.PlanesDisponibles);
        Assert.Equal(6, resultado.MaxCuotasSinInteres);
    }

    [Fact]
    public async Task ConPlanesActivos_MaximosEscalaresSeConservanSinCambios()
    {
        var producto = await SeedProductoAsync();
        var condicion = await SeedCondicionAsync(
            producto.Id,
            TipoPago.TarjetaCredito,
            maxCuotasSinInteres: 6);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 3);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            maxCuotasSinInteresGlobal: 12);

        Assert.Equal(6, resultado.MaxCuotasSinInteres);
        Assert.True(resultado.UsaPlanesEspecificos);
        Assert.Single(resultado.PlanesDisponibles);
    }

    [Fact]
    public async Task AjustePlanNegativoCeroPosivito_AparecenInformativosYNoAlteranTotal()
    {
        var producto = await SeedProductoAsync();
        var condicion = await SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 3, ajustePorcentaje: -5m);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 6, ajustePorcentaje: 0m);
        await SeedPlanAsync(condicion.Id, null, cantidadCuotas: 12, ajustePorcentaje: 10m);

        var resultado = await _resolver.ResolverAsync(
            new[] { producto.Id },
            TipoPago.TarjetaCredito,
            totalReferencia: 1_000m);

        Assert.Equal(3, resultado.PlanesDisponibles.Count);
        Assert.Contains(resultado.PlanesDisponibles, p => p.CantidadCuotas == 3 && p.AjustePorcentaje == -5m);
        Assert.Contains(resultado.PlanesDisponibles, p => p.CantidadCuotas == 6 && p.AjustePorcentaje == 0m);
        Assert.Contains(resultado.PlanesDisponibles, p => p.CantidadCuotas == 12 && p.AjustePorcentaje == 10m);
        Assert.Equal(1_000m, resultado.TotalReferencia);
        Assert.Equal(1_000m, resultado.TotalSinAplicarAjustes);
    }
}
