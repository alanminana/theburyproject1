using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase 17: dashboard operativo ML.
/// El servicio es de solo lectura: agrega KPIs y alertas sin tocar datos,
/// sin API client (no requiere tokens) y sin romper ante datos incompletos.
/// </summary>
public class MercadoLibreDashboardServiceTests
{
    private static (MercadoLibreDashboardService Servicio, TestDbContextFactory Factory) BuildServicio()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var configService = new MercadoLibreConfiguracionService(
            factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var servicio = new MercadoLibreDashboardService(
            factory, configService, NullLogger<MercadoLibreDashboardService>.Instance);

        return (servicio, factory);
    }

    // ------------------------------------------------------------------
    // Helpers de siembra
    // ------------------------------------------------------------------

    private static async Task SembrarConfigAsync(TestDbContextFactory factory, bool modoSimulacion, int? accountId = null)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreConfiguraciones.Add(new MercadoLibreConfiguracion
        {
            ModoSimulacion = modoSimulacion,
            AccountId = accountId
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<int> SembrarCuentaAsync(TestDbContextFactory factory, string nickname = "TEST")
    {
        await using var ctx = factory.CreateDbContext();
        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
            Nickname = nickname,
            SiteId = "MLA",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true,
            UltimaImportacionListingsUtc = DateTime.UtcNow.AddHours(-2)
        };
        ctx.MercadoLibreAccounts.Add(cuenta);
        await ctx.SaveChangesAsync();
        return cuenta.Id;
    }

    private static async Task<int> SembrarProductoAsync(TestDbContextFactory factory, decimal stock, decimal precio)
    {
        await using var ctx = factory.CreateDbContext();
        var marca = new Marca { Codigo = $"M{Random.Shared.Next(1000, 9999)}", Nombre = "Marca QA", Activo = true };
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"P{Random.Shared.Next(1000, 9999)}",
            Nombre = "Producto QA",
            CategoriaId = 1, // seed de AppDbContext
            MarcaId = marca.Id,
            StockActual = stock,
            PrecioVenta = precio,
            PrecioCompra = 0m,
            Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto.Id;
    }

    private static async Task SembrarListingAsync(
        TestDbContextFactory factory, int accountId,
        string status = "active", int? productoId = null, bool variaciones = false,
        int availableQuantity = 5, decimal precio = 1000m, long? variacionSinVinculo = null)
    {
        await using var ctx = factory.CreateDbContext();
        var listing = new MercadoLibreListing
        {
            AccountId = accountId,
            ItemId = $"MLA{Random.Shared.Next(100000, 999999)}",
            Titulo = "Pub QA",
            Status = status,
            ProductoId = productoId,
            TieneVariaciones = variaciones,
            AvailableQuantity = availableQuantity,
            Precio = precio
        };
        if (variacionSinVinculo.HasValue)
        {
            listing.Variaciones.Add(new MercadoLibreListingVariation
            {
                VariationId = variacionSinVinculo.Value,
                ProductoId = null,
                AvailableQuantity = availableQuantity,
                Precio = precio
            });
        }
        ctx.MercadoLibreListings.Add(listing);
        await ctx.SaveChangesAsync();
    }

    // No setea VentaId (FK a Venta): el dashboard deriva "venta creada" del
    // EstadoInterno, así que el estado basta para los KPIs de venta.
    private static async Task SembrarOrdenAsync(
        TestDbContextFactory factory, int accountId,
        MercadoLibreOrderEstadoInterno estado,
        MercadoLibreShipmentEstadoInterno envio = MercadoLibreShipmentEstadoInterno.Pendiente,
        decimal total = 1000m,
        decimal? netoEstimado = null, decimal? netoReal = null)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreOrders.Add(new MercadoLibreOrder
        {
            AccountId = accountId,
            MeliOrderId = Random.Shared.NextInt64(1, long.MaxValue),
            Status = "paid",
            EstadoInterno = estado,
            EstadoEnvioInterno = envio,
            TotalAmount = total,
            NetoEstimado = netoEstimado,
            NetoReal = netoReal,
            FechaCreacionUtc = DateTime.UtcNow.AddHours(-1)
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SembrarClaimAsync(
        TestDbContextFactory factory, int orderId,
        MercadoLibreClaimEstado estado,
        MercadoLibreClaimAccionStock accionStock = MercadoLibreClaimAccionStock.NoReingresar)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreClaims.Add(new MercadoLibreClaim
        {
            MercadoLibreOrderId = orderId,
            Estado = estado,
            AccionStock = accionStock,
            Tipo = MercadoLibreClaimTipo.Devolucion
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SembrarSyncLogAsync(TestDbContextFactory factory, bool exito, int? listingId = null, string operacion = "PushStock")
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
        {
            Operacion = operacion,
            Exito = exito,
            ListingId = listingId,
            Detalle = exito ? "ok" : "error simulado"
        });
        await ctx.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // 1. Carga sin datos
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_sin_datos_no_rompe_y_devuelve_ceros()
    {
        var (servicio, _) = BuildServicio();

        var vm = await servicio.GetDashboardAsync();

        Assert.NotNull(vm);
        Assert.Equal(0, vm.Publicaciones.Total);
        Assert.Equal(0, vm.Ordenes.Total);
        Assert.Equal(0, vm.Webhooks.Recibidos);
        Assert.False(vm.Conexion.CuentaConectada);
    }

    // ------------------------------------------------------------------
    // 2 + 3. Conexión + simulación
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_refleja_cuenta_conectada()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory, "ELSALCHIPAPAS");
        await SembrarConfigAsync(factory, modoSimulacion: true, accountId: cuentaId);

        var vm = await servicio.GetDashboardAsync();

        Assert.True(vm.Conexion.CuentaConectada);
        Assert.Equal("ELSALCHIPAPAS", vm.Conexion.Nickname);
        Assert.Equal("MLA", vm.Conexion.SiteId);
        Assert.NotNull(vm.Conexion.UltimaImportacionUtc);
    }

    [Fact]
    public async Task Dashboard_refleja_modo_simulacion()
    {
        var (servicio, factory) = BuildServicio();
        await SembrarConfigAsync(factory, modoSimulacion: false);

        var vm = await servicio.GetDashboardAsync();

        Assert.False(vm.Conexion.ModoSimulacion);
        // Sincronización real activa debe disparar alerta.
        Assert.Contains(vm.Alertas, a => a.Titulo == "Sincronización real activa");
    }

    // ------------------------------------------------------------------
    // 4 + 5. Publicaciones
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_cuenta_publicaciones_por_estado_y_vinculo()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        var productoId = await SembrarProductoAsync(factory, stock: 10, precio: 1000m);

        await SembrarListingAsync(factory, cuentaId, status: "active", productoId: productoId);
        await SembrarListingAsync(factory, cuentaId, status: "paused", productoId: null);
        await SembrarListingAsync(factory, cuentaId, status: "active", productoId: null, variaciones: true);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(3, vm.Publicaciones.Total);
        Assert.Equal(2, vm.Publicaciones.Activas);
        Assert.Equal(1, vm.Publicaciones.Pausadas);
        Assert.Equal(2, vm.Publicaciones.SinVincular);
        Assert.Equal(1, vm.Publicaciones.ConVariaciones);
    }

    // ------------------------------------------------------------------
    // 6. Órdenes por estado + ventas
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_cuenta_ordenes_por_estado()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.Importada);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, netoEstimado: 800m);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.Error);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(3, vm.Ordenes.Total);
        Assert.Equal(1, vm.Ordenes.Pendientes);
        Assert.Equal(1, vm.Ordenes.ConVentaCreada);
        Assert.Equal(1, vm.Ordenes.ConError);
        Assert.Equal(1, vm.Ordenes.VentasCreadas);
    }

    // ------------------------------------------------------------------
    // 7. Liquidaciones
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_cuenta_liquidaciones_pendientes_y_liquidadas()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, netoEstimado: 800m);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.Liquidada, netoEstimado: 900m, netoReal: 870m);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(1, vm.Liquidaciones.Pendientes);
        Assert.Equal(1, vm.Liquidaciones.Liquidadas);
        Assert.Equal(800m, vm.Liquidaciones.NetoEstimadoPendiente);
        Assert.Equal(870m, vm.Liquidaciones.NetoRealAcreditado);
        Assert.Equal(-30m, vm.Liquidaciones.DiferenciaEstimadoVsReal);
    }

    // ------------------------------------------------------------------
    // 8. Envíos demorados / cancelados
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_cuenta_envios_demorados_y_cancelados()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, envio: MercadoLibreShipmentEstadoInterno.Demorado);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, envio: MercadoLibreShipmentEstadoInterno.Cancelado);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, envio: MercadoLibreShipmentEstadoInterno.Entregado);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(1, vm.Envios.Demorados);
        Assert.Equal(1, vm.Envios.Cancelados);
        Assert.Equal(1, vm.Envios.Entregados);
    }

    // ------------------------------------------------------------------
    // 9. Claims pendientes
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_cuenta_claims_pendientes_y_resueltos()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada);

        await SembrarClaimAsync(factory, 1, MercadoLibreClaimEstado.PendienteRevision);
        await SembrarClaimAsync(factory, 1, MercadoLibreClaimEstado.Resuelto, MercadoLibreClaimAccionStock.ReingresarStock);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(1, vm.Reclamos.PendientesRevision);
        Assert.Equal(1, vm.Reclamos.Abiertos);
        Assert.Equal(1, vm.Reclamos.Resueltos);
        Assert.Equal(1, vm.Reclamos.ReingresosStock);
    }

    // ------------------------------------------------------------------
    // 10. Alerta por publicaciones sin vincular
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_genera_alerta_publicaciones_sin_vincular()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        await SembrarListingAsync(factory, cuentaId, productoId: null);

        var vm = await servicio.GetDashboardAsync();

        Assert.Contains(vm.Alertas, a => a.Titulo == "Publicaciones sin vincular" && a.Contador == 1);
    }

    // ------------------------------------------------------------------
    // 11. Alerta por liquidaciones pendientes
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_genera_alerta_liquidaciones_pendientes()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada, netoEstimado: 500m);

        var vm = await servicio.GetDashboardAsync();

        Assert.Contains(vm.Alertas, a => a.Titulo == "Liquidaciones pendientes");
    }

    // ------------------------------------------------------------------
    // 12. Alerta por sync fallido
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_genera_alerta_sync_fallido()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        await SembrarSyncLogAsync(factory, exito: false);

        var vm = await servicio.GetDashboardAsync();

        Assert.Contains(vm.Alertas, a => a.Severidad == MercadoLibreAlertaSeveridad.Error && a.Titulo == "Sincronizaciones fallidas");
        Assert.Equal(1, vm.StockPrecio.UltimosSyncFallidos);
    }

    // ------------------------------------------------------------------
    // 13. No modifica datos
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_no_modifica_datos()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);
        var productoId = await SembrarProductoAsync(factory, stock: 4, precio: 1000m);
        await SembrarListingAsync(factory, cuentaId, productoId: productoId, availableQuantity: 1, precio: 1000m);
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.VentaCreada);

        int ListingsCount, OrdersCount, LogsCount, ConfigCount;
        await using (var ctx = factory.CreateDbContext())
        {
            ListingsCount = await ctx.MercadoLibreListings.CountAsync();
            OrdersCount = await ctx.MercadoLibreOrders.CountAsync();
            LogsCount = await ctx.MercadoLibreSyncLogs.CountAsync();
            ConfigCount = await ctx.MercadoLibreConfiguraciones.CountAsync();
        }

        await servicio.GetDashboardAsync();
        await servicio.GetDashboardAsync(); // dos veces: idempotente

        await using (var ctx = factory.CreateDbContext())
        {
            Assert.Equal(ListingsCount, await ctx.MercadoLibreListings.CountAsync());
            Assert.Equal(OrdersCount, await ctx.MercadoLibreOrders.CountAsync());
            Assert.Equal(LogsCount, await ctx.MercadoLibreSyncLogs.CountAsync());
            Assert.Equal(ConfigCount, await ctx.MercadoLibreConfiguraciones.CountAsync());

            // El listing conserva su precio/stock originales.
            var listing = await ctx.MercadoLibreListings.FirstAsync();
            Assert.Equal(1000m, listing.Precio);
            Assert.Equal(1, listing.AvailableQuantity);
        }
    }

    // ------------------------------------------------------------------
    // 14. Diferencia de stock ERP vs ML (indicador) + alerta de oversell
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_detecta_diferencia_stock_erp_vs_ml()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        // ERP 4 vs ML 1 → ERP > ML (sin sobreventa).
        var prodA = await SembrarProductoAsync(factory, stock: 4, precio: 1000m);
        await SembrarListingAsync(factory, cuentaId, productoId: prodA, availableQuantity: 1, precio: 1000m);

        // ERP 0 vs ML 3 → ML > ERP (riesgo de sobreventa).
        var prodB = await SembrarProductoAsync(factory, stock: 0, precio: 1000m);
        await SembrarListingAsync(factory, cuentaId, productoId: prodB, availableQuantity: 3, precio: 1000m);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(2, vm.StockPrecio.ConDiferenciaStock);
        Assert.Equal(1, vm.StockPrecio.StockMlMayorErp);
        Assert.Equal(1, vm.StockPrecio.StockErpMayorMl);
        Assert.Contains(vm.Alertas, a => a.Titulo == "Stock ML mayor que ERP");
    }

    // ------------------------------------------------------------------
    // 15. No falla con datos incompletos (sin netos, sin producto)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_no_falla_con_datos_incompletos()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        // Orden liquidada SIN netos cargados; listing sin producto.
        await SembrarOrdenAsync(factory, cuentaId, MercadoLibreOrderEstadoInterno.Liquidada, netoEstimado: null, netoReal: null);
        await SembrarListingAsync(factory, cuentaId, productoId: null);

        var vm = await servicio.GetDashboardAsync();

        Assert.NotNull(vm);
        Assert.Equal(0m, vm.Liquidaciones.NetoRealAcreditado);
        Assert.Equal(1, vm.Liquidaciones.Liquidadas);
    }

    // ------------------------------------------------------------------
    // Preguntas / mensajes (Fase 16)
    // ------------------------------------------------------------------

    private static async Task SembrarPreguntaAsync(
        TestDbContextFactory factory, int accountId, MercadoLibreQuestionEstado estado, int? listingId = null)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreQuestions.Add(new MercadoLibreQuestion
        {
            AccountId = accountId,
            QuestionId = Random.Shared.NextInt64(1, long.MaxValue),
            ListingId = listingId,
            TextoPregunta = "QA",
            Estado = estado
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task SembrarMensajeAsync(
        TestDbContextFactory factory, int accountId, MercadoLibreMessageDireccion direccion, MercadoLibreMessageEstado estado)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.MercadoLibreMessages.Add(new MercadoLibreMessage
        {
            AccountId = accountId,
            MessageId = Guid.NewGuid().ToString("N"),
            Texto = "QA",
            Direccion = direccion,
            Estado = estado
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Dashboard_cuenta_preguntas_y_mensajes_pendientes()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        await SembrarPreguntaAsync(factory, cuentaId, MercadoLibreQuestionEstado.Pendiente);
        await SembrarPreguntaAsync(factory, cuentaId, MercadoLibreQuestionEstado.Respondida);
        await SembrarMensajeAsync(factory, cuentaId, MercadoLibreMessageDireccion.Entrante, MercadoLibreMessageEstado.Recibido);

        var vm = await servicio.GetDashboardAsync();

        Assert.Equal(1, vm.Preguntas.Pendientes);
        Assert.Equal(1, vm.Preguntas.Respondidas);
        Assert.Equal(2, vm.Preguntas.Total);
        Assert.Equal(1, vm.Mensajes.RecibidosPendientes);
    }

    [Fact]
    public async Task Dashboard_genera_alertas_por_preguntas_y_mensajes_pendientes()
    {
        var (servicio, factory) = BuildServicio();
        var cuentaId = await SembrarCuentaAsync(factory);
        await SembrarConfigAsync(factory, true, cuentaId);

        await SembrarPreguntaAsync(factory, cuentaId, MercadoLibreQuestionEstado.Pendiente);
        await SembrarMensajeAsync(factory, cuentaId, MercadoLibreMessageDireccion.Entrante, MercadoLibreMessageEstado.Recibido);

        var vm = await servicio.GetDashboardAsync();

        Assert.Contains(vm.Alertas, a => a.Titulo == "Preguntas pendientes");
        Assert.Contains(vm.Alertas, a => a.Titulo == "Mensajes sin responder");
    }
}
