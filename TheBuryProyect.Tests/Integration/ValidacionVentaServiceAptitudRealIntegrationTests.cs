using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

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

    // -----------------------------------------------------------------------
    // PUN-ML10-C: mora de capital vs. punitorio aplicado pendiente, de punta a punta a través de
    // ValidacionVentaService (ClienteAptitudService real, no stubeado).
    // -----------------------------------------------------------------------

    private static async Task<Cuota> SeedCuotaCapitalSaldadaAsync(AppDbContext ctx, int clienteId, decimal montoTotal)
    {
        var credito = new Credito
        {
            ClienteId = clienteId,
            Numero = $"PUNML10C-VVS-{clienteId}",
            Estado = EstadoCredito.Activo,
            IsDeleted = false,
            SaldoPendiente = 0m
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var cuota = new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            FechaVencimiento = DateTime.UtcNow.Date.AddDays(-30),
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoTotal = montoTotal,
            MontoPagado = montoTotal, // capital 100% saldado
            MontoPunitorio = 0m,
            // Estado real que deja EstadoCuotaResolver.Resolver cuando el capital está saldado pero
            // queda un punitorio aplicado pendiente: Parcial, nunca Pagada (PUN-ML7).
            Estado = EstadoCuota.Parcial
        };
        ctx.Cuotas.Add(cuota);
        await ctx.SaveChangesAsync();
        return cuota;
    }

    private static async Task SeedPunitorioAplicadoAsync(AppDbContext ctx, int cuotaId, decimal importe)
    {
        ctx.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuotaId,
            FechaCalculo = DateOnly.FromDateTime(DateTime.UtcNow),
            SaldoBase = importe,
            DiasComputados = 10,
            Importe = importe,
            Estado = EstadoPunitorioAplicado.Aplicado,
            DesgloseSnapshotJson = "{}",
            MotivoAplicacion = "PUN-ML10-C test",
            UsuarioAplicacion = "tester",
            FechaAplicacion = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static ConfiguracionCredito ConfigSoloMora(int? diasParaRequerirAutorizacion = 1) => new()
    {
        ValidarDocumentacion = false,
        ValidarLimiteCredito = false,
        ValidarMora = true,
        DiasParaRequerirAutorizacion = diasParaRequerirAutorizacion
    };

    [Fact]
    public async Task ValidacionCredito_CapitalSaldadoConPunitorioPendiente_NoBloqueaComoMora_RequiereAutorizacionPorPunitorio()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());

            // PuntajeCreditoLimite sólo admite Puntaje 1..5 (check constraint); BaseCliente no fija
            // PuntajeCliente (default 0), así que se ajusta a 1 para poder resolver un preset válido.
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(1);
            preset!.LimiteMonto = 100_000m;

            var cliente = BaseCliente(1);
            cliente.PuntajeCliente = 1;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var cuota = await SeedCuotaCapitalSaldadaAsync(ctx, 1, 1_000m);
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 75m);

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            // No bloquea como mora de capital: el capital está saldado.
            Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, result.EstadoAptitud);
            Assert.False(result.NoViable);
            Assert.True(result.RequiereAutorizacion);
            // PUN-ML10-D: motivo específico, no el genérico ClienteRequiereAutorizacion.
            Assert.Contains(result.RazonesAutorizacion, r => r.Tipo == TipoRazonAutorizacion.Punitorio && r.Descripcion.Contains("Punitorio"));
        }
    }

    // -----------------------------------------------------------------------
    // PUN-ML10-D: capital NoApto (mora bloqueante) + punitorio aplicado pendiente, de punta a
    // punta. El punitorio nunca es la causa del bloqueo ni duplica autorización — sólo se
    // informa como razón adicional junto al NoApto ya decidido por la mora de capital.
    // -----------------------------------------------------------------------

    private static ConfiguracionCredito ConfigMoraNoApto(int diasParaNoApto = 1) => new()
    {
        ValidarDocumentacion = false,
        ValidarLimiteCredito = false,
        ValidarMora = true,
        DiasParaNoApto = diasParaNoApto
    };

    [Fact]
    public async Task ValidacionCredito_CapitalMoraBloqueanteConPunitorioPendiente_MantieneNoApto_PunitorioComoRazonAdicional()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigMoraNoApto());

            var preset = await ctx.PuntajesCreditoLimite.FindAsync(1);
            preset!.LimiteMonto = 100_000m;

            var cliente = BaseCliente(1);
            cliente.PuntajeCliente = 1;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Capital NO saldado y vencido hace tiempo: mora de capital bloqueante (NoApto).
            var credito = new Credito
            {
                ClienteId = 1,
                Numero = "PUNML10D-VVS-NOAPTO",
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 500m
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            var cuota = new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.Date.AddDays(-90),
                MontoCapital = 500m,
                MontoInteres = 0m,
                MontoTotal = 500m,
                MontoPagado = 0m,
                MontoPunitorio = 0m,
                Estado = EstadoCuota.Vencida
            };
            ctx.Cuotas.Add(cuota);
            await ctx.SaveChangesAsync();

            // Punitorio aplicado pendiente sobre la misma cuota en mora.
            await SeedPunitorioAplicadoAsync(ctx, cuota.Id, 75m);

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            // El estado final lo decide la mora de capital: NoViable, sin autorización duplicada.
            Assert.Equal(EstadoCrediticioCliente.NoApto, result.EstadoAptitud);
            Assert.True(result.NoViable);
            Assert.False(result.RequiereAutorizacion);
            Assert.Contains(result.RequisitosPendientes, r => r.Tipo == TipoRequisitoPendiente.ClienteNoApto);

            // El punitorio se informa como razón adicional (no bloqueante), motivo específico.
            var razonPunitorio = Assert.Single(result.RazonesAutorizacion);
            Assert.Equal(TipoRazonAutorizacion.Punitorio, razonPunitorio.Tipo);
            Assert.Contains("Punitorio", razonPunitorio.Descripcion);
        }
    }

    [Fact]
    public async Task ValidacionCredito_CapitalSaldadoSinPunitorioAplicado_NoBloqueaNiRequiereAutorizacionPorPunitorio()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSoloMora());

            var preset = await ctx.PuntajesCreditoLimite.FindAsync(1);
            preset!.LimiteMonto = 100_000m;

            var cliente = BaseCliente(1);
            cliente.PuntajeCliente = 1;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Capital saldado y vencida hace tiempo, pero SIN ninguna fila PunitorioAplicado — es el
            // equivalente a "punitorio calculado pero nunca aplicado": no hay nada que leer.
            var credito = new Credito
            {
                ClienteId = 1,
                Numero = "PUNML10C-VVS-SINAPLICAR",
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 0m
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();
            ctx.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.Date.AddDays(-30),
                MontoCapital = 1_000m,
                MontoInteres = 0m,
                MontoTotal = 1_000m,
                MontoPagado = 1_000m,
                MontoPunitorio = 0m,
                Estado = EstadoCuota.Pagada
            });
            await ctx.SaveChangesAsync();

            var fakeBcra = new FakeSituacionCrediticiaBcraService();
            var service = BuildService(ctx, fakeBcra);

            var result = await service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

            Assert.Equal(EstadoCrediticioCliente.Apto, result.EstadoAptitud);
            Assert.False(result.NoViable);
            Assert.False(result.RequiereAutorizacion);
            Assert.True(result.PuedeProceeder);
        }
    }

    [Fact]
    public async Task Prevalidar_EnEdicion_NoCuentaElCreditoPropioDeLaVentaComoCupoUsado()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(4);
            preset!.LimiteMonto = 100_000m;

            ctx.Clientes.Add(ClienteBuenPagador(1, bcraSituacion: 1));
            await ctx.SaveChangesAsync();

            // El crédito propio de la venta (60.000) ya existe antes de confirmar y cuenta como saldo vigente.
            var credito = new Credito
            {
                ClienteId = 1, Numero = "CRED-EDIT-1", Estado = EstadoCredito.Configurado,
                MontoSolicitado = 60_000m, MontoAprobado = 60_000m, SaldoPendiente = 60_000m,
                CantidadCuotas = 3, FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1), IsDeleted = false
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();
            var venta = new Venta
            {
                ClienteId = 1, CreditoId = credito.Id, Numero = "VTA-EDIT-1", Estado = EstadoVenta.Presupuesto,
                TipoPago = TipoPago.CreditoPersonal, Total = 60_000m, IsDeleted = false
            };
            ctx.Ventas.Add(venta);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, new FakeSituacionCrediticiaBcraService());

            // Sin ventaId (nueva venta): 60.000 pedidos contra 40.000 disponibles -> no viable (comportamiento previo).
            var nueva = await service.PrevalidarAsync(1, 60_000m);
            Assert.Equal(ResultadoPrevalidacion.NoViable, nueva.Resultado);

            // En edición de esa venta: el crédito propio no se descuenta dos veces -> hay cupo.
            var edicion = await service.PrevalidarAsync(1, 60_000m, venta.Id);
            Assert.True(edicion.Resultado != ResultadoPrevalidacion.NoViable, string.Join(" | ", edicion.Motivos.Select(m => m.Titulo + ": " + m.Descripcion)));
            Assert.Equal(100_000m, edicion.CupoDisponible);
            Assert.Equal(0m, edicion.CreditoUtilizado);

            // Excluir el propio no habilita pedir más que el límite: 120.000 > 100.000 sigue siendo no viable.
            var excede = await service.PrevalidarAsync(1, 120_000m, venta.Id);
            Assert.Equal(ResultadoPrevalidacion.NoViable, excede.Resultado);

            // La venta de OTRO cliente no permite excluir créditos ajenos: el cliente 2 (sin deuda propia) no recibe
            // ningún descuento; su utilizado es 0 y el crédito del cliente 1 no se toca.
            ctx.Clientes.Add(ClienteBuenPagador(2, bcraSituacion: 1));
            await ctx.SaveChangesAsync();
            var ajena = await service.PrevalidarAsync(2, 60_000m, venta.Id);
            Assert.Equal(0m, ajena.CreditoUtilizado);
        }
    }

    private static async Task<(Venta venta, Credito credito)> SembrarVentaConCreditoAsync(
        AppDbContext ctx, int clienteId, decimal monto, EstadoVenta estadoVenta, string sufijo)
    {
        var credito = new Credito
        {
            ClienteId = clienteId, Numero = $"CRED-{sufijo}", Estado = EstadoCredito.Configurado,
            MontoSolicitado = monto, MontoAprobado = monto, SaldoPendiente = monto,
            CantidadCuotas = 3, FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1), IsDeleted = false
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();
        var venta = new Venta
        {
            ClienteId = clienteId, CreditoId = credito.Id, Numero = $"VTA-{sufijo}", Estado = estadoVenta,
            TipoPago = TipoPago.CreditoPersonal, Total = monto, IsDeleted = false
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return (venta, credito);
    }

    [Fact]
    public async Task Prevalidar_EnEdicion_CreditoPropioQueConsumeTodoElCupo_NoQuedaNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // ValidarLimiteCredito = true: la aptitud calcula el cupo contando el crédito propio como usado.
            ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
            {
                ValidarDocumentacion = false, ValidarLimiteCredito = true, ValidarMora = false
            });
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(4);
            preset!.LimiteMonto = 100_000m;
            ctx.Clientes.Add(ClienteBuenPagador(1, bcraSituacion: 1));
            await ctx.SaveChangesAsync();
            var (venta, _) = await SembrarVentaConCreditoAsync(ctx, 1, 100_000m, EstadoVenta.Presupuesto, "TODO");

            var service = BuildService(ctx, new FakeSituacionCrediticiaBcraService());

            var nueva = await service.PrevalidarAsync(1, 100_000m);
            Assert.Equal(ResultadoPrevalidacion.NoViable, nueva.Resultado);   // sin exclusión: cupo agotado

            var edicion = await service.PrevalidarAsync(1, 100_000m, venta.Id);
            Assert.True(edicion.Resultado != ResultadoPrevalidacion.NoViable,
                string.Join(" | ", edicion.Motivos.Select(m => m.Titulo + ": " + m.Descripcion)));
            Assert.Equal(0m, edicion.CreditoUtilizado);
            Assert.Equal(100_000m, edicion.CupoDisponible);

            // Pedir más que el límite sigue siendo no viable aun en edición.
            var excede = await service.PrevalidarAsync(1, 100_001m, venta.Id);
            Assert.Equal(ResultadoPrevalidacion.NoViable, excede.Resultado);
        }
    }

    [Fact]
    public async Task Prevalidar_ConVentaYaConfirmada_NoExcluyeSuCredito()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(4);
            preset!.LimiteMonto = 100_000m;
            ctx.Clientes.Add(ClienteBuenPagador(1, bcraSituacion: 1));
            await ctx.SaveChangesAsync();
            // Venta A ya confirmada: su crédito (60.000) es deuda real.
            var (ventaA, _) = await SembrarVentaConCreditoAsync(ctx, 1, 60_000m, EstadoVenta.Confirmada, "CONF");

            var service = BuildService(ctx, new FakeSituacionCrediticiaBcraService());

            // Otra operación de 60.000 enviando ventaId=A no obtiene el cupo de A: sigue habiendo 40.000 disponibles.
            var resultado = await service.PrevalidarAsync(1, 60_000m, ventaA.Id);
            Assert.Equal(ResultadoPrevalidacion.NoViable, resultado.Resultado);
            Assert.Equal(40_000m, resultado.CupoDisponible);
        }
    }
}
