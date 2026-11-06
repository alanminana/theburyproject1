using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H-24 (regla aprobada por el propietario) - concurrencia REAL en SQL Server (efimero) entre la BAJA de un cliente y el
// ALTA de un credito para ese mismo cliente.
//
// Regla: no se puede dar de baja a un cliente con creditos vigentes. Sin serializacion, la baja comprobaba "no tiene
// creditos" y el alta insertaba el credito antes del UPDATE de la baja: quedaba un credito vigente de un cliente dado de baja.
// Con la correccion ambos toman el lock de la fila de Clientes: o gana el credito (la baja se rechaza) o gana la baja
// (el credito se rechaza). Se comprueba el estado FINAL de la base.
//
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL (catalogo "qa_fin_conc*"), con RCSI 0 y 1.
// ---------------------------------------------------------------------------------------------

public sealed class ClienteBajaSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_clientebaja";
}

public class ClienteBajaVsCreditoSqlServerTests : IClassFixture<ClienteBajaSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private readonly ClienteBajaSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public ClienteBajaVsCreditoSqlServerTests(ClienteBajaSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static GatePlan AntesDelUpdateDeLaBaja() =>
        new("antes-del-update-cliente", sql => sql.Contains("UPDATE [Clientes]", StringComparison.Ordinal));

    private static GatePlan AntesDelInsertDelCredito() =>
        new("antes-del-insert-credito", sql => sql.Contains("INSERT INTO [Creditos]", StringComparison.Ordinal));

    private async Task<int> CrearClienteAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Baja",
            Apellido = "Concurrente",
            TipoDocumento = "DNI",
            NumeroDocumento = doc,
            Email = $"{doc}@test.invalid",
            Telefono = "1100000000",
            Domicilio = "Calle 1",
            Localidad = "CABA",
            Provincia = "CABA",
            CodigoPostal = "1000",
            Activo = true
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private async Task<(bool eliminado, int creditos)> EstadoFinalAsync(int clienteId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eliminado = await ctx.Clientes.AsNoTracking().Where(c => c.Id == clienteId).Select(c => c.IsDeleted).SingleAsync();
        var creditos = await ctx.Creditos.AsNoTracking().CountAsync(c => c.ClienteId == clienteId && !c.IsDeleted);
        return (eliminado, creditos);
    }

    private Task<string> Baja(int clienteId, GatePlan? plan = null) =>
        Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IClienteService>();
                return await svc.DeleteAsync(clienteId) ? "OK" : "NO_ENCONTRADO";
            }
            catch (InvalidOperationException ex)
            {
                return "RECHAZADA: " + ex.Message;
            }
            catch (Exception ex)
            {
                return Clasificar(ex);
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }
        });

    private Task<string> AltaCredito(int clienteId, GatePlan? plan = null) =>
        Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICreditoService>();
                var vm = await svc.CreateAsync(new CreditoViewModel { ClienteId = clienteId, MontoSolicitado = 0m });
                return $"OK({vm.Id})";
            }
            catch (InvalidOperationException ex)
            {
                return "RECHAZADA: " + ex.Message;
            }
            catch (Exception ex)
            {
                return Clasificar(ex);
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }
        });

    private static string Clasificar(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                return "DEADLOCK_VICTIMA";
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private async Task<bool> LlegoALaCompuertaAsync(GatePlan plan, Task<string> operacion)
    {
        var llego = await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;
        if (!llego)
        {
            _out.WriteLine($"--- no llego a la compuerta {plan.Nombre} ---");
            foreach (var l in plan.Trace) _out.WriteLine("  " + l);
        }
        return llego;
    }

    /// <summary>true si la operacion quedo BLOQUEADA esperando un lock de fila (se observa en el motor, no con una espera fija).</summary>
    private async Task<bool> QuedaBloqueadaAsync(Task<string> operacion)
    {
        var limite = DateTime.UtcNow + EsperaObservacion * 2;
        while (DateTime.UtcNow < limite)
        {
            if (operacion.IsCompleted) return false;
            await using var cn = new SqlConnection(_fx.ConnectionString);
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID()";
            if ((int)(await cmd.ExecuteScalarAsync())! > 0) return true;
            await Task.Delay(100);
        }
        return false;
    }

    [SqlServerConcFact(DisplayName = "H-24 [BAJA-00] el catalogo corre con el modo de aislamiento pedido (RCSI)")]
    public void ModoDeAislamientoEsElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_FIN_CONC_RCSI") == "1";
        Assert.Equal(pedido, _fx.RcsiActivo);
    }

    [SqlServerConcFact(DisplayName = "H-24 [BAJA-01] baja en vuelo (comprobacion hecha) + alta de credito: gana la baja y el credito se rechaza; nunca credito vigente con cliente dado de baja")]
    public async Task BajaEnVuelo_AltaDeCredito_NoQuedaCreditoVigenteDeClienteDadoDeBaja()
    {
        var cliente = await CrearClienteAsync();
        var plan = AntesDelUpdateDeLaBaja();
        var baja = Baja(cliente, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, baja), "la baja debia llegar a la compuerta (sin creditos comprobado, sin actualizar)");

        var alta = AltaCredito(cliente);
        var bloqueada = await QuedaBloqueadaAsync(alta);

        plan.Release.TrySetResult();
        var rBaja = await baja;
        var rAlta = await alta;
        _out.WriteLine($"baja: {rBaja}; alta: {rAlta} (bloqueada: {bloqueada})");

        var (eliminado, creditos) = await EstadoFinalAsync(cliente);
        Assert.True(bloqueada, "el alta de credito debia esperar el lock de la baja");
        Assert.Equal("OK", rBaja);
        Assert.StartsWith("RECHAZADA", rAlta);
        Assert.True(eliminado);
        Assert.Equal(0, creditos);
    }

    [SqlServerConcFact(DisplayName = "H-24 [BAJA-02] alta de credito en vuelo (sin insertar) + baja: gana el credito y la baja se rechaza; el cliente sigue activo")]
    public async Task AltaEnVuelo_Baja_GanaElCreditoYLaBajaSeRechaza()
    {
        var cliente = await CrearClienteAsync();
        var plan = AntesDelInsertDelCredito();
        var alta = AltaCredito(cliente, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, alta), "el alta debia llegar a la compuerta (cliente validado, sin insertar)");

        var baja = Baja(cliente);
        var bloqueada = await QuedaBloqueadaAsync(baja);

        plan.Release.TrySetResult();
        var rAlta = await alta;
        var rBaja = await baja;
        _out.WriteLine($"alta: {rAlta}; baja: {rBaja} (bloqueada: {bloqueada})");

        var (eliminado, creditos) = await EstadoFinalAsync(cliente);
        Assert.True(bloqueada, "la baja debia esperar el lock del alta de credito");
        Assert.StartsWith("OK(", rAlta);
        Assert.StartsWith("RECHAZADA", rBaja);
        Assert.False(eliminado);
        Assert.Equal(1, creditos);
    }

    [SqlServerConcFact(DisplayName = "H-24 [BAJA-03] secuencial: cliente con credito vigente no se da de baja; sin creditos si; dado de baja no admite creditos ni se reactiva")]
    public async Task Secuencial_ConCreditoVigente_Rechaza_SinCreditos_Acepta()
    {
        var conCredito = await CrearClienteAsync();
        Assert.StartsWith("OK(", await AltaCredito(conCredito));
        Assert.StartsWith("RECHAZADA", await Baja(conCredito));
        Assert.False((await EstadoFinalAsync(conCredito)).eliminado);

        var libre = await CrearClienteAsync();
        Assert.Equal("OK", await Baja(libre));
        var (eliminado, creditos) = await EstadoFinalAsync(libre);
        Assert.True(eliminado);
        Assert.Equal(0, creditos);

        Assert.StartsWith("RECHAZADA", await AltaCredito(libre));
        Assert.True((await EstadoFinalAsync(libre)).eliminado);
        Assert.Equal("NO_ENCONTRADO", await Baja(libre));
    }
}
