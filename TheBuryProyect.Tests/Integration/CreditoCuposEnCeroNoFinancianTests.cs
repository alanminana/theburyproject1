using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Decisión del propietario (2026-11-22): condición inicial de una instalación operativa = cupo $0 en TODOS los
/// puntajes. Contrato verificado con los servicios REALES (cupo + aptitud + validación de venta), sin stubs:
/// con todos los cupos en $0 ningún cliente, de cualquier puntaje, puede validar una venta a crédito personal,
/// exista o no un plan de cuotas activo y esté o no activada la «validación de límite» de la configuración.
/// La contraprueba (cupo &gt; 0 ⇒ viable) evita que la prueba pase por un motivo ajeno al cupo.
/// No cubre la excepción explícita de un usuario con <c>ventas.authorize</c> ni el límite manual por cliente:
/// son autorizaciones deliberadas, documentadas en docs/cierre-credito-cupos-cero-20261122.md.
/// </summary>
public class CreditoCuposEnCeroNoFinancianTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static Cliente ClienteConPuntaje(int id, int puntaje) => new()
    {
        Id = id,
        Nombre = "Test",
        Apellido = "Cliente",
        TipoDocumento = "DNI",
        NumeroDocumento = $"1000000{id}",
        NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
        IsDeleted = false,
        RowVersion = new byte[8],
        CuilCuit = "20123456786",
        SituacionCrediticiaBcra = 1,
        SituacionCrediticiaConsultaOk = true,
        SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1),
        PuntajeCliente = puntaje
    };

    private static ConfiguracionCredito Config(bool validarLimite) => new()
    {
        ReferenciasVerificadasMinimas = 0,
        ValidarDocumentacion = false,
        ValidarLimiteCredito = validarLimite,
        ValidarMora = false
    };

    private static ValidacionVentaService BuildService(AppDbContext ctx)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garanteService = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        var aptitud = new ClienteAptitudService(
            ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garanteService);
        return new ValidacionVentaService(
            ctx, aptitud, new FakeSituacionCrediticiaBcraService(), NullLogger<ValidacionVentaService>.Instance);
    }

    private static async Task PonerTodosLosCuposEnCeroAsync(AppDbContext ctx)
    {
        foreach (var preset in await ctx.PuntajesCreditoLimite.ToListAsync())
            preset.LimiteMonto = 0m;
        await ctx.SaveChangesAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TodosLosCuposEnCero_NingunPuntajeObtieneCupoNiValidaVentaACredito(bool validarLimite)
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await PonerTodosLosCuposEnCeroAsync(ctx);
            ctx.Set<ConfiguracionCredito>().Add(Config(validarLimite));
            // Un plan de cuotas activo NO habilita financiación por sí solo.
            ctx.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
            {
                CantidadCuotas = 6, TasaMensual = 0m, Activo = true, Orden = 1
            });
            for (var puntaje = 0; puntaje <= 10; puntaje++)
                ctx.Clientes.Add(ClienteConPuntaje(puntaje + 1, puntaje));
            await ctx.SaveChangesAsync();

            var cupo = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
            var service = BuildService(ctx);

            for (var puntaje = 0; puntaje <= 10; puntaje++)
            {
                var clienteId = puntaje + 1;

                // La semilla solo trae cupo para los puntajes 0-5. Sin cupo configurado (6-10) el cálculo lanza
                // CreditoDisponibleException y los consumidores lo toman como $0 (falla cerrado).
                if (puntaje <= 5)
                {
                    var disponible = await cupo.CalcularDisponibleAsync(clienteId);
                    Assert.Equal(0m, disponible.Limite);
                    Assert.Equal(0m, disponible.Disponible);
                }
                else
                {
                    await Assert.ThrowsAsync<TheBuryProject.Services.Exceptions.CreditoDisponibleException>(
                        () => cupo.CalcularDisponibleAsync(clienteId));
                }

                var result = await service.ValidarVentaCreditoPersonalAsync(clienteId, montoVenta: 1m);

                Assert.True(result.NoViable, $"puntaje {puntaje}: debe ser NoViable con cupo $0");
                Assert.False(result.PuedeProceeder, $"puntaje {puntaje}: no debe poder proceder");
            }
        }
    }

    [Fact]
    public async Task Contraprueba_ConCupoPositivoLaMismaVentaEsViable()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await PonerTodosLosCuposEnCeroAsync(ctx);
            (await ctx.PuntajesCreditoLimite.FirstAsync(p => p.Puntaje == 4)).LimiteMonto = 100_000m;
            ctx.Set<ConfiguracionCredito>().Add(Config(validarLimite: false));
            ctx.Clientes.Add(ClienteConPuntaje(1, puntaje: 4));
            await ctx.SaveChangesAsync();

            var result = await BuildService(ctx).ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            Assert.False(result.NoViable);
            Assert.True(result.PuedeProceeder);
        }
    }

    [Fact]
    public async Task SemillaActual_Puntaje0TraeCupoYPorEsoLaInstalacionDebeVerificarloEnCero()
    {
        // Documenta el hecho que motiva el control previo a operar (scripts/sql/p07-verificar-credito-cerrado.sql):
        // la semilla de las migraciones deja el puntaje 0 con $200.000 y el resto en $0.
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var presets = await ctx.PuntajesCreditoLimite.ToDictionaryAsync(p => p.Puntaje, p => p.LimiteMonto);
            Assert.Equal(200_000m, presets[0]);
            Assert.All(presets.Where(p => p.Key >= 1), p => Assert.Equal(0m, p.Value));
        }
    }
}
