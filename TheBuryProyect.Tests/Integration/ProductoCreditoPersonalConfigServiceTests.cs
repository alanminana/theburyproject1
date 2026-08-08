using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

public class ProductoCreditoPersonalConfigServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoCreditoPersonalConfigService _service;
    private readonly ConfiguracionPagoService _configuracionPagoService;

    public ProductoCreditoPersonalConfigServiceTests()
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

        _configuracionPagoService = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);

        _service = new ProductoCreditoPersonalConfigService(_context, _configuracionPagoService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Producto> SeedProducto()
    {
        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..10],
            Nombre = "Producto test",
            Categoria = new Categoria { Nombre = $"Cat {Guid.NewGuid():N}", Codigo = Guid.NewGuid().ToString("N")[..10] },
            Marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = Guid.NewGuid().ToString("N")[..10] },
            PrecioCompra = 0m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    // -------------------------------------------------------------------------
    // Round-trip básico: hereda / propio / bloqueado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Guardar_Obtener_RoundTripDeModoActivoYRestriccion()
    {
        var producto = await SeedProducto();

        // ML5: el payload trae TasaMensual (0/10/25 — como enviaría un cliente honesto con el
        // input viejo, o uno manipulado) pero ninguno de esos valores es autoridad del producto.
        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            AdmiteCreditoPersonal = true,
            MaxCuotasCredito = 12,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 0m, Activo = true, Orden = 1 },
                new() { CantidadCuotas = 3, TasaMensual = 10m, Activo = true, Orden = 3 },
                new() { CantidadCuotas = 6, TasaMensual = 25m, Activo = false, Orden = 6 }
            }
        }, "tester");

        Assert.True(ok, string.Join("; ", errores));

        var config = await _service.ObtenerAsync(producto.Id);

        Assert.Equal(ModoCreditoPersonalProducto.ConfiguracionPropia, config.Modo);
        Assert.True(config.AdmiteCreditoPersonal);
        Assert.Equal(12, config.MaxCuotasCredito);
        Assert.True(config.Cuotas.First(c => c.CantidadCuotas == 1).Activo);
        Assert.True(config.Cuotas.First(c => c.CantidadCuotas == 3).Activo);
        // Sin plan global configurado en este test para ninguna de las dos cantidades: el
        // recargo mostrado es "no disponible" (null), nunca el 0%/10% que mandó el payload.
        Assert.Null(config.Cuotas.First(c => c.CantidadCuotas == 1).TasaMensual);
        Assert.Null(config.Cuotas.First(c => c.CantidadCuotas == 3).TasaMensual);

        // La fila inactiva (Id = 0) no se persiste: vuelve como plantilla inactiva.
        var persistidas = await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Equal(2, persistidas.Count);

        // ML5: el 0%/10%/25% enviados en el payload nunca se persisten como autoridad propia.
        Assert.All(persistidas, p => Assert.Null(p.TasaMensual));
    }

    [Fact]
    public async Task Guardar_BloquearCreditoPersonal_PersisteRestriccion()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = false
        }, "tester");

        Assert.True(ok, string.Join("; ", errores));

        var restriccion = await _context.ProductoCreditoRestricciones
            .FirstOrDefaultAsync(r => r.ProductoId == producto.Id && r.Activo);

        Assert.NotNull(restriccion);
        Assert.False(restriccion!.Permitido);

        var config = await _service.ObtenerAsync(producto.Id);
        Assert.False(config.AdmiteCreditoPersonal);
        Assert.Equal(ModoCreditoPersonalProducto.NoDisponible, config.Modo);
    }

    [Fact]
    public async Task Guardar_DesactivarPlanExistente_ConservaFilaInactiva()
    {
        var producto = await SeedProducto();

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 10m, Activo = true, Orden = 3 }
            }
        }, "tester");

        var guardada = await _context.ProductoCreditoPersonalCuotas
            .FirstAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 3);

        var (ok, _) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.HeredaGlobal,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { Id = guardada.Id, CantidadCuotas = 3, TasaMensual = 10m, Activo = false, Orden = 3 }
            }
        }, "tester");

        Assert.True(ok);

        var fila = await _context.ProductoCreditoPersonalCuotas
            .FirstAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 3);
        Assert.False(fila.Activo);
        // ML5: la columna nunca se escribió (nació null en el alta) y desactivar tampoco la toca.
        Assert.Null(fila.TasaMensual);
    }

    // -------------------------------------------------------------------------
    // ML5 — Contrato congelado: Producto no define porcentajes bajo ningún valor entrante
    // -------------------------------------------------------------------------

    // ML5 — reemplaza "...PersisteCeroDistintoDeNull" (ML3): bajo el contrato viejo el 0 %
    // enviado se guardaba tal cual; ahora ningún valor entrante (0, null o uno manipulado) se
    // convierte en autoridad del producto. Ver goal ML5, test A/B.
    [Fact]
    public async Task Guardar_PlanPropioConCualquierTasaEnviada_NuncaSePersisteComoAutoridad()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 0m, Activo = true, Orden = 6 },  // 0% explícito enviado
                new() { CantidadCuotas = 9, TasaMensual = 99m, Activo = true, Orden = 9 }  // payload manipulado (99%)
            }
        }, "tester");

        Assert.True(ok, string.Join("; ", errores));

        var persistidas = await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Equal(2, persistidas.Count);
        // Ni el 0% ni el 99% enviados se convierten en autoridad: ambas filas nacen en null.
        Assert.All(persistidas, p => Assert.Null(p.TasaMensual));

        var config = await _service.ObtenerAsync(producto.Id);
        var plan6 = config.Cuotas.Single(c => c.CantidadCuotas == 6);
        var plan9 = config.Cuotas.Single(c => c.CantidadCuotas == 9);

        Assert.True(plan6.Activo);
        Assert.True(plan9.Activo);
        // Sin plan global configurado para 6 ni 9 cuotas en este test: no disponible, nunca
        // el 99% que intentó colarse.
        Assert.Null(plan6.TasaMensual);
        Assert.Null(plan9.TasaMensual);
    }

    [Fact]
    public async Task Guardar_PlanPropioConTasaEnviada_LaColumnaNaceNullSinTransformacion()
    {
        var producto = await SeedProducto();

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 12, TasaMensual = 10m, Activo = true, Orden = 12 }
            }
        }, "tester");

        var entidad = await _context.ProductoCreditoPersonalCuotas
            .AsNoTracking()
            .SingleAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 12);

        // ML5: GuardarAsync ignora explícitamente TasaMensual del payload en el alta — la
        // columna nace en null, nunca en el 10% enviado (ver goal ML5, "Persistencia legacy").
        Assert.Null(entidad.TasaMensual);
    }

    // -------------------------------------------------------------------------
    // Backend autoritativo: rechazo de datos inválidos y de combinaciones contradictorias
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Guardar_CantidadDuplicada_Rechaza()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true },
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        }, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("duplicad", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());
    }

    [Fact]
    public async Task Guardar_RecargoNegativo_Rechaza()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 4, TasaMensual = -1m, Activo = true }
            }
        }, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("negativ", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());
    }

    [Fact]
    public async Task Guardar_BloqueadoConPlanesActivos_Rechaza()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = false,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 5m, Activo = true }
            }
        }, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("bloquear", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await _context.ProductoCreditoRestricciones.FirstOrDefaultAsync(r => r.ProductoId == producto.Id));
    }

    [Fact]
    public async Task Guardar_HeredaGlobalConPlanesActivos_Rechaza()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.HeredaGlobal,
            AdmiteCreditoPersonal = true,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 5m, Activo = true }
            }
        }, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("Hereda", StringComparison.Ordinal));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());
    }

    [Fact]
    public async Task Guardar_ModoNoDisponibleConAdmiteCreditoPersonalEnTrue_Rechaza()
    {
        // Payload manipulado: el modo declarado dice "bloqueado" pero el flag real dice "admite".
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = true
        }, "tester");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("modo declarado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Guardar_IdDePlanDeOtroProducto_NoMutaElPlanAjeno()
    {
        var productoA = await SeedProducto();
        var productoB = await SeedProducto();

        await _service.GuardarAsync(productoA.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, Activo = true, Orden = 1 }
            }
        }, "tester");
        var planAjeno = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.ProductoId == productoA.Id && c.CantidadCuotas == 6);

        // DevTools: se envía el Id de la fila de productoA (y una tasa manipulada) al guardar productoB.
        var (ok, _) = await _service.GuardarAsync(productoB.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { Id = planAjeno.Id, CantidadCuotas = 6, TasaMensual = 99m, Activo = true, Orden = 2 }
            }
        }, "tester");

        Assert.True(ok);

        var planAjenoDespues = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.Id == planAjeno.Id);
        Assert.Equal(productoA.Id, planAjenoDespues.ProductoId);
        Assert.Equal(1, planAjenoDespues.Orden); // sin cambios: el Id ajeno se ignora, no se tocó esta fila
        Assert.Null(planAjenoDespues.TasaMensual); // ML5: tampoco tenía tasa propia

        var planB = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.ProductoId == productoB.Id && c.CantidadCuotas == 6);
        Assert.Equal(2, planB.Orden); // se creó como fila nueva de productoB
        Assert.Null(planB.TasaMensual); // ML5: el 99% del payload nunca se persiste
    }

    // -------------------------------------------------------------------------
    // Herencia dinámica de la configuración global (ML5) y precedencia propio > global
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Obtener_SinPlanesPropios_HeredaLosPlanesGlobalesActivosDeML5()
    {
        var producto = await SeedProducto();
        await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(
            new List<TheBuryProject.ViewModels.CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 15, TasaMensual = 7m, Activo = true }
            }, "admin");

        var config = await _service.ObtenerAsync(producto.Id);

        Assert.Equal(ModoCreditoPersonalProducto.HeredaGlobal, config.Modo);
        var plantilla = config.Cuotas.Single(c => c.CantidadCuotas == 15);
        Assert.False(plantilla.Activo); // es plantilla candidata, no un plan propio
        Assert.Equal(7m, plantilla.TasaMensual);
    }

    [Fact]
    public async Task Obtener_PlanGlobalInactivo_NoSeHereda()
    {
        var producto = await SeedProducto();
        await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(
            new List<TheBuryProject.ViewModels.CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 17, TasaMensual = 7m, Activo = false }
            }, "admin");

        var config = await _service.ObtenerAsync(producto.Id);

        Assert.DoesNotContain(config.Cuotas, c => c.CantidadCuotas == 17);
    }

    // ML5 — invierte completamente "...NoQuedaAfectadoPorPorcentajeGlobalDistinto" (contrato
    // viejo): antes el editor mostraba el valor propio persistido del producto por encima del
    // global; ahora el editor SIEMPRE muestra el recargo del plan global vigente, incluida una
    // tasa legacy ya persistida en la columna (dato histórico inerte, nunca se reescribe pero
    // tampoco se muestra como vigente). Ver goal ML5, test E.
    [Fact]
    public async Task Obtener_ProductoConPlanPropioActivo_MuestraElPorcentajeDelPlanGlobalNoElHistoricoDelProducto()
    {
        var producto = await SeedProducto();
        // Plan global vigente: 8% para 6 cuotas.
        await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(
            new List<TheBuryProject.ViewModels.CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }, "admin");

        // El producto activa esa cantidad; el 20% que "envía" nunca se persiste (ML5).
        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 20m, Activo = true }
            }
        }, "tester");

        var config = await _service.ObtenerAsync(producto.Id);
        var plan6 = config.Cuotas.Single(c => c.CantidadCuotas == 6);

        Assert.True(plan6.Activo);
        // El editor siempre muestra el recargo del plan global vigente (8%), nunca el 20% que se
        // intentó enviar ni ningún valor propio histórico del producto.
        Assert.Equal(8m, plan6.TasaMensual);

        // Simula un dato legacy directamente en la columna (pre-ML5, jamás reescrito por
        // GuardarAsync): tampoco se muestra como vigente.
        var entidad = await _context.ProductoCreditoPersonalCuotas
            .SingleAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 6);
        entidad.TasaMensual = 2.5m;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var configTrasLegacy = await _service.ObtenerAsync(producto.Id);
        Assert.Equal(8m, configTrasLegacy.Cuotas.Single(c => c.CantidadCuotas == 6).TasaMensual);
    }

    // ML5 — goal test C: 0% global se muestra explícito, no vacío/null.
    [Fact]
    public async Task Obtener_PlanGlobalEnCero_MuestraCeroExplicitoNoVacio()
    {
        var producto = await SeedProducto();
        await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(
            new List<TheBuryProject.ViewModels.CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 4, TasaMensual = 0m, Activo = true }
            }, "admin");

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 4, Activo = true }
            }
        }, "tester");

        var config = await _service.ObtenerAsync(producto.Id);
        var plan4 = config.Cuotas.Single(c => c.CantidadCuotas == 4);

        Assert.True(plan4.Activo);
        Assert.NotNull(plan4.TasaMensual);
        Assert.Equal(0m, plan4.TasaMensual!.Value);
    }

    // ML5 — goal test G: el producto activó una cantidad que ya no tiene plan global (o nunca lo
    // tuvo). No se inventa ninguna tasa: se marca "no disponible" (null), igual que la resolución
    // real de venta (ResolverPlanesCreditoPersonalAsync).
    [Fact]
    public async Task Obtener_ProductoConPlanPropioActivo_SinPlanGlobalParaEsaCantidad_NoDisponible()
    {
        var producto = await SeedProducto();
        // Ningún plan global para 50 cuotas.
        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 50, Activo = true }
            }
        }, "tester");

        var config = await _service.ObtenerAsync(producto.Id);
        var plan50 = config.Cuotas.Single(c => c.CantidadCuotas == 50);

        Assert.True(plan50.Activo); // el producto sigue ofreciendo la cantidad...
        Assert.Null(plan50.TasaMensual); // ...pero no hay recargo válido para venderla
    }

    // -------------------------------------------------------------------------
    // Validar() es puro: no toca el ChangeTracker ni la base. GuardarAsync se apoya en esto
    // para poder validar antes del primer cambio persistente (atomicidad Producto + Crédito).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Validar_NoModificaElChangeTrackerNiLlamaASaveChanges()
    {
        var producto = await SeedProducto();
        _context.ChangeTracker.Clear();

        // Config válida
        _service.Validar(new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true }
            }
        });
        Assert.False(_context.ChangeTracker.HasChanges());
        Assert.Empty(_context.ChangeTracker.Entries());

        // Config inválida (contradictoria): tampoco debe tocar el ChangeTracker
        _service.Validar(new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = false,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true }
            }
        });
        Assert.False(_context.ChangeTracker.HasChanges());
        Assert.Empty(_context.ChangeTracker.Entries());

        // La base sigue exactamente como quedó después del seed: nada de Validar() se persistió.
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());
    }

    // -------------------------------------------------------------------------
    // ObtenerAsync(0): candidatos para el modal de alta (todavía sin ProductoId)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Obtener_ConProductoIdCero_DevuelveSoloPlantillasSinRestriccionNiPropios()
    {
        var config = await _service.ObtenerAsync(0);

        Assert.Equal(ModoCreditoPersonalProducto.HeredaGlobal, config.Modo);
        Assert.True(config.AdmiteCreditoPersonal);
        Assert.Null(config.MaxCuotasCredito);
        Assert.All(config.Cuotas, c => Assert.False(c.Activo));
    }
}
