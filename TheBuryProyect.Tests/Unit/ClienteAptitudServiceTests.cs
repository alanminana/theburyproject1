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

    // -----------------------------------------------------------------------
    // E. ValidarLimiteCredito — sin cupo asignado → NoApto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidarCupo_SinLimiteAsignado_EstadoNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            // PuntajesCreditoLimite tienen LimiteMonto = 0 por seed
            // Cliente sin LimiteCredito manual → límite efectivo = 0
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Single(resultado.Detalles);
            Assert.Equal("Cupo", resultado.Detalles[0].Categoria);
            Assert.True(resultado.Detalles[0].EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // F. ValidarLimiteCredito — cupo agotado (saldo cubre límite) → NoApto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidarCupo_CupoAgotado_EstadoNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            // Asignar límite manual al cliente = 5000
            var cliente = BaseCliente(1);
            cliente.LimiteCredito = 5_000m;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Crédito activo que consume todo el límite
            ctx.Creditos.Add(new Credito
            {
                ClienteId = 1,
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 5_000m,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Single(resultado.Detalles);
            Assert.Equal("Cupo", resultado.Detalles[0].Categoria);
            Assert.True(resultado.Detalles[0].EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // G. ValidarLimiteCredito — cupo disponible → Apto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidarCupo_CupoDisponible_EstadoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            var cliente = BaseCliente(1);
            cliente.LimiteCredito = 10_000m;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Sin créditos activos → disponible = 10.000
            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
            Assert.Empty(resultado.Detalles);
        }
    }

    // -----------------------------------------------------------------------
    // H. ValidarVencimientoDocumentos — documento vencido → NoApto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidarDocumentacion_DocumentoVencido_EstadoNoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            config.ValidarVencimientoDocumentos = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Agregar los 3 tipos requeridos (DNI, ReciboSueldo, Servicio) como Verificados
            // pero el DNI con fecha de vencimiento pasada
            ctx.Set<DocumentoCliente>().AddRange(
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.DNI,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-30),
                    FechaVencimiento = DateTime.UtcNow.Date.AddDays(-1) // vencido ayer
                },
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.ReciboSueldo,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-10)
                },
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.Servicio,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-10)
                });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            // El servicio genera un detalle por "faltante" y otro por "vencido"
            Assert.True(resultado.Detalles.Count >= 1);
            Assert.All(resultado.Detalles, d => Assert.Equal("Documentación", d.Categoria));
            Assert.All(resultado.Detalles, d => Assert.True(d.EsBloqueo));
        }
    }

    // -----------------------------------------------------------------------
    // I. Documentación completa y vigente → Apto (cuando ValidarVencimiento activo)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidarDocumentacion_DocumentosVigentes_EstadoApto()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            config.ValidarVencimientoDocumentos = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.Set<DocumentoCliente>().AddRange(
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.DNI,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-5),
                    FechaVencimiento = DateTime.UtcNow.Date.AddDays(30) // vigente
                },
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.ReciboSueldo,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-5)
                },
                new DocumentoCliente
                {
                    ClienteId = 1,
                    TipoDocumento = TipoDocumentoCliente.Servicio,
                    Estado = EstadoDocumento.Verificado,
                    FechaVerificacion = DateTime.UtcNow.AddDays(-5)
                });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            Assert.Equal(EstadoCrediticioCliente.Apto, resultado.Estado);
            Assert.Empty(resultado.Detalles);
        }
    }

    // -----------------------------------------------------------------------
    // J. Mora (RequiereAutorizacion) + documentación faltante → NoApto gana
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MoraAutorizacion_MasDocumentacionFaltante_NoAptoPrioridad()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            config.ValidarMora = true;
            config.DiasParaRequerirAutorizacion = 1;
            config.DiasParaNoApto = null;
            ctx.Set<ConfiguracionCredito>().Add(config);

            // Preset permite crédito (para que la mora se evalúe)
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(5);
            preset!.LimiteMonto = 100_000m;

            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Cuota vencida → RequiereAutorizacion por mora
            var credito = new Credito
            {
                ClienteId = 1,
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                SaldoPendiente = 5_000m,
                RowVersion = new byte[8]
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            ctx.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                FechaVencimiento = DateTime.UtcNow.Date.AddDays(-5),
                MontoCapital = 400m,
                MontoInteres = 100m,
                MontoTotal = 500m,
                MontoPagado = 0m,
                MontoPunitorio = 0m,
                Estado = EstadoCuota.Pendiente
            });
            await ctx.SaveChangesAsync();

            // Sin documentos → documentación incompleta (bloqueo)
            var service = BuildService(ctx);
            var resultado = await service.EvaluarAptitudSinGuardarAsync(1);

            // NoApto debe ganar sobre RequiereAutorizacion
            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
            Assert.Equal(2, resultado.Detalles.Count);
            Assert.Contains(resultado.Detalles, d => d.Categoria == "Documentación" && d.EsBloqueo);
            Assert.Contains(resultado.Detalles, d => d.Categoria == "Mora" && !d.EsBloqueo);
        }
    }

    // -----------------------------------------------------------------------
    // K. VerificarAptitudParaMontoAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VerificarAptitudParaMonto_ClienteNoApto_RetornaFalse()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Config: validar mora, sin umbral forzado = bloqueará si hay mora severa
            var config = ConfigSinValidaciones();
            config.ValidarMora = true;
            config.DiasParaNoApto = 1; // 1 día de mora bloquea
            ctx.Set<ConfiguracionCredito>().Add(config);

            var cliente = BaseCliente(1);
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            // Crédito con cuota vencida hace 5 días
            var credito = new Credito
            {
                ClienteId = 1,
                Numero = "TEST-001",
                Estado = EstadoCredito.Activo,
                MontoSolicitado = 5_000m,
                MontoAprobado = 5_000m,
                SaldoPendiente = 5_000m,
                TasaInteres = 3m,
                CantidadCuotas = 6
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            ctx.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                MontoTotal = 1_000m,
                MontoCapital = 800m,
                MontoInteres = 200m,
                Estado = EstadoCuota.Vencida,
                FechaVencimiento = DateTime.UtcNow.AddDays(-5),
                MontoPagado = 0m
            });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var (esApto, motivo) = await service.VerificarAptitudParaMontoAsync(1, monto: 10_000m);

            Assert.False(esApto);
            Assert.False(string.IsNullOrWhiteSpace(motivo));
        }
    }

    [Fact]
    public async Task VerificarAptitudParaMonto_CupoInsuficiente_RetornaFalse()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Habilitar validación de límite para que CupoDisponible sea calculado
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            var cliente = BaseCliente(1);
            cliente.LimiteCredito = 5_000m; // límite bajo
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var (esApto, motivo) = await service.VerificarAptitudParaMontoAsync(1, monto: 10_000m);

            Assert.False(esApto);
            Assert.Contains("Cupo insuficiente", motivo);
        }
    }

    [Fact]
    public async Task VerificarAptitudParaMonto_CupoSuficiente_RetornaTrue()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Habilitar validación de límite para que CupoDisponible sea calculado
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            var cliente = BaseCliente(1);
            cliente.LimiteCredito = 50_000m; // límite amplio
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var (esApto, motivo) = await service.VerificarAptitudParaMontoAsync(1, monto: 10_000m);

            Assert.True(esApto);
            Assert.Null(motivo);
        }
    }

    [Fact]
    public async Task VerificarAptitudParaMonto_MontoExactoAlCupo_Aprobado()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarLimiteCredito = true;
            ctx.Set<ConfiguracionCredito>().Add(config);

            var cliente = BaseCliente(1);
            cliente.LimiteCredito = 10_000m;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            // monto == cupo disponible → debe aprobar (condición: disponible < monto, no <=)
            var (esApto, _) = await service.VerificarAptitudParaMontoAsync(1, monto: 10_000m);

            Assert.True(esApto);
        }
    }

    // -----------------------------------------------------------------------
    // L. GetCreditoUtilizadoAsync — filtra 6 estados activos
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCreditoUtilizado_SinCreditos_RetornaCero()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var utilizado = await service.GetCreditoUtilizadoAsync(1);

            Assert.Equal(0m, utilizado);
        }
    }

    [Fact]
    public async Task GetCreditoUtilizado_CreditosActivos_SumaCorrectamente()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.Creditos.AddRange(
                new Credito { ClienteId = 1, Numero = "C01", Estado = EstadoCredito.Activo, MontoSolicitado = 5_000m, SaldoPendiente = 4_000m, TasaInteres = 3m, CantidadCuotas = 6 },
                new Credito { ClienteId = 1, Numero = "C02", Estado = EstadoCredito.Aprobado, MontoSolicitado = 3_000m, SaldoPendiente = 3_000m, TasaInteres = 3m, CantidadCuotas = 3 }
            );
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var utilizado = await service.GetCreditoUtilizadoAsync(1);

            Assert.Equal(7_000m, utilizado);
        }
    }

    [Fact]
    public async Task GetCreditoUtilizado_CreditosFinalizados_ExcluirDeLaSuma()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.Creditos.AddRange(
                new Credito { ClienteId = 1, Numero = "C01", Estado = EstadoCredito.Activo, MontoSolicitado = 5_000m, SaldoPendiente = 4_000m, TasaInteres = 3m, CantidadCuotas = 6 },
                new Credito { ClienteId = 1, Numero = "C02", Estado = EstadoCredito.Finalizado, MontoSolicitado = 3_000m, SaldoPendiente = 0m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C03", Estado = EstadoCredito.Cancelado, MontoSolicitado = 2_000m, SaldoPendiente = 1_500m, TasaInteres = 3m, CantidadCuotas = 3 }
            );
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var utilizado = await service.GetCreditoUtilizadoAsync(1);

            // Solo el Activo debe sumar — Finalizado y Cancelado excluidos
            Assert.Equal(4_000m, utilizado);
        }
    }

    [Fact]
    public async Task GetCreditoUtilizado_TodosLosEstadosActivos_SeIncluyen()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Los 6 estados que deben incluirse
            ctx.Creditos.AddRange(
                new Credito { ClienteId = 1, Numero = "C01", Estado = EstadoCredito.Activo, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C02", Estado = EstadoCredito.Aprobado, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C03", Estado = EstadoCredito.Solicitado, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C04", Estado = EstadoCredito.PendienteConfiguracion, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C05", Estado = EstadoCredito.Configurado, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 },
                new Credito { ClienteId = 1, Numero = "C06", Estado = EstadoCredito.Generado, MontoSolicitado = 1_000m, SaldoPendiente = 1_000m, TasaInteres = 3m, CantidadCuotas = 3 }
            );
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var utilizado = await service.GetCreditoUtilizadoAsync(1);

            Assert.Equal(6_000m, utilizado);
        }
    }

    // -----------------------------------------------------------------------
    // M. AsignarLimiteCreditoAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsignarLimite_ClienteInexistente_RetornaFalse()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var service = BuildService(ctx);
            var resultado = await service.AsignarLimiteCreditoAsync(clienteId: 99_999, limite: 50_000m);

            Assert.False(resultado);
        }
    }

    [Fact]
    public async Task AsignarLimite_ClienteExistente_PersisteLimite()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.AsignarLimiteCreditoAsync(1, limite: 75_000m, motivo: "Actualización anual");

            Assert.True(resultado);
            ctx.ChangeTracker.Clear();
            var cliente = await ctx.Clientes.FindAsync(1);
            Assert.Equal(75_000m, cliente!.LimiteCredito);
        }
    }

    // -----------------------------------------------------------------------
    // N. GetUltimaEvaluacionAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetUltimaEvaluacion_ClienteInexistente_RetornaNull()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var service = BuildService(ctx);
            var resultado = await service.GetUltimaEvaluacionAsync(clienteId: 99_999);
            Assert.Null(resultado);
        }
    }

    [Fact]
    public async Task GetUltimaEvaluacion_SinFechaEvaluacion_RetornaNull()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var cliente = BaseCliente(1);
            cliente.FechaUltimaEvaluacion = null;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.GetUltimaEvaluacionAsync(1);
            Assert.Null(resultado);
        }
    }

    [Fact]
    public async Task GetUltimaEvaluacion_ConEvaluacionPersistida_RetornaViewModel()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var fecha = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
            var cliente = BaseCliente(1);
            cliente.EstadoCrediticio = EstadoCrediticioCliente.NoApto;
            cliente.MotivoNoApto = "Sin documentación";
            cliente.FechaUltimaEvaluacion = fecha;
            ctx.Clientes.Add(cliente);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var resultado = await service.GetUltimaEvaluacionAsync(1);

            Assert.NotNull(resultado);
            Assert.Equal(EstadoCrediticioCliente.NoApto, resultado!.Estado);
            Assert.Equal("Sin documentación", resultado.Motivo);
            Assert.Equal(fecha, resultado.FechaEvaluacion);
        }
    }

    // -----------------------------------------------------------------------
    // O. UpdateConfiguracionAsync — persiste los 14 campos
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateConfiguracion_PersisteCamposDocumentacion()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateConfiguracionAsync(new ConfiguracionCreditoViewModel
            {
                ValidarDocumentacion = true,
                ValidarVencimientoDocumentos = true,
                DiasGraciaVencimientoDocumento = 7,
                TiposDocumentoRequeridos = [TipoDocumentoCliente.DNI, TipoDocumentoCliente.ReciboSueldo]
            });

            ctx.ChangeTracker.Clear();
            var config = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.True(config.ValidarDocumentacion);
            Assert.True(config.ValidarVencimientoDocumentos);
            Assert.Equal(7, config.DiasGraciaVencimientoDocumento);
            Assert.NotNull(config.TiposDocumentoRequeridos); // serializado como JSON
        }
    }

    [Fact]
    public async Task UpdateConfiguracion_PersisteCamposMora()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateConfiguracionAsync(new ConfiguracionCreditoViewModel
            {
                ValidarMora = true,
                DiasParaRequerirAutorizacion = 3,
                DiasParaNoApto = 10,
                MontoMoraParaRequerirAutorizacion = 500m,
                MontoMoraParaNoApto = 2_000m,
                CuotasVencidasParaNoApto = 2
            });

            ctx.ChangeTracker.Clear();
            var config = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.True(config.ValidarMora);
            Assert.Equal(3, config.DiasParaRequerirAutorizacion);
            Assert.Equal(10, config.DiasParaNoApto);
            Assert.Equal(500m, config.MontoMoraParaRequerirAutorizacion);
            Assert.Equal(2_000m, config.MontoMoraParaNoApto);
            Assert.Equal(2, config.CuotasVencidasParaNoApto);
        }
    }

    [Fact]
    public async Task UpdateConfiguracion_PersisteCamposGenerales()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateConfiguracionAsync(new ConfiguracionCreditoViewModel
            {
                RecalculoAutomatico = false,
                DiasValidezEvaluacion = 60,
                AuditoriaActiva = false
            });

            ctx.ChangeTracker.Clear();
            var config = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.False(config.RecalculoAutomatico);
            Assert.Equal(60, config.DiasValidezEvaluacion);
            Assert.False(config.AuditoriaActiva);
        }
    }

    [Fact]
    public async Task UpdateConfiguracion_TiposDocumentoVacio_GuardaNull()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateConfiguracionAsync(new ConfiguracionCreditoViewModel
            {
                TiposDocumentoRequeridos = [] // vacío → null en DB
            });

            ctx.ChangeTracker.Clear();
            var config = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.Null(config.TiposDocumentoRequeridos);
        }
    }

    // -----------------------------------------------------------------------
    // P. VerificarConfiguracionAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VerificarConfiguracion_TodasDeshabilitadas_DevuelveAdvertencia()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones()); // todas false
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var (estaConfigurando, mensaje) = await service.VerificarConfiguracionAsync();

            Assert.True(estaConfigurando);
            Assert.False(string.IsNullOrWhiteSpace(mensaje));
        }
    }

    [Fact]
    public async Task VerificarConfiguracion_AlMenosUnaActiva_SinMensaje()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarMora = true; // al menos una activa
            ctx.Set<ConfiguracionCredito>().Add(config);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            var (estaConfigurando, mensaje) = await service.VerificarConfiguracionAsync();

            Assert.True(estaConfigurando);
            Assert.Null(mensaje);
        }
    }

    // =======================================================================
    // G. Métodos públicos no cubiertos: GetConfiguracionAsync,
    //    EvaluarAptitudAsync, EvaluarDocumentacionAsync,
    //    EvaluarCupoAsync, EvaluarMoraAsync, GetCupoDisponibleAsync
    // =======================================================================

    public class GetConfiguracionAsyncTests
    {
        [Fact]
        public async Task SinConfig_CreaDefaultYRetorna()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var service = BuildService(ctx);

                var config = await service.GetConfiguracionAsync();

                Assert.NotNull(config);
                Assert.True(config.Id > 0);
            }
        }

        [Fact]
        public async Task ConConfig_RetornaExistente()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var cfg = ConfigSinValidaciones();
                cfg.ValidarDocumentacion = true;
                ctx.Set<ConfiguracionCredito>().Add(cfg);
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var resultado = await service.GetConfiguracionAsync();

                Assert.Equal(cfg.Id, resultado.Id);
                Assert.True(resultado.ValidarDocumentacion);
            }
        }
    }

    public class EvaluarAptitudAsyncTests
    {
        [Fact]
        public async Task ConGuardar_ActualizaFechaEvaluacionEnCliente()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones();
                ctx.Set<ConfiguracionCredito>().Add(config);
                ctx.Clientes.Add(BaseCliente(10));
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                await service.EvaluarAptitudAsync(10, guardarResultado: true);

                ctx.ChangeTracker.Clear();
                var cliente = await ctx.Clientes.FindAsync(10);
                Assert.NotNull(cliente!.FechaUltimaEvaluacion);
            }
        }

        [Fact]
        public async Task SinGuardar_FechaEvaluacionNoPersiste()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones();
                ctx.Set<ConfiguracionCredito>().Add(config);
                ctx.Clientes.Add(BaseCliente(11));
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                await service.EvaluarAptitudAsync(11, guardarResultado: false);

                ctx.ChangeTracker.Clear();
                var cliente = await ctx.Clientes.FindAsync(11);
                Assert.Null(cliente!.FechaUltimaEvaluacion);
            }
        }
    }

    public class EvaluarDocumentacionAsyncTests
    {
        [Fact]
        public async Task ValidacionDeshabilitada_ResultadoCompleto()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones(); // ValidarDocumentacion = false
                ctx.Set<ConfiguracionCredito>().Add(config);
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var resultado = await service.EvaluarDocumentacionAsync(1);

                Assert.True(resultado.Completa);
                Assert.False(resultado.Evaluada);
            }
        }

        [Fact]
        public async Task ValidacionHabilitada_ClienteSinDocumentos_TieneFaltantes()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones();
                config.ValidarDocumentacion = true;
                ctx.Set<ConfiguracionCredito>().Add(config);
                ctx.Clientes.Add(BaseCliente(20));
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var resultado = await service.EvaluarDocumentacionAsync(20);

                Assert.True(resultado.Evaluada);
                Assert.False(resultado.Completa);
                Assert.True(resultado.DocumentosFaltantes.Count > 0);
            }
        }
    }

    public class EvaluarCupoAsyncTests
    {
        [Fact]
        public async Task ValidacionDeshabilitada_CupoSuficiente()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones(); // ValidarLimiteCredito = false
                ctx.Set<ConfiguracionCredito>().Add(config);
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var resultado = await service.EvaluarCupoAsync(1);

                Assert.True(resultado.CupoSuficiente);
                Assert.False(resultado.Evaluado);
            }
        }
    }

    public class EvaluarMoraAsyncTests
    {
        [Fact]
        public async Task ValidacionDeshabilitada_SinMora()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var config = ConfigSinValidaciones(); // ValidarMora = false
                ctx.Set<ConfiguracionCredito>().Add(config);
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var resultado = await service.EvaluarMoraAsync(1);

                Assert.False(resultado.Evaluada);
                Assert.False(resultado.TieneMora);
            }
        }
    }

    public class GetCupoDisponibleAsyncTests
    {
        [Fact]
        public async Task ClienteSinLimiteAsignado_RetornaCero()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                ctx.Clientes.Add(BaseCliente(30));
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var disponible = await service.GetCupoDisponibleAsync(30);

                Assert.Equal(0m, disponible);
            }
        }

        [Fact]
        public async Task ClienteConLimite_RetornaDisponible()
        {
            var (ctx, conn) = CreateContext();
            await using (ctx) using (conn)
            {
                var cliente = BaseCliente(31);
                cliente.LimiteCredito = 10_000m;
                ctx.Clientes.Add(cliente);
                await ctx.SaveChangesAsync();

                var service = BuildService(ctx);
                var disponible = await service.GetCupoDisponibleAsync(31);

                Assert.True(disponible >= 0m);
            }
        }
    }

    // -----------------------------------------------------------------------
    // M. GetScoringThresholdsAsync / UpdateScoringThresholdsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetScoringThresholds_SinConfigEnDB_RetornaDefaults()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var service = BuildService(ctx);
            var result = await service.GetScoringThresholdsAsync();

            Assert.Equal(3.0m,      result.PuntajeRiesgoMinimo);
            Assert.Equal(5.0m,      result.PuntajeRiesgoMedio);
            Assert.Equal(7.0m,      result.PuntajeRiesgoExcelente);
            Assert.Equal(0.25m,     result.UmbralCuotaIngresoBajo);
            Assert.Equal(0.35m,     result.RelacionCuotaIngresoMax);
            Assert.Equal(0.45m,     result.UmbralCuotaIngresoAlto);
            Assert.Equal(500_000m,  result.MontoRequiereGarante);
            Assert.Equal(70m,       result.PuntajeMinimoParaAprobacion);
            Assert.Equal(50m,       result.PuntajeMinimoParaAnalisis);
        }
    }

    [Fact]
    public async Task UpdateScoringThresholds_PersisteLosValoresYGetLosDevuelve()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);

            var nuevos = new ScoringThresholdsViewModel
            {
                PuntajeRiesgoMinimo         = 2.0m,
                PuntajeRiesgoMedio          = 4.5m,
                PuntajeRiesgoExcelente      = 8.0m,
                UmbralCuotaIngresoBajo      = 0.20m,
                RelacionCuotaIngresoMax     = 0.40m,
                UmbralCuotaIngresoAlto      = 0.50m,
                MontoRequiereGarante        = 750_000m,
                PuntajeMinimoParaAprobacion = 80m,
                PuntajeMinimoParaAnalisis   = 60m
            };
            await service.UpdateScoringThresholdsAsync(nuevos);

            var recuperado = await service.GetScoringThresholdsAsync();

            Assert.Equal(2.0m,      recuperado.PuntajeRiesgoMinimo);
            Assert.Equal(4.5m,      recuperado.PuntajeRiesgoMedio);
            Assert.Equal(8.0m,      recuperado.PuntajeRiesgoExcelente);
            Assert.Equal(0.20m,     recuperado.UmbralCuotaIngresoBajo);
            Assert.Equal(0.40m,     recuperado.RelacionCuotaIngresoMax);
            Assert.Equal(0.50m,     recuperado.UmbralCuotaIngresoAlto);
            Assert.Equal(750_000m,  recuperado.MontoRequiereGarante);
            Assert.Equal(80m,       recuperado.PuntajeMinimoParaAprobacion);
            Assert.Equal(60m,       recuperado.PuntajeMinimoParaAnalisis);
        }
    }

    [Fact]
    public async Task UpdateScoringThresholds_NoAfectaCamposAptitud()
    {
        // Guardar umbrales scoring NO debe sobreescribir ValidarDocumentacion ni ValidarMora
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.ValidarDocumentacion = true;
            config.ValidarMora = true;
            ctx.Set<ConfiguracionCredito>().Add(config);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateScoringThresholdsAsync(new ScoringThresholdsViewModel
            {
                PuntajeRiesgoMinimo         = 4.0m,
                PuntajeRiesgoMedio          = 5.5m,
                PuntajeRiesgoExcelente      = 7.5m,
                UmbralCuotaIngresoBajo      = 0.22m,
                RelacionCuotaIngresoMax     = 0.30m,
                UmbralCuotaIngresoAlto      = 0.42m,
                MontoRequiereGarante        = 300_000m,
                PuntajeMinimoParaAprobacion = 75m,
                PuntajeMinimoParaAnalisis   = 55m
            });

            var configActualizada = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.True(configActualizada.ValidarDocumentacion);
            Assert.True(configActualizada.ValidarMora);
        }
    }
    [Fact]
    public async Task GetSemaforoFinanciero_SinConfigEnDB_RetornaDefaults()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var service = BuildService(ctx);
            var result = await service.GetSemaforoFinancieroAsync();

            Assert.Equal(0.08m, result.RatioVerdeMax);
            Assert.Equal(0.15m, result.RatioAmarilloMax);
        }
    }

    [Fact]
    public async Task UpdateSemaforoFinanciero_PersisteLosValoresYGetLosDevuelve()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            ctx.Set<ConfiguracionCredito>().Add(ConfigSinValidaciones());
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateSemaforoFinancieroAsync(new SemaforoFinancieroViewModel
            {
                RatioVerdeMax = 0.10m,
                RatioAmarilloMax = 0.18m
            });

            var recuperado = await service.GetSemaforoFinancieroAsync();

            Assert.Equal(0.10m, recuperado.RatioVerdeMax);
            Assert.Equal(0.18m, recuperado.RatioAmarilloMax);
        }
    }

    [Fact]
    public async Task UpdateSemaforoFinanciero_NoAfectaScoringThresholds()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            var config = ConfigSinValidaciones();
            config.PuntajeRiesgoMedio = 6.0m;
            config.PuntajeRiesgoExcelente = 8.0m;
            config.UmbralCuotaIngresoBajo = 0.22m;
            config.RelacionCuotaIngresoMax = 0.33m;
            config.UmbralCuotaIngresoAlto = 0.44m;
            ctx.Set<ConfiguracionCredito>().Add(config);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx);
            await service.UpdateSemaforoFinancieroAsync(new SemaforoFinancieroViewModel
            {
                RatioVerdeMax = 0.09m,
                RatioAmarilloMax = 0.16m
            });

            var configActualizada = await ctx.Set<ConfiguracionCredito>().FirstAsync();
            Assert.Equal(6.0m, configActualizada.PuntajeRiesgoMedio);
            Assert.Equal(8.0m, configActualizada.PuntajeRiesgoExcelente);
            Assert.Equal(0.22m, configActualizada.UmbralCuotaIngresoBajo);
            Assert.Equal(0.33m, configActualizada.RelacionCuotaIngresoMax);
            Assert.Equal(0.44m, configActualizada.UmbralCuotaIngresoAlto);
        }
    }
}
