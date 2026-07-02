using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FASE 4D-B — A diferencia de ValidacionVentaServiceTests (que stubea
/// IClienteAptitudService), estos tests conectan ValidacionVentaService con el
/// ClienteAptitudService REAL para confirmar que la excepción de "cliente
/// antiguo buen pagador" con BCRA alto (FASE 4D) se propaga correctamente
/// hasta el flujo consumidor: ni Apto automático ni bloqueo definitivo, sino
/// RequiereAutorizacion. Reutiliza FakeSituacionCrediticiaBcraService, definido
/// en ValidacionVentaServiceTests.cs dentro del mismo namespace.
/// </summary>
public class ValidacionVentaServiceAptitudRealIntegrationTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static Cliente BaseCliente(int id) => new()
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
        SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1)
    };

    // Espejo de ClienteAptitudServiceTests.ClienteBuenPagador (FASE 4D).
    private static Cliente ClienteBuenPagador(int id, int bcraSituacion = 3, int puntajeCliente = 4)
    {
        var cliente = BaseCliente(id);
        cliente.SituacionCrediticiaBcra = bcraSituacion;
        cliente.SituacionCrediticiaConsultaOk = true;
        cliente.PuntajeCliente = puntajeCliente;
        cliente.AntiguedadDias = 90;
        cliente.CantidadComprasCliente = 3;
        cliente.CreditosEnTermino = 1;
        cliente.CreditosConAtraso = 0;
        return cliente;
    }

    private static ConfiguracionCredito ConfigSinValidaciones() => new()
    {
        ValidarDocumentacion = false,
        ValidarLimiteCredito = false,
        ValidarMora = false
    };

    private static ValidacionVentaService BuildService(AppDbContext ctx, FakeSituacionCrediticiaBcraService fakeBcra)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garanteService = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        var aptitudService = new ClienteAptitudService(
            ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garanteService);
        return new ValidacionVentaService(
            ctx, aptitudService, fakeBcra, NullLogger<ValidacionVentaService>.Instance);
    }

    [Fact]
    public async Task ValidacionCredito_BuenPagadorConBcraAlto_RequiereAutorizacion()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());

            // Cupo suficiente (Puntaje 4) para que la verificación de cupo no
            // degrade el resultado de RequiereAutorizacion a NoViable.
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(4);
            preset!.LimiteMonto = 100_000m;

            ctx.Clientes.Add(ClienteBuenPagador(1));
            await ctx.SaveChangesAsync();

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, result.EstadoAptitud);
            Assert.True(result.RequiereAutorizacion);
            Assert.False(result.NoViable);
            Assert.Contains(result.RazonesAutorizacion, r => r.Descripcion.Contains("buen pagador"));
        }
    }

    [Fact]
    public async Task ValidacionCredito_BuenPagadorConBcraAlto_NoQuedaViableNormal()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());

            var preset = await ctx.PuntajesCreditoLimite.FindAsync(4);
            preset!.LimiteMonto = 100_000m;

            ctx.Clientes.Add(ClienteBuenPagador(1));
            await ctx.SaveChangesAsync();

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            // No debe interpretarse como aprobación normal: PuedeProceeder exige
            // ausencia de autorización pendiente, no solo ausencia de bloqueo duro.
            Assert.False(result.PuedeProceeder);
            Assert.False(result.NoViable);
            Assert.True(result.RequiereAutorizacion);
        }
    }

    [Fact]
    public async Task ValidacionCredito_BcraAltoSinBuenPagador_SigueBloqueado()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());

            // Puntaje insuficiente (< 4): no cumple la excepción de buen pagador,
            // BCRA alto sigue siendo bloqueante puro.
            ctx.Clientes.Add(ClienteBuenPagador(1, puntajeCliente: 1));
            await ctx.SaveChangesAsync();

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            Assert.Equal(EstadoCrediticioCliente.NoApto, result.EstadoAptitud);
            Assert.True(result.NoViable);
            Assert.False(result.RequiereAutorizacion);
            Assert.NotEmpty(result.RequisitosPendientes);
        }
    }
}
