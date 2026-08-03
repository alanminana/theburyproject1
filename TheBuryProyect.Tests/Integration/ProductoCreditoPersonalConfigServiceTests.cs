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
    public async Task Guardar_Obtener_RoundTripDePlanesYRestriccion()
    {
        var producto = await SeedProducto();

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
        Assert.Equal(0m, config.Cuotas.First(c => c.CantidadCuotas == 1).TasaMensual);
        Assert.True(config.Cuotas.First(c => c.CantidadCuotas == 1).Activo);
        Assert.Equal(10m, config.Cuotas.First(c => c.CantidadCuotas == 3).TasaMensual);

        // La fila inactiva (Id = 0) no se persiste: vuelve como plantilla inactiva.
        var persistidas = await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Equal(2, persistidas.Count);
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
        Assert.Equal(10m, fila.TasaMensual);
    }

    // -------------------------------------------------------------------------
    // Plan propio: 0 % es válido y distinguible; el recargo se conserva sin transformación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Guardar_PlanPropioConCeroPorciento_EsValidoYDistinguibleDeHeredar()
    {
        var producto = await SeedProducto();

        var (ok, errores) = await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 0m, Activo = true, Orden = 6 },  // 0% explícito
                new() { CantidadCuotas = 9, TasaMensual = null, Activo = true, Orden = 9 } // hereda
            }
        }, "tester");

        Assert.True(ok, string.Join("; ", errores));

        var config = await _service.ObtenerAsync(producto.Id);
        var plan6 = config.Cuotas.Single(c => c.CantidadCuotas == 6);
        var plan9 = config.Cuotas.Single(c => c.CantidadCuotas == 9);

        Assert.True(plan6.Activo);
        Assert.Equal(0m, plan6.TasaMensual);
        Assert.True(plan9.Activo);
        Assert.Null(plan9.TasaMensual);
    }

    [Fact]
    public async Task Guardar_PlanPropioDiezPorciento_SePersisteComoRecargoTotalSinTransformacion()
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

        // El valor persistido es exactamente el 10% ingresado: ninguna fórmula mensual,
        // compuesta ni multiplicada por cuotas lo transforma en el camino de guardado.
        Assert.Equal(10m, entidad.TasaMensual);
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
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true }
            }
        }, "tester");
        var planAjeno = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.ProductoId == productoA.Id && c.CantidadCuotas == 6);

        // DevTools: se envía el Id de la fila de productoA al guardar productoB.
        var (ok, _) = await _service.GuardarAsync(productoB.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { Id = planAjeno.Id, CantidadCuotas = 6, TasaMensual = 99m, Activo = true }
            }
        }, "tester");

        Assert.True(ok);

        var planAjenoDespues = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.Id == planAjeno.Id);
        Assert.Equal(productoA.Id, planAjenoDespues.ProductoId);
        Assert.Equal(5m, planAjenoDespues.TasaMensual); // sin cambios: el Id ajeno se ignora

        var planB = await _context.ProductoCreditoPersonalCuotas.AsNoTracking()
            .SingleAsync(c => c.ProductoId == productoB.Id && c.CantidadCuotas == 6);
        Assert.Equal(99m, planB.TasaMensual); // se creó como fila nueva de productoB
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

    [Fact]
    public async Task Obtener_ProductoConConfiguracionPropia_NoQuedaAfectadoPorPorcentajeGlobalDistinto()
    {
        var producto = await SeedProducto();
        await _configuracionPagoService.GuardarCuotasCreditoPersonalAsync(
            new List<TheBuryProject.ViewModels.CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true }
            }, "admin");

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
        Assert.Equal(20m, plan6.TasaMensual); // el propio manda; el 5% global no lo pisa
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
