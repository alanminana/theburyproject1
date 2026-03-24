using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests de ClienteAptitudService.DeterminarEstadoFinal.
/// Verifican que los detalles producidos por CrearDetalle tienen todos los campos
/// correctos y que el comportamiento del método original no cambió.
/// </summary>
public class ClienteAptitudServiceTests
{
    // -----------------------------------------------------------------------
    // Infraestructura compartida
    // -----------------------------------------------------------------------

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
        RowVersion = new byte[8]
    };

    /// <summary>
    /// Config con todas las validaciones deshabilitadas → estado siempre Apto,
    /// sin detalles generados.
    /// </summary>
    private static ConfiguracionCredito ConfigSinValidaciones() => new()
    {
        ValidarDocumentacion = false,
        ValidarLimiteCredito = false,
        ValidarMora = false
    };

    private static ClienteAptitudService BuildService(AppDbContext ctx)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
        return new ClienteAptitudService(ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible);
    }

    // -----------------------------------------------------------------------
    // A. Campos de AptitudDetalleItem — verificados vía resultado observable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeterminarEstadoFinal_DocumentacionFaltante_DetalleConCamposCompletos()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Config: solo validar documentación, sin cupo ni mora
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            config.ValidarVencimientoDocumentos = false;
            ctx.Set<ConfiguracionCredito>().Add(config);

            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            // El cliente no tiene documentos → debe haber un detalle de bloqueo
            Assert.Single(resultado.Detalles);
            var detalle = resultado.Detalles[0];

            Assert.Equal("Documentación", detalle.Categoria);
            Assert.False(string.IsNullOrWhiteSpace(detalle.Descripcion));
            Assert.True(detalle.EsBloqueo);
            Assert.False(string.IsNullOrWhiteSpace(detalle.Icono));
            Assert.Equal("danger", detalle.Color);
        }
    }

    [Fact]
    public async Task DeterminarEstadoFinal_MoraRequiereAutorizacion_DetalleNoBloqueo()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Config: solo mora, umbral 1 día para autorización, sin bloqueo configurado
            var config = ConfigSinValidaciones();
            config.ValidarMora = true;
            config.DiasParaRequerirAutorizacion = 1;
            config.DiasParaNoApto = null;
            ctx.Set<ConfiguracionCredito>().Add(config);

            // Preset de puntaje necesario para CreditoDisponibleService
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(5);
            preset!.LimiteMonto = 100_000m;

            var cliente = BaseCliente(1);
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Crédito con cuota vencida hace 5 días
            var credito = new Credito
            {
                ClienteId = 1,
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 8_000m,
                RowVersion = new byte[8]
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            ctx.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.Date.AddDays(-5),
                MontoCapital = 800m,
                MontoInteres = 200m,
                MontoTotal = 1_000m,
                MontoPagado = 0m,
                MontoPunitorio = 0m,
                Estado = EstadoCuota.Pendiente
            });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Single(resultado.Detalles);
            var detalle = resultado.Detalles[0];

            Assert.Equal("Mora", detalle.Categoria);
            Assert.False(string.IsNullOrWhiteSpace(detalle.Descripcion));
            Assert.False(detalle.EsBloqueo);           // RequiereAutorizacion, no bloqueo
            Assert.Equal("bi-clock-history", detalle.Icono);
            Assert.Equal("warning", detalle.Color);
        }
    }

    // -----------------------------------------------------------------------
    // B. Sin nulls inesperados en detalles
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeterminarEstadoFinal_CualquierDetalle_NingunCampoEsNull()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            ctx.Set<ConfiguracionCredito>().Add(config);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            foreach (var detalle in resultado.Detalles)
            {
                Assert.NotNull(detalle.Categoria);
                Assert.NotNull(detalle.Descripcion);
                Assert.NotNull(detalle.Icono);
                Assert.NotNull(detalle.Color);
            }
        }
    }

    // -----------------------------------------------------------------------
    // C. Equivalencia de comportamiento — estado final correcto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeterminarEstadoFinal_SinProblemas_EstadoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
            Assert.Empty(resultado.Detalles);
            Assert.Null(resultado.Motivo);
        }
    }

    [Fact]
    public async Task DeterminarEstadoFinal_DocumentacionFaltante_EstadoNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            ctx.Set<ConfiguracionCredito>().Add(config);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.NotNull(resultado.Motivo);
            Assert.NotEmpty(resultado.Detalles);
            Assert.All(resultado.Detalles, d => Assert.True(d.EsBloqueo));
        }
    }

    [Fact]
    public async Task DeterminarEstadoFinal_MoraBloqueante_EstadoNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarMora = true;
            config.DiasParaNoApto = 3;   // 3 días → bloqueo
            ctx.Set<ConfiguracionCredito>().Add(config);

            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var credito = new Credito
            {
                ClienteId = 1,
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 8_000m,
                RowVersion = new byte[8]
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            ctx.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.Date.AddDays(-10), // 10 días vencida
                MontoCapital = 800m,
                MontoInteres = 200m,
                MontoTotal = 1_000m,
                MontoPagado = 0m,
                MontoPunitorio = 0m,
                Estado = EstadoCuota.Pendiente
            });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Single(resultado.Detalles);
            Assert.True(resultado.Detalles[0].EsBloqueo);
            Assert.Equal("danger", resultado.Detalles[0].Color);
        }
    }

    // -----------------------------------------------------------------------
    // D. ResolverEstadoFinal — unit tests puros, sin infraestructura
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolverEstadoFinal_Apto_CuandoAmbosFlags_False()
    {
        var motivos = new List<string>();

        var (estado, motivo) = ClienteAptitudService.ResolverEstadoFinal(
            esNoApto: false,
            requiereAutorizacion: false,
            motivos: motivos);

        Assert.Equal(EstadoCrediticioCliente.Apto, estado);
        Assert.Null(motivo);
    }

    [Fact]
    public void ResolverEstadoFinal_RequiereAutorizacion_CuandoSoloRequiereAutorizacion()
    {
        var motivos = new List<string> { "Tiene mora: 5 días" };

        var (estado, motivo) = ClienteAptitudService.ResolverEstadoFinal(
            esNoApto: false,
            requiereAutorizacion: true,
            motivos: motivos);

        Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, estado);
        Assert.Equal("Tiene mora: 5 días", motivo);
    }

    [Fact]
    public void ResolverEstadoFinal_NoApto_CuandoSoloEsNoApto()
    {
        var motivos = new List<string> { "Documentación incompleta: DNI" };

        var (estado, motivo) = ClienteAptitudService.ResolverEstadoFinal(
            esNoApto: true,
            requiereAutorizacion: false,
            motivos: motivos);

        Assert.Equal(EstadoCrediticioCliente.NoApto, estado);
        Assert.Equal("Documentación incompleta: DNI", motivo);
    }

    [Fact]
    public void ResolverEstadoFinal_NoApto_CuandoAmbosFlags_True_BloqueoTienePrioridad()
    {
        var motivos = new List<string> { "Mora crítica: 30 días, $ 5.000", "Tiene mora: 30 días" };

        var (estado, motivo) = ClienteAptitudService.ResolverEstadoFinal(
            esNoApto: true,
            requiereAutorizacion: true,
            motivos: motivos);

        Assert.Equal(EstadoCrediticioCliente.NoApto, estado);
        Assert.Equal("Mora crítica: 30 días, $ 5.000. Tiene mora: 30 días", motivo);
    }

    [Fact]
    public void ResolverEstadoFinal_Motivo_EsConcatenacionDeTodosLosMotivos()
    {
        var motivos = new List<string> { "Motivo A", "Motivo B", "Motivo C" };

        var (_, motivo) = ClienteAptitudService.ResolverEstadoFinal(
            esNoApto: true,
            requiereAutorizacion: false,
            motivos: motivos);

        Assert.Equal("Motivo A. Motivo B. Motivo C", motivo);
    }
}
