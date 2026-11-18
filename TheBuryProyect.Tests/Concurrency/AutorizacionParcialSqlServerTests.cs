using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// D-02: concurrencia REAL sobre SQL Server de la autorizacion por razon de una venta a credito personal.
// Cada razon se autoriza de forma expresa y por separado; dos autorizadores pueden actuar a la vez sobre la misma venta.
// El riesgo es la actualizacion perdida: ambos leen el JSON de razones sin autorizar, cada uno marca la suya y el ultimo
// en guardar pisa al otro. La venta lleva RowVersion: el segundo guardado debe fallar (DbUpdateConcurrencyException) y no
// pisar, y la venta nunca puede quedar Autorizada con una razon sin su autorizacion.
//
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// El fixture crea y elimina su propio catalogo ("<base>_aut") y aplica TODAS las migraciones.
//
// "Controlada" = OK, InvalidOperationException (mensaje al usuario) o DbUpdateConcurrencyException (el controller la
// traduce a "recarga e intenta de nuevo"). Una excepcion de otro tipo seria un 500 en la UI.
// ---------------------------------------------------------------------------------------------

public sealed class AutorizacionParcialSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_aut";
}

public class AutorizacionParcialSqlServerTests : IClassFixture<AutorizacionParcialSqlHostFixture>
{
    private const int Rondas = 12;
    private const string Creador = "vendedor.qa";

    private static readonly TipoRazonAutorizacion[] Refs = { TipoRazonAutorizacion.ReferenciasInsuficientes };
    private static readonly TipoRazonAutorizacion[] Mora = { TipoRazonAutorizacion.MoraActiva };

    private readonly AutorizacionParcialSqlHostFixture _fx;

    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public AutorizacionParcialSqlServerTests(AutorizacionParcialSqlHostFixture fx, Xunit.Abstractions.ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static string Sufijo() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    /// <summary>Venta a credito personal pendiente con dos razones sin autorizar (mora no bloqueante y referencias).</summary>
    private async Task<int> SembrarVentaPendienteAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Sufijo();

        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "Aut", Apellido = "QA" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var razones = new List<RazonAutorizacion>
        {
            new() { Tipo = TipoRazonAutorizacion.MoraActiva, Descripcion = "Mora activa" },
            new() { Tipo = TipoRazonAutorizacion.ReferenciasInsuficientes, Descripcion = "Referencias verificadas insuficientes: 0 de 1 requeridas" }
        };
        var venta = new Venta
        {
            ClienteId = cliente.Id, Numero = "VAUT" + sufijo, Estado = EstadoVenta.PendienteFinanciacion,
            TipoPago = TipoPago.CreditoPersonal, Total = 1_000m, Subtotal = 1_000m, FechaVenta = DateTime.UtcNow,
            RequiereAutorizacion = true, EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion,
            RazonesAutorizacionJson = VentaRazonesAutorizacionJson.Serializar(razones), FechaSolicitudAutorizacion = DateTime.UtcNow
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();

        // El creador y el ultimo editor son un tercero: los autorizadores de la prueba son segundos usuarios.
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET CreatedBy = {0}, UsuarioSolicita = {0} WHERE Id = {1}", Creador, venta.Id);
        return venta.Id;
    }

    private async Task<(Venta Venta, List<RazonAutorizacion> Razones)> LeerAsync(int id)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v = await ctx.Ventas.AsNoTracking().SingleAsync(x => x.Id == id);
        return (v, VentaRazonesAutorizacionJson.Leer(v.RazonesAutorizacionJson));
    }

    private async Task<string[]> EnParaleloAsync(params Func<IVentaService, Task<bool>>[] operaciones)
    {
        var compuerta = new TaskCompletionSource();
        var tareas = operaciones.Select(op => Task.Run(async () =>
        {
            using var scope = _fx.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IVentaService>();
            await compuerta.Task;
            try
            {
                return await op(svc) ? "OK" : "FALSE";
            }
            catch (InvalidOperationException ex)
            {
                return "RECHAZADA: " + ex.Message;
            }
            catch (DbUpdateConcurrencyException)
            {
                return "CONCURRENCIA";
            }
            catch (Exception ex)
            {
                return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message;
            }
        })).ToArray();

        await Task.Delay(200);
        compuerta.SetResult();
        return await Task.WhenAll(tareas);
    }

    private static void AssertControladas(IEnumerable<string> resultados, string contexto)
    {
        var malos = resultados.Where(r => r.StartsWith("EXCEPCION") || r == "FALSE").ToList();
        Assert.True(malos.Count == 0, $"{contexto}: resultado no controlado (seria un 500): {string.Join(" | ", malos)}");
    }

    /// <summary>
    /// Invariante: la venta solo esta Autorizada si TODAS sus razones tienen su autorizacion, y cada razon autorizada
    /// conserva quien, cuando y el motivo (nada pisado a medias).
    /// </summary>
    private static void AssertInvariante(Venta venta, List<RazonAutorizacion> razones, string contexto)
    {
        Assert.Equal(2, razones.Count);
        foreach (var r in razones.Where(r => r.Autorizada))
        {
            Assert.False(string.IsNullOrWhiteSpace(r.AutorizadaPor), $"{contexto}: razón {r.Tipo} autorizada sin usuario");
            Assert.NotNull(r.FechaAutorizacion);
            Assert.False(string.IsNullOrWhiteSpace(r.MotivoAutorizacion), $"{contexto}: razón {r.Tipo} autorizada sin motivo");
        }

        if (venta.EstadoAutorizacion == EstadoAutorizacionVenta.Autorizada)
        {
            Assert.All(razones, r => Assert.True(r.Autorizada, $"{contexto}: Autorizada con la razón {r.Tipo} sin autorizar"));
            Assert.False(string.IsNullOrWhiteSpace(venta.UsuarioAutoriza));
        }
        else
        {
            Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
            Assert.Contains(razones, r => !r.Autorizada);
        }
    }

    [SqlServerConcFact(DisplayName = "D02-CONC-01 dos autorizadores autorizan razones distintas a la vez: ninguna autorizacion se pierde ni queda la venta Autorizada a medias")]
    public async Task Dos_Autorizadores_De_Razones_Distintas_No_Pierden_Ni_Mezclan()
    {
        var resumen = new List<string>();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var id = await SembrarVentaPendienteAsync();

            var res = await EnParaleloAsync(
                svc => svc.AutorizarVentaAsync(id, "gerente.uno", "Referencias", Refs),
                svc => svc.AutorizarVentaAsync(id, "gerente.dos", "Mora no bloqueante", Mora));
            resumen.Add(string.Join(" / ", res));
            AssertControladas(res, $"ronda {ronda}");

            var (venta, razones) = await LeerAsync(id);
            AssertInvariante(venta, razones, $"ronda {ronda}");

            // Lo que figura como OK quedo persistido (no se perdio por una pisada) y lo que no, no aparece autorizado.
            var okRefs = res[0] == "OK";
            var okMora = res[1] == "OK";
            Assert.Equal(okRefs, razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);
            Assert.Equal(okMora, razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);
            Assert.Equal(okRefs && okMora, venta.EstadoAutorizacion == EstadoAutorizacionVenta.Autorizada);

            // El que no pudo reintenta (la UI le pide recargar) y la venta termina autorizada con ambas razones.
            if (!okRefs)
                Assert.True(await EnSerieAsync(svc => svc.AutorizarVentaAsync(id, "gerente.uno", "Referencias", Refs)));
            if (!okMora)
                Assert.True(await EnSerieAsync(svc => svc.AutorizarVentaAsync(id, "gerente.dos", "Mora no bloqueante", Mora)));

            var (final, razonesFinal) = await LeerAsync(id);
            Assert.Equal(EstadoAutorizacionVenta.Autorizada, final.EstadoAutorizacion);
            Assert.All(razonesFinal, r => Assert.True(r.Autorizada));
        }

        _out.WriteLine("resultados por ronda: " + string.Join(" | ", resumen));
        Assert.Contains(resumen, r => r.Contains("OK"));
    }

    [SqlServerConcFact(DisplayName = "D02-CONC-02 dos autorizadores autorizan la misma razon a la vez: una sola autorizacion, el otro recibe un rechazo controlado")]
    public async Task Dos_Autorizadores_De_La_Misma_Razon_Solo_Uno_Gana()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var id = await SembrarVentaPendienteAsync();

            var res = await EnParaleloAsync(
                svc => svc.AutorizarVentaAsync(id, "gerente.uno", "Referencias A", Refs),
                svc => svc.AutorizarVentaAsync(id, "gerente.dos", "Referencias B", Refs));
            AssertControladas(res, $"ronda {ronda}");

            Assert.Equal(1, res.Count(r => r == "OK"));
            var (venta, razones) = await LeerAsync(id);
            AssertInvariante(venta, razones, $"ronda {ronda}");
            var refs = razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes);
            Assert.True(refs.Autorizada);
            // Quien figura es exactamente el que recibio OK (no una mezcla de A y B).
            var ganador = res[0] == "OK" ? "gerente.uno" : "gerente.dos";
            Assert.Equal(ganador, refs.AutorizadaPor);
            Assert.EndsWith(ganador == "gerente.uno" ? "A" : "B", refs.MotivoAutorizacion);
            Assert.False(razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);
            Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        }
    }

    [SqlServerConcFact(DisplayName = "D02-CONC-03 autorizar una razon vs rechazar la venta a la vez: nunca queda Autorizada despues de un rechazo")]
    public async Task Autorizar_Una_Razon_Vs_Rechazar_No_Deja_Autorizada_Una_Venta_Rechazada()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var id = await SembrarVentaPendienteAsync();

            var res = await EnParaleloAsync(
                svc => svc.AutorizarVentaAsync(id, "gerente.uno", "Referencias", Refs),
                svc => svc.RechazarVentaAsync(id, "gerente.dos", "No corresponde"));
            AssertControladas(res, $"ronda {ronda}");

            var (venta, razones) = await LeerAsync(id);
            if (res[1] == "OK")
            {
                // El rechazo se aplico: la venta queda Rechazada (la autorizacion parcial, si llego antes, no la levanta).
                Assert.Equal(EstadoAutorizacionVenta.Rechazada, venta.EstadoAutorizacion);
            }
            else
            {
                // El rechazo no se aplico: la venta sigue pendiente con la razon autorizada solo si su autorizacion fue OK.
                Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
                Assert.Equal(res[0] == "OK", razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);
            }

            Assert.NotEqual(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        }
    }

    private async Task<bool> EnSerieAsync(Func<IVentaService, Task<bool>> operacion)
    {
        using var scope = _fx.Services.CreateScope();
        return await operacion(scope.ServiceProvider.GetRequiredService<IVentaService>());
    }
}
