using AutoMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests.Integration;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// PROMPT 85 — FIN-S-002 (A1/A2/F1/F2/F3) contra SQL Server real.
//
// NO corre por defecto. Requiere QA_FIN_S002_SQL con una cadena a una BD DESCARTABLE cuyo Initial
// Catalog empiece con "qa_fin_s002" (se crea aplicando las migraciones y se elimina al final).
// Nunca toca la BD de desarrollo.
// ---------------------------------------------------------------------------------------------

public sealed class FinS002SqlServerFactAttribute : FactAttribute
{
    public FinS002SqlServerFactAttribute()
    {
        var skip = FinS002SqlEnv.SkipReason();
        if (skip != null) Skip = skip;
    }
}

internal static class FinS002SqlEnv
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("QA_FIN_S002_SQL");

    public static string? SkipReason()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "BLOCKED: falta QA_FIN_S002_SQL (BD SQL Server descartable).";
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        return catalog.StartsWith("qa_fin_s002", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"BLOCKED: Initial Catalog '{catalog}' no empieza con 'qa_fin_s002' (protección de la BD de desarrollo).";
    }
}

public class FinS002VentaSqlServerTests : IAsyncLifetime
{
    private int _seq;
    private AppDbContext _context = default!;
    private VentaService _service = default!;
    private readonly StubValidacionVentaOverposting _validacion = new();

    public async Task InitializeAsync()
    {
        if (FinS002SqlEnv.SkipReason() != null) return;

        var csb = new SqlConnectionStringBuilder(FinS002SqlEnv.ConnectionString);
        var catalogo = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        await using (var c = new SqlConnection(csb.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand(
                $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END; CREATE DATABASE [{catalogo}]; " +
                $"ALTER DATABASE [{catalogo}] SET READ_COMMITTED_SNAPSHOT {(Environment.GetEnvironmentVariable("QA_FIN_S002_RCSI") == "1" ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE;", c);
            await cmd.ExecuteNonQueryAsync();
        }

        _context = Nuevo();
        await _context.Database.MigrateAsync();

        var caja = new Caja { Codigo = "FS2", Nombre = "Caja FS2" };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();
        var apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m };
        _context.AperturasCaja.Add(apertura);
        await _context.SaveChangesAsync();

        _apertura = apertura;
        _service = CrearServicio(_context);
    }

    private AperturaCaja _apertura = default!;

    private VentaService CrearServicio(AppDbContext ctx)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        return new VentaService(
            ctx, mapper, NullLogger<VentaService>.Instance,
            new StubAlertaStockOverposting(), new StubMovimientoStockOverposting(),
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(ctx, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(ctx), new StubCurrentUserOverposting(), _validacion,
            new StubCajaServiceOverposting(_apertura), new StubCreditoDisponibleOverposting(),
            new StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-NUM] altas concurrentes: todas se crean con numero unico (contador atomico)")]
    public async Task Num_AltasConcurrentes_NumerosUnicos()
    {
        const int n = 8;

        // Numeración previa: el contador debe continuar por encima de lo que ya existe aunque su fila falte (p. ej. números
        // creados antes de la tabla de contadores o por otro camino).
        var previas = new List<int>();
        for (var i = 0; i < 3; i++) previas.Add((await CrearServicio(_context).CreateAsync(await NuevoVmAsync())).Id);
        // Sufijos que cruzan el límite de 6 dígitos (el orden de texto los confundiría): 999999, 1000000 y 1000001.
        var sufijos = new[] { 999999, 1000000, 1000001 };
        for (var i = 0; i < previas.Count; i++)
        {
            var actual = await _context.Ventas.AsNoTracking().Where(v => v.Id == previas[i]).Select(v => v.Numero).SingleAsync();
            var prefijo = string.Join('-', actual.Split('-').Take(2));
            await _context.Database.ExecuteSqlRawAsync("UPDATE [Ventas] SET [Numero] = {0} WHERE [Id] = {1}", $"{prefijo}-{sufijos[i]}", previas[i]);
        }
        var numerosPrevios = await _context.Ventas.AsNoTracking().Where(v => previas.Contains(v.Id)).Select(v => v.Numero).ToListAsync();
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM [NumeracionVentaContadores]");

        var vms = new List<VentaViewModel>();
        for (var i = 0; i < n; i++) vms.Add(await NuevoVmAsync());

        var resultados = await Task.WhenAll(vms.Select(async vm =>
        {
            await using var ctx = Nuevo();
            try { return (Ok: true, Id: (await CrearServicio(ctx).CreateAsync(vm)).Id, Error: (string?)null); }
            catch (Exception ex) { return (Ok: false, Id: 0, Error: ex.GetType().Name + ": " + ex.Message + " | " + ex.InnerException?.Message); }
        }));

        var ids = resultados.Where(r => r.Ok).Select(r => r.Id).ToList();
        var numeros = await _context.Ventas.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => v.Numero).ToListAsync();

        // Integridad: jamás un número duplicado, ni repetido respecto de los previos.
        Assert.Equal(numeros.Count, numeros.Distinct().Count());
        Assert.Empty(numeros.Intersect(numerosPrevios));

        // Disponibilidad: con el contador atómico las 8 altas terminan siempre (con o sin READ_COMMITTED_SNAPSHOT).
        Assert.True(n == ids.Count, "Altas fallidas: " + string.Join(" || ", resultados.Where(r => !r.Ok).Select(r => r.Error)));

        // Continuidad: todos los números nuevos del prefijo están por encima del mayor previo.
        var maximoPrevio = numerosPrevios.Select(x => int.Parse(x.Split('-')[2])).Max();
        Assert.All(numeros, x => Assert.True(int.Parse(x.Split('-')[2]) > maximoPrevio, "Número no continuó tras los previos: " + x));
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-IDEM] FIN-S-018: altas simultaneas con la misma clave crean una sola venta")]
    public async Task Idem_AltasSimultaneasConLaMismaClave_UnaSolaVenta()
    {
        const int n = 8;
        var modelo = await NuevoVmAsync();
        var clave = Guid.NewGuid();

        VentaViewModel Copia() => new()
        {
            ClienteId = modelo.ClienteId,
            FechaVenta = modelo.FechaVenta,
            TipoPago = modelo.TipoPago,
            IdempotencyKey = clave,
            Detalles = modelo.Detalles.Select(d => new VentaDetalleViewModel
            {
                ProductoId = d.ProductoId, Cantidad = d.Cantidad, PrecioUnitario = d.PrecioUnitario, Subtotal = d.Subtotal
            }).ToList()
        };

        var resultados = await Task.WhenAll(Enumerable.Range(0, n).Select(async _ =>
        {
            await using var ctx = Nuevo();
            try { return (Ok: true, Id: (await CrearServicio(ctx).CreateAsync(Copia())).Id, Error: (string?)null); }
            catch (Exception ex) { return (Ok: false, Id: 0, Error: ex.GetType().Name + ": " + ex.Message + " | " + ex.InnerException?.Message); }
        }));

        // Integridad (siempre): exactamente una venta con la clave, y todo envio exitoso devuelve ESA venta.
        await using var lectura = Nuevo();
        var persistidas = await lectura.Ventas.AsNoTracking().Where(v => v.IdempotencyKey == clave).Select(v => v.Id).ToListAsync();
        var unica = Assert.Single(persistidas);
        var exitosos = resultados.Where(r => r.Ok).ToList();
        Assert.NotEmpty(exitosos);
        Assert.All(exitosos, r => Assert.Equal(unica, r.Id));

        // Disponibilidad: todos los envios terminan como reintento del ganador (con o sin READ_COMMITTED_SNAPSHOT; el contador
        // atomico de numeros eliminó la limitación que antes impedía exigirlo sin RCSI).
        Assert.Equal(n, exitosos.Count);
    }

    public async Task DisposeAsync()
    {
        if (FinS002SqlEnv.SkipReason() != null) return;
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static AppDbContext Nuevo() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(FinS002SqlEnv.ConnectionString).Options);

    private async Task<int> NuevoClienteAsync()
    {
        var cli = new Cliente
        {
            Nombre = "FS2", Apellido = "Test", NumeroDocumento = $"9{Interlocked.Increment(ref _seq)}{DateTime.UtcNow.Ticks % 100000}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        _context.Clientes.Add(cli);
        await _context.SaveChangesAsync();
        return cli.Id;
    }

    private async Task<int> NuevoCreditoAsync(int clienteId)
    {
        var credito = new Credito
        {
            ClienteId = clienteId, Numero = $"FS2-{Interlocked.Increment(ref _seq)}-{DateTime.UtcNow.Ticks % 1000000}",
            Estado = EstadoCredito.Activo, SaldoPendiente = 1_000_000m
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito.Id;
    }

    private async Task<VentaViewModel> NuevoVmAsync(TipoPago tipoPago = TipoPago.Efectivo)
    {
        var n = Interlocked.Increment(ref _seq);
        var cat = new Categoria { Codigo = $"CF{n}", Nombre = $"CatFS2{n}" };
        var mar = new Marca { Codigo = $"MF{n}", Nombre = $"MarFS2{n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(mar);
        await _context.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = $"FS2{n}", Nombre = $"ProdFS2{n}", PrecioVenta = 1_000m,
            StockActual = 10, CategoriaId = cat.Id, MarcaId = mar.Id
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();

        return new VentaViewModel
        {
            ClienteId = await NuevoClienteAsync(),
            FechaVenta = DateTime.UtcNow,
            TipoPago = tipoPago,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = prod.Id, Cantidad = 1, PrecioUnitario = 1_000m, Subtotal = 1_000m }
            }
        };
    }

    private async Task<Venta> LeerAsync(int id)
    {
        await using var ctx = Nuevo();
        return await ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == id);
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-A1] Create con credito de otro cliente se rechaza y no persiste")]
    public async Task A1_CreateConCreditoAjeno_Rechaza()
    {
        var vm = await NuevoVmAsync(TipoPago.CreditoPersonal);
        vm.CreditoId = await NuevoCreditoAsync(await NuevoClienteAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(vm));

        Assert.False(await _context.Ventas.AnyAsync(v => v.ClienteId == vm.ClienteId));
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-A2a] Update no permite cambiar el cliente con credito asociado")]
    public async Task A2_UpdateCambiarClienteConCredito_Rechaza()
    {
        var vm = await NuevoVmAsync();
        var creada = await _service.CreateAsync(vm);
        var propio = await NuevoCreditoAsync(vm.ClienteId);
        await _context.Ventas.Where(v => v.Id == creada.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.CreditoId, propio));
        _context.ChangeTracker.Clear();

        var original = await LeerAsync(creada.Id);
        vm.ClienteId = await NuevoClienteAsync();
        vm.RowVersion = original.RowVersion;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(creada.Id, vm));

        Assert.Equal(original.ClienteId, (await LeerAsync(creada.Id)).ClienteId);
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-A2b] Update valida con ids canonicos, no los del POST")]
    public async Task A2_UpdateCreditoPersonal_UsaIdsCanonicos()
    {
        var vm = await NuevoVmAsync(TipoPago.CreditoPersonal);
        var creada = await _service.CreateAsync(vm);
        var propio = await NuevoCreditoAsync(vm.ClienteId);
        await _context.Ventas.Where(v => v.Id == creada.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.CreditoId, propio));
        _context.ChangeTracker.Clear();

        vm.CreditoId = await NuevoCreditoAsync(await NuevoClienteAsync());   // forjado
        vm.RowVersion = (await LeerAsync(creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, vm);

        Assert.Equal(vm.ClienteId, _validacion.UltimoClienteId);
        Assert.Null(_validacion.UltimoCreditoId);   // ni el forjado ni el propio pendiente: cupo general
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-F1] Update conserva FechaVenta si la nueva esta fuera de rango; historicas editables")]
    public async Task F1_UpdateFechaVenta()
    {
        var vm = await NuevoVmAsync();
        var creada = await _service.CreateAsync(vm);
        var historica = DateTime.UtcNow.AddDays(-90);
        await _context.Ventas.Where(v => v.Id == creada.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FechaVenta, historica));
        _context.ChangeTracker.Clear();

        vm.FechaVenta = DateTime.UtcNow.AddDays(-30);
        vm.RowVersion = (await LeerAsync(creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, vm);
        Assert.Equal(historica, (await LeerAsync(creada.Id)).FechaVenta, TimeSpan.FromMilliseconds(5));

        vm.FechaVenta = historica;
        vm.Observaciones = "editada";
        vm.RowVersion = (await LeerAsync(creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, vm);
        var v2 = await LeerAsync(creada.Id);
        Assert.Equal("editada", v2.Observaciones);
        Assert.Equal(historica, v2.FechaVenta, TimeSpan.FromMilliseconds(5));
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-F2] Create ignora FechaConfiguracionCredito forjada")]
    public async Task F2_CreateIgnoraFechaConfiguracionCredito()
    {
        var vm = await NuevoVmAsync();
        vm.FechaConfiguracionCredito = new DateTime(2020, 1, 1);

        var creada = await _service.CreateAsync(vm);

        Assert.Null((await LeerAsync(creada.Id)).FechaConfiguracionCredito);
    }

    [FinS002SqlServerFact(DisplayName = "PROMPT 85 [FS2-F3] Create ignora CreatedAt forjado")]
    public async Task F3_CreateIgnoraCreatedAt()
    {
        var vm = await NuevoVmAsync();
        vm.CreatedAt = new DateTime(2001, 1, 1);

        var creada = await _service.CreateAsync(vm);

        var v = await LeerAsync(creada.Id);
        Assert.True(v.CreatedAt >= DateTime.UtcNow.AddMinutes(-5), $"CreatedAt forjado persistido: {v.CreatedAt}");
    }
}


