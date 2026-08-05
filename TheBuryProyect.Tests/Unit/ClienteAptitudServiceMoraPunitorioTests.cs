using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-C: separa la mora de CAPITAL (predicado canónico <see cref="EstadoCuotaResolver.EstaEnMoraCapitalDerivado"/>)
/// del punitorio APLICADO pendiente de cobro (<see cref="IPunitorioService.ObtenerPunitorioAplicadoPendientePorCuotasAsync"/>)
/// dentro de <see cref="ClienteAptitudService.EvaluarMoraAsync"/>/<c>DeterminarEstadoFinal</c>.
///
/// Política congelada cubierta acá: el punitorio aplicado pendiente nunca produce NoApto por sí solo
/// (siempre RequiereAutorizacion, con motivo separado "Punitorio"); la mora de capital mantiene los
/// umbrales de siempre; capital vencido + punitorio pendiente decide el estado una sola vez por
/// capital, sin duplicar bloqueo/autorización; capital saldado nunca cuenta como mora de capital
/// aunque tenga punitorio pendiente; punitorio pagado/anulado/calculado-no-aplicado no afecta.
/// </summary>
public class ClienteAptitudServiceMoraPunitorioTests
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

    // BCRA obligatorio pero irrelevante para estos escenarios: cliente con consulta válida y normal
    // (situación 1) para que nunca sea la causa de NoApto/RequiereAutorizacion en estos tests.
    private static Cliente BaseCliente(int id) => new()
    {
        Id = id,
        Nombre = "Test",
        Apellido = "Cliente",
        TipoDocumento = "DNI",
        NumeroDocumento = $"2000000{id}",
        NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
        IsDeleted = false,
        RowVersion = new byte[8],
        CuilCuit = "20123456786",
        SituacionCrediticiaBcra = 1,
        SituacionCrediticiaConsultaOk = true,
        SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1)
    };

    // Sólo mora activa; documentación/cupo apagados para aislar el efecto de capital/punitorio.
    private static ConfiguracionCredito ConfigSoloMora(
        int? diasParaRequerirAutorizacion = 1,
        int? diasParaNoApto = null,
        decimal? montoMoraParaNoApto = null,
        int? cuotasVencidasParaNoApto = null) => new()
    {
        ValidarDocumentacion = false,
        ValidarLimiteCredito = false,
        ValidarMora = true,
        DiasParaRequerirAutorizacion = diasParaRequerirAutorizacion,
        DiasParaNoApto = diasParaNoApto,
        MontoMoraParaNoApto = montoMoraParaNoApto,
        CuotasVencidasParaNoApto = cuotasVencidasParaNoApto
    };

    private static ClienteAptitudService BuildService(AppDbContext ctx, RelojComercialFake reloj, IPunitorioService? punitorioService = null)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garanteService = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        return new ClienteAptitudService(ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garanteService, reloj, punitorioService);
    }

    private static int _nextNumeroCredito = 1;

    private static async Task<Cuota> SeedCuotaAsync(
        AppDbContext ctx, int clienteId, decimal montoTotal, decimal montoPagado,
        DateTime fechaVencimiento, EstadoCuota estado)
    {
        var credito = new Credito
        {
            ClienteId = clienteId,
            Numero = $"PUNML10C-{_nextNumeroCredito++}", // Credito.Numero es único; un test con varias cuotas necesita varios créditos.
            Estado = EstadoCredito.Activo,
            IsDeleted = false,
            SaldoPendiente = montoTotal - montoPagado
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var cuota = new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            // Legacy, congelado desde PUN-ML5/6 — nunca debe leerse (regla 9 de PUN-ML10-C). Se
            // fija distinto de 0 para que un eventual regreso a leerlo se note en los montos.
            MontoPunitorio = 999m,
            Estado = estado
        };
        ctx.Cuotas.Add(cuota);
        await ctx.SaveChangesAsync();
        return cuota;
    }

    private static async Task<PunitorioAplicado> SeedPunitorioAplicadoAsync(
        AppDbContext ctx, int cuotaId, decimal importe, DateOnly fechaCalculo,
        EstadoPunitorioAplicado estado = EstadoPunitorioAplicado.Aplicado)
    {
        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuotaId,
            FechaCalculo = fechaCalculo,
            SaldoBase = importe,
            DiasComputados = 10,
            Importe = importe,
            Estado = estado,
            DesgloseSnapshotJson = "{}",
            MotivoAplicacion = "PUN-ML10-C test",
            UsuarioAplicacion = "tester",
            FechaAplicacion = fechaCalculo.ToDateTime(TimeOnly.MinValue)
        };
        ctx.PunitoriosAplicados.Add(aplicado);
        await ctx.SaveChangesAsync();
        return aplicado;
    }

    private static async Task SeedPagoPunitorioAsync(
        AppDbContext ctx, int cuotaId, int punitorioAplicadoId, decimal importeAplicadoPunitorio, DateOnly fecha)
    {
        ctx.PagosCuota.Add(new PagoCuota
        {
            CuotaId = cuotaId,
            FechaPagoComercial = fecha,
            ImporteTotal = importeAplicadoPunitorio,
            ImporteAplicadoCuota = 0m,
            ImporteAplicadoPunitorio = importeAplicadoPunitorio,
            PunitorioAplicadoId = punitorioAplicadoId,
            Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Aplicado,
            HistorialCompleto = true
        });
        await ctx.SaveChangesAsync();
    }

    // -----------------------------------------------------------------------
    // 1. Capital pendiente, sin punitorio
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalPendiente_SinPunitorio_EsMoraDeCapitalYNoHayPunitorio()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            await SeedCuotaAsync(ctx, 1, 1_000m, 0m, new DateTime(2026, 7, 20), EstadoCuota.Pendiente);

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.True(mora.TieneMora);
            Assert.Equal(1, mora.CuotasConMoraCapital);
            Assert.Equal(1_000m, mora.MontoMoraCapital);
            Assert.False(mora.TienePunitorioAplicadoPendiente);
            Assert.Equal(0m, mora.MontoPunitorioAplicadoPendiente);
            Assert.Equal(0, mora.CuotasConPunitorioAplicadoPendiente);
        }
    }

    // -----------------------------------------------------------------------
    // 2. Capital + punitorio pendiente → NoApto por capital, punitorio motivo separado
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalVencidoBloqueante_MasPunitorioPendiente_NoAptoPorCapital_PunitorioInformativo()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora(diasParaNoApto: 3));
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 0m, new DateTime(2026, 7, 20), EstadoCuota.Vencida); // 16 días
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 150m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Equal(2, resultado.Detalles.Count);
            Assert.Contains(resultado.Detalles, d => d.Categoria == "Mora" && d.EsBloqueo);
            var detallePunitorio = Assert.Single(resultado.Detalles, d => d.Categoria == "Punitorio");
            Assert.False(detallePunitorio.EsBloqueo);
            Assert.Contains("150", detallePunitorio.Descripcion);
            Assert.False(string.IsNullOrWhiteSpace(resultado.Motivo));
            Assert.Contains("Punitorio aplicado pendiente", resultado.Motivo);
        }
    }

    // -----------------------------------------------------------------------
    // 3. Capital saldado + punitorio aplicado pendiente → no bloquea como mora, RequiereAutorizacion
    //    por punitorio (también cubre escenario 10: capital saldado no cuenta en CuotasVencidas, y
    //    escenario 11: punitorio solo nunca produce Apto).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalSaldado_ConPunitorioPendiente_NoEsMoraDeCapital_RequiereAutorizacionPorPunitorio()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Capital 100% saldado (MontoPagado == MontoTotal); el estado real que dejaría
            // EstadoCuotaResolver.Resolver con punitorio pendiente es Parcial, no Pagada.
            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Parcial);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 80m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.False(mora.TieneMora);
            Assert.Equal(0, mora.CuotasVencidas);
            Assert.Equal(0, mora.CuotasConMoraCapital);
            Assert.Equal(0m, mora.MontoTotalMora);
            Assert.True(mora.TienePunitorioAplicadoPendiente);
            Assert.Equal(80m, mora.MontoPunitorioAplicadoPendiente);
            Assert.Equal(1, mora.CuotasConPunitorioAplicadoPendiente);

            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);
            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, resultado.Estado);
            var detalle = Assert.Single(resultado.Detalles);
            Assert.Equal("Punitorio", detalle.Categoria);
            Assert.False(detalle.EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // 4. Capital saldado + punitorio calculado no aplicado → no afecta (ninguna fila PunitorioAplicado)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalSaldado_SinAplicacionDePunitorio_NoAfectaAptitud()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Capital saldado, vencida hace rato, SIN ninguna fila PunitorioAplicado: equivale a
            // "punitorio calculado pero nunca aplicado" — ClienteAptitudService jamás lo calcula al
            // vuelo, así que no hay nada que leer.
            await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 6, 1), EstadoCuota.Pagada);

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
            Assert.Empty(resultado.Detalles);
            Assert.Null(resultado.Motivo);
        }
    }

    // -----------------------------------------------------------------------
    // 5. Punitorio parcialmente pagado → queda pendiente el resto, RequiereAutorizacion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PunitorioParcialmentePagado_QuedaSaldoPendiente_RequiereAutorizacion()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Parcial);
            var aplicado = await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 100m, new DateOnly(2026, 8, 1));
            await SeedPagoPunitorioAsync(ctx, cuota.Id, aplicado.Id, 40m, new DateOnly(2026, 8, 2));

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.True(mora.TienePunitorioAplicadoPendiente);
            Assert.Equal(60m, mora.MontoPunitorioAplicadoPendiente);

            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);
            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, resultado.Estado);
        }
    }

    // -----------------------------------------------------------------------
    // 6. Punitorio totalmente pagado → no afecta
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PunitorioTotalmentePagado_NoAfectaAptitud()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Pagada);
            var aplicado = await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 100m, new DateOnly(2026, 8, 1));
            await SeedPagoPunitorioAsync(ctx, cuota.Id, aplicado.Id, 100m, new DateOnly(2026, 8, 2));

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.False(mora.TienePunitorioAplicadoPendiente);
            Assert.Equal(0m, mora.MontoPunitorioAplicadoPendiente);

            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);
            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
        }
    }

    // -----------------------------------------------------------------------
    // 7. Punitorio anulado → no afecta
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PunitorioAnulado_NoAfectaAptitud()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Pagada);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 100m, new DateOnly(2026, 8, 1), EstadoPunitorioAplicado.Anulado);

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
            Assert.Empty(resultado.Detalles);
        }
    }

    // -----------------------------------------------------------------------
    // 8. Varias cuotas mixtas: una con mora de capital, otra con capital saldado + punitorio
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VariasCuotasMixtas_SumaCapitalYPunitorioPorSeparado()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            await SeedCuotaAsync(ctx, 1, 500m, 0m, new DateTime(2026, 7, 20), EstadoCuota.Vencida);
            var cuota2 = await SeedCuotaAsync(ctx, 1, 700m, 700m, new DateTime(2026, 6, 1), EstadoCuota.Parcial);
            await SeedPunitorioAplicadoAsync(ctx, cuota2.Id, 45m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.Equal(1, mora.CuotasConMoraCapital);
            Assert.Equal(500m, mora.MontoMoraCapital);
            Assert.Equal(1, mora.CuotasConPunitorioAplicadoPendiente);
            Assert.Equal(45m, mora.MontoPunitorioAplicadoPendiente);
        }
    }

    // -----------------------------------------------------------------------
    // 9. Múltiples aplicaciones históricas: sólo la activa (Modelo A) cuenta
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultiplesAplicacionesHistoricas_SoloCuentaLaActiva()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 6, 1), EstadoCuota.Parcial);
            // Historial: una anulada (no cuenta) y la activa vigente (Modelo A: a lo sumo una Aplicada).
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 200m, new DateOnly(2026, 7, 1), EstadoPunitorioAplicado.Anulado);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 90m, new DateOnly(2026, 8, 1), EstadoPunitorioAplicado.Aplicado);

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.Equal(1, mora.CuotasConPunitorioAplicadoPendiente);
            Assert.Equal(90m, mora.MontoPunitorioAplicadoPendiente);
        }
    }

    // -----------------------------------------------------------------------
    // 12. Punitorio solo nunca produce NoApto, aunque el monto sea grande
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PunitorioSolo_MontoGrande_NuncaProduceNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            // Umbrales de mora de capital agresivos — irrelevantes porque no hay mora de capital.
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora(diasParaNoApto: 1, montoMoraParaNoApto: 1m, cuotasVencidasParaNoApto: 1));
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Parcial);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 999_999m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, resultado.Estado);
        }
    }

    // -----------------------------------------------------------------------
    // 13. Capital NoApto + punitorio → no duplica ni escala (sigue NoApto, 2 detalles)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalNoApto_MasPunitorio_NoEscalaNiDuplica()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora(diasParaNoApto: 1));
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 0m, new DateTime(2026, 7, 1), EstadoCuota.Vencida);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 50m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Equal(2, resultado.Detalles.Count);
            Assert.Single(resultado.Detalles, d => d.Categoria == "Mora" && d.EsBloqueo);
            Assert.Single(resultado.Detalles, d => d.Categoria == "Punitorio" && !d.EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // 14. Capital RequiereAutorizacion + punitorio → no duplica (sigue RequiereAutorizacion, 2 detalles)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CapitalRequiereAutorizacion_MasPunitorio_NoDuplica()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora(diasParaRequerirAutorizacion: 1, diasParaNoApto: null));
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 0m, new DateTime(2026, 7, 20), EstadoCuota.Vencida);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 50m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, resultado.Estado);
            Assert.Equal(2, resultado.Detalles.Count);
            Assert.Single(resultado.Detalles, d => d.Categoria == "Mora" && !d.EsBloqueo);
            Assert.Single(resultado.Detalles, d => d.Categoria == "Punitorio" && !d.EsBloqueo);
            Assert.Contains("Tiene mora", resultado.Motivo);
            Assert.Contains("Punitorio aplicado pendiente", resultado.Motivo);
        }
    }

    // -----------------------------------------------------------------------
    // 15. Otras causas de aptitud (documentación) mantienen precedencia sobre el punitorio
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DocumentacionNoApto_MasPunitorioPendiente_MantienePrecedenciaNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            var config = ConfigSoloMora();
            config.ValidarDocumentacion = true; // sin documentos cargados → incompleta → NoApto
            ctx.Set<ConfiguracionCredito>().Add(config);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Capital saldado (sin mora de capital): la única causa de NoApto es documentación.
            var cuota = await SeedCuotaAsync(ctx, 1, 1_000m, 1_000m, new DateTime(2026, 7, 1), EstadoCuota.Parcial);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 60m, new DateOnly(2026, 8, 1));

            var service = BuildService(ctx, reloj);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Contains(resultado.Detalles, d => d.Categoria == "Documentación" && d.EsBloqueo);
            var detallePunitorio = Assert.Single(resultado.Detalles, d => d.Categoria == "Punitorio");
            Assert.False(detallePunitorio.EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // 16. La consulta batch de punitorio se invoca exactamente una vez por evaluación
    // -----------------------------------------------------------------------

    private sealed class CountingPunitorioService(IPunitorioService inner) : IPunitorioService
    {
        public int LlamadasBatch { get; private set; }

        public Task<IReadOnlyDictionary<int, decimal>> ObtenerPunitorioAplicadoPendientePorCuotasAsync(
            IEnumerable<int> cuotaIds, CancellationToken cancellationToken = default)
        {
            LlamadasBatch++;
            return inner.ObtenerPunitorioAplicadoPendientePorCuotasAsync(cuotaIds, cancellationToken);
        }

        public Task<PunitorioConsultaResultado> CalcularCuotaAsync(int cuotaId, DateOnly? fechaCalculo = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<PunitorioCuotaDetalleResultado> ObtenerDetalleCuotaAsync(int cuotaId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<decimal> ObtenerPunitorioAplicadoPendienteAsync(int cuotaId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<PunitorioAplicadoProgreso?> ObtenerAplicacionActivaConProgresoAsync(int cuotaId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<PunitorioAplicado> AplicarAsync(int cuotaId, PunitorioAplicarComando comando, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<PunitorioAplicado> AnularAsync(int punitorioAplicadoId, PunitorioAnularComando comando, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task EvaluarAptitud_InvocaConsultaBatchDePunitorioUnaSolaVez()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            await SeedCuotaAsync(ctx, 1, 500m, 0m, new DateTime(2026, 7, 1), EstadoCuota.Vencida);
            var cuota2 = await SeedCuotaAsync(ctx, 1, 300m, 300m, new DateTime(2026, 6, 1), EstadoCuota.Parcial);
            await SeedPunitorioAplicadoAsync(ctx, cuota2.Id, 30m, new DateOnly(2026, 8, 1));

            var real = new PunitorioService(ctx, new PunitorioCalculator(), reloj, new StubCurrentUserServiceSpy(), NullLogger<PunitorioService>.Instance);
            var spy = new CountingPunitorioService(real);
            var service = BuildService(ctx, reloj, spy);

            await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(1, spy.LlamadasBatch);
        }
    }

    private sealed class StubCurrentUserServiceSpy : ICurrentUserService
    {
        public string GetUsername() => "tester";
        public string GetUserId() => "system";
        public bool IsAuthenticated() => false;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => false;
        public string? GetIpAddress() => null;
    }

    // -----------------------------------------------------------------------
    // 17. Auditoría estructural: cero lectura de Cuota.MontoPunitorio en ClienteAptitudService
    // -----------------------------------------------------------------------

    [Fact]
    public void ClienteAptitudService_NuncaLeeCuotaMontoPunitorio()
    {
        var path = FindClienteAptitudServiceSourcePath();
        var source = File.ReadAllText(path);
        const string commentsAndStringsPattern = "//.*?$|/\\*.*?\\*/|@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"";
        var sinComentarios = System.Text.RegularExpressions.Regex.Replace(
            source,
            commentsAndStringsPattern,
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Multiline);

        // \b para no confundir con AptitudMoraDetalle.MontoPunitorioAplicadoPendiente (campo nuevo,
        // legítimo) — sólo debe fallar ante el token exacto ".MontoPunitorio" (Cuota.MontoPunitorio).
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\.MontoPunitorio\b"), sinComentarios);
    }

    private static string FindClienteAptitudServiceSourcePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "Services", "ClienteAptitudService.cs");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("No se encontró Services/ClienteAptitudService.cs a partir de AppContext.BaseDirectory.");
    }

    // -----------------------------------------------------------------------
    // 18. Compatibilidad de propiedades legacy cuando sólo hay mora de capital (sin punitorio)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SoloMoraDeCapital_PropiedadesLegacyCoincidenConLasNuevas()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var reloj = new RelojComercialFake(new DateOnly(2026, 8, 5));
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            await SeedCuotaAsync(ctx, 1, 1_000m, 0m, new DateTime(2026, 7, 20), EstadoCuota.Vencida);

            var service = BuildService(ctx, reloj);
            var mora = await service.EvaluarMoraAsync(1);

            Assert.Equal(mora.MontoMoraCapital, mora.MontoTotalMora);
            Assert.Equal(mora.CuotasConMoraCapital, mora.CuotasVencidas);
            Assert.Equal(mora.CuotasConMoraCapital > 0, mora.TieneMora);
            Assert.False(mora.TienePunitorioAplicadoPendiente);
        }
    }
}
