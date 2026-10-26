using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// PROMPT 96 — Configurar (CreditoService.ConfigurarCreditoAsync) vs. Confirmar (VentaService.ConfirmarVentaCreditoAsync)
// sobre la MISMA venta/crédito, con SERVICIOS REALES contra SQL Server real y el interleaving forzado de forma
// determinista (ConcGateInterceptor: una operación se detiene en un comando SQL concreto; el test lanza la otra).
//
// Estados finales válidos (cualquier otro es una mezcla = violación de integridad):
//   S_A  Confirmar ganó : venta Confirmada, crédito Generado con el plan A (12 cuotas, financiado 8.000), 12 cuotas,
//                         un anticipo de 2.000, stock 9, contrato vigente.
//   S_B  Configurar ganó: venta Presupuesto, crédito Configurado con el plan B (6 cuotas, financiado 5.000), 0 cuotas,
//                         sin anticipo, stock 10, contrato invalidado.
//
// Mismo gate de seguridad que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Contrato "generado" = existe un contrato vigente (no borrado) de la venta. El real exige plantilla y un archivo en
/// disco; este reemplazo conserva la parte que importa para la carrera: Configurar invalida el contrato y Confirmar lo exige.
/// </summary>
public class ContratoVigenteEnBdProxy : DispatchProxy
{
    /// <summary>Contexto del scope del servicio real: la lectura comparte conexión y transacción con la operación (como en producción).</summary>
    public AppDbContext Context { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IContratoVentaCreditoService.ExisteContratoGeneradoAsync))
            return ExisteAsync((int)args![0]!);
        throw new NotSupportedException($"{targetMethod?.Name} no está soportado por el stub de contrato de PROMPT 96.");
    }

    private async Task<bool> ExisteAsync(int ventaId) =>
        await Context.ContratosVentaCredito.AsNoTracking().AnyAsync(c => c.VentaId == ventaId && !c.IsDeleted);
}

public sealed class ConfigurarConfirmarSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_cv";

    protected override void ConfigurarServicios(IServiceCollection services)
    {
        services.RemoveAll<IContratoVentaCreditoService>();
        services.AddScoped<IContratoVentaCreditoService>(sp =>
        {
            var proxy = DispatchProxy.Create<IContratoVentaCreditoService, ContratoVigenteEnBdProxy>();
            ((ContratoVigenteEnBdProxy)(object)proxy).Context = sp.GetRequiredService<AppDbContext>();
            return proxy;
        });
    }
}

public class ConfigurarVsConfirmarSqlServerTests : IClassFixture<ConfigurarConfirmarSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(6);

    private const decimal Total = 10_000m;
    private const int CuotasA = 12;
    private const decimal FinanciadoA = 8_000m;
    private const int CuotasB = 6;
    private const decimal FinanciadoB = 5_000m;

    private readonly ConfigurarConfirmarSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public ConfigurarVsConfirmarSqlServerTests(ConfigurarConfirmarSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ------------------------------------------------------------------ compuertas

    private static GatePlan Compuerta(string nombre, Func<string, bool> coincide, int ocurrencia = 1)
    {
        var n = 0;
        return new GatePlan(nombre, sql => coincide(sql) && Interlocked.Increment(ref n) == ocurrencia);
    }

    private static GatePlan SinCompuerta(string nombre) => new(nombre, _ => false);

    private static bool EsEscritura(string sql, string tabla) =>
        sql.Contains($"UPDATE [{tabla}]", StringComparison.Ordinal) || sql.Contains($"INSERT INTO [{tabla}]", StringComparison.Ordinal);

    // ------------------------------------------------------------------ ejecución

    private async Task<string> EjecutarAsync(GatePlan? plan, Func<IServiceProvider, Task<object?>> operacion)
    {
        try
        {
            ConcGateInterceptor.Plan.Value = plan;
            using var scope = _fx.Services.CreateScope();
            var r = await operacion(scope.ServiceProvider);
            return $"OK({r})";
        }
        catch (Exception ex)
        {
            return Clasificar(ex);
        }
        finally
        {
            ConcGateInterceptor.Plan.Value = null;
        }
    }

    private static string Clasificar(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                return $"DEADLOCK_VICTIMA({ex.GetType().Name})";
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private Task<string> Confirmar(int ventaId, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            await sp.GetRequiredService<IVentaService>().ConfirmarVentaCreditoAsync(ventaId)));

    private Task<string> Configurar(Semilla s, int cuotas, decimal anticipo, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
        {
            await sp.GetRequiredService<ICreditoService>().ConfigurarCreditoAsync(ComandoConfigurar(s, cuotas, anticipo));
            return null;
        }));

    private static ConfiguracionCreditoComando ComandoConfigurar(Semilla s, int cuotas, decimal anticipo) => new()
    {
        CreditoId = s.CreditoId,
        VentaId = s.VentaId,
        Monto = Total,
        Anticipo = anticipo,
        CantidadCuotas = cuotas,
        TasaMensual = 3m,
        FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
        MetodoCalculo = MetodoCalculoCredito.Global,
        FuenteConfiguracion = FuenteConfiguracionCredito.Global,
        CuotasMinPermitidas = 1,
        CuotasMaxPermitidas = 24
    };

    private Task<string> ConfigurarB(Semilla s, GatePlan? plan = null) => Configurar(s, CuotasB, Total - FinanciadoB, plan);

    private async Task<bool> LlegoALaCompuertaAsync(GatePlan plan, Task<string> operacion)
    {
        var llego = await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;
        if (!llego) Volcar("no llegó a la compuerta", plan);
        return llego;
    }

    /// <summary>true si la operación NO terminó dentro de la ventana de observación (= quedó bloqueada por un lock).</summary>
    private static async Task<bool> QuedaBloqueadaAsync(Task<string> operacion) =>
        await Task.WhenAny(operacion, Task.Delay(EsperaObservacion)) != operacion;

    private void Volcar(string titulo, GatePlan plan)
    {
        _out.WriteLine($"--- traza SQL {titulo} ({plan.Nombre}) ---");
        foreach (var l in plan.Trace) _out.WriteLine("  " + l);
    }

    // ------------------------------------------------------------------ siembra

    private sealed record Semilla(int VentaId, int CreditoId, int ProductoId, int ContratoId);

    /// <summary>
    /// Venta a crédito con el plan A ya configurado (re-configuración: crédito Configurado, venta Presupuesto con
    /// FechaConfiguracionCredito) o recién creada por el wizard (crédito PendienteConfiguracion, venta PendienteFinanciacion).
    /// </summary>
    private async Task<Semilla> SembrarAsync(bool reconfiguracion = true)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ConcSeed.SembrarAperturaAsync(ctx);
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "Cv", Apellido = "QA" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.AddRange(categoria, marca, cliente);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = Total, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        ctx.Productos.Add(producto);
        foreach (var cuotas in new[] { CuotasA, CuotasB })
            if (!await ctx.ConfiguracionCreditoPersonalCuotas.AnyAsync(c => c.CantidadCuotas == cuotas && c.Activo))
                ctx.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
                {
                    CantidadCuotas = cuotas, TasaMensual = 3m, Activo = true, Orden = cuotas
                });

        var credito = new Credito
        {
            ClienteId = cliente.Id, Numero = "CRCV" + sufijo,
            Estado = reconfiguracion ? EstadoCredito.Configurado : EstadoCredito.PendienteConfiguracion,
            TasaInteres = 3m, MontoSolicitado = FinanciadoA, MontoAprobado = FinanciadoA, SaldoPendiente = FinanciadoA,
            CantidadCuotas = reconfiguracion ? CuotasA : 0, MontoCuota = 0m, TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1)
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id, CreditoId = credito.Id, Numero = "VCV" + sufijo,
            Estado = reconfiguracion ? EstadoVenta.Presupuesto : EstadoVenta.PendienteFinanciacion,
            TipoPago = TipoPago.CreditoPersonal, Total = Total, Subtotal = Total, FechaVenta = DateTime.UtcNow,
            FechaConfiguracionCredito = reconfiguracion ? DateTime.UtcNow : null,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = Total, Subtotal = Total }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();

        var plantilla = new PlantillaContratoCredito
        {
            Nombre = "Plantilla " + sufijo, NombreVendedor = "QA", DomicilioVendedor = "QA", CiudadFirma = "QA",
            Jurisdiccion = "QA", TextoContrato = "x", TextoPagare = "x"
        };
        ctx.PlantillasContratoCredito.Add(plantilla);
        await ctx.SaveChangesAsync();
        var contrato = new ContratoVentaCredito
        {
            VentaId = venta.Id, CreditoId = credito.Id, ClienteId = cliente.Id, PlantillaContratoCreditoId = plantilla.Id,
            NumeroContrato = "CT" + sufijo, NumeroPagare = "PG" + sufijo, UsuarioGeneracion = "qa",
            TextoContratoSnapshot = "x", TextoPagareSnapshot = "x", DatosSnapshotJson = "{}"
        };
        ctx.ContratosVentaCredito.Add(contrato);
        await ctx.SaveChangesAsync();
        return new Semilla(venta.Id, credito.Id, producto.Id, contrato.Id);
    }

    private sealed record Final(
        Venta Venta, Credito Credito, List<Cuota> Cuotas, List<MovimientoCaja> Anticipos,
        decimal Stock, bool ContratoVigente, int MovimientosCajaVenta);

    private async Task<Final> LeerAsync(Semilla s)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new Final(
            await ctx.Ventas.AsNoTracking().FirstAsync(x => x.Id == s.VentaId),
            await ctx.Creditos.AsNoTracking().FirstAsync(x => x.Id == s.CreditoId),
            await ctx.Cuotas.AsNoTracking().Where(c => c.CreditoId == s.CreditoId && !c.IsDeleted).ToListAsync(),
            await ctx.MovimientosCaja.AsNoTracking()
                .Where(m => m.Concepto == ConceptoMovimientoCaja.AnticipoCredito && m.ReferenciaId == s.CreditoId && !m.IsDeleted)
                .ToListAsync(),
            await ctx.Productos.AsNoTracking().Where(p => p.Id == s.ProductoId).Select(p => p.StockActual).FirstAsync(),
            await ctx.ContratosVentaCredito.AsNoTracking().AnyAsync(c => c.VentaId == s.VentaId && !c.IsDeleted),
            await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.VentaId == s.VentaId && !m.IsDeleted));
    }

    // ------------------------------------------------------------------ invariantes

    private enum Desenlace { ConfirmarGano, ConfigurarGano }

    /// <summary>Invariantes financieras sobre el estado PERSISTIDO (DbContext nuevo). Devuelve cuál de los dos finales válidos es.</summary>
    private async Task<Desenlace> AssertInvariantesAsync(Semilla s, int cuotasPlan = CuotasA, decimal financiadoPlan = FinanciadoA)
    {
        var f = await LeerAsync(s);
        _out.WriteLine(
            $"final: venta={f.Venta.Estado} credito={f.Credito.Estado} cuotasCred={f.Credito.CantidadCuotas} aprobado={f.Credito.MontoAprobado} " +
            $"cuotas={f.Cuotas.Count} anticipos={f.Anticipos.Count} stock={f.Stock} contratoVigente={f.ContratoVigente}");

        if (f.Venta.Estado == EstadoVenta.Confirmada)
        {
            // 1/2 venta Confirmada <=> crédito Generado con el plan aprobado (A: Confirmar ganó antes de cualquier Configurar).
            Assert.Equal(EstadoCredito.Generado, f.Credito.Estado);
            Assert.Equal(cuotasPlan, f.Credito.CantidadCuotas);
            Assert.Equal(financiadoPlan, f.Credito.MontoAprobado);
            // 3/4 sin cuotas duplicadas; cantidad e importes = plan aprobado.
            Assert.Equal(cuotasPlan, f.Cuotas.Count);
            Assert.Equal(cuotasPlan, f.Cuotas.Select(c => c.NumeroCuota).Distinct().Count());
            Assert.True(Math.Abs(f.Cuotas.Sum(c => c.MontoCapital) - financiadoPlan) <= 0.05m,
                $"El capital de las cuotas ({f.Cuotas.Sum(c => c.MontoCapital)}) no coincide con lo financiado ({financiadoPlan}).");
            // 5 el contrato no se invalidó: la venta confirmada conserva su contrato.
            Assert.True(f.ContratoVigente);
            // 6 un único anticipo en caja, exactamente Total - financiado.
            var anticipo = Assert.Single(f.Anticipos);
            Assert.Equal(Total - financiadoPlan, anticipo.Monto);
            // 7 stock descontado una sola vez.
            Assert.Equal(9m, f.Stock);
            return Desenlace.ConfirmarGano;
        }

        // 8/9 el que perdió no dejó nada: venta sin confirmar, crédito sin generar, sin cuotas, sin anticipo, sin stock tocado.
        Assert.NotEqual(EstadoCredito.Generado, f.Credito.Estado);
        Assert.Empty(f.Cuotas);
        Assert.Empty(f.Anticipos);
        Assert.Equal(10m, f.Stock);
        Assert.Equal(0, f.MovimientosCajaVenta);
        Assert.Null(f.Venta.FechaConfirmacion);
        // Plan B completo (nunca mezcla A/B): 6 cuotas, 5.000 financiados, contrato invalidado, crédito Configurado.
        Assert.Equal(EstadoCredito.Configurado, f.Credito.Estado);
        Assert.Equal(CuotasB, f.Credito.CantidadCuotas);
        Assert.Equal(FinanciadoB, f.Credito.MontoAprobado);
        Assert.False(f.ContratoVigente);
        Assert.Equal(FinanciadoB, f.Credito.SaldoPendiente);
        Assert.Equal(3m, f.Credito.TasaInteres);
        Assert.Equal((decimal?)3m, f.Credito.TasaInteresAplicada);
        return Desenlace.ConfigurarGano;
    }

    private static bool EsRechazoControladoConfigurar(string r) =>
        // Con el lock del crédito tomado antes de leer, el perdedor relee estado comprometido y recibe un rechazo
        // de negocio; un DbUpdateConcurrencyException aquí delataría que el lock dejó de serializar.
        r.StartsWith("ConfiguracionCreditoRechazadaException", StringComparison.Ordinal);
    // ------------------------------------------------------------------ utilidades de escenario

    private static async Task<(string Configurar, string Confirmar)> EsperarAmbasAsync(Task<string> configurar, Task<string> confirmar)
    {
        await Task.WhenAll(configurar, confirmar).WaitAsync(TimeSpan.FromSeconds(90));
        return (configurar.Result, confirmar.Result);
    }

    /// <summary>Sin deadlock, sin excepciones fuera del contrato y exactamente UN ganador coherente con el estado persistido.</summary>
    private void AssertCoherente(string cfg, string cnf, Desenlace d)
    {
        _out.WriteLine($"configurar: {cfg}\nconfirmar:  {cnf}");
        Assert.DoesNotContain("DEADLOCK", cfg + cnf);
        var cfgOk = cfg.StartsWith("OK(", StringComparison.Ordinal);
        var cnfOk = cnf.StartsWith("OK(True", StringComparison.Ordinal);
        Assert.False(cfgOk && cnfOk, "Configurar y Confirmar no pueden ganar a la vez sobre la misma venta.");
        Assert.True(cfgOk || EsRechazoControladoConfigurar(cfg), "Configurar falló fuera del contrato (HTTP 500 en el controller): " + cfg);
        Assert.True(cnfOk || cnf.StartsWith("InvalidOperationException", StringComparison.Ordinal), "Confirmar falló fuera del contrato: " + cnf);
        if (d == Desenlace.ConfirmarGano) { Assert.True(cnfOk); Assert.False(cfgOk); }
        else { Assert.True(cfgOk); Assert.False(cnfOk); }
    }

    /// <summary>El perdedor reintenta: debe ser rechazado limpiamente, sin mutar nada, y el estado final no cambia.</summary>
    private async Task AssertReintentoDelPerdedorAsync(Semilla s, Desenlace d)
    {
        if (d == Desenlace.ConfirmarGano)
        {
            var r = await ConfigurarB(s);
            _out.WriteLine("reintento Configurar: " + r);
            Assert.StartsWith("ConfiguracionCreditoRechazadaException", r);
        }
        else
        {
            var r = await Confirmar(s.VentaId);
            _out.WriteLine("reintento Confirmar: " + r);
            Assert.StartsWith("InvalidOperationException", r);
        }
        Assert.Equal(d, await AssertInvariantesAsync(s));
    }

    private async Task RegenerarContratoAsync(Semilla s)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var previo = await ctx.ContratosVentaCredito.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == s.ContratoId);
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        ctx.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = previo.VentaId, CreditoId = previo.CreditoId, ClienteId = previo.ClienteId,
            PlantillaContratoCreditoId = previo.PlantillaContratoCreditoId,
            NumeroContrato = "CT2" + sufijo, NumeroPagare = "PG2" + sufijo, UsuarioGeneracion = "qa",
            TextoContratoSnapshot = "x", TextoPagareSnapshot = "x", DatosSnapshotJson = "{}"
        });
        await ctx.SaveChangesAsync();
    }

    // ================================================================== escenarios

    [SqlServerConcFact(DisplayName = "PROMPT 96 [CV-05] Configurar empieza antes (pausado antes de persistir): Confirmar corre en el medio")]
    public async Task CV05_ConfigurarPrimero_PausadoAntesDePersistir()
    {
        var s = await SembrarAsync();
        var plan = Compuerta("configurar/antes-de-persistir", sql => EsEscritura(sql, "ContratosVentaCredito") || EsEscritura(sql, "Creditos"));
        var configurar = ConfigurarB(s, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, configurar),
            "Configurar no llegó a la compuerta: " + (configurar.IsCompleted ? configurar.Result : "?"));

        // Configurar ya LEYÓ crédito y venta y está a punto de escribir. Confirmar corre ahora.
        var confirmar = Confirmar(s.VentaId);
        var confirmarAvanzo = !await QuedaBloqueadaAsync(confirmar);
        _out.WriteLine($"Confirmar terminó mientras Configurar estaba pausado antes de persistir: {confirmarAvanzo}");
        Assert.False(confirmarAvanzo, "Confirmar debía esperar el lock del crédito que Configurar ya retiene.");

        plan.Release.TrySetResult();
        var (cfg, cnf) = await EsperarAmbasAsync(configurar, confirmar);
        Volcar("configurar", plan);

        var d = await AssertInvariantesAsync(s);
        Assert.Equal(Desenlace.ConfigurarGano, d); // Configurar tomó el crédito primero: su plan B es el que debe quedar
        AssertCoherente(cfg, cnf, d);
        await AssertReintentoDelPerdedorAsync(s, d);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 96 [CV-06] Confirmar empieza antes (pausado antes de tomar el lock): Configurar completa en el medio")]
    public async Task CV06_ConfirmarPrimero_PausadoAntesDelLockDelCredito()
    {
        var s = await SembrarAsync();
        var plan = Compuerta("confirmar/antes-del-lock-del-credito", sql => sql.Contains("[Creditos] WITH (UPDLOCK", StringComparison.Ordinal));
        var confirmar = Confirmar(s.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "Confirmar no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        // Confirmar empezó (caja y venta leídas) pero aún no tiene ningún lock: Configurar completa entera.
        var configurar = await ConfigurarB(s);
        Assert.StartsWith("OK(", configurar);

        plan.Release.TrySetResult();
        var cnf = await confirmar;
        Volcar("confirmar", plan);

        var d = await AssertInvariantesAsync(s);
        Assert.Equal(Desenlace.ConfigurarGano, d);
        AssertCoherente(configurar, cnf, d);
        await AssertReintentoDelPerdedorAsync(s, d);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 96 [CV-07] Confirmar durante la persistencia de Configurar: sin deadlock y sin configuración parcial")]
    public async Task CV07_ConfirmarDuranteLaPersistenciaDeConfigurar()
    {
        var s = await SembrarAsync();
        // Confirmar detenida con el lock del crédito ya tomado y ANTES de leer venta/contrato.
        var plan = Compuerta("confirmar/con-credito-bloqueado", sql => sql.Contains("UPDATE [v]", StringComparison.Ordinal));
        var confirmar = Confirmar(s.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "Confirmar no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        // Configurar arranca en esta ventana y queda esperando el lock del crédito (antes de leer o escribir nada).
        var configurar = ConfigurarB(s);
        var bloqueada = await QuedaBloqueadaAsync(configurar);
        _out.WriteLine($"Configurar bloqueada mientras Confirmar retiene el crédito: {bloqueada}");
        Assert.True(bloqueada, "Configurar NO debió completar mientras Confirmar retiene el crédito: " + (configurar.IsCompleted ? configurar.Result : "?"));

        // Confirmar sigue: ahora lee venta y contrato con Configurar a mitad de su persistencia.
        plan.Release.TrySetResult();
        var (cfg, cnf) = await EsperarAmbasAsync(configurar, confirmar);
        Volcar("confirmar", plan);

        var d = await AssertInvariantesAsync(s);
        AssertCoherente(cfg, cnf, d);
        await AssertReintentoDelPerdedorAsync(s, d);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 96 [CV-08] Configurar durante la generación de cuotas: los parámetros no cambian bajo Confirmar")]
    public async Task CV08_ConfigurarDuranteLaGeneracionDeCuotas()
    {
        var s = await SembrarAsync();
        // Cuotas ya calculadas en memoria con los parámetros del crédito; todavía sin persistir (mismo SaveChanges).
        var plan = Compuerta("confirmar/cuotas-generadas-sin-persistir",
            sql => EsEscritura(sql, "Creditos") && sql.Contains("[FechaAprobacion]", StringComparison.Ordinal));
        var confirmar = Confirmar(s.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "Confirmar no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        var configurar = ConfigurarB(s);
        var bloqueada = await QuedaBloqueadaAsync(configurar);
        _out.WriteLine($"Configurar bloqueada mientras Confirmar genera las cuotas: {bloqueada}");
        Assert.True(bloqueada, "Configurar NO debió completar mientras Confirmar genera las cuotas: " + (configurar.IsCompleted ? configurar.Result : "?"));

        plan.Release.TrySetResult();
        var (cfg, cnf) = await EsperarAmbasAsync(configurar, confirmar);
        Volcar("confirmar", plan);

        var d = await AssertInvariantesAsync(s);
        Assert.Equal(Desenlace.ConfirmarGano, d); // Confirmar ya había usado el plan A: ese es el que debe quedar
        AssertCoherente(cfg, cnf, d);
        await AssertReintentoDelPerdedorAsync(s, d);
    }

    [SqlServerConcFact(DisplayName = "QA-RONDA [CV-10] Confirmar con la venta ya rastreada en el contexto (Edit + confirmar en la misma request): no falla por RowVersion obsoleto")]
    public async Task CV10_ConfirmarConLaVentaYaRastreadaEnElContexto()
    {
        var s = await SembrarAsync();

        // Edit (POST) guarda la venta y encadena la confirmación en la misma request/contexto: la venta ya está rastreada
        // cuando Confirmar toma su lock de fila (ExecuteUpdate), que cambia el RowVersion en la base.
        var resultado = await EjecutarAsync(null, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            await ctx.Ventas.FirstAsync(v => v.Id == s.VentaId);
            return await sp.GetRequiredService<IVentaService>().ConfirmarVentaCreditoAsync(s.VentaId);
        });

        Assert.StartsWith("OK", resultado);
        var final = await LeerAsync(s);
        Assert.Equal(EstadoVenta.Confirmada, final.Venta.Estado);
        Assert.Equal(CuotasA, final.Cuotas.Count);
        Assert.Equal(9m, final.Stock);
    }

    [SqlServerConcFact(DisplayName = "PROMPT 96 [CV-09] conflictos y reintentos: rechazo controlado, sin parciales y reintento legítimo seguro")]
    public async Task CV09_ConflictosYReintentos()
    {
        // (a) Dos Configurar en conflicto: el perdedor, si lo hay, falla de forma controlada y su reintento es seguro.
        var a = await SembrarAsync();
        var plan = Compuerta("configurar#1/antes-de-persistir", sql => EsEscritura(sql, "ContratosVentaCredito") || EsEscritura(sql, "Creditos"));
        var primera = Configurar(a, CuotasA, Total - 9_000m, plan);      // plan C: 12 cuotas, 9.000 financiados
        Assert.True(await LlegoALaCompuertaAsync(plan, primera), "La primera Configurar no llegó a la compuerta.");
        var segunda = ConfigurarB(a);                                    // plan B: 6 cuotas, 5.000 financiados
        var segundaBloqueada = await QuedaBloqueadaAsync(segunda);
        _out.WriteLine($"segunda Configurar bloqueada con la primera pausada: {segundaBloqueada}");
        Assert.True(segundaBloqueada, "La segunda Configurar debía esperar el lock del crédito de la primera.");
        plan.Release.TrySetResult();
        await Task.WhenAll(primera, segunda).WaitAsync(TimeSpan.FromSeconds(90));
        _out.WriteLine($"primera: {primera.Result}\nsegunda: {segunda.Result}");
        foreach (var r in new[] { primera.Result, segunda.Result })
        {
            Assert.DoesNotContain("DEADLOCK", r);
            Assert.True(r.StartsWith("OK(", StringComparison.Ordinal) || EsRechazoControladoConfigurar(r), "Fuera del contrato: " + r);
        }
        var primeraOk = primera.Result.StartsWith("OK(", StringComparison.Ordinal);
        var segundaOk = segunda.Result.StartsWith("OK(", StringComparison.Ordinal);
        Assert.True(primeraOk && segundaOk, "Serializadas por el crédito, ambas Configurar deben aplicarse en orden.");
        if (!primeraOk)
            Assert.StartsWith("OK(", await Configurar(a, CuotasA, Total - 9_000m)); // reintento legítimo: se aplica completo
        if (!segundaOk)
            Assert.StartsWith("OK(", await ConfigurarB(a));
        var fa = await LeerAsync(a);
        Assert.Equal(EstadoCredito.Configurado, fa.Credito.Estado);
        Assert.Equal(EstadoVenta.Presupuesto, fa.Venta.Estado);
        Assert.Empty(fa.Cuotas);
        Assert.False(fa.ContratoVigente);
        Assert.Equal((CuotasB, FinanciadoB), (fa.Credito.CantidadCuotas, fa.Credito.MontoAprobado)); // la segunda (plan B) es la última

        // (b) Configurar ganó y Confirmar fue rechazada (contrato invalidado): tras generar el contrato nuevo, el reintento
        //     de Confirmar es seguro y usa SOLO el plan B (sin restos del plan A).
        var b = await SembrarAsync();
        Assert.StartsWith("OK(", await ConfigurarB(b));
        Assert.StartsWith("InvalidOperationException", await Confirmar(b.VentaId));
        Assert.Equal(Desenlace.ConfigurarGano, await AssertInvariantesAsync(b));
        await RegenerarContratoAsync(b);
        Assert.StartsWith("OK(True", await Confirmar(b.VentaId));
        Assert.Equal(Desenlace.ConfirmarGano, await AssertInvariantesAsync(b, CuotasB, FinanciadoB));
    }
}
